using System;
using System.Globalization;
using System.IO;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public static class RoutingService
    {
        // 最終目錄：Root / yyyy / <專案名稱> / <業務語意> / <檔案型態>
        public static string BuildTargetDirectory(AppConfig cfg, Item it)
        {
            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir) ? "." : cfg.App.RootDir;
            var year = (it.Year > 0 ? it.Year : DateTime.Now.Year).ToString(CultureInfo.InvariantCulture);
            var proj = Safe(string.IsNullOrWhiteSpace(it.Project) ? cfg.App.ProjectName : it.Project);
            var cat = Safe(string.IsNullOrWhiteSpace(it.Category) ? cfg.Classification.FallbackCategory : it.Category);
            var type = Safe(string.IsNullOrWhiteSpace(it.FileType) ? (Path.GetExtension(it.Filename) ?? "").TrimStart('.').ToLowerInvariant() : it.FileType);

            var sub = Path.Combine(year, proj, cat, type);
            return Path.GetFullPath(Path.Combine(root, sub));
        }

        public static string BuildAutoFolder(AppConfig cfg)
        {
            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir) ? "." : cfg.App.RootDir;
            var auto = string.IsNullOrWhiteSpace(cfg.Classification.AutoFolderName) ? "自整理" : cfg.Classification.AutoFolderName;
            return Path.Combine(root, Safe(auto));
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
