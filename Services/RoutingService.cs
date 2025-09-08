using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Helpers;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 根據設定與分類結果，決定檔案目的地路徑
    /// </summary>
    public sealed class RoutingService
    {
        private readonly AppConfig _cfg;

        // 繁中檔案類型分類（副檔名 → 分類名稱）
        private static readonly Dictionary<string, string> FileTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // 文件
            [".doc"] = "文件",
            [".docx"] = "文件",
            [".pdf"] = "文件",
            [".txt"] = "文件",
            [".xls"] = "文件",
            [".xlsx"] = "文件",
            [".ppt"] = "文件",
            [".pptx"] = "簡報",

            // 圖片
            [".jpg"] = "圖片",
            [".jpeg"] = "圖片",
            [".png"] = "圖片",
            [".gif"] = "圖片",
            [".bmp"] = "圖片",
            [".tif"] = "圖片",
            [".tiff"] = "圖片",
            [".svg"] = "圖片",

            // 音訊 / 影片
            [".mp3"] = "影音",
            [".wav"] = "影音",
            [".aac"] = "影音",
            [".mp4"] = "影音",
            [".mov"] = "影音",
            [".avi"] = "影音",
            [".mkv"] = "影音",

            // 壓縮
            [".zip"] = "壓縮",
            [".rar"] = "壓縮",
            [".7z"] = "壓縮",
            [".tar"] = "壓縮",

            // 程式碼
            [".cs"] = "技術",
            [".cpp"] = "技術",
            [".h"] = "技術",
            [".py"] = "技術",
            [".js"] = "技術",
            [".ts"] = "技術",
            [".java"] = "技術",
            [".html"] = "技術",
            [".css"] = "技術",
            [".json"] = "技術",
            [".xml"] = "技術",
        };

        public RoutingService(AppConfig cfg)
        {
            _cfg = cfg;
        }

        /// <summary>
        /// 建立目的地路徑
        /// </summary>
        public string BuildDestination(string srcPath, string category, DateTimeOffset when)
        {
            var fname = Path.GetFileName(srcPath);
            var parts = TimePathHelper.Parts(when);

            string destDir = _cfg.App.RootDir;

            switch (_cfg.App.ClassificationMode)
            {
                case "Category":
                    destDir = Path.Combine(_cfg.App.RootDir, category);
                    break;

                case "Date":
                    destDir = Path.Combine(_cfg.App.RootDir, parts.Year, parts.Month, category);
                    break;

                case "Project":
                    destDir = Path.Combine(_cfg.App.RootDir, _cfg.App.ProjectName, category);
                    break;

                case "FileType":
                    var ext = Path.GetExtension(srcPath);
                    var type = FileTypeMap.TryGetValue(ext, out var mapped) ? mapped : "其他";
                    destDir = Path.Combine(_cfg.App.RootDir, type, category);
                    break;

                case "TimePeriod":
                    destDir = _cfg.App.TimeGranularity switch
                    {
                        "Year" => Path.Combine(_cfg.App.RootDir, parts.Year, category),
                        "Quarter" => Path.Combine(_cfg.App.RootDir, parts.Year, parts.Quarter, category),
                        "Month" => Path.Combine(_cfg.App.RootDir, parts.Year, parts.Month, category),
                        "Week" => Path.Combine(_cfg.App.RootDir, parts.Year, parts.Week, category),
                        _ => Path.Combine(_cfg.App.RootDir, parts.Year, parts.Month, category),
                    };
                    break;

                case "Hybrid":
                    destDir = Path.Combine(_cfg.App.RootDir, _cfg.App.ProjectName, parts.Year, category);
                    break;

                default:
                    destDir = Path.Combine(_cfg.App.RootDir, "其他");
                    break;
            }

            // 確保資料夾存在
            Directory.CreateDirectory(destDir);

            return Path.Combine(destDir, fname);
        }

        /// <summary>
        /// 若檔案名稱衝突，依照 Overwrite 策略解決
        /// </summary>
        public string ResolveCollision(string path)
        {
            if (!File.Exists(path)) return path;

            var dir = Path.GetDirectoryName(path)!;
            var fname = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            if (_cfg.App.Overwrite.Equals("overwrite", StringComparison.OrdinalIgnoreCase))
                return path;

            if (_cfg.App.Overwrite.Equals("skip", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(dir, $"{fname}{ext}"); // 原樣返回，外層不覆蓋

            // 預設 rename
            int i = 2;
            string newPath;
            do
            {
                newPath = Path.Combine(dir, $"{fname} ({i++}){ext}");
            } while (File.Exists(newPath));
            return newPath;
        }
    }
}
