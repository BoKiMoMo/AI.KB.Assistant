using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
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

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        // ====== Services & States ======
        private readonly string _cfgPath;
        private AppConfig _cfg = new();
        private UiState _ui = new();
        private DbService? _db;
        private RoutingService? _routing;
        private LlmService? _llm;
        private IntakeService? _intake;

        private readonly List<Item> _items = new();
        private CancellationTokenSource _cts = new();
        private string _lockedProject = string.Empty;

        // ====== 左樹監聽與 Debounce ======
        private FileSystemWatcher? _watchRoot;
        private FileSystemWatcher? _watchHot;
        private FileSystemWatcher? _watchDesktop;
        private System.Timers.Timer? _fsDebounce;
        private static readonly object DummyNode = new();
        private string DesktopPath => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        // ====== 清單排序狀態 ======
        private string _sortColumn = "CreatedTs";
        private bool _sortDesc = true;

        // ====== 中心清單來源 ======
        private enum CenterSource { Folder, StatusTab }
        private CenterSource _centerSource = CenterSource.Folder;
        private string _currentFolder = string.Empty;

        // ====== Commands（給頂部四顆路徑籤用） ======
        public ICommand OpenPathCommand => new RelayCommand<string?>(p => OpenPath(p));
        public ICommand CopyPathCommand => new RelayCommand<string?>(p => { if (!string.IsNullOrWhiteSpace(p)) Clipboard.SetText(p!); });

        public MainWindow()
        {
            InitializeComponent();

            _cfgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            // 讀設定 & 初始化服務
            _cfg = ConfigService.TryLoad(_cfgPath);
            _ui = UiStateService.Load();

            _db = new DbService(_cfg.App.DbPath);
            _routing = new RoutingService(_cfg);
            _llm = new LlmService(_cfg);
            _intake = new IntakeService(_db, _routing, _llm, _cfg);

            // 門檻顯示
            RtThreshold.ValueChanged += (s, e2) => RtThresholdValue.Text = $"{RtThreshold.Value:0.00}";
            RtThreshold.Value = _cfg.Classification.ConfidenceThreshold;
            RtThresholdValue.Text = $"{_cfg.Classification.ConfidenceThreshold:0.00}";
            RtBlacklist.Text = string.Join(", ", _cfg.Import.BlacklistFolderNames ?? Array.Empty<string>());

            // 監聽 & 建樹
            InitFsDebounce();
            StartFolderWatchers();
            BuildFolderTreeRoots();

            // 頂部四顆路徑籤
            RefreshTopPaths();

            // 初始清單
            var startFolder = (!string.IsNullOrWhiteSpace(_ui.LastFolder) && Directory.Exists(_ui.LastFolder))
                              ? _ui.LastFolder
                              : (_cfg.App.RootDir ?? DesktopPath);
            if (Directory.Exists(startFolder))
            {
                _centerSource = CenterSource.Folder;
                _currentFolder = startFolder;
                ShowFolder(startFolder);
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try { StopFolderWatchers(); } catch { }
            try { _cts.Cancel(); } catch { }
            try { _db?.Dispose(); } catch { }
            try { _llm?.Dispose(); } catch { }
        }

        // ===============================
        // 工具：Banner / Log / 開啟路徑 / TopPaths
        // ===============================
        private void ShowBanner(string msg, bool isWarn = false)
        {
            try
            {
                Banner.Background = isWarn
                    ? (Brush)FindResource("App.BannerErrorBrush")
                    : (Brush)FindResource("App.BannerInfoBrush");
                Banner.BorderBrush = (Brush)FindResource("App.BorderBrush");
            }
            catch { }
            BannerText.Text = msg;
            Banner.Visibility = Visibility.Visible;
        }
        private void HideBanner() => Banner.Visibility = Visibility.Collapsed;

        private void Log(string m)
        {
            try
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {m}\n");
                LogBox.ScrollToEnd();
            }
            catch { }
        }

        private static void OpenPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            }
        }

        private void RefreshTopPaths()
        {
            var list = new[]
            {
                new TopPath("ROOT",   _cfg.App.RootDir ?? ""),
                new TopPath("收件夾", _cfg.Import.HotFolderPath ?? ""),
                new TopPath("桌面",   DesktopPath),
                new TopPath("DB",     _cfg.App.DbPath ?? "")
            };
            TopPaths.ItemsSource = list;
        }

        private record TopPath(string Label, string Path);

        // ===============================
        // A) 左樹：建樹、監聽、點擊
        // ===============================
        private void InitFsDebounce()
        {
            _fsDebounce = new System.Timers.Timer(700);
            _fsDebounce.AutoReset = false;
            _fsDebounce.Elapsed += (_, __) => Dispatcher.Invoke(RebuildTreeAndList);
        }

        private void StartFolderWatchers()
        {
            StopFolderWatchers();

            void setup(ref FileSystemWatcher? w, string? path)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
                w = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite
                };
                w.Created += OnFsChanged;
                w.Deleted += OnFsChanged;
                w.Renamed += OnFsChanged;
                w.Changed += OnFsChanged;
                w.EnableRaisingEvents = true;
            }

            setup(ref _watchRoot, _cfg.App.RootDir);
            setup(ref _watchHot, _cfg.Import.HotFolderPath);
            setup(ref _watchDesktop, DesktopPath);
        }

        private void StopFolderWatchers()
        {
            foreach (var w in new[] { _watchRoot, _watchHot, _watchDesktop })
            {
                if (w == null) continue;
                w.EnableRaisingEvents = false;
                w.Created -= OnFsChanged; w.Deleted -= OnFsChanged;
                w.Renamed -= OnFsChanged; w.Changed -= OnFsChanged;
                w.Dispose();
            }
            _watchRoot = _watchHot = _watchDesktop = null;
        }

        private void OnFsChanged(object sender, FileSystemEventArgs e)
        {
            _fsDebounce?.Stop();
            _fsDebounce?.Start();
        }

        private void RebuildTreeAndList()
        {
            BuildFolderTreeRoots();
            if (_centerSource == CenterSource.Folder && Directory.Exists(_currentFolder))
                ShowFolder(_currentFolder);
        }

        private void BuildFolderTreeRoots()
        {
            try
            {
                TvFolders.Items.Clear();

                void addRoot(string header, string? path)
                {
                    if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
                    var node = new TreeViewItem { Header = header, Tag = path, ToolTip = path };
                    node.Items.Add(DummyNode);
                    node.Expanded += DirNode_Expanded;
                    TvFolders.Items.Add(node);
                }

                addRoot("Root", _cfg.App.RootDir);
                addRoot("Hot Folder", _cfg.Import.HotFolderPath);
                addRoot("Desktop", DesktopPath);
            }
            catch (Exception ex) { Log($"建樹失敗：{ex.Message}"); }
        }

        private bool IsExcludedFolderName(string name)
        {
            var auto = _cfg.Routing?.AutoFolderName ?? "自整理";
            var low = _cfg.Routing?.LowConfidenceFolderName ?? "信心不足";
            if (name.Equals("_blacklist", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals(auto, StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals(low, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private static bool IsHiddenOrSystem(string path)
        {
            try
            {
                var a = File.GetAttributes(path);
                return (a & FileAttributes.Hidden) != 0 || (a & FileAttributes.System) != 0;
            }
            catch { return false; }
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

                IEnumerable<string> subs;
                try { subs = Directory.EnumerateDirectories(baseDir); }
                catch { return; }

                foreach (var d in subs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var name = System.IO.Path.GetFileName(d);
                    if (IsHiddenOrSystem(d) || IsExcludedFolderName(name)) continue;

                    var child = new TreeViewItem { Header = name, Tag = d, ToolTip = d };
                    child.Items.Add(DummyNode);
                    child.Expanded += DirNode_Expanded;
                    node.Items.Add(child);
                }
            }
        }

        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = TvFolders.SelectedItem as TreeViewItem;
            var path = node?.Tag as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                _centerSource = CenterSource.Folder;
                _currentFolder = path!;
                ShowFolder(path!);
            }
        }

        private string? GetSelectedTreePath()
        {
            if (TvFolders.SelectedItem is TreeViewItem node)
                return node.Tag as string;
            return null;
        }

        // ===============================
        // 中清單：載入、排序、雙擊
        // ===============================
        private void ShowFolder(string folder)
        {
            try
            {
                _items.Clear();

                foreach (var f in Directory.EnumerateFiles(folder)
                                           .Where(f => !IsHiddenOrSystem(f))
                                           .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(f);
                    var it = new Item
                    {
                        Id = 0,
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

                    // 合併 DB 資料（避免 TryGetByPath 依賴）
                    var fromDb = _db?.QueryByPath(info.FullName).FirstOrDefault();
                    if (fromDb != null)
                    {
                        it.Id = fromDb.Id;
                        it.Project = fromDb.Project;
                        it.Category = fromDb.Category;
                        it.Tags = fromDb.Tags;
                        it.Status = fromDb.Status;
                        it.Confidence = fromDb.Confidence;
                        it.CreatedTs = fromDb.CreatedTs > 0 ? fromDb.CreatedTs : it.CreatedTs;
                    }

                    // 預計路徑（ProposedPath）
                    try { it.ProposedPath = _routing?.PreviewDestPath(it.Path!, _lockedProject) ?? ""; }
                    catch { it.ProposedPath = ""; }

                    _items.Add(it);
                }

                ApplySortAndBind();
                HideBanner();
                Log($"顯示資料夾：{folder}（{_items.Count}）");
            }
            catch (Exception ex)
            {
                ShowBanner($"讀取資料夾失敗：{ex.Message}", isWarn: true);
            }
        }

        private void ApplySortAndBind()
        {
            IEnumerable<Item> q = _items;

            q = (_sortColumn, _sortDesc) switch
            {
                ("Name", var d) => d ? q.OrderByDescending(x => x.Filename) : q.OrderBy(x => x.Filename),
                ("Ext", var d) => d ? q.OrderByDescending(x => x.Ext) : q.OrderBy(x => x.Ext),
                ("Project", var d) => d ? q.OrderByDescending(x => x.Project) : q.OrderBy(x => x.Project),
                ("Tag", var d) => d ? q.OrderByDescending(x => x.Tags) : q.OrderBy(x => x.Tags),
                ("Path", var d) => d ? q.OrderByDescending(x => x.Path) : q.OrderBy(x => x.Path),
                ("PredictedPath", var d) => d ? q.OrderByDescending(x => x.ProposedPath) : q.OrderBy(x => x.ProposedPath),
                ("CreatedTs", var d) => d ? q.OrderByDescending(x => x.CreatedTs) : q.OrderBy(x => x.CreatedTs),
                _ => q
            };

            FileList.ItemsSource = q.ToList();
            TxtCounter.Text = $"清單筆數：{_items.Count}";
        }

        private void ListHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader h && h.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            {
                if (_sortColumn == tag) _sortDesc = !_sortDesc;
                else { _sortColumn = tag; _sortDesc = false; }

                // 更新標頭指示
                var gv = (GridView)FileList.View;
                foreach (var col in gv.Columns)
                {
                    if (col.Header is GridViewColumnHeader ch && ch.Tag is string t)
                        ch.Content = t == _sortColumn ? $"{t}{(_sortDesc ? " ▼" : " ▲")}" : t;
                }
                ApplySortAndBind();
            }
        }

        private void List_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileList.SelectedItem is Item it)
            {
                if (!string.IsNullOrWhiteSpace(it.Path) && File.Exists(it.Path))
                {
                    try { Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true }); }
                    catch { }
                }
                else if (!string.IsNullOrWhiteSpace(it.Path) && Directory.Exists(it.Path))
                {
                    try { Process.Start(new ProcessStartInfo("explorer.exe", it.Path) { UseShellExecute = true }); }
                    catch { }
                }
            }
        }

        // ===============================
        // 工具列 / 右側區塊 動作
        // ===============================
        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null) return;

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

                ShowBanner($"已加入 {dlg.FileNames.Length} 個檔案，請執行「檢視分類」。");
            }
        }

        private async void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null || _db == null) return;

            var inbox = _db.QueryByStatus("inbox").ToList();
            int done = 0;
            foreach (var it in inbox)
            {
                try { await _intake.ClassifyOnlyAsync(it.Path!, CancellationToken.None); done++; }
                catch { }
            }
            ShowBanner($"預分類完成：{done} 筆。");
        }

        private async void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null) return;
            var moved = await _intake.CommitPendingAsync(CancellationToken.None);
            ShowBanner($"搬檔完成：{moved} 筆。");
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(this, _cfg) { Owner = this };
            if (win.ShowDialog() == true)
                ReloadConfig();
        }

        public void ReloadConfig()
        {
            _cfg = ConfigService.TryLoad(_cfgPath);
            _routing?.ApplyConfig(_cfg);
            _llm?.UpdateConfig(_cfg);
            _intake?.UpdateConfig(_cfg);

            RtThreshold.Value = _cfg.Classification.ConfidenceThreshold;
            RtThresholdValue.Text = $"{_cfg.Classification.ConfidenceThreshold:0.00}";
            RtBlacklist.Text = string.Join(", ", _cfg.Import.BlacklistFolderNames ?? Array.Empty<string>());

            StartFolderWatchers();
            BuildFolderTreeRoots();
            RefreshTopPaths();
            ShowBanner("已重新載入設定。");
        }

        private void BtnApplyAiTuning_Click(object sender, RoutedEventArgs e)
        {
            _cfg.Classification.ConfidenceThreshold = RtThreshold.Value;
            _cfg.Import.BlacklistFolderNames = (RtBlacklist.Text ?? "")
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).ToArray();

            ConfigService.Save(_cfgPath, _cfg);
            ShowBanner("AI 自動整理設定已套用。");
        }

        private void BtnReloadSettings_Click(object sender, RoutedEventArgs e) => ReloadConfig();

        // 左右收合
        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e)
        {
            LeftPaneColumn.Width = LeftPaneColumn.Width.Value < 1 ? new GridLength(280) : new GridLength(0);
        }
        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e)
        {
            RightPaneColumn.Width = RightPaneColumn.Width.Value < 1 ? new GridLength(320) : new GridLength(0);
        }

        // 收件夾快捷鈕
        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e) => OpenPath(_cfg.Import.HotFolderPath);

        // 目錄搜尋（預留：當前只做占位，不破壞 UI）
        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 之後若要在已展開節點內做即時過濾，可在此實作
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RebuildTreeAndList();

        // ===============================
        // TreeView 右鍵功能
        // ===============================
        private void Tree_OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var path = GetSelectedTreePath();
            if (!string.IsNullOrWhiteSpace(path)) OpenPath(path);
        }

        private async void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null) return;

            var basePath = GetSelectedTreePath();
            if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath)) return;

            var includeSub = _cfg.Import?.IncludeSubdir ?? true;
            var maxDepth = includeSub ? 5 : 0;

            var blacklistExts = (_cfg.Import?.BlacklistExts ?? Array.Empty<string>())
                .Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet();

            int count = 0;

            foreach (var f in EnumerateFiles(basePath!, maxDepth))
            {
                var ext = System.IO.Path.GetExtension(f).TrimStart('.').ToLowerInvariant();
                if (blacklistExts.Contains(ext)) continue;
                await _intake.StageOnlyAsync(f, CancellationToken.None);
                count++;
            }

            ShowBanner($"已加入 {count} 檔案，請執行「檢視分類」。");

            static IEnumerable<string> EnumerateFiles(string root, int depth)
            {
                var stack = new Stack<(string dir, int d)>();
                stack.Push((root, 0));
                while (stack.Count > 0)
                {
                    var (dir, d) = stack.Pop();
                    IEnumerable<string> fs;
                    try { fs = Directory.EnumerateFiles(dir); } catch { continue; }
                    foreach (var f in fs)
                    {
                        try
                        {
                            var a = File.GetAttributes(f);
                            if ((a & FileAttributes.Hidden) == 0 && (a & FileAttributes.System) == 0)
                                yield return f;
                        }
                        catch { }
                    }
                    if (d >= depth) continue;
                    IEnumerable<string> subs;
                    try { subs = Directory.EnumerateDirectories(dir); } catch { continue; }
                    foreach (var s in subs)
                    {
                        var name = System.IO.Path.GetFileName(s);
                        if (IsHiddenOrSystem(s) || name.StartsWith("_blacklist", StringComparison.OrdinalIgnoreCase)) continue;
                        stack.Push((s, d + 1));
                    }
                }
            }
        }

        private void Tree_LockProject_Click(object sender, RoutedEventArgs e)
        {
            var path = GetSelectedTreePath();
            if (string.IsNullOrWhiteSpace(path)) return;
            var name = System.IO.Path.GetFileName(path);
            _lockedProject = name;
            _cfg.App.ProjectLock = name;
            ConfigService.Save(_cfgPath, _cfg);
            ShowBanner($"🔒 已鎖定專案「{name}」");
        }

        private void Tree_UnlockProject_Click(object sender, RoutedEventArgs e)
        {
            _lockedProject = string.Empty;
            _cfg.App.ProjectLock = string.Empty;
            ConfigService.Save(_cfgPath, _cfg);
            ShowBanner("🔓 已解除專案鎖定");
        }

        private void Tree_Rename_Click(object sender, RoutedEventArgs e)
        {
            var path = GetSelectedTreePath();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

            var oldName = System.IO.Path.GetFileName(path);
            var newName = PromptText("重新命名資料夾", "輸入新名稱：", oldName);
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

            try
            {
                var parent = System.IO.Path.GetDirectoryName(path)!;
                var newPath = System.IO.Path.Combine(parent, newName);
                Directory.Move(path, newPath);
                ShowBanner($"已重新命名資料夾：{newName}");
                RebuildTreeAndList();
            }
            catch (Exception ex) { ShowBanner($"重新命名失敗：{ex.Message}", isWarn: true); }
        }

        // 簡易輸入對話框
        private static string PromptText(string title, string msg, string? defaultValue = null)
        {
            var win = new Window
            {
                Title = title,
                Width = 420,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tbMsg = new TextBlock { Text = msg, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(tbMsg, 0);
            var txt = new TextBox { Text = defaultValue ?? "" };
            Grid.SetRow(txt, 1);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "確定", Width = 80, Margin = new Thickness(6, 0, 0, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 80, Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
            panel.Children.Add(cancel); panel.Children.Add(ok);
            Grid.SetRow(panel, 2);

            grid.Children.Add(tbMsg); grid.Children.Add(txt); grid.Children.Add(panel);
            win.Content = grid;

            string result = defaultValue ?? "";
            ok.Click += (_, __) => { result = txt.Text; win.DialogResult = true; };
            cancel.Click += (_, __) => { win.DialogResult = false; };
            win.ShowDialog();
            return result;
        }
    }

    // 最小 RelayCommand
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _exec;
        private readonly Func<T?, bool>? _can;
        public RelayCommand(Action<T?> exec, Func<T?, bool>? can = null) { _exec = exec; _can = can; }
        public bool CanExecute(object? parameter) => _can?.Invoke((T?)parameter) ?? true;
        public void Execute(object? parameter) => _exec((T?)parameter);
        public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    }
}
