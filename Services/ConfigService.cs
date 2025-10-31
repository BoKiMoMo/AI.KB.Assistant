using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.KB.Assistant.Common;  // ← ToSafeList() 擴充
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>統一管理 config.json 載入/儲存。</summary>
    public static class ConfigService
    {
        // 設定變更事件：任何 Load()/Save() 成功後都會廣播目前設定
        public static event EventHandler<AppConfig>? ConfigChanged;

        // A/B 優先 A：exe 同層；失敗回退到 B：%AppData%\AI.KB.Assistant\
        private static string ResolveConfigPath()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            var aPath = Path.Combine(exeDir, "config.json");
            if (File.Exists(aPath)) return aPath;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var bDir = Path.Combine(appData, "AI.KB.Assistant");
            Directory.CreateDirectory(bDir);
            return Path.Combine(bDir, "config.json");
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static AppConfig Cfg { get; private set; } = AppConfig.Default;

        private static string _cfgPath = ResolveConfigPath();

        /// <summary>從磁碟讀取 config.json 並套用到 <see cref="Cfg"/>，完成後觸發 ConfigChanged。</summary>
        public static void Load()
        {
            try
            {
                _cfgPath = ResolveConfigPath();

                if (!File.Exists(_cfgPath))
                {
                    // 第一次沒有檔案就建立預設
                    Cfg = AppConfig.Default;
                    Save(); // Save 內會廣播
                    return;
                }

                var json = File.ReadAllText(_cfgPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? AppConfig.Default;

                // 基本防禦 & 正規化
                loaded.App.RootDir ??= "";
                loaded.Db.Path ??= "";
                loaded.Import.HotFolderPath ??= "";
                loaded.Routing.UseType ??= "rule+llm";
                loaded.Routing.LowConfidenceFolderName ??= "_low_conf";
                loaded.Routing.BlacklistExts = loaded.Routing.BlacklistExts.ToSafeList();
                loaded.Routing.BlacklistFolderNames = loaded.Routing.BlacklistFolderNames.ToSafeList();
                if (loaded.Routing.Threshold <= 0) loaded.Routing.Threshold = 0.75;

                AppConfig.ReplaceCurrent(loaded);
                Cfg = AppConfig.Current;

                // 廣播：已載入
                ConfigChanged?.Invoke(null, Cfg);
            }
            catch
            {
                // 讀檔失敗則回預設避免整個 app 掛掉
                Cfg = AppConfig.Default;
                ConfigChanged?.Invoke(null, Cfg);
            }
        }

        /// <summary>把目前 <see cref="Cfg"/> 寫回磁碟，完成後觸發 ConfigChanged。</summary>
        public static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cfgPath)!);

            // 儲存前做一次正規化（避免寫出空白/重複）
            var norm = Cfg.Clone();
            norm.Routing.BlacklistExts = norm.Routing.BlacklistExts.ToSafeList();
            norm.Routing.BlacklistFolderNames = norm.Routing.BlacklistFolderNames.ToSafeList();
            if (norm.Routing.Threshold <= 0) norm.Routing.Threshold = 0.75;

            var json = JsonSerializer.Serialize(norm, JsonOpts);
            File.WriteAllText(_cfgPath, json);

            // 廣播：已儲存
            ConfigChanged?.Invoke(null, Cfg);
        }
    }
}
