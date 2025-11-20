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
    /// V20.13.21 (設定按鈕診斷版)
    /// 1. [Fix Settings] BtnOpenSettings_Click 加入 Try-Catch。
    /// </summary>
    public partial class LauncherWindow : Window
    {
        private T? Get<T>(string key) where T : class => Application.Current?.Resources[key] as T;
        private DbService? Db => Get<DbService>("Db");
        private RoutingService? Router => Get<RoutingService>("Router");
        private HotFolderService? HotFolder => Get<HotFolderService>("HotFolder");

        public LauncherWindow()
        {
            InitializeComponent();
            bool isPathValid = PopulateFooterPaths();
            if (BtnScanShallow != null) BtnScanShallow.IsEnabled = isPathValid;
            if (BtnScanRecursive != null) BtnScanRecursive.IsEnabled = isPathValid;
        }

        private async Task RunAutoClassifyAsync(SearchOption scanMode)
        {
            Log("簡易模式 (Simple Mode) 啟動...");

            var cfg = ConfigService.Cfg;
            bool isPathsValid = cfg?.App != null && cfg.Import != null &&
                                !string.IsNullOrWhiteSpace(cfg.App.RootDir) &&
                                !string.IsNullOrWhiteSpace(cfg.Import.HotFolder);

            if (!isPathsValid)
            {
                Log("錯誤：根目錄或收件夾未設定。");
                MessageBox.Show("根目錄或收件夾未設定。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Db == null || Router == null || HotFolder == null)
            {
                Log("錯誤：服務未初始化。");
                return;
            }

            string hotFolderFullPath = System.IO.Path.GetFullPath(cfg!.Import!.HotFolder!);

            try
            {
                Log("同步收件夾...");
                await Task.Run(() => HotFolder.ScanAsync(scanMode, scanOnlyHotFolder: true));

                var allItems = await Task.Run(() => Db.QueryAllAsync());
                var itemsToProcess = allItems
                    .Where(it => (it.Status ?? "intaked").Equals("intaked", StringComparison.OrdinalIgnoreCase) &&
                                 !string.IsNullOrWhiteSpace(it.Path) &&
                                 System.IO.Path.GetFullPath(it.Path).StartsWith(hotFolderFullPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (itemsToProcess.Count == 0)
                {
                    MessageBox.Show("無可處理檔案。", "完成");
                }
                else
                {
                    Log($"發現 {itemsToProcess.Count} 個檔案，開始分類...");
                    int ok = 0;
                    await Task.Run(() =>
                    {
                        foreach (var item in itemsToProcess)
                        {
                            var finalPath = Router.Commit(item);
                            if (!string.IsNullOrWhiteSpace(finalPath)) { item.Status = "committed"; ok++; }
                            else item.Status = "error";
                        }
                    });

                    if (ok > 0) await Task.Run(() => Db.UpdateItemsAsync(itemsToProcess));
                    MessageBox.Show($"成功搬移 {ok} / {itemsToProcess.Count} 個檔案。", "完成");
                }
            }
            catch (Exception ex)
            {
                Log($"錯誤：{ex.Message}");
                MessageBox.Show($"錯誤：\n{ex.Message}", "錯誤");
            }
        }

        public void Log(string message)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Log(message)); return; }
            if (TxtStatusLog != null) { TxtStatusLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n"); TxtStatusLog.ScrollToEnd(); }
        }

        private bool PopulateFooterPaths()
        {
            try
            {
                var cfg = ConfigService.Cfg;
                if (cfg == null) return false;

                Func<string, string> Truncate = (path) => string.IsNullOrWhiteSpace(path) ? "(未設定)" : (path.Length < 40 ? path : "..." + path.Substring(path.Length - 37));

                if (TxtRoot != null) TxtRoot.Text = Truncate(cfg.App?.RootDir ?? "");
                if (TxtInbox != null) TxtInbox.Text = Truncate(cfg.Import?.HotFolder ?? "");
                if (TxtDesktop != null) TxtDesktop.Text = Truncate(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                if (TxtDb != null) TxtDb.Text = Truncate(cfg.Db?.DbPath ?? "");

                return !string.IsNullOrWhiteSpace(cfg.App?.RootDir) && !string.IsNullOrWhiteSpace(cfg.Import?.HotFolder);
            }
            catch { return false; }
        }

        private async Task SetButtonsBusy(bool isBusy)
        {
            if (BtnScanShallow == null || BtnScanRecursive == null) return;

            if (isBusy)
            {
                BtnScanShallow.IsEnabled = false;
                BtnScanRecursive.IsEnabled = false;
                BtnScanRecursive.Content = "處理中...";
            }
            else
            {
                await Task.Delay(500);
                BtnScanShallow.IsEnabled = true;
                BtnScanRecursive.IsEnabled = true;
                BtnScanRecursive.Content = "分類全部(含子資料夾)";
            }
        }

        // ===== Event Handlers =====

        private async void BtnScanShallow_Click(object sender, RoutedEventArgs e)
        {
            await SetButtonsBusy(true);
            try { await RunAutoClassifyAsync(SearchOption.TopDirectoryOnly); } finally { await SetButtonsBusy(false); }
        }

        private async void BtnScanRecursive_Click(object sender, RoutedEventArgs e)
        {
            await SetButtonsBusy(true);
            try { await RunAutoClassifyAsync(SearchOption.AllDirectories); } finally { await SetButtonsBusy(false); }
        }

        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e)
        {
            var path = ConfigService.Cfg?.Import?.HotFolder;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) Process.Start("explorer.exe", path);
            else MessageBox.Show("收件夾不存在。", "錯誤");
        }

        private void BtnOpenDetailed_Click(object sender, RoutedEventArgs e)
        {
            Log("正在切換到詳細模式...");
            var mainWin = new MainWindow();
            Application.Current.MainWindow = mainWin;
            mainWin.Show();
            this.Close();
        }

        // [Fix Settings] 加入 Try-Catch
        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new SettingsWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
                bool isPathValid = PopulateFooterPaths();
                if (BtnScanShallow != null) BtnScanShallow.IsEnabled = isPathValid;
                if (BtnScanRecursive != null) BtnScanRecursive.IsEnabled = isPathValid;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"無法開啟設定視窗: {ex.Message}\n\n{ex.StackTrace}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnClearCommitted_Click(object sender, RoutedEventArgs e)
        {
            var cfg = ConfigService.Cfg;
            var hotPath = cfg?.Import?.HotFolder;

            if (Db == null || string.IsNullOrWhiteSpace(hotPath)) { MessageBox.Show("未設定。", "錯誤"); return; }

            if (MessageBox.Show("刪除已分類檔案？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            try
            {
                var allItems = await Task.Run(() => Db.QueryAllAsync());
                var committed = allItems.Where(it =>
                    it.Status == "committed" &&
                    !string.IsNullOrWhiteSpace(it.Path) &&
                    System.IO.Path.GetFullPath(it.Path).StartsWith(System.IO.Path.GetFullPath(hotPath), StringComparison.OrdinalIgnoreCase)).ToList();

                if (committed.Count == 0) { MessageBox.Show("無檔案。", "完成"); return; }

                int del = 0;
                await Task.Run(() => {
                    foreach (var it in committed)
                    {
                        try
                        {
                            if (File.Exists(it.Path)) { File.Delete(it.Path); del++; }
                        }
                        catch { }
                    }
                });

                var idsToDelete = committed.Select(it => it.Id).Where(id => id != null).Cast<string>().ToList();
                await Task.Run(() => Db.DeleteItemsAsync(idsToDelete));

                MessageBox.Show($"清理完成。刪除: {del}", "完成");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "錯誤"); }
        }

        private async void BtnResetInbox_Click(object sender, RoutedEventArgs e)
        {
            if (Db == null) return;
            if (MessageBox.Show("重置狀態？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                int c = await Task.Run(() => Db.DeleteNonCommittedAsync());
                MessageBox.Show($"重置完成。刪除: {c}", "完成");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "錯誤"); }
        }
    }
}