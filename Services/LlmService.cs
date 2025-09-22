using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>本地規則 v2；回傳強型別 ClassificationResult，第三階段可混合 LLM。</summary>
    public sealed class LlmService
    {
        private readonly AppConfig _cfg;
        public LlmService(AppConfig cfg) => _cfg = cfg ?? new AppConfig();

        public Task<ClassificationResult> ClassifyAsync(string filename, string? text = null)
        {
            text ??= filename ?? string.Empty;
            var (cat, conf, reason) = RuleBasedV2(filename ?? "", text);

            var result = new ClassificationResult
            {
                Category = cat,
                FileType = NormalizeExt(Path.GetExtension(filename ?? "")),
                Confidence = conf,
                Reason = reason
            };
            return Task.FromResult(result);
        }

        private static string NormalizeExt(string? ext)
        {
            ext ??= string.Empty;
            if (ext.StartsWith(".")) ext = ext[1..];
            return ext.ToLowerInvariant();
        }

        private (string category, double confidence, string reason) RuleBasedV2(string filename, string text)
        {
            var ext = NormalizeExt(Path.GetExtension(filename) ?? "");
            var cls = _cfg.Classification;

            // 1) 副檔名
            foreach (var kv in cls.ExtensionMap)
            {
                if (kv.Value.Any(e =>
                {
                    var ee = e.StartsWith(".") ? e[1..] : e;
                    return string.Equals(ee, ext, StringComparison.OrdinalIgnoreCase);
                }))
                    return (kv.Key, 0.90, $"副檔名命中：.{ext} → {kv.Key}");
            }

            // 2) 關鍵字
            var low = (filename + " " + (text ?? "")).ToLowerInvariant();
            foreach (var kv in cls.KeywordMap)
                if (kv.Value.Any(k => low.Contains(k.ToLowerInvariant())))
                    return (kv.Key, 0.80, $"關鍵字命中：{kv.Key}");

            // 3) 正則
            foreach (var kv in cls.RegexMap)
                if (kv.Value.Any(pattern => Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase)))
                    return (kv.Key, 0.85, $"正則命中：{kv.Key}");

            // fallback
            return (cls.FallbackCategory, 0.30, "無規則命中 → fallback");
        }
    }
}
