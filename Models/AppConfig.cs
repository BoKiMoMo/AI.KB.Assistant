using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 應用程式設定檔結構，對應 config.json
    /// </summary>
    public class AppConfig
    {
        public string RootDir { get; set; } = "";
        public string DbPath { get; set; } = "main.db";
        public bool DryRun { get; set; } = true;
        public string MoveMode { get; set; } = "copy"; // copy / move
        public string OverwritePolicy { get; set; } = "skip"; // skip / rename / replace

        public string ClassificationMode { get; set; } = "category"; // category/project/date
        public string TimeGranularity { get; set; } = "day"; // day / month / year
        public double ConfidenceThreshold { get; set; } = 0.6;
        public string AutoFolderName { get; set; } = "自整理";

        public Dictionary<string, string> ExtensionMap { get; set; } = new();
        public Dictionary<string, string> KeywordMap { get; set; } = new();
        public Dictionary<string, string> RegexMap { get; set; } = new();

        // LLM（第三階段）
        public string OpenAIApiKey { get; set; } = "";
        public string OpenAIModel { get; set; } = "gpt-4o-mini";
    }
}
