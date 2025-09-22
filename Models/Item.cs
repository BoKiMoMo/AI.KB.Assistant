namespace AI.KB.Assistant.Models
{
    public sealed class Item
    {
        public string Path { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Category { get; set; } = "";
        public string FileType { get; set; } = "";   // 新增：副檔名（不含點）
        public double Confidence { get; set; }
        public long CreatedTs { get; set; }
        public int Year { get; set; }                 // 新增：年
        public string Summary { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public string Status { get; set; } = "normal"; // normal / pending / in-progress / favorite / auto-sorted
        public string Tags { get; set; } = "";         // 逗號分隔
        public string Project { get; set; } = "DefaultProject";
        public int ScopeLocked { get; set; } = 0;      // 0/1：是否為目前專案鎖定
    }
}
