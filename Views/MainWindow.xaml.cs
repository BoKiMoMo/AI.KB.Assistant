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
        private bool _pathCollapsed = false;

        private static readonly object DummyNode = new();

        public MainWindow()
        {
            InitializeComponent();

            _cfgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
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
            _lockedProject = _cfg.App.ProjectLock ?? string.Empty;
            RefreshProjectCombo();

            try { _hot.Start(); } catch { }

            BuildFolderTreeRoots();
            RefreshList("auto-sorted");
            Log("就緒。");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try { _cts.Cancel(); } catch { }
            try { _hot.Dispose(); } catch { }
            try { _db.Dispose(); } catch { }
            try { _llm.Dispose(); } catch { }
        }

        // ================ 小工具 ================
        private IEnumerable<Item> GetSelection()
            => FileList?.SelectedItems?.Cast<Item>() ?? Enumerable.Empty<Item>();

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
            => (MainTabs.SelectedItem as TabItem)?.Tag as string ?? "auto-sorted";

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

        // ================ 列表資料 ================
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

            FileList.ItemsSource = null;
            FileList.ItemsSource = _items;
            UpdateInfoPane(null);
            Log($"清單已更新（{statusFilter}）");
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainTabs.SelectedItem is TabItem ti)
                RefreshList((ti.Tag as string) ?? "auto-sorted");
        }

        // ================ 匯入 / 分類 / 搬檔 ================
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
                        it.Project = _lockedProject;

                    await _intake.ClassifyOnlyAsync(it.Path!, _cts.Token);
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

        // ================ 收件夾 / 重新整理 ================
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshList(CurrentTabTag());

        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e) => OpenInbox();

        private void OpenInbox()
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
            catch (Exception ex)
            {
                Log($"開啟收件夾失敗：{ex.Message}");
            }
        }

        // ================ 清單右鍵 / 開啟 / Enter ================
        private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
            => FileList.ContextMenu.IsEnabled = GetSelection().Any();

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

        private DateTime _lastOpen = DateTime.MinValue;
        private void OpenSelectedFile()
        {
            try
            {
                if ((DateTime.Now - _lastOpen).TotalMilliseconds < 500) return;
                _lastOpen = DateTime.Now;

                if (FileList.SelectedItem is not Item it || string.IsNullOrWhiteSpace(it.Path)) return;

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
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                }
            }
        }

        private void CtxCopyPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FileList.SelectedItem is Item it && !string.IsNullOrWhiteSpace(it.Path))
                {
                    Clipboard.SetText(it.Path);
                    Log($"已複製路徑：{it.Path}");
                }
            }
            catch { }
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

        private async void BtnSuggestProject_Click(object sender, RoutedEventArgs e)
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

        // ================ 專案鎖定 ================
        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            var desired = (CbLockProject.Text ?? "").Trim();

            if (string.IsNullOrEmpty(_lockedProject))
            {
                if (!string.IsNullOrWhiteSpace(desired))
                {
                    _lockedProject = desired;
                    _cfg.App.ProjectLock = _lockedProject;
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
                    try { ConfigService.Save(_cfgPath, _cfg); } catch { }
                }
            }
        }

        private void BtnSearchProject_Click(object sender, RoutedEventArgs e)
        {
            var keyword = TbProjectSearch?.Text?.Trim();
            var list = string.IsNullOrWhiteSpace(keyword)
                ? _db.QueryDistinctProjects().ToList()
                : _db.QueryDistinctProjects(keyword).ToList();
            CbLockProject.ItemsSource = list;
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new SettingsWindow { Owner = this };
                win.ShowDialog();
                // 可能更動 Root/收件夾 → 重建樹
                BuildFolderTreeRoots();
            }
            catch (Exception ex)
            {
                Log($"開啟設定失敗：{ex.Message}");
            }
        }

        // ================ 右欄收合 ================
        private void BtnToggleRightPane_Click(object sender, RoutedEventArgs e)
        {
            _rightCollapsed = !_rightCollapsed;
            RightPaneColumn.Width = _rightCollapsed ? new GridLength(0) : new GridLength(320);
        }

        // ================ 拖放 ================
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

        // =====================================================================
        // 檔案系統樹：根/展開/右鍵/收合展開
        // =====================================================================
        private void BuildFolderTreeRoots()
        {
            try
            {
                TvFolders.Items.Clear();

                if (!string.IsNullOrWhiteSpace(_cfg.App.RootDir) && Directory.Exists(_cfg.App.RootDir))
                    TvFolders.Items.Add(CreateDirNode(_cfg.App.RootDir));

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (Directory.Exists(desktop))
                    TvFolders.Items.Add(CreateDirNode(desktop, "桌面"));

                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    TvFolders.Items.Add(CreateDirNode(drive.RootDirectory.FullName, drive.Name));
            }
            catch (Exception ex)
            {
                Log($"建立路徑樹失敗：{ex.Message}");
            }
        }

        private TreeViewItem CreateDirNode(string path, string? headerOverride = null)
        {
            var name = headerOverride ?? (string.IsNullOrEmpty(System.IO.Path.GetFileName(path)) ? path : System.IO.Path.GetFileName(path));
            var node = new TreeViewItem { Header = name, Tag = path };
            node.Items.Add(DummyNode); // 為了顯示展開箭頭
            node.Expanded += DirNode_Expanded;
            return node;
        }

        private void DirNode_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem node) return;

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
            if (node == null) { UpdateInfoPane(null); return; }

            var path = node.Tag as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                ShowFolder(path);
                BuildBreadcrumb(path);
            }
        }

        // **這就是你缺的事件：在左樹節點上按右鍵**
        private void TvFolders_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 依滑鼠位置找出 TreeViewItem
            var tvi = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (tvi == null) return;

            tvi.IsSelected = true;
            var path = tvi.Tag as string;
            if (string.IsNullOrWhiteSpace(path)) return;

            // 動態組 ContextMenu：開啟資料夾、將此資料夾設為分類根目錄（鎖定）
            var cm = new ContextMenu();

            var open = new MenuItem { Header = "在檔案總管開啟" };
            open.Click += (_, __) =>
            {
                if (Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            };
            cm.Items.Add(open);

            var lockHere = new MenuItem { Header = "將此資料夾設為分類根目錄" };
            lockHere.Click += (_, __) =>
            {
                _cfg.App.RootDir = path;
                try { ConfigService.Save(_cfgPath, _cfg); } catch { }
                Log($"已將分類根目錄設為：{path}");
                BuildFolderTreeRoots();
            };
            cm.Items.Add(lockHere);

            cm.IsOpen = true;
            e.Handled = true;
        }

        // 左邊「收合 / 展開」：已展開則全部收合；未展開則展開目前第一層
        private void BtnTreeToggle_Click(object sender, RoutedEventArgs e)
        {
            bool anyExpanded = EnumerateAllTreeItems(TvFolders).Any(i => i.IsExpanded);

            if (anyExpanded)
            {
                foreach (var n in EnumerateAllTreeItems(TvFolders))
                    n.IsExpanded = false;
            }
            else
            {
                foreach (var n in TvFolders.Items.OfType<TreeViewItem>())
                    n.IsExpanded = true;
            }
        }

        private static IEnumerable<TreeViewItem> EnumerateAllTreeItems(ItemsControl root)
        {
            foreach (var o in root.Items)
            {
                var tvi = o as TreeViewItem ?? root.ItemContainerGenerator.ContainerFromItem(o) as TreeViewItem;
                if (tvi != null)
                {
                    yield return tvi;
                    foreach (var child in EnumerateAllTreeItems(tvi))
                        yield return child;
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T target) return target;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // 顯示資料夾「當層」到中清單
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
                        Ext = info.Extension?.Trim('.').ToLowerInvariant() ?? "",
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

                FileList.ItemsSource = null;
                FileList.ItemsSource = _items;
                UpdateInfoPane(null);

                Log($"顯示資料夾：{folder}（{files.Count} 筆）");
            }
            catch (Exception ex)
            {
                Log($"讀取資料夾失敗：{ex.Message}");
            }
        }

        // 路徑麵包屑
        private void BuildBreadcrumb(string folder)
        {
            try
            {
                PathStrip.Children.Clear();

                var parts = new List<string>();
                var current = folder;
                while (!string.IsNullOrEmpty(current))
                {
                    parts.Insert(0, current);
                    var parent = System.IO.Path.GetDirectoryName(current);
                    if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
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

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var it = FileList.SelectedItem as Item;
            UpdateInfoPane(it);
        }

        private void UpdateInfoPane(Item? it)
        {
            if (InfoPane == null) return;

            var empty = (StackPanel)InfoBody.FindName("InfoEmpty");
            var detail = (StackPanel)InfoBody.FindName("InfoDetail");

            if (it == null)
            {
                if (empty != null) empty.Visibility = Visibility.Visible;
                if (detail != null) detail.Visibility = Visibility.Collapsed;
                return;
            }

            if (empty != null) empty.Visibility = Visibility.Collapsed;
            if (detail != null) detail.Visibility = Visibility.Visible;

            var name = (TextBlock)InfoBody.FindName("InfoName");
            var path = (TextBlock)InfoBody.FindName("InfoPath");
            var size = (TextBlock)InfoBody.FindName("InfoSize");
            var created = (TextBlock)InfoBody.FindName("InfoCreated");
            var proj = (TextBlock)InfoBody.FindName("InfoProject");
            var cat = (TextBlock)InfoBody.FindName("InfoCategory");
            var status = (TextBlock)InfoBody.FindName("InfoStatus");
            var tags = (TextBlock)InfoBody.FindName("InfoTags");

            name.Text = it.Filename ?? "";
            path.Text = it.Path ?? "";
            proj.Text = it.Project ?? "";
            cat.Text = it.Category ?? "";
            status.Text = it.Status ?? "";
            tags.Text = it.Tags ?? "";

            try
            {
                if (!string.IsNullOrWhiteSpace(it.Path) && File.Exists(it.Path))
                {
                    var fi = new FileInfo(it.Path);
                    size.Text = $"{fi.Length:N0} bytes";
                    created.Text = fi.CreationTime.ToString("yyyy/MM/dd HH:mm:ss");
                }
                else
                {
                    size.Text = "-";
                    created.Text = "-";
                }
            }
            catch
            {
                size.Text = "-";
                created.Text = "-";
            }
        }

        private void BtnPathToggle_Click(object sender, RoutedEventArgs e)
        {
            _pathCollapsed = !_pathCollapsed;
            PathStrip.Visibility = _pathCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }

        // -------------- 內嵌簡易對話框 --------------
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

                var top = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
                DockPanel.SetDock(top, Dock.Top);
                root.Children.Add(top);

                _list = new ListBox();
                _list.ItemsSource = options?.ToList() ?? new List<string>();
                root.Children.Add(_list);

                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
                var ok = new Button { Content = "套用", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
                var cancel = new Button { Content = "取消", Width = 80 };
                ok.Click += (_, __) => { DialogResult = true; Close(); };
                cancel.Click += (_, __) => { DialogResult = false; Close(); };
                DockPanel.SetDock(row, Dock.Bottom);
                row.Children.Add(ok);
                row.Children.Add(cancel);

                root.Children.Add(row);
                Content = root;
            }
        }
    }
}
