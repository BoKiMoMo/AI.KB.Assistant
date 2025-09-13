using System.IO;
using System.Text;
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
            if (!File.Exists(path)) return new AppConfig();
            return Load(path);
        }

        public static AppConfig Load(string path)
        {
            var json = File.ReadAllText(path);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _jsonSettings) ?? new AppConfig();

            cfg.App ??= new AppSection();
            cfg.OpenAI ??= new OpenAISection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();

            // 預先建立資料夾（若已設定）
            if (!string.IsNullOrWhiteSpace(cfg.App.RootDir)) Directory.CreateDirectory(cfg.App.RootDir);
            if (!string.IsNullOrWhiteSpace(cfg.App.InboxDir)) Directory.CreateDirectory(cfg.App.InboxDir);
            if (!string.IsNullOrWhiteSpace(cfg.App.DbPath))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(cfg.App.DbPath));
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir!);
            }

            return cfg;
        }

        public static void Save(string path, AppConfig cfg)
        {
            cfg.App ??= new AppSection();
            cfg.OpenAI ??= new OpenAISection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();

            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir!);

            var json = JsonConvert.SerializeObject(cfg, _jsonSettings);
            using var sw = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); // UTF-8 BOM
            sw.Write(json);
        }
    }
}
