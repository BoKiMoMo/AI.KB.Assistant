using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Views;

namespace AI.KB.Assistant
{
    /// <summary>
    /// 相容舊版：啟動模式（僅用於 App 啟動判斷）
    /// </summary>
    public enum StartupMode
    {
        Detailed,
        Simple
    }

    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1️⃣ 載入設定（新版統一用 AppConfig.Load）
            AppConfig.Load();
            // 若存在 config.json 則讀取，不存在會自動建立預設。

            // 2️⃣ 初始化核心服務
            var db = new DbService();
            await db.InitializeAsync(); // SQLite 或 JSONL 自動建檔

            var intake = new IntakeService(db);
            var router = new RoutingService(AppConfig.Current);
            var llm = new LlmService(AppConfig.Current);

            // 3️⃣ 將服務註冊到全域資源，可供 MainWindow 直接取用
            Resources["Db"] = db;
            Resources["Intake"] = intake;
            Resources["Router"] = router;
            Resources["Llm"] = llm;

            // 4️⃣ 若有主題系統，可在此即時套用（保留你的註解）
            // ThemeService.Apply(AppConfig.Current);
            // ThemeService.ApplyAccent("#3B82F6");

            // 5️⃣ 依啟動模式決定進入畫面
            var modeText = (AppConfig.Current.App?.StartupUIMode ?? "Detailed").Trim();
            if (!Enum.TryParse<StartupMode>(modeText, true, out var mode))
                mode = StartupMode.Detailed;

            // 6️⃣ 啟動主畫面
            var main = new MainWindow();
            main.Show();

            // ✅ TODO: 若要在啟動時自動匯入 HotFolder，可在此加入：
            // var hot = AppConfig.Current.Import?.HotFolder;
            // if (!string.IsNullOrWhiteSpace(hot) && Directory.Exists(hot))
            // {
            //     var files = Directory.GetFiles(hot);
            //     await intake.IntakeFilesAsync(files);
            // }
        }
    }
}
