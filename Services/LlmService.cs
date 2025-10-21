using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// LLM 服務：目前內建本地啟發式 + 非同步介面。
    /// 若將來要串接 OpenAI，只要在本類內部替換策略即可（介面不變）。
    /// </summary>
    public sealed class LlmService : IDisposable
    {
        private readonly AppConfig _cfg;

        public LlmService(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();
        }

        /// <summary>是否具備雲端 API Key（給 UI 顯示狀態用）。</summary>
        public bool IsReady => !string.IsNullOrWhiteSpace(_cfg.OpenAI?.ApiKey);

        /// <summary>
        /// 依多個檔名提出「可能的專案」建議清單（依關鍵字與共同 token 推估）。
        /// </summary>
        public Task<List<string>> SuggestProjectNamesAsync(IEnumerable<string> filenames, CancellationToken ct)
        {
            var list = (filenames ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (list.Count == 0) return Task.FromResult(new List<string>());

            // 取常見 token
            var tokens = list
                .SelectMany(name =>
                    (name ?? "")
                        .ToLowerInvariant()
                        .Split(new[] { ' ', '_', '-', '.', '[', ']', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(t => t.Length >= 2 && t.Length <= 24)
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .Take(6)
                .Select(g => g.Key)
                .ToList();

            // 一些常見專案建議
            var commons = new[] { "AI專案", "設計稿", "會議記錄", "提案文件", "內部資料", "個人筆記" };

            var result = new List<string>();
            result.AddRange(tokens.Select(ToTitle));
            result.AddRange(commons);

            // 去重、保留順序
            var dedup = result.Where(s => !string.IsNullOrWhiteSpace(s))
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Take(10)
                              .ToList();

            return Task.FromResult(dedup);
        }

        /// <summary>
        /// 精煉分類：輸入目前專案/分類，回傳 (project, category, reasoning)。
        /// </summary>
        public async Task<(string project, string category, string reasoning)> RefineAsync(
            string filename, string currentProject, string currentCategory, CancellationToken ct)
        {
            // 模擬非同步（若將來串雲端 API，保留 await 介面即可）
            await Task.Delay(50, ct);

            // 適度的啟發式微調
            var proj = string.IsNullOrWhiteSpace(currentProject)
                ? GuessProjectFromName(filename)
                : currentProject;

            var cat = string.IsNullOrWhiteSpace(currentCategory)
                ? GuessCategoryFromName(filename)
                : currentCategory;

            var reason = $"依檔名「{filename}」與既有設定，推估專案：{proj}、分類：{cat}。";
            return (proj, cat, reason);
        }

        /// <summary>
        /// 嘗試輸出完整分類（若之後要用在自動預分類可沿用），附信心分數與理由。
        /// </summary>
        public Task<(string Project, string Category, double Confidence, string Reason)> TryClassifyAsync(
            string filename, CancellationToken ct)
        {
            var project = GuessProjectFromName(filename);
            var category = GuessCategoryFromName(filename);
            var reason = $"依關鍵字與常見模式推估；檔名：{filename}";
            const double confidence = 0.72; // 與 AppConfig 預設門檻對齊
            return Task.FromResult((project, category, confidence, reason));
        }

        // === helpers ===

        private static string GuessProjectFromName(string filename)
        {
            var f = (filename ?? "").ToLowerInvariant();
            if (f.Contains("ai")) return "AI專案";
            if (f.Contains("design") || f.Contains("ui") || f.Contains("ux")) return "設計稿";
            if (f.Contains("meeting") || f.Contains("minutes")) return "會議記錄";
            if (f.Contains("proposal") || f.Contains("pitch")) return "提案文件";

            var token = f.Split(new[] { ' ', '_', '-', '.', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
                         .FirstOrDefault();
            return string.IsNullOrWhiteSpace(token) ? "未分類" : ToTitle(token);
        }

        private static string GuessCategoryFromName(string filename)
        {
            var f = (filename ?? "").ToLowerInvariant();
            if (f.Contains("invoice") || f.Contains("發票")) return "財務";
            if (f.Contains("contract") || f.Contains("合約")) return "合約";
            if (f.Contains("resume") || f.Contains("履歷")) return "履歷";
            if (f.Contains("report") || f.Contains("報告")) return "報告";
            if (f.Contains("proposal") || f.Contains("提案")) return "提案";
            if (f.Contains("spec") || f.Contains("規格")) return "規格";
            return "一般";
        }

        private static string ToTitle(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return token;
            if (token.Length == 1) return token.ToUpperInvariant();
            return char.ToUpperInvariant(token[0]) + token.Substring(1);
        }

        public void Dispose()
        {
            // 目前無需釋放資源；若串 OpenAI HttpClient 可在此處理
        }
    }
}
