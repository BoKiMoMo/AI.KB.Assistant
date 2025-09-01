using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    public class AppConfig
    {
        public AppSection App { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
    }

    public class AppSection
    {
        public string RootDir { get; set; } = @"C:\AIKB\Root";
        public string InboxDir { get; set; } = @"C:\AIKB\Inbox";
        public string DbPath { get; set; } = @"C:\AIKB\data\kb.db";
        public bool DryRun { get; set; } = true;
        /// <summary>move / copy</summary>
        public string MoveMode { get; set; } = "move";
        /// <summary>overwrite / skip / rename</summary>
        public string Overwrite { get; set; } = "rename";
    }

    public class RoutingSection
    {
        /// <summary>
        /// 範例：{Root}\{YYYY}\{MM}\{Category}\{Filename}
        /// 可用 token：{Root} {YYYY} {MM} {DD} {Category} {Filename}
        /// </summary>
        public string PathTemplate { get; set; } = @"{Root}\{YYYY}\{MM}\{Category}\{Filename}";

        /// <summary>
        /// 若為 true，分類名稱會用安全字元白名單（避免路徑非法字元）
        /// </summary>
        public bool SafeCategories { get; set; } = true;
    }

    public class ClassificationSection
    {
        /// <summary>llm / dummy / hybrid…（自定義）</summary>
        public string Engine { get; set; } = "llm";
        /// <summary>風格或提示詞</summary>
        public string Style { get; set; } = string.Empty;
        /// <summary>0~1</summary>
        public double ConfidenceThreshold { get; set; } = 0.6;
        /// <summary>分類失敗的備援類別</summary>
        public string FallbackCategory { get; set; } = "Unsorted";
        /// <summary>自訂分類清單</summary>
        public List<string> CustomTaxonomy { get; set; } = new();
    }

    public class OpenAISection
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4o-mini";
    }
}
