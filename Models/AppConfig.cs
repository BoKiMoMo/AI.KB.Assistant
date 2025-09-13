using Newtonsoft.Json;

namespace AI.KB.Assistant.Models
{
    /// <summary>整體設定（對應 config.json）</summary>
    public sealed class AppConfig
    {
        public AppSection App { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
    }

    /// <summary>應用程式相關路徑 / 旗標</summary>
    public sealed class AppSection
    {
        /// 根目錄（必要）
        public string RootDir { get; set; } = "";
        /// 收件匣資料夾
        public string InboxDir { get; set; } = "";
        /// SQLite DB 路徑
        public string DbPath { get; set; } = "";
        /// 專案名稱（可選）
        public string ProjectName { get; set; } = "DefaultProject";
        /// 乾跑（不真的移動檔案）
        public bool DryRun { get; set; } = true;
        /// 覆寫既有檔案
        public bool Overwrite { get; set; } = false;
        /// 檔案移動模式：copy / move
        public string MoveMode { get; set; } = "copy";
    }

    /// <summary>OpenAI 連線設定（第三階段用，這版先保留）</summary>
    public sealed class OpenAISection
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
    }

    /// <summary>路由設定（目前保留基本即可）</summary>
    public sealed class RoutingSection
    {
        // 之後可擴充安全清單等
    }

    /// <summary>分類與目錄風格</summary>
    public sealed class ClassificationSection
    {
        /// 分類風格：category / date / project
        public string ClassificationMode { get; set; } = "category";
        /// 時間顆粒度：year / month / day
        public string TimeGranularity { get; set; } = "month";
        /// 預設後備分類
        public string FallbackCategory { get; set; } = "其他";
        /// 類別顯示風格（保留）
        public string Style { get; set; } = "default";
    }
}
