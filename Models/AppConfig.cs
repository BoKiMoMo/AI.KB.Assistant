using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 應用程式設定：集中管理，支援模組化 Section 與 Load/Save。
    /// ※ 含舊屬性相容層（RootDir / HotFolder / Threshold），方便過渡。
    /// </summary>
    public partial class AppConfig
    {
        // === 路徑 ===
        /// <summary>預設設定檔檔名</summary>
        public const string DefaultConfigFile = "config.json";

        /// <summary>設定檔實際路徑（預設在執行檔相同資料夾）</summary>
        [JsonIgnore]
        public string ConfigPath { get; set; } =
            Path.Combine(AppContext.BaseDirectory, DefaultConfigFile);

        // === 模組化 Section ===
        public ThemeSection Theme { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ImportSection Import { get; set; } = new();
        public DbSection Db { get; set; } = new();

        // === 舊版相容（讀寫都代理到對應 Section）===
        [JsonIgnore]
        public string? RootDir
        {
            get => Routing.RootDir;
            set => Routing.RootDir = value;
        }

        [JsonIgnore]
        public string? HotFolder
        {
            get => Import.HotFolder;
            set => Import.HotFolder = value;
        }

        [JsonIgnore]
        public double Threshold
        {
            get => Routing.Threshold;
            set => Routing.Threshold = value;
        }

        // === 靜態 Json 選項 ===
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        // === 讀寫 ===
        public static AppConfig Load(string? path = null)
        {
            var cfg = new AppConfig();
            if (!string.IsNullOrWhiteSpace(path))
                cfg.ConfigPath = path;

            try
            {
                var p = cfg.ConfigPath;
                if (File.Exists(p))
                {
                    var json = File.ReadAllText(p);
                    var loaded = JsonSerializer.Deserialize<AppConfig>(json, s_jsonOptions);
                    if (loaded != null)
                    {
                        // 保留外部傳入的 ConfigPath
                        loaded.ConfigPath = p;
                        loaded.Normalize();
                        return loaded;
                    }
                }
            }
            catch
            {
                // 若讀取失敗，以預設值續行；不拋例外以確保程式可啟動
            }

            cfg.Normalize();
            return cfg;
        }

        public void Save(string? path = null)
        {
            if (!string.IsNullOrWhiteSpace(path))
                ConfigPath = path;

            Normalize();

            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this, s_jsonOptions);
            File.WriteAllText(ConfigPath, json);
        }

        /// <summary>
        /// 修正/回填空值與不合法值，確保執行時穩定。
        /// </summary>
        public void Normalize()
        {
            Theme ??= new ThemeSection();
            OpenAI ??= new OpenAISection();
            Routing ??= new RoutingSection();
            Import ??= new ImportSection();
            Db ??= new DbSection();

            // Route 預設夾名
            if (string.IsNullOrWhiteSpace(Routing.AutoFolderName))
                Routing.AutoFolderName = "自整理";
            if (string.IsNullOrWhiteSpace(Routing.LowConfidenceFolderName))
                Routing.LowConfidenceFolderName = "信心不足";

            // 合法化門檻
            if (Routing.Threshold < 0) Routing.Threshold = 0;
            if (Routing.Threshold > 1) Routing.Threshold = 1;

            // 路徑規整
            if (!string.IsNullOrWhiteSpace(Routing.RootDir))
                Routing.RootDir = NormalizeDir(Routing.RootDir);

            if (!string.IsNullOrWhiteSpace(Import.HotFolder))
                Import.HotFolder = NormalizeDir(Import.HotFolder);

            if (!string.IsNullOrWhiteSpace(Db.DbPath))
                Db.DbPath = Path.GetFullPath(Db.DbPath);
        }

        private static string NormalizeDir(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!Directory.Exists(full)) Directory.CreateDirectory(full);
                return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }
    }

    // ========= 各 Section 定義 =========

    /// <summary>主題設定（保留簡單鍵值，讓 ThemeService 統一運用）</summary>
    public class ThemeSection
    {
        /// <summary>Light / Dark（僅用於 UI 切換，不影響 XAML ResourceKey 命名）</summary>
        public string Mode { get; set; } = "Light";
        public string AccentColor { get; set; } = "#4F46E5";
    }

    public class OpenAISection
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
        /// <summary>是否在低信心時自動產生建議（不自動搬檔）</summary>
        public bool SuggestOnLowConfidence { get; set; } = true;
        /// <summary>最低信心閾值，低於此值才觸發 LLM 建議（僅用於建議，不會覆蓋 Routing.Threshold）</summary>
        public double SuggestThreshold { get; set; } = 0.75;
    }

    public class RoutingSection
    {
        /// <summary>根目錄（實際分類的目的地根）</summary>
        public string? RootDir { get; set; }

        /// <summary>自整理資料夾名稱（RootDir 下）</summary>
        public string AutoFolderName { get; set; } = "自整理";

        /// <summary>信心不足資料夾名稱（RootDir 下）</summary>
        public string LowConfidenceFolderName { get; set; } = "信心不足";

        /// <summary>AI 信心門檻（0~1）</summary>
        public double Threshold { get; set; } = 0.80;

        /// <summary>是否使用年份/月份路徑</summary>
        public bool UseYear { get; set; } = true;
        public bool UseMonth { get; set; } = false;

        /// <summary>是否使用專案夾層</summary>
        public bool UseProject { get; set; } = true;

        /// <summary>是否使用類型夾層（副檔名/語義類別）</summary>
        public bool UseType { get; set; } = true;

        /// <summary>目的地檔名衝突策略</summary>
        public OverwritePolicy OverwritePolicy { get; set; } = OverwritePolicy.Rename;
    }

    public class ImportSection
    {
        /// <summary>收件夾（HotFolder）路徑；加入收件夾與拖放都會落這裡</summary>
        public string? HotFolder { get; set; }

        /// <summary>是否啟用 HotFolder 背景監聽</summary>
        public bool EnableHotFolder { get; set; } = true;

        /// <summary>拖放到主視窗是否自動加入收件夾</summary>
        public bool AutoOnDrop { get; set; } = true;

        /// <summary>加入資料夾時是否包含子資料夾</summary>
        public bool IncludeSubdirectories { get; set; } = true;

        /// <summary>加入資料夾時最大遞迴深度（避免誤吃爆量檔案）</summary>
        public int MaxDepth { get; set; } = 5;
    }

    public class DbSection
    {
        /// <summary>SQLite 檔案路徑；若 UseMemory=true 則忽略</summary>
        public string DbPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "data.db");

        /// <summary>是否改用記憶體資料庫（測試模式）</summary>
        public bool UseMemory { get; set; } = false;
    }

    // ========= 列舉 =========

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OverwritePolicy
    {
        /// <summary>覆蓋</summary>
        Replace,
        /// <summary>改名（附加 -1, -2…）</summary>
        Rename,
        /// <summary>略過</summary>
        Skip
    }
}
