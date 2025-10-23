using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 本地規則分類引擎（不落實搬檔，只提供預測：Category/Project/TargetFolder）
    /// </summary>
    public sealed class RuleEngineService
    {
        private readonly AppConfig _cfg;

        public RuleEngineService(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();
        }

        /// <summary>
        /// 根據副檔名比對到 Routing.ExtensionGroups 的 Key，回傳群組名（例如：Images / Documents / Videos ...）
        /// 如果找不到就回傳 "Others"
        /// </summary>
        public string ResolveCategoryByExt(string? ext)
        {
            var e = (ext ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(e))
                return "Others";

            var groups = _cfg.Routing.ExtensionGroups ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in groups)
            {
                if ((kv.Value ?? Array.Empty<string>()).Any(x => string.Equals(x?.Trim('.').ToLowerInvariant(), e, StringComparison.OrdinalIgnoreCase)))
                    return kv.Key;
            }
            return "Others";
        }

        /// <summary>
        /// 沒有鎖定專案時，預設專案名 = yyyyMM（UTC）
        /// </summary>
        public static string DefaultProjectNow()
        {
            var ts = DateTimeOffset.UtcNow;
            return $"{ts:yyyyMM}";
        }

        /// <summary>
        /// 依照 Routing 設定組出目標路徑。
        /// rootDir / (AutoFolderName?) / [yyyy] / [MM] / [Project] / [Type]
        /// </summary>
        public string BuildTargetFolder(string category, string project)
        {
            var root = string.IsNullOrWhiteSpace(_cfg.App.RootDir) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : _cfg.App.RootDir;
            var parts = new List<string> { root };

            // 可選：以 AutoFolderName 作為所有自動整理的根，以避免污染使用者根目錄
            var autoName = _cfg.Routing.AutoFolderName;
            if (!string.IsNullOrWhiteSpace(autoName))
                parts.Add(autoName);

            if (_cfg.Routing.UseYear)
                parts.Add(DateTime.UtcNow.ToString("yyyy"));

            if (_cfg.Routing.UseMonth)
                parts.Add(DateTime.UtcNow.ToString("MM"));

            if (_cfg.Routing.UseProject && !string.IsNullOrWhiteSpace(project))
                parts.Add(project);

            if (_cfg.Routing.UseType && !string.IsNullOrWhiteSpace(category))
                parts.Add(category);

            return Path.Combine(parts.ToArray());
        }

        /// <summary>
        /// 核心：由原始檔案路徑推論「類別/專案/目標資料夾」。不做搬檔。
        /// </summary>
        public PredictedRoute Predict(string filePath, string? lockedProject = null)
        {
            var filename = Path.GetFileName(filePath);
            var ext = Path.GetExtension(filePath)?.Trim('.').ToLowerInvariant();
            var category = ResolveCategoryByExt(ext);

            // 專案：若有鎖定則用鎖定；否則 yyyyMM
            var project = string.IsNullOrWhiteSpace(lockedProject) ? DefaultProjectNow() : lockedProject;

            var targetFolder = BuildTargetFolder(category, project);
            var targetPath = Path.Combine(targetFolder, filename);

            return new PredictedRoute
            {
                Category = category,
                Project = project,
                TargetFolder = targetFolder,
                TargetFullPath = targetPath,
            };
        }
    }

    public sealed class PredictedRoute
    {
        public string Category { get; set; } = "Others";
        public string Project { get; set; } = "";
        public string TargetFolder { get; set; } = "";
        public string TargetFullPath { get; set; } = "";
    }
}
