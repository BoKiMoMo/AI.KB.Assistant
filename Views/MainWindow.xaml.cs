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
using System.Globalization; // [V9.1 修正] 確保引用

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V19.0 (V18.0 回滾 P2 + V19.0 P3)
    /// 1. (V17.0) 修正 V16.1 測試清單中的 5 個 BUG (V17.0 [cite: `Services/ConfigService.cs (V17.0)`], V17.0 [cite: `Services/HotFolderService.cs (V17.1)`], V17.0 [cite: `Services/RoutingService.cs (V17.0)`], V17.0 [cite: `Views/MainWindow.xaml.cs (V17.0)`])
    /// 2. [V19.0 回滾 P2] 移除 V18.0 [cite: `Views/MainWindow.xaml.cs (V18.0)`] (V17.1 P2 需求) 的 'Folder' 邏輯：
    ///    - 'StatusToLabelConverter' [Line 72]
    ///    - 'StatusToBrushConverter' [Line 82]
    ///    - 'MainList_SelectionChanged' [Line 662]
    ///    - 'List_DoubleClick' [Line 1007] (移除 'if (row.IsFolder)' [cite: `Views/MainWindow.xaml.cs (V18.0)` Line 1011])
    ///    - 'StatusComparer' [Line 1294]
    ///    - 'ApplyListFilters' [Line 906] (移除 'if (row.Item.IsFolder)' [cite: `Views/MainWindow.xaml.cs (V19.0)` Line 917])
    /// 3. [V19.0 修正 P1 BUG] 'ApplyListFilters' [Line 906] (V17.1 P1/P2 需求)
    ///    V16.1 [cite: `Views/MainWindow.xaml.cs (V16.1)` Line 958] 'StartsWith' (遞迴) 
    ///    V19.0 (Line 958) 修正回 'Equals' (非遞迴)，以匹配 V19.0 [cite: `Services/HotFolderService.cs (V19.0)`] (V18.1 P1) [cite: `Services/HotFolderService.cs (V19.0)` Line 116] 'TopDirectoryOnly'。
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
        // (V17.0)
        private HotFolderService? HotFolder => Get<HotFolderService>("HotFolder");

        // UI 狀態
        private string _sortKey = "CreatedAt";
        private ListSortDirection _sortDir = ListSortDirection.Descending;
        private string _searchKeyword = string.Empty;
        private string _currentTabTag = "home";
        private string _selectedFolderPath = "";
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
                // [V19.0 回滾 P2] 移除 V18.0 [cite: `Views/MainWindow.xaml.cs (V18.0)` Line 94] "folder"
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
                    // [V19.0 回滾 P2] 移除 V18.0 [cite: `Views/MainWindow.xaml.cs (V18.0)` Line 94] "folder"
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
            Log("MainWindow Initializing (V19.0)...");

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

            // (V17.0)
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

        /// <summary>
        /// (V17.0)
        /// </summary>
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

                if (Db == null)
                {
                    TxtCounterSafe("DB 尚未初始化");
                    Log("錯誤：DbService 尚未初始化 (null)。");
                    return;
                }

                var items = await Task.Run(() => Db!.QueryAllAsync());
                Log($"資料庫讀取完畢，共載入 {items.Count} 筆項目。");

                foreach (var it in items.Where(x => !string.IsNullOrWhiteSpace(x.Path)))
                {
                    if (string.IsNullOrWhiteSpace(it.ProposedPath) && Router != null)
                        it.ProposedPath = Router.PreviewDestPath(it.Path);

                    // [V19.0 新增 P3] 引用 V19.0 [cite: `Models/UiRow.cs (V19.0)`] UiRow (包含 FileIcon [cite: `Models/UiRow.cs (V19.0)` Line 103])
                    _rows.Add(new UiRow(it));
                }

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
                            var destFile = Path.Combine(hotPath, Path.GetFileName(srcFile));
                            if (File.Exists(destFile))
                            {
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
                hotFolderFullPath = Path.GetFullPath(hotPath);
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
                    Path.GetFullPath(it.Path).StartsWith(hotFolderFullPath, StringComparison.OrdinalIgnoreCase)
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
                    root = Path.GetDirectoryName(root)!;

                root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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

                var path = Path.Combine(root, pendingFolder);
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
                ProcessUtils.OpenInExplorer(Path.GetDirectoryName(p ?? string.Empty));
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
                "Ext" => new CategoryComparer(dir, Router),
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

        private async void CmAddTags_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) { MessageBox.Show("請先選取資料列"); return; }

            var existingTags = _rows.SelectMany(r => (r.Item.Tags ?? new List<string>())).Distinct().OrderBy(s => s).ToList();

            MessageBox.Show("標籤選取功能已在程式碼中，但 UI 介面為輔助Window，暫時跳過。");
            await Task.Yield();
        }

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
                await Task.Run(() => Db?.UpdateItemsAsync(rows.Select(x => x.Item).ToArray()));

                if (rows.Length == 1)
                {
                    RefreshRtTags(rows[0]);
                }
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
            ResetPreview();
            if (row == null)
            {
                ResetRtDetail();
                return;
            }

            try
            {
                string size, created, modified;

                // [V19.0 回滾 P2] 
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
                    // [V19.0 回滾 P2]
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
                r.Tags = string.Join(",", newTags);
            }

            try
            {
                await Task.Run(() => Db?.UpdateItemsAsync(rows.Select(x => x.Item).ToArray()));
                CollectionViewSource.GetDefaultView(_rows)?.Refresh();
                Log($"已為 {rows.Length} 個項目更新標籤。");
            }
            catch (Exception ex) { Log($"更新標籤失敗: {ex.Message}"); MessageBox.Show(ex.Message, "更新標籤失敗"); }
        }

        // ===== 左側：樹狀 + 麵包屑 =====
        private TreeView? ResolveTv() => (TvFolders ?? FindName("TvFolders") as TreeView);

        // (V13.0) 

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

                // (V13.0) 
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
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
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
                    if (Directory.EnumerateDirectories(node.FullPath).Any())
                    {
                        tvi.Items.Add(null);
                        tvi.Expanded += TvFolders_Expanded;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
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
                var subDirs = Directory.EnumerateDirectories(node.FullPath);
                foreach (var dir in subDirs)
                {
                    tvi.Items.Add(MakeTvi(MakeNode(dir)));
                }
            }
            catch (UnauthorizedAccessException)
            {
                Log($"[Lazy Load] 權限不足: {node.FullPath}");
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

        private void CmbSearchProject_SelectionChanged(object sender, SelectionChangedEventArgs e) { /* V7.5 實作 */ }
        private void BtnSearchProject_Click(object sender, RoutedEventArgs e) { /* V7.5 實作 */ }
        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            Log("專案鎖定功能 (V7.5) 尚未實作。");
            MessageBox.Show("專案鎖定：尚未實作（V7.5）");
        }

        private void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e) { MessageBox.Show("整份資料夾加入收件夾：尚未實T.作（V7.4）"); }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource is not TabControl tc) return;
            if (tc.SelectedItem is not TabItem ti) return;
            if (_view == null) return;

            _currentTabTag = (ti.Tag as string) ?? "home";
            Log($"切換 Tab: {_currentTabTag}");
            ApplyListFilters();
        }

        private void ApplyListFilters()
        {
            if (_view == null) return;

            Log($"[V19.0] 套用過濾：Tab='{_currentTabTag}', Path='{_selectedFolderPath}', Keyword='{_searchKeyword}', HideCommitted='{_hideCommitted}'");

            _view.Filter = (obj) =>
            {
                if (obj is not UiRow row) return false;

                // [V19.0 回滾 P2] 

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

                bool folderMatch = true;
                if (!string.IsNullOrWhiteSpace(_selectedFolderPath))
                {
                    var fileDir = System.IO.Path.GetDirectoryName(row.SourcePath) ?? "";
                    string normalizedFileDir, normalizedSelectedPath;
                    try
                    {
                        normalizedFileDir = Path.GetFullPath(fileDir).TrimEnd(Path.DirectorySeparatorChar);
                        normalizedSelectedPath = Path.GetFullPath(_selectedFolderPath).TrimEnd(Path.DirectorySeparatorChar);
                    }
                    catch (Exception ex)
                    {
                        Log($"[DIAG] Path normalization failed: {ex.Message}");
                        normalizedFileDir = fileDir.TrimEnd(Path.DirectorySeparatorChar);
                        normalizedSelectedPath = _selectedFolderPath.TrimEnd(Path.DirectorySeparatorChar);
                    }

                    // [V19.0 修正 P1 BUG] (V17.1 P2 需求 "但不包含子資料夾的內容")
                    // V19.0 [cite: `Services/HotFolderService.cs (V19.0)`] (V18.1 P1) [cite: `Services/HotFolderService.cs (V19.0)` Line 116] 已修正 'TopDirectoryOnly'
                    // V19.0 [Line 958] (V17.0) [cite: `Views/MainWindow.xaml.cs (V17.0)` Line 958] 'Equals' (非遞迴)。
                    folderMatch = normalizedFileDir.Equals(normalizedSelectedPath, StringComparison.OrdinalIgnoreCase);
                }
                if (!folderMatch) return false;

                bool keywordMatch = true;
                if (!string.IsNullOrWhiteSpace(_searchKeyword))
                {
                    var key = _searchKeyword.ToLowerInvariant();
                    keywordMatch = (row.FileName.ToLowerInvariant().Contains(key))
                                 || (row.Tags.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                                 || (row.Project.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                                 || (row.SourcePath.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (!keywordMatch) return false;

                return statusMatch && tabMatch && folderMatch && keywordMatch;
            };

            _view.Refresh();
            Log($"[DIAG] Filter applied. Current view count: {_view.Count}");
            TxtCounterSafe($"顯示: {_view.Count} / {_rows.Count}");
        }

        private void DebugFilter() { }


        private void List_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) return;

            // [V19.0 回滾 P2] 移除 V18.0 [cite: `Views/MainWindow.xaml.cs (V18.0)` Line 1011] 的 'if (row.IsFolder)' 檢查

            // (V7.5) 
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

                // 檢查 LLM 是否有回傳 (API Key 存在)
                if (!string.IsNullOrWhiteSpace(suggestion))
                {
                    Log($"[AI] 專案建議 (LLM): {suggestion}");
                    RtSuggestedProject.Text = suggestion;
                }
                else
                {
                    // [V17.0 修正 BUG #3.2] (V16.1 測試清單 #3.2)
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
        /// [V19.0 回滾 P2] (V17.1 P2 需求)
        /// </summary>
        private static string StatusToLabel(string? s)
        {
            var v = (s ?? "").ToLowerInvariant();
            return v switch
            {
                // [V19.0 回滾 P2] 移除 V18.0 [cite: `Views/MainWindow.xaml.cs (V18.0)` Line 1147] "folder"
                "committed" => "已提交",
                "error" => "錯誤",
                "" or null => "未處理",
                "intaked" => "未處理",
                _ when v.StartsWith("stage") => "暫存",
                _ => v
            };
        }

        // [V9.1 (V7.6) 檔案樹過濾]
        /// <summary>
        /// (V7.6) 遞迴過濾 TreeViewItem 及其子項。
        /// 如果節點本身或任何子節點符合過濾條件，則返回 true。
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
                    try
                    {
                        // [V16.1 修正 BUG #4]
                        // (V17.0) 引用 V17.0 [cite: `Services/RoutingService.cs (V17.0)`] 'RoutingService.cs'
                        if (_router == null) return ext;
                        return _router.MapExtensionToCategoryConfig(ext, ConfigService.Cfg) ?? ext;
                    }
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
                // [V19.0 回滾 P2] 移除 V18.0 [cite: `Views/MainWindow.xaml.cs (V18.0)` Line 1300] "folder"
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