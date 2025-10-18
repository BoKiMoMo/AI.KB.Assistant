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
using System.Windows.Media;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
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
        private bool _rightCollapsed = false;
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
            // 預設淺色：把基本前景/背景調和（淺色風格）
            ApplyLightTheme();

            _lockedProject = _cfg.App.ProjectLock ?? string.Empty;
            TxtLockedProject.Text = string.IsNullOrWhiteSpace(_lockedProject)
                ? "目前未鎖定專案"
                : $"目前鎖定：{_lockedProject}";

            RefreshProjectCombo();
            LoadFolderTree();

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

        // ===================== 主題（淺色簡易款） =====================
        private void ApplyLightTheme()
        {
            var bg = Brushes.White;
            var fg = Brushes.Black;
            Background = bg;
            Foreground = fg;
            if (FileList != null) { FileList.Background = bg; FileList.Foreground = fg; }
            if (LogBox != null) { LogBox.Background = Brushes.White; LogBox.Foreground = Brushes.Black; }
        }

        // ===================== 小工具 =====================
        private IEnumerable<Item> GetSelection()
        {
            if (FileList is ListView lv && lv.SelectedItems != null)
                return lv.SelectedItems.Cast<Item>().ToList();
            return Enumerable.Empty<Item>();
        }

        private void Log(string m)
        {
            try
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {m}\r\n");
                LogBox.ScrollToEnd();
            }
            catch { }
        }

        private string CurrentTabTag()
        {
            if (MainTabs.SelectedItem is TabItem ti) return (ti.Tag as string) ?? "auto-sorted";
            return "auto-sorted";
        }

        private void RefreshProjectCombo()
        {
            try
            {
                var all = _db.QueryDistinctProjects().ToList();
                CbLockProject.ItemsSource = all;
                if (!string.IsNullOrWhiteSpace(_lockedProject))
                    CbLockProject.Text = _lockedProject;
            }
            catch { }
        }

        // ===================== 左側樹狀（RootDir 掃描） =====================
        private sealed class DirNode
        {
            public string Path { get; set; } = string.Empty;
            public string Name => System.IO.Path.GetFileName(Path);
            public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Path : Name;
        }

        private void LoadFolderTree()
        {
            try
            {
                FolderTree.Items.Clear();
                var root = string.IsNullOrWhiteSpace(_cfg.App.RootDir)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    : _cfg.App.RootDir;

                if (!Directory.Exists(root)) Directory.CreateDirectory(root);

                var rootItem = new TreeViewItem { Header = root, Tag = new DirNode { Path = root } };
                rootItem.Items.Add("loading");
                rootItem.Expanded += TreeItem_Expanded;
                FolderTree.Items.Add(rootItem);
            }
            catch (Exception ex)
            {
                Log($"載入路徑失敗：{ex.Message}");
            }
        }

        private void TreeItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem tvi) return;
            if (tvi.Tag is not DirNode node) return;
            if (tvi.Items.Count == 1 && Equals(tvi.Items[0], "loading"))
            {
                tvi.Items.Clear();
                try
                {
                    foreach (var dir in Directory.GetDirectories(node.Path))
                    {
                        var sub = new TreeViewItem { Header = System.IO.Path.GetFileName(dir), Tag = new DirNode { Path = dir } };
                        sub.Items.Add("loading");
                        sub.Expanded += TreeItem_Expanded;
                        tvi.Items.Add(sub);
                    }
                }
                catch { }
            }
        }

        private void BtnReloadTree_Click(object sender, RoutedEventArgs e) => LoadFolderTree();

        private void FolderTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FolderTree.SelectedItem is TreeViewItem tvi && tvi.Tag is DirNode node)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", node.Path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log($"開啟資料夾失敗：{ex.Message}");
                }
            }
        }

        private void CtxTreeOpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (FolderTree.SelectedItem is TreeViewItem tvi && tvi.Tag is DirNode node)
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", node.Path) { UseShellExecute = true }); }
                catch (Exception ex) { Log($"開啟失敗：{ex.Message}"); }
            }
        }

        private void CtxTreeRename_Click(object sender, RoutedEventArgs e)
        {
            if (FolderTree.SelectedItem is TreeViewItem tvi && tvi.Tag is DirNode node)
            {
                var dlg = new SetTextDialog("重新命名", "輸入新資料夾名稱：");
                if (dlg.ShowDialog() == true)
                {
                    var newName = (dlg.Value ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        try
                        {
                            var parent = System.IO.Path.GetDirectoryName(node.Path)!;
                            var dest = System.IO.Path.Combine(parent, newName);
                            Directory.Move(node.Path, dest);
                            Log($"已更名：{node.Path} → {dest}");
                            LoadFolderTree();
                        }
                        catch (Exception ex) { Log($"更名失敗：{ex.Message}"); }
                    }
                }
            }
        }

        private void CtxTreeLockAsProject_Click(object sender, RoutedEventArgs e)
        {
            if (FolderTree.SelectedItem is TreeViewItem tvi && tvi.Tag is DirNode node)
            {
                _lockedProject = System.IO.Path.GetFileName(node.Path);
                _cfg.App.ProjectLock = _lockedProject;
                CbLockProject.Text = _lockedProject;
                TxtLockedProject.Text = $"目前鎖定：{_lockedProject}";
                try { ConfigService.Save(_cfgPath, _cfg); } catch { }
                Log($"🔒 已鎖定專案「{_lockedProject}」");
            }
        }

        // ===================== 列表刷新 =====================
        private void RefreshList(string statusFilter)
        {
            _items.Clear();

            IEnumerable<Item> src;
            switch (statusFilter?.ToLowerInvariant())
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
            FileList.ItemsSource = null; // 保持乾淨
            FileList.ItemsSource = _items;
            Log($"清單已更新（{statusFilter}）");
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainTabs.SelectedItem is TabItem ti)
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
                    {
                        it.Project = _lockedProject;
                        _db.UpdateProject(it.Id, _lockedProject);
                    }

                    await _intake.ClassifyOnlyAsync(it.Path!, _cts.Token); // 只預估
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
            var tag = CurrentTabTag();
            RefreshList(tag);
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
            if (FileList?.ContextMenu != null)
                FileList.ContextMenu.IsEnabled = GetSelection().Any();
        }

        private void CtxOpenFile_Click(object sender, RoutedEventArgs e) => OpenSelectedFile();
        private void CtxCopyPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var it = FileList.SelectedItem as Item;
                if (it != null && !string.IsNullOrWhiteSpace(it.Path))
                {
                    Clipboard.SetText(it.Path);
                    Log($"已複製路徑：{it.Path}");
                }
            }
            catch { }
        }

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
                if ((DateTime.Now - _lastOpen).TotalMilliseconds < 500) return; // 去抖動
                _lastOpen = DateTime.Now;

                var it = FileList.SelectedItem as Item;
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
                {
                    try { Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true }); }
                    catch (Exception ex) { Log($"開啟資料夾失敗：{ex.Message}"); }
                }
            }
        }

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

        private async void BtnSuggestProject_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.InvokeAsync(() => CtxSuggestProject_Click(sender, e));
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

        // ===================== 專案鎖定（即時寫檔） =====================
        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            var desired = (CbLockProject.Text ?? "").Trim();

            if (string.IsNullOrEmpty(_lockedProject))
            {
                if (!string.IsNullOrWhiteSpace(desired))
                {
                    _lockedProject = desired;
                    _cfg.App.ProjectLock = _lockedProject;
                    TxtLockedProject.Text = $"目前鎖定：{_lockedProject}";
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
                    TxtLockedProject.Text = "目前未鎖定專案";
                    try { ConfigService.Save(_cfgPath, _cfg); } catch { }
                }
            }
        }

        private void BtnSearchProject_Click(object sender, RoutedEventArgs e)
        {
            // 若你在 XAML 放了 TbProjectSearch / ProjectList，可以在此實作搜尋清單
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

        // ===================== 右欄收合 =====================
        private void BtnToggleRightPane_Click(object sender, RoutedEventArgs e)
        {
            if (_rightCollapsed)
            {
                RightPaneColumn.Width = new GridLength(320);
                _rightCollapsed = false;
            }
            else
            {
                RightPaneColumn.Width = new GridLength(0);
                _rightCollapsed = true;
            }
        }

        // ===================== 拖放 =====================
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

    // ===================== 內嵌對話框 =====================
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
