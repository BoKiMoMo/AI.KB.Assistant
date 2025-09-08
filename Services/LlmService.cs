using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 分類服務：可用本地規則或 LLM
    /// </summary>
    public sealed class LlmService
    {
        private readonly AppConfig _cfg;

        // 關鍵字白名單對照
        private readonly Dictionary<string, string[]> _keywordMap = new()
        {
            ["會議"] = new[] { "會議", "開會", "meeting", "minutes" },
            ["報告"] = new[] { "報告", "report" },
            ["財務"] = new[] { "財務", "發票", "invoice", "帳單", "bill" },
            ["合約"] = new[] { "合約", "契約", "contract" },
            ["人事"] = new[] { "人事", "hr", "resume", "履歷" },
            ["研究"] = new[] { "研究", "paper", "thesis", "analysis" },
            ["設計"] = new[] { "設計", "design", "ui", "ux", "figma", "psd" },
            ["簡報"] = new[] { "簡報", "slides", "presentation", "ppt" },
            ["行銷"] = new[] { "行銷", "marketing", "campaign" },
            ["法務"] = new[] { "法務", "法律", "legal", "compliance" },
            ["技術"] = new[] { "技術", "code", "程式", "source", "git" },
            ["圖片"] = new[] { "圖片", "image", "photo", "jpg", "png" },
            ["影音"] = new[] { "影音", "video", "audio", "mp3", "mp4", "mov" },
            ["壓縮"] = new[] { "壓縮", "zip", "rar", "7z" },
            ["個人文件"] = new[] { "個人", "private", "self" },
            ["採購/供應商"] = new[] { "採購", "供應商", "採買", "purchase", "vendor" },
            ["教學/課程"] = new[] { "教學", "課程", "lesson", "class", "tutorial" },
            ["其他"] = Array.Empty<string>()
        };

        public LlmService(AppConfig cfg)
        {
            _cfg = cfg;
        }

        /// <summary>
        /// 對外 API：分類檔案名稱或文字內容
        /// </summary>
        public async Task<(string primary_category, double confidence, string summary, string reasoning)>
            ClassifyAsync(string text)
        {
            if (_cfg.Classification.Engine == "dummy")
            {
                return await Task.FromResult(RuleBasedClassify(text));
            }

            if (_cfg.Classification.Engine == "llm" && !string.IsNullOrEmpty(_cfg.OpenAI.ApiKey))
            {
                // TODO: 串接 OpenAI Chat Completions
                // 現在先回傳假資料避免編譯錯誤
                return await Task.FromResult(("其他", 0.5, "暫無摘要", "尚未串接 LLM"));
            }

            // hybrid：先本地規則，低信心再丟給 LLM（尚未實作）
            return await Task.FromResult(RuleBasedClassify(text));
        }

        /// <summary>
        /// 本地規則分類
        /// </summary>
        private (string primary_category, double confidence, string summary, string reasoning)
            RuleBasedClassify(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (_cfg.Classification.FallbackCategory, 0.0, "", "空白輸入");

            text = text.ToLowerInvariant();
            foreach (var kv in _keywordMap)
            {
                if (kv.Value.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return (kv.Key, 0.9, $"自動判定為 {kv.Key}", $"關鍵字命中：{kv.Key}");
                }
            }

            // 沒命中 → fallback
            return (_cfg.Classification.FallbackCategory, 0.3,
                    $"歸類至 {_cfg.Classification.FallbackCategory}",
                    "未命中任何規則");
        }
    }
}
