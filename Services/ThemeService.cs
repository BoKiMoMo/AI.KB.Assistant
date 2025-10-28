using AI.KB.Assistant.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 負責把 AppConfig.ThemeColors 轉成全域資源（Brush），
    /// 供 XAML 以 {DynamicResource ...} 取得並即時更新。
    /// </summary>
    public static class ThemeService
    {
        // 資源鍵（與 XAML 對齊）
        public const string BackgroundBrushKey = "App.BackgroundBrush";
        public const string PanelBrushKey = "App.PanelBrush";
        public const string BorderBrushKey = "App.BorderBrush";
        public const string TextBrushKey = "App.TextBrush";
        public const string TextMutedBrushKey = "App.TextMutedBrush";

        public const string PrimaryBrushKey = "App.PrimaryBrush";
        public const string PrimaryHoverBrushKey = "App.PrimaryHoverBrush";

        public const string SuccessBrushKey = "App.SuccessBrush";
        public const string WarningBrushKey = "App.WarningBrush";
        public const string ErrorBrushKey = "App.ErrorBrush";

        public const string BannerInfoBrushKey = "App.BannerInfoBrush";
        public const string BannerWarnBrushKey = "App.BannerWarnBrush";
        public const string BannerErrorBrushKey = "App.BannerErrorBrush";

        /// <summary>
        /// 從整份設定套用主題（常用）
        /// </summary>
        public static void Apply(AppConfig cfg)
        {
            if (cfg == null) return;
            Apply(cfg.ThemeColors ?? new ThemeSection());
        }

        /// <summary>
        /// 直接從 ThemeSection 套用主題
        /// </summary>
        public static void Apply(ThemeSection theme)
        {
            // 背景 / 面板 / 邊框 / 文字
            SetBrush(BackgroundBrushKey, theme.Background);
            SetBrush(PanelBrushKey, theme.Panel);
            SetBrush(BorderBrushKey, theme.Border);
            SetBrush(TextBrushKey, theme.Text);
            SetBrush(TextMutedBrushKey, theme.TextMuted);

            // 主色
            SetBrush(PrimaryBrushKey, theme.Primary);
            SetBrush(PrimaryHoverBrushKey, theme.PrimaryHover);

            // 狀態色
            SetBrush(SuccessBrushKey, theme.Success);
            SetBrush(WarningBrushKey, theme.Warning);
            SetBrush(ErrorBrushKey, theme.Error);

            // Banner
            SetBrush(BannerInfoBrushKey, theme.BannerInfo);
            SetBrush(BannerWarnBrushKey, theme.BannerWarn);
            SetBrush(BannerErrorBrushKey, theme.BannerError);
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        private static void SetBrush(string key, string colorHex)
        {
            var brush = ToBrush(colorHex);
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            // 若已存在則直接覆寫，否則新增
            if (dict.Contains(key))
                dict[key] = brush;
            else
                dict.Add(key, brush);
        }

        private static SolidColorBrush ToBrush(string hexOrName)
        {
            // 容錯：允許 #RGB / #ARGB / #RRGGBB / #AARRGGBB 或已知顏色名稱
            try
            {
                var c = ParseColor(hexOrName);
                var b = new SolidColorBrush(c);
                b.Freeze(); // 讓 Brush 可跨執行緒 & 效能更佳
                return b;
            }
            catch
            {
                // 退回安全預設（深灰）
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D2D"));
                b.Freeze();
                return b;
            }
        }

        private static Color ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Colors.Transparent;

            value = value.Trim();

            // 支援不帶 # 的 6/8 位十六進位
            if (!value.StartsWith("#", StringComparison.Ordinal))
            {
                // 嘗試顏色名稱（e.g., "Red"）
                try
                {
                    var named = (Color)ColorConverter.ConvertFromString(value);
                    return named;
                }
                catch { }

                // 嘗試直接以 RRGGBB/ AARRGGBB 解析
                if (value.Length is 6 or 8)
                    value = "#" + value;
                else
                    return Colors.Transparent;
            }

            // 標準 #RRGGBB / #AARRGGBB / #RGB / #ARGB
            return (Color)ColorConverter.ConvertFromString(value);
        }
    }
}
