using System;

namespace AI.KB.Assistant.Models
{
    /// <summary>單一檔案紀錄模型。</summary>
    public class Item
    {
        public int Id { get; set; }

        public string Path { get; set; } = "";
        public string Filename { get; set; } = "";
        public string FileType { get; set; } = "";

        public string Category { get; set; } = "";
        public string Project { get; set; } = "";

        public double Confidence { get; set; }
        public string Reasoning { get; set; } = "";

        /// <summary>inbox, pending, auto-sorted, favorite, in-progress, blacklisted</summary>
        public string Status { get; set; } = "inbox";

        public string Tags { get; set; } = "";

        public long CreatedTs { get; set; } = DateTimeOffset.Now.ToUnixTimeSeconds();

        /// <summary>「預計搬到」的目的路徑（顯示用，不入庫）。</summary>
        public string ProposedPath { get; set; } = "";
    }
}
