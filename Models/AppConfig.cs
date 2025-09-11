using Newtonsoft.Json;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 整體設定（對應 config.json）
    /// </summary>
    public sealed class AppConfig
    {
        public AppSection App { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
    }

    /// <summary>
    /// 應用程式相關路徑 / 旗標
    /// </summary>
    public sealed class AppSection
    {
        /// 根目錄（必要）
        public string RootDir { get; set; } = "";

        /// 收件匣資料夾
        public string InboxDir { get; set; } = "";

        /// SQLite DB 路徑
        public string DbPath { get; set; } = "";

        /// 專案名稱（可選）
        public string ProjectName { get; set; } = "";

        /// 乾跑（不真的移動檔案）
        public bool DryRun { get; set; } = false;

        /// 覆寫既有檔案
        public bool Overwrite { get; set; } = false;

        /// 檔案移動模式：copy / move
        public string MoveMode { get; set; } = "copy";
    }

    /// <summary>
    /// OpenAI 連線設定
    /// </summary>
    public sealed class OpenAISection
    {
        /// API Key（執行時由 UI 填入）
        public string ApiKey { get; set; } = "";

        /// 模型名稱
        public string Model { get; set; } = "gpt-4o-mini";
    }

    /// <summary>
    /// 路由與安全選項
    /// </summary>
    public sealed class RoutingSection
    {
        /// 僅允許安全類別（若你的分類服務會用到）
        public bool SafeCategoriesOnly { get; set; } = true;
    }

    /// <summary>
    /// 分類相關偏好（供本地規則/LLM 參考）
    /// </summary>
    public sealed class ClassificationSection
    {
        /// 預設後備分類
        public string FallbackCategory { get; set; } = "其他";

        /// 分類引擎：local / llm / hybrid（保留供未來切換）
        public string Engine { get; set; } = "local";

        /// 分類風格：category / date / project
        public string ClassificationMode { get; set; } = "category";

        /// 日期粒度：day / month / year
        public string TimeGranularity { get; set; } = "month";

        /// 顯示或輸出風格（保留）
        public string Style { get; set; } = "default";
    }
}
