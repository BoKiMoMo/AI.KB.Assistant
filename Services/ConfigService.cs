using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 設定檔存取工具（最小相容版）。
    /// </summary>
    public static class ConfigService
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } // enum 以字串序列化
        };

        /// <summary>嘗試讀取設定；若失敗則回傳新預設設定。</summary>
        public static AppConfig TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return CreateDefault();

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return CreateDefault();

                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? CreateDefault();

                // 防守：確保每節都有預設物件
                cfg.App ??= new AppConfig.AppSection();
                cfg.Import ??= new AppConfig.ImportSection();
                cfg.Routing ??= new AppConfig.RoutingSection();
                cfg.Classification ??= new AppConfig.ClassificationSection();
                cfg.OpenAI ??= new AppConfig.OpenAISection();

                return cfg;
            }
            catch
            {
                return CreateDefault();
            }
        }

        /// <summary>寫入設定檔；若資料夾不存在會自動建立。</summary>
        public static void Save(string path, AppConfig cfg)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                cfg ??= CreateDefault();
                cfg.App ??= new AppConfig.AppSection();
                cfg.Import ??= new AppConfig.ImportSection();
                cfg.Routing ??= new AppConfig.RoutingSection();
                cfg.Classification ??= new AppConfig.ClassificationSection();
                cfg.OpenAI ??= new AppConfig.OpenAISection();

                var json = JsonSerializer.Serialize(cfg, Options);
                File.WriteAllText(path, json);
            }
            catch
            {
                // 保守處理：不拋例外；如需紀錄可在此寫 Log。
            }
        }

        private static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                App = new AppConfig.AppSection
                {
                    RootDir = "",
                    DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai.kb.assistant.db"),
                    ProjectLock = "",
                    Theme = "Light",
                    StartupUIMode = "Detailed"
                },
                Import = new AppConfig.ImportSection
                {
                    HotFolderPath = "",
                    MoveMode = MoveMode.Move,
                    OverwritePolicy = OverwritePolicy.Rename,
                    IncludeSubdir = true,
                    BlacklistExts = Array.Empty<string>(),
                    BlacklistFolderNames = new[] { "_blacklist" },
                    ExtGroupMap = new System.Collections.Generic.Dictionary<string, string[]>(),
                    ExtGroupsJson = ""
                },
                Routing = new AppConfig.RoutingSection
                {
                    UseYear = true,
                    UseMonth = true,
                    UseProject = true,
                    UseType = true,
                    AutoFolderName = "自整理",
                    LowConfidenceFolderName = "信心不足"
                },
                Classification = new AppConfig.ClassificationSection
                {
                    ConfidenceThreshold = 0.75
                },
                OpenAI = new AppConfig.OpenAISection
                {
                    ApiKey = "",
                    Model = "gpt-4o-mini"
                }
            };
        }

        /// <summary>
        /// 相容舊版：最簡單的正規化。避免找不到 Normalize 的編譯錯誤。
        /// </summary>
        public static string Normalize(string input) => input?.Trim() ?? string.Empty;
    }
}
