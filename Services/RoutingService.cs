using System;
using System.IO;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Helpers;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 決定檔案要搬到哪個資料夾
    /// </summary>
    public static class RoutingService
    {
        public static string GetTargetPath(AppConfig cfg, Item item)
        {
            var root = cfg.RootDir;
            if (string.IsNullOrEmpty(root)) return item.Path;

            string datePart = "";
            if (cfg.TimeGranularity == "year")
                datePart = DateTimeOffset.FromUnixTimeSeconds(item.CreatedTs).Year.ToString();
            else if (cfg.TimeGranularity == "month")
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(item.CreatedTs);
                datePart = $"{dt:yyyy-MM}";
            }
            else if (cfg.TimeGranularity == "day")
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(item.CreatedTs);
                datePart = $"{dt:yyyy-MM-dd}";
            }

            var proj = string.IsNullOrEmpty(item.Project) ? "General" : item.Project;
            var category = string.IsNullOrEmpty(item.Category) ? cfg.AutoFolderName : item.Category;
            var fileType = string.IsNullOrEmpty(item.FileType) ? "Other" : item.FileType;

            var safeName = SanitizeFileName(item.Filename);

            var dir = Path.Combine(root, datePart, proj, category, fileType);
            Directory.CreateDirectory(dir);

            return Path.Combine(dir, safeName);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
