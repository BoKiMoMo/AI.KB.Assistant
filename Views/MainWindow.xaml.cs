// Views/MainWindow.xaml.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text; // For LogBox
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        // ===== UI Row =====
        private sealed class UiRow
        {
            public Item Item { get; }
            public UiRow(Item it)
            {
                Item = it;
                FileName = Path.GetFileName(it.Path ?? string.Empty);
                Ext = (Path.GetExtension(it.Path ?? string.Empty) ?? "").Trim('.').ToLowerInvariant();
                Project = it.Project ?? "";
                Tags = it.Tags == null ? "" : string.Join(",", it.Tags);
                SourcePath = it.Path ?? "";
                DestPath = it.ProposedPath ?? "";
                CreatedAt = it.Timestamp ?? it.UpdatedAt;
                Status = string.IsNullOrWhiteSpace(it.Status) ? "intaked" : it.Status!;
            }

            public string FileName { get; }
            public string Ext { get; }
            public string Project { get; set; }
            public string Tags { get; set; }
            public string SourcePath { get; }
            public string DestPath { get; set; }
            public DateTime CreatedAt { get; }
            public string Status { get; set; }
        }

        private readonly ObservableCollection<UiRow> _rows = new();
        private ListCollectionView? _view;

        // 左樹節點
        private sealed class FolderNode
        {
            public string Name { get; set; } = "";
            public string FullPath { get; set; } = "";
            public override string ToString() => Name;
        }

        // 服務
        private T? Get<T>(string key) where T : class => Application.Current?.Resources[key] as T;
        private DbService? Db => Get<DbService>("Db");
        private IntakeService? Intake => Get<IntakeService>("Intake");
        private RoutingService? Router => Get<RoutingService>("Router");
        private LlmService? Llm => Get<LlmService>("Llm");

        // 路徑欄寬暫存
        private double _srcWidth = 300;
        private double _dstWidth = 320;

        // 排序狀態
        private string _sortKey = "CreatedAt";
        private ListSortDirection _sortDir = ListSortDirection.Descending;

        // V7.5.8 過濾狀態
        private string _currentTabTag = "home";
        private string _selectedFolderPath = ""; // V7.5.8 樹狀圖連動

        // Converters expose
        public static readonly IValueConverter StatusToLabelConverterInstance = new StatusToLabelConverter();
        public static readonly IMultiValueConverter StatusToBrushConverterInstance = new StatusToBrushConverter();

        public MainWindow()
        {
            InitializeComponent();
            Log("MainWindow Initializing...");

            // 綁清單
            MainList.ItemsSource = _rows;
            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
            ApplySort(_sortKey, _sortDir);
            Log("MainList CollectionView bound.");

            // 設定變更
            ConfigService.ConfigChanged += (_, cfg) =>
            {
                // 確保在 UI 執行緒
                Dispatcher.Invoke(() =>
                {
                    Log("偵測到設定變更，已重新載入 Router/Llm 並刷新左側樹。");
                    try { Router?.ApplyConfig(cfg); Llm?.UpdateConfig(cfg); } catch { }
                    _ = RefreshFromDbAsync();
                    LoadFolderRoot(cfg); // 【P3 修正】左樹刷新，直接傳入新設定
                });
            };

            Loaded += async (_, __) =>
            {
                Log("MainWindow Loaded.");

                // 左樹 & 清單
                LoadFolderRoot();
                await RefreshFromDbAsync(); // 載入 _rows
                ApplyListFilters(); // 應用初始過濾 (home)
            };
        }

        // ===== Log (V7.4) =====
        private void Log(string message)
        {
            // 確保 Log 總是在 UI 執行緒上更新
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(message));
                return;
            }

            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            if (LogBox != null)
            {
                LogBox.AppendText(entry);
                LogBox.ScrollToEnd();
            }
        }

        // (V7.5 介面清理：BtnToggleLog_Click 和 LogSplitter_DragCompleted 已被 XAML Expander 取代，故刪除)

        // ===== DB → UI =====
        private async Task RefreshFromDbAsync()
        {
            try
            {
                TxtCounterSafe("讀取中…");
                Log("開始從資料庫重新整理 (RefreshFromDbAsync)...");
                _rows.Clear();

                if (Db == null)
                {
                    TxtCounterSafe("DB 尚未初始化");
                    Log("錯誤：DbService 尚未初始化 (null)。");
                    return;
                }

                var items = await Db.QueryAllAsync();
                Log($"資料庫讀取完畢，共載入 {items.Count} 筆項目。");

                // 濾掉 Path 空白的無效紀錄，避免出現「空白列」
                foreach (var it in items.Where(x => !string.IsNullOrWhiteSpace(x.Path)))
                {
                    if (string.IsNullOrWhiteSpace(it.ProposedPath) && Router != null)
                        it.ProposedPath = Router.PreviewDestPath(it.Path);

                    _rows.Add(new UiRow(it));
                }

                ApplySort(_sortKey, _sortDir);
                ApplyListFilters(); // V7.5.8 重新載入資料後，要重新套用目前過濾
                TxtCounterSafe($"共 {_rows.Count} 筆");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "重新整理失敗");
                TxtCounterSafe("讀取失敗");
                Log($"重新整理失敗: {ex.Message}");
            }
        }

        private void TxtCounterSafe(string text)
        {
            if (FindName("TxtCounter") is TextBlock t) t.Text = text;
        }

        // ===== Toolbar =====
        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Intake == null || Router == null)
                {
                    Log("服務尚未初始化 (Intake / Router)。");
                    MessageBox.Show("服務尚未初始化（Intake / Router）。");
                    return;
                }

                var dlg = new OpenFileDialog { Title = "選擇要加入的檔案", Multiselect = true, CheckFileExists = true };
                if (dlg.ShowDialog(this) != true) return;

                Log($"手動加入 {dlg.FileNames.Length} 個檔案...");
                var added = await Intake.IntakeFilesAsync(dlg.FileNames);
                foreach (var it in added.Where(a => a != null))
                {
                    it.ProposedPath = Router.PreviewDestPath(it.Path);
                    _rows.Insert(0, new UiRow(it));
                }

                ApplySort(_sortKey, _sortDir);
                // ApplyListFilters(); // _rows 變動時 CollectionView 會自動重套過濾
                TxtCounterSafe($"共 {_rows.Count} 筆");
                Log($"成功加入 {added.Count} 筆新項目。");
            }
            catch (Exception ex) { Log($"加入檔案失敗: {ex.Message}"); MessageBox.Show(ex.Message, "加入檔案失敗"); }
        }

        private async void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Router == null || Db == null)
                {
                    Log("服務尚未初始化 (Router / Db)。");
                    MessageBox.Show("服務尚未初始化（Router / Db）。");
                    return;
                }

                var selected = GetSelectedUiRows();
                if (selected.Length == 0)
                {
                    Log("提交操作已取消 (未選取項目)。");
                    MessageBox.Show("請先在清單中選取要提交的項目。");
                    return;
                }

                Log($"開始提交 {selected.Length} 個項目...");
                int ok = 0;
                foreach (var row in selected)
                {
                    var it = row.Item;
                    if (string.IsNullOrWhiteSpace(it.ProposedPath))
                        it.ProposedPath = Router.PreviewDestPath(it.Path);

                    var final = Router.Commit(it);
                    if (!string.IsNullOrWhiteSpace(final))
                    {
                        it.Status = "committed";
                        it.ProposedPath = final;
                        row.Status = "committed";
                        row.DestPath = final;
                        ok++;
                    }
                }

                if (ok > 0)
                {
                    await Db.UpdateItemsAsync(selected.Select(r => r.Item).ToArray());
                    Log($"成功提交 {ok} / {selected.Length} 個項目。");
                }
                CollectionViewSource.GetDefaultView(_rows)?.Refresh(); // 僅刷新可見項目 (例如狀態顏色)
                MessageBox.Show($"完成提交：{ok} / {selected.Length}");
            }
            catch (Exception ex) { Log($"提交失敗: {ex.Message}"); MessageBox.Show(ex.Message, "提交失敗"); }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshFromDbAsync();

        private void BtnOpenHot_Click(object sender, RoutedEventArgs e)
        {
            // V7.3 修正：補上 { ... }
            try
            {
                var cfg = ConfigService.Cfg;
                var p = cfg?.Import?.HotFolderPath ?? cfg?.Import?.HotFolder;
                Log($"開啟收件夾: {p}");
                OpenInExplorer(p);
            }
            catch (Exception ex)
            {
                Log($"開啟收件夾失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "開啟收件夾失敗");
            }
        }

        private void BtnOpenRoot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // V7.3 P3 修正：RootDir 按鈕直接從 ConfigService.Cfg 取值
                var cfg = ConfigService.Cfg;

                // V7.5 Bug 修正：
                // 1. 設定頁面 (SettingsWindow) 儲存的是 App.RootDir。
                // 2. 邏輯應改為：優先使用 App.RootDir，若其為空，才回退(fallback)到 Routing.RootDir。
                var root = (cfg?.App?.RootDir ?? cfg?.Routing?.RootDir ?? "").Trim();

                if (string.IsNullOrWhiteSpace(root))
                {
                    Log("錯誤：根目錄 (RootDir) 尚未設定。");
                    MessageBox.Show("Root 目錄尚未設定。請到「設定」頁面指定。");
                    return;
                }

                if (File.Exists(root))
                    root = Path.GetDirectoryName(root)!;

                root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!Directory.Exists(root))
                {
                    Log($"錯誤：根目錄不存在: {root}");
                    MessageBox.Show($"Root 目錄不存在：{root}");
                    return;
                }

                Log($"開啟根目錄: {root}");
                OpenInExplorer(root);
            }
            catch (Exception ex)
            {
                Log($"開啟根目錄失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "開啟根目錄失敗");
            }
        }

        // V7.5.7 修正：合併為一個按鈕
        private void BtnOpenPendingFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigService.Cfg;
                var root = (cfg?.App?.RootDir ?? cfg?.Routing?.RootDir ?? "").Trim();

                // 我們將 LowConfidenceFolderName 視為「待整理」資料夾
                var pendingFolder = cfg?.Routing?.LowConfidenceFolderName ?? "_low_conf";

                if (string.IsNullOrWhiteSpace(root))
                {
                    MessageBox.Show("Root 目錄尚未設定。請到「設定」頁面指定。");
                    return;
                }

                var path = Path.Combine(root, pendingFolder);
                Log($"開啟待整理資料夾: {path}");
                OpenInExplorer(path, createIfNotExist: true);
            }
            catch (Exception ex)
            {
                Log($"開啟待整理資料夾失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "開啟資料夾失敗");
            }
        }

        private void BtnOpenDb_Click(object sender, RoutedEventArgs e)
        {
            var cfg = ConfigService.Cfg;
            var p = cfg?.Db?.DbPath ?? cfg?.Db?.Path;
            Log($"開啟資料庫: {p}");
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) TryStart(p);
            else OpenInExplorer(Path.GetDirectoryName(p ?? string.Empty));
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("開啟設定視窗...");
                new SettingsWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
                Log("設定視窗已關閉。");
            }
            catch (Exception ex) { Log($"開啟設定失敗: {ex.Message}"); MessageBox.Show(ex.Message, "開啟設定失敗"); }
        }

        private void CmbPathView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is not ComboBox cb || cb.SelectedItem is not ComboBoxItem it) return;
                var tag = (it.Tag as string) ?? "actual";
                var showPred = string.Equals(tag, "pred", StringComparison.OrdinalIgnoreCase);

                if (ColSourcePath != null && ColDestPath != null)
                {
                    if (showPred)
                    {
                        // V7.5.8 Bug 修正：使用 0.01 避免 Header 跳動
                        _srcWidth = ColSourcePath.Width;
                        ColSourcePath.Width = 0.01;
                        ColDestPath.Width = _dstWidth switch { 0 => 320, _ => _dstWidth };
                        Log("切換檢視：顯示預計路徑");
                    }
                    else
                    {
                        _dstWidth = ColDestPath.Width;
                        ColDestPath.Width = 0.01; // V7.5.8 Bug 修正
                        ColSourcePath.Width = _srcWidth switch { 0 => 300, _ => _srcWidth };
                        Log("切換檢視：顯示實際路徑");
                    }
                }
            }
            catch (Exception ex) { Log($"切換檢視失敗: {ex.Message}"); MessageBox.Show(ex.Message, "切換檢視失敗"); }
        }

        // ===== 排序 =====
        private void ListHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader h) return;
            var text = (h.Content as string)?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return;

            string key = text switch
            {
                "檔名" => "FileName",
                "副檔名" => "Ext",
                "狀態" => "Status",
                "專案" => "Project",
                "標籤" => "Tags",
                "路徑" => "SourcePath",
                "預計路徑" => "DestPath",
                "建立時間" => "CreatedAt",
                _ => "CreatedAt"
            };

            _sortDir = (_sortKey == key && _sortDir == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _sortKey = key;
            ApplySort(_sortKey, _sortDir);
            Log($"清單排序：{key} {_sortDir}");
        }

        private void ApplySort(string key, ListSortDirection dir)
        {
            if (_view == null) return;

            IComparer cmp = key switch
            {
                "FileName" => new PropComparer<UiRow>(r => r.FileName, dir),
                "Ext" => new CategoryComparer(dir, Router),
                "Status" => new StatusComparer(dir),
                "Project" => new PropComparer<UiRow>(r => r.Project, dir),
                "Tags" => new PropComparer<UiRow>(r => r.Tags, dir),
                "SourcePath" => new PropComparer<UiRow>(r => r.SourcePath, dir),
                "DestPath" => new PropComparer<UiRow>(r => r.DestPath, dir),
                "CreatedAt" => new DateComparer(dir),
                _ => new DateComparer(dir)
            };

            _view.CustomSort = cmp;
            _view.Refresh();
        }

        // ===== 右鍵功能 =====
        private void CmOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault(); if (row == null) return;
            try
            {
                if (File.Exists(row.SourcePath))
                    Process.Start(new ProcessStartInfo { FileName = row.SourcePath, UseShellExecute = true });
                else MessageBox.Show("找不到檔案。");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟檔案失敗"); }
        }

        private void CmRevealInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault(); if (row == null) return;
            OpenInExplorer(row.SourcePath);
        }

        private void CmCopySourcePath_Click(object sender, RoutedEventArgs e)
        {
            var txt = string.Join(Environment.NewLine, GetSelectedUiRows().Select(r => r.SourcePath));
            if (!string.IsNullOrWhiteSpace(txt)) Clipboard.SetText(txt);
        }

        private void CmCopyDestPath_Click(object sender, RoutedEventArgs e)
        {
            var txt = string.Join(Environment.NewLine, GetSelectedUiRows().Select(r => r.DestPath));
            if (!string.IsNullOrWhiteSpace(txt)) Clipboard.SetText(txt);
        }

        private async void CmAddTags_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) { MessageBox.Show("請先選取資料列"); return; }

            var existingTags = _rows.SelectMany(r => (r.Item.Tags ?? new List<string>())).Distinct().OrderBy(s => s).ToList();

            // 簡易多選視窗
            var win = new Window
            {
                Title = "添加標籤",
                Width = 420,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("App.PanelBrush")
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            win.Content = root;

            var list = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 360 };
            foreach (var t in existingTags) list.Items.Add(t);

            var tb = new TextBox { Margin = new Thickness(0, 8, 0, 0), ToolTip = "輸入新標籤後按 Enter 新增" };
            tb.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    var nt = tb.Text.Trim();
                    if (!list.Items.Contains(nt)) list.Items.Add(nt);
                    tb.Clear();
                }
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "確定", Width = 80, Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = "取消", Width = 80 };
            ok.Click += (_, __) => win.DialogResult = true;
            cancel.Click += (_, __) => win.DialogResult = false;

            DockPanel.SetDock(sp, Dock.Bottom);
            DockPanel.SetDock(tb, Dock.Bottom);
            root.Children.Add(sp);
            root.Children.Add(tb);
            root.Children.Add(list);
            sp.Children.Add(cancel);
            sp.Children.Add(ok);

            if (win.ShowDialog() == true)
            {
                var picked = list.SelectedItems.Cast<string>().ToList();
                if (picked.Count == 0) { MessageBox.Show("未選取標籤"); return; }

                foreach (var r in rows)
                {
                    var set = (r.Item.Tags ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in picked) set.Add(p);
                    r.Item.Tags = set.ToList();
                    r.Tags = string.Join(",", set);
                }

                try
                {
                    if (Db != null) await Db.UpdateItemsAsync(rows.Select(x => x.Item).ToArray());
                    CollectionViewSource.GetDefaultView(_rows)?.Refresh();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "更新標籤失敗"); }
            }
        }

        // V7.5.8 新增：標籤操作輔助函式
        private async Task ModifyTagsAsync(string tag, bool add, bool exclusive = false)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) return;

            Log($"V7.5.8: 正在為 {rows.Length} 個項目 {(add ? "加入" : "移除")} 標籤 '{tag}'。");

            // 定義獨佔標籤
            var exclusiveTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "InProgress", "Backlog", "Pending" };

            foreach (var r in rows)
            {
                var set = (r.Item.Tags ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (exclusive) // 如果是獨佔標籤 (如 Backlog, Pending)，先移除所有狀態標籤
                {
                    set.RemoveWhere(t => exclusiveTags.Contains(t));
                }

                if (add)
                    set.Add(tag);
                else
                    set.Remove(tag);

                r.Item.Tags = set.ToList();
                r.Tags = string.Join(",", set);
            }

            try
            {
                if (Db != null) await Db.UpdateItemsAsync(rows.Select(x => x.Item).ToArray());
                _view?.Refresh(); // 更新過濾
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "更新標籤失敗"); }
        }

        // V7.5.7 修正：實作標籤切換
        private async void CmToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) return;
            // 以第一個項目決定是「加入」還是「移出」
            bool addFav = !(rows[0].Item.Tags?.Contains("Favorite", StringComparer.OrdinalIgnoreCase) ?? false);
            await ModifyTagsAsync("Favorite", addFav, exclusive: false); // 'Favorite' 不是獨佔的
        }

        // V7.5.8 新增：
        private async void CmAddTagProgress_Click(object sender, RoutedEventArgs e)
            => await ModifyTagsAsync("InProgress", add: true, exclusive: true);

        private async void CmAddTagBacklog_Click(object sender, RoutedEventArgs e)
            => await ModifyTagsAsync("Backlog", add: true, exclusive: true);

        private async void CmAddTagPending_Click(object sender, RoutedEventArgs e)
            => await ModifyTagsAsync("Pending", add: true, exclusive: true);

        private async void CmRemoveAllTags_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) return;

            foreach (var r in rows)
            {
                r.Item.Tags = new List<string>();
                r.Tags = "";
            }

            try
            {
                if (Db != null) await Db.UpdateItemsAsync(rows.Select(x => x.Item).ToArray());
                _view?.Refresh();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "更新標籤失敗"); }
        }

        private void CmStageToInbox_Click(object sender, RoutedEventArgs e) { /* 之後 V7.4 */ }
        private void CmClassify_Click(object sender, RoutedEventArgs e) { /* 之後 V7.4 */ }
        private void CmCommit_Click(object sender, RoutedEventArgs e) => BtnCommit_Click(sender, e);
        private void CmDeleteRecord_Click(object sender, RoutedEventArgs e) { /* 之後 V7.4 */ }

        // ===== 右側資訊欄 (V7.4/V7.5) =====
        private void MainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault();

            // 無選取時，重設所有欄位
            if (row == null)
            {
                ResetRtDetail();
                return;
            }

            try
            {
                string size, created, modified;
                if (File.Exists(row.SourcePath))
                {
                    var fi = new FileInfo(row.SourcePath);
                    size = $"{fi.Length:n0} bytes";
                    created = fi.CreationTime.ToString("yyyy-MM-dd HH:mm");
                    modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                }
                else
                {
                    size = "-";
                    created = row.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    modified = "-";
                }

                // 1. 設定檔案資訊
                SetRtDetail(row.FileName, row.Ext, size, created, modified, StatusToLabel(row.Status));

                // 2. 設定分類
                RtSuggestedProject.Text = row.Project;
                RtTags.Text = row.Item.Tags == null ? "" : string.Join(", ", row.Item.Tags);

                // 3. (V7.5) 嘗試載入預覽和 Metadata (此處僅為 V7.5 預留)
                // TODO V7.5:
                // LoadPreviewAsync(row.SourcePath, row.Ext);
                // LoadMetadataAsync(row.SourcePath);
            }
            catch (Exception ex)
            {
                Log($"右側欄更新失敗: {ex.Message}");
                ResetRtDetail();
            }
        }

        /// <summary>V7.5 重設右側資訊欄所有內容</summary>
        private void ResetRtDetail()
        {
            SetRtDetail("-", "-", "-", "-", "-", "-");

            // V7.4
            RtSuggestedProject.Text = "";
            RtTags.Text = "";

            // V7.5 Metadata
            if (FindName("RtShotDate") is TextBlock sd) sd.Text = "-";
            if (FindName("RtCameraModel") is TextBlock cm) cm.Text = "-";

            // V7.5 Preview
            if (FindName("RtPreviewImage") is Image pi) pi.Visibility = Visibility.Collapsed;
            if (FindName("RtPreviewText") is TextBox pt) pt.Visibility = Visibility.Collapsed;
            if (FindName("RtPreviewNotSupported") is TextBlock pn) pn.Visibility = Visibility.Visible;
        }

        /// <summary>設定右側資訊欄 - 基礎資訊</summary>
        private void SetRtDetail(string name, string ext, string size, string created, string modified, string status)
        {
            if (FindName("RtName") is TextBlock a) a.Text = name;
            if (FindName("RtExt") is TextBlock b) b.Text = ext;
            if (FindName("RtSize") is TextBlock c) c.Text = size;
            if (FindName("RtCreated") is TextBlock d) d.Text = created;
            if (FindName("RtModified") is TextBlock e) e.Text = modified;
            if (FindName("RtStatus") is TextBlock f) f.Text = status;
        }

        private async void BtnApplySuggestedProject_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) return;

            var proj = (RtSuggestedProject.SelectedItem as string) ?? RtSuggestedProject.Text?.Trim();
            if (string.IsNullOrWhiteSpace(proj)) return;

            row.Project = proj;
            row.Item.Project = proj;
            try
            {
                if (Db != null) await Db.UpdateItemsAsync(new[] { row.Item });
                CollectionViewSource.GetDefaultView(_rows)?.Refresh();
                Log($"已套用專案 '{proj}' 到 {row.FileName}");
            }
            catch (Exception ex) { Log($"更新專案失敗: {ex.Message}"); MessageBox.Show(ex.Message, "更新專案失敗"); }
        }

        // V7.4 新增：套用右側面板的標籤
        private async void RtBtnApplyTags_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0)
            {
                Log("請先在中間清單選取一個或多個項目。");
                return;
            }

            var tagsStr = RtTags.Text ?? "";
            var newTags = tagsStr.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(t => t.Trim())
                                 .Where(t => !string.IsNullOrWhiteSpace(t))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            foreach (var r in rows)
            {
                r.Item.Tags = newTags;
                r.Tags = string.Join(",", newTags); // 更新 UiRow 的快取
            }

            try
            {
                if (Db != null)
                {
                    var count = await Db.UpdateItemsAsync(rows.Select(x => x.Item).ToArray());
                    CollectionViewSource.GetDefaultView(_rows)?.Refresh();
                    Log($"已為 {count} 個項目更新標籤。");
                }
            }
            catch (Exception ex) { Log($"更新標籤失敗: {ex.Message}"); MessageBox.Show(ex.Message, "更新標籤失敗"); }
        }

        // ===== 左側：樹狀 + 麵包屑 (P1 修正) =====
        private TreeView? ResolveTv() => (TvFolders ?? FindName("TvFolders") as TreeView);

        // CheckBox 綁定事件
        private void TreeToggles_Changed(object sender, RoutedEventArgs e)
        {
            Log("左側樹顯示切換 (桌面/磁碟)。");
            LoadFolderRoot();
        }

        private void LoadFolderRoot(AppConfig? cfg = null) // 【P3 修正】接受傳入的 cfg
        {
            try
            {
                // 如果沒有傳入 cfg，才自己從 Service 讀取
                if (cfg == null)
                    cfg = ConfigService.Cfg;

                var tv = ResolveTv();
                if (tv == null) return;

                tv.Items.Clear();

                // 1) ROOT
                // V7.5 Bug 修正：(同 BtnOpenRoot_Click)
                var root = (cfg?.App?.RootDir ?? cfg?.Routing?.RootDir ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    var node = MakeNode(root);
                    tv.Items.Add(MakeTvi(node, headerOverride: $"ROOT：{node.Name}"));
                }
                else if (!string.IsNullOrWhiteSpace(root))
                {
                    Log($"警告：ROOT 目錄不存在: {root}");
                }

                // 2) 桌面（若有勾選）
                if (ChkShowDesktop?.IsChecked == true)
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (Directory.Exists(desktop))
                        tv.Items.Add(MakeTvi(MakeNode(desktop), headerOverride: "桌面"));
                }

                // 3) 系統磁碟（若有勾選）
                if (ChkShowDrives?.IsChecked == true)
                {
                    foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    {
                        try
                        {
                            var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})";
                            tv.Items.Add(MakeTvi(new FolderNode { Name = label, FullPath = d.Name }, headerOverride: label));
                        }
                        catch (Exception ex)
                        {
                            Log($"讀取磁碟 {d.Name} 失敗 (可能無光碟): {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex) { Log($"LoadFolderRoot failed: {ex.Message}"); }
        }

        private static FolderNode MakeNode(string path)
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name)) name = trimmed;
            return new FolderNode { Name = name, FullPath = trimmed };
        }

        // 統一的節點建立工廠
        private TreeViewItem MakeTvi(FolderNode node, string? headerOverride = null)
        {
            var tvi = new TreeViewItem { Header = headerOverride ?? node.Name, Tag = node };

            try
            {
                // 使用 Any() 檢查子目錄，比 GetDirectories().Length > 0 更高效
                if (Directory.Exists(node.FullPath) && Directory.EnumerateDirectories(node.FullPath).Any())
                {
                    // 先放一個 dummy node，並加上 "__DUMMY__" 標記
                    tvi.Items.Add(new TreeViewItem { Header = "…", Tag = "__DUMMY__" });
                }
            }
            catch { /* 權限不足，忽略子目錄 */ }

            // 統一綁定展開事件
            tvi.Expanded += TvFolders_Expanded;
            return tvi;
        }

        // 真正的懶載入：展開時替換 Dummy
        private void TvFolders_Expanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not TreeViewItem tvi) return;

            // 1. 檢查 Tag 是否為 FolderNode (我們統一的模型)
            if (tvi.Tag is not FolderNode node) return;

            // 2. 檢查虛擬節點
            if (tvi.Items.Count != 1) return;
            if (tvi.Items[0] is not TreeViewItem dummy || (string?)dummy.Tag != "__DUMMY__") return;

            tvi.Items.Clear();

            // 3. 從 FolderNode 取得路徑
            var path = node.FullPath;
            try
            {
                foreach (var dir in System.IO.Directory.EnumerateDirectories(path))
                {
                    // 4. 遞迴呼叫 MakeTvi，這會自動綁定下一層的 Expanded 事件
                    tvi.Items.Add(MakeTvi(MakeNode(dir)));
                }
            }
            catch (Exception ex)
            {
                Log($"載入子目錄失敗 (權限?): {ex.Message}");
            }
        }

        // V7.5.8 修正：樹狀圖連動
        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                if (ResolveTv()?.SelectedItem is not TreeViewItem tvi || tvi.Tag is not FolderNode node)
                {
                    _selectedFolderPath = ""; // 清除選取
                }
                else
                {
                    _selectedFolderPath = node.FullPath; // 記錄選取

                    // 麵包屑
                    var stack = new List<FolderNode>();
                    var cur = tvi;
                    while (cur != null)
                    {
                        if (cur.Tag is FolderNode fn) stack.Add(fn);
                        cur = cur.Parent as TreeViewItem;
                    }
                    stack.Reverse();
                    Breadcrumb.ItemsSource = stack;
                }

                // 觸發清單過濾
                ApplyListFilters();
            }
            catch { }
        }

        private void Breadcrumb_Click(object sender, RoutedEventArgs e) { }

        private void CmFolderOpen_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveTv()?.SelectedItem is TreeViewItem tvi && tvi.Tag is FolderNode n)
                OpenInExplorer(n.FullPath);
        }

        private void CmFolderCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveTv()?.SelectedItem is TreeViewItem tvi && tvi.Tag is FolderNode n)
                Clipboard.SetText(n.FullPath);
        }

        // ===== 其他事件 =====
        private void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
            Log("「檢視分類」按鈕點擊 (V7.4 尚未實作)。");
            _ = RefreshFromDbAsync();
        }
        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e)
        {
            if (LeftPaneColumn.Width.Value > 0) LeftPaneColumn.Width = new GridLength(0);
            else LeftPaneColumn.Width = new GridLength(300);
            Log($"左側面板開關。");
        }
        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e)
        {
            if (RightPaneColumn.Width.Value > 0) RightPaneColumn.Width = new GridLength(0);
            else RightPaneColumn.Width = new GridLength(360);
            Log($"右側面板開關。");
        }

        // V7.4/V7.5 介面空事件
        private void TxtSearchKeywords_TextChanged(object sender, TextChangedEventArgs e) { /* V7.5 實作 */ }
        private void CmbSearchProject_SelectionChanged(object sender, SelectionChangedEventArgs e) { /* V7.5 實作 */ }
        private void BtnSearchProject_Click(object sender, RoutedEventArgs e) { /* V7.5 實作 */ }
        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            Log("專案鎖定功能 (V7.5) 尚未實作。");
            MessageBox.Show("專案鎖定：尚未實作（V7.5）");
        }
        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e) { /* V7.5 實作 */ }
        private void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e) { MessageBox.Show("整份資料夾加入收件夾：尚未實作（V7.4）"); }

        // V7.5.8 實作：Tab 過濾
        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource is not TabControl tc) return;
            if (tc.SelectedItem is not TabItem ti) return;
            if (_view == null) return;

            _currentTabTag = (ti.Tag as string) ?? "home";
            Log($"切換 Tab: {_currentTabTag}");
            ApplyListFilters();
        }

        // V7.5.8 核心：統一過濾
        private void ApplyListFilters()
        {
            if (_view == null) return;

            Log($"套用過濾：Tab='{_currentTabTag}', Path='{_selectedFolderPath}'");

            _view.Filter = (obj) =>
            {
                if (obj is not UiRow row) return false;

                // 1. 檢查 Tab 標籤
                bool tabMatch = false;
                switch (_currentTabTag)
                {
                    case "fav":
                        tabMatch = row.Item.Tags?.Contains("Favorite", StringComparer.OrdinalIgnoreCase) ?? false;
                        break;
                    case "progress":
                        tabMatch = row.Item.Tags?.Contains("InProgress", StringComparer.OrdinalIgnoreCase) ?? false;
                        break;
                    case "backlog":
                        tabMatch = row.Item.Tags?.Contains("Backlog", StringComparer.OrdinalIgnoreCase) ?? false;
                        break;
                    case "pending":
                        tabMatch = row.Item.Tags?.Contains("Pending", StringComparer.OrdinalIgnoreCase) ?? false;
                        break;
                    case "home":
                    default:
                        tabMatch = true; // 主頁預設顯示全部
                        break;
                }

                if (!tabMatch) return false;

                // 2. 僅在「主頁」時，套用樹狀圖過濾
                bool folderMatch = true;
                if (_currentTabTag == "home" && !string.IsNullOrWhiteSpace(_selectedFolderPath))
                {
                    folderMatch = row.SourcePath.StartsWith(_selectedFolderPath, StringComparison.OrdinalIgnoreCase);
                }

                return tabMatch && folderMatch;
            };

            _view.Refresh();
        }


        private void List_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) return;
            try
            {
                if (File.Exists(row.SourcePath))
                    Process.Start(new ProcessStartInfo { FileName = row.SourcePath, UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟檔案失敗"); }
        }

        // V7.5 AI 按鈕
        private void BtnGenTags_Click(object sender, RoutedEventArgs e) { MessageBox.Show("產生建議：V7.5 接 AI 後啟用"); }
        private void BtnSummarize_Click(object sender, RoutedEventArgs e) { MessageBox.Show("摘要：V7.5 接 AI 後啟用"); }
        private void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e) { MessageBox.Show("信心分析：V7.5 接 AI 後啟用"); }


        // ===== Helpers =====
        private UiRow[] GetSelectedUiRows()
            => MainList.SelectedItems.Cast<UiRow>().ToArray();

        // V7.5.6 修正：加入 createIfNotExist 參數
        private static void OpenInExplorer(string? path, bool createIfNotExist = false)
        {
            if (string.IsNullOrWhiteSpace(path)) { MessageBox.Show("路徑為空。"); return; }

            if (File.Exists(path))
            {
                TryStart("explorer.exe", $"/select,\"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                TryStart("explorer.exe", $"\"{path}\"");
            }
            else if (createIfNotExist)
            {
                try
                {
                    Directory.CreateDirectory(path);
                    TryStart("explorer.exe", $"\"{path}\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"建立並開啟資料夾失敗：{path}\n{ex.Message}");
                }
            }
            else
            {
                MessageBox.Show($"找不到路徑：{path}");
            }
        }

        private static void TryStart(string fileName, string? args = null)
        {
            try { Process.Start(new ProcessStartInfo { FileName = fileName, Arguments = args ?? "", UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "啟動失敗"); }
        }

        private static string StatusToLabel(string? s)
        {
            var v = (s ?? "").ToLowerInvariant();
            return v switch
            {
                "committed" => "已提交",
                "error" => "錯誤",
                "" or null => "未處理",
                "intaked" => "未處理",
                _ when v.StartsWith("stage") => "暫存",
                _ => v
            };
        }

        // ===== Converters & Comparers =====
        #region Converters & Comparers
        // V7.5.9 修正：從 private 改為 public，解決 XAML 編譯錯誤
        public sealed class StatusToLabelConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => StatusToLabel(value as string);
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => Binding.DoNothing;
        }

        // V7.5.9 修正：從 private 改為 public，解決 XAML 編譯錯誤
        public sealed class StatusToBrushConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var status = (values[0] as string)?.ToLowerInvariant() ?? "";
                Brush pick(string key) => key switch
                {
                    "commit" => new SolidColorBrush(Color.FromRgb(0x34, 0xA8, 0x53)),
                    "stage" => new SolidColorBrush(Color.FromRgb(0xF4, 0xB4, 0x00)),
                    "error" => new SolidColorBrush(Color.FromRgb(0xEA, 43, 35)),
                    _ => new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
                };
                return status switch
                {
                    "committed" => pick("commit"),
                    "error" => pick("error"),
                    "" or null => pick("unset"),
                    "intaked" => pick("unset"),
                    _ when status.StartsWith("stage") => pick("stage"),
                    _ => pick("unset")
                };
            }
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
                => throw new NotSupportedException();
        }

        private sealed class PropComparer<T> : IComparer
        {
            private readonly Func<T, string> _selector;
            private readonly ListSortDirection _dir;
            public PropComparer(Func<T, string> selector, ListSortDirection dir) { _selector = selector; _dir = dir; }
            public int Compare(object? x, object? y)
            {
                var a = x is T tx ? _selector(tx) : string.Empty;
                var b = y is T ty ? _selector(ty) : string.Empty;
                var r = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }

        private sealed class DateComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            public DateComparer(ListSortDirection dir) { _dir = dir; }
            public int Compare(object? x, object? y)
            {
                var a = x is UiRow rx ? rx.CreatedAt : DateTime.MinValue;
                var b = y is UiRow ry ? ry.CreatedAt : DateTime.MinValue;
                var r = a.CompareTo(b);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }

        private sealed class CategoryComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            private readonly RoutingService? _router;
            public CategoryComparer(ListSortDirection dir, RoutingService? router) { _dir = dir; _router = router; }
            public int Compare(object? x, object? y)
            {
                string cat(UiRow? r)
                {
                    if (r == null) return "";
                    var ext = "." + (r.Ext ?? "");
                    try { return _router?.MapExtensionToCategory(ext) ?? ext; }
                    catch { return ext; }
                }
                var cx = cat(x as UiRow);
                var cy = cat(y as UiRow);
                var rlt = string.Compare(cx, cy, StringComparison.OrdinalIgnoreCase);
                if (rlt == 0)
                {
                    var nx = (x as UiRow)?.FileName ?? "";
                    var ny = (y as UiRow)?.FileName ?? "";
                    rlt = string.Compare(nx, ny, StringComparison.OrdinalIgnoreCase);
                }
                return _dir == ListSortDirection.Ascending ? rlt : -rlt;
            }
        }

        private sealed class StatusComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            public StatusComparer(ListSortDirection dir) => _dir = dir;
            private static int Weight(string? s)
            {
                var v = (s ?? "").ToLowerInvariant();
                return v switch
                {
                    "error" => 0,
                    "" or null => 1,
                    "intaked" => 1,
                    _ when v.StartsWith("stage") => 2,
                    "committed" => 3,
                    _ => 1
                };
            }
            public int Compare(object? x, object? y)
            {
                var a = Weight((x as UiRow)?.Status);
                var b = Weight((y as UiRow)?.Status);
                var r = a.CompareTo(b);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }
        #endregion
    }
}

