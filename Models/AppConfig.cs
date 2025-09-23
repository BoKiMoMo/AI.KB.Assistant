// Models/AppConfig.cs
using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    /// <summary>整體設定（對應 config.json）</summary>
    public sealed class AppConfig
    {
        public AppSection App { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
        public ViewsSection Views { get; set; } = new();
    }

    public sealed class AppSection
    {
        public string RootDir { get; set; } = "";
        public string InboxDir { get; set; } = "";
        public string DbPath { get; set; } = "";
        public string ProjectName { get; set; } = "DefaultProject";
        public bool DryRun { get; set; } = true;
        public string MoveMode { get; set; } = "copy"; // copy | move
        public string OverwritePolicy { get; set; } = "skip"; // skip | rename | replace
    }

    public sealed class OpenAISection
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
    }

    public sealed class RoutingSection
    {
        public string TimeGranularity { get; set; } = "month"; // day | month | year
        public string ClassificationMode { get; set; } = "category"; // category | project | date
        public bool SafeCategoriesOnly { get; set; } = false;
    }

    public sealed class ClassificationSection
    {
        public string FallbackCategory { get; set; } = "其他";
        public string Engine { get; set; } = "local"; // local | openai（第三階段）
        public double ConfidenceThreshold { get; set; } = 0.6;
        public string AutoFolderName { get; set; } = "自整理";

        // v2 規則
        public Dictionary<string, List<string>> ExtensionMap { get; set; } = new(); // 類別 → 副檔名
        public Dictionary<string, List<string>> KeywordMap { get; set; } = new(); // 類別 → 關鍵字
        public Dictionary<string, List<string>> RegexMap { get; set; } = new(); // 類別 → 正則
    }

    public sealed class ViewsSection
    {
        public List<string> FavoriteTags { get; set; } = new(); // 最近標籤（右鍵快捷）
    }
}
