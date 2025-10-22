using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class RoutingService
    {
        private AppConfig _cfg;

        // 用來將副檔名對應到群組名（Type）
        private Dictionary<string, string> _ext2Group = new(StringComparer.OrdinalIgnoreCase);

        public RoutingService(AppConfig cfg)
        {
            _cfg = cfg;
            RebuildExtMap();
        }

        public void ApplyConfig(AppConfig cfg)
        {
            _cfg = cfg;
            RebuildExtMap();
        }

        private void RebuildExtMap()
        {
            _ext2Group.Clear();
            var groups = _cfg.Routing.ExtensionGroups ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in groups)
            {
                var groupName = kv.Key; // e.g. Images / Documents / Code...
                var exts = kv.Value ?? Array.Empty<string>();
                foreach (var ext in exts)
                {
                    var ex = (ext ?? "").Trim('.').ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(ex))
                        _ext2Group[ex] = groupName;
                }
            }
        }

        public string GetTypeGroupByExt(string ext)
        {
            var ex = (ext ?? "").Trim('.').ToLowerInvariant();
            if (_ext2Group.TryGetValue(ex, out var group))
                return group;
            return "Others";
        }

        /// <summary>
        /// 產生目的地完整路徑（不含同名處理），支援黑名單與低信心固定落在 ROOT。
        /// </summary>
        public string BuildDestination(string fileName, string project, string category, string ext, DateTime ts,
                                       bool isBlacklist, bool isLowConfidence)
        {
            var root = _cfg.App.RootDir;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                root = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            var pureExt = (ext ?? "").Trim('.').ToLowerInvariant();
            var typeGroup = GetTypeGroupByExt(pureExt);

            // ✅ 黑名單：ROOT/_blacklist/<file>
            if (isBlacklist)
            {
                var blackRoot = Path.Combine(root, "_blacklist");
                Directory.CreateDirectory(blackRoot);
                return Path.Combine(blackRoot, fileName);
            }

            // ✅ 低信心：ROOT/自整理/<file>
            if (isLowConfidence)
            {
                var autoRoot = Path.Combine(root, _cfg.Routing.AutoFolderName ?? "自整理");
                Directory.CreateDirectory(autoRoot);
                return Path.Combine(autoRoot, fileName);
            }

            // ⬇ 一般模板路徑（依勾選片段）
            var parts = new List<string> { root };

            if (_cfg.Routing.UseYear)
                parts.Add(ts.Year.ToString("0000"));
            if (_cfg.Routing.UseMonth)
                parts.Add(ts.Month.ToString("00"));

            if (_cfg.Routing.UseProject && !string.IsNullOrWhiteSpace(project))
                parts.Add(Sanitize(project));

            if (_cfg.Routing.UseType && !string.IsNullOrWhiteSpace(typeGroup))
                parts.Add(Sanitize(typeGroup));

            if (!string.IsNullOrWhiteSpace(category))
                parts.Add(Sanitize(category));

            var dir = Path.Combine(parts.ToArray());
            Directory.CreateDirectory(dir);

            return Path.Combine(dir, fileName);
        }

        /// <summary>
        /// 給 Intake/外部使用的便利介面：由 Item 直接產生目的地。
        /// </summary>
        public string BuildDestination(Item item, bool isBlacklist, bool isLowConfidence)
        {
            var name = item.Filename ?? "noname";
            var ext = item.Ext ?? Path.GetExtension(name).Trim('.');
            var ts = FromUnix(item.CreatedTs);
            return BuildDestination(name, item.Project ?? "", item.Category ?? "", ext, ts, isBlacklist, isLowConfidence);
        }

        private static DateTime FromUnix(long sec)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(sec <= 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : sec)
                                      .LocalDateTime;
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private static string Sanitize(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(s.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return safe.Trim();
        }
    }
}
