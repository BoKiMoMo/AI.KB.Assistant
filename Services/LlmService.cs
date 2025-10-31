using System;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// LLM 模組（目前為安全 No-op/模擬）。
    /// TODO: 後續串接 OpenAI / 本地模型時，新增 Provider 並由 Config 切換。
    /// </summary>
    public class LlmService : IDisposable
    {
        private AppConfig _cfg = new();

        public LlmService(AppConfig cfg) { _cfg = cfg ?? new AppConfig(); }
        public void UpdateConfig(AppConfig cfg) => _cfg = cfg ?? _cfg;

        public Task<string> SummarizeAsync(string text, CancellationToken ct = default)
        {
            text ??= string.Empty;
            var s = text.Trim();
            if (s.Length > 120) s = s.Substring(0, 120) + "…";
            return Task.FromResult($"概要：{s}");
        }

        public Task<string[]> SuggestTagsAsync(string text, double threshold = 0.75, CancellationToken ct = default)
            => Task.FromResult(new[] { "文件", "一般", "待確認" });

        public Task<double> AnalyzeConfidenceAsync(string text, CancellationToken ct = default)
            => Task.FromResult(0.82);

        public void Dispose() { /* no-op */ }
    }
}
