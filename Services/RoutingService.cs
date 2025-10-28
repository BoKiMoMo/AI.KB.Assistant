using AI.KB.Assistant.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 路徑預覽 / 實際目的地計算（不負責 IO）
    /// </summary>
    public class RoutingService
    {
        public const string AutoFolderName = "自整理";
        public const string LowConfidenceFolderName = "信心不足";

        private AppConfig _cfg;

        public RoutingService(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();
            ApplyConfig(_cfg);
        }

        public void ApplyConfig(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();
            ConfigService.Normalize(_cfg);
        }

        /// <summary>
        /// 預覽目的地（不做 IO），若信心不足或無法判斷，會落到「自整理」或「信心不足」。
        /// </summary>
        public string PreviewDestPath(string srcPath, string? lockedProject, double? confidence = null)
        {
            if (string.IsNullOrWhiteSpace(srcPath)) return srcPath;

            var root = _cfg.App.RootDir!;
            var project = !string.IsNullOrWhiteSpace(lockedProject) ? lockedProject! : GuessProject(srcPath);
            var baseDir = Path.Combine(root, project);

            // 低信心 → 指向「信心不足」
            var th = _cfg.Classification.ConfidenceThreshold;
            if (confidence.HasValue && confidence.Value < th)
                return Path.Combine(root, LowConfidenceFolderName, Path.GetFileName(srcPath));

            // 依副檔名群組 → 子類別資料夾
            var group = ResolveGroupByExt(srcPath);
            if (string.IsNullOrWhiteSpace(group))
                return Path.Combine(root, AutoFolderName, Path.GetFileName(srcPath));

            var destDir = Path.Combine(baseDir, group);
            return Path.Combine(destDir, Path.GetFileName(srcPath));
        }

        /// <summary>
        /// 實際目的地（會處理同名策略 Replace / Rename / Skip，僅回傳結果路徑，不做搬檔）
        /// </summary>
        public string BuildDestination(string srcPath, string? lockedProject, double? confidence = null)
        {
            var dest = PreviewDestPath(srcPath, lockedProject, confidence);
            var policy = _cfg.Import.OverwritePolicy;

            if (!File.Exists(dest)) return dest;

            return policy switch
            {
                OverwritePolicy.Replace => dest,
                OverwritePolicy.Skip => string.Empty, // 呼叫端自行略過
                OverwritePolicy.Rename => MakeNonConflictFile(dest),
                _ => MakeNonConflictFile(dest)
            };
        }

        // -------------------------------------------------
        // Helpers
        // -------------------------------------------------
        private string GuessProject(string srcPath)
        {
            // 以父層資料夾名當 project
            var dir = Path.GetDirectoryName(srcPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                var name = new DirectoryInfo(dir).Name;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            return "Default";
        }

        private string ResolveGroupByExt(string srcPath)
        {
            var ext = Path.GetExtension(srcPath).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) return "Others";

            // 到 ExtGroupsCache 找
            foreach (var kv in _cfg.Import.ExtGroupsCache)
            {
                if (kv.Value != null && kv.Value.Contains(ext))
                    return kv.Key;
            }
            return "Others";
        }

        private static string MakeNonConflictFile(string fullPath)
        {
            var dir = Path.GetDirectoryName(fullPath)!;
            var name = Path.GetFileNameWithoutExtension(fullPath);
            var ext = Path.GetExtension(fullPath);
            var i = 1;
            var tryPath = fullPath;

            while (File.Exists(tryPath))
            {
                tryPath = Path.Combine(dir, $"{name} ({i++}){ext}");
            }
            return tryPath;
        }

        // 左樹過濾：黑名單資料夾或系統隱藏
        public bool ShouldHideFolder(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return true;
            var n = folderName.Trim();
            if (_cfg.Import.BlacklistFolderNames.Any(b => string.Equals(b, n, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (string.Equals(n, AutoFolderName, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(n, LowConfidenceFolderName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
