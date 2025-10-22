using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.KB.Assistant.Models
{
    public sealed class AppConfig
    {
        [JsonPropertyName("app")]
        public AppSection App { get; set; } = new();

        [JsonPropertyName("import")]
        public ImportSection Import { get; set; } = new();

        [JsonPropertyName("routing")]
        public RoutingSection Routing { get; set; } = new();

        [JsonPropertyName("openAI")]
        public OpenAISection OpenAI { get; set; } = new();

        [JsonPropertyName("classification")]
        public ClassificationSection Classification { get; set; } = new();

        // ★ 新增：完整主題色彩設定（供 ThemeService / SettingsWindow 使用）
        [JsonPropertyName("theme")]
        public ThemeSection Theme { get; set; } = ThemeSection.Default();

        [JsonPropertyName("themeColors")]
        public ThemeColors ThemeColors { get; set; } = new ThemeColors();


        // ============== Sections ==============

        public sealed class AppSection
        {
            [JsonPropertyName("dbPath")] public string DbPath { get; set; } = "data.db";
            [JsonPropertyName("rootDir")] public string RootDir { get; set; } = "";
            [JsonPropertyName("projectLock")] public string ProjectLock { get; set; } = "";
            // 舊版曾在這裡放 theme 名稱，保留以維持相容（僅當作字串標記，不參與 ThemeService）
            [JsonPropertyName("theme")] public string? ThemeName { get; set; }
        }

        public sealed class ImportSection
        {
            [JsonPropertyName("autoOnDrop")] public bool AutoOnDrop { get; set; } = true;
            [JsonPropertyName("includeSubdirectories")] public bool IncludeSubdirectories { get; set; } = true;

            [JsonPropertyName("hotFolderPath")] public string HotFolderPath { get; set; } = "";
            [JsonPropertyName("enableHotFolder")] public bool EnableHotFolder { get; set; } = false;

            [JsonPropertyName("blacklistFolderNames")] public string[] BlacklistFolderNames { get; set; } = Array.Empty<string>();
            [JsonPropertyName("blacklistExts")] public string[] BlacklistExts { get; set; } = Array.Empty<string>();

            [JsonPropertyName("moveMode")]
            [JsonConverter(typeof(LowercaseEnumConverter<MoveMode>))]
            public MoveMode MoveMode { get; set; } = MoveMode.Move;

            [JsonPropertyName("overwritePolicy")]
            [JsonConverter(typeof(LowercaseEnumConverter<OverwritePolicy>))]
            public OverwritePolicy OverwritePolicy { get; set; } = OverwritePolicy.Rename;

            // 舊版相容：單一黑名單欄位
            [JsonPropertyName("blacklistFolderName")] public string? _compatSingleFolderName { get; set; }
        }

        public sealed class RoutingSection
        {
            [JsonPropertyName("useYear")] public bool UseYear { get; set; } = true;
            [JsonPropertyName("useMonth")] public bool UseMonth { get; set; } = true;
            [JsonPropertyName("useType")] public bool UseType { get; set; } = true;
            [JsonPropertyName("useProject")] public bool UseProject { get; set; } = true;

            [JsonPropertyName("autoFolderName")] public string AutoFolderName { get; set; } = "自整理";

            [JsonPropertyName("extensionGroups")]
            public Dictionary<string, string[]> ExtensionGroups { get; set; } = DefaultExtensionGroups();

            private static Dictionary<string, string[]> DefaultExtensionGroups()
            {
                return new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Images"] = new[] { "png", "jpg", "jpeg", "gif", "bmp", "tiff", "heic", "webp", "avif", "raw", "cr2", "nef", "dng" },
                    ["Vector"] = new[] { "ai", "eps", "svg", "pdf" },
                    ["Design"] = new[] { "psd", "psb", "xd", "fig", "sketch", "ind", "indd", "idml", "afphoto", "afdesign" },
                    ["Documents"] = new[] { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp", "rtf", "txt", "md" },
                    ["Videos"] = new[] { "mp4", "mov", "avi", "mkv", "webm", "m4v", "wmv", "flv", "mpg", "mpeg", "3gp", "prores" },
                    ["Audio"] = new[] { "mp3", "wav", "aac", "m4a", "flac", "ogg", "wma", "aiff", "opus" },
                    ["Projects"] = new[] { "prproj", "aep", "aepx", "mogrt", "drp", "drproj", "veg", "imovieproj", "resolve" },
                    ["Subtitles"] = new[] { "srt", "ass", "vtt", "sub" },
                    ["3DModels"] = new[] { "obj", "fbx", "blend", "stl", "dae", "3ds", "max", "c4d", "glb", "gltf" },
                    ["Fonts"] = new[] { "ttf", "otf", "woff", "woff2", "eot", "font" },
                    ["Data"] = new[] { "csv", "json", "xml", "yaml", "yml", "parquet", "feather", "npy", "h5", "sav", "mat", "db", "sqlite", "sql", "xmind" },
                    ["BuildFiles"] = new[] { "dockerfile", "makefile", "gradle", "cmake", "sln", "csproj", "vcxproj", "xcodeproj", "pbxproj" },
                    ["Package"] = new[] { "npmrc", "package.json", "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "requirements.txt", "pipfile", "poetry.lock", "gemfile", "go.mod", "go.sum" },
                    ["Code"] = new[] { "cs", "py", "js", "ts", "jsx", "tsx", "vue", "java", "kt", "go", "rs", "cpp", "c", "h", "swift", "php", "rb", "dart", "r", "lua", "pl", "sh", "ps1", "bat", "cmd", "html", "css", "scss", "toml", "ini", "jsonc" },
                    ["Config"] = new[] { "env", "config", "cfg", "ini", "toml", "jsonc", "yml", "yaml", "xml", "plist" },
                    ["Archives"] = new[] { "zip", "rar", "7z", "tar", "gz", "bz2" },
                    ["Executables"] = new[] { "exe", "dll", "app", "pkg", "deb", "rpm", "bin", "run" },
                    ["Notes"] = new[] { "md", "markdown", "txt" },
                    ["Others"] = new[] { "log", "old", "unknown" }
                };
            }
        }

        public sealed class OpenAISection
        {
            [JsonPropertyName("apiKey")] public string ApiKey { get; set; } = "";
            [JsonPropertyName("model")] public string Model { get; set; } = "gpt-4o-mini";
            [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = "";
            [JsonPropertyName("enableWhenLowConfidence")] public bool EnableWhenLowConfidence { get; set; } = true;

            // 舊版相容
            [JsonPropertyName("enable")] public bool? _compatEnable { get; set; }
        }

        public sealed class ClassificationSection
        {
            [JsonPropertyName("confidenceThreshold")] public double ConfidenceThreshold { get; set; } = 0.65;

            // 舊版相容
            [JsonPropertyName("useLLM")] public bool? _compatUseLlm { get; set; }
        }

        // ★ 新增：主題顏色區段（字串用 #RRGGBB 或 #AARRGGBB）
        public sealed class ThemeSection
        {
            // 基礎
            [JsonPropertyName("background")] public string Background { get; set; } = "#111319";
            [JsonPropertyName("panel")] public string Panel { get; set; } = "#1B1F2A";
            [JsonPropertyName("border")] public string Border { get; set; } = "#2A3140";
            [JsonPropertyName("text")] public string Text { get; set; } = "#E7EAF0";
            [JsonPropertyName("textMuted")] public string TextMuted { get; set; } = "#A9B1C1";

            // 主色
            [JsonPropertyName("primary")] public string Primary { get; set; } = "#3B82F6";
            [JsonPropertyName("primaryHover")] public string PrimaryHover { get; set; } = "#60A5FA";
            [JsonPropertyName("secondary")] public string Secondary { get; set; } = "#64748B";

            // Banner / 狀態
            [JsonPropertyName("bannerInfo")] public string BannerInfo { get; set; } = "#FFF8D6";
            [JsonPropertyName("bannerWarn")] public string BannerWarn { get; set; } = "#FFE7E7";
            [JsonPropertyName("bannerError")] public string BannerError { get; set; } = "#FFE1E1";

            [JsonPropertyName("success")] public string Success { get; set; } = "#22C55E";
            [JsonPropertyName("warning")] public string Warning { get; set; } = "#F59E0B";
            [JsonPropertyName("error")] public string Error { get; set; } = "#EF4444";

            public static ThemeSection Default() => new ThemeSection();
        }

        // ============== Load / Save ==============

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
                return new AppConfig();

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions()) ?? new AppConfig();

            // 舊版相容：單一黑名單欄位 → 陣列
            if (!string.IsNullOrWhiteSpace(cfg.Import._compatSingleFolderName) &&
                (cfg.Import.BlacklistFolderNames == null || cfg.Import.BlacklistFolderNames.Length == 0))
            {
                cfg.Import.BlacklistFolderNames = new[] { cfg.Import._compatSingleFolderName! };
            }

            // 舊版相容：分類是否使用 LLM → OpenAI.EnableWhenLowConfidence
            if (cfg.Classification._compatUseLlm.HasValue && cfg.Classification._compatUseLlm.Value == false)
                cfg.OpenAI.EnableWhenLowConfidence = false;

            // 舊版相容：OpenAI.enable → EnableWhenLowConfidence
            if (cfg.OpenAI._compatEnable.HasValue)
                cfg.OpenAI.EnableWhenLowConfidence = cfg.OpenAI._compatEnable.Value;

            // 保險：Theme 區段不存在時補預設
            cfg.Theme ??= ThemeSection.Default();

            return cfg;
        }

        public static void Save(string path, AppConfig cfg)
        {
            var json = JsonSerializer.Serialize(cfg, JsonOptions(indented: true));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            File.WriteAllText(path, json);
        }

        private static JsonSerializerOptions JsonOptions(bool indented = false) => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = indented,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    // ====== 相依型別（沿用你專案裡的定義） ======
    public enum MoveMode { Move = 0, Copy = 1 }
    public enum OverwritePolicy { Replace = 0, Rename = 1, Skip = 2 }

    /// <summary>把 enum 以小寫寫入/讀取 JSON 的 converter（沿用你的專案習慣）</summary>
    public sealed class LowercaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s)) return default;
            if (Enum.TryParse<T>(s, ignoreCase: true, out var v)) return v;
            return default;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString().ToLowerInvariant());
        }
    }

}
