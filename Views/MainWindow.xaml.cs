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
        // ==== 快捷鍵：RoutedUICommand (Ctrl+O / Ctrl+I / Ctrl+T / Ctrl+Enter) ====
        public static readonly RoutedUICommand CmdOpenInbox =
            new("Open Inbox", nameof(CmdOpenInbox), typeof(MainWindow),
                new InputGestureCollection { new KeyGesture(Key.O, ModifierKeys.Control) });

        public static readonly RoutedUICommand CmdToggleInfo =
            new("Toggle Info Pane", nameof(CmdToggleInfo), typeof(MainWindow),
                new InputGestureCollection { new KeyGesture(Key.I, ModifierKeys.Control) });

        public static readonly RoutedUICommand CmdToggleTree =
            new("Toggle Tree Pane", nameof(CmdToggleTree), typeof(MainWindow),
                new InputGestureCollection { new KeyGesture(Key.T, ModifierKeys.Control) });

        public static readonly RoutedUICommand CmdPrimaryAction =
            new("Primary Action", nameof(CmdPrimaryAction), typeof(MainWindow),
                new InputGestureCollection { new KeyGesture(Key.Enter, ModifierKeys.Control) });

        private readonly string _cfgPath;
        private AppConfig _cfg = new();
        private DbService? _db;
        private RoutingService? _routing;
        private LlmService? _llm;
        private IntakeService? _intake;

        private readonly List<Item> _items = new();
        private CancellationTokenSource _cts = new();
        private string _lockedProject = string.Empty;

        private static readonly object DummyNode = new();
        private bool _isReady;

        // 折疊狀態
        private bool _leftCollapsed = false;
        private bool _rightCollapsed = false;

        public MainWindow()
        {
            InitializeComponent();

            _cfgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;

            RtThreshold.ValueChanged += (s, e) => { RtThresholdValue.Text = $"{RtThreshold.Value:0.00}"; };
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _cfg = ConfigService.TryLoad(_cfgPath);

            _db = new DbService(_cfg.App.DbPath);
            _routing = new RoutingService(_cfg);
            _llm = new LlmService(_cfg);
            _intake = new IntakeService(_db, _routing, _llm, _cfg);

            _lockedProject = _cfg.App.ProjectLock ?? string.Empty;
            TxtLockedProject.Text = string.IsNullOrWhiteSpace(_lockedProject) ? "目前未鎖定專案" : $"目前鎖定：{_lockedProject}";
            RefreshProjectCombo();

            BuildFolderTreeRoots();

            RtThreshold.Value = _cfg.Classification.ConfidenceThreshold;
            RtBlacklist.Text = string.Join(", ", _cfg.Import.BlacklistFolderNames ?? Array.Empty<string>());

            MainTabs.SelectedIndex = 0;
            _isReady = true;

            if (!string.IsNullOrWhiteSpace(_cfg.App.RootDir) && Directory.Exists(_cfg.App.RootDir))
            {
                ShowFolder(_cfg.App.RootDir);
                BuildBreadcrumb(_cfg.App.RootDir);
            }
            else
            {
                RefreshList("home");
            }

            Log("系統已就緒。");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try { _cts.Cancel(); } catch { }
            try { _db?.Dispose(); } catch { }
            try { _llm?.Dispose(); } catch { }
        }

        // ===== 工具 =====
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
                var line = $"[{DateTime.Now:HH:mm:ss}] {m}";
                LogBox.AppendText(line + Environment.NewLine);
                LogBox.ScrollToEnd();
                RtHistory?.Items?.Add(line);
            }
            catch { }
        }

        private void RefreshProjectCombo()
        {
            try
            {
                if (_db == null) return;
                var all = _db.QueryDistinctProjects().ToList();
                CbLockProject.ItemsSource = all;
                if (!string.IsNullOrWhiteSpace(_lockedProject))
                    CbLockProject.Text = _lockedProject;
            }
            catch { }
        }

        private void UpdateCounters()
        {
            try
            {
                TxtCounter.Text = $"清單筆數：{_items.Count}；選取：{GetSelection().Count()}";
                if (FileList.SelectedItem is Item it)
                {
                    RtName.Text = it.Filename ?? "";
                    RtMeta.Text = $"{it.Category} / {it.Project} / {it.Tags}";
                    RtPath.Text = it.Path ?? "";
                }
                else
                {
                    RtName.Text = RtMeta.Text = RtPath.Text = "";
                }
            }
            catch { }
        }

        private void BindAndRefreshList()
        {
            FileList.ItemsSource = null;
            FileList.ItemsSource = _items;
            UpdateCounters();
        }

        private static string[] ParseListFromText(string? text)
            => (text ?? "")
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        // ===== 清單刷新 =====
        private string CurrentTabTag()
        {
            if (MainTabs.SelectedItem is TabItem ti && ti.Tag is string tag) return tag;
            return "home";
        }

        private void RefreshList(string statusFilter)
        {
            if (!_isReady || _db == null) return;

            _items.Clear();
            IEnumerable<Item> src = Enumerable.Empty<Item>();

            switch (statusFilter)
            {
                case "fav":
                    src = _db.QueryByTag("我的最愛").OrderByDescending(i => i.CreatedTs);
                    break;
                case "processing":
                    src = _db.QueryByTag("處理中").OrderByDescending(i => i.CreatedTs);
                    break;
                case "backlog":
                    src = _db.QueryByTag("待處理").OrderByDescending(i => i.CreatedTs);
                    break;
                case "blacklist":
                    src = _db.QueryByStatus("blacklist").OrderByDescending(i => i.CreatedTs);
                    break;
                case "autosort-staging":
                    src = _db.QueryByStatus("autosort-staging").OrderByDescending(i => i.CreatedTs);
                    break;
                case "home":
                default:
                    src = _db.QueryByStatus("auto-sorted").OrderByDescending(i => i.CreatedTs);
                    break;
            }

            foreach (var it in src) _items.Add(it);
            BindAndRefreshList();
            Log($"清單已更新（{statusFilter}）");
        }

        // ===== 匯入 / 分類 / 搬檔 =====
        private async void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null || _db == null) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var inbox = _db.QueryByStatus("inbox").ToList();
            int done = 0;

            foreach (var it in inbox)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_lockedProject))
                        it.Project = _lockedProject;

                    await _intake.ClassifyOnlyAsync(it.Path!, _cts.Token);
                    done++;
                }
                catch { }
            }

            RefreshList("home");
            Log($"預分類完成：{done} 筆");
        }

        private async void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var moved = await _intake.CommitPendingAsync(_cts.Token);
                Log($"搬檔完成：{moved} 筆");
                RefreshList(CurrentTabTag());
            }
            catch (Exception ex) { Log($"搬檔失敗：{ex.Message}"); }
        }

        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null) return;

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

                    RefreshList("home");
                    Log($"加入 {dlg.FileNames.Length} 筆到 Inbox");
                }
            }
            catch (Exception ex) { Log($"加入失敗：{ex.Message}"); }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_db != null)
                {
                    var removed = _db.PurgeMissing();
                    if (removed > 0) Log($"已清理不存在檔案的 DB 紀錄：{removed} 筆");
                }
            }
            catch (Exception ex) { Log($"清理失敗：{ex.Message}"); }

            RefreshList(CurrentTabTag());
        }

        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var root = string.IsNullOrWhiteSpace(_cfg.App.RootDir)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    : _cfg.App.RootDir;
                var inbox = string.IsNullOrWhiteSpace(_cfg.Import.HotFolderPath)
                    ? System.IO.Path.Combine(root, "_Inbox")
                    : _cfg.Import.HotFolderPath;

                if (!Directory.Exists(inbox)) Directory.CreateDirectory(inbox);
                Process.Start(new ProcessStartInfo("explorer.exe", inbox) { UseShellExecute = true });
                Log($"已開啟收件夾：{inbox}");
            }
            catch (Exception ex) { Log($"開啟收件夾失敗：{ex.Message}"); }
        }

        // ===== 清單：操作 =====
        private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (FileList?.ContextMenu != null)
                FileList.ContextMenu.IsEnabled = GetSelection().Any();
            UpdateCounters();
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

        private DateTime _lastOpen = DateTime.MinValue;
        private void OpenSelectedFile()
        {
            try
            {
                if ((DateTime.Now - _lastOpen).TotalMilliseconds < 500) return;
                _lastOpen = DateTime.Now;

                var it = FileList.SelectedItem as Item;
                if (it == null || string.IsNullOrWhiteSpace(it.Path)) return;

                if (File.Exists(it.Path))
                {
                    Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true });
                    Log($"開啟檔案：{System.IO.Path.GetFileName(it.Path)}");
                }
                else
                {
                    var dir = System.IO.Path.GetDirectoryName(it.Path);
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
            catch (Exception ex) { Log($"開啟失敗：{ex.Message}"); }
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

        private void CtxOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            foreach (var it in GetSelection())
            {
                var dir = System.IO.Path.GetDirectoryName(it.Path);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                }
            }
        }

        private void CtxSetProject_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;

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

        private void CtxQuickTag_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;
            if (sender is MenuItem mi && mi.Tag is string tag)
            {
                var sel = GetSelection().ToList();
                foreach (var it in sel)
                {
                    it.Tags = string.IsNullOrWhiteSpace(it.Tags) ? tag : $"{it.Tags},{tag}";
                    _db.UpdateTags(it.Id, it.Tags!);
                }
                Log($"已套用標籤「{tag}」到 {sel.Count} 筆");
                RefreshList(CurrentTabTag());
            }
        }

        private void CtxSetTags_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;

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

        private void CtxRename_Click(object sender, RoutedEventArgs e)
        {
            var it = FileList.SelectedItem as Item;
            if (it == null || string.IsNullOrWhiteSpace(it.Path)) return;

            var box = new SetTextDialog("重新命名", "輸入新檔名（含副檔名）：", it.Filename ?? "");
            if (box.ShowDialog() == true)
            {
                var newName = (box.Value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(newName)) return;

                try
                {
                    var newPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(it.Path)!, newName);
                    File.Move(it.Path!, newPath);
                    it.Filename = newName;
                    it.Path = newPath;
                    BindAndRefreshList();
                    Log($"已重新命名：{newName}");
                }
                catch (Exception ex) { Log($"重新命名失敗：{ex.Message}"); }
            }
        }

        private async void CtxMoveToInbox_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null || _db == null) return;

            try
            {
                var sel = GetSelection().ToList();
                if (sel.Count == 0) return;

                int ok = 0;
                foreach (var it in sel)
                {
                    if (string.IsNullOrWhiteSpace(it.Path) || !File.Exists(it.Path)) continue;
                    await _intake.StageOnlyAsync(it.Path!, CancellationToken.None);
                    it.Status = "inbox";
                    _db.UpsertItem(it);
                    ok++;
                }

                Log($"已移動到收件夾：{ok} 筆");
                RefreshList("home");
            }
            catch (Exception ex) { Log($"移動到收件夾失敗：{ex.Message}"); }
        }

        private void CtxDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sel = GetSelection().ToList();
                if (sel.Count == 0) return;

                if (MessageBox.Show($"確定要刪除所選 {sel.Count} 筆檔案嗎？\n（僅刪除磁碟檔案，資料庫紀錄請另行清理）",
                                    "刪除確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                int ok = 0;
                foreach (var it in sel)
                {
                    if (!string.IsNullOrWhiteSpace(it.Path) && File.Exists(it.Path))
                    {
                        File.Delete(it.Path);
                        ok++;
                    }
                    _items.Remove(it);
                }
                BindAndRefreshList();
                Log($"已刪除檔案：{ok} 筆");
            }
            catch (Exception ex) { Log($"刪除失敗：{ex.Message}"); }
        }

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCounters();

        // ===== 左/右欄收合（工具列按鈕沿用） =====
        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e)
        {
            LeftPaneColumn.Width = _leftCollapsed ? new GridLength(280) : new GridLength(0);
            _leftCollapsed = !_leftCollapsed;
        }

        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e)
        {
            RightPaneColumn.Width = _rightCollapsed ? new GridLength(360) : new GridLength(0);
            _rightCollapsed = !_rightCollapsed;
        }

        // ===== 拖放 =====
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (_intake == null) return;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var f in files)
                    await _intake.StageOnlyAsync(f, CancellationToken.None);

                Log($"拖放加入 {files.Length} 筆至 Inbox");
                RefreshList("home");
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        // ===== 路徑樹 =====
        private void BuildFolderTreeRoots()
        {
            try
            {
                TvFolders.Items.Clear();

                if (!string.IsNullOrWhiteSpace(_cfg.App.RootDir) && Directory.Exists(_cfg.App.RootDir))
                    TvFolders.Items.Add(CreateDirNode(_cfg.App.RootDir, "專案根目錄"));

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (Directory.Exists(desktop))
                    TvFolders.Items.Add(CreateDirNode(desktop, "桌面"));

                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    TvFolders.Items.Add(CreateDirNode(drive.RootDirectory.FullName, drive.Name));
            }
            catch (Exception ex) { Log($"建立路徑樹失敗：{ex.Message}"); }
        }

        private TreeViewItem CreateDirNode(string path, string? headerOverride = null)
        {
            var name = headerOverride ?? (string.IsNullOrEmpty(System.IO.Path.GetFileName(path)) ? path : System.IO.Path.GetFileName(path));
            var node = new TreeViewItem { Header = name, Tag = path };
            node.Items.Add(DummyNode);
            node.Expanded += DirNode_Expanded;
            return node;
        }

        private void DirNode_Expanded(object? sender, RoutedEventArgs e)
        {
            var node = sender as TreeViewItem;
            if (node == null) return;

            if (node.Items.Count == 1 && ReferenceEquals(node.Items[0], DummyNode))
            {
                node.Items.Clear();
                var baseDir = node.Tag as string;
                if (string.IsNullOrWhiteSpace(baseDir)) return;

                try
                {
                    var subdirs = Directory.EnumerateDirectories(baseDir)
                                           .Where(d => !IsHiddenOrSystem(d))
                                           .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
                    foreach (var dir in subdirs)
                        node.Items.Add(CreateDirNode(dir));
                }
                catch { }
            }
        }

        private static bool IsHiddenOrSystem(string path)
        {
            try
            {
                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.Hidden) != 0 || (attr & FileAttributes.System) != 0;
            }
            catch { return false; }
        }

        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = TvFolders.SelectedItem as TreeViewItem;
            if (node == null) return;

            var path = node.Tag as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                MainTabs.SelectedIndex = 0;
                ShowFolder(path);
                BuildBreadcrumb(path);
            }
        }

        private void ShowFolder(string folder)
        {
            try
            {
                _items.Clear();

                var files = Directory.EnumerateFiles(folder)
                                     .Where(f =>
                                     {
                                         try
                                         {
                                             var attr = File.GetAttributes(f);
                                             return (attr & FileAttributes.Hidden) == 0 && (attr & FileAttributes.System) == 0;
                                         }
                                         catch { return false; }
                                     })
                                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

                long id = 1;
                foreach (var f in files)
                {
                    var info = new FileInfo(f);
                    var item = new Item
                    {
                        Id = id++,
                        Filename = info.Name,
                        Ext = (info.Extension ?? "").Trim('.').ToLowerInvariant(),
                        Project = "",
                        Category = "",
                        Confidence = 0,
                        CreatedTs = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds(),
                        Status = "",
                        Path = info.FullName,
                        Tags = ""
                    };
                    _items.Add(item);
                }

                BindAndRefreshList();
                Log($"顯示資料夾：{folder}（{files.Count} 筆）");
            }
            catch (Exception ex) { Log($"讀取資料夾失敗：{ex.Message}"); }
        }

        private void BuildBreadcrumb(string folder)
        {
            try
            {
                PathStrip.Children.Clear();

                var parts = new List<string>();
                var current = folder;
                while (!string.IsNullOrEmpty(current))
                {
                    var parent = System.IO.Path.GetDirectoryName(current);
                    if (string.IsNullOrEmpty(parent))
                    {
                        parts.Insert(0, current);
                        break;
                    }
                    parts.Insert(0, current);
                    if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                        break;
                    current = parent;
                }

                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    var btn = new Button
                    {
                        Content = System.IO.Path.GetFileName(p).Length == 0 ? p : System.IO.Path.GetFileName(p),
                        Margin = new Thickness(0, 0, 4, 0),
                        Padding = new Thickness(6, 2, 6, 2),
                        Tag = p
                    };
                    btn.Click += (s, _) =>
                    {
                        var path = (s as Button)?.Tag as string;
                        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                        {
                            MainTabs.SelectedIndex = 0;
                            ShowFolder(path);
                            BuildBreadcrumb(path);
                        }
                    };
                    PathStrip.Children.Add(btn);

                    if (i != parts.Count - 1)
                        PathStrip.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                }
            }
            catch { }
        }

        // ===== 分頁（只處理 TabControl 自己的變更） =====
        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isReady) return;
            if (e.OriginalSource is not TabControl) return; // 避免 ListView 的 SelectionChanged 冒泡
            RefreshList(CurrentTabTag());
        }

        // ===== 設定視窗 / 右欄設定 =====
        public void ReloadConfig()
        {
            _cfg = ConfigService.TryLoad(_cfgPath);
            _routing?.ApplyConfig(_cfg);
            _llm?.UpdateConfig(_cfg);
            _intake?.UpdateConfig(_cfg);
            RefreshProjectCombo();

            RtThreshold.Value = _cfg.Classification.ConfidenceThreshold;
            RtBlacklist.Text = string.Join(", ", _cfg.Import.BlacklistFolderNames ?? Array.Empty<string>());

            Log("已重新載入設定。");
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(this, _cfg) { Owner = this };
            if (win.ShowDialog() == true)
                ReloadConfig();
        }

        // ===== LLM 協助 =====
        private async void BtnGenTags_Click(object sender, RoutedEventArgs e)
        {
            if (_llm == null) return;
            var it = FileList.SelectedItem as Item;
            if (it == null) { Log("請先選取一筆檔案"); return; }

            try
            {
                var tags = await _llm.SuggestProjectNamesAsync(new[] { it.Filename ?? "" }, CancellationToken.None);
                RtLlmOut.Text = string.Join(", ", tags ?? Array.Empty<string>());
            }
            catch (Exception ex) { RtLlmOut.Text = ex.Message; }
        }

        private async void BtnSummarize_Click(object sender, RoutedEventArgs e)
        {
            if (_llm == null) return;
            var it = FileList.SelectedItem as Item;
            if (it == null) { Log("請先選取一筆檔案"); return; }

            try
            {
                RtLlmOut.Text = $"摘要：{it.Filename}";
                await Task.CompletedTask;
            }
            catch (Exception ex) { RtLlmOut.Text = ex.Message; }
        }

        private async void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e)
        {
            if (_llm == null) return;
            var it = FileList.SelectedItem as Item;
            if (it == null) { Log("請先選取一筆檔案"); return; }

            try
            {
                await Task.CompletedTask;
                RtLlmOut.Text = $"門檻：{_cfg.Classification.ConfidenceThreshold:0.00}；檔案：{it.Filename}";
            }
            catch (Exception ex) { RtLlmOut.Text = ex.Message; }
        }

        private void BtnApplyAiTuning_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cfg.Classification.ConfidenceThreshold = RtThreshold.Value;
                _cfg.Import.BlacklistFolderNames = ParseListFromText(RtBlacklist.Text);

                ConfigService.Save(_cfgPath, _cfg);
                ReloadConfig();
                Log("AI 自動整理設定已套用。");
            }
            catch (Exception ex) { Log($"套用失敗：{ex.Message}"); }
        }

        private void BtnReloadSettings_Click(object sender, RoutedEventArgs e) => ReloadConfig();

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
                else
                {
                    var dlg = new SetTextDialog("鎖定專案", "請輸入要鎖定的專案名稱：");
                    if (dlg.ShowDialog() == true)
                    {
                        var name = dlg.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            _lockedProject = name!;
                            _cfg.App.ProjectLock = _lockedProject;
                            TxtLockedProject.Text = $"目前鎖定：{_lockedProject}";
                            CbLockProject.Text = _lockedProject;
                            try { ConfigService.Save(_cfgPath, _cfg); } catch { }
                            Log($"🔒 已鎖定專案「{_lockedProject}」");
                        }
                    }
                }
            }
            else
            {
                var ans = MessageBox.Show($"是否要解除目前鎖定的專案「{_lockedProject}」？",
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
            if (_db == null) return;

            var keyword = TbProjectSearch?.Text?.Trim();
            var list = string.IsNullOrWhiteSpace(keyword)
                ? _db.QueryDistinctProjects().ToList()
                : _db.QueryDistinctProjects(keyword).ToList();
            CbLockProject.ItemsSource = list;
        }

        // ===== 右鍵先選取該列 =====
        private void ListViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var dep = e.OriginalSource as DependencyObject;
                var lvi = FindParent<ListViewItem>(dep);
                if (lvi != null)
                {
                    lvi.IsSelected = true;
                    lvi.Focus();
                    FileList.Focus();
                }
            }
            catch { }
        }

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t) return t;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        // ===== 左樹右鍵功能 =====
        private void Tree_OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = (TvFolders.SelectedItem as TreeViewItem)?.Tag as string;
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            }
            catch (Exception ex) { Log($"開啟檔案總管失敗：{ex.Message}"); }
        }

        private async void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null) { Log("匯入服務未就緒。"); return; }

            var folder = (TvFolders.SelectedItem as TreeViewItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

            try
            {
                var files = Directory.EnumerateFiles(folder)
                    .Where(f =>
                    {
                        try
                        {
                            var a = File.GetAttributes(f);
                            return (a & FileAttributes.Hidden) == 0 && (a & FileAttributes.System) == 0;
                        }
                        catch { return false; }
                    })
                    .ToList();

                int ok = 0;
                foreach (var f in files)
                {
                    await _intake.StageOnlyAsync(f, CancellationToken.None);
                    ok++;
                }

                Log($"已加入收件夾：{ok} 筆（{folder}）");
                RefreshList("home");
            }
            catch (Exception ex) { Log($"加入收件夾失敗：{ex.Message}"); }
        }

        private void Tree_LockProject_Click(object sender, RoutedEventArgs e)
        {
            var path = (TvFolders.SelectedItem as TreeViewItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(path)) return;

            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name)) name = path;

            _lockedProject = name;
            _cfg.App.ProjectLock = name;
            TxtLockedProject.Text = $"目前鎖定：{name}";
            try { ConfigService.Save(_cfgPath, _cfg); } catch { }
            Log($"🔒 已鎖定專案「{name}」");
            CbLockProject.Text = name;
        }

        private void Tree_UnlockProject_Click(object sender, RoutedEventArgs e)
        {
            _lockedProject = string.Empty;
            _cfg.App.ProjectLock = string.Empty;
            TxtLockedProject.Text = "目前未鎖定專案";
            try { ConfigService.Save(_cfgPath, _cfg); } catch { }
            Log("🔓 已解除專案鎖定");
        }

        private void Tree_Rename_Click(object sender, RoutedEventArgs e)
        {
            var path = (TvFolders.SelectedItem as TreeViewItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

            var box = new SetTextDialog("重新命名資料夾", "輸入新名稱：", System.IO.Path.GetFileName(path));
            if (box.ShowDialog() == true)
            {
                var newName = (box.Value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(newName)) return;

                try
                {
                    var parent = System.IO.Path.GetDirectoryName(path)!;
                    var newPath = System.IO.Path.Combine(parent, newName);
                    Directory.Move(path, newPath);
                    Log($"已重新命名資料夾：{newName}");
                    BuildFolderTreeRoots();
                }
                catch (Exception ex) { Log($"重新命名資料夾失敗：{ex.Message}"); }
            }
        }

        // ====== ★★★ 快捷鍵對應的 Command 事件處理器 ★★★ ======
        private void CmdOpenInbox_Executed(object sender, ExecutedRoutedEventArgs e)
            => BtnOpenInbox_Click(sender, new RoutedEventArgs());

        private void CmdToggleInfo_Executed(object sender, ExecutedRoutedEventArgs e)
            => BtnEdgeRight_Click(sender, new RoutedEventArgs());

        private void CmdToggleTree_Executed(object sender, ExecutedRoutedEventArgs e)
            => BtnEdgeLeft_Click(sender, new RoutedEventArgs());

        /// <summary>
        /// Ctrl+Enter：智慧主動作
        /// 若有待搬移（autosort-staging），則執行「搬檔」；否則執行「預分類」。
        /// </summary>
        private async void CmdPrimaryAction_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                if (_db != null && _db.QueryByStatus("autosort-staging").Any())
                {
                    BtnCommit_Click(sender, new RoutedEventArgs());
                }
                else
                {
                    BtnStartClassify_Click(sender, new RoutedEventArgs());
                }
            }
            catch
            {
                // 保守 fallback：先試搬檔，不行就預分類
                if (_intake != null)
                    await _intake.CommitPendingAsync(CancellationToken.None);
                else
                    BtnStartClassify_Click(sender, new RoutedEventArgs());
            }
        }

        // ====== 內嵌簡易對話框 ======
        internal sealed class SetTextDialog : Window
        {
            private readonly TextBox _tb;
            public string? Value => _tb.Text;

            public SetTextDialog(string title, string prompt, string initial = "")
            {
                Title = title;
                Width = 440;
                Height = 170;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ResizeMode = ResizeMode.NoResize;
                Owner = Application.Current?.Windows?.OfType<Window>()?.FirstOrDefault(w => w.IsActive);

                var p = new StackPanel { Margin = new Thickness(12) };
                p.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
                _tb = new TextBox { Margin = new Thickness(0, 0, 0, 12), Text = initial };
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
    }

    // ====== DbService 擴充（標籤快速查） ======
    internal static class DbServiceExtensions
    {
        public static IEnumerable<Item> QueryByTag(this DbService db, string tag)
        {
            return db.QuerySince(0).Where(i => (i.Tags ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Contains(tag));
        }
    }
}
