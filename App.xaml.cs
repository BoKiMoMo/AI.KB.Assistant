using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Views;
using AI.KB.Assistant.Common;
using System.Diagnostics; // V7.34 偵錯

namespace AI.KB.Assistant
{
    public partial class App : Application
    {
        private const string MutexName = "AI.KB.Assistant.Singleton.Mutex";
        private Mutex? _mutex;

        // V7.6 FIX: 將 OnStartup 設為 async void
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

            // ==================== V7.34 啟動核心邏輯 ====================
            DbService dbService;
            HotFolderService hotFolderService = null!;
            AppConfig cfg; // V7.34 修正：在此宣告

            try
            {
                // V7.34 啟動崩潰修正：
                // 將 ConfigService.Load() 移入 try...catch 區塊。
                ConfigService.Load(); // V7.34 重構：此方法現在指向 %AppData%
                cfg = ConfigService.Cfg;

                Console.WriteLine("[APP INIT V7.7] ConfigService.Load() OK.");

                // 1. 依序建立服務實例
                Console.WriteLine("[APP INIT V7.7] new DbService() starting...");
                dbService = new DbService();
                Console.WriteLine("[APP INIT V7.7] new DbService() OK.");

                Console.WriteLine("[APP INIT V7.7] dbService.InitializeAsync() starting...");
                await dbService.InitializeAsync();
                Console.WriteLine("[APP INIT V7.7] dbService.InitializeAsync() OK.");

                Console.WriteLine("[APP INIT V7.7] Other services starting...");
                var routingService = new RoutingService(cfg);
                var llmService = new LlmService(cfg);
                var intakeService = new IntakeService(dbService);

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
                LogCrash("ServiceInitialization", ex);
                var innerEx = ex.InnerException ?? ex;
                MessageBox.Show($"服務初始化失敗 (請檢查 %AppData% 設定檔權限): {innerEx.Message}", "嚴重錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            // =================================================================

            // 4. 檢查首次啟動 (V7.34 UI 串接)
            if (IsFirstRunOrConfigInvalid(cfg))
            {
                MessageBox.Show("歡迎使用 AI.KB Assistant！\n\n系統偵測到您是首次啟動，或核心路徑尚未設定。\n請先完成初始設定。", "歡迎", MessageBoxButton.OK, MessageBoxImage.Information);

                var settingsWindow = new SettingsWindow();
                settingsWindow.ShowDialog();

                // 重新載入設定，再次檢查
                ConfigService.Load();
                cfg = ConfigService.Cfg;
                if (IsFirstRunOrConfigInvalid(cfg))
                {
                    MessageBox.Show("核心路徑 (Root 目錄 / 收件夾) 尚未設定。\nApp 即將關閉。", "設定未完成", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // V7.34 崩潰修正 (NullReferenceException)
                    // 在 OnStartup 中，應使用 this.Shutdown() 而不是 Application.Current.Shutdown()
                    this.Shutdown();
                    return;
                }
            }

            // 5. 服務都緒後，依據設定檔啟動視窗 (V7.34 UI 串接)
            Window startupWindow;
            // V7.34 修正：預設啟動模式改為 "simple"
            var mode = cfg?.App?.LaunchMode?.ToLowerInvariant() ?? "simple";

            if (mode == "detailed")
            {
                startupWindow = new MainWindow();
                Console.WriteLine("[APP INIT V7.34] Starting in DETAILED mode (MainWindow).");
            }
            else
            {
                startupWindow = new LauncherWindow();
                Console.WriteLine("[APP INIT V7.34] Starting in SIMPLE mode (LauncherWindow).");
            }

            this.MainWindow = startupWindow;
            startupWindow.Show();
            Console.WriteLine("[APP INIT V7.34] MainWindow.Show() called.");


            // 6. 啟動 HotFolder 監控 (V7.6 修正)
            try
            {
                hotFolderService.StartMonitoring();
                Console.WriteLine("[APP INIT V7.7] HotFolderService started.");
            }
            catch (Exception ex)
            {
                LogCrash("HotFolderService.StartMonitoring.Failed", ex);
                MessageBox.Show($"HotFolder 監控啟動失敗: {ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// V7.34 檢查是否為首次執行 (或設定檔不完整)
        /// </summary>
        private bool IsFirstRunOrConfigInvalid(AppConfig cfg)
        {
            // V7.34 邏輯修正：
            // 1. 檢查 cfg 本身是否為 null
            // 2. 檢查 App 區段是否為 null
            // 3. 檢查 Import 區段是否為 null
            if (cfg == null || cfg.App == null || cfg.Import == null)
            {
                // 如果設定檔嚴重損毀或無法載入，強制要求重新設定
                return true;
            }

            // 只要 RootDir 或 HotFolder 任何一個為空，就視為設定不完整
            return string.IsNullOrWhiteSpace(cfg.App.RootDir) ||
                   string.IsNullOrWhiteSpace(cfg.Import.HotFolder);
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
            try
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
            catch (Exception loggingEx)
            {
                // 如果連寫入日誌都失敗，至少在 Debug 視窗顯示
                Debug.WriteLine($"CRASH LOGGING FAILED: {loggingEx.Message}");
            }
        }
    }
}