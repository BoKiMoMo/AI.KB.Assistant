using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Common;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V20.0 (最終修復版)
    /// 1. (V19.1) 整合「首次啟動」[cite:"Services/ConfigService.cs (V20.0 最終版) (line 34)"] 邏輯 (IsNewUserConfig [cite:"Services/ConfigService.cs (V20.0 最終版) (line 34)"]、空白路徑 [cite:"Services/ConfigService.cs (V20.0 最終版) (lines 156, 159)"]、簡易 UI [cite:"Services/ConfigService.cs (V20.0 最終版) (line 162)"])。
    /// 2. (V20.0) `CreateDefault` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 153)"] 已包含「中文類別」[cite:"Services/ConfigService.cs (V20.0 最終版) (lines 165-212)"] 和「VS 專案黑名單」[cite:"Services/ConfigService.cs (V20.0 最終版) (line 214)"]。
    /// 3. [V20.0 快取修復] 修正 `Save()` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 90)"]，使其在儲存後更新靜態快取 `Cfg` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 101) (modified)"]。
    /// 4. [V20.0 黑名單修復] 修正 `CreateDefault()` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 153)"]，將 `MapV76ToV734` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 228) (modified)"] 改為 `MapV734ToV76` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 228) (modified)"]。
    /// 5. [V20.0 黑名單修復] 修正 `Load()` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 55)"]，將 `MapV76ToV734` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 86) (modified)"] 移至 `if (File.Exists)` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 60)"] 區塊中。
    /// </summary>
    public static class ConfigService
    {
        public static string ConfigPath { get; private set; }

        public static AppConfig Cfg { get; private set; }

        /// <summary>
        /// [V19.1 首次啟動修復] 
        /// 旗標：指示 `CreateDefault()` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 153)"] 是否剛被呼叫。
        /// `App.xaml.cs` [cite:"App.xaml.cs (V20.0 最終版)"] 將使用此旗標來決定是否顯示「歡迎」訊息。
        /// </summary>
        public static bool IsNewUserConfig { get; private set; } = false;

        public static event Action<AppConfig> ConfigChanged = delegate { };

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        static ConfigService()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AI.KB.Assistant");
            Directory.CreateDirectory(appDataDir);
            ConfigPath = Path.Combine(appDataDir, "config.json");

            Cfg = Load();
        }

        public static AppConfig Load()
        {
            // [V19.1 首次啟動修復] 
            // 每次 Load 時都應重置旗標
            IsNewUserConfig = false;

            AppConfig? cfg = null;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    cfg = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);

                    // [V20.0 黑名單修復] 
                    // 智能映射 (V9.1) 僅應在載入現有檔案時執行
                    if (cfg != null)
                    {
                        MapV76ToV734(cfg);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService.Load Error] {ex.Message}");
                // App.LogCrash("ConfigService.Load.Deserialize", ex);
            }

            if (cfg == null)
            {
                // [V19.1 首次啟動修復] 
                // 檔案不存在，設定旗標，並建立空白路徑的預設值
                IsNewUserConfig = true;
                cfg = CreateDefault();
                Save(cfg); // 儲存預設值
            }

            // [V20.0 黑名單修復] 
            // 移除第 86 行 [cite:"Services/ConfigService.cs (V20.0 最終版) (line 86)"] 的 MapV76ToV734(cfg) 呼叫
            // MapV76ToV734(cfg);

            Cfg = cfg;
            ConfigChanged?.Invoke(Cfg);
            return Cfg;
        }

        public static bool Save(AppConfig? cfgToSave = null)
        {
            var cfg = cfgToSave ?? Cfg;
            if (cfg == null) return false;

            try
            {
                // 智能映射 (V9.1)
                MapV734ToV76(cfg);

                var json = JsonSerializer.Serialize(cfg, _jsonOptions);
                File.WriteAllText(ConfigPath, json);

                // [V20.0 快取修復] 
                // 儲存後，必須手動更新靜態快取 (Cfg) 並觸發事件
                // 以解決「設定頁面儲存後再開啟內容為空」[cite:"設定頁面，設定路徑儲存後再開啟內容為空。"] 的 BUG
                Cfg = cfg;
                ConfigChanged?.Invoke(Cfg);

                // [V19.1 啟動修復] 移除 Load();，避免 IsNewUserConfig [cite:"Services/ConfigService.cs (V20.0 最終版) (line 34)"] 被重置
                // Load(); 
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService.Save Error] {ex.Message}");
                // App.LogCrash("ConfigService.Save.Serialize", ex);
                return false;
            }
        }

        /// <summary>
        /// 映射 (config.json V7.6 -> C# V7.34)
        /// </summary>
        private static void MapV76ToV734(AppConfig cfg)
        {
            if (cfg == null) return;

            // 1. 映射 DB 路徑
            if (cfg.App != null && cfg.Db != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.App.DbPath))
                {
                    cfg.Db.DbPath = cfg.App.DbPath;
                }
            }

            // 2. 映射黑名單
            if (cfg.Import != null && cfg.Routing != null)
            {
                // [V20.0 黑名單修復] 
                // 僅在 Routing 為空時才從 Import 讀取 (避免覆蓋)
                if (cfg.Routing.BlacklistExts == null || cfg.Routing.BlacklistExts.Count == 0)
                {
                    cfg.Routing.BlacklistExts = cfg.Import.BlacklistExts ?? new List<string>();
                }
                if (cfg.Routing.BlacklistFolderNames == null || cfg.Routing.BlacklistFolderNames.Count == 0)
                {
                    cfg.Routing.BlacklistFolderNames = cfg.Import.BlacklistFolderNames ?? new List<string>();
                }
            }
        }

        /// <summary>
        /// 映射 (C# V7.34 -> config.json V7.6)
        /// </summary>
        private static void MapV734ToV76(AppConfig cfg)
        {
            if (cfg == null) return;

            // 1. 映射 DB 路徑
            if (cfg.App != null && cfg.Db != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.Db.DbPath))
                {
                    cfg.App.DbPath = cfg.Db.DbPath;
                }
            }

            // 2. 映射黑名單
            if (cfg.Import != null && cfg.Routing != null)
            {
                cfg.Import.BlacklistExts = cfg.Routing.BlacklistExts ?? new List<string>();
                cfg.Import.BlacklistFolderNames = cfg.Routing.BlacklistFolderNames ?? new List<string>();
            }
        }

        /// <summary>
        /// [V20.0 最終修復版] 建立預設設定檔
        /// </summary>
        private static AppConfig CreateDefault()
        {
            var cfg = new AppConfig();

            // [V19.1 首次啟動修復] 
            // 預設路徑必須為空 [cite:"Services/ConfigService.cs (V20.0 最終版) (lines 156, 159)"]，以觸發 `App.xaml.cs` [cite:"App.xaml.cs (V20.0 最終版)"] 的防呆
            cfg.App.RootDir = "";
            cfg.App.DbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AI.KB.Assistant", "ai_kb.db");
            cfg.Import.HotFolder = "";

            // [V19.1 首次啟動修復] 
            // 預設啟動模式為 "Simple" [cite:"Services/ConfigService.cs (V20.0 最終版) (line 162)"]
            cfg.App.LaunchMode = "Simple";

            // [V20.0] 完備的中文類別 [cite:"Services/ConfigService.cs (V20.0 最終版) (lines 165-212)"]
            cfg.Routing.ExtensionGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                // 視覺
                { "圖片影像", new List<string> { "png", "jpg", "jpeg", "gif", "bmp", "tiff", "heic", "webp", "avif", "raw", "cr2", "nef", "dng" } },
                { "向量繪圖", new List<string> { "ai", "eps", "svg" } },
                { "設計檔案", new List<string> { "psd", "psb", "xd", "fig", "sketch", "ind", "indd", "idml", "afphoto", "afdesign" } },
                { "3D模型", new List<string> { "obj", "fbx", "blend", "stl", "dae", "3ds", "max", "c4d", "glb", "gltf" } },
                { "字型檔案", new List<string> { "ttf", "otf", "woff", "woff2", "eot", "font" } },

                // 媒體
                { "影片檔案", new List<string> { "mp4", "mov", "avi", "mkv", "webm", "m4v", "wmv", "flv", "mpg", "mpeg", "3gp", "prores" } },
                { "音訊檔案", new List<string> { "mp3", "wav", "aac", "m4a", "flac", "ogg", "wma", "aiff", "opus" } },
                { "字幕檔案", new List<string> { "srt", "ass", "vtt", "sub" } },

                // 文件
                { "文書文件", new List<string> { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp", "rtf" } },
                { "筆記", new List<string> { "md", "markdown", "txt", "log", "old", "unknown" } },
                { "數據資料", new List<string> { "csv", "json", "xml", "yaml", "yml", "parquet", "feather", "npy", "h5", "sav", "mat", "db", "sqlite", "sql", "xmind" } },

                // 開發
                { "程式原始碼", new List<string> { "cs", "py", "js", "ts", "jsx", "tsx", "vue", "java", "kt", "go", "rs", "cpp", "c", "h", "swift", "php", "rb", "dart", "r", "lua", "pl", "sh", "ps1", "bat", "cmd", "html", "css", "scss" } },
                { "專案檔案", new List<string> { "prproj", "aep", "aepx", "mogrt", "drp", "drproj", "veg", "imovieproj", "resolve" } },
                { "建置檔案", new List<string> { "dockerfile", "makefile", "gradle", "cmake", "sln", "csproj", "vcxproj", "xcodeproj", "pbxproj" } },
                { "套件設定", new List<string> { "npmrc", "package.json", "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "requirements.txt", "pipfile", "poetry.lock", "gemfile", "go.mod", "go.sum" } },
                { "設定檔案", new List<string> { "env", "config", "cfg", "ini", "toml", "jsonc", "plist" } },

                // 其他
                { "壓縮封存", new List<string> { "zip", "rar", "7z", "tar", "gz", "bz2" } },
                { "執行檔案", new List<string> { "exe", "dll", "app", "pkg", "deb", "rpm", "bin", "run" } },

                // 備援 (Fallback)
                { "其他", new List<string>() }
            };

            cfg.Routing.FolderOrder = new List<string> { "year", "month", "project", "category" };

            // [V20.0 C-2 黑名單 BUG 修復]
            // 首次啟動時，自動加入 VS 專案黑名單 [cite:"Services/ConfigService.cs (V20.0 最終版) (line 214)"]
            cfg.Routing.BlacklistFolderNames = new List<string>
            {
                ".vs",
                "bin",
                "obj",
                "packages",
                "_DoNotMove",
                "_blacklist"
            };

            cfg.Routing.BlacklistExts = new List<string> { "tmp", "bak", "dmg", "iso" };

            // [V20.0 黑名單修復] 
            // 呼叫 `MapV734ToV76` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 143)"] (寫入)，
            // 而不是 `MapV76ToV734` [cite:"Services/ConfigService.cs (V20.0 最終版) (line 127)"] (讀取)
            MapV734ToV76(cfg);

            return cfg;
        }
    }
}