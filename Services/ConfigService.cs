using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.KB.Assistant.Common;   // ToSafeList()
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>統一管理 config.json（固定儲存在 %AppData%\AI.KB.Assistant\config.json）。</summary>
    public static class ConfigService
    {
        /// <summary>目前的設定</summary>
        public static AppConfig Cfg { get; private set; } = AppConfig.Default;

        /// <summary>設定變更事件：任何 Load()/Save() 成功後都會廣播目前設定</summary>
        public static event EventHandler<AppConfig>? ConfigChanged;

        /// <summary>%AppData%\AI.KB.Assistant</summary>
        public static string ConfigFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AI.KB.Assistant");

        /// <summary>%AppData%\AI.KB.Assistant\config.json</summary>
        public static string ConfigPath => Path.Combine(ConfigFolder, "config.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>從磁碟讀取 config.json 並套用到 <see cref="Cfg"/>，完成後觸發 ConfigChanged。</summary>
        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);

                if (!File.Exists(ConfigPath))
                {
                    // 第一次沒有檔案就建立預設
                    Cfg = AppConfig.Default;
                    Save(); // Save() 內會廣播
                    return;
                }

                var json = File.ReadAllText(ConfigPath);
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
        public static bool Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);
                // 儲存前做一次正規化（避免寫出空白/重複）
                var norm = Cfg.Clone();
                norm.Routing.BlacklistExts = norm.Routing.BlacklistExts.ToSafeList();
                norm.Routing.BlacklistFolderNames = norm.Routing.BlacklistFolderNames.ToSafeList();
                if (norm.Routing.Threshold <= 0) norm.Routing.Threshold = 0.75;

                var json = JsonSerializer.Serialize(norm, JsonOpts);
                File.WriteAllText(ConfigPath, json);

                ConfigChanged?.Invoke(null, Cfg);
                return true;
            }
            catch 
            {

                return false;
            }

        }

        /// <summary>將設定重設為預設並立即儲存。</summary>
        public static void ResetToDefault()
        {
            Cfg = AppConfig.Default;
            Save();
        }
    }
}
