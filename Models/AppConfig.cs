// ���|�GAI.KB.Assistant/Models/AppConfig.cs
using System.Collections.Generic;

namespace AI.KB.Assistant.Models
{
    /// <summary>
    /// ����]�w�]���� config.json�^
    /// </summary>
    public sealed class AppConfig
    {
        public AppSection App { get; set; } = new();
        public RoutingSection Routing { get; set; } = new();
        public ClassificationSection Classification { get; set; } = new();
        public OpenAISection OpenAI { get; set; } = new();
    }

    /// <summary>
    /// �@�����γ]�w
    /// </summary>
    public sealed class AppSection
    {
        /// <summary>�ڥؿ��]��ڷh�ɥؼЪ� root�^</summary>
        public string RootDir { get; set; } = "";
        /// <summary>����X�]���/�פJ�ӷ��^</summary>
        public string InboxDir { get; set; } = "";
        /// <summary>SQLite DB �ɮק�����|</summary>
        public string DbPath { get; set; } = "data/knowledge.db";

        /// <summary>move / copy</summary>
        public string MoveMode { get; set; } = "move";
        /// <summary>overwrite / skip / rename</summary>
        public string Overwrite { get; set; } = "rename";

        /// <summary>���]�]�u�������h�ɡ^</summary>
        public bool DryRun { get; set; } = true;

        // === �A�ثe�ʤ֪��ݩ� ===
        /// <summary>��������]�������/���O����/�M�פ����^</summary>
        public string ClassificationMode { get; set; } = "category";

        /// <summary>�ثe�M�צW�١]�U�Ԧ��h�M�׺޲z�|�Ψ�^</summary>
        public string ProjectName { get; set; } = "DefaultProject";

        /// <summary>�ɶ��ɫס]��B��B�~�A�v�T��Ƨ��h�š^</summary>
        public string TimeGranularity { get; set; } = "month";
    }


    /// <summary>
    /// ���|�P�R�W����
    /// </summary>
    public sealed class RoutingSection
    {
        /// <summary>
        /// �ҡG{root}/{category}/{yyyy}/{mm}/
        /// �i�����G{root},{category},{yyyy},{mm},{dd}
        /// </summary>
        public string PathTemplate { get; set; } = "{root}/{category}/{yyyy}/{mm}/";

        /// <summary>�Ȥ��\�w���M�����</summary>
        public bool SafeCategories { get; set; } = false;
    }

    /// <summary>
    /// AI/�W�h�����]�w
    /// </summary>
    public sealed class ClassificationSection
    {
        /// <summary>���������Gllm / rules / hybrid / dummy</summary>
        public string Engine { get; set; } = "rules";

        /// <summary>���ܭ���]�i�d�ա^</summary>
        public string Style { get; set; } = "topic";

        /// <summary>�H�ߪ��e�]0~1�^</summary>
        public double ConfidenceThreshold { get; set; } = 0.6;

        /// <summary>���ѥ��Ѯɪ��w�]���O</summary>
        public string FallbackCategory { get; set; } = "unsorted";

        /// <summary>�ۭq�����M��</summary>
        public List<string>? CustomTaxonomy { get; set; } = new()
        {
            "�M��/�|ĳ", "�ݨD/����", "�}�o/�{��", "�]�p/UIUX", "����/�禬",
            "��s/�פ�", "���/�ƾ�", "�k�W/����", "����/����", "�X��/�k��",
            "�]�|/�o��", "�H��/��F", "²��/��v��", "�Ϥ�/����", "�v��/�h�C��",
            "��U/����", "�о�/�ҵ{", "������/�峹", "��P/���", "�䥦"
        };
    }

    /// <summary>
    /// OpenAI �s�u�]�w
    /// </summary>
    public sealed class OpenAISection
    {
        /// <summary>OpenAI API Key�]�d�Ū�ܰ��� LLM�^</summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>�ҫ��W�١G�p gpt-4o-mini / gpt-4o / o3-mini / gpt-4.1 ��</summary>
        public string Model { get; set; } = "gpt-4o-mini";
    }
}
