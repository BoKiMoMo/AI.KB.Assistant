using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public static class ThemeService
    {
        /// <summary>
        /// 相容舊呼叫；目前不從 cfg 取色（避免型別差異）。保留入口以免編譯錯誤。
        /// </summary>
        public static void Apply(AppConfig cfg)
        {
            // 若之後你在 AppConfig 重新定義 Theme 相關屬性，再從這裡讀取並呼叫 SetBrush。
            // 目前留白即可，確保不拋例外。
        }

        /// <summary>
        /// 直接以十六進位色碼套用重點色（Primary）並自動生成 Hover。
        /// </summary>
        public static void ApplyAccent(string hex)
        {
            var primary = ToBrushSafe(hex) ?? new SolidColorBrush(Colors.SteelBlue);
            var hover = new SolidColorBrush(Lighten(((SolidColorBrush)primary).Color, 0.12));

            SetResourceBrush("App.PrimaryBrush", primary);
            SetResourceBrush("App.PrimaryHoverBrush", hover);
        }

        // ================= Helper =================

        private static void SetResourceBrush(string key, SolidColorBrush brush)
        {
            brush.Freeze();
            if (Application.Current.Resources.Contains(key))
                Application.Current.Resources[key] = brush;
            else
                Application.Current.Resources.Add(key, brush);
        }

        private static SolidColorBrush? ToBrushSafe(string? hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return null;
                var c = (Color)ColorConverter.ConvertFromString(NormalizeHex(hex))!;
                return new SolidColorBrush(c);
            }
            catch { return null; }
        }

        private static string NormalizeHex(string s)
        {
            s = s.Trim();
            if (!s.StartsWith("#")) s = "#" + s;
            // #RGB -> #RRGGBB
            if (s.Length == 4)
            {
                var r = s[1]; var g = s[2]; var b = s[3];
                s = $"#{r}{r}{g}{g}{b}{b}";
            }
            // #RRGGBB -> #AARRGGBB（補滿不透明）
            if (s.Length == 7) s = "#FF" + s.Substring(1);
            return s.ToUpperInvariant();
        }

        private static Color Lighten(Color c, double by)
        {
            byte L(byte v) => (byte)Math.Clamp(v + (255 - v) * by, 0, 255);
            return Color.FromArgb(c.A, L(c.R), L(c.G), L(c.B));
        }
    }
}
