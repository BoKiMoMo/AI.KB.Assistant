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

        /// <summary>
        /// �D���[�ơG�D�e���Ψ���ܡu�w�p�h�B����Ӹ��|�v�����C
        /// ���|�g�J DB�A�� UI �w���C
        /// </summary>
        public string? PredictedPath { get; set; }
    }
}
