using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 根據 AppConfig 的 Routing 設定，產生層級式目的路徑。
    /// 預設建議結構：Year / Project / Category(語意) / Type(副檔名)
    /// </summary>
    public sealed class RoutingService
    {
        private static string SafeFolder(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "_";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>
        /// 產生目的路徑（不含檔名）。會依照 RoutingSection 的開關決定層級。
        /// </summary>
        public string BuildDestination(AppConfig cfg, string project, string category, string fileType, DateTime dt)
        {
            var parts = new List<string>();

            // Year
            if (cfg.Routing.EnableYear)
                parts.Add(dt.Year.ToString());

            // Project
            if (cfg.Routing.EnableProject)
                parts.Add(SafeFolder(string.IsNullOrWhiteSpace(project) ? "一般專案" : project));

            // Category (語意/業務分類)
            if (cfg.Routing.EnableCategory)
                parts.Add(SafeFolder(string.IsNullOrWhiteSpace(category) ? "未分類" : category));

            // Type (副檔名族群)
            if (cfg.Routing.EnableType)
                parts.Add(SafeFolder(NormalizeType(fileType)));

            // Month 放最後（可選），通常 Year 已足夠
            if (cfg.Routing.EnableMonth)
                parts.Add(dt.Month.ToString("D2"));

            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : cfg.App.RootDir;

            var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
            return path;
        }

        /// <summary>
        /// 將副檔名歸類為族群，如圖片、影音、文件、設計、程式碼…等。
        /// </summary>
        public static string NormalizeType(string extOrType)
        {
            var e = (extOrType ?? "").Trim().Trim('.').ToLowerInvariant();

            // 若已經是族群名，直接回傳
            var knownGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "圖片", "文件", "表單", "投影片", "影音", "設計", "向量", "專案檔", "程式碼", "壓縮", "純文字", "PDF", "資料" };

            if (knownGroups.Contains(e)) return e;

            // 真正副檔名
            var ext = e;

            // 圖片
            var img = new[] { "png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff", "webp", "heic", "svg" };
            if (img.Contains(ext)) return "圖片";

            // 影音
            var av = new[] { "mp4", "m4v", "mov", "avi", "mkv", "wmv", "mp3", "wav", "m4a", "flac", "aac", "ogg" };
            if (av.Contains(ext)) return "影音";

            // 文件/表單/投影片/PDF
            if (ext is "doc" or "docx" or "rtf" or "md" or "txt") return ext == "txt" ? "純文字" : "文件";
            if (ext is "xls" or "xlsx" or "csv" or "tsv") return "表單";
            if (ext is "ppt" or "pptx" or "key") return "投影片";
            if (ext is "pdf") return "PDF";

            // 設計/向量/專案檔
            if (ext is "psd" or "ai" or "xd" or "fig" or "sketch") return "設計";
            if (ext is "svg" or "eps") return "向量";
            if (ext is "aep" or "prproj" or "aup" or "aup3") return "專案檔";

            // 程式碼
            var code = new[]
            { "cs","js","ts","jsx","tsx","py","rb","php","java","kt","go","rs","cpp","c","h","m","swift","dart","scala","sh","ps1","lua","sql","yml","yaml","toml","xml","json" };
            if (code.Contains(ext)) return "程式碼";

            // 壓縮/封裝
            if (ext is "zip" or "7z" or "rar" or "gz" or "tar" or "tgz") return "壓縮";

            // 其他
            return "資料";
        }
    }
}
