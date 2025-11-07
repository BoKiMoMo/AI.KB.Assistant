using System;
using System.Collections.Generic; // V7.34 修正：加入
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.KB.Assistant.Common;   // ToSafeList()
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 統一管理 config.json（固定儲存在 %AppData%\AI.KB.Assistant\config.json）。
    /// V7.34 重構：
    /// 1) 將 AppConfig.CreateDefault() 邏輯移至此處。
    /// 2) 修正 Load()，使其正確呼叫 CreateDefault()。
    /// </summary>
    public static class ConfigService
    {
        // V7.34 修正：搬移 CreateDefault() 邏輯到 ConfigService 內部
        private static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                App = new AppSection
                {
                    StartupUIMode = "home",
                    RootDir = "",
                    LaunchMode = "simple" // V7.34 邏輯修正：預設為 "simple"
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
                    UseCategory = false,
                    Threshold = 0.75,
                    AutoFolderName = "_auto",
                    LowConfidenceFolderName = "_low_conf",
                    UseType = "rule+llm",
                    BlacklistExts = new List<string>(),
                    BlacklistFolderNames = new List<string>(),
                    FolderOrder = null
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

        /// <summary>目前設定（永遠非 null）</summary>
        public static AppConfig Cfg { get; private set; } = CreateDefault(); // V7.34 修正

        /// <summary>任何成功的 Load()/Save() 都會廣播</summary>
        public static event EventHandler<AppConfig>? ConfigChanged;

        /// <summary>%AppData%\AI.KB.Assistant</summary>
        public static string ConfigFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AI.KB.Assistant");

        /// <summary>%AppData%\AI.KB.Assistant\config.json</summary>
        public static string ConfigPath => Path.Combine(ConfigFolder, "config.json");

        private static readonly object _sync = new();
        private static string _lastSnapshotJson = "";   // 用於去重廣播
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>將設定正規化（填預設、清理清單、閾值下限）</summary>
        private static AppConfig Normalize(AppConfig src)
        {
            var cfg = src ?? CreateDefault(); // V7.34 修正

            cfg.App.RootDir ??= "";
            cfg.Db.Path ??= "";
            cfg.Import.HotFolderPath ??= "";

            cfg.Routing.UseType ??= "rule+llm";
            cfg.Routing.LowConfidenceFolderName ??= "_low_conf";
            cfg.Routing.BlacklistExts = cfg.Routing.BlacklistExts.ToSafeList();
            cfg.Routing.BlacklistFolderNames = cfg.Routing.BlacklistFolderNames.ToSafeList();
            if (cfg.Routing.Threshold <= 0) cfg.Routing.Threshold = 0.75;

            return cfg;
        }

        /// <summary>取得可比較的快照 JSON（正規化後、縮排固定）。</summary>
        private static string SnapshotJson(AppConfig cfg)
            => JsonSerializer.Serialize(cfg, JsonOpts);

        /// <summary>從磁碟讀取 config.json 並套用到 <see cref="Cfg"/>，完成後（若內容有變更）觸發 ConfigChanged。</summary>
        public static void Load()
        {
            lock (_sync)
            {
                try
                {
                    Directory.CreateDirectory(ConfigFolder);

                    AppConfig loaded;
                    if (!File.Exists(ConfigPath))
                    {
                        // 首次無檔 → 建立預設並寫檔
                        loaded = Normalize(CreateDefault().Clone()); // V7.34 修正
                        var jsonNew = SnapshotJson(loaded);
                        File.WriteAllText(ConfigPath, jsonNew, Encoding.UTF8);
                        ApplyIfChanged(loaded, jsonNew);
                        return;
                    }

                    var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    // 任何反序列化失敗都回到 Default
                    loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? CreateDefault(); // V7.34 修正
                    loaded = Normalize(loaded);

                    var snap = SnapshotJson(loaded);
                    ApplyIfChanged(loaded, snap);
                }
                catch
                {
                    // 讀檔出錯 → 回預設（仍做一次去重廣播）
                    var fallback = Normalize(CreateDefault().Clone()); // V7.34 修正
                    var snap = SnapshotJson(fallback);
                    ApplyIfChanged(fallback, snap);
                }
            }
        }

        /// <summary>把目前 <see cref="Cfg"/> 寫回磁碟；若內容有變更則觸發 ConfigChanged。</summary>
        public static bool Save()
        {
            lock (_sync)
            {
                try
                {
                    Directory.CreateDirectory(ConfigFolder);

                    // 儲存前做正規化（寫檔以正規化後為準）
                    var norm = Normalize(Cfg.Clone());
                    var newJson = SnapshotJson(norm);

                    // 若檔案存在且內容一致，就不重寫也不重發事件
                    var needWrite = true;
                    if (File.Exists(ConfigPath))
                    {
                        var diskJson = File.ReadAllText(ConfigPath, Encoding.UTF8);
                        needWrite = !StringEquals(diskJson, newJson);
                    }

                    if (needWrite)
                        File.WriteAllText(ConfigPath, newJson, Encoding.UTF8);

                    // 確保內存 Cfg 與快照一致（避免外部直接改 Cfg 但未正規化）
                    ApplyIfChanged(norm, newJson);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>將設定重設為預設並立即儲存。</summary>
        public static void ResetToDefault()
        {
            lock (_sync)
            {
                Cfg = CreateDefault(); // V7.34 修正
                Save();
            }
        }

        /// <summary>若內容與前一版不同，更新 Cfg、更新快照並廣播。</summary>
        private static void ApplyIfChanged(AppConfig next, string nextJson)
        {
            if (StringEquals(_lastSnapshotJson, nextJson))
            {
                // 無變更，不廣播
                Cfg = next; // 仍同步記憶體狀態（保險）
                return;
            }

            // V7.34 修正：移除對 AppConfig.ReplaceCurrent 的靜態呼叫
            Cfg = next;

            _lastSnapshotJson = nextJson;
            try { ConfigChanged?.Invoke(null, Cfg); } catch { /* 外部事件失敗不影響流程 */ }
        }

        private static bool StringEquals(string a, string b)
            => string.Equals(a?.Trim(), b?.Trim(), StringComparison.Ordinal);
    }
}