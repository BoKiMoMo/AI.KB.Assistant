using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 對應 config.json 的根設定物件
    /// </summary>
    public sealed class AppConfig
    {
        public AppSection App { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
    }

    /// <summary>
    /// 應用程式層級設定
    /// </summary>
    public sealed class AppSection
    {
        /// <summary>根目錄 (例如：D:\KB)</summary>
        public string RootDir { get; set; } = "";

        /// <summary>收件匣/投入匣目錄</summary>
        public string InboxDir { get; set; } = "";

        /// <summary>SQLite DB 檔案路徑</summary>
        public string DbPath { get; set; } = "";

        /// <summary>乾跑模式（只模擬不搬檔）</summary>
        public bool DryRun { get; set; }

        /// <summary>搬移模式：move / copy</summary>
        public string MoveMode { get; set; } = "move";

        /// <summary>覆寫策略：overwrite / skip / rename</summary>
        public string Overwrite { get; set; } = "skip";

        /// <summary>目前作用中的專案名稱</summary>
        public string ProjectName { get; set; } = "Default";

        /// <summary>多專案清單（可於 UI 下拉選）</summary>
        public List<string> Projects { get; set; } = new();

        /// <summary>
        /// 分類風格：
        /// category（依類別路徑），date（依日期路徑），project（依專案路徑）
        /// </summary>
        public string ClassificationMode { get; set; } = "category";

        /// <summary>
        /// 日期粒度：day / week / month（配合 date 模式使用）
        /// </summary>
        public string TimeGranularity { get; set; } = "month";
    }

    /// <summary>
    /// 檔案路徑與命名規則
    /// </summary>
    public sealed class RoutingSection
    {
        /// <summary>
        /// 路徑樣板（支援占位：{root}、{project}、{category}、{yyyy}、{mm}、{dd}）
        /// 範例：{root}/{category}/{yyyy}/{mm}/
        /// </summary>
        public string PathTemplate { get; set; } = "{root}/{category}/{yyyy}/{mm}/";

        /// <summary>是否只允許安全清單內的分類</summary>
        public bool SafeCategories { get; set; } = false;
    }

    /// <summary>
    /// AI 分類與自訂分類
    /// </summary>
    public sealed class ClassificationSection
    {
        /// <summary>引擎：llm / dummy / hybrid</summary>
        public string Engine { get; set; } = "llm";

        /// <summary>提示風格（topic、keywords…自由字串）</summary>
        public string Style { get; set; } = "topic";

        /// <summary>信心度閾值（0~1）</summary>
        public double Threshold { get; set; } = 0.6;

        /// <summary>當無法判斷時的預設類別</summary>
        public string FallbackCategory { get; set; } = "unsorted";

        /// <summary>自訂分類清單（會出現在設定頁 ListBox）</summary>
        public List<string> CustomTaxonomy { get; set; } = new();
    }

    /// <summary>
    /// OpenAI 連線設定
    /// </summary>
    public sealed class OpenAISection
    {
        /// <summary>OpenAI API Key（執行時於 UI 設定，不建議硬寫入檔）</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>模型名稱（例如：gpt-4o-mini / gpt-4o / o3-mini）</summary>
        public string Model { get; set; } = "gpt-4o-mini";
    }
}
