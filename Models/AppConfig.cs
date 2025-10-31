using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 全域設定檔 (A 模式：config.json 與 .exe 同資料夾)
    /// </summary>
    public sealed class AppConfig
    {
        // === 全域靜態屬性 ===
        public static AppConfig Current { get; private set; } = Default;
        public static AppConfig Default => CreateDefault();
        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        // === 區段設定 ===
        public AppSection App { get; set; } = new();
        public DbSection Db { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ImportSection Import { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();

        #region === 載入 / 儲存 ===

        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                App = new AppSection
                {
                    StartupUIMode = "home",
                    RootDir = ""
                },
                Db = new DbSection
                {
                    DbPath = Path.Combine(AppContext.BaseDirectory, "ai_kb.db")
                },
                Routing = new RoutingSection
                {
                    RootDir = "",
                    UseProject = true,
                    UseYear = true,
                    UseMonth = true,
                    Threshold = 0.75,
                    AutoFolderName = "_auto",
                    LowConfidenceFolderName = "_low_conf",
                    UseType = "rule+llm",
                    BlacklistExts = new List<string>(),
                    BlacklistFolderNames = new List<string>()
                },
                Import = new ImportSection
                {
                    IncludeSubdir = true,
                    HotFolder = "",
                    EnableHotFolder = false,
                    OverwritePolicy = OverwritePolicy.KeepBoth,
                    MoveMode = "copy"
                },
                OpenAI = new OpenAISection
                {
                    ApiKey = "",
                    Model = "gpt-4o-mini"
                }
            };
        }

        public static AppConfig Load(string? path = null)
        {
            var p = string.IsNullOrWhiteSpace(path) ? ConfigPath : path!;
            if (!File.Exists(p))
            {
                Current = Default;
                Save(p);
                return Current;
            }

            try
            {
                var json = File.ReadAllText(p);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                Current = cfg ?? Default;
            }
            catch
            {
                Current = Default;
            }
            return Current;
        }

        public static void Save(string? path = null)
        {
            var p = string.IsNullOrWhiteSpace(path) ? ConfigPath : path!;
            var dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(p, json);
        }

        /// <summary>將目前設定直接寫入 config.json。</summary>
        public void SaveAsCurrent()
        {
            Current = this;
            Save();
        }

        /// <summary>以新設定替換目前實例（不自動儲存）。</summary>
        public static void ReplaceCurrent(AppConfig cfg)
        {
            if (cfg != null)
                Current = cfg;
        }

        /// <summary>建立淺拷貝副本（避免 UI 改動直接影響記憶體內設定）。</summary>
        public AppConfig Clone()
        {
            return new AppConfig
            {
                App = new AppSection
                {
                    StartupUIMode = App.StartupUIMode,
                    RootDir = App.RootDir
                },
                Db = new DbSection
                {
                    DbPath = Db.DbPath
                },
                Routing = new RoutingSection
                {
                    RootDir = Routing.RootDir,
                    UseProject = Routing.UseProject,
                    UseYear = Routing.UseYear,
                    UseMonth = Routing.UseMonth,
                    Threshold = Routing.Threshold,
                    AutoFolderName = Routing.AutoFolderName,
                    LowConfidenceFolderName = Routing.LowConfidenceFolderName,
                    UseType = Routing.UseType,
                    BlacklistExts = Routing.BlacklistExts?.ToList() ?? new List<string>(),
                    BlacklistFolderNames = Routing.BlacklistFolderNames?.ToList() ?? new List<string>()
                },
                Import = new ImportSection
                {
                    IncludeSubdir = Import.IncludeSubdir,
                    HotFolder = Import.HotFolder,
                    EnableHotFolder = Import.EnableHotFolder,
                    OverwritePolicy = Import.OverwritePolicy,
                    MoveMode = Import.MoveMode
                },
                OpenAI = new OpenAISection
                {
                    ApiKey = OpenAI.ApiKey,
                    Model = OpenAI.Model
                }
            };
        }

        #endregion
    }

    // === 子區段定義 ===

    public sealed class AppSection
    {
        public string StartupUIMode { get; set; } = "home";
        public string RootDir { get; set; } = "";
    }

    public sealed class DbSection
    {
        public string DbPath { get; set; } = "";
        public string Path { get => DbPath; set => DbPath = value; } // 相容舊名
    }

    public sealed class RoutingSection
    {
        public string RootDir { get; set; } = "";
        public bool UseProject { get; set; } = true;
        public bool UseYear { get; set; } = true;
        public bool UseMonth { get; set; } = true;
        public double Threshold { get; set; } = 0.75;
        public string LowConfidenceFolderName { get; set; } = "_low_conf";
        public string AutoFolderName { get; set; } = "_auto";
        public string UseType { get; set; } = "rule+llm";
        public List<string> BlacklistExts { get; set; } = new();
        public List<string> BlacklistFolderNames { get; set; } = new();
    }

    public sealed class ImportSection
    {
        public bool IncludeSubdir { get; set; } = true;
        public string HotFolder { get; set; } = "";
        public string HotFolderPath { get => HotFolder; set => HotFolder = value; } // 相容舊名
        public bool EnableHotFolder { get; set; } = false;
        public OverwritePolicy OverwritePolicy { get; set; } = OverwritePolicy.KeepBoth;
        public string MoveMode { get; set; } = "copy"; // 舊版本相容
    }

    public sealed class OpenAISection
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
    }
}
