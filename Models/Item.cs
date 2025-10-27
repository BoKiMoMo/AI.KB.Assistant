using System.Diagnostics;

namespace AI.KB.Assistant.Models
{
    [DebuggerDisplay("{Filename} ({Project}) [{Tags}]")]
    public sealed class Item
    {
        public long Id { get; set; }                 // DB: items.id
        public string? Path { get; set; }            // DB: items.path
        public string? Filename { get; set; }        // DB: items.filename
        public string? Ext { get; set; }             // DB: items.ext
        public string? Project { get; set; }         // DB: items.project
        public string? Category { get; set; }        // DB: items.category
        public string? Tags { get; set; }            // DB: items.tags
        public string? Status { get; set; }          // DB: items.status
        public double Confidence { get; set; }       // DB: items.confidence
        public long CreatedTs { get; set; }          // DB: items.created_ts (Unix seconds)
        public string? ProposedPath { get; set; }

        /// <summary>
        /// 非持久化：主畫面用來顯示「預計搬運到哪個路徑」的欄位。
        /// 不會寫入 DB，純 UI 預覽。
        /// </summary>
        public string? PredictedPath { get; set; }
    }
}
