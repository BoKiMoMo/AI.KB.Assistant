using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AI.KB.Assistant.Models;   // �ϥ� Models �̪� MoveMode / OverwritePolicy

namespace AI.KB.Assistant.Services
{
    public sealed class RoutingService
    {
        // --- enum �ѪR�]�e�Ԥj�p�g/�Ŧr��^ ---
        public static bool TryParseMoveMode(string? s, out MoveMode mode)
        {
            if (!string.IsNullOrWhiteSpace(s) && Enum.TryParse<MoveMode>(s, true, out var m)) { mode = m; return true; }
            mode = MoveMode.Move; return false;
        }
        public static bool TryParseOverwritePolicy(string? s, out OverwritePolicy p)
        {
            if (!string.IsNullOrWhiteSpace(s) && Enum.TryParse<OverwritePolicy>(s, true, out var v)) { p = v; return true; }
            p = OverwritePolicy.Rename; return false;
        }

        // --- �P�_���]�P�ɤ䴩 enum �P string�^ ---
        public bool IsMove(MoveMode mode) => mode == MoveMode.Move;
        public bool IsMove(string? mode) => TryParseMoveMode(mode, out var m) && IsMove(m);

        public bool IsReplace(OverwritePolicy p) => p == OverwritePolicy.Replace;
        public bool IsReplace(string? p) => TryParseOverwritePolicy(p, out var v) && IsReplace(v);

        // --- ���ɦW �� �a�� ---
        public string FamilyFromExt(string ext)
        {
            var e = (ext ?? "").Trim('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(e)) return "��L";

            string[] image = { "jpg", "jpeg", "png", "gif", "bmp", "tiff", "webp", "heic" };
            string[] video = { "mp4", "mov", "avi", "mkv", "wmv", "m4v" };
            string[] audio = { "mp3", "wav", "aac", "flac", "m4a", "ogg" };
            string[] office = { "doc", "docx", "xls", "xlsx", "ppt", "pptx", "pdf" };
            string[] adobe = { "psd", "ai", "ae", "prproj", "indd" };
            string[] code = { "cs", "js", "ts", "py", "java", "cpp", "h", "json", "xml", "yml", "yaml", "sql", "md" };

            if (image.Contains(e)) return "�Ϥ�";
            if (video.Contains(e)) return "�v��";
            if (audio.Contains(e)) return "���T";
            if (office.Contains(e)) return "Office";
            if (adobe.Contains(e)) return "Adobe";
            if (code.Contains(e)) return "�{���X";
            return "��L";
        }

        // --- ���ɦW�q�M�ס]�ܽd�A�i�ۦ��X�R�^ ---
        public string GuessProjectByName(string? filename)
        {
            var name = (filename ?? "").ToLowerInvariant();
            if (name.Contains("2025") && name.Contains("launch")) return "2025Launch";
            if (name.Contains("spec") || name.Contains("�W��")) return "���~�W��";
            return "";
        }

        // --- ���O���סG�^�� (category, reason)�]�M IntakeService ����^ ---
        public (string category, string reason) GuessCategoryByKeyword(string filename, string family)
        {
            var name = (filename ?? "").ToLowerInvariant();

            if (name.Contains("invoice") || name.Contains("�o��")) return ("�]��", "�ɦW�t invoice/�o��");
            if (name.Contains("contract") || name.Contains("�X��")) return ("�X��", "�ɦW�t contract/�X��");
            if (name.Contains("proposal") || name.Contains("����")) return ("����", "�ɦW�t proposal/����");
            if (name.Contains("spec") || name.Contains("�W��")) return ("�W��", "�ɦW�t spec/�W��");

            return (family, $"�̰��ɦW�a�ڱ��w�� {family}");
        }

        // --- �ت��a���|�]��ئh���A��K�I�s�^ ---
        public string BuildDestination(AppConfig cfg, string project, string category, string ext, DateTime when)
            => BuildDestinationInternal(cfg, project, category, ext, when);

        public string BuildDestination(AppConfig cfg, string project, string category, string ext, DateTime when, MoveMode _)
            => BuildDestinationInternal(cfg, project, category, ext, when);
        public string BuildDestination(AppConfig cfg, string project, string category, string ext, DateTime when, string? __)
            => BuildDestinationInternal(cfg, project, category, ext, when);

        private string BuildDestinationInternal(AppConfig cfg, string project, string category, string ext, DateTime when)
        {
            var root = string.IsNullOrWhiteSpace(cfg.App.RootDir)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : cfg.App.RootDir;

            var parts = new List<string> { root };
            if (cfg.Routing.EnableYear) parts.Add(when.Year.ToString("0000"));
            if (cfg.Routing.EnableMonth) parts.Add(when.ToString("MM", CultureInfo.InvariantCulture));
            if (cfg.Routing.EnableProject)
                parts.Add(string.IsNullOrWhiteSpace(project) ? cfg.Classification.FallbackCategory : project);

            var cat = string.IsNullOrWhiteSpace(category) ? cfg.Routing.AutoFolderName : category;
            if (cfg.Routing.EnableType) parts.Add(cat);

            var filename = $"{when:yyyyMMdd_HHmmss}.{(ext ?? "dat").Trim('.')}";
            var dir = Path.Combine(parts.ToArray());
            return Path.Combine(dir, filename);
        }

        // --- ���W�����GReplace / Rename ---
        public string WithAutoRename(string dest, OverwritePolicy policy)
            => policy == OverwritePolicy.Replace ? dest : NextAvailableFilename(dest);
        public string WithAutoRename(string dest, string? policy)
            => IsReplace(policy) ? dest : NextAvailableFilename(dest);

        private static string NextAvailableFilename(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                var cand = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(cand)) return cand;
            }
        }
    }
}
