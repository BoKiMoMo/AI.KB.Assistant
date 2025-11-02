using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>路徑計算 +（可選）實際搬檔。</summary>
    public class RoutingService
    {
        private AppConfig _cfg = new();

        public RoutingService(AppConfig cfg) => ApplyConfig(cfg);
        public void ApplyConfig(AppConfig cfg) => _cfg = cfg ?? new AppConfig();

        /// <summary>只計算目的地（不做 IO）。</summary>
        public string PreviewDestPath(string srcFullPath, string? lockedProject = null, string? category = null, DateTime? ts = null)
        {
            if (string.IsNullOrWhiteSpace(srcFullPath)) return srcFullPath;
            var fileName = Path.GetFileName(srcFullPath);

            // V7.5 Bug 修正：
            // 1. 設定頁面 (SettingsWindow) 儲存的是 App.RootDir。
            // 2. 邏輯應改為：優先使用 App.RootDir，若其為空，才回退(fallback)到 Routing.RootDir。
            var root = (_cfg.App?.RootDir ?? _cfg.Routing?.RootDir ?? "").Trim();
            if (string.IsNullOrWhiteSpace(root)) return srcFullPath;

            var item = new Item
            {
                Path = srcFullPath,
                Project = lockedProject ?? GuessProjectFromPath(srcFullPath),
                Category = category,
                Timestamp = ts
            };

            var rel = BuildRelativePath(item, _cfg);
            return Path.Combine(root, rel, fileName);
        }

        /// <summary>建構目的地（舊介面相容）。</summary>
        public string BuildDestination(string rootDir, Item item, string? lockedProject = null, bool isBlacklist = false)
        {
            if (string.IsNullOrWhiteSpace(rootDir) || string.IsNullOrWhiteSpace(item.Path))
                return item.Path;

            if (isBlacklist)
            {
                var auto = _cfg.Routing?.AutoFolderName ?? "自整理";
                return Path.Combine(rootDir, San(auto), Path.GetFileName(item.Path));
            }

            var rel = BuildRelativePath(new Item
            {
                Path = item.Path,
                Project = lockedProject ?? item.Project,
                Category = item.Category,
                Timestamp = item.Timestamp
            }, _cfg);

            return Path.Combine(rootDir, rel, Path.GetFileName(item.Path));
        }

        // 舊呼叫相容
        public string BuildDestination(AppConfig cfg, Item item, string? lockedProject = null, bool isBlacklist = false)
            => BuildDestination(cfg.App?.RootDir ?? "", item, lockedProject, isBlacklist);

        /// <summary>
        /// 依 Import.MoveMode 決定 copy/move，並依 OverwritePolicy 處理重名。
        /// 成功回傳最終目的路徑；失敗回傳 null。
        /// </summary>
        public string? Commit(Item item, string? lockedProject = null, bool isBlacklist = false)
        {
            try
            {
                // V7.5 Bug 修正：(同 PreviewDestPath)
                var root = (_cfg.App?.RootDir ?? _cfg.Routing?.RootDir ?? "").Trim();
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

        // ========== NEW: 組相對路徑（依設定層級順序） ==========
        public static string BuildRelativePath(Item item, AppConfig cfg)
        {
            var r = cfg.Routing ?? new RoutingSection();

            DateTime ResolveTime()
            {
                if (item.Timestamp.HasValue) return item.Timestamp.Value;
                try
                {
                    if (!string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
                        return File.GetCreationTime(item.Path);
                }
                catch { }
                return DateTime.Now;
            }

            var order = (r.FolderOrder == null || r.FolderOrder.Count == 0)
                ? DefaultOrder(r.UseCategory)
                : new List<string>(r.FolderOrder);

            var segs = new List<string>();
            foreach (var token in order)
            {
                switch ((token ?? "").Trim().ToLowerInvariant())
                {
                    case "year":
                        if (r.UseYear) segs.Add(ResolveTime().Year.ToString("0000"));
                        break;
                    case "month":
                        if (r.UseMonth) segs.Add(ResolveTime().ToString("MM"));
                        break;
                    case "project":
                        if (r.UseProject)
                        {
                            var project = string.IsNullOrWhiteSpace(item.Project) ? "_project" : San(item.Project!);
                            segs.Add(project);
                        }
                        break;
                    case "category":
                        if (r.UseCategory)
                        {
                            var cat = string.IsNullOrWhiteSpace(item.Category)
                                ? (r.LowConfidenceFolderName ?? "_pending")
                                : San(item.Category!);
                            segs.Add(cat);
                        }
                        break;
                }
            }
            return string.Join(Path.DirectorySeparatorChar, segs);
        }

        public static List<string> DefaultOrder(bool useCategory)
        {
            var list = new List<string> { "year", "month", "project" };
            if (useCategory) list.Add("category");
            return list;
        }

        // 類別映射（提供副檔名→類別排序）
        public string MapExtensionToCategory(string ext)
        {
            if (string.IsNullOrWhiteSpace(ext)) return "other";
            ext = ext.Trim().ToLowerInvariant();
            if (!ext.StartsWith(".")) ext = "." + ext;

            return ext switch
            {
                ".pdf" or ".doc" or ".docx" or ".odt" or ".rtf" or ".txt" or ".md" => "document",
                ".xls" or ".xlsx" or ".csv" => "sheet",
                ".ppt" or ".pptx" => "slide",
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tiff" or ".webp" => "image",
                ".mp3" or ".wav" or ".m4a" or ".flac" => "audio",
                ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => "video",
                ".zip" or ".rar" or ".7z" => "archive",
                ".cs" or ".js" or ".ts" or ".json" or ".xml" or ".yml" or ".yaml" or ".py" or ".cpp" or ".h" => "code",
                _ => "other"
            };
        }

        // ===== Helpers =====
        private string ResolveCollision(string destFullPath)
        {
            var policy = _cfg.Import?.OverwritePolicy;
            var dir = Path.GetDirectoryName(destFullPath)!;
            var name = Path.GetFileNameWithoutExtension(destFullPath);
            var ext = Path.GetExtension(destFullPath);

            if (!File.Exists(destFullPath)) return destFullPath;

            switch (policy?.ToString() ?? "Rename")
            {
                case "Overwrite":
                case "Replace": // 相容舊版
                    File.Delete(destFullPath);
                    return destFullPath;
                case "Skip":
                    return destFullPath;
                case "KeepBoth":
                case "Rename":
                default:
                    int i = 1;
                    string candidate;
                    do { candidate = Path.Combine(dir, $"{name} ({i++}){ext}"); }
                    while (File.Exists(candidate));
                    return candidate;
            }
        }

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
