using System;

namespace AI.KB.Assistant.Models
{
    public class Item
    {
        public int Id { get; set; }
        public string Path { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Category { get; set; } = "";
        public string FileType { get; set; } = ""; // ← 新增：副檔名/型態（同步維護）
        public string Tags { get; set; } = "";
        public string Status { get; set; } = "";
        public double Confidence { get; set; }
        public string Reason { get; set; } = "";
        public long CreatedTs { get; set; } // Unix seconds
        public int Year { get; set; }

        public string FullPath => System.IO.Path.Combine(Path, Filename);
    }
}
