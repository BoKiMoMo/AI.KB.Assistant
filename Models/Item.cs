namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// 單一檔案的資料列
    /// </summary>
    public class Item
    {
        public int Id { get; set; }
        public string Path { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Category { get; set; } = "";
        public string Status { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Project { get; set; } = "";
        public string FileType { get; set; } = "";
        public double Confidence { get; set; }
        public long CreatedTs { get; set; }
    }
}
