using System;
using System.Globalization;
using System.IO;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public static class RoutingService
    {
        public static string BuildTargetPath(AppConfig cfg, Item it)
        {
            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir) ? "." : cfg.App.RootDir;

            // 低信心 → _自整理
            if (it.Confidence < Math.Max(0, Math.Min(1, cfg.Classification.ConfidenceThreshold)))
            {
                var special = Path.Combine(root, "_自整理");
                Directory.CreateDirectory(special);
                return special;
            }

            string sub = (cfg.Classification.ClassificationMode ?? "category").ToLowerInvariant() switch
            {
                "date" => DatePath(it.CreatedTs, cfg.Routing.TimeGranularity),
                "project" => CombineSafe(cfg.App.ProjectName, DatePath(it.CreatedTs, cfg.Routing.TimeGranularity)),
                _ => CombineSafe(it.Category, DatePath(it.CreatedTs, cfg.Routing.TimeGranularity)),
            };

            var full = Path.GetFullPath(Path.Combine(root, sub));
            Directory.CreateDirectory(full);
            return full;
        }

        private static string DatePath(long unix, string? granularity)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(
                        unix > 0 ? unix : DateTimeOffset.Now.ToUnixTimeSeconds())
                    .ToLocalTime().DateTime;

            return (granularity ?? "month").ToLowerInvariant() switch
            {
                "year" => dt.ToString("yyyy", CultureInfo.InvariantCulture),
                "day" => Path.Combine(dt.ToString("yyyy"), dt.ToString("MM"), dt.ToString("dd")),
                _ => Path.Combine(dt.ToString("yyyy"), dt.ToString("MM")),
            };
        }

        private static string CombineSafe(params string[] parts)
        {
            string San(string? s)
            {
                s ??= "";
                foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
                return s.Trim();
            }
            return Path.Combine(Array.ConvertAll(parts, San));
        }
    }
}
