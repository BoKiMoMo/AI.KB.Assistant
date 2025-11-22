using AI.KB.Assistant.Common;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V20.13.30 (掃描邏輯最終穩定版)
    /// 1. [Fix Scan All Empty] 確保在 ScanLogic 前強制清空 _selPath (解決 TreeView 選取造成的過度過濾)。
    /// 2. 修正 ApplyListFilters 中的邏輯，確保非 Inbox Mode 且無選取路徑時，仍能顯示所有檔案。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<UiRow> _rows = new();
        private ListCollectionView? _view;
        private T? Get<T>(string k) where T : class => Application.Current?.Resources[k] as T;
        private readonly MainWindowViewModel _vm;

        // State
        private string _sortKey = "CreatedAt";
        private ListSortDirection _sortDir = ListSortDirection.Descending;
        private string _searchKeyword = "", _currentTabTag = "home", _selPath = "", _projFilter = "";
        private bool _showDest = false, _hideCommitted = false, _isLoading = false;
        private bool _isShallowView = false;

        // Static Properties
        public static IValueConverter StatusToLabelConverterInstance { get; } = new StatusToLabelConverter();
        public static IMultiValueConverter StatusToBrushConverterInstance { get; } = new StatusToBrushConverter();

        public sealed class StatusToLabelConverter : IValueConverter
        {
            public object Convert(object v, Type t, object p, CultureInfo c) => MainWindow.StatusToLabel(v as string);
            public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
        }
        public sealed class StatusToBrushConverter : IMultiValueConverter
        {
            public object Convert(object[] v, Type t, object p, CultureInfo c) => (v[0] as string ?? "").ToLower() switch { "committed" => new SolidColorBrush(Color.FromRgb(52, 168, 83)), "error" => new SolidColorBrush(Color.FromRgb(234, 67, 53)), "blacklisted" => new SolidColorBrush(Color.FromRgb(234, 67, 53)), _ when (v[0] as string)!.StartsWith("stage") => new SolidColorBrush(Color.FromRgb(244, 180, 0)), _ => new SolidColorBrush(Color.FromRgb(154, 160, 166)) };
            public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
        }

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainWindowViewModel(
                Get<DbService>("Db")!,
                Get<RoutingService>("Router")!,
                Get<LlmService>("Llm")!,
                Get<HotFolderService>("HotFolder")!,
                ConfigService.Cfg);

            MainList.ItemsSource = _rows;
            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
            ApplySort(_sortKey, _sortDir);

            ConfigService.ConfigChanged += (c) => Dispatcher.InvokeAsync(async () => {
                RefreshServices(c);
                await RefreshFromDbAsync();
                LoadFolderRoot(c);
            });

            if (Get<HotFolderService>("HotFolder") is var hf && hf != null)
                hf.FilesChanged += () => Dispatcher.InvokeAsync(RefreshFromDbAsync);

            Loaded += async (_, __) => { await InitWeb(); LoadFolderRoot(); await RefreshFromDbAsync(); };
        }

        private void RefreshServices(AppConfig c) { try { Get<RoutingService>("Router")?.ApplyConfig(c); Get<LlmService>("Llm")?.UpdateConfig(c); } catch { } }
        private async Task InitWeb() { try { if (RtPreviewWebView != null) await RtPreviewWebView.EnsureCoreWebView2Async(null); } catch { } }
        public void Log(string m) { if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Log(m)); return; } LogBox?.AppendText($"[{DateTime.Now:HH:mm:ss}] {m}\n"); LogBox?.ScrollToEnd(); }
        private void TxtCounterSafe(string t) { if (FindName("TxtCounter") is TextBlock b) b.Text = t; }

        // ===== Core Logic =====
        public async Task RefreshFromDbAsync()
        {
            await RunBusyAsync("讀取中...", async () =>
            {
                _rows.Clear();
                var (items, cfg) = await _vm.GetDatabaseItemsAsync();
                var router = Get<RoutingService>("Router");
                if (router == null) return;

                foreach (var it in items.Where(x => !string.IsNullOrWhiteSpace(x.Path)))
                {
                    if (string.IsNullOrWhiteSpace(it.ProposedPath)) it.ProposedPath = router.PreviewDestPath(it.Path);
                    var ext = System.IO.Path.GetExtension(it.Path);
                    _rows.Add(new UiRow(it, router.MapExtensionToCategoryConfig(ext, cfg)));
                }

                UpdateProjectList();
                ApplySort(_sortKey, _sortDir);
                ApplyListFilters();
            }, showErr: false);
        }

        private void UpdateProjectList()
        {
            try
            {
                if (CmbSearchProject == null) return;
                var cur = CmbSearchProject.SelectedItem as string;
                var list = _rows.Select(r => r.Project).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
                list.Insert(0, "[所有專案]");
                CmbSearchProject.ItemsSource = list;

                if (!string.IsNullOrWhiteSpace(cur) && list.Contains(cur))
                {
                    CmbSearchProject.SelectedItem = cur;
                    if (BtnLockProject?.IsChecked != true) _projFilter = (cur == "[所有專案]") ? "" : cur;
                }
                else
                {
                    CmbSearchProject.SelectedIndex = 0;
                    _projFilter = "";
                }
            }
            catch { }
        }

        private void ApplyListFilters()
        {
            if (_view == null) return;
            var cfg = ConfigService.Cfg;
            string hot = cfg?.Import?.HotFolder ?? "";
            bool hasHot = !string.IsNullOrWhiteSpace(hot);
            string fullHot = hasHot ? System.IO.Path.GetFullPath(hot).TrimEnd(System.IO.Path.DirectorySeparatorChar) : "";
            // isInbox 判斷：僅當沒有選取 TreeView 路徑且在 Home Tab 時
            bool isInbox = string.IsNullOrEmpty(_selPath) && _currentTabTag == "home";

            _view.Filter = (o) =>
            {
                if (o is not UiRow r) return false;

                // 1. Inbox Mode (收件夾檢視)
                if (isInbox)
                {
                    if (string.Equals(r.Status, "committed", StringComparison.OrdinalIgnoreCase)) return false;
                    if (hasHot && !string.IsNullOrWhiteSpace(r.SourcePath))
                    {
                        try
                        {
                            string rowPath = System.IO.Path.GetFullPath(r.SourcePath);
                            if (!rowPath.StartsWith(fullHot, StringComparison.OrdinalIgnoreCase)) return false;

                            // 淺層過濾：排除子資料夾
                            if (_isShallowView)
                            {
                                string rowDir = System.IO.Path.GetDirectoryName(rowPath)?.TrimEnd(System.IO.Path.DirectorySeparatorChar) ?? "";
                                if (!rowDir.Equals(fullHot, StringComparison.OrdinalIgnoreCase)) return false;
                            }
                        }
                        catch { return false; }
                    }
                    else return false;
                }
                else if (_hideCommitted && string.Equals(r.Status, "committed", StringComparison.OrdinalIgnoreCase)) return false;

                // 2. Tabs
                if (!isInbox || _currentTabTag != "home")
                {
                    var tags = r.Item.Tags ?? new List<string>();
                    bool m = _currentTabTag switch
                    {
                        "fav" => tags.Contains("Favorite", StringComparer.OrdinalIgnoreCase),
                        "progress" => tags.Contains("InProgress", StringComparer.OrdinalIgnoreCase),
                        "backlog" => tags.Contains("Backlog", StringComparer.OrdinalIgnoreCase),
                        "pending" => tags.Contains("Pending", StringComparer.OrdinalIgnoreCase),
                        "recent" => r.CreatedAt.ToUniversalTime() >= DateTime.Now.ToUniversalTime().AddDays(-3),
                        _ => true
                    };
                    if (!m) return false;
                }

                // 3. Project
                if (!string.IsNullOrWhiteSpace(_projFilter) && !string.Equals(r.Project, _projFilter, StringComparison.OrdinalIgnoreCase)) return false;

                // 4. Folder (Tree Mode) - 僅在非 Inbox Mode 且 _selPath 有值時啟用
                if (!isInbox && !string.IsNullOrWhiteSpace(_selPath))
                {
                    if (string.IsNullOrWhiteSpace(r.SourcePath)) return false;
                    try
                    {
                        // 這裡執行嚴格的比對，只顯示選取資料夾內的檔案，不顯示子資料夾的檔案
                        var p = System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(r.SourcePath)!).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                        if (!p.Equals(System.IO.Path.GetFullPath(_selPath).TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return false;
                    }
                    catch { return false; }
                }

                // 5. Search
                if (!string.IsNullOrWhiteSpace(_searchKeyword))
                {
                    var k = _searchKeyword;
                    var comp = StringComparison.OrdinalIgnoreCase;
                    bool nameMatch = r.FileName.IndexOf(k, comp) >= 0;
                    bool tagMatch = r.Item.Tags?.Any(t => t.IndexOf(k, comp) >= 0) ?? false;
                    bool projMatch = r.Project.IndexOf(k, comp) >= 0;
                    bool pathMatch = r.SourcePath.IndexOf(k, comp) >= 0;
                    if (!nameMatch && !tagMatch && !projMatch && !pathMatch) return false;
                }
                return true;
            };
            _view.Refresh();
            TxtCounterSafe($"顯示: {_view.Count} / {_rows.Count}");
        }

        // ===== Unified Busy/Error Handler =====
        private async Task RunBusyAsync(string msg, Func<Task> action, bool showErr = true)
        {
            if (_isLoading) return;
            SetLoading(true, msg);
            try { await action(); }
            catch (Exception ex) { Log($"Error: {ex.Message}"); if (showErr) MessageBox.Show(ex.Message, "錯誤"); }
            finally { SetLoading(false); }
        }

        private void SetLoading(bool busy, string msg = "處理中...")
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetLoading(busy, msg)); return; }
            _isLoading = busy;
            if (LoadingOverlay != null) LoadingOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (LoadingText != null) LoadingText.Text = msg;
            if (BtnAddFiles != null) BtnAddFiles.IsEnabled = !busy;
            if (BtnCommit != null) BtnCommit.IsEnabled = !busy;
        }

        // ===== Logic Methods (Tasks) =====
        private async Task UpdateTagsAsync(UiRow[] rs, Func<UiRow, List<string>> logic)
        {
            if (rs.Length == 0) return;
            await RunBusyAsync("更新標籤...", async () =>
            {
                foreach (var r in rs) { var t = logic(r); await _vm.ApplyTagSetAsync(new[] { r }, t); }
                _view?.Refresh(); if (rs.Length == 1) RefreshRtTags(rs[0]);
            });
        }

        private async Task ModifyTagAsync(string t, bool add, bool ex = false)
        {
            var rs = GetSelectedUiRows(); if (rs.Length == 0) return;
            await RunBusyAsync("更新標籤...", async () => { await _vm.ModifyTagsAsync(rs, t, add, ex); _view?.Refresh(); if (rs.Length == 1) RefreshRtTags(rs[0]); });
        }

        private async Task ToggleFavoriteAsync()
        {
            var rs = GetSelectedUiRows();
            if (rs.Any() && rs[0].Item.Tags != null)
                await ModifyTagAsync("Favorite", !rs[0].Item.Tags.Contains("Favorite", StringComparer.OrdinalIgnoreCase));
        }

        private async Task CommitSelectedAsync()
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) throw new Exception("請選取項目。");
            string? locked = BtnLockProject.IsChecked == true ? CmbSearchProject.SelectedItem as string : null;
            if (BtnLockProject.IsChecked == true && (string.IsNullOrEmpty(locked) || locked == "[所有專案]")) throw new Exception("鎖定無效。");

            var (ok, _, tot) = await _vm.CommitFilesAsync(rows, locked);
            _view?.Refresh();
            MessageBox.Show($"提交完成: {ok} / {tot}");
        }

        // [V20.13.28] 掃描邏輯更新：確保在掃描前清除 TreeView 選取
        private async Task ScanLogic(SearchOption mode, bool isShallow)
        {
            // 1. 強制重置非必要篩選器和 TreeView 選取
            ResetFilters(keepViewMode: true);

            // 2. 設定全域檢視旗標
            _isShallowView = isShallow;

            // 3. 執行 ViewModel 掃描
            await _vm.ScanHotFolderAsync(mode, true);

            // 4. 刷新資料
            await RefreshFromDbAsync();

            // 5. 重設過濾器並保留檢視模式
            ResetFilters(keepViewMode: true);

            MessageBox.Show("掃描完成。", "完成");
        }

        // ===== Event Handlers =====

        private async void CmToggleFavorite_Click(object sender, RoutedEventArgs e) => await ToggleFavoriteAsync();
        private async void RtQuickTag_Favorite_Click(object sender, RoutedEventArgs e) => await ToggleFavoriteAsync();

        private async void CmAddTagProgress_Click(object sender, RoutedEventArgs e) => await ModifyTagAsync("InProgress", true, true);
        private async void RtQuickTag_InProgress_Click(object sender, RoutedEventArgs e) => await ModifyTagAsync("InProgress", true, true);

        private async void CmAddTagBacklog_Click(object sender, RoutedEventArgs e) => await ModifyTagAsync("Backlog", true, true);
        private async void RtQuickTag_Backlog_Click(object sender, RoutedEventArgs e) => await ModifyTagAsync("Backlog", true, true);

        private async void CmAddTagPending_Click(object sender, RoutedEventArgs e) => await ModifyTagAsync("Pending", true, true);
        private async void RtQuickTag_Pending_Click(object sender, RoutedEventArgs e) => await ModifyTagAsync("Pending", true, true);

        private async void CmRemoveAllTags_Click(object sender, RoutedEventArgs e) => await UpdateTagsAsync(GetSelectedUiRows(), r => new List<string>());
        private void RtQuickTag_Clear_Click(object sender, RoutedEventArgs e) => CmRemoveAllTags_Click(sender, e);

        private async void CmAddTags_Click(object sender, RoutedEventArgs e)
        {
            var rs = GetSelectedUiRows(); if (rs.Length == 0) return;
            var all = _rows.SelectMany(r => r.Item.Tags ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var cur = rs.SelectMany(r => r.Item.Tags ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var dlg = new TagPickerWindow(all, cur) { Owner = this };
            if (dlg.ShowDialog() == true) await UpdateTagsAsync(rs, r => dlg.SelectedTags);
        }
        private async void RtBtnApplyTags_Click(object sender, RoutedEventArgs e)
        {
            var rs = GetSelectedUiRows(); if (rs.Length == 0) return;
            if (RtTags == null) return;
            var t = RtTags.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            await UpdateTagsAsync(rs, r => t);
        }

        private async void BtnCommit_Click(object sender, RoutedEventArgs e) => await CommitSelectedAsync();
        private async void CmCommit_Click(object sender, RoutedEventArgs e) => await CommitSelectedAsync();

        private async void Cm_Rename_Click(object sender, RoutedEventArgs e)
        {
            if (GetSelectedUiRows().FirstOrDefault() is not UiRow r) return;
            var (_, n) = ShowInput("重新命名", "新檔名:", r.FileName); if (string.IsNullOrEmpty(n) || n == r.FileName) return;
            var dir = System.IO.Path.GetDirectoryName(r.SourcePath);
            if (dir != null) await RunBusyAsync("更名中...", async () => { await _vm.RenameFileAsync(r.SourcePath, n, dir, r.Item); await RefreshFromDbAsync(); });
        }
        private async void Cm_Delete_Click(object sender, RoutedEventArgs e)
        {
            var rs = GetSelectedUiRows(); if (rs.Length == 0) return;
            if (MessageBox.Show($"刪除 {rs.Length} 個檔案?", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await RunBusyAsync("刪除中...", async () => { await _vm.DeleteFilesAsync(rs); await RefreshFromDbAsync(); });
        }
        private async void CmDeleteRecord_Click(object sender, RoutedEventArgs e)
        {
            var rs = GetSelectedUiRows(); if (rs.Length == 0) return;
            if (MessageBox.Show($"移除 {rs.Length} 筆紀錄 (保留檔案)?", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await RunBusyAsync("移除中...", async () => { await _vm.DeleteRecordsAsync(rs.Select(x => x.Item.Id!).ToList()); await RefreshFromDbAsync(); });
        }

        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e) => await RunBusyAsync("加入檔案...", async () => {
            var hot = ConfigService.Cfg?.Import?.HotFolder;
            if (string.IsNullOrWhiteSpace(hot)) throw new Exception("未設定收件夾。");
            Directory.CreateDirectory(hot);
            var dlg = new OpenFileDialog { Multiselect = true, CheckFileExists = true };
            if (dlg.ShowDialog() != true) return;
            int n = await _vm.AddFilesAsync(dlg.FileNames, hot);
            Log($"加入 {n} 個檔案。");
        });

        private async void BtnOneClickAutoCommit_Click(object sender, RoutedEventArgs e) => await RunBusyAsync("自動分類...", async () => {
            var hot = ConfigService.Cfg?.Import?.HotFolder;
            if (string.IsNullOrWhiteSpace(hot)) throw new Exception("未設定收件夾。");
            await ScanLogic(SearchOption.AllDirectories, false);

            string? locked = BtnLockProject.IsChecked == true ? CmbSearchProject.SelectedItem as string : null;
            var (ok, _, tot) = await _vm.CommitFilesAsync(_rows.ToArray(), locked, hot);
            if (tot == 0) MessageBox.Show("無可處理檔案。", "完成");
            else { _view?.Refresh(); MessageBox.Show($"成功搬移 {ok} / {tot}。", "完成"); }
        });

        private async void MenuScanShallow_Click(object sender, RoutedEventArgs e) => await RunBusyAsync("掃描中...", () => ScanLogic(SearchOption.TopDirectoryOnly, true));
        private async void MenuScanRecursive_Click(object sender, RoutedEventArgs e) => await RunBusyAsync("掃描中...", () => ScanLogic(SearchOption.AllDirectories, false));

        private async void BtnClearCommitted_Click(object sender, RoutedEventArgs e) => await RunBusyAsync("清理中...", async () => {
            var hot = ConfigService.Cfg?.Import?.HotFolder;
            if (string.IsNullOrEmpty(hot)) throw new Exception("未設定 HotFolder。");
            if (MessageBox.Show("永久刪除收件夾已分類檔案？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var (d, f, _) = await _vm.ClearCommittedFilesAsync(hot);
            await RefreshFromDbAsync();
            MessageBox.Show($"刪除: {d}, 失敗: {f}", "完成");
        });

        private async void BtnResetInbox_Click(object sender, RoutedEventArgs e) => await RunBusyAsync("重置中...", async () => {
            if (MessageBox.Show("重置收件夾狀態？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            int c = await _vm.ResetInboxAsync();
            await RefreshFromDbAsync();
            MessageBox.Show($"已重置 {c} 筆紀錄。", "完成");
        });

        // ===== Tools / Nav =====
        public static void OpenPath(string? p) { if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) ProcessUtils.OpenInExplorer(p); else MessageBox.Show("路徑無效或未設定。"); }
        private void BtnOpenHot_Click(object sender, RoutedEventArgs e) => OpenPath(ConfigService.Cfg?.Import?.HotFolder);
        private void BtnOpenRoot_Click(object sender, RoutedEventArgs e) => OpenPath(ConfigService.Cfg?.App?.RootDir ?? ConfigService.Cfg?.Routing?.RootDir);
        private void BtnOpenPendingFolder_Click(object sender, RoutedEventArgs e)
        {
            var r = ConfigService.Cfg?.App?.RootDir;
            if (!string.IsNullOrEmpty(r)) ProcessUtils.OpenInExplorer(System.IO.Path.Combine(r, ConfigService.Cfg?.Routing?.LowConfidenceFolderName ?? "_low_conf"), true);
        }
        private void BtnOpenDb_Click(object sender, RoutedEventArgs e) { var p = ConfigService.Cfg?.Db?.DbPath; if (File.Exists(p)) ProcessUtils.TryStart(p); else if (p != null) ProcessUtils.OpenInExplorer(System.IO.Path.GetDirectoryName(p)); }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new SettingsWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"無法開啟設定視窗: {ex.Message}\n\n{ex.StackTrace}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnGoToSimpleMode_Click(object sender, RoutedEventArgs e)
        {
            try { Log("切換到簡易模式..."); var launcher = new LauncherWindow(); Application.Current.MainWindow = launcher; launcher.Show(); this.Close(); }
            catch (Exception ex) { MessageBox.Show($"切換失敗: {ex.Message}"); }
        }

        // ===== UI Events =====
        private void ListHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader h && h.Content is string t)
            {
                string k = t switch { "檔名" => "FileName", "副檔名" => "Ext", "類別" => "Category", "狀態" => "Status", "專案" => "Project", "標籤" => "Tags", "路徑" => "SourcePath", "預計路徑" => "SourcePath", _ => "CreatedAt" };
                _sortDir = (_sortKey == k && _sortDir == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending;
                _sortKey = k; ApplySort(k, _sortDir);
            }
        }
        private void ApplySort(string k, ListSortDirection d)
        {
            if (_view == null) return;
            _view.CustomSort = k switch
            {
                "FileName" => new PropComparer(r => r.FileName, d),
                "Ext" => new PropComparer(r => r.Ext, d),
                "Category" => new PropComparer(r => r.Category, d),
                "Status" => new StatusComparer(d),
                "Project" => new PropComparer(r => r.Project, d),
                "Tags" => new PropComparer(r => r.Tags, d),
                "SourcePath" => _showDest ? new PropComparer(r => r.DestPath, d) : new PropComparer(r => r.SourcePath, d),
                _ => new DateComparer(d)
            };
        }
        private void BtnStartClassify_Click(object sender, RoutedEventArgs e) { _showDest = !_showDest; if (ColSourcePath != null) { ColSourcePath.Header = _showDest ? "預計路徑" : "路徑"; ColSourcePath.DisplayMemberBinding = new Binding(_showDest ? "DestPath" : "SourcePath"); } }
        private void TxtSearchKeywords_TextChanged(object sender, TextChangedEventArgs e) { _searchKeyword = TxtSearchKeywords.Text; ApplyListFilters(); }
        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = (sender as TextBox)?.Text ?? "";
            var filterLower = filter.ToLowerInvariant();
            if (TvFolders == null) return;
            foreach (var item in TvFolders.Items)
            {
                if (item is TreeViewItem tvi) { bool v = FilterNode(tvi, filterLower); tvi.Visibility = v ? Visibility.Visible : Visibility.Collapsed; }
            }
        }
        private void ChkHideCommitted_Changed(object sender, RoutedEventArgs e) { _hideCommitted = (sender as CheckBox)?.IsChecked == true; ApplyListFilters(); }
        private void CmbSearchProject_SelectionChanged(object sender, RoutedEventArgs e) { if (BtnLockProject.IsChecked != true) { var s = CmbSearchProject.SelectedItem as string; _projFilter = (s == "[所有專案]") ? "" : s ?? ""; ApplyListFilters(); } }
        private void MainTabs_SelectionChanged(object sender, RoutedEventArgs e) { if (MainTabs.SelectedItem is TabItem t) { _currentTabTag = t.Tag as string ?? "home"; ApplyListFilters(); } }
        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            bool l = BtnLockProject.IsChecked == true; CmbSearchProject.IsEnabled = !l;
            BtnLockProject.Content = l ? "✅ 已鎖定" : "🔒 鎖定專案";
            BtnLockProject.Background = l ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : null;
            BtnLockProject.Foreground = l ? Brushes.White : null;
            if (l) { if (string.IsNullOrWhiteSpace(CmbSearchProject.SelectedItem as string) || (string)CmbSearchProject.SelectedItem == "[所有專案]") { MessageBox.Show("請先選專案。"); BtnLockProject.IsChecked = false; return; } _projFilter = ""; }
            else _projFilter = (CmbSearchProject.SelectedItem as string == "[所有專案]") ? "" : CmbSearchProject.SelectedItem as string ?? "";
            ApplyListFilters();
        }
        private void ResetFilters(bool keepViewMode = false)
        {
            // [V20.13.28] 清空 TreeView 選取
            if (TvFolders != null)
            {
                // 將 TreeView 的選取項目清除，SelectedItem 為唯讀，需將 SelectedItem 對應的 TreeViewItem 的 IsSelected 設為 false
                if (TvFolders.SelectedItem is TreeViewItem selectedTvi)
                {
                    selectedTvi.IsSelected = false;
                }
                else
                {
                    // 若 SelectedItem 不是 TreeViewItem，嘗試遞迴尋找並清除
                    ClearTreeViewSelection(TvFolders.Items);
                }
            }

            _selPath = "";
            if (Breadcrumb != null) Breadcrumb.ItemsSource = null;

            _searchKeyword = ""; if (TxtSearchKeywords != null) TxtSearchKeywords.Text = "";
            if (MainTabs != null) MainTabs.SelectedIndex = 0;

            if (BtnLockProject.IsChecked != true) { _projFilter = ""; if (CmbSearchProject != null) CmbSearchProject.SelectedIndex = 0; }
            _hideCommitted = false; if (ChkHideCommitted != null) ChkHideCommitted.IsChecked = false;

            if (!keepViewMode) _isShallowView = false;

            ApplyListFilters(); // 立即重新過濾
        }

        // 新增遞迴方法以清除所有 TreeViewItem 的選取狀態
        private void ClearTreeViewSelection(ItemCollection items)
        {
            foreach (var item in items)
            {
                if (item is TreeViewItem tvi)
                {
                    if (tvi.IsSelected)
                        tvi.IsSelected = false;
                    ClearTreeViewSelection(tvi.Items);
                }
            }
        }
        // ===== Context Menu / Tags (List) =====
        private UiRow[] GetSelectedUiRows() => MainList.SelectedItems.Cast<UiRow>().ToArray();
        private void List_DoubleClick(object? sender, MouseButtonEventArgs? e) { if (GetSelectedUiRows().FirstOrDefault() is UiRow r && File.Exists(r.SourcePath)) ProcessUtils.TryStart(r.SourcePath); }
        private void CmOpenFile_Click(object sender, RoutedEventArgs e) => List_DoubleClick(null, null);
        private void CmRevealInExplorer_Click(object sender, RoutedEventArgs e) { if (GetSelectedUiRows().FirstOrDefault() is UiRow r) ProcessUtils.OpenInExplorer(r.SourcePath); }
        private void CmCopySourcePath_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(string.Join("\n", GetSelectedUiRows().Select(r => r.SourcePath)));
        private void CmCopyDestPath_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(string.Join("\n", GetSelectedUiRows().Select(r => r.DestPath)));
        // ===== Tree View =====
        private TreeView? Tv => TvFolders;
        private void LoadFolderRoot(AppConfig? c = null)
        {
            try
            {
                c ??= ConfigService.Cfg;
                if (Tv == null) return;
                Tv.Items.Clear();
                var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(c.App?.RootDir)) list.Add(c.App.RootDir.Trim());
                if (c.App?.TreeViewRootPaths != null) foreach (var p in c.App.TreeViewRootPaths) list.Add(p.Trim());
                foreach (var p in list) if (Directory.Exists(p)) Tv.Items.Add(MakeTvi(new FolderNode { Name = System.IO.Path.GetFileName(p.TrimEnd('\\')), FullPath = p }));
            }
            catch { }
        }
        private TreeViewItem MakeTvi(FolderNode n)
        {
            var t = new TreeViewItem { Header = n.Name, Tag = n };
            try { if (Directory.EnumerateDirectories(n.FullPath).Any()) { t.Items.Add(new object()); t.Expanded += TvExpanded; } } catch { }
            return t;
        }
        private void TvExpanded(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem t && t.Items.Count == 1 && !(t.Items[0] is TreeViewItem) && t.Tag is FolderNode n)
            {
                t.Items.Clear(); try { foreach (var d in Directory.EnumerateDirectories(n.FullPath)) t.Items.Add(MakeTvi(new FolderNode { Name = System.IO.Path.GetFileName(d), FullPath = d })); } catch { }
            }
        }
        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // [V20.13.28] TvFolders_SelectedItemChanged 不應呼叫 ResetFilters，只需設置 _selPath
            if (Tv?.SelectedItem is TreeViewItem t && t.Tag is FolderNode n)
            {
                _selPath = n.FullPath; var s = new List<FolderNode>(); var c = t; while (c != null) { if (c.Tag is FolderNode fn) s.Add(fn); c = c.Parent as TreeViewItem; }
                s.Reverse(); if (Breadcrumb != null) Breadcrumb.ItemsSource = s;
            }
            else { _selPath = ""; if (Breadcrumb != null) Breadcrumb.ItemsSource = null; }
            ApplyListFilters();
        }
        private void BtnRefreshTree_Click(object sender, RoutedEventArgs e) => LoadFolderRoot();
        private void BreadcrumbItem_Click(object sender, MouseButtonEventArgs e) { if (sender is TextBlock b && b.DataContext is FolderNode n) { _selPath = n.FullPath; ApplyListFilters(); } }
        private void CmFolderOpen_Click(object sender, RoutedEventArgs e) { if (Tv?.SelectedItem is TreeViewItem t && t.Tag is FolderNode n) ProcessUtils.OpenInExplorer(n.FullPath); }
        private void CmFolderCopyPath_Click(object sender, RoutedEventArgs e) { if (Tv?.SelectedItem is TreeViewItem t && t.Tag is FolderNode n) Clipboard.SetText(n.FullPath); }

        // Tree I/O
        private async Task TreeIO(Func<FolderNode, Task> act, string confirm = "")
        {
            if (Tv?.SelectedItem is not TreeViewItem t || t.Tag is not FolderNode n) return;
            if (!string.IsNullOrEmpty(confirm) && MessageBox.Show(confirm.Replace("{name}", n.Name), "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await RunBusyAsync("執行中...", async () => { await act(n); LoadFolderRoot(); });
        }
        private async void Tree_NewFolder_Click(object sender, RoutedEventArgs e) => await TreeIO(async n =>
        {
            var (_, name) = ShowInput("新增資料夾", "名稱:", "NewFolder"); if (string.IsNullOrEmpty(name)) return;
            await _vm.CreateFolderAsync(n.FullPath, name);
        });
        private async void Tree_Rename_Click(object sender, RoutedEventArgs e) => await TreeIO(async n =>
        {
            var (_, name) = ShowInput("重新命名", "新名稱:", n.Name); if (string.IsNullOrEmpty(name) || name == n.Name) return;
            var dir = System.IO.Path.GetDirectoryName(n.FullPath);
            if (dir != null) await _vm.RenameFolderAsync(n.FullPath, name, dir);
        });
        private async void Tree_Delete_Click(object sender, RoutedEventArgs e) => await TreeIO(async n => await _vm.DeleteFolderAsync(n.FullPath), "刪除 '{name}'?");
        private async void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e) => await TreeIO(async n =>
        {
            var hot = ConfigService.Cfg?.Import?.HotFolder; if (string.IsNullOrEmpty(hot)) throw new Exception("未設定 HotFolder");
            var dlg = new MoveCopyCancelDialog($"匯入 '{n.Name}'?") { Owner = this }; if (dlg.ShowDialog() != true) return;
            await _vm.MoveOrCopyFolderToInboxAsync(n.FullPath, hot, dlg.SelectedAction == "Move");
        });

        private void Tree_LockProject_Click(object sender, RoutedEventArgs e)
        {
            if (Tv?.SelectedItem is TreeViewItem t && t.Tag is FolderNode n)
            {
                var list = CmbSearchProject.ItemsSource as List<string> ?? new List<string>(); if (!list.Contains(n.Name)) { list.Add(n.Name); CmbSearchProject.ItemsSource = list.OrderBy(x => x).ToList(); }
                CmbSearchProject.SelectedItem = n.Name; BtnLockProject.IsChecked = true; BtnLockProject_Click(null!, null!);
            }
        }
        private void Tree_UnlockProject_Click(object sender, RoutedEventArgs e) { BtnLockProject.IsChecked = false; BtnLockProject_Click(null!, null!); CmbSearchProject.SelectedIndex = 0; }

        // AI
        private async void BtnGenTags_Click(object sender, RoutedEventArgs e) => await RunBusyAsync("AI Tagging...", async () => { await _vm.GenerateTagsAsync(GetSelectedUiRows()); _view?.Refresh(); });
        private async void BtnSummarize_Click(object sender, RoutedEventArgs e) { var r = GetSelectedUiRows().FirstOrDefault(); if (r != null) await RunBusyAsync("Summarizing...", async () => { MessageBox.Show(await _vm.GenerateSummaryAsync(r)); }); }
        private async void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e) { var r = GetSelectedUiRows().FirstOrDefault(); if (r != null) await RunBusyAsync("Analyzing...", async () => { MessageBox.Show($"信心度: {await _vm.AnalyzeConfidenceAsync(r):P1}"); }); }
        private async void BtnGenProject_Click(object sender, RoutedEventArgs e) { var r = GetSelectedUiRows().FirstOrDefault(); if (r != null) await RunBusyAsync("Suggesting...", async () => { var p = await _vm.GenerateProjectAsync(r); if (!string.IsNullOrEmpty(p)) RtSuggestedProject.Text = p; }); }
        private async void BtnApplySuggestedProject_Click(object sender, RoutedEventArgs e) { var r = GetSelectedUiRows().FirstOrDefault(); var p = RtSuggestedProject.Text; if (r != null && !string.IsNullOrEmpty(p)) await RunBusyAsync("Applying...", async () => { await _vm.ApplyProjectAsync(r, p); _view?.Refresh(); }); }

        // Right Pane & Utils
        private void MainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var r = GetSelectedUiRows().FirstOrDefault(); ResetPreview(); if (r == null) { ResetRt(); return; }
            SetRt(r.FileName, r.Ext, "-", "-", "-", StatusToLabel(r.Status));
            if (File.Exists(r.SourcePath)) { var f = new FileInfo(r.SourcePath); SetRt(r.FileName, r.Ext, $"{f.Length:n0}", f.CreationTime.ToString("g"), f.LastWriteTime.ToString("g"), StatusToLabel(r.Status)); LoadPreview(r.SourcePath, r.Ext); }
            if (RtSuggestedProject != null) RtSuggestedProject.Text = r.Project; RefreshRtTags(r);
        }
        private void ResetRt() => SetRt("-", "-", "-", "-", "-", "-");
        private void SetRt(string n, string e, string s, string c, string m, string st) { if (RtName != null) RtName.Text = n; if (RtExt != null) RtName.Text = e; if (RtSize != null) RtSize.Text = s; if (RtCreated != null) RtCreated.Text = c; if (RtModified != null) RtModified.Text = m; if (RtStatus != null) RtStatus.Text = st; }
        private void ResetPreview() { if (RtPreviewImage != null) RtPreviewImage.Visibility = Visibility.Collapsed; if (RtPreviewText != null) RtPreviewText.Visibility = Visibility.Collapsed; if (RtPreviewNotSupported != null) RtPreviewNotSupported.Visibility = Visibility.Visible; if (RtPreviewWebView != null) RtPreviewWebView.Visibility = Visibility.Collapsed; }
        private void LoadPreview(string p, string e)
        {
            try
            {
                e = e.ToLower();
                if (new[] { "jpg", "png", "bmp" }.Contains(e)) { var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(p); b.EndInit(); RtPreviewImage.Source = b; RtPreviewImage.Visibility = Visibility.Visible; RtPreviewNotSupported.Visibility = Visibility.Collapsed; }
                else if (new[] { "txt", "log", "json" }.Contains(e)) { RtPreviewText.Text = File.ReadAllText(p); RtPreviewText.Visibility = Visibility.Visible; RtPreviewNotSupported.Visibility = Visibility.Collapsed; }
                else if (e == "pdf" && RtPreviewWebView.CoreWebView2 != null) { RtPreviewWebView.Source = new Uri(p); RtPreviewWebView.Visibility = Visibility.Visible; RtPreviewNotSupported.Visibility = Visibility.Collapsed; }
            }
            catch { }
        }
        private void RefreshRtTags(UiRow r) { if (RtTags != null) RtTags.Text = string.Join(", ", r.Item.Tags ?? new List<string>()); }
        private (bool, string) ShowInput(string t, string p, string d) { var dlg = new InputDialog(t, p, d) { Owner = this }; return (dlg.ShowDialog() == true, dlg.InputText); }
        public static string StatusToLabel(string? s) => (s ?? "").ToLower() switch { "blacklisted" => "黑名單", "committed" => "已提交", "error" => "錯誤", "" => "未處理", "intaked" => "未處理", _ when s!.StartsWith("stage") => "暫存", _ => s! };
        private bool FilterNode(TreeViewItem i, string f)
        {
            bool m = (i.Header as string)?.ToLower().Contains(f) == true;
            bool c = false; if (i.Items.Count > 0 && i.Items[0] != null) foreach (TreeViewItem child in i.Items) if (FilterNode(child, f)) c = true;
            bool v = m || c; i.Visibility = v ? Visibility.Visible : Visibility.Collapsed; return v;
        }
        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e) => LeftPaneColumn.Width = new GridLength(LeftPaneColumn.Width.Value > 0 ? 0 : 300);
        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e) => RightPaneColumn.Width = new GridLength(RightPaneColumn.Width.Value > 0 ? 0 : 360);

        // Comparers
        public class PropComparer : IComparer { Func<UiRow, string> _s; ListSortDirection _d; public PropComparer(Func<UiRow, string> s, ListSortDirection d) { _s = s; _d = d; } public int Compare(object? x, object? y) { if (x is not UiRow a || y is not UiRow b) return 0; return (_d == ListSortDirection.Ascending ? 1 : -1) * string.Compare(_s(a), _s(b), StringComparison.OrdinalIgnoreCase); } }
        public class DateComparer : IComparer { ListSortDirection _d; public DateComparer(ListSortDirection d) => _d = d; public int Compare(object? x, object? y) { if (x is not UiRow a || y is not UiRow b) return 0; return (_d == ListSortDirection.Ascending ? 1 : -1) * a.CreatedAt.CompareTo(b.CreatedAt); } }
        public class StatusComparer : IComparer
        {
            ListSortDirection _d; public StatusComparer(ListSortDirection d) => _d = d;
            static int W(string? s) => (s ?? "").ToLower() switch { "error" => 0, "blacklisted" => 0, "" => 1, "intaked" => 1, "committed" => 3, _ => 2 };
            public int Compare(object? x, object? y) { if (x is not UiRow a || y is not UiRow b) return 0; return (_d == ListSortDirection.Ascending ? 1 : -1) * W(a.Status).CompareTo(W(b.Status)); }
        }
    }
}