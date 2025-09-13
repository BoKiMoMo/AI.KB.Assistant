using Newtonsoft.Json;

namespace AI.KB.Assistant.Models
{
    public sealed class AppConfig
    {
        public AppSection App { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
    }

    public sealed class AppSection
    {
        public string RootDir { get; set; } = "";
        public string InboxDir { get; set; } = "";
        public string DbPath { get; set; } = "";
        public string ProjectName { get; set; } = "DefaultProject";
        public bool DryRun { get; set; } = true;   // 預設先模擬
        public bool Overwrite { get; set; } = false;
        public string MoveMode { get; set; } = "copy"; // copy / move
    }

    public sealed class OpenAISection
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
        public int TimeoutSeconds { get; set; } = 20;
    }

    public sealed class RoutingSection
    {
        public string TimeGranularity { get; set; } = "month"; // year/month/day
        public bool SafeCategoriesOnly { get; set; } = false;
    }

    public sealed class ClassificationSection
    {
        public string ClassificationMode { get; set; } = "category"; // category/project/date
        public string FallbackCategory { get; set; } = "其他";
        public string Style { get; set; } = "default";
        public string Engine { get; set; } = "local"; // local/llm/hybrid
        public double ConfidenceThreshold { get; set; } = 0.6;
        public bool UseLLM { get; set; } = true;
        public int MaxTags { get; set; } = 5;
        public bool EnableChatSearch { get; set; } = true;
    }
}
