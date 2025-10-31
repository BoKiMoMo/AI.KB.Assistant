using System;
using System.IO;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 路徑計算 +（可選）實際搬檔。
    /// 你原本的 Preview 方法完全保留；我只新增 Move/Copy 的安全實作。
    /// </summary>
    public class RoutingService
    {
        private AppConfig _cfg = new();

        public RoutingService(AppConfig cfg) => ApplyConfig(cfg);
        public void ApplyConfig(AppConfig cfg) => _cfg = cfg ?? new AppConfig();

        /// <summary>只計算目的地（不做 IO）。</summary>
        public string PreviewDestPath(string srcFullPath, string? lockedProject = null)
        {
            if (string.IsNullOrWhiteSpace(srcFullPath)) return srcFullPath;

            var fileName = Path.GetFileName(srcFullPath);
            var ext = Path.GetExtension(srcFullPath).TrimStart('.').ToLowerInvariant();
            var root = _cfg.Routing?.RootDir ?? _cfg.App?.RootDir ?? "";
            if (string.IsNullOrWhiteSpace(root)) return srcFullPath;

            var project = lockedProject ?? GuessProjectFromPath(srcFullPath);
            var typeFolder = MapExtToGroup(ext);
            return Path.Combine(root, San(project), San(typeFolder), fileName);
        }

        /// <summary>建構目的地（提供舊介面相容）。</summary>
        public string BuildDestination(string rootDir, Item item, string? lockedProject = null, bool isBlacklist = false)
        {
            if (string.IsNullOrWhiteSpace(rootDir) || string.IsNullOrWhiteSpace(item.Path))
                return item.Path;

            if (isBlacklist)
            {
                var auto = _cfg.Routing?.AutoFolderName ?? "自整理";
                return Path.Combine(rootDir, San(auto), Path.GetFileName(item.Path));
            }

            return PreviewDestPath(item.Path, lockedProject);
        }

        // 舊呼叫相容
        public string BuildDestination(AppConfig cfg, Item item, string? lockedProject = null, bool isBlacklist = false)
            => BuildDestination(cfg.App?.RootDir ?? "", item, lockedProject, isBlacklist);

        /// <summary>
        /// 依 Import.MoveMode 決定「copy 或 move」，並依 OverwritePolicy 處理重名。
        /// 成功回傳最終目的路徑；失敗回傳 null。
        /// </summary>
        public string? Commit(Item item, string? lockedProject = null, bool isBlacklist = false)
        {
            try
            {
                var root = _cfg.Routing?.RootDir ?? _cfg.App?.RootDir ?? "";
                if (string.IsNullOrWhiteSpace(root) || !File.Exists(item.Path)) return null;

                var dest = BuildDestination(root, item, lockedProject, isBlacklist);
                var destDir = Path.GetDirectoryName(dest)!;
                Directory.CreateDirectory(destDir);

                var final = ResolveCollision(dest);
                var mode = (_cfg.Import?.MoveMode ?? "copy").ToLowerInvariant();

                if (mode == "move")
                {
                    if (!string.Equals(item.Path, final, StringComparison.OrdinalIgnoreCase))
                        File.Copy(item.Path, final, overwrite: false);
                    File.Delete(item.Path);
                }
                else
                {
                    // copy
                    File.Copy(item.Path, final, overwrite: false);
                }

                item.ProposedPath = final;
                item.Status = "committed";
                return final;
            }
            catch
            {
                // TODO: log
                return null;
            }
        }

        // ========== Helpers ==========

        private string ResolveCollision(string destFullPath)
        {
            var policy = _cfg.Import?.OverwritePolicy; // enum OverwritePolicy
            var dir = Path.GetDirectoryName(destFullPath)!;
            var name = Path.GetFileNameWithoutExtension(destFullPath);
            var ext = Path.GetExtension(destFullPath);

            if (!File.Exists(destFullPath)) return destFullPath;

            switch (policy?.ToString() ?? "Rename")
            {
                case "Overwrite":
                    // 小心：為了保守，這裡改成覆蓋寫入前先刪除
                    File.Delete(destFullPath);
                    return destFullPath;

                case "Skip":
                    // 保留原檔，不拷貝，回傳現有路徑
                    return destFullPath;

                case "KeepBoth":
                case "Rename":
                default:
                    int i = 1;
                    string candidate;
                    do
                    {
                        candidate = Path.Combine(dir, $"{name} ({i++}){ext}");
                    } while (File.Exists(candidate));
                    return candidate;
            }
        }

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
