namespace AI.KB.Assistant.Models
{
    /// <summary>本地/AI 分類的強型別結果（第三階段可擴充）</summary>
    public sealed class ClassificationResult
    {
        public string Category { get; set; } = "";
        public string FileType { get; set; } = "";
        public double Confidence { get; set; }
        public string Reason { get; set; } = "";
    }
}
