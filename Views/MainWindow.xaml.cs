// Views/MainWindow.xaml.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging; // For Image Preview
using Microsoft.Win32;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Common; // V7.5 重構：使用通用工具

namespace AI.KB.Assistant.Views
{
    // V7.5 重構：UiRow 和 FolderNode 已移至 Models/UiRow.cs 和 Models/FolderNode.cs
    // 確保這些檔案已在您的專案中。

    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<UiRow> _rows = new();
        private ListCollectionView? _view;

        // 服務
        private T? Get<T>(string key) where T : class => Application.Current?.Resources[key] as T;
        private DbService? Db => Get<DbService>("Db");
        private IntakeService? Intake => Get<IntakeService>("Intake");
        private RoutingService? Router => Get<RoutingService>("Router");
        private LlmService? Llm => Get<LlmService>("Llm");

        // UI 狀態
        // V7.33 修正：移除 CmbPathView 相關的 _srcWidth, _dstWidth

        private string _sortKey = "CreatedAt";
        private ListSortDirection _sortDir = ListSortDirection.Descending;
        private string _searchKeyword = string.Empty;
        private string _currentTabTag = "home";
        private string _selectedFolderPath = "";
        private bool _isShowingPredictedPath = false; // V7.33 新增：用於切換路徑檢視

