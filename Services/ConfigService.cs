using System.IO;
using Newtonsoft.Json;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public static class ConfigService
    {
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        public static AppConfig TryLoad(string path)
        {
            try { return Load(path); } catch { return new AppConfig(); }
        }

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path)) return new AppConfig();

            var json = File.ReadAllText(path);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _jsonSettings) ?? new AppConfig();

            cfg.App ??= new AppSection();
            cfg.OpenAI ??= new OpenAISection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();

            return cfg;
        }

        /// <summary>存檔（UTF-8 BOM 以避免中文在記事本亂碼）</summary>
        public static void Save(string path, AppConfig cfg)
        {
            cfg.App ??= new AppSection();
            cfg.OpenAI ??= new OpenAISection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();

            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            var json = JsonConvert.SerializeObject(cfg, _jsonSettings);
            using var sw = new StreamWriter(full, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            sw.Write(json);
        }
    }
}
