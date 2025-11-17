using System.Windows;
using System;
using System.Linq;
using System.Threading.Tasks;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Models;
using System.IO;
using System.Windows.Controls;
using System.Diagnostics;

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V20.2 (手動掃描模式版)
    /// 1. [V19.1] 整合「啟動防呆」邏輯 (禁用「開始」按鈕)。
    /// 2. [V20.2] 'BtnAutoClassify' 替換為 'BtnScanShallow' 和 'BtnScanRecursive'。
    /// 3. [V20.2] 'RunAutoClassifyAsync' 現在接受 'ScanMode' 參數。
    /// 4. [V20.2] 'RunAutoClassifyAsync' 現在呼叫 'HotFolder.ScanAsync(scanMode)'。
    /// </summary>
    public partial class LauncherWindow : Window
    {
        // V7.34 UI 串接：取得服務
        private T? Get<T>(string key) where T : class => Application.Current?.Resources[key] as T;
        private DbService? Db => Get<DbService>("Db");
        private RoutingService? Router => Get<RoutingService>("Router");
        private HotFolderService? HotFolder => Get<HotFolderService>("HotFolder");


        public LauncherWindow()
        {
            InitializeComponent();
            // [V19.1 防呆修復] 檢查路徑並設定按鈕
            bool isPathValid = PopulateFooterPaths();

            // [V20.2] 更新 IsEnabled 邏輯
            BtnScanShallow.IsEnabled = isPathValid;
            BtnScanRecursive.IsEnabled = isPathValid;
        }

        /// <summary>
        /// [V20.2] 自動分類核心邏輯，現在接受掃描模式
        /// </summary>
        private async Task RunAutoClassifyAsync(SearchOption scanMode)
        {
            Log("簡易模式 (Simple Mode) 啟動...");

            // [V19.1 防呆修復] 執行前再次檢查路徑
            var cfg = ConfigService.Cfg;
            bool isPathsValid = !string.IsNullOrWhiteSpace(cfg.App?.RootDir) && !string.IsNullOrWhiteSpace(cfg.Import?.HotFolder);
            if (!isPathsValid)
            {
                Log("錯誤：無法開始自動分類。根目錄 (RootDir) 或 收件夾 (HotFolder) 尚未設定。");
                MessageBox.Show(this, "根目錄 (RootDir) 或 收件夾 (HotFolder) 尚未設定。\n\n請點擊「設定」按鈕完成設定。", "路徑未設定", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Db == null || Router == null || HotFolder == null)
            {
                Log("錯誤：服務 (Db/Router/HotFolder) 尚未初始化。");
                MessageBox.Show(this, "服務 (Db/Router/HotFolder) 尚未初始化。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Log($"步驟 1/3：正在同步收件夾 (HotFolder) - 模式: {scanMode}...");
                // [V20.2] 呼叫參數化的 ScanAsync
                await Task.Run(() => HotFolder.ScanAsync(scanMode));
                Log("同步收件夾完畢。");

                Log("步驟 2/3：正在讀取資料庫...");
                var allItems = await Task.Run(() => Db.QueryAllAsync());
                var itemsToProcess = allItems
                    .Where(it => (it.Status ?? "intaked").Equals("intaked", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (itemsToProcess.Count == 0)
                {
                    Log("步驟 2/3：完成。收件夾中無需要處理的檔案。");
                    MessageBox.Show(this, "收件夾中沒有需要處理的檔案。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Log($"步驟 2/3：發現 {itemsToProcess.Count} 個未處理的檔案，開始自動分類...");
                    int ok = 0;

                    await Task.Run(() =>
                    {
                        foreach (var item in itemsToProcess)
                        {
                            var finalPath = Router.Commit(item);
                            if (!string.IsNullOrWhiteSpace(finalPath))
                            {
                                item.Status = "committed";
                                ok++;
                            }
                            else
                            {
                                item.Status = "error";
                            }
                        }
                    });

                    Log($"步驟 3/3：成功搬移 {ok} / {itemsToProcess.Count} 個檔案。");

                    if (ok > 0)
                    {
                        await Task.Run(() => Db.UpdateItemsAsync(itemsToProcess));
                        Log("資料庫更新完畢。");
                    }
                    MessageBox.Show(this, $"自動分類完成。\n\n成功搬移 {ok} / {itemsToProcess.Count} 個檔案。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log($"發生嚴重錯誤：{ex.Message}");
                MessageBox.Show(this, $"簡易模式執行失敗：\n{ex.Message}", "嚴重錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Log(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(message));
                return;
            }

            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            if (TxtStatusLog != null)
            {
                TxtStatusLog.AppendText(entry);
                TxtStatusLog.ScrollToEnd();
            }
            System.Diagnostics.Debug.WriteLine($"[Launcher] {message}");
        }

        /// <summary>
        /// [V19.1 防呆修復] 修改方法，使其回傳路徑是否有效
        /// </summary>
        private bool PopulateFooterPaths()
        {
            try
            {
                var cfg = ConfigService.Cfg;
                if (cfg == null) return false;

                Func<string, string> Truncate = (path) =>
                {
                    if (string.IsNullOrWhiteSpace(path)) return "(未設定)";
                    if (path.Length < 40) return path;
                    return $"...{path.Substring(path.Length - 37)}";
                };

                if (TxtRoot != null) TxtRoot.Text = Truncate(cfg.App?.RootDir ?? "");
                if (TxtInbox != null) TxtInbox.Text = Truncate(cfg.Import?.HotFolder ?? "");
                if (TxtDesktop != null) TxtDesktop.Text = Truncate(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                if (TxtDb != null) TxtDb.Text = Truncate(cfg.Db?.DbPath ?? "");

                // [V19.1 防呆修復] 驗證路徑
                bool isValid = !string.IsNullOrWhiteSpace(cfg.App?.RootDir) && !string.IsNullOrWhiteSpace(cfg.Import?.HotFolder);
                if (!isValid)
                {
                    Log("警告：根目錄 (RootDir) 或 收件夾 (HotFolder) 尚未在「設定」中指定。");
                }
                return isValid;
            }
            catch (Exception ex)
            {
                Log($"載入底部路徑失敗: {ex.Message}");
            }
            return false;
        }

        // --- 按鈕點擊事件 ---

        /// <summary>
        /// [V20.2] 輔助方法：設定兩個掃描按鈕的忙碌狀態
        /// </summary>
        private async Task SetButtonsBusy(bool isBusy)
        {
            if (isBusy)
            {
                BtnScanShallow.IsEnabled = false;
                BtnScanRecursive.IsEnabled = false;
                BtnScanRecursive.Content = "處理中...";
            }
            else
            {
                // [V20.2] 延遲 0.5 秒，避免 UI 閃爍
                await Task.Delay(500);
                BtnScanShallow.IsEnabled = true;
                BtnScanRecursive.IsEnabled = true;
                BtnScanRecursive.Content = "掃描子資料夾";
            }
        }

        /// <summary>
        /// [V20.2] 掃描第一層
        /// </summary>
        private async void BtnScanShallow_Click(object sender, RoutedEventArgs e)
        {
            Log("手動觸發自動分類 (僅第一層)...");
            await SetButtonsBusy(true);
            try
            {
                await RunAutoClassifyAsync(SearchOption.TopDirectoryOnly);
            }
            finally
            {
                await SetButtonsBusy(false);
            }
        }

        /// <summary>
        /// [V20.2] 掃描子資料夾 (遞迴)
        /// </summary>
        private async void BtnScanRecursive_Click(object sender, RoutedEventArgs e)
        {
            Log("手動觸發自動分類 (含子資料夾)...");
            await SetButtonsBusy(true);
            try
            {
                await RunAutoClassifyAsync(SearchOption.AllDirectories);
            }
            finally
            {
                await SetButtonsBusy(false);
            }
        }


        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigService.Cfg;
                var hotPath = cfg?.Import?.HotFolder;
                if (!string.IsNullOrWhiteSpace(hotPath) && Directory.Exists(hotPath))
                {
                    Process.Start("explorer.exe", hotPath);
                }
                else
                {
                    MessageBox.Show(this, "收件夾 (HotFolder) 路徑未設定或不存在。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"開啟收件夾失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenDetailed_Click(object sender, RoutedEventArgs e)
        {
            Log("正在切換到詳細模式...");
            var mainWin = new MainWindow();

            if (Application.Current != null)
            {
                Application.Current.MainWindow = mainWin;
            }
            mainWin.Show();

            this.Close();
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var w = new SettingsWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            w.ShowDialog();

            // [V19.1 防呆修復] 儲存設定後，重新驗證路徑並更新按鈕狀態
            bool isPathValid = PopulateFooterPaths();

            // [V20.2] 更新 IsEnabled 邏輯
            BtnScanShallow.IsEnabled = isPathValid;
            BtnScanRecursive.IsEnabled = isPathValid;
        }

        // V7.35 新功能 (選項 B)：清除收件夾中已分類的實體檔案
        private async void BtnClearCommitted_Click(object sender, RoutedEventArgs e)
        {
            Log("[V7.35-Opt.B] 開始清除已分類的實體檔案...");
            var cfg = ConfigService.Cfg;
            var hotPath = cfg?.Import?.HotFolder;

            if (Db == null || string.IsNullOrWhiteSpace(hotPath))
            {
                Log(" -> 錯誤：DbService 未初始化或 HotFolder 未設定。");
                MessageBox.Show(this, "DbService 未初始化或 HotFolder 未設定。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(this, $"HotFolder 路徑無效: {hotPath}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }


            var q = "您確定要永久刪除 [收件夾] 中所有 [已分類 (Committed)] 的實體檔案嗎？\n\n" +
                    "此操作主要用於「複製」模式。\n" +
                    "（此操作無法復原，且只會刪除收件夾內的檔案）";

            if (MessageBox.Show(this, q, "確認刪除 (選項B)", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
                    MessageBox.Show(this, "在收件夾中找不到已分類 (Committed) 的檔案。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show(this, $"清理完畢。\n\n成功刪除 {deletedFiles} 個實體檔案。\n（{failedFiles} 個檔案刪除失敗）", "清理完成 (選項B)", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($" -> [V7.35-Opt.B] 發生嚴重錯誤: {ex.Message}");
                MessageBox.Show(this, $"發生嚴重錯誤：\n{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // V7.35 新功能 (選項 C)：重置收件夾中未處理的項目狀態
        private async void BtnResetInbox_Click(object sender, RoutedEventArgs e)
        {
            Log("[V7.35-Opt.C] 開始重置未處理的資料庫狀態...");
            if (Db == null)
            {
                Log(" -> 錯誤：DbService 未初始化。");
                MessageBox.Show(this, "DbService 未初始化。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var q = "您確定要重置 [收件夾] 狀態嗎？\n\n" +
                    "此操作將刪除資料庫中所有 [未處理 (Intaked/Error)] 的紀錄。\n" +
                    "這將使 HotFolder 監控器在下次掃描時重新匯入它們。\n\n" +
                    "（此操作不會刪除您的任何實體檔案）";

            if (MessageBox.Show(this, q, "確認重置 (選項C)", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                Log(" -> 操作已取消。");
                return;
            }

            try
            {
                // 呼叫 DbService 的新方法
                int deletedCount = await Task.Run(() => Db.DeleteNonCommittedAsync());

                Log($" -> 重置完畢。共 {deletedCount} 筆未處理的紀錄已從資料庫刪除。");
                MessageBox.Show(this, $"重置完畢。\n\n共 {deletedCount} 筆未處理的紀錄已從資料庫刪除。\nHotFolder 將在 2 秒後重新掃描。", "重置完成 (選項C)", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($" -> [V7.35-Opt.C] 發生嚴重錯誤: {ex.Message}");
                MessageBox.Show(this, $"發生嚴重錯誤：\n{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}