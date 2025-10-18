using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        // === 新增：主題列舉 ===
        private enum Theme { Light, Dark }

        private readonly string _cfgPath;
        private readonly AppConfig _cfg;
        private readonly DbService _db;
        private readonly RoutingService _routing;
        private readonly LlmService _llm;
        private readonly IntakeService _intake;
        private readonly HotFolderService _hot;

        private readonly List<Item> _items = new();
        private CancellationTokenSource _cts = new();

        private string _lockedProject = string.Empty;

        // 右欄收闔狀態
        private bool _rightCollapsed = false;

        // 防雙擊開啟去抖
        private DateTime _lastOpen = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();

            _cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _cfg = ConfigService.TryLoad(_cfgPath);

            _db = new DbService(_cfg.App.DbPath);
            _routing = new RoutingService();
            _llm = new LlmService(_cfg);
            _intake = new IntakeService(_db, _routing, _llm, _cfg);
            _hot = new HotFolderService(_intake, _cfg);

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // === 主題套用（避免 dynamic）===
            var themeStr = _cfg?.Views?.Theme?.Trim().ToLowerInvariant();
            ApplyTheme(themeStr == "dark" ? Theme.Dark : Theme.Light);

            _lockedProject = _cfg.App.ProjectLock ?? string.Empty;
            if (FindName("TxtLockedProject") is TextBlock lockTxt)
            {
                lockTxt.Text = string.IsNullOrWhiteSpace(_lockedProject)
                    ? "目前未鎖定專案"
                    : $"目前鎖定：{_lockedProject}";
            }

            RefreshProjectCombo();

            try { _hot.Start(); } catch { }
            RefreshList("auto-sorted");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try { _cts.Cancel(); } catch { }
            try { _hot.Dispose(); } catch { }
            try { _db.Dispose(); } catch { }
            try { _llm.Dispose(); } catch { }
        }

        // ===================== 主題 =====================

        private void ApplyTheme(Theme theme)
        {
            // 簡易主題（無 ResourceDictionary 版本）
            var isDark = theme == Theme.Dark;

            Background = isDark ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
            Foreground = isDark ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;

            if (FindName("FileList") is ListView lv)
            {
                lv.Background = isDark ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
                lv.Foreground = isDark ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;
            }

            if (FindName("LogBox") is TextBox tb)
            {
                tb.Background = isDark ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
                tb.Foreground = isDark ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;
            }
        }

        // ===================== 常用工具 =====================

        private IEnumerable<Item> GetSelection()
        {
            if (FindName("FileList") is ListView lv && lv.SelectedItems != null)
                return lv.SelectedItems.Cast<Item>().ToList();
            return Enumerable.Empty<Item>();
        }

        private void Log(string message)
        {
            try
            {
                if (FindName("LogBox") is TextBox tb)
                {
                    tb.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
                    tb.ScrollToEnd();
                }
            }
            catch { }
        }

        private string CurrentTabTag()
        {
            if (FindName("MainTabs") is TabControl tabs && tabs.SelectedItem is TabItem ti)
                return (ti.Tag as string) ?? "auto-sorted";
            return "auto-sorted";
        }

        private void RefreshProjectCombo()
        {
            try
            {
                var all = _db.QueryDistinctProjects().ToList();

                if (FindName("CbLockProject") is ComboBox cb)
                {
                    cb.ItemsSource = all;
                    if (!string.IsNullOrWhiteSpace(_lockedProject))
                        cb.Text = _lockedProject;
                }
            }
            catch { }
        }

        // ===================== 列表刷新 =====================

        private void RefreshList(string statusFilter)
        {
            _items.Clear();

            IEnumerable<Item> src;
            switch ((statusFilter ?? "").ToLowerInvariant())
            {
                case "recent":
                    var since = DateTimeOffset.Now.AddDays(-3).ToUnixTimeSeconds();
                    src = _db.QuerySince(since).OrderByDescending(i => i.CreatedTs);
                    break;
                case "favorite":
                    src = _db.QueryByStatus("favorite").OrderByDescending(i => i.CreatedTs);
                    break;
                case "in-progress":
                    src = _db.QueryByStatus("in-progress").OrderByDescending(i => i.CreatedTs);
                    break;
                case "pending":
                    src = _db.QueryByStatus("pending").OrderByDescending(i => i.CreatedTs);
                    break;
                case "inbox":
                    src = _db.QueryByStatus("inbox").OrderByDescending(i => i.CreatedTs);
                    break;
                default:
                    src = _db.QueryByStatus("auto-sorted").OrderByDescending(i => i.CreatedTs);
                    break;
            }

            foreach (var it in src) _items.Add(it);

            if (FindName("FileList") is ListView lv)
            {
                lv.ItemsSource = null;      // 保持乾淨
                lv.ItemsSource = _items;
            }

            Log($"清單已更新（{statusFilter}）");
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is TabControl tabs && tabs.SelectedItem is TabItem ti)
                RefreshList((ti.Tag as string) ?? "auto-sorted");
        }

        // ===================== 匯入 / 分類 =====================

        private async void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var inbox = _db.QueryByStatus("inbox").ToList();
            int done = 0;

            foreach (var it in inbox)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_lockedProject))
                        it.Project = _lockedProject; // ProjectLock 優先

                    await _intake.ClassifyOnlyAsync(it.Path!, _cts.Token); // 預估，不搬檔
                    done++;
                }
                catch { }
            }

            RefreshList("pending");
            Log($"預分類完成：{done} 筆");
        }

        private async void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var moved = await _intake.CommitPendingAsync(_cts.Token);
                Log($"搬檔完成：{moved} 筆");
                RefreshList("auto-sorted");
            }
            catch (Exception ex)
            {
                Log($"搬檔失敗：{ex.Message}");
            }
        }

        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "選擇檔案加入 Inbox",
                    Filter = "所有檔案 (*.*)|*.*",
                    Multiselect = true
                };
                if (dlg.ShowDialog() == true)
                {
                    foreach (var f in dlg.FileNames)
                        await _intake.StageOnlyAsync(f, CancellationToken.None);

                    RefreshList("inbox");
                    Log($"加入 {dlg.FileNames.Length} 筆到 Inbox");
                }
            }
            catch (Exception ex)
            {
                Log($"加入失敗：{ex.Message}");
            }
        }

        // ===================== 重新整理 / 收件夾 =====================

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh_Executed(this, null!);

        private void Refresh_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            RefreshList(CurrentTabTag());
        }

        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e) => OpenInbox();
        private void OpenInbox_Executed(object sender, ExecutedRoutedEventArgs e) => OpenInbox();

        private void OpenInbox()
        {
            try
            {
                var root = string.IsNullOrWhiteSpace(_cfg.App.RootDir)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    : _cfg.App.RootDir;
                var inbox = string.IsNullOrWhiteSpace(_cfg.Import.HotFolderPath)
                    ? Path.Combine(root, "_Inbox")
                    : _cfg.Import.HotFolderPath;

                if (!Directory.Exists(inbox)) Directory.CreateDirectory(inbox);
                Process.Start(new ProcessStartInfo("explorer.exe", inbox) { UseShellExecute = true });
                Log($"已開啟收件夾：{inbox}");
            }
            catch (Exception ex)
            {
                Log($"開啟收件夾失敗：{ex.Message}");
            }
        }

        // ===================== 右鍵 / 開啟檔案 =====================

        private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (FindName("FileList") is ListView lv && lv.ContextMenu != null)
                lv.ContextMenu.IsEnabled = GetSelection().Any();
        }

        private void CtxOpenFile_Click(object sender, RoutedEventArgs e) => OpenSelectedFile();

        private void FileList_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedFile();

        private void FileList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OpenSelectedFile();
                e.Handled = true;
            }
        }

        private void OpenSelectedFile_Executed(object sender, ExecutedRoutedEventArgs e) => OpenSelectedFile();

        private void OpenSelectedFile()
        {
            try
            {
                // 去抖 500ms
                if ((DateTime.Now - _lastOpen).TotalMilliseconds < 500) return;
                _lastOpen = DateTime.Now;

                if (FindName("FileList") is not ListView lv) return;
                var it = lv.SelectedItem as Item;
                if (it == null || string.IsNullOrWhiteSpace(it.Path)) return;

                if (File.Exists(it.Path))
                {
                    Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true });
                    Log($"開啟檔案：{Path.GetFileName(it.Path)}");
                }
                else
                {
                    var dir = Path.GetDirectoryName(it.Path);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                        Log($"找不到檔案，已打開所在資料夾：{dir}");
                    }
                    else
                    {
                        Log("找不到檔案與所在資料夾，可能已移動或刪除。");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"開啟失敗：{ex.Message}");
            }
        }

        private void CtxOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in GetSelection())
            {
                var dir = Path.GetDirectoryName(it.Path);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
        }

        private void CtxCopyPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FindName("FileList") is not ListView lv) return;
                var it = lv.SelectedItem as Item;
                if (it != null && !string.IsNullOrWhiteSpace(it.Path))
                {
                    Clipboard.SetText(it.Path);
                    Log($"已複製路徑：{it.Path}");
                }
            }
            catch { }
        }

        // ===================== 右欄收合 =====================

        private void BtnToggleRightPane_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("RightPaneColumn") is ColumnDefinition col)
            {
                if (_rightCollapsed)
                {
                    col.Width = new GridLength(300);
                    _rightCollapsed = false;
                }
                else
                {
                    col.Width = new GridLength(0);
                    _rightCollapsed = true;
                }
            }
        }

        // ===================== 專案鎖定（即時寫檔） =====================

        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            var desired = (FindName("CbLockProject") as ComboBox)?.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(_lockedProject))
            {
                if (!string.IsNullOrWhiteSpace(desired))
                {
                    _lockedProject = desired;
                    _cfg.App.ProjectLock = _lockedProject;

                    if (FindName("TxtLockedProject") is TextBlock t)
                        t.Text = $"目前鎖定：{_lockedProject}";

                    try { ConfigService.Save(_cfgPath, _cfg); } catch { }
                    Log($"🔒 已鎖定專案「{_lockedProject}」");
                }
            }
            else
            {
                var ans = MessageBox.Show(
                    $"是否要解除目前鎖定的專案「{_lockedProject}」？",
                    "解除鎖定",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (ans == MessageBoxResult.Yes)
                {
                    Log($"🔓 已解除專案鎖定「{_lockedProject}」");
                    _lockedProject = string.Empty;
                    _cfg.App.ProjectLock = string.Empty;

                    if (FindName("TxtLockedProject") is TextBlock t)
                        t.Text = "目前未鎖定專案";

                    try { ConfigService.Save(_cfgPath, _cfg); } catch { }
                }
            }
        }

        private void BtnSearchProject_Click(object sender, RoutedEventArgs e)
        {
            var keyword = (FindName("TbProjectSearch") as TextBox)?.Text?.Trim();
            var list = string.IsNullOrWhiteSpace(keyword)
                ? _db.QueryDistinctProjects().ToList()
                : _db.QueryDistinctProjects(keyword).ToList();

            if (FindName("ProjectList") is ListBox lb)
                lb.ItemsSource = list;

            if (FindName("CbLockProject") is ComboBox cb)
                cb.ItemsSource = list;
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new SettingsWindow { Owner = this };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                Log($"開啟設定失敗：{ex.Message}");
            }
        }

        // ===================== 右鍵：設定專案 / 標籤 =====================

        private void CtxSetProject_Click(object sender, RoutedEventArgs e)
        {
            var sel = GetSelection().ToList();
            if (sel.Count == 0) return;

            var box = new SetTextDialog("指定專案", "請輸入專案名稱：");
            if (box.ShowDialog() == true)
            {
                var name = box.Value?.Trim();
                if (string.IsNullOrWhiteSpace(name)) return;

                foreach (var it in sel)
                {
                    it.Project = name!;
                    _db.UpdateProject(it.Id, name!);
                }

                Log($"已指定專案：{name}（{sel.Count} 筆）");
                RefreshList(CurrentTabTag());
                RefreshProjectCombo();
            }
        }

        private async void CtxSuggestProject_Click(object sender, RoutedEventArgs e)
        {
            var files = GetSelection().Select(x => x.Filename ?? "").ToArray();
            if (files.Length == 0) return;

            try
            {
                var suggestions = await _llm.SuggestProjectNamesAsync(files, CancellationToken.None);
                var chooser = new ChooseOneDialog("AI 建議專案", "請選擇專案：", suggestions);
                if (chooser.ShowDialog() == true)
                {
                    var project = chooser.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(project))
                    {
                        foreach (var it in GetSelection())
                        {
                            it.Project = project!;
                            _db.UpdateProject(it.Id, project!);
                        }
                        Log($"已套用 AI 建議專案：{project}");
                        RefreshList(CurrentTabTag());
                        RefreshProjectCombo();
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"AI 建議失敗：{ex.Message}");
            }
        }

        private void CtxSetTags_Click(object sender, RoutedEventArgs e)
        {
            var sel = GetSelection().ToList();
            if (sel.Count == 0) return;

            var box = new SetTextDialog("設定標籤", "輸入標籤（以逗號分隔）：");
            if (box.ShowDialog() == true)
            {
                var tags = box.Value?.Trim() ?? "";
                foreach (var it in sel)
                {
                    it.Tags = tags;
                    _db.UpdateTags(it.Id, tags);
                }
                Log($"已更新標籤（{sel.Count} 筆）");
                RefreshList(CurrentTabTag());
            }
        }

        // ===================== 視窗拖放 =====================

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var f in files)
                    await _intake.StageOnlyAsync(f, CancellationToken.None);

                Log($"拖放加入 {files.Length} 筆至 Inbox");
                RefreshList("inbox");
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    // ===================== 內嵌對話盒 =====================

    internal sealed class SetTextDialog : Window
    {
        private readonly TextBox _tb;
        public string? Value => _tb.Text;

        public SetTextDialog(string title, string prompt)
        {
            Title = title;
            Width = 420;
            Height = 160;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var p = new StackPanel { Margin = new Thickness(12) };
            p.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
            _tb = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
            p.Children.Add(_tb);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "確定", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "取消", Width = 80 };
            ok.Click += (_, __) => { DialogResult = true; Close(); };
            cancel.Click += (_, __) => { DialogResult = false; Close(); };
            row.Children.Add(ok);
            row.Children.Add(cancel);

            p.Children.Add(row);
            Content = p;
        }
    }

    internal sealed class ChooseOneDialog : Window
    {
        private readonly ListBox _list;
        public string? Value => _list.SelectedItem as string;

        public ChooseOneDialog(string title, string prompt, IEnumerable<string> options)
        {
            Title = title;
            Width = 420;
            Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new DockPanel { Margin = new Thickness(12) };

            var lbl = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(lbl, Dock.Top);
            root.Children.Add(lbl);

            _list = new ListBox { ItemsSource = options?.ToList() ?? new List<string>() };
            root.Children.Add(_list);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            DockPanel.SetDock(row, Dock.Bottom);

            var ok = new Button { Content = "套用", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "取消", Width = 80 };
            ok.Click += (_, __) => { DialogResult = true; Close(); };
            cancel.Click += (_, __) => { DialogResult = false; Close(); };

            row.Children.Add(ok);
            row.Children.Add(cancel);
            root.Children.Add(row);

            Content = root;
        }
    }
}
