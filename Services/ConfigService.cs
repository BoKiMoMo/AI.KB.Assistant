// Services/ConfigService.cs
using System;
using System.IO;
using AI.KB.Assistant.Models;
using Newtonsoft.Json;

namespace AI.KB.Assistant.Services
{
    public static class ConfigService
    {
        // 建議用統一設定：縮排＋忽略 null、允許註解
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static AppConfig TryLoad(string path = "config.json")
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

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
                return new AppConfig();

            var json = File.ReadAllText(path);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _jsonSettings) ?? new AppConfig();

            // 確保各區塊不為 null
            cfg.App ??= new AppSection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();
            cfg.OpenAI ??= new OpenAISection();

            return cfg;
        }

        public static void Save(string path, AppConfig cfg)
        {
            cfg ??= new AppConfig();

            // 確保各區塊不為 null（避免寫出來缺段）
            cfg.App ??= new AppSection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();
            cfg.OpenAI ??= new OpenAISection();

            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            var json = JsonConvert.SerializeObject(cfg, _jsonSettings);

            // 用 UTF-8 (含 BOM) 寫出以避免中文亂碼
            using var sw = new StreamWriter(full, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            sw.Write(json);
        }
    }
}
