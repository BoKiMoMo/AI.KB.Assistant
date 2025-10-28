using AI.KB.Assistant.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// LLM 輕量封裝（目前為本地 stub，無雲端呼叫；保留同介面以利日後換成 API 實作）
    /// </summary>
    public class LlmService
    {
        private AppConfig _cfg;

        public LlmService(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();
            ConfigService.Normalize(_cfg);
        }

        public void ApplyConfig(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();
            ConfigService.Normalize(_cfg);
        }

        /// <summary>產生簡易摘要（目前取檔名與副檔名作為示意）。</summary>
        public Task<string> SummarizeAsync(string filePath, CancellationToken ct = default)
        {
            var name = Path.GetFileName(filePath);
            var ext = Path.GetExtension(filePath).Trim('.').ToLowerInvariant();
            var summary = $"檔案：{name}（.{ext}）— 這是示意摘要；未接雲端模型。";
            return Task.FromResult(summary);
        }

        /// <summary>產生建議標籤（依副檔名群組 + 路徑線索）。</summary>
        public Task<string[]> SuggestTagsAsync(string filePath, CancellationToken ct = default)
        {
            var tags = Array.Empty<string>();
            var ext = Path.GetExtension(filePath).Trim('.').ToLowerInvariant();

            // 由 ExtGroupsCache 推論群組名當作 tag
            var group = _cfg.Import.ExtGroupsCache
                .FirstOrDefault(kv => kv.Value != null && kv.Value.Contains(ext)).Key;

            if (!string.IsNullOrWhiteSpace(group))
                tags = new[] { group };

            // 路徑上層資料夾名也當 tag
            var parent = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? string.Empty).Name;
            if (!string.IsNullOrWhiteSpace(parent))
                tags = tags.Concat(new[] { parent }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            if (tags.Length == 0) tags = new[] { "Uncategorized" };
            return Task.FromResult(tags);
        }

        /// <summary>簡易信心分析（若能在群組表中命中 => 提高信心）。</summary>
        public Task<double> AnalyzeConfidenceAsync(string filePath, CancellationToken ct = default)
        {
            var ext = Path.GetExtension(filePath).Trim('.').ToLowerInvariant();
            var hasGroup = _cfg.Import.ExtGroupsCache.Any(kv => kv.Value != null && kv.Value.Contains(ext));
            var baseScore = hasGroup ? 0.85 : 0.55;

            // 以檔名長度/是否含數字做點假訊號調整（純示意）
            var name = Path.GetFileNameWithoutExtension(filePath);
            if (name.Any(char.IsDigit)) baseScore += 0.05;

            // clamp
            baseScore = Math.Max(0, Math.Min(1, baseScore));
            return Task.FromResult(baseScore);
        }
    }
}
