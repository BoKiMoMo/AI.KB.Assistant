using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Views;
using System;
using System.IO;
using System.Windows;

namespace AI.KB.Assistant
{
    public partial class App : Application
    {
        private string _cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private AppConfig _cfg = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1) 載入設定（不存在會自動產生預設檔）
            _cfg = ConfigService.TryLoad(_cfgPath);

            // 2) 套用主題
            try { ThemeService.Apply(_cfg); } catch { /* theme best-effort */ }

            // 3) 決定啟動 UI（預設 Simple=Launcher）
            Window w = _cfg.App.StartupUIMode == StartupMode.Detailed
                ? new MainWindow()
                : new LauncherWindow();

            w.Show();
        }
    }
}
