using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks; // V7.6 修正：確保引入 Task 命名空間
using System.Windows;
using System.Windows.Threading;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Views;
using AI.KB.Assistant.Common;

namespace AI.KB.Assistant
{
    public partial class App : Application
    {
        private const string MutexName = "AI.KB.Assistant.Singleton.Mutex";
        private Mutex? _mutex;

        // V7.6 FIX: 將 OnStartup 設為 async void，解決 UI 執行緒阻塞死結問題
        protected override async void OnStartup(StartupEventArgs e)
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

            // ==================== V7.6 修正：服務註冊核心邏輯 ====================
            DbService dbService;
            HotFolderService hotFolderService = null!;

            // V7.7 增強日誌
            Console.WriteLine("[APP INIT V7.7] OnStartup started.");

            try
            {
                Console.WriteLine("[APP INIT V7.7] ConfigService.Load() starting...");
                ConfigService.Load();
                var cfg = ConfigService.Cfg;
                Console.WriteLine("[APP INIT V7.7] ConfigService.Load() OK.");

                // 1. 依序建立服務實例
                Console.WriteLine("[APP INIT V7.7] new DbService() starting...");
                dbService = new DbService();
                Console.WriteLine("[APP INIT V7.7] new DbService() OK.");

                Console.WriteLine("[APP INIT V7.7] dbService.InitializeAsync() starting...");
                // FIX: 使用 await 替代 GetAwaiter().GetResult()，消除死結
                await dbService.InitializeAsync();
                Console.WriteLine("[APP INIT V7.7] dbService.InitializeAsync() OK.");

                Console.WriteLine("[APP INIT V7.7] Other services starting...");
                var routingService = new RoutingService(cfg);
                var llmService = new LlmService(cfg);
                var intakeService = new IntakeService(dbService);

                // FIX: HotFolderService 依賴 IntakeService 和 RoutingService
                hotFolderService = new HotFolderService(intakeService, routingService);
                Console.WriteLine("[APP INIT V7.7] Other services OK.");

                // 2. 將服務註冊到全域資源字典
                Resources.Add("Db", dbService);
                Resources.Add("Router", routingService);
                Resources.Add("Llm", llmService);
                Resources.Add("Intake", intakeService);
                Resources.Add("HotFolder", hotFolderService);
                Console.WriteLine("[APP INIT V7.7] Services registered.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[APP INIT V7.7] FATAL CRASH: {ex.Message}");
                LogCrash("ServiceInitialization", ex); // <== 關鍵的日誌寫入
                // 顯示內部錯誤，而不是 TargetInvocationException
                var innerEx = ex.InnerException ?? ex;
                MessageBox.Show($"服務初始化失敗: {innerEx.Message}", "嚴重錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            // =================================================================

            // 3. 服務都緒後，手動建立並顯示主視窗
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            mainWindow.Show();
            Console.WriteLine("[APP INIT V7.7] MainWindow.Show() called.");

            // FIX: 在主視窗顯示後，啟動 HotFolder 監控
            try
            {
                // V7.6 修正：確保只有在 HotFolderService 存在時才呼叫 StartMonitoring
                hotFolderService.StartMonitoring();
                Console.WriteLine("[APP INIT V7.7] HotFolderService started.");
            }
            catch (Exception ex)
            {
                LogCrash("HotFolderService.StartMonitoring.Failed", ex);
                MessageBox.Show($"HotFolder 監控啟動失敗: {ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // V7.6 修正：因為 OnStartup 是 async void，不需要呼叫 base.OnStartup(e);
            // base.OnStartup(e); // 應該被移除或註釋掉
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
            // V7.4 修正：確保 HotFolderService 被釋放
            (Resources["HotFolder"] as IDisposable)?.Dispose();

            try { _mutex?.ReleaseMutex(); } catch { }
            _mutex?.Dispose();
            _mutex = null;
            base.OnExit(e);
        }

        // V7.15 修正：改為 public，以便 DbService 可以呼叫
        public static void LogCrash(string tag, Exception? ex)
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