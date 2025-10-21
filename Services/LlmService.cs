using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// LLM �A�ȡG�ثe���إ��a�ҵo�� + �D�P�B�����C
    /// �Y�N�ӭn�걵 OpenAI�A�u�n�b�����������������Y�i�]�������ܡ^�C
    /// </summary>
    public sealed class LlmService : IDisposable
    {
        private readonly AppConfig _cfg;

        public LlmService(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();
        }

        /// <summary>�O�_��ƶ��� API Key�]�� UI ��ܪ��A�Ρ^�C</summary>
        public bool IsReady => !string.IsNullOrWhiteSpace(_cfg.OpenAI?.ApiKey);

        /// <summary>
        /// �̦h���ɦW���X�u�i�઺�M�סv��ĳ�M��]������r�P�@�P token �����^�C
        /// </summary>
        public Task<List<string>> SuggestProjectNamesAsync(IEnumerable<string> filenames, CancellationToken ct)
        {
            var list = (filenames ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (list.Count == 0) return Task.FromResult(new List<string>());

            // ���`�� token
            var tokens = list
                .SelectMany(name =>
                    (name ?? "")
                        .ToLowerInvariant()
                        .Split(new[] { ' ', '_', '-', '.', '[', ']', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(t => t.Length >= 2 && t.Length <= 24)
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .Take(6)
                .Select(g => g.Key)
                .ToList();

            // �@�Ǳ`���M�׫�ĳ
            var commons = new[] { "AI�M��", "�]�p�Z", "�|ĳ�O��", "���פ��", "�������", "�ӤH���O" };

            var result = new List<string>();
            result.AddRange(tokens.Select(ToTitle));
            result.AddRange(commons);

            // �h���B�O�d����
            var dedup = result.Where(s => !string.IsNullOrWhiteSpace(s))
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Take(10)
                              .ToList();

            return Task.FromResult(dedup);
        }

        /// <summary>
        /// ��Ҥ����G��J�ثe�M��/�����A�^�� (project, category, reasoning)�C
        /// </summary>
        public async Task<(string project, string category, string reasoning)> RefineAsync(
            string filename, string currentProject, string currentCategory, CancellationToken ct)
        {
            // �����D�P�B�]�Y�N�Ӧ궳�� API�A�O�d await �����Y�i�^
            await Task.Delay(50, ct);

            // �A�ת��ҵo���L��
            var proj = string.IsNullOrWhiteSpace(currentProject)
                ? GuessProjectFromName(filename)
                : currentProject;

            var cat = string.IsNullOrWhiteSpace(currentCategory)
                ? GuessCategoryFromName(filename)
                : currentCategory;

            var reason = $"���ɦW�u{filename}�v�P�J���]�w�A�����M�סG{proj}�B�����G{cat}�C";
            return (proj, cat, reason);
        }

        /// <summary>
        /// ���տ�X��������]�Y����n�Φb�۰ʹw�����i�u�Ρ^�A���H�ߤ��ƻP�z�ѡC
        /// </summary>
        public Task<(string Project, string Category, double Confidence, string Reason)> TryClassifyAsync(
            string filename, CancellationToken ct)
        {
            var project = GuessProjectFromName(filename);
            var category = GuessCategoryFromName(filename);
            var reason = $"������r�P�`���Ҧ������F�ɦW�G{filename}";
            const double confidence = 0.72; // �P AppConfig �w�]���e���
            return Task.FromResult((project, category, confidence, reason));
        }

        // === helpers ===

        private static string GuessProjectFromName(string filename)
        {
            var f = (filename ?? "").ToLowerInvariant();
            if (f.Contains("ai")) return "AI�M��";
            if (f.Contains("design") || f.Contains("ui") || f.Contains("ux")) return "�]�p�Z";
            if (f.Contains("meeting") || f.Contains("minutes")) return "�|ĳ�O��";
            if (f.Contains("proposal") || f.Contains("pitch")) return "���פ��";

            var token = f.Split(new[] { ' ', '_', '-', '.', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
                         .FirstOrDefault();
            return string.IsNullOrWhiteSpace(token) ? "������" : ToTitle(token);
        }

        private static string GuessCategoryFromName(string filename)
        {
            var f = (filename ?? "").ToLowerInvariant();
            if (f.Contains("invoice") || f.Contains("�o��")) return "�]��";
            if (f.Contains("contract") || f.Contains("�X��")) return "�X��";
            if (f.Contains("resume") || f.Contains("�i��")) return "�i��";
            if (f.Contains("report") || f.Contains("���i")) return "���i";
            if (f.Contains("proposal") || f.Contains("����")) return "����";
            if (f.Contains("spec") || f.Contains("�W��")) return "�W��";
            return "�@��";
        }

        private static string ToTitle(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return token;
            if (token.Length == 1) return token.ToUpperInvariant();
            return char.ToUpperInvariant(token[0]) + token.Substring(1);
        }

        public void Dispose()
        {
            // �ثe�L������귽�F�Y�� OpenAI HttpClient �i�b���B�z
        }
    }
}