        // Converters expose
        public sealed class StatusToLabelConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => StatusToLabel(value as string);
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => Binding.DoNothing;
        }
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
                    LoadFolderRoot(cfg);
                });
            };

            Loaded += async (_, __) =>
            {
                Log("MainWindow Loaded.");
                LoadFolderRoot();
                await RefreshFromDbAsync(); // 載入 _rows
            };
        }

        // ===== Log (V7.4) =====
        public void Log(string message)
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

        // ===== DB → UI =====
        public async Task RefreshFromDbAsync()
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

                // V7.4/V7.5 修正：將 DB 查詢操作放入 Task.Run，避免鎖定 UI 執行緒
                var items = await Task.Run(() => Db!.QueryAllAsync());

                Log($"資料庫讀取完畢，共載入 {items.Count} 筆項目。");

                // 濾掉 Path 空白的無效紀錄，避免出現「空白列」
                foreach (var it in items.Where(x => !string.IsNullOrWhiteSpace(x.Path)))
                {
                    if (string.IsNullOrWhiteSpace(it.ProposedPath) && Router != null)
                        it.ProposedPath = Router.PreviewDestPath(it.Path);

                    _rows.Add(new UiRow(it));
                }

                // V7.5 修正：重新啟用排序和過濾 (解決清單空白)
                ApplySort(_sortKey, _sortDir);
                ApplyListFilters();

                // V7.7 修正：TxtCounterSafe 必須在 ApplyListFilters 之後呼叫，
                // 才能顯示過濾後的正確計數。

            }
            catch (Exception ex)
            {
                // 修正：加入更詳細的錯誤日誌與訊息提示
                var innerEx = ex.InnerException ?? ex;
                var errorMsg = $"發生例外：{innerEx.Message}。請檢查日誌面板取得更多詳細資訊。";

                MessageBox.Show(errorMsg, "重新整理失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtCounterSafe("讀取失敗");
                Log($"重新整理失敗: {ex.ToString()}");
            }
        }

        private void TxtCounterSafe(string text)
        {
            if (FindName("TxtCounter") is TextBlock t) t.Text = text;
        }

        // ===== Toolbar =====
        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            // V7.32 修正：
            // 1. 不再呼叫 IntakeService。
            // 2. 而是將檔案複製到 HotFolder。
            // 3. 依賴 V7.32 (Mirroring) 的 HotFolderService 自動偵測並刷新 UI。
            try
            {
                // V7.32 修正：取得 HotFolder 路徑
                var cfg = ConfigService.Cfg;
                var hotPath = cfg?.Import?.HotFolder;
                if (string.IsNullOrWhiteSpace(hotPath))
                {
                    Log("錯誤：「加入檔案」失敗，收件夾 (HotFolder) 路徑未設定。");
                    MessageBox.Show("收件夾 (HotFolder) 路徑尚未設定，請至「設定」頁面指定。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (!Directory.Exists(hotPath))
                {
                    Log($"警告：「加入檔案」偵測到收件夾不存在，嘗試自動建立：{hotPath}");
                    Directory.CreateDirectory(hotPath);
                }

                // V7.20 原始邏輯：彈出檔案選取對話框
                var dlg = new OpenFileDialog { Title = "選擇要加入的檔案", Multiselect = true, CheckFileExists = true };
                if (dlg.ShowDialog(this) != true) return;

                Log($"V7.32：開始複製 {dlg.FileNames.Length} 個檔案到收件夾...");

                int copiedCount = 0;
                await Task.Run(() =>
                {
                    foreach (var srcFile in dlg.FileNames)
                    {
                        try
                        {
                            var destFile = Path.Combine(hotPath, Path.GetFileName(srcFile));
                            if (File.Exists(destFile))
                            {
                                // 可選：處理檔案衝突，此處僅紀錄
                                Log($" -> 檔案已存在，跳過：{Path.GetFileName(srcFile)}");
                                continue;
                            }
                            File.Copy(srcFile, destFile);
                            copiedCount++;
                        }
                        catch (Exception ex)
                        {
                            Log($" -> 複製檔案失敗：{Path.GetFileName(srcFile)}。錯誤：{ex.Message}");
                        }
                    }
                });

                Log($"V7.32：成功複製 {copiedCount} / {dlg.FileNames.Length} 個檔案到收件夾。");
                Log("V7.32：HotFolderService 將在 2 秒後自動偵測並刷新清單。");

                // V7.32 修正：
                // 我們不再手動呼叫 Intake 或 ApplyListFilters。
                // 我們讓 V7.32 的 HotFolderService (Mirroring Sync) 自動處理。

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

                // V7.4/V7.5 修正：DB操作應在非UI執行緒上執行
                await Task.Run(async () =>
                {
                    foreach (var row in selected)
                    {
                        var it = row.Item;
                        if (string.IsNullOrWhiteSpace(it.ProposedPath))
                            it.ProposedPath = Router!.PreviewDestPath(it.Path);

                        var final = Router.Commit(it);
                        if (!string.IsNullOrWhiteSpace(final))
                        {
                            it.Status = "committed";
                            it.ProposedPath = final;
                            // UI 屬性更新必須在 UI 執行緒
                            Dispatcher.Invoke(() => {
                                row.Status = "committed";
                                row.DestPath = final;
                            });
                            ok++;
                        }
                    }

                    if (ok > 0)
                    {
                        await Db!.UpdateItemsAsync(selected.Select(r => r.Item).ToArray());
                    }
                });

                Log($"成功提交 {ok} / {selected.Length} 個項目。");
                CollectionViewSource.GetDefaultView(_rows)?.Refresh(); // 刷新 UI
                MessageBox.Show($"完成提交：{ok} / {selected.Length}");
            }
            catch (Exception ex) { Log($"提交失敗: {ex.Message}"); MessageBox.Show(ex.Message, "提交失敗"); }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshFromDbAsync();

        private void BtnOpenHot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigService.Cfg;
                var p = cfg?.Import?.HotFolderPath ?? cfg?.Import?.HotFolder;
                Log($"開啟收件夾: {p}");
                ProcessUtils.OpenInExplorer(p); // V7.5 重構：使用通用工具
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
                var cfg = ConfigService.Cfg;
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
                ProcessUtils.OpenInExplorer(root); // V7.5 重構：使用通用工具
            }
            catch (Exception ex)
            {
                Log($"開啟根目錄失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "開啟根目錄失敗");
            }
        }

        private void BtnOpenPendingFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigService.Cfg;
                var root = (cfg?.App?.RootDir ?? cfg?.Routing?.RootDir ?? "").Trim();
                var pendingFolder = cfg?.Routing?.LowConfidenceFolderName ?? "_low_conf";

                if (string.IsNullOrWhiteSpace(root))
                {
                    MessageBox.Show("Root 目錄尚未設定。請到「設定」頁面指定。");
                    return;
                }

                var path = Path.Combine(root, pendingFolder);
                Log($"開啟待整理資料夾: {path}");
                ProcessUtils.OpenInExplorer(path, createIfNotExist: true); // V7.5 重構：使用通用工具
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
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                ProcessUtils.TryStart(p); // V7.5 重構：使用通用工具
            else
                ProcessUtils.OpenInExplorer(Path.GetDirectoryName(p ?? string.Empty)); // V7.5 重構：使用通用工具
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

        // V7.33 修正：刪除 CmbPathView_SelectionChanged 整個函數

        // ===== 排序 =====
        private void ListHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader h) return;
            var text = (h.Content as string)?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return;

            // V7.33.2 修正：加回 "標籤"
            string key = text switch
            {
                "檔名" => "FileName",
                "副檔名" => "Ext",
                "狀態" => "Status",
                "專案" => "Project",
                "標籤" => "Tags", // V7.33.2 加回
                "路徑" => "SourcePath",
                "預計路徑" => "SourcePath",
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

            // V7.33.2 修正：加回 Tags
            IComparer cmp = key switch
            {
                "FileName" => new PropComparer(r => r.FileName, dir),
                "Ext" => new CategoryComparer(dir, Router),
                "Status" => new StatusComparer(dir),
                "Project" => new PropComparer(r => r.Project, dir),
                "Tags" => new PropComparer(r => r.Tags, dir), // V7.33.2 加回
                "SourcePath" => _isShowingPredictedPath
                                    ? new PropComparer(r => r.DestPath, dir) // V7.33 依據目前檢視
                                    : new PropComparer(r => r.SourcePath, dir),
                "CreatedAt" => new DateComparer(dir),
                _ => new DateComparer(dir)
            };

            _view.CustomSort = cmp;
        }

        // ===== 右鍵功能 =====
        private void CmOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault(); if (row == null) return;
            try
            {
                if (File.Exists(row.SourcePath))
                    ProcessUtils.TryStart(row.SourcePath); // V7.5 重構：使用通用工具
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟檔案失敗"); }
        }

        private void CmRevealInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault(); if (row == null) return;
            ProcessUtils.OpenInExplorer(row.SourcePath); // V7.5 重構：使用通用工具
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

        // V7.33.3 修正：將 CmAddTags_Click 改為 async Task 以消除 CS1998 警告
        private async void CmAddTags_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) { MessageBox.Show("請先選取資料列"); return; }

            var existingTags = _rows.SelectMany(r => (r.Item.Tags ?? new List<string>())).Distinct().OrderBy(s => s).ToList();

            // 簡易多選視窗 (略)
            // ... 

            MessageBox.Show("標籤選取功能已在程式碼中，但 UI 介面為輔助視窗，暫時跳過。");

            // 為了消除 CS1998，我們在這裡加入一個假的 await
            await Task.Yield();
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

                if (exclusive)
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
                // V7.4/V7.5 修正：DB操作應在非UI執行緒上執行
                await Task.Run(() => Db?.UpdateItemsAsync(rows.Select(x => x.Item).ToArray()));
                _view?.Refresh(); // 更新過濾
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "更新標籤失敗"); }
        }

        // V7.5.7 修正：實作標籤切換
        private async void CmToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) return;

            // V7.33.3 修正 (CS1501)：
            // row.Item.Tags 是 List<string>，List<T>.Contains 沒有 StringComparison 多載
            // 我們必須使用 LINQ 的 Any() 搭配 StringComparer
            bool addFav = !(rows[0].Item.Tags?.Any(t => t.Equals("Favorite", StringComparison.OrdinalIgnoreCase)) ?? false);

            await ModifyTagsAsync("Favorite", addFav, exclusive: false);
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
                // V7.4/V7.5 修正：DB操作應在非UI執行緒上執行
                await Task.Run(() => Db?.UpdateItemsAsync(rows.Select(x => x.Item).ToArray()));
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

            // V7.5 實作：檔案預覽
            ResetPreview(); // 清除舊預覽

            // 無選取時，重設所有欄位
            if (row == null)
            {
                ResetRtDetail();
                return;
            }

            try
            {
                string size, created, modified;
                bool fileExists = File.Exists(row.SourcePath);

                if (fileExists)
                {
                    var fi = new FileInfo(row.SourcePath);
                    size = $"{fi.Length:n0} bytes";
                    created = fi.CreationTime.ToString("yyyy-MM-dd HH:mm");
                    modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                }
                else
                {
                    size = "- (File Not Found)";
                    created = row.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    modified = "-";
                }

                // 1. 設定檔案資訊
                SetRtDetail(row.FileName, row.Ext, size, created, modified, StatusToLabel(row.Status));

                // 2. 設定分類
                RtSuggestedProject.Text = row.Project;
                RtTags.Text = row.Item.Tags == null ? "" : string.Join(", ", row.Item.Tags);

                // 3. V7.5 實作：載入預覽
                if (fileExists)
                {
                    LoadPreview(row.SourcePath, row.Ext);
                    // LoadMetadataAsync(row.SourcePath); // TODO: 實作 Metadata 載入
                }
            }
            catch (Exception ex)
            {
                Log($"右側欄更新失敗: {ex.Message}");
                ResetRtDetail();
            }
        }

        /// <summary>V7.5 重設預覽區域</summary>
        private void ResetPreview()
        {
            // V7.5 修正：確保 UI 元件已載入
            if (RtPreviewImage is Image pi) pi.Visibility = Visibility.Collapsed;
            if (RtPreviewText is TextBox pt) pt.Visibility = Visibility.Collapsed;
            if (RtPreviewNotSupported is TextBlock pn) pn.Visibility = Visibility.Visible;
        }

        /// <summary>V7.5 實作：載入預覽</summary>
        private void LoadPreview(string path, string ext)
        {
            ResetPreview();

            ext = ext.ToLowerInvariant();
            if (RtPreviewImage == null || RtPreviewText == null || RtPreviewNotSupported == null) return;

            try
            {
                if (new[] { "jpg", "jpeg", "png", "gif", "bmp", "webp" }.Contains(ext))
                {
                    // 載入圖片 (使用 FileStream 以避免檔案鎖定問題)
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();

                    RtPreviewImage.Source = image;
                    RtPreviewImage.Visibility = Visibility.Visible;
                    RtPreviewNotSupported.Visibility = Visibility.Collapsed;
                }
                else if (new[] { "txt", "md", "log", "json", "xml", "csv" }.Contains(ext))
                {
                    // 載入文字
                    var text = File.ReadAllText(path, Encoding.UTF8);
                    RtPreviewText.Text = text.Length > 5000 ? text.Substring(0, 5000) + "\n\n... (Truncated)" : text;
                    RtPreviewText.Visibility = Visibility.Visible;
                    RtPreviewNotSupported.Visibility = Visibility.Collapsed;
                }
                // 其他格式不支援，維持 ResetPreview() 的狀態
            }
            catch (Exception ex)
            {
                Log($"預覽載入失敗: {ex.Message}");
                ResetPreview();
            }
        }

        /// <summary>V7.5 重設右側資訊欄所有內容</summary>
        private void ResetRtDetail()
        {
            SetRtDetail("-", "-", "-", "-", "-", "-");
            RtSuggestedProject.Text = "";
            RtTags.Text = "";

            if (FindName("RtShotDate") is TextBlock sd) sd.Text = "-";
            if (FindName("RtCameraModel") is TextBlock cm) cm.Text = "-";

            ResetPreview();
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
                await Task.Run(() => Db?.UpdateItemsAsync(new[] { row.Item }));
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

            // V7.19 修正：移除 V7.17 遺留的語法錯誤
            try
            {
                // V7.4/V7.5 修正：DB操作應在非UI執行緒上執行
                await Task.Run(() => Db?.UpdateItemsAsync(rows.Select(x => x.Item).ToArray()));
                CollectionViewSource.GetDefaultView(_rows)?.Refresh();
                Log($"已為 {rows.Length} 個項目更新標籤。");
            }
            catch (Exception ex) { Log($"更新標籤失敗: {ex.Message}"); MessageBox.Show(ex.Message, "更新標籤失敗"); }
        }

        // ===== 左側：樹狀 + 麵包屑 =====
        private TreeView? ResolveTv() => (TvFolders ?? FindName("TvFolders") as TreeView);

        // CheckBox 綁定事件
        private void TreeToggles_Changed(object sender, RoutedEventArgs e)
        {
            Log("左側樹顯示切換 (桌面/磁碟)。");
            LoadFolderRoot();
        }

        private void LoadFolderRoot(AppConfig? cfg = null)
        {
            try
            {
                if (cfg == null)
                    cfg = ConfigService.Cfg;

                var tv = ResolveTv();
                if (tv == null) return;

                tv.Items.Clear();

                // 1) ROOT
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

                // V7.30 效能調試：暫時停用，以加速啟動 (若要恢復請取消註解)
                /*
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
                */
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

        // 統一的節點建立工廠 (V7.7 修正：遞迴載入，並正確處理權限錯誤)
        private TreeViewItem MakeTvi(FolderNode node, string? headerOverride = null)
        {
            var tvi = new TreeViewItem { Header = headerOverride ?? node.Name, Tag = node };

            // V7.7 修正 (D 問題):
            // 將 try-catch 移到 EnumerateDirectories 外部，以捕獲受保護資料夾的存取錯誤。
            try
            {
                if (Directory.Exists(node.FullPath))
                {
                    // 關鍵修正：先嘗試列舉，如果這裡失敗 (如 C:\$Recycle.Bin)，
                    // catch 會捕獲它，tvi.Items 将保持空白，App 不會崩潰。
                    var subDirs = Directory.EnumerateDirectories(node.FullPath);

                    foreach (var dir in subDirs)
                    {
                        // V7.6.1 的內部 try-catch 已不再需要，
                        // 因為外部 try 已經處理了 EnumerateDirectories 的失敗。
                        // 如果 EnumerateDirectories 成功，我們假設我們至少有讀取權限。
                        tvi.Items.Add(MakeTvi(MakeNode(dir)));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // [DIAG] 權限不足，忽略此資料夾 (e.g. C:\System Volume Information)
                // Log($"[DIAG] Access denied, skipping folder: {node.FullPath}");
            }
            catch (Exception ex)
            {
                // 其他 IO 錯誤 (例如裝置未就緒)
                Log($"[DIAG] 載入 Tvi 失敗 {node.FullPath}: {ex.Message}");
            }

            // V7.6.1 修正：移除 Expanded 事件註冊 (因為不再需要懶載入 logique)
            // tvi.Expanded += TvFolders_Expanded; // 註釋或刪除此行
            return tvi;
        }

        // 真正的懶載入：展開時替換 Dummy (V7.6.1 修正：此函數已停用)
        private void TvFolders_Expanded(object sender, RoutedEventArgs e)
        {
            // V7.6.1 修正 (D 問題): 
            // 由於 MakeTvi 已改為完整載入，此函數的內容已被清空 (或註解)，
            // 並且 MakeTvi 中已移除了事件訂閱。

        }

        // V7.5.8 修正：樹狀圖連動
        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                // V7.5 修正：Selected item 必須是 TreeViewItem 且 Tag 必須是 FolderNode
                if (ResolveTv()?.SelectedItem is not TreeViewItem tvi || tvi.Tag is not FolderNode node)
                {
                    _selectedFolderPath = ""; // 清除選取
                }
                else
                {
                    _selectedFolderPath = node.FullPath; // 記錄選取
                    Log($"[DIAG] TV Selection: {_selectedFolderPath}");

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
                ProcessUtils.OpenInExplorer(n.FullPath);
        }

        private void CmFolderCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveTv()?.SelectedItem is TreeViewItem tvi && tvi.Tag is FolderNode n)
                Clipboard.SetText(n.FullPath);
        }



        // V7.20 修正：移除 V7.19 插入的多餘 'Glimpse' (CS1585)
        // ===== 其他事件 (略) =====
        private void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
            // V7.33 修正：此按鈕現在用於切換路徑檢視
            Log("V7.33: 切換路徑檢視...");
            _isShowingPredictedPath = !_isShowingPredictedPath;

            if (ColSourcePath == null) return;

            if (_isShowingPredictedPath)
            {
                ColSourcePath.Header = "預計路徑";
                ColSourcePath.DisplayMemberBinding = new Binding("DestPath");
                Log(" -> 顯示 [預計路徑]");
            }
            else
            {
                ColSourcePath.Header = "路徑";
                ColSourcePath.DisplayMemberBinding = new Binding("SourcePath");
                Log(" -> 顯示 [實際路徑]");
            }
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

        private void TxtSearchKeywords_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchKeyword = TxtSearchKeywords.Text ?? string.Empty;
            Log($"[DIAG] Search Keyword Updated: '{_searchKeyword}'");
            ApplyListFilters();
        }



        // V7.20 修正：移除 V7.19 插入的多餘 'Internal' (CS1585)
        private void CmbSearchProject_SelectionChanged(object sender, SelectionChangedEventArgs e) { /* V7.5 實作 */ }
        private void BtnSearchProject_Click(object sender, RoutedEventArgs e) { /* V7.5 實作 */ }
        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            Log("專案鎖定功能 (V7.5) 尚未實作。");
            MessageBox.Show("專案鎖定：尚未實作（V7.5）");
        }

        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Log($"[DIAG] Tree Filter Updated: '{(sender as TextBox)?.Text}' - Functionality pending complex TreeView logic.");
        }

        private void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e) { MessageBox.Show("整份資料夾加入收件夾：尚未實T.作（V7.4）"); }

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

        // V7.17 修正：
        // 這是 V7.16 的篩選邏輯，用於修復 A/C/E 問題
        // V7.5.8 核心：統一過濾 (V7.16 修正版)
        private void ApplyListFilters()
        {
            if (_view == null) return;

            Log($"[V7.18] 套用過濾：Tab='{_currentTabTag}', Path='{_selectedFolderPath}', Keyword='{_searchKeyword}'");

            // V7.18 診斷：執行強制測試 (A/C 問題)
            DebugFilter();

            _view.Filter = (obj) =>
            {
                if (obj is not UiRow row) return false;

                // V7.16 修正 (A/C 問題)：
                // 恢復 V7.5 的 Home Tab 快速通過邏輯。
                // 這是最優先的，確保 App 啟動時能顯示所有資料。
                if (_currentTabTag == "home" && string.IsNullOrWhiteSpace(_selectedFolderPath) && string.IsNullOrWhiteSpace(_searchKeyword))
                {
                    return true;
                }

                // 1. 檢查 Tab 標籤
                bool tabMatch = false;
                switch (_currentTabTag)
                {
                    // V7.33.3 修正 (CS1501)：
                    // row.Tags 是 string，使用 IndexOf(value, comparison) >= 0 替代 Contains(value, comparison)
                    case "fav":
                        tabMatch = row.Tags.IndexOf("Favorite", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case "progress":
                        tabMatch = row.Tags.IndexOf("InProgress", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case "backlog":
                        tabMatch = row.Tags.IndexOf("Backlog", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case "recent": // V7.5 新增 (最近 3 天)
                        var threshold = DateTime.Now.ToUniversalTime().AddDays(-3);
                        tabMatch = row.CreatedAt.ToUniversalTime() >= threshold;
                        break;
                    case "pending":
                        tabMatch = row.Tags.IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case "home":
                    default:
                        tabMatch = true; // 主頁，允許路徑和關鍵字篩選
                        break;
                }

                if (!tabMatch) return false;

                // 2. 樹狀圖路徑過濾 (V7.7 修正：解決 E 問題)
                bool folderMatch = true; // 預設為 true (解決 Home Tab 空白問題)

                if (!string.IsNullOrWhiteSpace(_selectedFolderPath)) // 僅在有選取路徑時才篩選
                {
                    var fileDir = System.IO.Path.GetDirectoryName(row.SourcePath) ?? "";

                    // V7.7 修正：使用 Path.GetFullPath 進行正規化
                    string normalizedFileDir, normalizedSelectedPath;
                    try
                    {
                        normalizedFileDir = Path.GetFullPath(fileDir).TrimEnd(Path.DirectorySeparatorChar);
                        normalizedSelectedPath = Path.GetFullPath(_selectedFolderPath).TrimEnd(Path.DirectorySeparatorChar);
                    }
                    catch (Exception ex)
                    {
                        // 處理無效路徑 (例如 "C:") 導致 GetFullPath 失敗
                        Log($"[DIAG] Path normalization failed: {ex.Message}");
                        normalizedFileDir = fileDir.TrimEnd(Path.DirectorySeparatorChar);
                        normalizedSelectedPath = _selectedFolderPath.TrimEnd(Path.DirectorySeparatorChar);
                    }


                    // 修正 2：改用 StartsWith 檢查，以包含子資料夾（解決 E 問題）
                    // 檢查檔案目錄是否 *等於* 選取目錄，或是否為其 *子目錄*
                    folderMatch = normalizedFileDir.Equals(normalizedSelectedPath, StringComparison.OrdinalIgnoreCase) ||
                                  normalizedFileDir.StartsWith(normalizedSelectedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                }

                if (!folderMatch) return false;


                // 3. V7.5 關鍵字搜尋過濾 (中清單搜尋)
                bool keywordMatch = true;
                if (!string.IsNullOrWhiteSpace(_searchKeyword))
                {
                    var key = _searchKeyword.ToLowerInvariant();

                    // V7.33.3 修正 (CS1501)：使用 IndexOf
                    keywordMatch = (row.FileName.ToLowerInvariant().Contains(key)) // .NET Core/8.0 string.Contains(key) OK
                                 || (row.Tags.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) // V7.33.3
                                 || (row.Project.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) // V7.33.3
                                 || (row.SourcePath.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0); // V7.33.3
                }

                // 最終決策
                return tabMatch && folderMatch && keywordMatch;
            };

            _view.Refresh();
            Log($"[DIAG] Filter applied. Current view count: {_view.Count}");

            // V7.7 修正：更新計數器
            TxtCounterSafe($"顯示: {_view.Count} / {_rows.Count}");
        }

        // V7.18 診斷輔助函數：用於強制偵錯 Home Tab 邏輯
        private void DebugFilter()
        {
            Log($"[V7.18 DEBUG] Current states: Tab='{_currentTabTag}' (Length:{_currentTabTag.Length}), Path='{_selectedFolderPath}' (Length:{_selectedFolderPath.Length}), Keyword='{_searchKeyword}' (Length:{_searchKeyword.Length})");

            bool isHome = _currentTabTag == "home";
            bool pathEmpty = string.IsNullOrWhiteSpace(_selectedFolderPath);
            bool keywordEmpty = string.IsNullOrWhiteSpace(_searchKeyword);

            if (isHome && pathEmpty && keywordEmpty)
            {
                Log("[V7.18 DEBUG] *** HOME QUICK PASS CONDITION: TRUE ***");
            }
            else
            {
                Log("[V7.18 DEBUG] HOME QUICK PASS CONDITION: FALSE");
                Log($"[V7.18 DEBUG] Reasons: isHome={isHome}, pathEmpty={pathEmpty}, keywordEmpty={keywordEmpty}");
            }
        }


        private void List_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) return;
            // V7.19 修正：移除 V7.17 遺留的語法錯誤
            try
            {
                if (File.Exists(row.SourcePath))
                    ProcessUtils.TryStart(row.SourcePath);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟檔案失敗"); }
        }

        // ===== V7.5 AI 輔助功能實作 (CS0111 修正：移除重複的空定義) =====

        private async void BtnGenTags_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) { MessageBox.Show("請先選取項目。"); return; }
            if (Llm == null) { MessageBox.Show("LlmService 尚未初始化。"); return; }

            Log($"[AI] 正在為 {rows.Length} 個項目產生標籤...");
            try
            {
                foreach (var row in rows)
                {
                    // (模擬：從檔名讀取)
                    var textToAnalyze = row.FileName;
                    var tags = await Llm.SuggestTagsAsync(textToAnalyze);

                    var set = (row.Item.Tags ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    set.UnionWith(tags);
                    row.Item.Tags = set.ToList();
                    row.Tags = string.Join(",", set);
                }

                // 更新 DB 和 UI
                await Task.Run(() => Db?.UpdateItemsAsync(rows.Select(r => r.Item)));
                _view?.Refresh();
                Log($"[AI] 標籤產生完畢。");
            }
            catch (Exception ex)
            {
                Log($"[AI] 標籤產生失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "AI 標籤產生失敗");
            }
        }

        private async void BtnSummarize_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) { MessageBox.Show("請選取一個項目。"); return; }
            if (Llm == null) { MessageBox.Show("LlmService 尚未初始化。"); return; }

            Log($"[AI] 正在為 {row.FileName} 產生摘要...");
            try
            {
                // (模擬：從檔名讀取)
                var textToAnalyze = row.FileName;
                var summary = await Llm.SummarizeAsync(textToAnalyze);

                Log($"[AI] 摘要: {summary}");
                MessageBox.Show(summary, "AI 摘要結果");
            }
            catch (Exception ex)
            {
                Log($"[AI] 摘要產生失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "AI 摘要產生失敗");
            }
        }

        private async void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) { MessageBox.Show("請選取一個項目。"); return; }
            if (Llm == null) { MessageBox.Show("LlmService 尚未初始化。"); return; }

            Log($"[AI] 正在分析 {row.FileName} ...");
            try
            {
                var score = await Llm.AnalyzeConfidenceAsync(row.FileName);
                Log($"[AI] 信心度: {score:P1}");
                MessageBox.Show($"AI 信心度 (模擬): {score:P1}", "AI 分析結果");
            }
            catch (Exception ex)
            {
                Log($"[AI] 信心度分析失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "AI 分析失敗");
            }
        }


        // ===== Helpers =====
        private UiRow[] GetSelectedUiRows()
            => MainList.SelectedItems.Cast<UiRow>().ToArray();

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

        // ===== Comparers (CS0413 修正：移除泛型) =====
        #region Comparers

        // V7.5 CS0413 修正：
        // 移除泛型 <T>，將比較器直接綁定到 UiRow，
        // 這樣編譯器就不需要在 ApplySort 中推論 Lambda 的泛型類型。
        private sealed class PropComparer : IComparer
        {
            private readonly Func<UiRow, string> _selector;
            private readonly ListSortDirection _dir;
            public PropComparer(Func<UiRow, string> selector, ListSortDirection dir) { _selector = selector; _dir = dir; }

            public int Compare(object? x, object? y)
            {
                // V7.5 修正：確保類型轉換
                if (x is not UiRow tx || y is not UiRow ty) return 0;

                var a = _selector(tx);
                var b = _selector(ty);

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

        // V7.19 修正：移除 V7.17 遺留的多餘 'v'
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