using System;
using System.Globalization;
using System.IO;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>根據設定生出目標資料夾路徑 + 檔名衝突處理</summary>
    public static class RoutingService
    {
        public static string BuildTargetPath(AppConfig cfg, Item it)
        {
            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir) ? "." : cfg.App.RootDir;
            var gran = (cfg.Classification?.TimeGranularity ?? "month").ToLowerInvariant();
            var mode = (cfg.Classification?.ClassificationMode ?? "category").ToLowerInvariant();
            var proj = string.IsNullOrWhiteSpace(cfg.App.ProjectName) ? "DefaultProject" : cfg.App.ProjectName;

            var dt = FromUnix(it.CreatedTs);
            var dateSeg = gran switch
            {
                "day" => Path.Combine(dt.ToString("yyyy", CultureInfo.InvariantCulture),
                                        dt.ToString("MM", CultureInfo.InvariantCulture),
                                        dt.ToString("dd", CultureInfo.InvariantCulture)),
                "year" => dt.ToString("yyyy", CultureInfo.InvariantCulture),
                _ => Path.Combine(dt.ToString("yyyy", CultureInfo.InvariantCulture),
                                        dt.ToString("MM", CultureInfo.InvariantCulture)),
            };

            string sub = mode switch
            {
                "date" => Path.Combine(dateSeg, Safe(it.Category)),
                "project" => Path.Combine(Safe(proj), Safe(it.Category), dateSeg),
                _ => Path.Combine(Safe(it.Category), dateSeg), // category-first
            };

            var destDir = Path.GetFullPath(Path.Combine(root, sub));
            Directory.CreateDirectory(destDir);
            return destDir;
        }

        public static string ResolveCollision(string targetFullPath)
        {
            if (!File.Exists(targetFullPath)) return targetFullPath;

            var dir = Path.GetDirectoryName(targetFullPath)!;
            var name = Path.GetFileNameWithoutExtension(targetFullPath);
            var ext = Path.GetExtension(targetFullPath);
            int i = 2;
            while (true)
            {
                var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(candidate)) return candidate;
                i++;
            }
        }

        private static DateTime FromUnix(long ts)
            => DateTimeOffset.FromUnixTimeSeconds(ts <= 0 ? DateTimeOffset.Now.ToUnixTimeSeconds() : ts)
                             .ToLocalTime().DateTime;

        public static string Safe(string? s)
        {
            s ??= "其他";
            foreach (var ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch, '_');
            return s.Trim();
        }
    }
}
