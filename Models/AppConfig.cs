using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AI.KB.Assistant.Models
{
    public sealed class AppConfig
    {
        public AppSection App { get; set; } = new();
        public ImportSection Import { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
    }

    public sealed class AppSection
    {
        public string DbPath { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "AI.KB.Assistant", "data", "kb.db");

        public string RootDir { get; set; } =
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        public string Theme { get; set; } = "Light";     // Light / Dark
        public string ProjectLock { get; set; } = "";     // 空字串 = 無鎖定
    }

    public sealed class ImportSection
    {
        public bool EnableHotFolder { get; set; } = true;
        public string HotFolderPath { get; set; } = "";           // 空字串時使用 RootDir\_Inbox
        public string BlacklistFolderName { get; set; } = "_blacklist";
        public bool IncludeSubdirectories { get; set; } = false;
        public bool AutoClassifyOnDrop { get; set; } = true;      // 丟進收件夾時，直接做「預分類」
    }

    public sealed class RoutingSection
    {
        public bool EnableYear { get; set; } = true;     // 建立 年 資料夾
        public bool EnableMonth { get; set; } = true;    // 建立 月 資料夾
        public bool EnableProject { get; set; } = true;  // 建立 專案 資料夾（ProjectLock 或每筆的 project）
        public bool EnableType { get; set; } = true;     // 建立 型態 資料夾（圖片/影片/音訊/Office/Adobe/程式碼/其他）
        public string AutoFolderName { get; set; } = "自整理"; // 信心不足時的預設分類夾
    }

    public sealed class ClassificationSection
    {
        public double ConfidenceThreshold { get; set; } = 0.72;   // 低於此門檻可啟用 LLM
        public string FallbackCategory { get; set; } = "一般專案"; // 缺省分類
        public Dictionary<string, string> KeywordMap { get; set; } = new()
        {
            // keyword -> category（可自行在 config.json 擴充）
            ["invoice"] = "財務",
            ["contract"] = "合約",
            ["spec"] = "規格",
            ["proposal"] = "提案"
        };
    }

    public sealed class OpenAISection
    {
        public bool EnableWhenLowConfidence { get; set; } = true;
        public string ApiKey { get; set; } = "";
        public string BaseUrl { get; set; } = "";        // 可留空
        public string Model { get; set; } = "gpt-4o-mini";
    }

    // --- 檔案 I/O（給 ConfigService 使用或直接使用也可以） ---
    public static class ConfigService
    {
        public static AppConfig TryLoad(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (cfg != null) return cfg;
                }
            }
            catch { /* ignore and generate default */ }

            var def = new AppConfig();
            try { Save(path, def); } catch { }
            return def;
        }

        public static void Save(string path, AppConfig cfg)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}
