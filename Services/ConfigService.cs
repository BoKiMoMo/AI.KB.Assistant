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

            // 副檔名
            AddIfMissing(cfg.Classification.ExtensionMap, "圖片", ".png", ".jpg", ".jpeg", ".gif", ".tif", ".bmp", ".webp");
            AddIfMissing(cfg.Classification.ExtensionMap, "影音", ".mp4", ".mov", ".avi", ".mkv", ".mp3", ".wav", ".m4a");
            AddIfMissing(cfg.Classification.ExtensionMap, "壓縮", ".zip", ".rar", ".7z");
            AddIfMissing(cfg.Classification.ExtensionMap, "簡報", ".ppt", ".pptx", ".key");
            AddIfMissing(cfg.Classification.ExtensionMap, "報告", ".doc", ".docx", ".pdf");
            AddIfMissing(cfg.Classification.ExtensionMap, "報表", ".xls", ".xlsx", ".csv");
            AddIfMissing(cfg.Classification.ExtensionMap, "程式碼", ".cs", ".ts", ".js", ".py", ".java", ".cpp", ".json", ".xml", ".yaml", ".yml");
            AddIfMissing(cfg.Classification.ExtensionMap, "筆記", ".txt", ".md", ".rtf");

            // 關鍵字
            AddIfMissing(cfg.Classification.KeywordMap, "會議", "會議", "meeting", "minutes", "議程");
            AddIfMissing(cfg.Classification.KeywordMap, "財務", "發票", "invoice", "收據", "報銷");
            AddIfMissing(cfg.Classification.KeywordMap, "合約", "契約", "contract", "agreement");
            AddIfMissing(cfg.Classification.KeywordMap, "人事", "履歷", "resume", "cv", "hr");
            AddIfMissing(cfg.Classification.KeywordMap, "研究", "paper", "thesis", "analysis", "研究");
            AddIfMissing(cfg.Classification.KeywordMap, "設計", "design", "ui", "ux", "figma");
            AddIfMissing(cfg.Classification.KeywordMap, "行銷", "marketing", "campaign", "品牌");
            AddIfMissing(cfg.Classification.KeywordMap, "法務", "legal", "compliance", "法務");

            // 正則
            AddIfMissing(cfg.Classification.RegexMap, "財務", @"invoice\s*#?\d+", @"發票\d+");

            return cfg;
        }
    }
}
