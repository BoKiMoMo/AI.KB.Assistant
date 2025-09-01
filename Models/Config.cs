using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    public class AppConfig
    {
        public AppSection App { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
    }

    public class AppSection
    {
        public string RootDir { get; set; } = @"C:\AIKB\Root";
        public string InboxDir { get; set; } = @"C:\AIKB\Inbox";
        public string DbPath { get; set; } = @"C:\AIKB\data\kb.db";
        public bool DryRun { get; set; } = true;
        /// <summary>move / copy</summary>
        public string MoveMode { get; set; } = "move";
        /// <summary>overwrite / skip / rename</summary>
        public string Overwrite { get; set; } = "rename";
    }

    public class RoutingSection
    {
        /// <summary>
        /// �d�ҡG{Root}\{YYYY}\{MM}\{Category}\{Filename}
        /// �i�� token�G{Root} {YYYY} {MM} {DD} {Category} {Filename}
        /// </summary>
        public string PathTemplate { get; set; } = @"{Root}\{YYYY}\{MM}\{Category}\{Filename}";

        /// <summary>
        /// �Y�� true�A�����W�ٷ|�Φw���r���զW��]�קK���|�D�k�r���^
        /// </summary>
        public bool SafeCategories { get; set; } = true;
    }

    public class ClassificationSection
    {
        /// <summary>llm / dummy / hybrid�K�]�۩w�q�^</summary>
        public string Engine { get; set; } = "llm";
        /// <summary>����δ��ܵ�</summary>
        public string Style { get; set; } = string.Empty;
        /// <summary>0~1</summary>
        public double ConfidenceThreshold { get; set; } = 0.6;
        /// <summary>�������Ѫ��ƴ����O</summary>
        public string FallbackCategory { get; set; } = "Unsorted";
        /// <summary>�ۭq�����M��</summary>
        public List<string> CustomTaxonomy { get; set; } = new();
    }

    public class OpenAISection
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4o-mini";
    }
}
