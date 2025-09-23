// Services/LlmService.cs
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>第二階段：本地規則分類（副檔名 → 關鍵字 → 正則；全失敗→fallback）</summary>
    public sealed class LlmService
    {
        private readonly AppConfig _cfg;
        public LlmService(AppConfig cfg) => _cfg = cfg;

        public Task<(string category, double confidence, string reason)> ClassifyAsync(string filename, string? text = null)
        {
            var (cat, conf, reason) = RuleBasedV2(filename, text ?? filename);
            return Task.FromResult((cat, conf, reason));
        }

        private (string category, double confidence, string reason) RuleBasedV2(string filename, string text)
        {
            string ext = (System.IO.Path.GetExtension(filename) ?? "").ToLowerInvariant();
            var cls = _cfg.Classification;

            // 1) 副檔名
            foreach (var kv in cls.ExtensionMap)
            {
                if (kv.Value.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
                    return (kv.Key, 0.90, $"副檔名命中：{ext} → {kv.Key}");
            }

            // 2) 關鍵字
            var low = (filename + " " + text).ToLowerInvariant();
            foreach (var kv in cls.KeywordMap)
            {
                if (kv.Value.Any(k => low.Contains(k.ToLowerInvariant())))
                    return (kv.Key, 0.80, $"關鍵字命中：{kv.Key}");
            }

            // 3) 正則
            foreach (var kv in cls.RegexMap)
            {
                if (kv.Value.Any(pattern => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)))
                    return (kv.Key, 0.85, $"正則命中：{kv.Key}");
            }

            // 4) fallback
            return (cls.FallbackCategory, 0.30, "無規則命中 → fallback");
        }
    }
}
