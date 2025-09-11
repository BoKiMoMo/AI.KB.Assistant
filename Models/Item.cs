namespace AI.KB.Assistant.Models
{
    public sealed class Item
    {
        public string Path { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Category { get; set; } = "";
        public double Confidence { get; set; }
        public long CreatedTs { get; set; }
        public string Summary { get; set; } = "";
        public string Reasoning { get; set; } = "";
        /// <summary>normal / pending / in-progress / todo / favorite</summary>
        public string Status { get; set; } = "normal";
        public string Tags { get; set; } = "";
        public string Project { get; set; } = "DefaultProject";
    }
}
