using AI.KB.Assistant.Models;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 設定檔管理：讀取 / 寫入 / 預設補齊。
    /// </summary>
    public static class ConfigService
    {
        private static readonly JsonSerializerOptions _jsonOpt = new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true
        };

        private static readonly string _defaultPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        /// <summary>
        /// 舊介面相容：讀取指定路徑設定（失敗時回傳預設設定並建立檔案）。
        /// </summary>
        public static AppConfig TryLoad(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, _jsonOpt) ?? new AppConfig();
                    Normalize(cfg);
                    return cfg;
                }
            }
            catch
            {
                // ignore, fallback below
            }

            var def = BuildDefault();
            Save(def, path);
            return def;
        }

        /// <summary>
        /// 新介面：讀取預設路徑 (./config.json)
        /// </summary>
        public static AppConfig Load() => TryLoad(_defaultPath);

        /// <summary>
        /// 將設定存入預設路徑。
        /// </summary>
        public static void Save(AppConfig cfg) => Save(cfg, _defaultPath);

        /// <summary>
        /// 將設定存入指定路徑。
        /// </summary>
        public static void Save(AppConfig cfg, string path)
        {
            try
            {
                Normalize(cfg);
                var json = JsonSerializer.Serialize(cfg, _jsonOpt);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService] 寫入設定失敗：{ex.Message}");
            }
        }

        /// <summary>
        /// 補齊必要欄位、確保資料夾存在。
        /// </summary>
        public static void Normalize(AppConfig cfg)
        {
            cfg ??= new AppConfig();
            cfg.App ??= new AppSection();
            cfg.Import ??= new ImportSection();
            cfg.Classification ??= new ClassificationSection();
            cfg.ThemeColors ??= new ThemeSection();

            // Root / Db 預設
            if (string.IsNullOrWhiteSpace(cfg.App.RootDir))
                cfg.App.RootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AI.KB.Root");
            if (string.IsNullOrWhiteSpace(cfg.App.DbPath))
                cfg.App.DbPath = Path.Combine(cfg.App.RootDir, "ai.kb.assistant.db");

            // Import 預設
            if (string.IsNullOrWhiteSpace(cfg.Import.HotFolderPath))
                cfg.Import.HotFolderPath = Path.Combine(cfg.App.RootDir, "Inbox");
            cfg.Import.BlacklistExts ??= Array.Empty<string>();
            cfg.Import.BlacklistFolderNames ??= new[] { "_blacklist", "自整理", "信心不足" };
            cfg.Import.ExtGroups ??= new();
            cfg.Import.ExtGroupsCache ??= new();
            cfg.Import.RebuildExtGroupsCache();

            SafeCreateDir(cfg.App.RootDir);
            SafeCreateDir(cfg.Import.HotFolderPath);
            SafeCreateDir(Path.GetDirectoryName(cfg.App.DbPath));
        }

        /// <summary>
        /// 產生預設設定。
        /// </summary>
        private static AppConfig BuildDefault()
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AI.KB.Root");
            var inbox = Path.Combine(root, "Inbox");
            return new AppConfig
            {
                App = new AppSection
                {
                    RootDir = root,
                    DbPath = Path.Combine(root, "ai.kb.assistant.db"),
                    StartupUIMode = StartupMode.Simple,
                    ProjectLock = null
                },
                Import = new ImportSection
                {
                    HotFolderPath = inbox,
                    IncludeSubdir = true,
                    MoveMode = MoveMode.Move,
                    OverwritePolicy = OverwritePolicy.Rename,
                    BlacklistExts = Array.Empty<string>(),
                    BlacklistFolderNames = new[] { "_blacklist", "自整理", "信心不足" },
                    ExtGroups = new()
                },
                Classification = new ClassificationSection { ConfidenceThreshold = 0.75 },
                ThemeColors = new ThemeSection()
            };
        }

        private static void SafeCreateDir(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try { Directory.CreateDirectory(dir); } catch { }
        }
    }
}
