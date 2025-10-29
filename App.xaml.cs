using System;
using System.IO;
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
        public static AppConfig Config { get; private set; } = new AppConfig();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1) 載入設定
            var cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            Config = ConfigService.TryLoad(cfgPath);

            // 2) 可在此即時套用主題重點色（若你有 PrimaryBrush 之類資源）
            // ThemeService.Apply(Config); // 目前 ThemeService.Apply() 為相容空實作
            // 或直接 ThemeService.ApplyAccent("#3B82F6");

            // 3) 依啟動模式決定進入畫面（相容舊/新屬性）
            var modeText = (Config.App?.StartupUIMode ?? "Detailed").Trim();
            if (!Enum.TryParse<StartupMode>(modeText, true, out var mode))
                mode = StartupMode.Detailed;

            // 4) 啟動 MainWindow
            var main = new MainWindow();
            main.Show();
        }
    }
}
