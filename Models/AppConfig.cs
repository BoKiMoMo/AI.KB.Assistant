using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AI.KB.Assistant.Services; // V7.34 修正：移除 using

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 全域設定檔 (V7.34 重構：移除所有靜態邏輯，改由 ConfigService 統一管理)
    /// </summary>
    public sealed class AppConfig
    {
        // === 區段設定 ===
        public AppSection App { get; set; } = new();
        public DbSection Db { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ImportSection Import { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();

        #region === 載入 / 儲存 (V7.34 註解：以下方法已棄用，邏輯移至 ConfigService) ===

        /*
        // V7.34 棄用：邏輯移至 ConfigService
        public static AppConfig Current { get; private set; } = Default;
        public static AppConfig Default => CreateDefault();
        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static AppConfig CreateDefault()
        {
            // ... (邏輯已搬到 ConfigService) ...
        }

        public static AppConfig Load(string? path = null)
        {
            // ... (邏輯已搬到 ConfigService) ...
        }

        public static void Save(string? path = null)
        {
            // ... (邏輯已搬到 ConfigService) ...
        }
        
        public void SaveAsCurrent()
        {
            // ... (邏輯已搬到 ConfigService) ...
        }
        
        public static void ReplaceCurrent(AppConfig cfg)
        {
            if (cfg != null)
                Current = cfg;
        }
        */

        /// <summary>建立淺拷貝副本（避免 UI 改動直接影響記憶體內設定）。</summary>
        public AppConfig Clone()
        {
            return new AppConfig
            {
                App = new AppSection
                {
                    StartupUIMode = App.StartupUIMode,
                    RootDir = App.RootDir,
                    LaunchMode = App.LaunchMode // V7.34 UI 串接：新增
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
                    UseCategory = Routing.UseCategory,                          // NEW
                    Threshold = Routing.Threshold,
                    AutoFolderName = Routing.AutoFolderName,
                    LowConfidenceFolderName = Routing.LowConfidenceFolderName,
                    UseType = Routing.UseType,
                    BlacklistExts = Routing.BlacklistExts?.ToList() ?? new List<string>(),
                    BlacklistFolderNames = Routing.BlacklistFolderNames?.ToList() ?? new List<string>(),
                    FolderOrder = Routing.FolderOrder?.ToList()                 // NEW
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

        // V7.34 UI 串接：新增
        // "Simple" 或 "Detailed"
        public string? LaunchMode { get; set; }
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

        // NEW: 勾選才啟用類別層
        public bool UseCategory { get; set; } = false;

        public double Threshold { get; set; } = 0.75;
        public string LowConfidenceFolderName { get; set; } = "_low_conf";
        public string AutoFolderName { get; set; } = "_auto";
        public string UseType { get; set; } = "rule+llm";
        public List<string> BlacklistExts { get; set; } = new();
        public List<string> BlacklistFolderNames { get; set; } = new();

        // NEW: 可自訂層級順序（token：year, month, project, category）
        public List<string>? FolderOrder { get; set; } = null;
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