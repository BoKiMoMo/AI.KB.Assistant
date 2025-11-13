using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Common; // V7.34 LogCrash 依賴

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V17.0 (V9.1 修正版)
    /// 1. (V9.1) [CS1061 修正] 智能映射 (Mapping) V7.6 (config.json) 與 V7.34 (C#)
    /// 2. [V17.0 修正 BUG #3.3] 修正 'CreateDefault' [Line 131]，將 'pdf' 從 "Vector" [Line 136] 移至 "Documents" [Line 137]。
    /// </summary>
    public static class ConfigService
    {
        // (V9.1)
        public static string ConfigPath { get; private set; }

        public static AppConfig Cfg { get; private set; }

        // (V9.1)
        public static event Action<AppConfig> ConfigChanged = delegate { };

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            // (V7.7)
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        static ConfigService()
        {
            // (V7.34)
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AI.KB.Assistant");
            Directory.CreateDirectory(appDataDir);
            ConfigPath = Path.Combine(appDataDir, "config.json");

            Cfg = Load();
        }

        public static AppConfig Load()
        {
            AppConfig? cfg = null;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    cfg = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                }
            }
            catch (Exception ex)
            {
                // (V9.6)
                Console.WriteLine($"[ConfigService.Load Error] {ex.Message}");
                // App.LogCrash("ConfigService.Load.Deserialize", ex);
            }

            if (cfg == null)
            {
                cfg = CreateDefault();
                Save(cfg); // 儲存預設值
            }

            // (V9.1) V7.6 (config.json) -> V7.34 (C#)
            MapV76ToV734(cfg);

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
                // (V9.1) V7.34 (C#) -> V7.6 (config.json)
                MapV734ToV76(cfg);

                var json = JsonSerializer.Serialize(cfg, _jsonOptions);
                File.WriteAllText(ConfigPath, json);

                Load();
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
        /// (V9.1) 映射 (config.json V7.6 -> C# V7.34)
        /// </summary>
        private static void MapV76ToV734(AppConfig cfg)
        {
            if (cfg == null) return;

            // 1. 映射 DB 路徑 (V7.6 app.dbPath -> V7.34 db.dbPath)
            if (cfg.App != null && cfg.Db != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.App.DbPath))
                {
                    cfg.Db.DbPath = cfg.App.DbPath;
                }
            }

            // 2. 映射黑名單 (V7.6 import.blacklist -> V7.34 routing.blacklist)
            if (cfg.Import != null && cfg.Routing != null)
            {
                cfg.Routing.BlacklistExts = cfg.Import.BlacklistExts ?? new List<string>();
                cfg.Routing.BlacklistFolderNames = cfg.Import.BlacklistFolderNames ?? new List<string>();
            }
        }

        /// <summary>
        /// (V9.1) 映射 (C# V7.34 -> config.json V7.6)
        /// </summary>
        private static void MapV734ToV76(AppConfig cfg)
        {
            if (cfg == null) return;

            // 1. 映射 DB 路徑 (V7.34 db.dbPath -> V7.6 app.dbPath)
            if (cfg.App != null && cfg.Db != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.Db.DbPath))
                {
                    cfg.App.DbPath = cfg.Db.DbPath;
                }
            }

            // 2. 映射黑名單 (V7.34 routing.blacklist -> V7.6 import.blacklist)
            if (cfg.Import != null && cfg.Routing != null)
            {
                cfg.Import.BlacklistExts = cfg.Routing.BlacklistExts ?? new List<string>();
                cfg.Import.BlacklistFolderNames = cfg.Routing.BlacklistFolderNames ?? new List<string>();
            }
        }

        private static AppConfig CreateDefault()
        {
            var cfg = new AppConfig();

            // (V7.6) 結構
            cfg.App.RootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AI_KB_Root");
            cfg.App.DbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AI.KB.Assistant", "ai_kb.db");
            cfg.Import.HotFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AI_KB_Inbox");

            cfg.Routing.ExtensionGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Images", new List<string> { "png", "jpg", "jpeg", "gif", "bmp", "tiff", "heic", "webp", "avif", "raw" } },
                // [V17.0 修正 BUG #3.3] 移除 'pdf'
                { "Vector", new List<string> { "ai", "eps", "svg" } }, 
                // [V17.0 修正 BUG #3.3] 'pdf' 現在只在 Documents 中
                { "Documents", new List<string> { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp", "rtf", "txt", "md" } },
                { "Videos", new List<string> { "mp4", "mov", "avi", "mkv", "webm", "m4v" } },
                { "Audio", new List<string> { "mp3", "wav", "aac", "m4a", "flac" } },
                { "Archives", new List<string> { "zip", "rar", "7z", "tar", "gz" } },
                { "Code", new List<string> { "cs", "py", "js", "ts", "html", "css", "json", "xml" } },
                { "Others", new List<string>() } // 'Others' 作為預設
            };

            cfg.Routing.FolderOrder = new List<string> { "year", "month", "project", "category" };

            // (V9.1)
            MapV76ToV734(cfg);

            return cfg;
        }
    }
}