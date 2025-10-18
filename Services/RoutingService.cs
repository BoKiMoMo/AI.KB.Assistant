using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// �ھ� AppConfig �� Routing �]�w�A���ͼh�Ŧ��ت����|�C
    /// �w�]��ĳ���c�GYear / Project / Category(�y�N) / Type(���ɦW)
    /// </summary>
    public sealed class RoutingService
    {
        private static string SafeFolder(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "_";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>
        /// ���ͥت����|�]���t�ɦW�^�C�|�̷� RoutingSection ���}���M�w�h�šC
        /// </summary>
        public string BuildDestination(AppConfig cfg, string project, string category, string fileType, DateTime dt)
        {
            var parts = new List<string>();

            // Year
            if (cfg.Routing.EnableYear)
                parts.Add(dt.Year.ToString());

            // Project
            if (cfg.Routing.EnableProject)
                parts.Add(SafeFolder(string.IsNullOrWhiteSpace(project) ? "�@��M��" : project));

            // Category (�y�N/�~�Ȥ���)
            if (cfg.Routing.EnableCategory)
                parts.Add(SafeFolder(string.IsNullOrWhiteSpace(category) ? "������" : category));

            // Type (���ɦW�ڸs)
            if (cfg.Routing.EnableType)
                parts.Add(SafeFolder(NormalizeType(fileType)));

            // Month ��̫�]�i��^�A�q�` Year �w����
            if (cfg.Routing.EnableMonth)
                parts.Add(dt.Month.ToString("D2"));

            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : cfg.App.RootDir;

            var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
            return path;
        }

        /// <summary>
        /// �N���ɦW�k�����ڸs�A�p�Ϥ��B�v���B���B�]�p�B�{���X�K���C
        /// </summary>
        public static string NormalizeType(string extOrType)
        {
            var e = (extOrType ?? "").Trim().Trim('.').ToLowerInvariant();

            // �Y�w�g�O�ڸs�W�A�����^��
            var knownGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "�Ϥ�", "���", "���", "��v��", "�v��", "�]�p", "�V�q", "�M����", "�{���X", "���Y", "�¤�r", "PDF", "���" };

            if (knownGroups.Contains(e)) return e;

            // �u�����ɦW
            var ext = e;

            // �Ϥ�
            var img = new[] { "png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff", "webp", "heic", "svg" };
            if (img.Contains(ext)) return "�Ϥ�";

            // �v��
            var av = new[] { "mp4", "m4v", "mov", "avi", "mkv", "wmv", "mp3", "wav", "m4a", "flac", "aac", "ogg" };
            if (av.Contains(ext)) return "�v��";

            // ���/���/��v��/PDF
            if (ext is "doc" or "docx" or "rtf" or "md" or "txt") return ext == "txt" ? "�¤�r" : "���";
            if (ext is "xls" or "xlsx" or "csv" or "tsv") return "���";
            if (ext is "ppt" or "pptx" or "key") return "��v��";
            if (ext is "pdf") return "PDF";

            // �]�p/�V�q/�M����
            if (ext is "psd" or "ai" or "xd" or "fig" or "sketch") return "�]�p";
            if (ext is "svg" or "eps") return "�V�q";
            if (ext is "aep" or "prproj" or "aup" or "aup3") return "�M����";

            // �{���X
            var code = new[]
            { "cs","js","ts","jsx","tsx","py","rb","php","java","kt","go","rs","cpp","c","h","m","swift","dart","scala","sh","ps1","lua","sql","yml","yaml","toml","xml","json" };
            if (code.Contains(ext)) return "�{���X";

            // ���Y/�ʸ�
            if (ext is "zip" or "7z" or "rar" or "gz" or "tar" or "tgz") return "���Y";

            // ��L
            return "���";
        }
    }
}
