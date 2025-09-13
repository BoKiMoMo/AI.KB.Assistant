using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AI.KB.Assistant.Services
{
    /// <summary>本地規則分類器（第三階段再換成 LLM）</summary>
    public sealed class LlmService
    {
        private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["合約/法律"] = new[] { "contract", "nda", "terms", "law", "legal", "合約", "法律", "條款" },
            ["財務/帳務"] = new[] { "invoice", "receipt", "tax", "bill", "報表", "帳務", "發票", "報稅", "請款" },
            ["簡報/提案"] = new[] { "ppt", "pptx", "key", "keynote", "deck", "簡報", "提案", "presentation" },
            ["報告/文件"] = new[] { "report", "doc", "docx", "spec", "minutes", "會議紀錄", "規格", "報告" },
            ["人事/履歷"] = new[] { "resume", "cv", "hr", "履歷", "面試" },
            ["採購/供應商"] = new[] { "purchase", "po", "vendor", "quote", "採購", "供應商", "報價" },

            ["程式碼/專案"] = new[] { "cs", "ts", "js", "py", "java", "go", "rs", "cpp", "git", "source", "repo", "solution", "sln" },
            ["設計/UIUX"] = new[] { "fig", "figma", "sketch", "psd", "ai", "xd", "ui", "ux", "wireframe", "設計" },
            ["研究/分析"] = new[] { "paper", "thesis", "dataset", "benchmark", "analysis", "研究", "分析" },

            ["行銷/活動"] = new[] { "campaign", "ad", "edm", "行銷", "活動", "企劃" },
            ["客戶/銷售"] = new[] { "crm", "客訴", "sales", "proposal", "方案" },

            ["圖片/相片"] = new[] { "jpg", "jpeg", "png", "heic", "raw", "tiff", "bmp", "photo", "image" },
            ["影音"] = new[] { "mp4", "mov", "avi", "mkv", "wav", "mp3", "audio", "video" },
            ["截圖"] = new[] { "screenshot", "截圖", "screen shot" },

            ["壓縮/封存"] = new[] { "zip", "rar", "7z", "tar", "gz", "tgz" },
            ["安裝包/執行檔"] = new[] { "exe", "msi", "dmg", "pkg", "appimage" },

            ["個人文件"] = new[] { "個人", "private", "self" },
            ["教學/課程"] = new[] { "lesson", "tutorial", "course", "課程", "教學" },
        };

        public Task<(string category, double conf)> ClassifyLocalAsync(string fileName)
        {
            var name = fileName?.ToLowerInvariant() ?? "";
            var ext = System.IO.Path.GetExtension(name).Trim('.');

            foreach (var kv in Map)
            {
                if (kv.Value.Any(k =>
                        name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(ext) && string.Equals(ext, k, StringComparison.OrdinalIgnoreCase))))
                {
                    return Task.FromResult((kv.Key, 0.9));
                }
            }
            return Task.FromResult(("其他", 0.3));
        }
    }
}
