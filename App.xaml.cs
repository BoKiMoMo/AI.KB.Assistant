using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
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

            try
            {
                ConfigService.Load();
                Log("✅ 設定載入成功。");

                await RebuildServicesAsync();

                ConfigService.ConfigChanged += async (_, __) =>
                {
                    try { Log("🔁 Config 變更 → 重建服務…"); await RebuildServicesAsync(); Log("✅ 服務重建完成。"); }
                    catch (Exception ex) { MessageBox.Show($"重新初始化服務失敗：{ex.Message}", "AI.KB Assistant"); }
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

        private async Task RebuildServicesAsync()
        {
            try
            {
                try { _db?.Dispose(); } catch { }
                _db = null; _intake = null; _router = null; _llm = null;

                var cfg = ConfigService.Cfg;

                // DB 路徑保底
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AI.KB.Assistant");
                Directory.CreateDirectory(appData);
                if (string.IsNullOrWhiteSpace(cfg.Db.Path) && string.IsNullOrWhiteSpace(cfg.Db.DbPath))
                {
                    var def = Path.Combine(appData, "ai_kb.db");
                    cfg.Db.Path = def;
                    cfg.Db.DbPath = def;
                    ConfigService.Save(); // 記住一次
                }

                _db = new DbService();
                await _db.InitializeAsync();

                _intake = new IntakeService(_db);
                _router = new RoutingService(cfg);
                _llm = new LlmService(cfg);

                Resources["Db"] = _db;
                Resources["Intake"] = _intake;
                Resources["Router"] = _router;
                Resources["Llm"] = _llm;

                Log($"🧩 服務就緒：DB={cfg.Db.Path}  Root={cfg.App.RootDir}  Hot={cfg.Import.HotFolderPath}");
            }
            catch (Exception ex) { MessageBox.Show($"初始化服務時發生錯誤：{ex.Message}", "AI.KB Assistant"); }
        }

        private static void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Debug.WriteLine(line);
            Console.WriteLine(line);
        }
    }
}
