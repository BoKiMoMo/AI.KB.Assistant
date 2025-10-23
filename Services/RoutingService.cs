using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class RoutingService
    {
        private AppConfig _cfg;

        public RoutingService(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();
        }

        public void ApplyConfig(AppConfig cfg)
        {
            _cfg = cfg ?? _cfg;
        }

        /// <summary>
        /// 預覽分類後的「完整檔案目標路徑」（不進行 IO 與搬檔；純字串組合）。
        /// </summary>
        /// <param name="srcPath">來源完整檔案路徑</param>
        /// <param name="lockedProject">若 UI 有鎖定專案，傳入該名稱；否則可傳 null/空字串</param>
        /// <returns>預計目的地完整檔案路徑（含檔名）; 若設定不完整則回傳空字串</returns>
        public string PreviewDestPath(string srcPath, string? lockedProject)
        {
            if (string.IsNullOrWhiteSpace(srcPath) || string.IsNullOrWhiteSpace(_cfg?.App?.RootDir))
                return string.Empty;

            var root = _cfg.App.RootDir!.Trim();
            if (!Path.IsPathRooted(root))
                root = Path.GetFullPath(root);

            var fi = SafeGetFileInfo(srcPath);
            var fileName = fi?.Name ?? Path.GetFileName(srcPath);
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            // 1) 年 / 月
            var dt = fi?.Exists == true ? fi.CreationTime : DateTime.Now;
            var year = dt.ToString("yyyy");
            var month = dt.ToString("MM");

            // 2) 類型（由副檔名對 ExtensionGroups）
            var ext = Path.GetExtension(fileName)?.Trim('.').ToLowerInvariant() ?? "";
            var type = MapTypeByExtension(ext);

            // 3) 專案（lockedProject > yyyyMM）
            var project = !string.IsNullOrWhiteSpace(lockedProject)
                ? lockedProject!.Trim()
                : $"{year}{month}";

            // 4) 組合資料夾層級（依設定開關）
            var parts = new List<string>();
            if (_cfg.Routing?.UseYear == true) parts.Add(year);
            if (_cfg.Routing?.UseMonth == true) parts.Add(month);
            if (_cfg.Routing?.UseProject == true) parts.Add(project);
            if (_cfg.Routing?.UseType == true) parts.Add(type);

            // 5) 合法化每個段落
            parts = parts.Select(NormalizeFolderSegment).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            // 6) 拼出完整路徑
            var destDir = Path.Combine(new[] { root }.Concat(parts).ToArray());
            var destFull = Path.Combine(destDir, fileName);
            return destFull;
        }

        // ====== 工具 ======

        /// <summary>
        /// 由副檔名（不含點）對應到 ExtensionGroups 的 key；找不到則回傳 "Others"。
        /// </summary>
        private string MapTypeByExtension(string ext)
        {
            var groups = _cfg?.Routing?.ExtensionGroups;
            if (groups == null || groups.Count == 0)
                return "Others";

            foreach (var kv in groups)
            {
                var key = kv.Key ?? "";
                var list = kv.Value ?? Array.Empty<string>();
                if (list.Any(e => string.Equals(e?.Trim().TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase)))
                    return string.IsNullOrWhiteSpace(key) ? "Others" : key;
            }
            return "Others";
        }

        private static FileInfo? SafeGetFileInfo(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                return fi.Exists ? fi : null;
            }
            catch { return null; }
        }

        private static readonly Regex _illegal = new Regex(@"[\\/:*?""<>|]+", RegexOptions.Compiled);

        private static string NormalizeFolderSegment(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var t = raw.Trim();
            t = _illegal.Replace(t, "_");           // Windows 非法字元轉底線
            t = t.Trim('.', ' ');                    // 避免尾端是點或空白
            if (string.IsNullOrWhiteSpace(t)) t = "_";
            return t;
        }
    }
}
