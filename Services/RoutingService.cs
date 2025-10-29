using System.IO;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 純計算路徑（不做 IO）
    /// </summary>
    public class RoutingService
    {
        private AppConfig _cfg = new();

        public RoutingService(AppConfig cfg) => ApplyConfig(cfg);
        public void ApplyConfig(AppConfig cfg) => _cfg = cfg ?? new AppConfig();

        public string PreviewDestPath(string srcFullPath, string? lockedProject = null)
        {
            if (string.IsNullOrWhiteSpace(srcFullPath)) return srcFullPath;

            var fileName = Path.GetFileName(srcFullPath);
            var ext = Path.GetExtension(srcFullPath).TrimStart('.').ToLowerInvariant();
            var root = _cfg.App?.RootDir ?? "";
            if (string.IsNullOrWhiteSpace(root)) return srcFullPath;

            var project = lockedProject ?? GuessProjectFromPath(srcFullPath);
            var typeFolder = MapExtToGroup(ext);
            return Path.Combine(root, San(project), San(typeFolder), fileName);
        }

        public string BuildDestination(string rootDir, Item item, string? lockedProject = null, bool isBlacklist = false)
        {
            if (string.IsNullOrWhiteSpace(rootDir) || string.IsNullOrWhiteSpace(item.SourcePath))
                return item.SourcePath;

            if (isBlacklist)
            {
                var auto = _cfg.Routing?.AutoFolderName ?? "自整理";
                return Path.Combine(rootDir, San(auto), Path.GetFileName(item.SourcePath));
            }

            return PreviewDestPath(item.SourcePath, lockedProject);
        }

        // 舊呼叫相容
        public string BuildDestination(AppConfig cfg, Item item, string? lockedProject = null, bool isBlacklist = false)
            => BuildDestination(cfg.App?.RootDir ?? "", item, lockedProject, isBlacklist);

        private static string MapExtToGroup(string ext)
            => string.IsNullOrWhiteSpace(ext) ? "other" : ext;

        private static string GuessProjectFromPath(string path)
        {
            var dir = Path.GetDirectoryName(path) ?? "";
            return string.IsNullOrWhiteSpace(dir) ? "未分類" : new DirectoryInfo(dir).Name;
        }

        private static string San(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
