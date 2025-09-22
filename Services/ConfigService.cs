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
            catch { return SeedDefaults(new AppConfig()); }
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
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);

            var json = JsonConvert.SerializeObject(cfg, _json);
            using var sw = new StreamWriter(full, false, new System.Text.UTF8Encoding(true));
            sw.Write(json);
        }

        /// <summary>�ɻ��w�]�]�S�O�O�W�h�� Extension/Keyword/Regex�^</summary>
        private static AppConfig SeedDefaults(AppConfig cfg)
        {
            cfg.App ??= new(); cfg.OpenAI ??= new(); cfg.Routing ??= new();
            cfg.Classification ??= new(); cfg.Views ??= new();

            static void AddIfMissing(System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> map, string key, params string[] values)
            {
                if (!map.ContainsKey(key)) map[key] = new();
                foreach (var v in values)
                {
                    var vv = v.StartsWith(".") ? v.ToLowerInvariant() : ("." + v.ToLowerInvariant());
                    if (!map[key].Contains(vv)) map[key].Add(vv);
                }
            }

            // ���ɦW
            AddIfMissing(cfg.Classification.ExtensionMap, "�Ϥ�", "png", "jpg", "jpeg", "gif", "tif", "bmp", "webp");
            AddIfMissing(cfg.Classification.ExtensionMap, "�v��", "mp4", "mov", "avi", "mkv", "mp3", "wav", "m4a");
            AddIfMissing(cfg.Classification.ExtensionMap, "���Y", "zip", "rar", "7z");
            AddIfMissing(cfg.Classification.ExtensionMap, "²��", "ppt", "pptx", "key");
            AddIfMissing(cfg.Classification.ExtensionMap, "���i", "doc", "docx", "pdf");
            AddIfMissing(cfg.Classification.ExtensionMap, "����", "xls", "xlsx", "csv");
            AddIfMissing(cfg.Classification.ExtensionMap, "�{���X", "cs", "ts", "js", "py", "java", "cpp", "json", "xml", "yaml", "yml");
            AddIfMissing(cfg.Classification.ExtensionMap, "���O", "txt", "md", "rtf");

            // ����r
            void K(string cat, params string[] words)
            {
                if (!cfg.Classification.KeywordMap.ContainsKey(cat)) cfg.Classification.KeywordMap[cat] = new();
                foreach (var w in words) if (!cfg.Classification.KeywordMap[cat].Contains(w)) cfg.Classification.KeywordMap[cat].Add(w);
            }
            K("�|ĳ", "�|ĳ", "meeting", "minutes", "ĳ�{");
            K("�]��", "�o��", "invoice", "����", "���P");
            K("�X��", "����", "contract", "agreement");
            K("�H��", "�i��", "resume", "cv", "hr");
            K("��s", "paper", "thesis", "analysis", "��s");
            K("�]�p", "design", "ui", "ux", "figma");
            K("��P", "marketing", "campaign", "�~�P");
            K("�k��", "legal", "compliance", "�k��");

            // ���h
            void R(string cat, params string[] patterns)
            {
                if (!cfg.Classification.RegexMap.ContainsKey(cat)) cfg.Classification.RegexMap[cat] = new();
                foreach (var p in patterns) if (!cfg.Classification.RegexMap[cat].Contains(p)) cfg.Classification.RegexMap[cat].Add(p);
            }
            R("�]��", @"invoice\s*#?\d+", @"�o��\d+");

            return cfg;
        }
    }
}
