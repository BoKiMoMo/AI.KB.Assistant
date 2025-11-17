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
    /// <summary>
    /// V20.0 (最終修復版)
    /// 1. (V19.1) 修復 STAThread [cite:"image_e16a88.jpg"] 崩潰 (改回 'void OnStartup' [cite:"App.xaml.cs (V20.0 最終版) (line 29)"])。
    /// 2. (V19.1) 實作最終啟動邏輯 (首次啟動強制迴圈 [cite:"App.xaml.cs (V20.0 最終版) (line 140)"] / 再次啟動僅警告 [cite:"App.xaml.cs (V20.0 最終版) (line 161)"])。
    /// 3. (V19.1) 修正 `LogCrash` [cite:"App.xaml.cs (V20.0 最終版) (line 241)"] 中遺漏的 `{}` [cite:"App.xaml.cs (V20.0 最終版) (line 253)"] 和拼字 [cite:"App.xaml.cs (V20.0 最終版) (line 254)"] 錯誤。
    /// </summary>
    public partial class App : Application
    {
        private const string MutexName = "AI.KB.Assistant.Singleton.Mutex";
        private Mutex? _mutex;

        // [V19.1 STAThread 崩潰修復]
        // 1. 將 OnStartup 改回 'void' (同步)，以確保 UI 在 STA 執行緒上建立。
        // 2. 將 'async' 服務 (DB Init) 移至 UI 啟動後呼叫。
        protected override void OnStartup(StartupEventArgs e)
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
                // [V19.1 首次啟動修復] Load() [cite:"Services/ConfigService.cs (V20.0 最終版) (line 55)"] 現在會設定 IsNewUserConfig [cite:"Services/ConfigService.cs (V20.0 最終版) (line 34)"] 旗標
                cfg = ConfigService.Cfg;
                Console.WriteLine("[APP INIT V14.1] ConfigService.Load() OK.");

                Console.WriteLine("[APP INIT V14.1] new DbService() starting...");
                dbService = new DbService(); // V14.0 (已移除 v2.x 反射)
                Console.WriteLine("[APP INIT V14.1] new DbService() OK.");

                // [V19.1 STAThread 崩潰修復] 將 'await dbService.InitializeAsync()' [cite:"App.xaml.cs (V20.0 最終版) (line 200)"] 移至 UI 啟動後

                Console.WriteLine("[APP INIT V14.1] Other services starting...");
                var routingService = new RoutingService(cfg);
                var llmService = new LlmService();
                var intakeService = new IntakeService(dbService);
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

            // [V19.1 最終防呆修復] 
            // 實現「首次啟動 (強制迴圈)」[cite:"3.應該要跳回設定頁面"] vs 「再次啟動 (僅警告)」[cite:"4.再次啟動偵測到沒有設定應該在跳一次警告!"] 邏輯

            bool pathsAreValid = !IsFirstRunOrConfigInvalid(cfg);

            if (ConfigService.IsNewUserConfig)
            {
                // 1. 首次啟動 (Force Loop)
                MessageBox.Show("歡迎使用 AI.KB Assistant！\n\n系統偵測到您是首次啟動。\n請先完成初始設定。", "歡迎", MessageBoxButton.OK, MessageBoxImage.Information);

                while (!pathsAreValid)
                {
                    // [V19.1 STAThread 崩潰修復] 
                    // 由於 OnStartup [cite:"App.xaml.cs (V20.0 最終版) (line 29)"] 是同步的，'new SettingsWindow()' [cite:"App.xaml.cs (V20.0 最終版) (line 143)"] 現在是安全的。
                    var settingsWindow = new SettingsWindow();
                    settingsWindow.ShowDialog();

                    ConfigService.Load();
                    cfg = ConfigService.Cfg;
                    pathsAreValid = !IsFirstRunOrConfigInvalid(cfg);

                    if (!pathsAreValid)
                    {
                        var result = MessageBox.Show("核心路徑 (Root 目錄 / 收件夾) 尚未設定。\n\n您是否要離開程式？", "設定未完成", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result == MessageBoxResult.Yes)
                        {
                            this.Shutdown();
                            return;
                        }
                        // [V19.1 邏輯確認] 如果按「否」，while 迴圈會強制再次顯示設定頁面 [cite:"3.應該要跳回設定頁面"]
                    }
                }
            }
            else if (!pathsAreValid)
            {
                // 2. 再次啟動 (Warn Only)
                // [V19.1 邏輯確認] [cite:"4.再次啟動偵測到沒有設定應該在跳一次警告!"]
                var result = MessageBox.Show("核心路徑 (Root 目錄 / 收件夾) 尚未設定。\n\n您是否要離開程式？", "設定未完成", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    this.Shutdown();
                    return;
                }
                // [V19.1 邏輯確認] 如果按「否」，App 會繼續啟動 (進入 LauncherWindow 的防呆模式)
            }

            // =================================================================
            // 啟動主視窗 (此時路徑可能有效，也可能無效)
            // =================================================================

            Window startupWindow;
            var mode = cfg.App.LaunchMode.ToLowerInvariant();

            if (mode == "detailed")
            {
                startupWindow = new MainWindow();
                Console.WriteLine("[APP INIT V19.1] Starting in DETAILED mode (MainWindow).");
            }
            else
            {
                startupWindow = new LauncherWindow();
                Console.WriteLine("[APP INIT V19.1] Starting in SIMPLE mode (LauncherWindow).");
            }

            this.MainWindow = startupWindow;
            startupWindow.Show();
            Console.WriteLine("[APP INIT V19.1] MainWindow.Show() called.");

            try
            {
                hotFolderService.StartMonitoring();
                Console.WriteLine("[APP INIT V19.1] HotFolderService started.");
            }
            catch (Exception ex)
            {
                LogCrash("HotFolderService.StartMonitoring.Failed", ex);
                MessageBox.Show($"HotFolder 監控啟動失敗: {ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // [V19.1 STAThread 崩潰修復]
            // 在 UI 啟動後，以非同步方式初始化 DB
            InitializeAsyncServices(dbService);
        }

        /// <summary>
        /// [V19.1 STAThread 崩潰修復] 
        /// 異步初始化服務 (例如 DB)
        /// </summary>
        private async void InitializeAsyncServices(DbService dbService)
        {
            try
            {
                Console.WriteLine("[APP INIT V19.1] dbService.InitializeAsync() (async) starting...");
                await dbService.InitializeAsync();
                Console.WriteLine("[APP INIT V19.1] dbService.InitializeAsync() (async) OK.");
            }
            catch (Exception ex)
            {
                LogCrash("DbService.InitializeAsync.Failed", ex);
                // 由於 UI 已在執行，我們只能在日誌中記錄錯誤
                Console.WriteLine($"[APP INIT V19.1] FATAL CRASH (DbService.InitializeAsync): {ex.Message}");
            }
        }


        /// <summary>
        /// [V19.1 首次啟動修復] 此方法現在可以正確運作，
        /// 因為 CreateDefault() [cite:"Services/ConfigService.cs (V20.0 最終版) (line 153)"] 會將路徑設為空字串 [cite:"Services/ConfigService.cs (V20.0 最終版) (lines 156, 159)"]。
        /// </summary>
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
            // [V19.1 語法修復] 補上遺漏的 {} 並修正拼字
            catch (Exception loggingEx)
            {
                Debug.WriteLine($"CRASH LOGGING FAILED: {loggingEx.Message}");
            }
        }
    }
}