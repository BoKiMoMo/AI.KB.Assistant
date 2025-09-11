using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>���a�W�h�����F�O�d LLM ���f���ӱ� OpenAI</summary>
    public sealed class LlmService
    {
        private readonly AppConfig _cfg;

        private readonly Dictionary<string, string[]> _keywordMap = new()
        {
            ["�|ĳ"] = new[] { "�|ĳ", "meeting", "minutes" },
            ["���i"] = new[] { "���i", "report" },
            ["�]��"] = new[] { "�]��", "�o��", "invoice", "receipt", "�b��", "bill" },
            ["�X��"] = new[] { "�X��", "����", "contract" },
            ["�H��"] = new[] { "�H��", "hr", "resume", "�i��" },
            ["��s"] = new[] { "��s", "paper", "thesis", "analysis" },
            ["�]�p"] = new[] { "�]�p", "design", "ui", "ux", "figma", "psd" },
            ["²��"] = new[] { "²��", "slides", "presentation", "ppt" },
            ["��P"] = new[] { "��P", "marketing", "campaign" },
            ["�k��"] = new[] { "�k��", "�k��", "legal", "compliance" },
            ["�޳N"] = new[] { "�޳N", "code", "�{��", "source", "git" },
            ["�Ϥ�"] = new[] { "�Ϥ�", "image", "photo", "jpg", "jpeg", "png" },
            ["�v��"] = new[] { "�v��", "video", "audio", "mp3", "mp4", "mov" },
            ["���Y"] = new[] { "���Y", "zip", "rar", "7z" },
            ["�ӤH���"] = new[] { "�ӤH", "private", "self" },
            ["����/������"] = new[] { "����", "������", "purchase", "vendor" },
            ["�о�/�ҵ{"] = new[] { "�о�", "�ҵ{", "lesson", "class", "tutorial" },
            ["��L"] = Array.Empty<string>()
        };

        public LlmService(AppConfig cfg) => _cfg = cfg;

        public async Task<(string primary_category, double confidence, string summary, string reasoning)>
            ClassifyAsync(string text)
        {
            // �ثe�u�γW�h�F�d LLM ���f�����X�R
            return await Task.FromResult(RuleBasedClassify(text));
        }

        private (string primary_category, double confidence, string summary, string reasoning)
            RuleBasedClassify(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (_cfg.Classification.FallbackCategory, 0.0, "", "�ťտ�J");

            var t = text.ToLowerInvariant();
            foreach (var kv in _keywordMap)
            {
                if (kv.Value.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return (kv.Key, 0.9, $"�۰ʧP�w�� {kv.Key}", $"����r�R���G{kv.Key}");
            }

            return (_cfg.Classification.FallbackCategory, 0.3,
                    $"�k���� {_cfg.Classification.FallbackCategory}", "���R������W�h");
        }
    }
}
