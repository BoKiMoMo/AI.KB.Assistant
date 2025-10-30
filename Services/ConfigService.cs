using System;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 設定服務封裝：
    /// - 統一入口存取 AppConfig（AppConfig.Current）
    /// - 提供 Load/Save 與簡易 Get/Set 輔助
    /// - 相容舊專案對 ConfigService 的呼叫型式
    /// </summary>
    public static class ConfigService
    {
        /// <summary>
        /// 目前設定（等同 AppConfig.Current）
        /// </summary>
        public static AppConfig App => AppConfig.Current;

        /// <summary>
        /// 載入設定（若檔案不存在會建立預設並儲存）
        /// </summary>
        public static void Load(string? path = null) => AppConfig.Load(path);

        /// <summary>
        /// 儲存設定至檔案
        /// </summary>
        public static void Save(string? path = null) => AppConfig.Save(path);

        /// <summary>
        /// 相容舊呼叫：TryLoad(out cfg, path)
        /// </summary>
        public static void TryLoad(out AppConfig cfg, string? path = null)
        {
            try
            {
                AppConfig.Load(path);
                cfg = AppConfig.Current;
            }
            catch
            {
                // 發生例外時回預設並覆寫
                AppConfig.Load(path);
                cfg = AppConfig.Current;
            }
        }

        /// <summary>
        /// 讀取（便利方法）：selector(AppConfig.Current)
        /// </summary>
        public static T Get<T>(Func<AppConfig, T> selector) => selector(App);

        /// <summary>
        /// 寫入（便利方法）：mutator(AppConfig.Current)；預設會自動 Save
        /// </summary>
        public static void Set(Action<AppConfig> mutator, bool save = true)
        {
            mutator(App);
            if (save) Save();
        }

        /// <summary>
        /// 目前使用中的設定檔路徑（轉拋 AppConfig.ConfigPath）
        /// </summary>
        public static string ConfigPath => AppConfig.ConfigPath;
    }
}
