using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class LlmService
    {
        private readonly AppConfig _cfg;

        // 簡易關鍵字規則（fallback / local 引擎用）
        private readonly Dictionary<string, string[]> _keywordMap = new()
        {
            ["會議"] = new[] { "會議", "meeting", "minutes" },
            ["報告"] = new[] { "報告", "report" },
            ["財務"] = new[] { "財務", "發票", "invoice", "帳單" },
            ["合約"] = new[] { "合約", "契約", "contract" },
            ["人事"] = new[] { "人事", "hr", "resume", "履歷" },
            ["研究"] = new[] { "研究", "paper", "thesis", "analysis" },
            ["設計"] = new[] { "設計", "design", "ui", "ux", "figma", "psd" },
            ["簡報"] = new[] { "簡報", "slides", "presentation", "ppt" },
            ["行銷"] = new[] { "行銷", "marketing", "campaign" },
            ["法務"] = new[] { "法務", "法律", "legal" },
            ["技術"] = new[] { "技術", "code", "程式", "source", "git" },
            ["圖片"] = new[] { "圖片", "image", "photo", "jpg", "png" },
            ["影音"] = new[] { "影音", "video", "audio", "mp3", "mp4", "mov" },
            ["壓縮"] = new[] { "壓縮", "zip", "rar", "7z" },
            ["個人文件"] = new[] { "個人", "private", "self" },
            ["教學/課程"] = new[] { "教學", "課程", "lesson", "class", "tutorial" },
            ["採購/供應商"] = new[] { "採購", "供應商", "採買", "vendor" },
            ["其他"] = Array.Empty<string>()
        };

        public LlmService(AppConfig cfg) => _cfg = cfg;

        /* =================== A1: 分類 =================== */
        public async Task<(string category, double confidence, string summary, string reasoning)>
            ClassifyAsync(string filename, string? content = null, CancellationToken ct = default)
        {
            // 若沒開 LLM 或沒 API Key → 使用本地規則
            if (!_cfg.Classification.UseLLM || string.IsNullOrWhiteSpace(_cfg.OpenAI.ApiKey))
                return await Task.FromResult(RuleBasedClassify(filename + " " + (content ?? "")));

            try
            {
                // TODO: 接 OpenAI Completions（保留接口）
                // 這裡先以「強化版規則」模擬，並給較溫和的信心值
                var local = RuleBasedClassify(filename + " " + (content ?? ""));
                var boosted = Math.Min(0.95, Math.Max(0.55, local.confidence + 0.1));
                return (local.category, boosted, local.summary, "LLM 模擬（介面預留）");
            }
            catch
            {
                // 失敗 → fallback
                return RuleBasedClassify(filename + " " + (content ?? ""));
            }
        }

        private (string category, double confidence, string summary, string reasoning)
            RuleBasedClassify(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (_cfg.Classification.FallbackCategory, 0.3, "", "空白輸入");

            var lower = text.ToLowerInvariant();
            foreach (var kv in _keywordMap)
            {
                if (kv.Value.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return (kv.Key, 0.85, $"自動判定為 {kv.Key}", $"關鍵字命中：{kv.Key}");
            }
            return (_cfg.Classification.FallbackCategory, 0.5, $"歸類至 {_cfg.Classification.FallbackCategory}", "未命中規則");
        }

        /* =================== A2: 摘要與標籤 =================== */
        public async Task<string> SummarizeAsync(string filename, string? content = null, CancellationToken ct = default)
        {
            var baseName = System.IO.Path.GetFileNameWithoutExtension(filename);
            if (!_cfg.Classification.UseLLM || string.IsNullOrWhiteSpace(_cfg.OpenAI.ApiKey))
            {
                // 本地簡化摘要
                var s = (content ?? baseName);
                s = s.Length > 40 ? s[..40] + "..." : s;
                return await Task.FromResult(s);
            }

            try
            {
                // TODO: 接 OpenAI；暫用簡化
                var s = (content ?? baseName);
                s = s.Length > 50 ? s[..50] + "..." : s;
                return await Task.FromResult(s);
            }
            catch
            {
                var s = (content ?? baseName);
                return s.Length > 40 ? s[..40] + "..." : s;
            }
        }

        public async Task<string[]> SuggestTagsAsync(string filename, string category, string? summary, CancellationToken ct = default)
        {
            // 本地：用幾個已知詞 + 類別，並去重
            var seed = new List<string>();
            if (!string.IsNullOrWhiteSpace(category)) seed.Add(category);

            var lower = filename.ToLowerInvariant();
            foreach (var kv in _keywordMap)
            {
                if (kv.Value.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase))) seed.Add(kv.Key);
            }

            if (!string.IsNullOrWhiteSpace(summary))
            {
                var words = summary.Split(new[] { ' ', '　', ',', '，', '/', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Where(w => w.Length >= 2 && w.Length <= 8)
                                   .Take(8);
                seed.AddRange(words);
            }

            var tags = seed.Select(NormalizeTag)
                           .Where(s => !string.IsNullOrWhiteSpace(s))
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .Take(Math.Max(1, Math.Min(10, _cfg.Classification.MaxTags)))
                           .ToArray();

            return await Task.FromResult(tags);

            static string NormalizeTag(string s)
            {
                s = s.Trim();
                if (s.Length > 12) s = s[..12];
                return s.Replace("#", "").Replace(";", "、");
            }
        }

        /* =================== A3: 對話搜尋解析 =================== */
        public async Task<(string? keyword, string[]? categories, string[]? tags, long? from, long? to)>
            ParseQueryAsync(string question, CancellationToken ct = default)
        {
            // 簡化版解析器（不呼叫 LLM 也能動）
            // 支援語句：上個月、本月、今年、去年；含「會議/報告/合約」等詞映成類別；#標籤
            question = (question ?? "").Trim();

            string? keyword = null;
            var cats = new List<string>();
            var tgs = new List<string>();
            long? from = null;
            long? to = null;

            // 標籤：#xxx
            foreach (var token in question.Split(' ', '　'))
            {
                if (token.StartsWith("#") && token.Length > 1) tgs.Add(token[1..]);
            }

            // 類別詞彙對應
            foreach (var kv in _keywordMap.Keys)
            {
                if (question.Contains(kv, StringComparison.OrdinalIgnoreCase)) cats.Add(kv);
            }
            cats = cats.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // 日期語意（本月、上個月、今年、去年）
            var now = DateTimeOffset.Now;
            if (question.Contains("上個月") || question.Contains("上月"))
            {
                var d = now.AddMonths(-1);
                var fromDt = new DateTime(d.Year, d.Month, 1);
                var toDt = fromDt.AddMonths(1).AddSeconds(-1);
                from = new DateTimeOffset(fromDt).ToUnixTimeSeconds();
                to = new DateTimeOffset(toDt).ToUnixTimeSeconds();
            }
            else if (question.Contains("本月") || question.Contains("這個月"))
            {
                var fromDt = new DateTime(now.Year, now.Month, 1);
                var toDt = fromDt.AddMonths(1).AddSeconds(-1);
                from = new DateTimeOffset(fromDt).ToUnixTimeSeconds();
                to = new DateTimeOffset(toDt).ToUnixTimeSeconds();
            }
            else if (question.Contains("今年"))
            {
                var fromDt = new DateTime(now.Year, 1, 1);
                var toDt = new DateTime(now.Year, 12, 31, 23, 59, 59);
                from = new DateTimeOffset(fromDt).ToUnixTimeSeconds();
                to = new DateTimeOffset(toDt).ToUnixTimeSeconds();
            }
            else if (question.Contains("去年"))
            {
                var y = now.Year - 1;
                var fromDt = new DateTime(y, 1, 1);
                var toDt = new DateTime(y, 12, 31, 23, 59, 59);
                from = new DateTimeOffset(fromDt).ToUnixTimeSeconds();
                to = new DateTimeOffset(toDt).ToUnixTimeSeconds();
            }

            // 關鍵字：去掉 #標籤 後剩餘的文字
            var raw = string.Join(" ",
                question.Split(' ', '　').Where(t => !t.StartsWith("#")));
            keyword = string.IsNullOrWhiteSpace(raw) ? null : raw;

            return await Task.FromResult((keyword, cats.Count > 0 ? cats.ToArray() : null,
                                          tgs.Count > 0 ? tgs.ToArray() : null, from, to));
        }
    }
}
