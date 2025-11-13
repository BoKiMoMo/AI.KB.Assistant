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
using System.Diagnostics;
using SQLitePCL; // [V14.1 新增] 引用 SQLitePCL (v3.x)

namespace AI.KB.Assistant
{
    public partial class App : Application
    {
        private const string MutexName = "AI.KB.Assistant.Singleton.Mutex";
        private Mutex? _mutex;

        // V14.1 (V9.1)
        protected override async void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("AI.KB Assistant 已在背景執行中。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            SetupExceptionHandling();

            // ==================== V14.1 啟動核心邏輯 ====================

            // [V14.1 關鍵修正]
            // 強制初始化 SQLitePCLRaw (v3.x) 的 C++ 函式庫 (e_sqlite3.dll)。
            // 這必須在 DbService (V14.0) [cite: `Services/DbService.cs (V14.0)`] (它依賴 Microsoft.Data.Sqlite) 
            // 被 new() 之前呼叫，以解決 V13.x 的 InvalidOperationException 崩潰 [cite: `image_1684aa.png`]。
            try
            {
                SQLitePCL.Batteries.Init();
                Console.WriteLine("[APP INIT V14.1] SQLitePCL.Batteries.Init() (v3.x) OK.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[APP INIT V14.1] FATAL CRASH: SQLitePCL.Batteries.Init() failed: {ex.Message}");
                LogCrash("SQLitePCL.Batteries.Init.Failed", ex);
                MessageBox.Show($"SQLitePCL (v3.x) 核心初始化失敗: {ex.Message}", "嚴重錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }


            DbService dbService;
            HotFolderService hotFolderService = null!;
            AppConfig cfg;

            try
            {
                // ConfigService V9.0/V9.1 已在靜態建構函式中自動 Load()
                cfg = ConfigService.Cfg;
                Console.WriteLine("[APP INIT V14.1] ConfigService.Load() OK.");

                Console.WriteLine("[APP INIT V14.1] new DbService() starting...");
                dbService = new DbService(); // V14.0 [cite: `Services/DbService.cs (V14.0)`] (已移除 v2.x 反射)
                Console.WriteLine("[APP INIT V14.1] new DbService() OK.");

                Console.WriteLine("[APP INIT V14.1] dbService.InitializeAsync() starting...");
                await dbService.InitializeAsync();
                Console.WriteLine("[APP INIT V14.1] dbService.InitializeAsync() OK.");

                Console.WriteLine("[APP INIT V14.1] Other services starting...");
                var routingService = new RoutingService(cfg);

                // [V9.0 修正 CS1503] LlmService V7.6 建構函式不需參數
                var llmService = new LlmService();

                var intakeService = new IntakeService(dbService);

                // [V9.0 修正 CS1503] HotFolderService V8.1/V9.0 建構函式需要 DbService
                hotFolderService = new HotFolderService(intakeService, dbService);

                Console.WriteLine("[APP INIT V14.1] Other services OK.");

                Resources.Add("Db", dbService);
                Resources.Add("Router", routingService);
                Resources.Add("Llm", llmService);
                Resources.Add("Intake", intakeService);
                Resources.Add("HotFolder", hotFolderService);
                Console.WriteLine("[APP INIT V14.1] Services registered.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[APP INIT V14.1] FATAL CRASH (ServiceInitialization): {ex.Message}");
                LogCrash("ServiceInitialization", ex);
                var innerEx = ex.InnerException ?? ex;
                MessageBox.Show($"服務初始化失敗 (請檢查 %AppData% 設定檔權限): {innerEx.Message}", "嚴重錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            // =================================================================

            if (IsFirstRunOrConfigInvalid(cfg))
            {
                MessageBox.Show("歡迎使用 AI.KB Assistant！\n\n系統偵測到您是首次啟動，或核心路徑尚未設定。\n請先完成初始設定。", "歡迎", MessageBoxButton.OK, MessageBoxImage.Information);

                var settingsWindow = new SettingsWindow();
                settingsWindow.ShowDialog();

                ConfigService.Load();
                cfg = ConfigService.Cfg;
                if (IsFirstRunOrConfigInvalid(cfg))
                {
                    MessageBox.Show("核心路徑 (Root 目錄 / 收件夾) 尚未設定。\nApp 即將關閉。", "設定未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
                    this.Shutdown();
                    return;
                }
            }

            Window startupWindow;
            var mode = cfg.App.LaunchMode.ToLowerInvariant();

            if (mode == "detailed")
            {
                startupWindow = new MainWindow();
                Console.WriteLine("[APP INIT V14.1] Starting in DETAILED mode (MainWindow).");
            }
            else
            {
                startupWindow = new LauncherWindow();
                Console.WriteLine("[APP INIT V14.1] Starting in SIMPLE mode (LauncherWindow).");
            }

            this.MainWindow = startupWindow;
            startupWindow.Show();
            Console.WriteLine("[APP INIT V14.1] MainWindow.Show() called.");

            try
            {
                // [V9.0 修正 CS1061] 
                hotFolderService.StartMonitoring();
                Console.WriteLine("[APP INIT V14.1] HotFolderService started.");
            }
            catch (Exception ex)
            {
                LogCrash("HotFolderService.StartMonitoring.Failed", ex);
                MessageBox.Show($"HotFolder 監控啟動失敗: {ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool IsFirstRunOrConfigInvalid(AppConfig cfg)
        {
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

       // Enqueues cleanup operations to be performed on exit
        protected override void OnExit(ExitEventArgs e)
        {
            (Resources["HotFolder"] as IDisposable)?.Dispose();

            try { _mutex?.ReleaseMutex(); } catch { }
            _mutex?.Dispose();
            _mutex = null;
            base.OnExit(e);
        }

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
                Debug.WriteLine($"CRASH LOGGING FAILED: {loggingEx.Message}");
            }
        }
    }
}