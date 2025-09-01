using System;
using System.IO;
using Newtonsoft.Json;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 提供設定檔讀取與儲存的服務
    /// </summary>
    public static class ConfigService
    {
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// 從指定路徑載入設定檔（預設為 config.json）
        /// </summary>
        public static AppConfig Load(string path = "config.json")
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"找不到設定檔：{path}");

            var json = File.ReadAllText(path);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _jsonSettings) ?? new();

            EnsureDirectories(cfg);
            return cfg;
        }

        /// <summary>
        /// 嘗試載入設定檔，若失敗則回傳預設值
        /// </summary>
        public static AppConfig TryLoad(string path = "config.json")
        {
            try
            {
                return Load(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService] 載入失敗：{ex.Message}，將使用預設值。");
                var cfg = new AppConfig();
                EnsureDirectories(cfg);
                return cfg;
            }
        }

        /// <summary>
        /// 儲存設定檔到指定路徑
        /// </summary>
        public static void Save(string path, AppConfig cfg)
        {
            cfg ??= new AppConfig();

            // 確保內層不為 null
            cfg.App ??= new AppSection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();
            cfg.OpenAI ??= new OpenAISection();

            // 確保目的地資料夾存在
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            // 寫回 JSON
            var json = JsonConvert.SerializeObject(cfg, _jsonSettings);
            File.WriteAllText(full, json);
        }

        /// <summary>
        /// 確保設定檔指定的資料夾存在
        /// </summary>
        private static void EnsureDirectories(AppConfig cfg)
        {
            if (!string.IsNullOrWhiteSpace(cfg.App.DbPath))
            {
                var dbDir = Path.GetDirectoryName(cfg.App.DbPath);
                if (!string.IsNullOrEmpty(dbDir))
                    Directory.CreateDirectory(dbDir);
            }

            if (!string.IsNullOrWhiteSpace(cfg.App.RootDir))
                Directory.CreateDirectory(cfg.App.RootDir);

            if (!string.IsNullOrWhiteSpace(cfg.App.InboxDir))
                Directory.CreateDirectory(cfg.App.InboxDir);
        }
    }
}
