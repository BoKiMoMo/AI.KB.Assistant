// Services/RoutingService.cs
using System;
using System.Globalization;
using System.IO;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public static class RoutingService
    {
        public static string BuildTargetDirectory(AppConfig cfg, Item it)
        {
            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir) ? "." : cfg.App.RootDir;

            string datePart = DatePath(it.CreatedTs, cfg.Routing.TimeGranularity);
            string sub = cfg.Routing.ClassificationMode?.ToLowerInvariant() switch
            {
                "date" => datePart,
                "project" => Path.Combine(Safe(cfg.App.ProjectName), datePart),
                _ => Path.Combine(Safe(it.Category), datePart), // category
            };

            var full = Path.GetFullPath(Path.Combine(root, sub));
            return full;
        }

        public static string BuildAutoFolder(AppConfig cfg)
        {
            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir) ? "." : cfg.App.RootDir;
            var auto = string.IsNullOrWhiteSpace(cfg.Classification.AutoFolderName) ? "¦Û¾ã²z" : cfg.Classification.AutoFolderName;
            return Path.Combine(root, Safe(auto));
        }

        private static string DatePath(long unix, string granularity)
        {
            if (unix <= 0) unix = DateTimeOffset.Now.ToUnixTimeSeconds();
            var dt = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().DateTime;

            return (granularity?.ToLowerInvariant()) switch
            {
                "day" => Path.Combine(dt.ToString("yyyy", CultureInfo.InvariantCulture),
                                       dt.ToString("MM", CultureInfo.InvariantCulture),
                                       dt.ToString("dd", CultureInfo.InvariantCulture)),
                "year" => dt.ToString("yyyy", CultureInfo.InvariantCulture),
                _ => Path.Combine(dt.ToString("yyyy", CultureInfo.InvariantCulture),
                                       dt.ToString("MM", CultureInfo.InvariantCulture)),
            };
        }

        public static string Safe(string? name)
        {
            name ??= "";
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            return name.Trim();
        }

        public static string ResolveCollision(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path)!;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                i++;
            } while (File.Exists(candidate));
            return candidate;
        }
    }
}
