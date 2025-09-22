namespace AI.KB.Assistant.Models
{
    public sealed class Item
    {
        public string Path { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Category { get; set; } = "";
        public string FileType { get; set; } = "";   // �s�W�G���ɦW�]���t�I�^
        public double Confidence { get; set; }
        public long CreatedTs { get; set; }
        public int Year { get; set; }                 // �s�W�G�~
        public string Summary { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public string Status { get; set; } = "normal"; // normal / pending / in-progress / favorite / auto-sorted
        public string Tags { get; set; } = "";         // �r�����j
        public string Project { get; set; } = "DefaultProject";
        public int ScopeLocked { get; set; } = 0;      // 0/1�G�O�_���ثe�M����w
    }
}
