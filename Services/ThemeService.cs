using System;
using System.Windows;
using System.Windows.Media;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public static class ThemeService
    {
        public static void ApplyFromConfig(AppConfig cfg)
        {
            if (cfg?.Theme == null) return;
            var t = cfg.Theme;

            SetBrush("App.BackgroundBrush", t.Background);
            SetBrush("App.PanelBrush", t.Panel);
            SetBrush("App.BorderBrush", t.Border);
            SetBrush("App.TextBrush", t.Text);
            SetBrush("App.TextMutedBrush", t.TextMuted);

            SetBrush("App.PrimaryBrush", t.Primary);
            SetBrush("App.PrimaryHover", t.PrimaryHover);
            SetBrush("App.SecondaryBrush", t.Secondary);

            SetBrush("App.BannerInfoBrush", t.BannerInfo);
            SetBrush("App.BannerWarnBrush", t.BannerWarn);
            SetBrush("App.BannerErrorBrush", t.BannerError);

            SetBrush("App.SuccessBrush", t.Success);
            SetBrush("App.WarningBrush", t.Warning);
            SetBrush("App.ErrorBrush", t.Error);
        }

        private static void SetBrush(string key, string hex)
        {
            if (Application.Current?.Resources == null) return;
            if (!TryParseColor(hex, out var color)) return;

            var brush = Application.Current.Resources[key] as SolidColorBrush;
            if (brush == null)
                Application.Current.Resources[key] = new SolidColorBrush(color);
            else
                brush.Color = color; // 即時更新
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
                return true;
            }
            catch
            {
                color = Colors.Transparent;
                return false;
            }
        }
    }
}
