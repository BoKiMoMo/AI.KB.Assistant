// 路徑：AI.KB.Assistant/Models/AppConfig.cs
using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 整體設定（對應 config.json）
    /// </summary>
    public sealed class AppConfig
    {
        public AppSection App { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
    }

    /// <summary>
    /// 一般應用設定
    /// </summary>
    public sealed class AppSection
    {
        /// <summary>根目錄（實際搬檔目標的 root）</summary>
        public string RootDir { get; set; } = "";
        /// <summary>收件匣（拖放/匯入來源）</summary>
        public string InboxDir { get; set; } = "";
        /// <summary>SQLite DB 檔案完整路徑</summary>
        public string DbPath { get; set; } = "data/knowledge.db";

        /// <summary>move / copy</summary>
        public string MoveMode { get; set; } = "move";
        /// <summary>overwrite / skip / rename</summary>
        public string Overwrite { get; set; } = "rename";

        /// <summary>乾跑（只模擬不搬檔）</summary>
        public bool DryRun { get; set; } = true;

        // === 你目前缺少的屬性 ===
        /// <summary>分類風格（日期分類/類別分類/專案分類）</summary>
        public string ClassificationMode { get; set; } = "category";

        /// <summary>目前專案名稱（下拉式多專案管理會用到）</summary>
        public string ProjectName { get; set; } = "DefaultProject";

        /// <summary>時間粒度（日、月、年，影響資料夾層級）</summary>
        public string TimeGranularity { get; set; } = "month";
    }


    /// <summary>
    /// 路徑與命名策略
    /// </summary>
    public sealed class RoutingSection
    {
        /// <summary>
        /// 例：{root}/{category}/{yyyy}/{mm}/
        /// 可用欄位：{root},{category},{yyyy},{mm},{dd}
        /// </summary>
        public string PathTemplate { get; set; } = "{root}/{category}/{yyyy}/{mm}/";

        /// <summary>僅允許安全清單分類</summary>
        public bool SafeCategories { get; set; } = false;
    }

    /// <summary>
    /// AI/規則分類設定
    /// </summary>
    public sealed class ClassificationSection
    {
        /// <summary>分類引擎：llm / rules / hybrid / dummy</summary>
        public string Engine { get; set; } = "rules";

        /// <summary>提示風格（可留白）</summary>
        public string Style { get; set; } = "topic";

        /// <summary>信心門檻（0~1）</summary>
        public double ConfidenceThreshold { get; set; } = 0.6;

        /// <summary>辨識失敗時的預設類別</summary>
        public string FallbackCategory { get; set; } = "unsorted";

        /// <summary>自訂分類清單</summary>
        public List<string>? CustomTaxonomy { get; set; } = new()
        {
            "專案/會議", "需求/企劃", "開發/程式", "設計/UIUX", "測試/驗收",
            "研究/論文", "資料/數據", "法規/條款", "採購/報價", "合約/法務",
            "財會/發票", "人資/行政", "簡報/投影片", "圖片/素材", "影音/多媒體",
            "手冊/說明", "教學/課程", "部落格/文章", "行銷/文案", "其它"
        };
    }

    /// <summary>
    /// OpenAI 連線設定
    /// </summary>
    public sealed class OpenAISection
    {
        /// <summary>OpenAI API Key（留空表示停用 LLM）</summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>模型名稱：如 gpt-4o-mini / gpt-4o / o3-mini / gpt-4.1 等</summary>
        public string Model { get; set; } = "gpt-4o-mini";
    }
}
