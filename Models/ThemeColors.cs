using System.Text.Json.Serialization;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 使用者自訂主題色盤（十六進位色碼，含 #）。
    /// 欄位名稱完全對齊 SettingsWindow.xaml.cs 讀寫的屬性。
    /// </summary>
    public sealed class ThemeColors
    {
        // Base / text
        [JsonPropertyName("background")] public string Background { get; set; } = "#1D232A";
        [JsonPropertyName("panel")] public string Panel { get; set; } = "#222833";
        [JsonPropertyName("border")] public string Border { get; set; } = "#2E3642";
        [JsonPropertyName("text")] public string Text { get; set; } = "#E6EAF0";
        [JsonPropertyName("textMuted")] public string TextMuted { get; set; } = "#9AA4B2";

        // Accent
        [JsonPropertyName("primary")] public string Primary { get; set; } = "#3AA0FF";
        [JsonPropertyName("primaryHover")] public string PrimaryHover { get; set; } = "#58B3FF";
        [JsonPropertyName("secondary")] public string Secondary { get; set; } = "#64748B";

        // Banner (info/warn/error 背景色，走偏亮以利辨識)
        [JsonPropertyName("bannerInfo")] public string BannerInfo { get; set; } = "#FFF8D6";
        [JsonPropertyName("bannerWarn")] public string BannerWarn { get; set; } = "#FFEAD5";
        [JsonPropertyName("bannerError")] public string BannerError { get; set; } = "#FFE2E5";

        // Semantic
        [JsonPropertyName("success")] public string Success { get; set; } = "#20C997";
        [JsonPropertyName("warning")] public string Warning { get; set; } = "#FFC107";
        [JsonPropertyName("error")] public string Error { get; set; } = "#FF6B6B";

        public static ThemeColors Default() => new ThemeColors();
    }
}
