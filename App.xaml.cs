using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Views;

namespace AI.KB.Assistant
{
    public partial class App : Application
    {
        private DbService? _db;
        private IntakeService? _intake;
        private RoutingService? _router;
        private LlmService? _llm;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // --- 確保 Theme.xaml（淺色主題）已載入 ---
            // TODO: 之後若要支援主題切換，可把此段抽成 ThemeService 並監聽設定改變
            TryEnsureTheme();

            // 全域例外（避免不友善崩潰）
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            try
            {
                ConfigService.Load();
                Log("✅ 設定載入成功。");

                await RebuildServicesAsync(); // 初始化 Db/Intake/Router/Llm

                // 設定異動 → 安全重建服務
                ConfigService.ConfigChanged += async (_, __) =>
                {
                    try
                    {
                        Log("🔁 Config 變更 → 重建服務…");
                        await RebuildServicesAsync();
                        Log("✅ 服務重建完成。");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"重新初始化服務失敗：{ex.Message}", "AI.KB Assistant");
                    }
                };

                new MainWindow().Show();
                Log("🚀 應用程式啟動完成。");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"啟動時發生錯誤：{ex.Message}", "AI.KB Assistant");
                Shutdown(-1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // TODO: 若後續加上 HotFolder 監聽，這裡記得解除監聽
            try { _llm = null; } catch { }
            try { _router = null; } catch { }
            try { _intake = null; } catch { }
            try { _db?.Dispose(); } catch { }
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // TODO: 導入統一 LogService 後改為寫檔
            MessageBox.Show(e.Exception.Message, "未處理的錯誤");
            e.Handled = true;
        }

        /// <summary>
        /// 依目前設定重建所有服務，並注入至 Application.Resources。
        /// </summary>
        private async Task RebuildServicesAsync()
        {
            try
            {
                // 先釋放舊的（避免把檔案握住）
                try { _db?.Dispose(); } catch { }
                _db = null; _intake = null; _router = null; _llm = null;

                var cfg = ConfigService.Cfg;

                // DB 路徑保底（使用 %AppData%\AI.KB.Assistant\ai_kb.db）
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AI.KB.Assistant");
                Directory.CreateDirectory(appData);

                if (string.IsNullOrWhiteSpace(cfg.Db.Path) && string.IsNullOrWhiteSpace(cfg.Db.DbPath))
                {
                    var def = Path.Combine(appData, "ai_kb.db");
                    cfg.Db.Path = def;
                    cfg.Db.DbPath = def;
                    ConfigService.Save(); // 記住一次
                }

                // 建立服務
                _db = new DbService();
                await _db.InitializeAsync(); // SQLite 初始化

                _intake = new IntakeService(_db);
                _router = new RoutingService(cfg);
                _llm = new LlmService(cfg); // V7.5 再實做 AI 能力

                // 注入全域
                Resources["Db"] = _db;
                Resources["Intake"] = _intake;
                Resources["Router"] = _router;
                Resources["Llm"] = _llm;

                Log($"🧩 服務就緒：DB={cfg.Db.Path ?? cfg.Db.DbPath}  Root={cfg.App.RootDir}  Hot={cfg.Import.HotFolderPath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化服務時發生錯誤：{ex.Message}", "AI.KB Assistant");
            }
        }

        /// <summary>
        /// 確保 Themes/Theme.xaml 已在 MergedDictionaries 中（淺色主題）。
        /// </summary>
        private void TryEnsureTheme()
        {
            try
            {
                // 避免重複加入：若已存在同一 Source 就不重複塞
                var uri = new Uri("Themes/Theme.xaml", UriKind.Relative);
                bool already = false;
                foreach (var rd in Resources.MergedDictionaries)
                {
                    if (rd.Source != null && rd.Source.OriginalString.Equals("Themes/Theme.xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        already = true; break;
                    }
                }
                if (!already)
                {
                    Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
                }
            }
            catch
            {
                // 若失敗，不要阻擋啟動；視覺會回退為系統預設
            }
        }

        private static void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Debug.WriteLine(line);
            Console.WriteLine(line);
        }
    }
}
