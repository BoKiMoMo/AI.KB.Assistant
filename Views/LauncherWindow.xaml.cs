using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AI.KB.Assistant.Views
{
    public partial class LauncherWindow : Window
    {
        private readonly string _cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private AppConfig _cfg = new();

        private DbService _db = null!;
        private RoutingService _routing = null!;
        private LlmService _llm = null!;
        private IntakeService _intake = null!;

        public LauncherWindow()
        {
            InitializeComponent();

            InitServices();
            RefreshEnvSummary();
        }

        private void InitServices()
        {
            _cfg = ConfigService.TryLoad(_cfgPath);
            ThemeService.Apply(_cfg);

            // 確保 DB 目錄存在
            Directory.CreateDirectory(Path.GetDirectoryName(_cfg.App.DbPath!)!);

            _db = new DbService(_cfg.App.DbPath!);
            _routing = new RoutingService(_cfg);
            _llm = new LlmService(_cfg);
            _intake = new IntakeService(_cfg, _db, _routing, _llm);
        }

        private void RefreshEnvSummary()
        {
            try
            {
                TxtRoot.Text = _cfg.App.RootDir ?? "(未設定)";
                TxtInbox.Text = _cfg.Import.HotFolderPath ?? "(未設定)";
                TxtDesktop.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                TxtDb.Text = _cfg.App.DbPath ?? "(未設定)";
            }
            catch
            {
                TxtRoot.Text = TxtInbox.Text = TxtDesktop.Text = TxtDb.Text = "(讀取失敗)";
            }
        }

        // ⚡ 自動分類：掃描 → 預分類 → 依策略直接搬檔
        private async void BtnAutoClassify_Click(object sender, RoutedEventArgs e)
        {
            var hot = _cfg.Import?.HotFolderPath;
            if (string.IsNullOrWhiteSpace(hot) || !Directory.Exists(hot))
            {
                MessageBox.Show("收件夾未設定或不存在，請先到設定頁設定收件夾路徑。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var btn = (Button)sender;
            btn.IsEnabled = false;

            try
            {
                int staged = 0, classified = 0, moved = 0;
                var searchOpt = (_cfg.Import?.IncludeSubdir ?? true)
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                // 1) 掃描並加入（Inbox）
                foreach (var f in Directory.EnumerateFiles(hot, "*", searchOpt))
                {
                    try { await _intake.StageOnlyAsync(f, CancellationToken.None); staged++; } catch { }
                }

                // 2) 預分類（不搬）
                foreach (var f in Directory.EnumerateFiles(hot, "*", searchOpt))
                {
                    try { await _intake.ClassifyOnlyAsync(f, CancellationToken.None); classified++; } catch { }
                }

                // 3) 直接搬檔（依設定策略）
                var policy = _cfg.Import.OverwritePolicy;
                var copyMode = _cfg.Import.MoveMode == MoveMode.Copy;
                moved = await _intake.CommitPendingAsync(policy, copyMode, CancellationToken.None);

                MessageBox.Show($"完成！\n加入 {staged} 筆\n預分類 {classified} 筆\n搬檔 {moved} 筆",
                    "自動分類", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("自動分類發生錯誤：\n" + ex.Message, "錯誤",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        // 🗂 開啟收件夾
        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e)
        {
            var hot = _cfg.Import?.HotFolderPath;
            if (!string.IsNullOrWhiteSpace(hot) && Directory.Exists(hot))
                Process.Start(new ProcessStartInfo(hot) { UseShellExecute = true });
            else
                MessageBox.Show("收件夾未設定或不存在。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 🖼 進入詳細版
        private void BtnOpenDetailed_Click(object sender, RoutedEventArgs e)
        {
            var w = new MainWindow { Owner = this };
            w.Show();
        }

        // ⚙ 設定
        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var w = new SettingsWindow(this, _cfg) { Owner = this };
            if (w.ShowDialog() == true)
            {
                // 使用者修改設定後重新載入服務與摘要
                InitServices();
                RefreshEnvSummary();
            }
        }
    }
}
