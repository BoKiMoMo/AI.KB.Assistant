using System;
using System.IO;
using Newtonsoft.Json;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 設定檔存取服務（讀/寫 config.json）
    /// </summary>
    public static class ConfigService
    {
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        /// <summary>
        /// 讀取設定；若檔案不存在回傳預設物件。
        /// 失敗會擲出例外。
        /// </summary>
        public static AppConfig Load(string path)
        {
            if (!File.Exists(path)) return new AppConfig();

            var json = File.ReadAllText(path);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _jsonSettings) ?? new AppConfig();
            EnsureSections(cfg);
            return cfg;
        }

        /// <summary>
        /// 嘗試讀取設定；任何錯誤都回傳預設物件，不擲例外。
        /// </summary>
        public static AppConfig TryLoad(string path)
        {
            try
            {
                return Load(path);
            }
            catch
            {
                return new AppConfig();
            }
        }

        /// <summary>
        /// 儲存設定為 UTF-8 (BOM)；會自動建立目錄。
        /// </summary>
        public static void Save(string path, AppConfig cfg)
        {
            EnsureSections(cfg);

            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir!);

            var json = JsonConvert.SerializeObject(cfg, _jsonSettings);
            using var sw = new StreamWriter(full, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            sw.Write(json);
        }

        /// <summary>
        /// 確保各子區塊不為 null（避免舊檔或手動編輯造成空值）
        /// </summary>
        private static void EnsureSections(AppConfig cfg)
        {
            cfg.App ??= new AppSection();
            cfg.OpenAI ??= new OpenAISection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();

            // 必填欄位的保底值（避免空字串）
            cfg.Classification.ClassificationMode ??= "category";
            cfg.Classification.TimeGranularity ??= "month";
            cfg.Classification.FallbackCategory ??= "其他";
            cfg.App.MoveMode ??= "copy";
        }
    }
}
