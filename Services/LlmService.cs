using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class LlmService : IDisposable
    {
        private AppConfig _cfg;

        public LlmService(AppConfig cfg) => _cfg = cfg ?? new AppConfig();
        public void UpdateConfig(AppConfig cfg) { if (cfg != null) _cfg = cfg; }

        public static string GuessProjectFromName(string? filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return string.Empty;
            var seps = new[] { '_', '-', ' ' };
            return (filename.Trim().Split(seps, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "").Trim();
        }

        public async Task<string[]> SuggestProjectNamesAsync(IEnumerable<string?> filenames, CancellationToken ct)
        {
            if (!(_cfg?.OpenAI?.EnableWhenLowConfidence ?? false)) return Array.Empty<string>();

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fn in filenames ?? Array.Empty<string?>())
            {
                ct.ThrowIfCancellationRequested();
                var p = GuessProjectFromName(fn);
                if (!string.IsNullOrWhiteSpace(p)) set.Add(p);
            }
            if (set.Count == 0) set.Add("General");
            await Task.Yield();
            return set.ToArray();
        }

        // 🔧 供 IntakeService 呼叫的暫存實作
        public Task<string> RefineAsync(string prompt, CancellationToken ct) => Task.FromResult(prompt);

        internal static IEnumerable<string> SplitTokens(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) yield break;
            foreach (var t in s.Split(new[] { ' ', '_', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var v = t.Trim();
                if (!string.IsNullOrWhiteSpace(v)) yield return v;
            }
        }

        public void Dispose() { }
    }
}
