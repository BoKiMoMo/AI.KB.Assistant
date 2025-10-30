using System;
using System.IO;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Models
{
    // 統一入口：維持 Current / Load / Save 與各區段結構
    public sealed class AppConfig
    {
        public static AppConfig Current { get; private set; } = Default();

        public AppSection App { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ImportSection Import { get; set; } = new();
        public DbSection Db { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();

        // 舊程式會呼叫：AppConfig.Default()
        public static AppConfig Default() => new AppConfig
        {
            App = new AppSection { StartupUIMode = "home" },
            Routing = new RoutingSection
            {
                RootDir = "",
                UseProject = true,
                UseYear = true,
                UseMonth = true,
                Threshold = 0.75,
                AutoFolderName = "_auto",
                LowConfidenceFolderName = "_low_conf"
            },
            Import = new ImportSection
            {
                IncludeSubdir = true,
                HotFolder = "",
                OverwritePolicy = OverwritePolicy.KeepBoth,
                EnableHotFolder = false
            },
            Db = new DbSection
            {
                DbPath = Path.Combine(AppContext.BaseDirectory, "ai_kb.db")
            },
            OpenAI = new OpenAISection { ApiKey = "" }
        };

        public static void Load()
        {
            // 以 ConfigService 為唯一儲存點（你專案已經有）
            var cfg = ConfigService.TryLoad<AppConfig>("app_config");
            Current = cfg ?? Default();
        }

        public static void Save() => ConfigService.Save("app_config", Current);

        // 舊程式會呼叫：ConfigService.Save(cfg)；這裡保留一個簡易代理，避免外部改動太多
        public void SaveAsCurrent()
        {
            Current = this;
            Save();
        }
    }

    // ===== 區段結構（維持舊呼叫介面） =====

    public sealed class AppSection
    {
        // 舊程式在 AppSection 要的屬性
        public string StartupUIMode { get; set; } = "home";
    }

    public sealed class RoutingSection
    {
        public string RootDir { get; set; } = "";
        public bool UseProject { get; set; } = true;
        public bool UseYear { get; set; } = true;
        public bool UseMonth { get; set; } = true;

        // 舊碼會取/設 double 門檻
        public double Threshold { get; set; } = 0.75;

        // 舊碼用來放低信心或自動分類資料夾名稱
        public string LowConfidenceFolderName { get; set; } = "_low_conf";
        public string AutoFolderName { get; set; } = "_auto";
    }

    public sealed class ImportSection
    {
        public bool IncludeSubdir { get; set; } = true;
        public string HotFolder { get; set; } = "";
        public bool EnableHotFolder { get; set; } = false;

        // 舊碼需要覆蓋策略
        public OverwritePolicy OverwritePolicy { get; set; } = OverwritePolicy.KeepBoth;
    }

    public sealed class DbSection
    {
        // 舊碼會叫 DbSection.DbPath
        public string DbPath { get; set; } = "";
    }

    public sealed class OpenAISection
    {
        // 舊碼會透過 SettingsWindow 寫入/讀取
        public string ApiKey { get; set; } = "";
    }
}
