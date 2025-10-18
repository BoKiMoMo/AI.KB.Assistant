using System;

namespace AI.KB.Assistant.Models
{
    /// <summary>��@�ɮ׬����ҫ��C</summary>
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

        /// <summary>�u�w�p�h��v���ت����|�]��ܥΡA���J�w�^�C</summary>
        public string ProposedPath { get; set; } = "";
    }
}
