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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1) 載入設定（新版統一用 AppConfig.Load）
            AppConfig.Load();

            // 2) 可在此即時套用主題重點色（若你有 PrimaryBrush 之類資源）
            // ThemeService.Apply(AppConfig.Current);
            // ThemeService.ApplyAccent("#3B82F6");

            // 3) 依啟動模式決定進入畫面（相容舊/新屬性）
            var modeText = (AppConfig.Current.App?.StartupUIMode ?? "Detailed").Trim();
            if (!Enum.TryParse<StartupMode>(modeText, true, out var mode))
                mode = StartupMode.Detailed;

            // 4) 啟動主畫面
            var main = new MainWindow();
            main.Show();
        }
    }
}
