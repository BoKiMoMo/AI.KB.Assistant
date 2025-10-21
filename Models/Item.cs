using System;

namespace AI.KB.Assistant.Models
{
    public sealed class Item
    {
        public long Id { get; set; }
        public string Filename { get; set; } = string.Empty;
        public string Ext { get; set; } = string.Empty;

        // 業務欄位（在 Intake / Routing 會用到）
        public string Project { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double Confidence { get; set; } = 0d;
        public string Status { get; set; } = string.Empty;

        // 檔案資訊
        public long CreatedTs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public string Path { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;

        // IntakeService 會用到的暫存說明欄位
        public string FileType { get; set; } = string.Empty;  // e.g. image/pdf/office/code…
        public string ProposedPath { get; set; } = string.Empty;  // 建議搬移目的地
        public string Reasoning { get; set; } = string.Empty;  // 規則/LLM 的推論說明
    }
}
