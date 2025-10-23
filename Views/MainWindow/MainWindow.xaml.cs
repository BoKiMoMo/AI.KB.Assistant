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
        // ====== Commands (快捷鍵) ======
        public ICommand OpenInboxCommand => new RoutedUICommand();
        public ICommand PrimaryActionCommand => new RoutedUICommand();
        public ICommand ToggleInfoPaneCommand => new RoutedUICommand();
        public ICommand ToggleTreePaneCommand => new RoutedUICommand();

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

        private static readonly object DummyNode = new();
        private bool _isReady;
        private List<string> _currentLevelDirs = new();

        // 折疊狀態
        private bool _leftCollapsed;
        private bool _rightCollapsed;
        // 避免 Log 切換造成重入
        private bool _togglingLog;

        // 記住目前中心清單正在顯示的來源（主頁 = 檔案系統資料夾；其他 = DB 篩選）
        private enum CenterSource { Folder, StatusTab }
        private CenterSource _centerSource = CenterSource.Folder;
        private string _currentFolder = string.Empty; // 當前資料夾（回到主頁面時維持）

        public MainWindow()
        {
            InitializeComponent();

            // 綁定 Commands
            CommandBindings.Add(new CommandBinding(OpenInboxCommand, CmdOpenInbox_Executed));
            CommandBindings.Add(new CommandBinding(PrimaryActionCommand, CmdPrimaryAction_Executed));
            CommandBindings.Add(new CommandBinding(ToggleInfoPaneCommand, CmdToggleInfo_Executed));
            CommandBindings.Add(new CommandBinding(ToggleTreePaneCommand, CmdToggleTree_Executed));

            _cfgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;

            RtThreshold.ValueChanged += (s, e) => { RtThresholdValue.Text = $"{RtThreshold.Value:0.00}"; };
        }

        // ====== Life cycle ======
        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _cfg = ConfigService.TryLoad(_cfgPath);
            _ui = UiStateService.Load();

            _db = new DbService(_cfg.App.DbPath);
            _routing = new RoutingService(_cfg);
            _llm = new LlmService(_cfg);
            _intake = new IntakeService(_db, _routing, _llm, _cfg);

            _lockedProject = _cfg.App.ProjectLock ?? string.Empty;
            RefreshProjectCombo();

            ApplyUiState();

            RtThreshold.Value = _cfg.Classification.ConfidenceThreshold;
            RtBlacklist.Text = string.Join(", ", _cfg.Import.BlacklistFolderNames ?? Array.Empty<string>());

            BuildFolderTreeRoots();

            MainTabs.SelectedIndex = 0;
            _isReady = true;

            // 初始顯示：主頁面（資料夾）
            var startFolder = !string.IsNullOrWhiteSpace(_ui.LastFolder) && Directory.Exists(_ui.LastFolder)
                ? _ui.LastFolder
                : (!string.IsNullOrWhiteSpace(_cfg.App.RootDir) && Directory.Exists(_cfg.App.RootDir)
                    ? _cfg.App.RootDir
                    : null);

            if (!string.IsNullOrWhiteSpace(startFolder))
            {
                _centerSource = CenterSource.Folder;
                _currentFolder = startFolder!;
                ShowFolder(startFolder!);
                BuildBreadcrumb(startFolder!);
            }
            else
            {
                _centerSource = CenterSource.StatusTab;
                RefreshList("home");
                ShowBanner("拖放檔案到視窗，或點「＋加入」開始。");
            }

            Log("系統已就緒。");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try { CaptureUiState(); UiStateService.Save(_ui); } catch { }
            try { _cts.Cancel(); } catch { }
            try { _db?.Dispose(); } catch { }
            try { _llm?.Dispose(); } catch { }
        }

        // ====== UI 狀態套用 / 取回 ======
        private static double Clamp(double value, double min, double def)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min) return def;
            return value;
        }

        private void ApplyUiState()
        {
            try
            {
                // 左/右欄寬度做 clamp，避免載到 0 或異常值把 UI 擠壞
                var leftW = Clamp(_ui.LeftWidth, 160, 280);
                var rightW = Clamp(_ui.RightWidth, 260, 360);
                var logH = Clamp(_ui.LogHeight, 60, 110);

                _leftCollapsed = _ui.LeftCollapsed;
                _rightCollapsed = _ui.RightCollapsed;

                LeftPaneColumn.Width = _leftCollapsed ? new GridLength(0) : new GridLength(leftW);
                RightPaneColumn.Width = _rightCollapsed ? new GridLength(0) : new GridLength(rightW);

                LogExpander.IsExpanded = _ui.LogExpanded;
                LogBox.Height = logH;

                // 還原 GridView 欄寬
                var gv = MainGridView;
                if (gv != null && _ui.ColumnWidths != null && _ui.ColumnWidths.Count > 0)
                {
                    foreach (var c in gv.Columns)
                    {
                        var key = c.Header?.ToString() ?? "";
                        if (_ui.ColumnWidths.TryGetValue(key, out var w))
                            c.Width = Clamp(w, 40, c.Width); // 最小 40
                    }
                }
            }
            catch { }
        }

        private void CaptureUiState()
        {
            try
            {
                _ui.LeftCollapsed = LeftPaneColumn.Width.Value < 1;
                _ui.RightCollapsed = RightPaneColumn.Width.Value < 1;
                if (!_ui.LeftCollapsed) _ui.LeftWidth = LeftPaneColumn.Width.Value;
                if (!_ui.RightCollapsed) _ui.RightWidth = RightPaneColumn.Width.Value;
                _ui.LogExpanded = LogExpander.IsExpanded;
                _ui.LogHeight = LogBox.ActualHeight > 40 ? LogBox.ActualHeight : _ui.LogHeight;

                var gv = MainGridView;
                if (gv != null)
                {
                    _ui.ColumnWidths ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in gv.Columns)
                    {
                        var key = c.Header?.ToString() ?? "";
                        _ui.ColumnWidths[key] = c.Width;
                    }
                }

                // 記錄當前資料夾（若目前顯示的是資料夾）
                if (_centerSource == CenterSource.Folder && !string.IsNullOrWhiteSpace(_currentFolder))
                {
                    _ui.LastFolder = _currentFolder;
                }
            }
            catch { }
        }

        // ====== 小工具 ======
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
            }
            catch { }
        }

        private void ShowBanner(string message, bool warn = false)
        {
            try
            {
                Banner.Background = warn
                    ? (Brush)FindResource("App.BannerErrorBrush")
                    : (Brush)FindResource("App.BannerInfoBrush");
                Banner.BorderBrush = (Brush)FindResource("App.BorderBrush");
            }
            catch
            {
                // 保底：硬編碼
                Banner.Background = warn ? new SolidColorBrush(Color.FromRgb(254, 226, 226))
                                         : new SolidColorBrush(Color.FromRgb(219, 234, 254));
                Banner.BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219));
            }
            BannerText.Text = message;
            Banner.Visibility = Visibility.Visible;
        }

        private void HideBanner() => Banner.Visibility = Visibility.Collapsed;

        private void RefreshProjectCombo()
        {
            try
            {
                if (_db == null) return;
                var all = _db.QueryDistinctProjects().ToList();
                CbLockProject.ItemsSource = all;
                _lockedProject = _cfg.App.ProjectLock ?? string.Empty;
                CbLockProject.Text = _lockedProject;
            }
            catch { }
        }

        private void UpdateCounters()
        {
            try
            {
                var selCount = GetSelection().Count();
                TxtCounter.Text = $"清單筆數：{_items.Count}；選取：{selCount}";
                BatchBar.Visibility = selCount > 1 ? Visibility.Visible : Visibility.Collapsed;

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

        private static string NormalizeCsv(string raw)
        {
            return string.Join(",",
                (raw ?? string.Empty)
                    .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        // ====== 清單刷新（狀態分頁） ======
        private string CurrentTabTag()
        {
            if (MainTabs.SelectedItem is TabItem ti && ti.Tag is string tag) return tag;
            return "home";
        }

        private void RefreshList(string statusFilter)
        {
            if (!_isReady || _db == null) return;

            _centerSource = CenterSource.StatusTab;
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

            if (!_items.Any())
                ShowBanner("目前沒有資料。拖放檔案到視窗，或點「＋加入」。");
            else
                HideBanner();

            Log($"清單已更新（{statusFilter}）");
        }

        // ====== 匯入 / 分類 / 搬檔 ======
        private async void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null || _db == null) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            // 僅進行『預分類』：把 inbox 依規則產生 Route，並將結果寫入 autosort-staging
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
                catch (Exception ex)
                {
                    Log($"預分類失敗：{ex.Message}");
                }
            }

            // 顯示待確認分頁
            MainTabs.SelectedIndex = 5; // autosort-staging tab
            RefreshList("autosort-staging");
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

                    // 加入 Inbox 後，回首頁並提示
                    MainTabs.SelectedIndex = 0;
                    RefreshList("home");
                    ShowBanner($"加入 {dlg.FileNames.Length} 筆到 Inbox，請執行『檢視分類』以預覽搬檔路徑。");
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

            if (_centerSource == CenterSource.Folder && Directory.Exists(_currentFolder))
            {
                ShowFolder(_currentFolder);
            }
            else
            {
                RefreshList(CurrentTabTag());
            }
        }

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
            catch (Exception ex) { Log($"開啟收件夾失敗：{ex.Message}"); }
        }

        // ====== 清單：操作 ======
        private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (FileList?.ContextMenu is not ContextMenu cm) return;

            // 右鍵先啟用/停用
            cm.IsEnabled = GetSelection().Any();
            UpdateCounters();

            // 找到「設定標籤」這個父 MenuItem（內含一個 placeholder 的 TagsSubmenu）
            MenuItem? tagsParent = null;
            foreach (var obj in cm.Items)
            {
                if (obj is MenuItem mi && mi.Header is string h && h.Contains("設定標籤"))
                {
                    tagsParent = mi;
                    break;
                }
                // 若 Header 是 StackPanel（我們在 XAML 用 StackPanel 包 TextBlock）
                if (obj is MenuItem mi2 && mi2.Header is StackPanel)
                {
                    // 大概就是它：內有 Items
                    if (mi2.Items.Count > 0) tagsParent = mi2;
                }
            }
            if (tagsParent == null) return;

            // 重建子選單：常用標籤 + 快捷標籤
            tagsParent.Items.Clear();

            var defaults = new[] { "我的最愛", "處理中", "待處理" };
            var fromList = _items
                .SelectMany(i => (i.Tags ?? "")
                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Tag = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(12)
                .Select(x => x.Tag);

            var tagPool = defaults
                .Concat(fromList)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tagPool.Count == 0)
            {
                tagsParent.Items.Add(new MenuItem { Header = "（目前沒有可用標籤）", IsEnabled = false });
                // 仍然提供「自訂…」
                tagsParent.Items.Add(new Separator());
                var custom = new MenuItem { Header = "自訂（輸入或貼上）…" };
                custom.Click += (_, __) => CtxSetTags_Click(sender, new RoutedEventArgs());
                tagsParent.Items.Add(custom);
                return;
            }

            foreach (var t in tagPool)
            {
                var mi = new MenuItem { Header = t };
                mi.Click += (_, __) => ToggleTagForSelection(t);
                tagsParent.Items.Add(mi);
            }

            tagsParent.Items.Add(new Separator());

            // 自訂（輸入多個，逗號分隔）
            var customAdd = new MenuItem { Header = "自訂新增…" };
            customAdd.Click += (_, __) => CtxSetTags_Click(sender, new RoutedEventArgs());
            tagsParent.Items.Add(customAdd);

            // 批次移除（打勾列表）
            var removePanel = new MenuItem { Header = "批次移除…" };
            removePanel.Click += (_, __) => ShowRemoveTagsDialog();
            tagsParent.Items.Add(removePanel);
        }

        private void ToggleTagForSelection(string tag)
        {
            if (_db == null) return;
            var sel = GetSelection().ToList();
            if (sel.Count == 0) return;

            int add = 0, remove = 0;
            foreach (var it in sel)
            {
                var tags = (it.Tags ?? "");
                var set = new HashSet<string>(
                    tags.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                if (set.Contains(tag))
                {
                    set.RemoveWhere(s => s.Equals(tag, StringComparison.OrdinalIgnoreCase));
                    remove++;
                }
                else
                {
                    set.Add(tag);
                    add++;
                }

                var newTags = string.Join(",", set.Where(s => !string.IsNullOrWhiteSpace(s)));
                it.Tags = newTags;
                _db.UpdateTags(it.Id, newTags);
            }

            Log($"標籤「{tag}」已套用：新增 {add} 筆、移除 {remove} 筆。");
            RefreshAfterEdit();
        }

        private void ShowRemoveTagsDialog()
        {
            var sel = GetSelection().ToList();
            if (sel.Count == 0)
            {
                Log("請先選取檔案再進行批次移除標籤。");
                return;
            }

            // 彙整目前選取檔案的所有標籤
            var allTags = sel
                .SelectMany(i => (i.Tags ?? "")
                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (allTags.Count == 0)
            {
                Log("選取的檔案沒有任何標籤可移除。");
                return;
            }

            var dlg = new SelectTagsDialog("批次移除標籤", "勾選要移除的標籤：", allTags);
            if (dlg.ShowDialog() == true)
            {
                var toRemove = dlg.Selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (toRemove.Count == 0) return;

                foreach (var it in sel)
                {
                    var keep = (it.Tags ?? "")
                        .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(t => !toRemove.Contains(t));
                    var newTags = string.Join(",", keep);
                    it.Tags = newTags;
                    _db?.UpdateTags(it.Id, newTags);
                }
                Log($"已批次移除 {toRemove.Count} 個標籤。");
                RefreshAfterEdit();
            }
        }

        private void RefreshAfterEdit()
        {
            if (_centerSource == CenterSource.Folder && Directory.Exists(_currentFolder))
            {
                ShowFolder(_currentFolder);
            }
            else
            {
                RefreshList(CurrentTabTag());
            }
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
                RefreshAfterEdit();
                RefreshProjectCombo();
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
                var tags = NormalizeCsv(box.Value?.Trim() ?? "");
                foreach (var it in sel)
                {
                    it.Tags = tags;
                    _db.UpdateTags(it.Id, tags);
                }
                Log($"已更新標籤（{sel.Count} 筆）");
                RefreshAfterEdit();
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
                MainTabs.SelectedIndex = 5; // 使用者多半要檢視分類
                RefreshList("autosort-staging");
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

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCounters();
            HideBanner();
        }

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

        // ====== 左/右欄收合 & 底部訊息列 ======
        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e)
        {
            _leftCollapsed = LeftPaneColumn.Width.Value >= 1;
            var target = _leftCollapsed ? 0 : Clamp(_ui.LeftWidth, 160, 280);
            LeftPaneColumn.Width = new GridLength(target);
        }

        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e)
        {
            _rightCollapsed = RightPaneColumn.Width.Value >= 1;
            var target = _rightCollapsed ? 0 : Clamp(_ui.RightWidth, 260, 360);
            RightPaneColumn.Width = new GridLength(target);
        }

        private void BtnToggleLog_Click(object sender, RoutedEventArgs e)
        {
            if (_togglingLog) return;      // ★ 防重入
            _togglingLog = true;
            try
            {
                e.Handled = true;
                LogExpander.IsExpanded = !(LogExpander?.IsExpanded ?? false);
            }
            finally { _togglingLog = false; }
        }

        // ====== 拖放 ======
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (_intake == null) return;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var f in files)
                    await _intake.StageOnlyAsync(f, CancellationToken.None);

                Log($"拖放加入 {files.Length} 筆至 Inbox");
                MainTabs.SelectedIndex = 5;      // 直接引導去「待確認」
                RefreshList("autosort-staging");
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        // ====== 路徑樹 & 篩選 ======
        private void BuildFolderTreeRoots()
        {
            try
            {
                TvFolders.Items.Clear();
                _currentLevelDirs.Clear();

                // [資料庫] = RootDir（不顯示路徑，只顯示標題）
                if (!string.IsNullOrWhiteSpace(_cfg.App.RootDir) && Directory.Exists(_cfg.App.RootDir))
                    TvFolders.Items.Add(CreateDirNode(_cfg.App.RootDir, "資料庫"));

                // [收件夾] 捷徑
                var inbox = string.IsNullOrWhiteSpace(_cfg.Import.HotFolderPath)
                    ? Path.Combine(_cfg.App.RootDir ?? string.Empty, "_Inbox")
                    : _cfg.Import.HotFolderPath;
                if (!string.IsNullOrWhiteSpace(inbox))
                {
                    if (!Directory.Exists(inbox))
                    {
                        try { Directory.CreateDirectory(inbox); } catch { }
                    }
                    if (Directory.Exists(inbox))
                        TvFolders.Items.Add(CreateDirNode(inbox, "收件夾"));
                }

                // [桌面] 捷徑
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (Directory.Exists(desktop))
                    TvFolders.Items.Add(CreateDirNode(desktop, "桌面"));

                // 其他磁碟（可選）
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    TvFolders.Items.Add(CreateDirNode(drive.RootDirectory.FullName, drive.Name));
            }
            catch (Exception ex) { Log($"建立路徑樹失敗：{ex.Message}"); }
        }

        private TreeViewItem CreateDirNode(string path, string? headerOverride = null)
        {
            var name = headerOverride ?? (string.IsNullOrEmpty(System.IO.Path.GetFileName(path)) ? path : System.IO.Path.GetFileName(path));
            var node = new TreeViewItem { Header = name, Tag = path, ToolTip = path };
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
                    _currentLevelDirs = subdirs.ToList();
                    PopulateDirs(node, _currentLevelDirs);
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

        private void PopulateDirs(TreeViewItem node, IEnumerable<string> dirs)
        {
            node.Items.Clear();
            foreach (var dir in dirs)
            {
                node.Items.Add(CreateDirNode(dir));
            }
        }

        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = TvFolders.SelectedItem as TreeViewItem;
            if (node == null) return;

            var path = node.Tag as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                _centerSource = CenterSource.Folder;
                _currentFolder = path;
                _ui.LastFolder = path;
                MainTabs.SelectedIndex = 0; // 切回首頁頁籤，但維持顯示資料夾
                ShowFolder(path);
                BuildBreadcrumb(path);
            }
        }

        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var keyword = (TvFilterBox.Text ?? "").Trim();
            var node = TvFolders.SelectedItem as TreeViewItem;
            if (node == null || node.Items.Count == 0)
                node = TvFolders.Items.Count > 0 ? TvFolders.Items[0] as TreeViewItem : null;
            if (node == null) return;

            var baseDir = node.Tag as string;
            IEnumerable<string> dirs;
            try
            {
                if (_currentLevelDirs.Count == 0 && !string.IsNullOrWhiteSpace(baseDir) && Directory.Exists(baseDir))
                {
                    _currentLevelDirs = Directory.EnumerateDirectories(baseDir)
                                                 .Where(d => !IsHiddenOrSystem(d))
                                                 .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                                                 .ToList();
                }
                dirs = _currentLevelDirs;
            }
            catch { return; }

            if (!string.IsNullOrWhiteSpace(keyword))
                dirs = dirs.Where(d => Path.GetFileName(d).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

            PopulateDirs(node, dirs);
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
                        Project = string.Empty,
                        Category = string.Empty,
                        Confidence = 0,
                        CreatedTs = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds(),
                        Status = string.Empty,
                        Path = info.FullName,
                        Tags = string.Empty
                    };

                    // 合併 DB 的標籤/專案（若存在）
                    var fromDb = _db?.TryGetByPath(info.FullName);
                    if (fromDb != null)
                    {
                        item.Id = fromDb.Id;
                        item.Project = fromDb.Project;
                        item.Category = fromDb.Category;
                        item.Tags = fromDb.Tags;
                        item.Status = fromDb.Status;
                        item.Confidence = fromDb.Confidence;
                        item.CreatedTs = fromDb.CreatedTs > 0 ? fromDb.CreatedTs : item.CreatedTs;
                    }

                    _items.Add(item);
                }

                BindAndRefreshList();

                if (files.Count == 0)
                    ShowBanner("此資料夾目前沒有檔案。");
                else
                    HideBanner();

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
                        Tag = p,
                        Style = (Style)FindResource("TonedBtn")
                    };
                    btn.Click += (s, _) =>
                    {
                        var path = (s as Button)?.Tag as string;
                        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                        {
                            MainTabs.SelectedIndex = 0;
                            _centerSource = CenterSource.Folder;
                            _currentFolder = path;
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

        // ====== 分頁（只處理 TabControl 自己的變更） ======
        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isReady) return;
            if (e.OriginalSource is not TabControl) return;

            var tag = CurrentTabTag();
            if (tag == "home")
            {
                if (_centerSource == CenterSource.Folder && Directory.Exists(_currentFolder))
                {
                    ShowFolder(_currentFolder);
                }
                else
                {
                    RefreshList("home");
                }
            }
            else
            {
                RefreshList(tag);
            }
        }

        // ====== 設定視窗 / 右欄設定 ======
        public void ReloadConfig()
        {
            _cfg = ConfigService.TryLoad(_cfgPath);
            _routing?.ApplyConfig(_cfg);
            _llm?.UpdateConfig(_cfg);
            _intake?.UpdateConfig(_cfg);
            RefreshProjectCombo();

            RtThreshold.Value = _cfg.Classification.ConfidenceThreshold;
            RtBlacklist.Text = string.Join(", ", _cfg.Import.BlacklistFolderNames ?? Array.Empty<string>());

            BuildFolderTreeRoots();
            Log("已重新載入設定。");
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(this, _cfg) { Owner = this };
            if (win.ShowDialog() == true)
                ReloadConfig();
        }

        // ====== LLM 協助 ======
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
                    CbLockProject.Text = string.Empty;
                    try { ConfigService.Save(_cfgPath, _cfg); } catch { }
                }
            }
            // 觸發 Banner 綁定更新
            HideBanner();
        }

        private void CbLockProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 僅同步目前選取的文字，不強制寫回 config（由鎖定按鈕負責）
            // 讓 Banner 能顯示 MultiBinding 的目前值
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

        // ====== 快捷鍵命令對應 ======
        private void CmdOpenInbox_Executed(object sender, ExecutedRoutedEventArgs e) => OpenInbox();

        private async void CmdPrimaryAction_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var current = CurrentTabTag();
            if (string.Equals(current, "autosort-staging", StringComparison.OrdinalIgnoreCase))
                BtnCommit_Click(sender, e);
            else
                BtnStartClassify_Click(sender, e);
            await Task.CompletedTask;
        }

        private void CmdToggleInfo_Executed(object sender, ExecutedRoutedEventArgs e) => BtnEdgeRight_Click(sender, e);
        private void CmdToggleTree_Executed(object sender, ExecutedRoutedEventArgs e) => BtnEdgeLeft_Click(sender, e);

        // ====== 內嵌簡易對話框們 ======
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

        internal sealed class SelectTagsDialog : Window
        {
            private readonly ListBox _lb;
            public IEnumerable<string> Selected => _lb.SelectedItems.Cast<string>();

            public SelectTagsDialog(string title, string prompt, IEnumerable<string> tags)
            {
                Title = title;
                Width = 420;
                Height = 380;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ResizeMode = ResizeMode.CanMinimize;
                Owner = Application.Current?.Windows?.OfType<Window>()?.FirstOrDefault(w => w.IsActive);

                var root = new DockPanel { Margin = new Thickness(12) };
                root.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });

                _lb = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 260 };
                foreach (var t in tags) _lb.Items.Add(t);
                DockPanel.SetDock(_lb, Dock.Top);
                root.Children.Add(_lb);

                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
                var ok = new Button { Content = "確定", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
                var cancel = new Button { Content = "取消", Width = 80 };
                ok.Click += (_, __) => { DialogResult = true; Close(); };
                cancel.Click += (_, __) => { DialogResult = false; Close(); };
                row.Children.Add(ok);
                row.Children.Add(cancel);

                DockPanel.SetDock(row, Dock.Bottom);
                root.Children.Add(row);

                Content = root;
            }
        }

        // ====== TreeView 右鍵功能 ======
        private string? GetSelectedTreePath()
        {
            var node = TvFolders.SelectedItem as TreeViewItem;
            var path = node?.Tag as string;
            return (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) ? path : null;
        }

        private void Tree_OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var path = GetSelectedTreePath();
            if (path == null) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                Log($"已在檔案總管開啟：{path}");
            }
            catch (Exception ex) { Log($"開啟失敗：{ex.Message}"); }
        }

        private async void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e)
        {
            if (_intake == null) return;

            var path = GetSelectedTreePath();
            if (path == null) return;

            int count = 0;
            try
            {
                var files = Directory.EnumerateFiles(path)
                    .Where(f =>
                    {
                        try
                        {
                            var attr = File.GetAttributes(f);
                            return (attr & FileAttributes.Hidden) == 0 && (attr & FileAttributes.System) == 0;
                        }
                        catch { return false; }
                    })
                    .ToList();

                foreach (var f in files)
                {
                    await _intake.StageOnlyAsync(f, CancellationToken.None);
                    count++;
                }

                Log($"已將此資料夾檔案加入收件夾：{count} 筆");
                MainTabs.SelectedIndex = 5;
                RefreshList("autosort-staging");
            }
            catch (Exception ex) { Log($"加入收件夾失敗：{ex.Message}"); }
        }

        private void Tree_LockProject_Click(object sender, RoutedEventArgs e)
        {
            var path = GetSelectedTreePath();
            if (path == null) return;

            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name)) name = path;

            _lockedProject = name;
            _cfg.App.ProjectLock = name;
            CbLockProject.Text = name;
            try { ConfigService.Save(_cfgPath, _cfg); } catch { }
            Log($"🔒 已鎖定專案「{name}」");
        }

        private void Tree_UnlockProject_Click(object sender, RoutedEventArgs e)
        {
            _lockedProject = string.Empty;
            _cfg.App.ProjectLock = string.Empty;
            CbLockProject.Text = string.Empty;
            try { ConfigService.Save(_cfgPath, _cfg); } catch { }
            Log("🔓 已解除專案鎖定");
        }

        private void Tree_Rename_Click(object sender, RoutedEventArgs e)
        {
            var path = GetSelectedTreePath();
            if (path == null) return;

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
    }
}