using System;
using System.Globalization;
using System.IO;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class RoutingService
    {
        public static string BuildTargetPath(AppConfig cfg, Item it)
        {
            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir) ? "." : cfg.App.RootDir;

            string sub = (cfg.Classification.ClassificationMode ?? "category").ToLowerInvariant() switch
            {
                "date" => DatePath(it.CreatedTs, cfg.Classification.TimeGranularity),
                "project" => CombineSafe(cfg.App.ProjectName, DatePath(it.CreatedTs, cfg.Classification.TimeGranularity)),
                _ => CombineSafe(it.Category, DatePath(it.CreatedTs, cfg.Classification.TimeGranularity)), // category
            };

            return Path.GetFullPath(Path.Combine(root, sub));
        }

        private static string DatePath(long unix, string? granularity)
        {
            var ts = unix > 0 ? unix : DateTimeOffset.Now.ToUnixTimeSeconds();
            var dt = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime().DateTime;

            return (granularity ?? "month").ToLowerInvariant() switch
            {
                "year" => dt.ToString("yyyy", CultureInfo.InvariantCulture),
                "day" => Path.Combine(dt.ToString("yyyy"), dt.ToString("MM"), dt.ToString("dd")),
                _ => Path.Combine(dt.ToString("yyyy"), dt.ToString("MM")), // month
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
