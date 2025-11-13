using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V17.0 (V16.1 修正版)
    /// 1. (V16.1) 'MapExtensionToCategoryConfig' [Line 166] 動態讀取 'ExtensionGroups' [cite: `ConfigService.cs` Line 133]。
    /// 2. [V17.0 修正 BUG #3.2] 'BuildRelativePath' [Line 94] 中，'item.Project' (若為 null) 
    ///    的備援 (fallback) 邏輯從 V16.1 [cite: `Services/RoutingService.cs (V16.1)` Line 110] 的 'GuessProjectFromPath' [cite: `Services/RoutingService.cs (V16.1)` Line 201] 
    ///    改為 "Month" [Line 121]。
    /// </summary>
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

            // (V13.0)
            var root = (_cfg.App?.RootDir ?? _cfg.Routing?.RootDir ?? "").Trim();
            if (string.IsNullOrWhiteSpace(root)) return srcFullPath;

            var item = new Item
            {
                Path = srcFullPath,
                Project = lockedProject, // [V17.0] 允許 lockedProject (null) 傳入 BuildRelativePath
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
                // (V13.0)
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
        public string BuildRelativePath(Item item, AppConfig cfg)
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

            // (V16.1)
            if (string.IsNullOrWhiteSpace(item.Category))
            {
                var ext = Path.GetExtension(item.Path);
                item.Category = MapExtensionToCategoryConfig(ext, cfg);
            }

            // [V17.0 修正 BUG #3.2] (V15.2 BUG #5)
            // 如果 item.Project 為 null (例如 V10.2 [cite: `Services/LlmService.cs (V10.2)`] 'SuggestProjectAsync' [cite: `Services/LlmService.cs (V10.2)` Line 133] (本地規則) 回傳 string.Empty)，
            // 則使用 "Month" (月份) 作為 V16.1 [cite: `Services/RoutingService.cs (V16.1)`] 的預設值。
            if (string.IsNullOrWhiteSpace(item.Project))
            {
                item.Project = ResolveTime().ToString("MM");
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
                            // [V17.0 修正] 'item.Project' [Line 121] 現在已被填入 (例如 "05")
                            var project = string.IsNullOrWhiteSpace(item.Project) ? "_project" : San(item.Project!);
                            segs.Add(project);
                        }
                        break;
                    case "category":
                        if (r.UseCategory)
                        {
                            // (V16.1) 'item.Category' [Line 110] 現在已被填入
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

        /// <summary>
        /// [V16.1 修正 BUG #4]
        /// 類別映射 (動態讀取 config.json [cite: `ConfigService.cs` Line 133])
        /// </summary>
        public string MapExtensionToCategoryConfig(string ext, AppConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(ext)) return "other";
            ext = ext.Trim().ToLowerInvariant();
            if (ext.StartsWith(".")) ext = ext[1..]; // 移除 '.'

            if (cfg?.Routing?.ExtensionGroups == null) return "other";

            // 遍歷 config.json [cite: `ConfigService.cs` Line 133] 中的字典
            foreach (var group in cfg.Routing.ExtensionGroups)
            {
                var categoryName = group.Key;
                var extensionsInGroup = group.Value;

                if (extensionsInGroup != null && extensionsInGroup.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    return categoryName;
                }
            }

            if (cfg.Routing.ExtensionGroups.ContainsKey("Others"))
            {
                return "Others";
            }

            return "other"; // 最終備援
        }


        // ===== Helpers =====
        private string ResolveCollision(string destFullPath)
        {
            // (V13.0)
            var policy = _cfg.Import?.OverwritePolicy;
            var dir = Path.GetDirectoryName(destFullPath)!;
            var name = Path.GetFileNameWithoutExtension(destFullPath);
            var ext = Path.GetExtension(destFullPath);

            if (!File.Exists(destFullPath)) return destFullPath;

            // (V13.0) OverwritePolicy 是 string
            switch (policy ?? "Rename")
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

        /// <summary>
        /// (V10.2) 設為 public static
        /// </summary>
        public static string GuessProjectFromPath(string path)
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