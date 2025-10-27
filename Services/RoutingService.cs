using AI.KB.Assistant.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 路由規則服務：負責預覽/產生最終目的地路徑
    /// </summary>
    public class RoutingService : IDisposable
    {
        private AppConfig _cfg;

        public RoutingService(AppConfig cfg)
        {
            _cfg = cfg;
            _cfg.Import?.RebuildExtGroupsCache();
        }

        public void ApplyConfig(AppConfig cfg)
        {
            _cfg = cfg;
            _cfg.Import?.RebuildExtGroupsCache();
        }

        public void Dispose() { }

        /// <summary>
        /// 依目前設定預覽目標路徑（含檔名），不執行任何 IO。lockedProject 優先於 item.Project。
        /// </summary>
        public string PreviewDestPath(string srcFullPath, string? lockedProject)
        {
            if (string.IsNullOrWhiteSpace(srcFullPath))
                return string.Empty;

            var fi = new FileInfo(srcFullPath);
            var item = new Models.Item
            {
                Filename = fi.Name,
                Ext = (fi.Extension ?? "").Trim('.').ToLowerInvariant(),
                Project = string.Empty,
                Path = fi.FullName,
                CreatedTs = fi.Exists
                    ? new DateTimeOffset(fi.CreationTimeUtc).ToUnixTimeSeconds()
                    : DateTimeOffset.Now.ToUnixTimeSeconds()
            };

            // 依門檻判斷是否為低信心（資料若不存在 DB，預設用 0）
            var lowConf = item.Confidence < (_cfg?.Classification?.ConfidenceThreshold ?? 0.75);

            return BuildDestination(item,
                                    isBlacklist: false,
                                    isLowConfidence: lowConf,
                                    lockedProject: lockedProject);
        }

        /// <summary>
        /// 產生最終目的地（含檔名）。此方法供 Commit 時實際路由使用。
        /// </summary>
        public string BuildDestination(Models.Item item, bool isBlacklist, bool isLowConfidence, string? lockedProject)
            => BuildDestination(item, isBlacklist, isLowConfidence, lockedProject, DateTimeOffset.FromUnixTimeSeconds(item.CreatedTs).UtcDateTime);

        private string BuildDestination(Models.Item item, bool isBlacklist, bool isLowConfidence, string? lockedProject, DateTime utcCreated)
        {
            var root = _cfg.App?.RootDir ?? "";
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;

            // 目的地的第一層：黑名單 / 低信心 / 一般自整理
            var level1 = isBlacklist
                ? "_blacklist"
                : isLowConfidence
                    ? (_cfg.Routing?.LowConfidenceFolderName ?? "信心不足")
                    : (_cfg.Routing?.AutoFolderName ?? "自整理");

            var segments = new List<string> { root, level1 };

            // 片段：年、月、專案、類型
            if (_cfg.Routing?.UseYear == true)
            {
                var y = (utcCreated == default ? DateTime.UtcNow : utcCreated).ToLocalTime().ToString("yyyy");
                segments.Add(y);
            }

            if (_cfg.Routing?.UseMonth == true)
            {
                var m = (utcCreated == default ? DateTime.UtcNow : utcCreated).ToLocalTime().ToString("MM");
                segments.Add(m);
            }

            if (_cfg.Routing?.UseProject == true)
            {
                var proj = FirstNonEmpty(lockedProject, _cfg.App?.ProjectLock, item.Project);
                if (!string.IsNullOrWhiteSpace(proj)) segments.Add(SanitizeFolder(proj!));
            }

            if (_cfg.Routing?.UseType == true)
            {
                var type = ResolveTypeByExt(item.Ext);
                if (!string.IsNullOrWhiteSpace(type)) segments.Add(SanitizeFolder(type!));
            }

            // 組最終資料夾
            var destDir = Path.Combine(segments.ToArray());

            // 最終檔名：直接使用原始檔名
            var filename = item.Filename ?? "unknown";

            return Path.Combine(destDir, filename);
        }

        private string ResolveTypeByExt(string? ext)
        {
            if (string.IsNullOrWhiteSpace(ext)) return string.Empty;
            var map = _cfg.Import?.ExtGroupMap ?? new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in map)
            {
                if (kv.Value.Contains(ext.Trim('.')))
                    return kv.Key;
            }
            return "其他";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v!;
            return string.Empty;
        }

        private static string SanitizeFolder(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var arr = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            var cleaned = new string(arr).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Unnamed" : cleaned;
        }
    }
}
