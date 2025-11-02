using System;
using System.IO;
using System.Threading;
using System.Windows;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services; // 引用 Services
using AI.KB.Assistant.Views;    // 引用 Views

namespace AI.KB.Assistant
{
    public partial class App : Application
    {
        private const string MutexName = "AI.KB.Assistant.Singleton.Mutex";
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. 單實例防重入
            bool createdNew;
            _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("AI.KB Assistant 已在背景執行中。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            // 2. 全域例外護欄
            SetupExceptionHandling();

            // ==================== V7.3 修正：服務註冊核心邏輯 ====================
            try
            {
                // 優先載入設定檔
                ConfigService.Load();
                var cfg = ConfigService.Cfg;

                // 依序建立服務實例
                var dbService = new DbService();
                var routingService = new RoutingService(cfg);
                var llmService = new LlmService(cfg);
                var intakeService = new IntakeService(dbService);
                var hotFolderService = new HotFolderService(dbService);

                // 將服務註冊到全域資源字典，供 MainWindow 等地方取用
                Resources.Add("Db", dbService);
                Resources.Add("Router", routingService);
                Resources.Add("Llm", llmService);
                Resources.Add("Intake", intakeService);
                Resources.Add("HotFolder", hotFolderService);
            }
            catch (Exception ex)
            {
                LogCrash("ServiceInitialization", ex);
                MessageBox.Show($"服務初始化失敗: {ex.Message}", "嚴重錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            // =================================================================

            // 3. 服務都緒後，手動建立並顯示主視窗 (App.xaml 已移除 StartupUri)
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            mainWindow.Show();

            base.OnStartup(e);
        }

        private void SetupExceptionHandling()
        {
            this.DispatcherUnhandledException += (s, ev) =>
            {
                try { LogCrash("DispatcherUnhandledException", ev.Exception); } catch { }
                MessageBox.Show(ev.Exception.Message, "未處理例外", MessageBoxButton.OK, MessageBoxImage.Error);
                ev.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                try { LogCrash("AppDomain.UnhandledException", ev.ExceptionObject as Exception); } catch { }
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            _mutex?.Dispose();
            _mutex = null;
            base.OnExit(e);
        }

        private static void LogCrash(string tag, Exception? ex)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AI.KB.Assistant", "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.AppendAllText(path,
$@"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}
{ex}
----------------------------------------------------
");
        }
    }
}
