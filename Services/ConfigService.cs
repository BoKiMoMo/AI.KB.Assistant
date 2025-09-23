// Services/ConfigService.cs
using System.IO;
using Newtonsoft.Json;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public static class ConfigService
    {
        private static readonly JsonSerializerSettings _json = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        public static AppConfig TryLoad(string path)
        {
            try { return Load(path); }
            catch { return new AppConfig(); }
        }

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path)) return SeedDefaults(new AppConfig());

            var json = File.ReadAllText(path);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _json) ?? new AppConfig();
            return SeedDefaults(cfg);
        }

        public static void Save(string path, AppConfig cfg)
        {
            cfg ??= new AppConfig();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var json = JsonConvert.SerializeObject(cfg, _json);
            using var sw = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
            sw.Write(json);
        }

        private static AppConfig SeedDefaults(AppConfig cfg)
        {
            cfg.App ??= new(); cfg.OpenAI ??= new(); cfg.Routing ??= new();
            cfg.Classification ??= new(); cfg.Views ??= new();

            void AddIfMissing(System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> map, string key, params string[] values)
            {
                if (!map.ContainsKey(key)) map[key] = new();
                foreach (var v in values)
                    if (!map[key].Contains(v)) map[key].Add(v);
            }

            // ���ɦW
            AddIfMissing(cfg.Classification.ExtensionMap, "�Ϥ�", ".png", ".jpg", ".jpeg", ".gif", ".tif", ".bmp", ".webp");
            AddIfMissing(cfg.Classification.ExtensionMap, "�v��", ".mp4", ".mov", ".avi", ".mkv", ".mp3", ".wav", ".m4a");
            AddIfMissing(cfg.Classification.ExtensionMap, "���Y", ".zip", ".rar", ".7z");
            AddIfMissing(cfg.Classification.ExtensionMap, "²��", ".ppt", ".pptx", ".key");
            AddIfMissing(cfg.Classification.ExtensionMap, "���i", ".doc", ".docx", ".pdf");
            AddIfMissing(cfg.Classification.ExtensionMap, "����", ".xls", ".xlsx", ".csv");
            AddIfMissing(cfg.Classification.ExtensionMap, "�{���X", ".cs", ".ts", ".js", ".py", ".java", ".cpp", ".json", ".xml", ".yaml", ".yml");
            AddIfMissing(cfg.Classification.ExtensionMap, "���O", ".txt", ".md", ".rtf");

            // ����r
            AddIfMissing(cfg.Classification.KeywordMap, "�|ĳ", "�|ĳ", "meeting", "minutes", "ĳ�{");
            AddIfMissing(cfg.Classification.KeywordMap, "�]��", "�o��", "invoice", "����", "���P");
            AddIfMissing(cfg.Classification.KeywordMap, "�X��", "����", "contract", "agreement");
            AddIfMissing(cfg.Classification.KeywordMap, "�H��", "�i��", "resume", "cv", "hr");
            AddIfMissing(cfg.Classification.KeywordMap, "��s", "paper", "thesis", "analysis", "��s");
            AddIfMissing(cfg.Classification.KeywordMap, "�]�p", "design", "ui", "ux", "figma");
            AddIfMissing(cfg.Classification.KeywordMap, "��P", "marketing", "campaign", "�~�P");
            AddIfMissing(cfg.Classification.KeywordMap, "�k��", "legal", "compliance", "�k��");

            // ���h
            AddIfMissing(cfg.Classification.RegexMap, "�]��", @"invoice\s*#?\d+", @"�o��\d+");

            return cfg;
        }
    }
}
