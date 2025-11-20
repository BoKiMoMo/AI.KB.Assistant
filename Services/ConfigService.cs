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
    /// V20.4 (優化版)
    /// 1. [V20.0] 修正 V19/V20 的所有啟動與黑名單錯誤。
    /// 2. [V20.4] 優化 3：`CreateDefault` 現在會填入預設的 AI 提示詞 (Prompts)。
    /// </summary>
    public static class ConfigService
    {
        public static string ConfigPath { get; private set; }

        public static AppConfig Cfg { get; private set; }

        /// <summary>
        /// [V19.1 首次啟動修復] 
        /// 旗標：指示 `CreateDefault()` 是否剛被呼叫。
        /// `App.xaml.cs` 將使用此旗標來決定是否顯示「歡迎」訊息。
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

            // [V20.4] 檢查：如果舊設定檔沒有 Prompts，補上預設值
            if (cfg.Prompts == null || string.IsNullOrWhiteSpace(cfg.Prompts.AnalyzeConfidence))
            {
                cfg.Prompts = GetDefaultPrompts();
                // 注意：這裡不設定 IsNewUserConfig，但我們儲存以確保檔案是最新的
                Save(cfg);
            }


            // [V20.0 黑名單修復] 
            // 移除第 86 行的 MapV76ToV734(cfg) 呼叫
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
                // 以解決「設定頁面儲存後再開啟內容為空」 的 BUG
                Cfg = cfg;
                ConfigChanged?.Invoke(Cfg);

                // [V19.1 啟動修復] 移除 Load();，避免 IsNewUserConfig 被重置
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
        /// [V20.4] 優化 3：取得預設的 AI 提示詞
        /// </summary>
        private static PromptConfig GetDefaultPrompts()
        {
            return new PromptConfig
            {
                AnalyzeConfidence = "您是一個文件分類信心度分析師。" +
                                    "請分析使用者提供的檔名。" +
                                    "您**必須**只回傳一個介於 0.0 到 1.0 之間的數字 (JSON 格式的數字)，" +
                                    "代表您有多大的信心能正確分類此檔案。範例：0.85",

                Summarize = "您是一個專業的檔案摘要器。" +
                            "請根據使用者提供的文字 (通常是檔名或路徑)，用繁體中文產生一句話的簡潔摘要。",

                SuggestTags = "您是一個檔案標籤專家。" +
                              "請根據使用者提供的文字 (通常是檔名)，" +
                              "產生 3 到 5 個最相關的繁體中文標籤。" +
                              "請只回傳標籤本身，並用逗號 (,) 分隔。範例: '報告,財務,2025,Q3'",

                SuggestProject = "您是一個專業的檔案分類師。" +
                                 "請根據使用者提供的檔名或路徑，建議一個最適合的「專案名稱」。" +
                                 "**必須**只回傳專案名稱本身 (建議使用 英文/數字/底線)，不要包含任何解釋。" +
                                 "範例：'2025_Q3_Marketing' 或 'Project_Alpha'"
            };
        }

        /// <summary>
        /// [V20.0 最終修復版] 建立預設設定檔
        /// </summary>
        private static AppConfig CreateDefault()
        {
            var cfg = new AppConfig();

            // [V19.1 首次啟動修復] 
            // 預設路徑必須為空，以觸發 `App.xaml.cs` 的防呆
            cfg.App.RootDir = "";
            cfg.App.DbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AI.KB.Assistant", "ai_kb.db");
            cfg.Import.HotFolder = "";

            // [V19.1 首次啟動修復] 
            // 預設啟動模式為 "Simple"
            cfg.App.LaunchMode = "Simple";

            // [V20.0] 完備的中文類別
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
            // 首次啟動時，自動加入 VS 專案黑名單
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

            // [V20.4] 優化 3：填入預設提示詞
            cfg.Prompts = GetDefaultPrompts();

            // [V20.0 黑名單修復] 
            // 呼叫 `MapV734ToV76` (寫入)，
            // 而不是 `MapV76ToV734` (讀取)
            MapV734ToV76(cfg);

            return cfg;
        }
    }
}