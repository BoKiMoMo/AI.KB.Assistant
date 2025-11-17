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
using AI.KB.Assistant.Common;
using System.Globalization;
using System.Windows.Shapes; // [V20.0] I/O 依賴
// [V20.2] TagPicker 依賴
using AI.KB.Assistant.Views;

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V20.3 (程式碼清理版)
    /// 1. [V20.2] 實作「專案鎖定」功能。
    /// 2. [V20.2] 實作「專案篩選」下拉選單功能。
    /// 3. [V20.2] 實作「標籤選取器」(TagPicker) 彈出視窗。
    /// 4. [V20.2] 實作「資料夾匯入」功能 (Tree_MoveFolderToInbox_Click)。
    /// 5. [V20.2] 修正 (CS0104) 'Path' 模稜兩可的參考。
    /// 6. [V20.3] 新增 `MenuScanShallow_Click` 和 `MenuScanRecursive_Click`。
    /// 7. [V20.3] 移除 V7.5 遺留的空事件 (BtnSearchProject_Click, CmStageToInbox_Click...)
    /// </summary>
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
        private HotFolderService? HotFolder => Get<HotFolderService>("HotFolder");

        // UI 狀態
        private string _sortKey = "CreatedAt";
        private ListSortDirection _sortDir = ListSortDirection.Descending;
        private string _searchKeyword = string.Empty;
        private string _currentTabTag = "home";
        private string _selectedFolderPath = "";
        private string _projectFilter = string.Empty; // [V20.2] 專案篩選
        private bool _isShowingPredictedPath = false;
        private bool _hideCommitted = false;

        // Converters expose
        public sealed class StatusToLabelConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                => StatusToLabel(value as string);
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => Binding.DoNothing;
        }
        public sealed class StatusToBrushConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
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
                    // [V20.1] 黑名單使用 "error" 顏色 (紅色)
                    "blacklisted" => pick("error"),
                    "" or null => pick("unset"),
                    "intaked" => pick("unset"),
                    _ when status.StartsWith("stage") => pick("stage"),
                    _ => pick("unset")
                };
            }
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
                => throw new NotSupportedException();
        }

        public static readonly IValueConverter StatusToLabelConverterInstance = new StatusToLabelConverter();
        public static readonly IMultiValueConverter StatusToBrushConverterInstance = new StatusToBrushConverter();

        public MainWindow()
        {
            InitializeComponent();
            Log("MainWindow Initializing (V20.3)...");

            MainList.ItemsSource = _rows;
            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
            ApplySort(_sortKey, _sortDir);
            Log("MainList CollectionView bound.");

            ConfigService.ConfigChanged += (cfg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log("偵測到設定變更，已重新載入 Router/Llm 並刷新 V13.0 左側樹。");
                    try
                    {
                        Router?.ApplyConfig(cfg);
                        Llm?.UpdateConfig(cfg);
                    }
                    catch (Exception ex)
                    {
                        Log($"ConfigChanged 處理失敗: {ex.Message}");
                    }
                    _ = RefreshFromDbAsync();
                    // (V13.0) 
                    LoadFolderRoot(cfg);
                });
            };

            if (HotFolder != null)
            {
                HotFolder.FilesChanged += HotFolder_FilesChanged;
            }

            Loaded += async (_, __) =>
            {
                Log("MainWindow Loaded.");
                LoadFolderRoot();
                await RefreshFromDbAsync();
            };
        }

        private async void HotFolder_FilesChanged()
        {
            await Dispatcher.Invoke(async () =>
            {
                Log("[V17.0] HotFolderService 偵測到檔案變更，正在刷新 UI...");
                await RefreshFromDbAsync();
            });
        }


        // ===== Log (V7.4) =====
        public void Log(string message)
        {
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

                if (Db == null || Router == null)
                {
                    TxtCounterSafe("DB/Router 尚未初始化");
                    Log("錯誤：DbService/RoutingService 尚未初始化 (null)。");
                    return;
                }

                var items = await Task.Run(() => Db!.QueryAllAsync());
                Log($"資料庫讀取完畢，共載入 {items.Count} 筆項目。");

                // [V20.0] 取得目前的設定檔以計算類別
                var currentCfg = ConfigService.Cfg;

                foreach (var it in items.Where(x => !string.IsNullOrWhiteSpace(x.Path)))
                {
                    if (string.IsNullOrWhiteSpace(it.ProposedPath))
                        it.ProposedPath = Router.PreviewDestPath(it.Path);

                    // [V20.0] 計算中文類別
                    // [V20.2] (Fix CS0104) 
                    var ext = System.IO.Path.GetExtension(it.Path);
                    var category = Router.MapExtensionToCategoryConfig(ext, currentCfg);

                    // [V20.0] 引用 V20.0 'UiRow' (包含 Category)
                    _rows.Add(new UiRow(it, category));
                }

                // ===== [V20.2] 專案篩選：填入 ComboBox =====
                try
                {
                    var currentProject = CmbSearchProject.SelectedItem as string;
                    var allProjects = _rows.Select(r => r.Project).Where(p => !string.IsNullOrWhiteSpace(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();

                    allProjects.Insert(0, "[所有專案]");
                    CmbSearchProject.ItemsSource = allProjects;

                    // 嘗試恢復先前的選取
                    if (!string.IsNullOrWhiteSpace(currentProject) && allProjects.Contains(currentProject))
                    {
                        CmbSearchProject.SelectedItem = currentProject;
                        // _projectFilter 會在 SelectionChanged 事件中被設定，但如果選取沒有變更，我們需要手動更新
                        _projectFilter = (currentProject == "[所有專案]") ? string.Empty : currentProject;
                    }
                    else
                    {
                        CmbSearchProject.SelectedIndex = 0;
                        _projectFilter = string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[V20.2] 填入專案篩選清單失敗: {ex.Message}");
                }
                // ===== [V20.2] 結束 =====

                ApplySort(_sortKey, _sortDir);
                ApplyListFilters();

            }
            catch (Exception ex)
            {
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
            try
            {
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
                            // [V20.2] (Fix CS0104) 
                            var destFile = System.IO.Path.Combine(hotPath, System.IO.Path.GetFileName(srcFile));
                            if (File.Exists(destFile))
                            {
                                // [V20.2] (Fix CS0104) 
                                Log($" -> 檔案已存在，跳過：{System.IO.Path.GetFileName(srcFile)}");
                                continue;
                            }
                            File.Copy(srcFile, destFile);
                            copiedCount++;
                        }
                        catch (Exception ex)
                        {
                            // [V20.2] (Fix CS0104) 
                            Log($" -> 複製檔案失敗：{System.IO.Path.GetFileName(srcFile)}。錯誤：{ex.Message}");
                        }
                    }
                });

                Log($"V7.32：成功複製 {copiedCount} / {dlg.FileNames.Length} 個檔案到收件夾。");
                Log("V7.32：HotFolderService 將在 2 秒後自動偵測並刷新清單。");

            }
            catch (Exception ex) { Log($"加入檔案失敗: {ex.Message}"); MessageBox.Show(ex.Message, "加入檔案失敗"); }
        }

        /// <summary>
        /// [V20.3] 手動觸發掃描 (僅第一層)
        /// </summary>
        private async void MenuScanShallow_Click(object sender, RoutedEventArgs e)
        {
            Log("[V20.3] 手動觸發掃描 (僅第一層)...");
            if (HotFolder == null)
            {
                Log("[V20.3] 錯誤：HotFolderService 未初始化。");
                return;
            }

            try
            {
                await HotFolder.ScanAsync(SearchOption.TopDirectoryOnly);
                Log("[V20.3] 掃描 (第一層) 觸發完畢。");
                MessageBox.Show(this, "已觸發 [掃描第一層]。\n\n背景服務將開始處理。", "掃描已觸發", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"[V20.3] 掃描 (第一層) 失敗: {ex.Message}");
                MessageBox.Show(this, $"掃描 (第一層) 失敗:\n{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// [V20.3] 手動觸發掃描 (遞迴)
        /// </summary>
        private async void MenuScanRecursive_Click(object sender, RoutedEventArgs e)
        {
            Log("[V20.3] 手動觸發掃描 (遞迴)...");
            if (HotFolder == null)
            {
                Log("[V20.3] 錯誤：HotFolderService 未初始化。");
                return;
            }

            try
            {
                await HotFolder.ScanAsync(SearchOption.AllDirectories);
                Log("[V20.3] 掃描 (遞迴) 觸發完畢。");
                MessageBox.Show(this, "已觸發 [遞迴掃描]。\n\n背景服務將開始處理。", "掃描已觸發", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"[V20.3] 掃描 (遞迴) 失敗: {ex.Message}");
                MessageBox.Show(this, $"掃描 (遞迴) 失敗:\n{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

                // ===== [V20.2 專案鎖定] 開始 =====
                string? lockedProjectName = null;
                if (BtnLockProject.IsChecked == true)
                {
                    // [V20.2] 從 ComboBox 取得鎖定的專案名稱 (V20.1: .Text -> V20.2: .SelectedItem)
                    lockedProjectName = CmbSearchProject.SelectedItem as string;

                    // 如果選的是 "[所有專案]"，則視為無效鎖定
                    if (string.IsNullOrWhiteSpace(lockedProjectName) || lockedProjectName == "[所有專案]")
                    {
                        Log("錯誤：專案已鎖定，但未選取有效的專案名稱。");
                        MessageBox.Show("專案鎖定已啟用，但未在專案下拉選單中指定有效的專案名稱。", "鎖定錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                        // (V20.2) 自動解除鎖定
                        BtnLockProject.IsChecked = false;
                        BtnLockProject_Click(BtnLockProject, new RoutedEventArgs()); // 手動觸發更新 UI
                        return; // 中斷提交
                    }
                    Log($"[V20.2] 專案已鎖定：將強制使用專案 '{lockedProjectName}'。");
                }
                // ===== [V20.2 專案鎖定] 結束 =====

                Log($"開始提交 {selected.Length} 個項目...");
                int ok = 0;

                await Task.Run(async () =>
                {
                    foreach (var row in selected)
                    {
                        // [V20.1] 防呆：跳過黑名單 [cite:"黑名單用途只是不餐與分類"] 項目
                        if (row.Status?.Equals("blacklisted", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            continue;
                        }

                        var it = row.Item;

                        // ===== [V20.2 專案鎖定] 套用至 Data Model =====
                        // 必須在 Router.Commit 之前設定 Item (it) 的 Project 屬性
                        if (lockedProjectName != null)
                        {
                            it.Project = lockedProjectName;
                        }
                        // ===== [V20.2 專案鎖定] 結束 =====

                        if (string.IsNullOrWhiteSpace(it.ProposedPath))
                            it.ProposedPath = Router!.PreviewDestPath(it.Path);

                        var final = Router.Commit(it);
                        if (!string.IsNullOrWhiteSpace(final))
                        {
                            it.Status = "committed";
                            it.ProposedPath = final;
                            Dispatcher.Invoke(() => {
                                row.Status = "committed";
                                row.DestPath = final;

                                // ===== [V20.2 專案鎖定] 套用至 UI Model (On Success) =====
                                if (lockedProjectName != null)
                                {
                                    row.Project = lockedProjectName;
                                }
                                // ===== [V20.2 專案鎖定] 結束 =====
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
                CollectionViewSource.GetDefaultView(_rows)?.Refresh();
                MessageBox.Show($"完成提交：{ok} / {selected.Length}");
            }
            catch (Exception ex) { Log($"提交失敗: {ex.Message}"); MessageBox.Show(ex.Message, "提交失敗"); }
        }

        // (V16.1) 

        private async void BtnClearCommitted_Click(object sender, RoutedEventArgs e)
        {
            Log("[V7.35-Opt.B] 開始清除已分類的實體檔案...");
            var cfg = ConfigService.Cfg;
            var hotPath = cfg?.Import?.HotFolder;

            if (Db == null || string.IsNullOrWhiteSpace(hotPath))
            {
                Log(" -> 錯誤：DbService 未初始化或 HotFolder 未設定。");
                MessageBox.Show("DbService 未初始化或 HotFolder 未設定。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string hotFolderFullPath;
            try
            {
                // [V20.2] (Fix CS0104) 
                hotFolderFullPath = System.IO.Path.GetFullPath(hotPath);
            }
            catch (Exception ex)
            {
                Log($" -> 錯誤：HotFolder 路徑無效: {hotPath} ({ex.Message})");
                MessageBox.Show($"HotFolder 路徑無效: {hotPath}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }


            var q = "您確定要永久刪除 [收件夾] 中所有 [已分類 (Committed)] 的實體檔案嗎？\n\n" +
                    "此操作主要用於「複製」模式。\n" +
                    "（此操作無法復原，且只會刪除收件夾內的檔案）";

            if (MessageBox.Show(q, "確認刪除 (選項B)", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                Log(" -> 操作已取消。");
                return;
            }

            try
            {
                var allItems = await Task.Run(() => Db.QueryAllAsync());
                var committedInInbox = allItems.Where(it =>
                    (it.Status == "committed") &&
                    !string.IsNullOrWhiteSpace(it.Path) &&
                    // [V20.2] (Fix CS0104) 
                    System.IO.Path.GetFullPath(it.Path).StartsWith(hotFolderFullPath, StringComparison.OrdinalIgnoreCase)
                ).ToList();

                if (committedInInbox.Count == 0)
                {
                    Log(" -> 檢查完畢：在收件夾中找不到已分類 (Committed) 的檔案。");
                    MessageBox.Show("在收件夾中找不到已分類 (Committed) 的檔案。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Log($" -> 發現 {committedInInbox.Count} 筆已分類的紀錄在收件夾中，開始刪除實體檔案...");
                int deletedFiles = 0;
                int failedFiles = 0;

                await Task.Run(() =>
                {
                    foreach (var item in committedInInbox)
                    {
                        try
                        {
                            if (File.Exists(item.Path))
                            {
                                File.Delete(item.Path);
                                deletedFiles++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"  -> 刪除檔案失敗: {item.Path} ({ex.Message})");
                            failedFiles++;
                        }
                    }
                });

                Log($" -> 成功刪除 {deletedFiles} 個實體檔案，失敗 {failedFiles} 個。");
                Log(" -> 正在從資料庫中移除這些紀錄...");

                var deletedIds = committedInInbox.Select(it => it.Id!).ToList();
                await Task.Run(() => Db.DeleteItemsAsync(deletedIds));

                Log(" -> 資料庫紀錄已清除。");
                MessageBox.Show($"清理完畢。\n\n成功刪除 {deletedFiles} 個實體檔案。\n（{failedFiles} 個檔案刪除失敗）", "清理完成 (選項B)", MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshFromDbAsync();
            }
            catch (Exception ex)
            {
                Log($" -> [V7.35-Opt.B] 發生嚴重錯誤: {ex.Message}");
                MessageBox.Show($"發生嚴重錯誤：\n{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnResetInbox_Click(object sender, RoutedEventArgs e)
        {
            Log("[V7.35-Opt.C] 開始重置未處理的資料庫狀態...");
            if (Db == null)
            {
                Log(" -> 錯誤：DbService 未初始化。");
                MessageBox.Show("DbService 未初始化。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var q = "您確定要重置 [收件夾] 狀態嗎？\n\n" +
                    "此操作將刪除資料庫中所有 [未處理 (Intaked/Error)] 的紀錄。\n" +
                    "這將使 HotFolder 監控器在下次掃描時重新匯入它們。\n\n" +
                    "（此操作不會刪除您的任何實體檔案）";

            if (MessageBox.Show(q, "確認重置 (選項C)", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                Log(" -> 操作已取消。");
                return;
            }

            try
            {
                int deletedCount = await Task.Run(() => Db.DeleteNonCommittedAsync());

                Log($" -> 重置完畢。共 {deletedCount} 筆未處理的紀錄已從資料庫刪除。");
                MessageBox.Show($"重置完畢。\n\n共 {deletedCount} 筆未處理的紀錄已從資料庫刪除。\nHotFolder 將在 2 秒後重新掃描。", "重置完成 (選項C)", MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshFromDbAsync();
            }
            catch (Exception ex)
            {
                Log($" -> [V7.35-Opt.C] 發生嚴重錯誤: {ex.Message}");
                MessageBox.Show($"發生嚴重錯誤：\n{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenHot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigService.Cfg;
                var p = cfg?.Import?.HotFolder;
                Log($"開啟收件夾: {p}");
                ProcessUtils.OpenInExplorer(p);
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
                    // [V20.2] (Fix CS0104) 
                    root = System.IO.Path.GetDirectoryName(root)!;

                // [V20.2] (Fix CS0104) 
                root = root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

                if (!Directory.Exists(root))
                {
                    Log($"錯誤：根目錄不存在: {root}");
                    MessageBox.Show($"Root 目錄不存在：{root}");
                    return;
                }

                Log($"開啟根目錄: {root}");
                ProcessUtils.OpenInExplorer(root);
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

                // [V20.2] (Fix CS0104) 
                var path = System.IO.Path.Combine(root, pendingFolder);
                Log($"開啟待整理資料夾: {path}");
                ProcessUtils.OpenInExplorer(path, createIfNotExist: true);
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
            var p = cfg?.Db?.DbPath;
            Log($"開啟資料庫: {p}");
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                ProcessUtils.TryStart(p);
            else
                // [V20.2] (Fix CS0104) 
                ProcessUtils.OpenInExplorer(System.IO.Path.GetDirectoryName(p ?? string.Empty));
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("開啟設定Window...");
                new SettingsWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
                Log("設定Window已關閉。");
            }
            catch (Exception ex) { Log($"開啟設定失敗: {ex.Message}"); MessageBox.Show(ex.Message, "開啟設定失敗"); }
        }

        private void BtnGoToSimpleMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("切換到簡易模式...");
                var launcherWin = new LauncherWindow();

                if (Application.Current != null)
                {
                    Application.Current.MainWindow = launcherWin;
                }
                launcherWin.Show();

                this.Close();
            }
            catch (Exception ex)
            {
                Log($"切換簡易模式失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "切換失敗");
            }
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
                // [V20.0] 新增
                "類別" => "Category",
                "狀態" => "Status",
                "專案" => "Project",
                "標籤" => "Tags",
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

            IComparer cmp = key switch
            {
                "FileName" => new PropComparer(r => r.FileName, dir),
                "Ext" => new PropComparer(r => r.Ext, dir), // [V20.0] 簡化 (CategoryComparer 已不需要)
                "Category" => new PropComparer(r => r.Category, dir), // [V20.0] 新增
                "Status" => new StatusComparer(dir),
                "Project" => new PropComparer(r => r.Project, dir),
                "Tags" => new PropComparer(r => r.Tags, dir),
                "SourcePath" => _isShowingPredictedPath
                                    ? new PropComparer(r => r.DestPath, dir)
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
                    ProcessUtils.TryStart(row.SourcePath);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟檔案失敗"); }
        }

        private void CmRevealInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault(); if (row == null) return;
            ProcessUtils.OpenInExplorer(row.SourcePath);
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

        /// <summary>
        /// [V20.2] 輔助方法：將一組標籤 *取代* 指定項目的標籤
        /// </summary>
        /// <param name="rows">要修改的 UiRow 陣列</param>
        /// <param name="newTags">要套用的新標籤列表</param>
        private async Task ApplyTagSetAsync(UiRow[] rows, List<string> newTags)
        {
            if (rows.Length == 0) return;

            var newTagsNormalized = newTags
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Log($"[V20.2] 正在為 {rows.Length} 個項目套用 {newTagsNormalized.Count} 個標籤...");

            foreach (var r in rows)
            {
                r.Item.Tags = newTagsNormalized;
                r.Tags = string.Join(",", newTagsNormalized);
            }

            try
            {
                await Task.Run(() => Db?.UpdateItemsAsync(rows.Select(x => x.Item).ToArray()));

                if (rows.Length == 1)
                {
                    RefreshRtTags(rows[0]);
                }
                _view?.Refresh();
                Log($"[V20.2] {rows.Length} 個項目的標籤已更新。");
            }
            catch (Exception ex)
            {
                Log($"[V20.2] 套用標籤失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "更新標籤失敗");
            }
        }

        /// <summary>
        /// [V7.5.8] (保留) 處理標籤的 *新增* / *移除* (用於快速鍵)
        /// </summary>
        private async Task ModifyTagsAsync(string tag, bool add, bool exclusive = false)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) return;

            Log($"V7.5.8: 正在為 {rows.Length} 個項目 {(add ? "加入" : "移除")} 標籤 '{tag}'。");

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
                await Task.Run(() => Db?.UpdateItemsAsync(rows.Select(x => x.Item).ToArray()));

                if (rows.Length == 1)
                {
                    RefreshRtTags(rows[0]);
                }
                _view?.Refresh();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "更新標籤失敗"); }
        }

        private async void CmToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) return;
            bool addFav = !(rows[0].Item.Tags?.Any(t => t.Equals("Favorite", StringComparison.OrdinalIgnoreCase)) ?? false);
            await ModifyTagsAsync("Favorite", addFav, exclusive: false);
        }

        private async void CmAddTagProgress_Click(object sender, RoutedEventArgs e)
            => await ModifyTagsAsync("InProgress", add: true, exclusive: true);

        private async void CmAddTagBacklog_Click(object sender, RoutedEventArgs e)
            => await ModifyTagsAsync("Backlog", add: true, exclusive: true);

        private async void CmAddTagPending_Click(object sender, RoutedEventArgs e)
            => await ModifyTagsAsync("Pending", add: true, exclusive: true);

        /// <summary>
        /// [V20.2] 實作標籤選取器
        /// </summary>
        private async void CmAddTags_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0)
            {
                MessageBox.Show("請先在清單中選取要編輯標籤的項目。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Log($"[V20.2] 開啟標籤選取器，目標 {rows.Length} 個項目。");

            // 1. 取得所有已知的標籤
            var allKnownTags = _rows
                .SelectMany(r => r.Item.Tags ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 2. 取得目前選取項目的所有標籤
            var currentTags = rows
                .SelectMany(r => r.Item.Tags ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 3. 開啟對話框
            var dlg = new TagPickerWindow(allKnownTags, currentTags) { Owner = this };

            if (dlg.ShowDialog() == true)
            {
                // 4. 使用者點擊「確定」，取得回傳的標籤
                var newTags = dlg.SelectedTags;
                Log($"[V20.2] 標籤選取器回傳 {newTags.Count} 個標籤，正在套用...");

                // 5. 套用標籤 (使用 V20.2 新的輔助方法)
                await ApplyTagSetAsync(rows, newTags);
            }
            else
            {
                Log("[V20.2] 標籤選取器已取消。");
            }
        }

        /// <summary>
        /// [V20.2 重構] 移除所有標籤
        /// </summary>
        private async void CmRemoveAllTags_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) return;

            // 呼叫新的輔助方法，傳入空列表
            await ApplyTagSetAsync(rows, new List<string>());
        }

        // [V20.3] 移除 CmStageToInbox_Click 和 CmClassify_Click

        private void CmCommit_Click(object sender, RoutedEventArgs e) => BtnCommit_Click(sender, e);

        // ===== 右側資訊欄 (V7.4/V7.5) =====
        private void MainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault();
            ResetPreview();
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

                SetRtDetail(row.FileName, row.Ext, size, created, modified, StatusToLabel(row.Status));
                RtSuggestedProject.Text = row.Project;
                RefreshRtTags(row);

                if (fileExists)
                {
                    LoadPreview(row.SourcePath, row.Ext);
                }
            }
            catch (Exception ex)
            {
                Log($"右側欄更新失敗: {ex.Message}");
                ResetRtDetail();
            }
        }

        private void ResetPreview()
        {
            if (RtPreviewImage is Image pi) pi.Visibility = Visibility.Collapsed;
            if (RtPreviewText is TextBox pt) pt.Visibility = Visibility.Collapsed;
            if (RtPreviewNotSupported is TextBlock pn) pn.Visibility = Visibility.Visible;
        }

        private void LoadPreview(string path, string ext)
        {
            ResetPreview();
            ext = ext.ToLowerInvariant();
            if (RtPreviewImage == null || RtPreviewText == null || RtPreviewNotSupported == null) return;

            try
            {
                if (new[] { "jpg", "jpeg", "png", "gif", "bmp", "webp" }.Contains(ext))
                {
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
                    var text = File.ReadAllText(path, Encoding.UTF8);
                    RtPreviewText.Text = text.Length > 5000 ? text.Substring(0, 5000) + "\n\n... (Truncated)" : text;
                    RtPreviewText.Visibility = Visibility.Visible;
                    RtPreviewNotSupported.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log($"預覽載入失敗: {ex.Message}");
                ResetPreview();
            }
        }

        private void ResetRtDetail()
        {
            SetRtDetail("-", "-", "-", "-", "-", "-");
            RtSuggestedProject.Text = "";
            RtTags.Text = "";
            if (FindName("RtShotDate") is TextBlock sd) sd.Text = "-";
            if (FindName("RtCameraModel") is TextBlock cm) cm.Text = "-";
            ResetPreview();
        }

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

        private void RefreshRtTags(UiRow row)
        {
            if (RtTags != null)
            {
                RtTags.Text = row?.Item?.Tags == null ? "" : string.Join(", ", row.Item.Tags);
            }
        }

        private async void RtQuickTag_Favorite_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) return;
            bool addFav = !(rows[0].Item.Tags?.Any(t => t.Equals("Favorite", StringComparison.OrdinalIgnoreCase)) ?? false);
            await ModifyTagsAsync("Favorite", addFav, exclusive: false);
        }

        private async void RtQuickTag_InProgress_Click(object sender, RoutedEventArgs e)
            => await ModifyTagsAsync("InProgress", add: true, exclusive: true);

        private async void RtQuickTag_Backlog_Click(object sender, RoutedEventArgs e)
            => await ModifyTagsAsync("Backlog", add: true, exclusive: true);

        private async void RtQuickTag_Pending_Click(object sender, RoutedEventArgs e)
            => await ModifyTagsAsync("Pending", add: true, exclusive: true);

        private void RtQuickTag_Clear_Click(object sender, RoutedEventArgs e)
            => CmRemoveAllTags_Click(sender, e);

        /// <summary>
        /// [V20.2 重構] 右側面板「套用標籤」按鈕
        /// </summary>
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
                                 .ToList(); // 輔助方法 ApplyTagSetAsync 會處理 Trim 和 Distinct

            await ApplyTagSetAsync(rows, newTags);
        }

        // ===== 左側：樹狀 + 麵包屑 =====
        private TreeView? ResolveTv() => (TvFolders ?? FindName("TvFolders") as TreeView);

        // [V20.1] 檔案樹重新整理
        private void BtnRefreshTree_Click(object sender, RoutedEventArgs e)
        {
            Log("[V20.1] 手動重新整理檔案樹...");
            LoadFolderRoot();
        }


        /// <summary>
        /// [V13.0 修正] 載入 AppConfig 中定義的自訂路徑
        /// </summary>
        private void LoadFolderRoot(AppConfig? cfg = null)
        {
            try
            {
                if (cfg == null)
                    cfg = ConfigService.Cfg;

                var tv = ResolveTv();
                if (tv == null) return;

                tv.Items.Clear();

                var rootPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var appRoot = (cfg?.App?.RootDir ?? cfg?.Routing?.RootDir ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(appRoot))
                {
                    rootPaths.Add(appRoot);
                }

                if (cfg?.App?.TreeViewRootPaths != null)
                {
                    foreach (var customPath in cfg.App.TreeViewRootPaths)
                    {
                        rootPaths.Add(customPath.Trim());
                    }
                }

                foreach (var path in rootPaths)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    if (Directory.Exists(path))
                    {
                        var node = MakeNode(path);
                        var header = string.Equals(path, appRoot, StringComparison.OrdinalIgnoreCase)
                            ? $"ROOT：{node.Name}"
                            : node.Name;
                        tv.Items.Add(MakeTvi(node, headerOverride: header));
                    }
                    else
                    {
                        Log($"警告：檔案樹根目錄不存在: {path}");
                    }
                }
            }
            catch (Exception ex) { Log($"LoadFolderRoot failed: {ex.Message}"); }
        }

        private static FolderNode MakeNode(string path)
        {
            // [V20.2] (Fix CS0104) 
            var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name)) name = trimmed;
            return new FolderNode { Name = name, FullPath = trimmed };
        }

        private TreeViewItem MakeTvi(FolderNode node, string? headerOverride = null)
        {
            var tvi = new TreeViewItem { Header = headerOverride ?? node.Name, Tag = node };

            try
            {
                if (Directory.Exists(node.FullPath))
                {
                    // [V20.2] 修正：檢查權限前先檢查是否有子目錄
                    // 感謝 https://stackoverflow.com/a/1790757
                    if (Directory.EnumerateDirectories(node.FullPath).Any())
                    {
                        tvi.Items.Add(null);
                        tvi.Expanded += TvFolders_Expanded;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 沒有權限，當作沒有子目錄處理
            }
            catch (Exception ex)
            {
                Log($"[DIAG] 載入 Tvi 失敗 {node.FullPath}: {ex.Message}");
            }

            return tvi;
        }

        private void TvFolders_Expanded(object sender, RoutedEventArgs e)
        {
            var tvi = sender as TreeViewItem;
            if (tvi == null || tvi.Items.Count != 1 || tvi.Items[0] != null)
            {
                return;
            }

            if (tvi.Tag is not FolderNode node) return;
            Log($"[Lazy Load] 展開: {node.FullPath}");

            try
            {
                tvi.Items.Clear();
                // [V20.2] 修正：僅列舉子目錄
                var subDirs = Directory.EnumerateDirectories(node.FullPath);
                foreach (var dir in subDirs)
                {
                    try
                    {
                        // [V20.2] 增加一層 try-catch，避免因單一資料夾權限問題導致整個展開失敗
                        tvi.Items.Add(MakeTvi(MakeNode(dir)));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Log($"[Lazy Load] 權限不足 (子項): {dir}");
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                Log($"[Lazy Load] 權限不足 (父項): {node.FullPath}");
            }
            catch (Exception ex)
            {
                Log($"[Lazy Load] 展開 Tvi 失敗 {node.FullPath}: {ex.Message}");
            }
        }

        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                if (ResolveTv()?.SelectedItem is not TreeViewItem tvi || tvi.Tag is not FolderNode node)
                {
                    _selectedFolderPath = "";
                }
                else
                {
                    _selectedFolderPath = node.FullPath;
                    Log($"[DIAG] TV Selection: {_selectedFolderPath}");

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

                ApplyListFilters();
            }
            catch { }
        }

        /// <summary>
        /// (V11.2) 處理麵包屑點擊事件 (無 LAG)
        /// </summary>
        private void BreadcrumbItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBlock tb) return;
            if (tb.DataContext is not FolderNode node) return;

            Log($"[V11.2] 麵包屑點擊: {node.FullPath}");

            // 1. 更新主清單的過濾路徑
            _selectedFolderPath = node.FullPath;
            ApplyListFilters();
        }

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

        /// <summary>
        /// [V20.3] '檢視分類' (切換路徑) 功能保留，現在從選單呼叫
        /// </summary>
        private void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
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

        // (V7.6) 檔案樹過濾
        private void TxtSearchKeywords_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchKeyword = TxtSearchKeywords.Text ?? string.Empty;
            Log($"[DIAG] Search Keyword Updated: '{_searchKeyword}'");
            ApplyListFilters();
        }

        // (V7.6) 檔案樹過濾
        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = (sender as TextBox)?.Text ?? "";
            Log($"[Filter] 正在過濾檔案樹: '{filter}'");

            var tv = ResolveTv();
            if (tv == null) return;

            var filterLower = filter.ToLowerInvariant();

            foreach (var item in tv.Items)
            {
                if (item is TreeViewItem tvi)
                {
                    bool visible = FilterNode(tvi, filterLower);
                    tvi.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void ChkHideCommitted_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                _hideCommitted = chk.IsChecked == true;
                Log($"V7.34: 過濾已分類 (Hide Committed) = {_hideCommitted}");
                ApplyListFilters();
            }
        }

        /// <summary>
        /// [V20.2] 實作專案篩選
        /// </summary>
        private void CmbSearchProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cmb) return;

            // 如果專案已鎖定，則不允許變更篩選器
            if (BtnLockProject.IsChecked == true)
            {
                Log("[V20.2] 專案已鎖定，篩選器變更被忽略。");
                return;
            }

            var selectedProject = cmb.SelectedItem as string;

            if (selectedProject == "[所有專案]")
                _projectFilter = string.Empty;
            else
                _projectFilter = selectedProject ?? string.Empty;

            Log($"[V20.2] 專案篩選器變更為: '{_projectFilter}'");
            ApplyListFilters();
        }

        // [V20.3] 移除 BtnSearchProject_Click

        /// <summary>
        /// [V20.2] 實作專案鎖定按鈕 (ToggleButton)
        /// </summary>
        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            if (BtnLockProject.IsChecked == true)
            {
                // === 嘗試鎖定 ===
                var projectName = CmbSearchProject.SelectedItem as string; // V20.2: .Text -> .SelectedItem

                // 如果選的是 "[所有專案]" 或 null，則鎖定失敗
                if (string.IsNullOrWhiteSpace(projectName) || projectName == "[所有專案]")
                {
                    Log("[V20.2] 專案鎖定失敗：未選取有效專案。");
                    MessageBox.Show("請先從下拉選單中選取一個有效專案，然後再鎖定。", "鎖定失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
                    BtnLockProject.IsChecked = false; // 自動彈回
                    return;
                }

                // 鎖定成功：停用 ComboBox 並提供視覺回饋
                CmbSearchProject.IsEnabled = false;
                BtnLockProject.Content = "✅ 已鎖定";
                // 使用一個醒目的顏色
                BtnLockProject.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
                BtnLockProject.Foreground = Brushes.White;
                Log($"[V20.2] 專案已鎖定於: '{projectName}'");
            }
            else
            {
                // === 解除鎖定 ===
                CmbSearchProject.IsEnabled = true;
                BtnLockProject.Content = "🔒 鎖定專案";
                // 恢復為預設樣式
                BtnLockProject.ClearValue(Control.BackgroundProperty);
                BtnLockProject.ClearValue(Control.ForegroundProperty);
                Log("[V20.2] 專案已解除鎖定。");
            }
        }

        /// <summary>
        /// [V20.2] 實作：將檔案樹中的資料夾「整份」匯入收件夾
        /// </summary>
        private async void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e)
        {
            // 1. 取得來源資料夾
            var selectedTvi = ResolveTv()?.SelectedItem as TreeViewItem;
            var selectedNode = selectedTvi?.Tag as FolderNode;
            if (selectedNode == null || !Directory.Exists(selectedNode.FullPath))
            {
                MessageBox.Show("請先在左側選取一個有效的資料夾。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string sourcePath = selectedNode.FullPath;

            // 2. 取得目的地 (HotFolder)
            var cfg = ConfigService.Cfg;
            var hotPath = cfg?.Import?.HotFolder;
            if (string.IsNullOrWhiteSpace(hotPath))
            {
                Log("錯誤：「資料夾匯入」失敗，收件夾 (HotFolder) 路徑未設定。");
                MessageBox.Show("收件夾 (HotFolder) 路徑尚未設定，請至「設定」頁面指定。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!Directory.Exists(hotPath))
            {
                Log($"警告：「資料夾匯入」偵測到收件夾不存在，嘗試自動建立：{hotPath}");
                Directory.CreateDirectory(hotPath);
            }

            // 3. 防呆：檢查是否將 HotFolder 移入 HotFolder
            try
            {
                // [V20.2] (Fix CS0104) 
                string fullSourcePath = System.IO.Path.GetFullPath(sourcePath);
                string fullHotPath = System.IO.Path.GetFullPath(hotPath);
                if (fullSourcePath.Equals(fullHotPath, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("無法將收件夾匯入其自身。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // [V20.2.1] 修正防呆邏輯：檢查 HotFolder 是否在 SourcePath *之內*
                if (fullHotPath.StartsWith(fullSourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("無法將包含收件夾的父資料夾匯入收件夾。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"[V20.2] 路徑檢查失敗: {ex.Message}");
                MessageBox.Show($"路徑檢查失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 4. 取得目標路徑 (選項 B：整包資料夾)
            string folderName = new DirectoryInfo(sourcePath).Name;
            // [V20.2] (Fix CS0104) 
            string destPath = System.IO.Path.Combine(hotPath, folderName);

            // 5. 檢查衝突
            if (Directory.Exists(destPath) || File.Exists(destPath))
            {
                MessageBox.Show($"匯入失敗：收件夾中已存在同名檔案或資料夾 '{folderName}'。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 6. 詢問使用者：移動 (Move) 或 複製 (Copy)
            string q = $"您要將資料夾 '{folderName}' 匯入到收件夾嗎？\n\n" +
                       "[是 (Yes)] = 移動 (Move)\n" +
                       "[否 (No)] = 複製 (Copy)\n" +
                       "[取消 (Cancel)] = 取消操作";

            MessageBoxResult choice = MessageBox.Show(q, "確認匯入資料夾", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel)
            {
                Log("[V20.2] 資料夾匯入已取消。");
                return;
            }

            bool isMove = (choice == MessageBoxResult.Yes);
            string operationName = isMove ? "移動" : "複製";

            // 7. 執行操作
            Log($"[V20.2] 開始 {operationName} 資料夾 '{sourcePath}' 到 '{destPath}'...");
            try
            {
                await Task.Run(() =>
                {
                    if (isMove)
                    {
                        Directory.Move(sourcePath, destPath);
                    }
                    else
                    {
                        CopyDirectoryRecursively(sourcePath, destPath);
                    }
                });

                Log($"[V20.2] {operationName} 完成。HotFolderService 將自動偵測變更。");
                MessageBox.Show($"資料夾 {operationName} 完成。\n\nHotFolder 服務將在背景自動處理新檔案。", "匯入成功", MessageBoxButton.OK, MessageBoxImage.Information);

                // 8. 如果是「移動」，則從檔案樹中移除該節點
                if (isMove && selectedTvi != null)
                {
                    var parentTvi = selectedTvi.Parent as ItemsControl;
                    parentTvi?.Items.Remove(selectedTvi);
                }
            }
            catch (Exception ex)
            {
                Log($"[V20.2] {operationName} 資料夾失敗: {ex.Message}");
                MessageBox.Show($"資料夾 {operationName} 失敗：\n{ex.Message}", "I/O 錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource is not TabControl tc) return;
            if (tc.SelectedItem is not TabItem ti) return;
            if (_view == null) return;

            _currentTabTag = (ti.Tag as string) ?? "home";
            Log($"切換 Tab: {_currentTabTag}");
            ApplyListFilters();
        }

        /// <summary>
        /// [V20.0 遞迴搜尋優化]
        /// </summary>
        private void ApplyListFilters()
        {
            if (_view == null) return;

            Log($"[V20.2] 套用過濾：Tab='{_currentTabTag}', Path='{_selectedFolderPath}', Keyword='{_searchKeyword}', HideCommitted='{_hideCommitted}', Project='{_projectFilter}'");

            bool isSearching = !string.IsNullOrWhiteSpace(_searchKeyword);

            _view.Filter = (obj) =>
            {
                if (obj is not UiRow row) return false;

                bool statusMatch = true;
                if (_hideCommitted)
                {
                    if (row.Status?.Equals("committed", StringComparison.OrdinalIgnoreCase) ?? false)
                    {
                        statusMatch = false;
                    }
                }
                if (!statusMatch) return false;

                bool tabMatch = false;
                switch (_currentTabTag)
                {
                    case "fav":
                        tabMatch = row.Tags.IndexOf("Favorite", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case "progress":
                        tabMatch = row.Tags.IndexOf("InProgress", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case "backlog":
                        tabMatch = row.Tags.IndexOf("Backlog", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case "recent":
                        var threshold = DateTime.Now.ToUniversalTime().AddDays(-3);
                        tabMatch = row.CreatedAt.ToUniversalTime() >= threshold;
                        break;
                    case "pending":
                        tabMatch = row.Tags.IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case "home":
                    default:
                        tabMatch = true;
                        break;
                }
                if (!tabMatch) return false;

                // [V20.2] 專案篩選
                bool projectMatch = true;
                if (!string.IsNullOrWhiteSpace(_projectFilter))
                {
                    projectMatch = row.Project.Equals(_projectFilter, StringComparison.OrdinalIgnoreCase);
                }
                if (!projectMatch) return false;

                bool folderMatch = true;
                if (!string.IsNullOrWhiteSpace(_selectedFolderPath))
                {
                    // [V20.2] (Fix CS0104) 
                    var fileDir = System.IO.Path.GetDirectoryName(row.SourcePath) ?? "";
                    string normalizedFileDir, normalizedSelectedPath;
                    try
                    {
                        // [V20.2] (Fix CS0104) 
                        normalizedFileDir = System.IO.Path.GetFullPath(fileDir).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                        normalizedSelectedPath = System.IO.Path.GetFullPath(_selectedFolderPath).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                    }
                    catch (Exception ex)
                    {
                        Log($"[DIAG] Path normalization failed: {ex.Message}");
                        // [V20.2] (Fix CS0104) 
                        normalizedFileDir = fileDir.TrimEnd(System.IO.Path.DirectorySeparatorChar);
                        normalizedSelectedPath = _selectedFolderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar);
                    }

                    // [V20.0 遞迴搜尋優化]
                    // 如果正在搜尋，則遞迴 (StartsWith)，否則非遞迴 (Equals)
                    if (isSearching)
                    {
                        folderMatch = normalizedFileDir.StartsWith(normalizedSelectedPath, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        folderMatch = normalizedFileDir.Equals(normalizedSelectedPath, StringComparison.OrdinalIgnoreCase);
                    }
                }
                if (!folderMatch) return false;

                bool keywordMatch = true;
                if (isSearching)
                {
                    var key = _searchKeyword.ToLowerInvariant();
                    keywordMatch = (row.FileName.ToLowerInvariant().Contains(key))
                                 || (row.Tags.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                                 || (row.Project.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                                 || (row.SourcePath.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (!keywordMatch) return false;

                return statusMatch && tabMatch && projectMatch && folderMatch && keywordMatch;
            };

            _view.Refresh();
            Log($"[DIAG] Filter applied. Current view count: {_view.Count}");
            TxtCounterSafe($"顯示: {_view.Count} / {_rows.Count}");
        }

        // ================================================================
        // [V20.0] 檔案 I/O 功能 (新增/重新命名/刪除)
        // ================================================================

        #region [V20.0] 檔案樹 (Tree) I/O 功能

        private async void Tree_NewFolder_Click(object sender, RoutedEventArgs e)
        {
            var selectedTvi = ResolveTv()?.SelectedItem as TreeViewItem;
            var selectedNode = selectedTvi?.Tag as FolderNode;

            if (selectedNode == null || !Directory.Exists(selectedNode.FullPath))
            {
                MessageBox.Show("請先在左側選取一個有效的父資料夾。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (ok, newName) = ShowInputDialog("新增資料夾", "請輸入新資料夾名稱：", "NewFolder");
            if (!ok) return;

            try
            {
                // [V20.2] (Fix CS0104) 
                var newPath = System.IO.Path.Combine(selectedNode.FullPath, newName);
                if (Directory.Exists(newPath))
                {
                    MessageBox.Show($"資料夾 '{newName}' 已存在。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Directory.CreateDirectory(newPath);
                Log($"[V20.0 I/O] 已建立資料夾: {newPath}");

                // [V20.1] 刷新父節點
                if (selectedTvi != null)
                {
                    selectedTvi.IsExpanded = false; // 關閉
                    selectedTvi.Items.Clear();      // 清空
                    selectedTvi.Items.Add(null);    // 加入 dummy
                    selectedTvi.IsExpanded = true;  // 重新展開 (觸發 TvFolders_Expanded)
                }
                else
                {
                    LoadFolderRoot(); // 備援：刷新整個樹
                }
            }
            catch (Exception ex)
            {
                Log($"[V20.0 I/O] 建立資料夾失敗: {ex.Message}");
                MessageBox.Show($"建立資料夾失敗：\n{ex.Message}", "I/O 錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Tree_Rename_Click(object sender, RoutedEventArgs e)
        {
            var tvi = ResolveTv()?.SelectedItem as TreeViewItem;
            var selectedNode = tvi?.Tag as FolderNode;

            if (selectedNode == null || !Directory.Exists(selectedNode.FullPath))
            {
                MessageBox.Show("請先在左側選取一個要重新命名的資料夾。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (ok, newName) = ShowInputDialog("重新命名資料夾", "請輸入新名稱：", selectedNode.Name);
            if (!ok || newName == selectedNode.Name) return;

            try
            {
                var oldPath = selectedNode.FullPath;
                // [V20.2] (Fix CS0104) 
                var parentDir = System.IO.Path.GetDirectoryName(oldPath);
                if (parentDir == null)
                {
                    MessageBox.Show("無法重新命名根目錄。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // [V20.2] (Fix CS0104) 
                var newPath = System.IO.Path.Combine(parentDir, newName);

                Directory.Move(oldPath, newPath);
                Log($"[V20.0 I/O] 已重新命名資料夾: {oldPath} -> {newPath}");

                // [V20.0 DB Sync] 
                await UpdateDbPathsAsync(oldPath, newPath);

                // [V20.1] 刷新 UI
                selectedNode.FullPath = newPath;
                selectedNode.Name = newName;
                if (tvi != null)
                {
                    tvi.Header = newName;
                }
            }
            catch (Exception ex)
            {
                Log($"[V20.0 I/O] 重新命名資料夾失敗: {ex.Message}");
                MessageBox.Show($"重新命名資料夾失敗：\n{ex.Message}", "I/O 錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Tree_Delete_Click(object sender, RoutedEventArgs e)
        {
            var tvi = ResolveTv()?.SelectedItem as TreeViewItem;
            var selectedNode = tvi?.Tag as FolderNode;

            if (selectedNode == null || !Directory.Exists(selectedNode.FullPath))
            {
                MessageBox.Show("請先在左側選取一個要刪除的資料夾。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var parentTvi = tvi?.Parent as ItemsControl;
            if (parentTvi == null)
            {
                MessageBox.Show("無法刪除根目錄。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var q = $"您確定要永久刪除資料夾 '{selectedNode.Name}' 及其所有內容嗎？\n\n（此操作無法復原）";
            if (MessageBox.Show(q, "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                var oldPath = selectedNode.FullPath;
                Directory.Delete(oldPath, recursive: true);
                Log($"[V20.0 I/O] 已刪除資料夾: {oldPath}");

                // [V20.0 DB Sync] 
                await DeleteDbPathsAsync(oldPath);

                // [V20.1] 
                parentTvi.Items.Remove(tvi);
            }
            catch (Exception ex)
            {
                Log($"[V20.0 I/O] 刪除資料夾失敗: {ex.Message}");
                MessageBox.Show($"刪除資料夾失敗：\n{ex.Message}", "I/O 錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region [V20.0] 中清單 (List) I/O 功能

        private async void Cm_Rename_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null || !File.Exists(row.SourcePath))
            {
                MessageBox.Show("請先選取一個有效的檔案。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var (ok, newName) = ShowInputDialog("重新命名檔案", "請輸入新檔名 (含副檔名)：", row.FileName);
            if (!ok || newName == row.FileName) return;

            try
            {
                var oldPath = row.SourcePath;
                // [V20.2] (Fix CS0104) 
                var parentDir = System.IO.Path.GetDirectoryName(oldPath)!;
                var newPath = System.IO.Path.Combine(parentDir, newName);

                File.Move(oldPath, newPath);
                Log($"[V20.0 I/O] 已重新命名檔案: {oldPath} -> {newPath}");

                // [V20.0 DB Sync] 
                row.Item.Path = newPath;
                await Db!.UpdateItemsAsync(new[] { row.Item });

                await RefreshFromDbAsync();
            }
            catch (Exception ex)
            {
                Log($"[V20.0 I/O] 重新命名檔案失敗: {ex.Message}");
                MessageBox.Show($"重新命名檔案失敗：\n{ex.Message}", "I/O 錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Cm_Delete_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0)
            {
                MessageBox.Show("請先選取要刪除的檔案。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var q = $"您確定要永久刪除選取的 {rows.Length} 個檔案嗎？\n\n（此操作無法復原）";
            if (MessageBox.Show(q, "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                var deletedIds = new List<string>();
                var itemsToRefresh = new List<Item>();

                foreach (var row in rows)
                {
                    try
                    {
                        if (File.Exists(row.SourcePath))
                        {
                            File.Delete(row.SourcePath);
                            Log($"[V20.0 I/O] 已刪除檔案: {row.SourcePath}");
                        }
                        if (row.Item.Id != null)
                        {
                            deletedIds.Add(row.Item.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[V20.0 I/O] 刪除檔案 {row.FileName} 失敗: {ex.Message}");
                        itemsToRefresh.Add(row.Item);
                    }
                }

                // [V20.0 DB Sync] 
                if (deletedIds.Count > 0)
                {
                    await Db!.DeleteItemsAsync(deletedIds);
                }

                await RefreshFromDbAsync();
            }
            catch (Exception ex)
            {
                Log($"[V20.0 I/O] 刪除檔案時發生 DB 錯誤: {ex.Message}");
                MessageBox.Show($"刪除檔案時發生資料庫錯誤：\n{ex.Message}", "I/O 錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CmDeleteRecord_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) return;

            var q = $"您確定要從資料庫中移除這 {rows.Length} 筆紀錄嗎？\n\n（注意：這*不會*刪除實體檔案）";
            if (MessageBox.Show(q, "確認刪除紀錄", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                var ids = rows.Select(r => r.Item.Id).Where(id => id != null).ToList()!;
                await Db!.DeleteItemsAsync(ids);
                await RefreshFromDbAsync();
            }
            catch (Exception ex) { Log($"刪除 DB 紀錄失敗: {ex.Message}"); }
        }

        #endregion

        #region [V20.0] I/O 輔助方法

        private FolderNode? GetSelectedTreeNode()
        {
            return (ResolveTv()?.SelectedItem as TreeViewItem)?.Tag as FolderNode;
        }

        private (bool Ok, string Text) ShowInputDialog(string title, string prompt, string defaultText)
        {
            var dlg = new InputDialog(title, prompt, defaultText) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                return (true, dlg.InputText);
            }
            return (false, "");
        }

        /// <summary>
        /// [V20.2] 輔助方法：遞迴複製資料夾 (用於資料夾匯入)
        /// </summary>
        private void CopyDirectoryRecursively(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"來源資料夾不存在: {dir.FullName}");

            // 取得來源資料夾中的所有子資料夾
            DirectoryInfo[] dirs = dir.GetDirectories();

            // 建立目的地資料夾
            Directory.CreateDirectory(destinationDir);

            // 複製所有檔案
            foreach (FileInfo file in dir.GetFiles())
            {
                // [V20.2] (Fix CS0104) 
                string targetFilePath = System.IO.Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            // 遞迴複製所有子資料夾
            foreach (DirectoryInfo subDir in dirs)
            {
                // [V20.2] (Fix CS0104) 
                string newDestinationDir = System.IO.Path.Combine(destinationDir, subDir.Name);
                CopyDirectoryRecursively(subDir.FullName, newDestinationDir);
            }
        }

        private async Task UpdateDbPathsAsync(string oldFolderPath, string newFolderPath)
        {
            if (Db == null) return;
            Log($"[V20.0 DB Sync] 正在更新 DB 路徑: {oldFolderPath} -> {newFolderPath}");
            var itemsToUpdate = new List<Item>();
            var allItems = await Task.Run(() => Db.QueryAllAsync()); // [V20.0.1] 從 DB 重抓

            foreach (var item in allItems)
            {
                if (item.Path.StartsWith(oldFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    // [V20.2] (Fix CS0104) 
                    item.Path = System.IO.Path.Combine(newFolderPath, item.Path.Substring(oldFolderPath.Length + 1));
                    itemsToUpdate.Add(item);
                }
            }

            if (itemsToUpdate.Count > 0)
            {
                await Db.UpdateItemsAsync(itemsToUpdate);
                Log($"[V20.0 DB Sync] 已更新 {itemsToUpdate.Count} 筆相關紀錄。");
                await RefreshFromDbAsync();
            }
        }

        private async Task DeleteDbPathsAsync(string folderPath)
        {
            if (Db == null) return;
            Log($"[V20.0 DB Sync] 正在刪除 DB 紀錄於: {folderPath}");
            var idsToDelete = new List<string>();
            var allItems = await Task.Run(() => Db.QueryAllAsync()); // [V20.0.1] 從 DB 重抓

            foreach (var item in allItems)
            {
                if (item.Path.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    idsToDelete.Add(item.Id);
                }
            }

            if (idsToDelete.Count > 0)
            {
                await Db.DeleteItemsAsync(idsToDelete);
                Log($"[V20.0 DB Sync] 已刪除 {idsToDelete.Count} 筆相關紀錄。");
                await RefreshFromDbAsync();
            }
        }

        #endregion

        // ================================================================

        private void List_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) return;

            try
            {
                if (File.Exists(row.SourcePath))
                    ProcessUtils.TryStart(row.SourcePath);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟檔案失敗"); }
        }

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
                    var textToAnalyze = row.FileName;
                    var tags = await Llm.SuggestTagsAsync(textToAnalyze);

                    var set = (row.Item.Tags ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    set.UnionWith(tags);
                    row.Item.Tags = set.ToList();
                    row.Tags = string.Join(",", set);
                }

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
                MessageBox.Show($"AI 信心度 (V10.2): {score:P1}", "AI 分析結果");
            }
            catch (Exception ex)
            {
                Log($"[AI] 信心度分析失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "AI 分析失敗");
            }
        }

        /// <summary>
        /// [V10.2 新增] AI 產生專案名稱
        /// </summary>
        private async void BtnGenProject_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) { MessageBox.Show("請選取一個項目。"); return; }
            if (Llm == null) { MessageBox.Show("LlmService 尚未初始化。"); return; }
            if (Router == null) { MessageBox.Show("RouterService 尚未初始化。"); return; }

            Log($"[AI] 正在為 {row.FileName} 產生專案名稱...");
            try
            {
                var suggestion = await Llm.SuggestProjectAsync(row.FileName);

                if (!string.IsNullOrWhiteSpace(suggestion))
                {
                    Log($"[AI] 專案建議 (LLM): {suggestion}");
                    RtSuggestedProject.Text = suggestion;
                }
                else
                {
                    Log("[AI] API Key 未設定，使用本地規則 (V17.0 月份)。");

                    DateTime ts = row.Item.Timestamp ?? row.CreatedAt;
                    var monthGuess = ts.ToString("MM");
                    Log($"[AI] 專案建議 (V17.0 本地規則 - 月份): {monthGuess}");
                    RtSuggestedProject.Text = monthGuess;
                }
            }
            catch (Exception ex)
            {
                Log($"[AI] 專案名稱產生失敗: {ex.Message}");
                MessageBox.Show(ex.Message, "AI 專案名稱產生失敗");
            }
        }


        // ===== Helpers =====
        private UiRow[] GetSelectedUiRows()
            => MainList.SelectedItems.Cast<UiRow>().ToArray();

        /// <summary>
        /// [V20.1] 狀態標籤轉換
        /// </summary>
        private static string StatusToLabel(string? s)
        {
            var v = (s ?? "").ToLowerInvariant();
            return v switch
            {
                // [V20.1] 新增 "blacklisted" 狀態
                "blacklisted" => "黑名單",
                "committed" => "已提交",
                "error" => "錯誤",
                "" or null => "未處理",
                "intaked" => "未處理",
                _ when v.StartsWith("stage") => "暫存",
                _ => v
            };
        }

        /// <summary>
        /// (V7.6) 遞迴過濾 TreeViewItem 及其子項。
        /// [V19.1 CS0103 修復]
        /// </summary>
        private bool FilterNode(TreeViewItem item, string filterLower)
        {
            bool headerMatches = false;
            if (item.Header is string header)
            {
                headerMatches = header.ToLowerInvariant().Contains(filterLower);
            }

            bool anyChildMatches = false;

            if (item.Items.Count == 1 && item.Items[0] == null)
            {
                // 「懶載入」Dummy 節點 - 僅依標頭決定
            }
            else if (item.Items.Count > 0)
            {
                foreach (var child in item.Items)
                {
                    if (child is TreeViewItem childTvi)
                    {
                        // [V19.1 CS0103 修復] tvi -> childTvi
                        if (FilterNode(childTvi, filterLower))
                        {
                            anyChildMatches = true;
                        }
                    }
                }
            }

            bool isVisible = headerMatches || anyChildMatches;
            item.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            return isVisible;
        }


        // ===== Comparers =====
        #region Comparers
        private sealed class PropComparer : IComparer
        {
            private readonly Func<UiRow, string> _selector;
            private readonly ListSortDirection _dir;
            public PropComparer(Func<UiRow, string> selector, ListSortDirection dir) { _selector = selector; _dir = dir; }

            public int Compare(object? x, object? y)
            {
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

        // [V20.0] 簡化：CategoryComparer 已不再需要

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
                    // [V20.1] 黑名單排序權重同 "error"
                    "blacklisted" => 0,
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