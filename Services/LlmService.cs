using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class LlmService
    {
        private readonly AppConfig _cfg;

        // ²������r�W�h�]fallback / local �����Ρ^
        private readonly Dictionary<string, string[]> _keywordMap = new()
        {
            ["�|ĳ"] = new[] { "�|ĳ", "meeting", "minutes" },
            ["���i"] = new[] { "���i", "report" },
            ["�]��"] = new[] { "�]��", "�o��", "invoice", "�b��" },
            ["�X��"] = new[] { "�X��", "����", "contract" },
            ["�H��"] = new[] { "�H��", "hr", "resume", "�i��" },
            ["��s"] = new[] { "��s", "paper", "thesis", "analysis" },
            ["�]�p"] = new[] { "�]�p", "design", "ui", "ux", "figma", "psd" },
            ["²��"] = new[] { "²��", "slides", "presentation", "ppt" },
            ["��P"] = new[] { "��P", "marketing", "campaign" },
            ["�k��"] = new[] { "�k��", "�k��", "legal" },
            ["�޳N"] = new[] { "�޳N", "code", "�{��", "source", "git" },
            ["�Ϥ�"] = new[] { "�Ϥ�", "image", "photo", "jpg", "png" },
            ["�v��"] = new[] { "�v��", "video", "audio", "mp3", "mp4", "mov" },
            ["���Y"] = new[] { "���Y", "zip", "rar", "7z" },
            ["�ӤH���"] = new[] { "�ӤH", "private", "self" },
            ["�о�/�ҵ{"] = new[] { "�о�", "�ҵ{", "lesson", "class", "tutorial" },
            ["����/������"] = new[] { "����", "������", "�ĶR", "vendor" },
            ["��L"] = Array.Empty<string>()
        };

        public LlmService(AppConfig cfg) => _cfg = cfg;

        /* =================== A1: ���� =================== */
        public async Task<(string category, double confidence, string summary, string reasoning)>
            ClassifyAsync(string filename, string? content = null, CancellationToken ct = default)
        {
            // �Y�S�} LLM �ΨS API Key �� �ϥΥ��a�W�h
            if (!_cfg.Classification.UseLLM || string.IsNullOrWhiteSpace(_cfg.OpenAI.ApiKey))
                return await Task.FromResult(RuleBasedClassify(filename + " " + (content ?? "")));

            try
            {
                // TODO: �� OpenAI Completions�]�O�d���f�^
                // �o�̥��H�u�j�ƪ��W�h�v�����A�õ����ũM���H�߭�
                var local = RuleBasedClassify(filename + " " + (content ?? ""));
                var boosted = Math.Min(0.95, Math.Max(0.55, local.confidence + 0.1));
                return (local.category, boosted, local.summary, "LLM �����]�����w�d�^");
            }
            catch
            {
                // ���� �� fallback
                return RuleBasedClassify(filename + " " + (content ?? ""));
            }
        }

        private (string category, double confidence, string summary, string reasoning)
            RuleBasedClassify(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (_cfg.Classification.FallbackCategory, 0.3, "", "�ťտ�J");

            var lower = text.ToLowerInvariant();
            foreach (var kv in _keywordMap)
            {
                if (kv.Value.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return (kv.Key, 0.85, $"�۰ʧP�w�� {kv.Key}", $"����r�R���G{kv.Key}");
            }
            return (_cfg.Classification.FallbackCategory, 0.5, $"�k���� {_cfg.Classification.FallbackCategory}", "���R���W�h");
        }

        /* =================== A2: �K�n�P���� =================== */
        public async Task<string> SummarizeAsync(string filename, string? content = null, CancellationToken ct = default)
        {
            var baseName = System.IO.Path.GetFileNameWithoutExtension(filename);
            if (!_cfg.Classification.UseLLM || string.IsNullOrWhiteSpace(_cfg.OpenAI.ApiKey))
            {
                // ���a²�ƺK�n
                var s = (content ?? baseName);
                s = s.Length > 40 ? s[..40] + "..." : s;
                return await Task.FromResult(s);
            }

            try
            {
                // TODO: �� OpenAI�F�ȥ�²��
                var s = (content ?? baseName);
                s = s.Length > 50 ? s[..50] + "..." : s;
                return await Task.FromResult(s);
            }
            catch
            {
                var s = (content ?? baseName);
                return s.Length > 40 ? s[..40] + "..." : s;
            }
        }

        public async Task<string[]> SuggestTagsAsync(string filename, string category, string? summary, CancellationToken ct = default)
        {
            // ���a�G�δX�Ӥw���� + ���O�A�åh��
            var seed = new List<string>();
            if (!string.IsNullOrWhiteSpace(category)) seed.Add(category);

            var lower = filename.ToLowerInvariant();
            foreach (var kv in _keywordMap)
            {
                if (kv.Value.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase))) seed.Add(kv.Key);
            }

            if (!string.IsNullOrWhiteSpace(summary))
            {
                var words = summary.Split(new[] { ' ', '�@', ',', '�A', '/', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Where(w => w.Length >= 2 && w.Length <= 8)
                                   .Take(8);
                seed.AddRange(words);
            }

            var tags = seed.Select(NormalizeTag)
                           .Where(s => !string.IsNullOrWhiteSpace(s))
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .Take(Math.Max(1, Math.Min(10, _cfg.Classification.MaxTags)))
                           .ToArray();

            return await Task.FromResult(tags);

            static string NormalizeTag(string s)
            {
                s = s.Trim();
                if (s.Length > 12) s = s[..12];
                return s.Replace("#", "").Replace(";", "�B");
            }
        }

        /* =================== A3: ��ܷj�M�ѪR =================== */
        public async Task<(string? keyword, string[]? categories, string[]? tags, long? from, long? to)>
            ParseQueryAsync(string question, CancellationToken ct = default)
        {
            // ²�ƪ��ѪR���]���I�s LLM �]��ʡ^
            // �䴩�y�y�G�W�Ӥ�B����B���~�B�h�~�F�t�u�|ĳ/���i/�X���v�����M�����O�F#����
            question = (question ?? "").Trim();

            string? keyword = null;
            var cats = new List<string>();
            var tgs = new List<string>();
            long? from = null;
            long? to = null;

            // ���ҡG#xxx
            foreach (var token in question.Split(' ', '�@'))
            {
                if (token.StartsWith("#") && token.Length > 1) tgs.Add(token[1..]);
            }

            // ���O���J����
            foreach (var kv in _keywordMap.Keys)
            {
                if (question.Contains(kv, StringComparison.OrdinalIgnoreCase)) cats.Add(kv);
            }
            cats = cats.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // ����y�N�]����B�W�Ӥ�B���~�B�h�~�^
            var now = DateTimeOffset.Now;
            if (question.Contains("�W�Ӥ�") || question.Contains("�W��"))
            {
                var d = now.AddMonths(-1);
                var fromDt = new DateTime(d.Year, d.Month, 1);
                var toDt = fromDt.AddMonths(1).AddSeconds(-1);
                from = new DateTimeOffset(fromDt).ToUnixTimeSeconds();
                to = new DateTimeOffset(toDt).ToUnixTimeSeconds();
            }
            else if (question.Contains("����") || question.Contains("�o�Ӥ�"))
            {
                var fromDt = new DateTime(now.Year, now.Month, 1);
                var toDt = fromDt.AddMonths(1).AddSeconds(-1);
                from = new DateTimeOffset(fromDt).ToUnixTimeSeconds();
                to = new DateTimeOffset(toDt).ToUnixTimeSeconds();
            }
            else if (question.Contains("���~"))
            {
                var fromDt = new DateTime(now.Year, 1, 1);
                var toDt = new DateTime(now.Year, 12, 31, 23, 59, 59);
                from = new DateTimeOffset(fromDt).ToUnixTimeSeconds();
                to = new DateTimeOffset(toDt).ToUnixTimeSeconds();
            }
            else if (question.Contains("�h�~"))
            {
                var y = now.Year - 1;
                var fromDt = new DateTime(y, 1, 1);
                var toDt = new DateTime(y, 12, 31, 23, 59, 59);
                from = new DateTimeOffset(fromDt).ToUnixTimeSeconds();
                to = new DateTimeOffset(toDt).ToUnixTimeSeconds();
            }

            // ����r�G�h�� #���� ��Ѿl����r
            var raw = string.Join(" ",
                question.Split(' ', '�@').Where(t => !t.StartsWith("#")));
            keyword = string.IsNullOrWhiteSpace(raw) ? null : raw;

            return await Task.FromResult((keyword, cats.Count > 0 ? cats.ToArray() : null,
                                          tgs.Count > 0 ? tgs.ToArray() : null, from, to));
        }
    }
}
