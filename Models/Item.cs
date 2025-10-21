using System;

namespace AI.KB.Assistant.Models
{
    public sealed class Item
    {
        public long Id { get; set; }
        public string Filename { get; set; } = string.Empty;
        public string Ext { get; set; } = string.Empty;

        // �~�����]�b Intake / Routing �|�Ψ�^
        public string Project { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double Confidence { get; set; } = 0d;
        public string Status { get; set; } = string.Empty;

        // �ɮ׸�T
        public long CreatedTs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public string Path { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;

        // IntakeService �|�Ψ쪺�Ȧs�������
        public string FileType { get; set; } = string.Empty;  // e.g. image/pdf/office/code�K
        public string ProposedPath { get; set; } = string.Empty;  // ��ĳ�h���ت��a
        public string Reasoning { get; set; } = string.Empty;  // �W�h/LLM �����׻���
    }
}
