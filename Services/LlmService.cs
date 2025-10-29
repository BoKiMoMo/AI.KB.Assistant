using System;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public class LlmService : IDisposable
    {
        private AppConfig _cfg = new();

        public LlmService(AppConfig cfg) { _cfg = cfg ?? new AppConfig(); }
        public void UpdateConfig(AppConfig cfg) => _cfg = cfg ?? _cfg;

        public Task<string> SummarizeAsync(string text, CancellationToken ct = default)
            => Task.FromResult($"概要：{(text ?? string.Empty).Substring(0, Math.Min(80, text?.Length ?? 0))}…");

        public Task<string[]> SuggestTagsAsync(string text, double threshold = 0.75, CancellationToken ct = default)
            => Task.FromResult(new[] { "文件", "一般", "待確認" });

        public Task<double> AnalyzeConfidenceAsync(string text, CancellationToken ct = default)
            => Task.FromResult(0.82);

        public void Dispose() { /* no-op */ }
    }
}
