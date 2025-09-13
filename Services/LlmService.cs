using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AI.KB.Assistant.Services
{
    /// <summary>���a�W�h�������]�ĤT���q�A���� LLM�^</summary>
    public sealed class LlmService
    {
        private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["�X��/�k��"] = new[] { "contract", "nda", "terms", "law", "legal", "�X��", "�k��", "����" },
            ["�]��/�b��"] = new[] { "invoice", "receipt", "tax", "bill", "����", "�b��", "�o��", "���|", "�д�" },
            ["²��/����"] = new[] { "ppt", "pptx", "key", "keynote", "deck", "²��", "����", "presentation" },
            ["���i/���"] = new[] { "report", "doc", "docx", "spec", "minutes", "�|ĳ����", "�W��", "���i" },
            ["�H��/�i��"] = new[] { "resume", "cv", "hr", "�i��", "����" },
            ["����/������"] = new[] { "purchase", "po", "vendor", "quote", "����", "������", "����" },

            ["�{���X/�M��"] = new[] { "cs", "ts", "js", "py", "java", "go", "rs", "cpp", "git", "source", "repo", "solution", "sln" },
            ["�]�p/UIUX"] = new[] { "fig", "figma", "sketch", "psd", "ai", "xd", "ui", "ux", "wireframe", "�]�p" },
            ["��s/���R"] = new[] { "paper", "thesis", "dataset", "benchmark", "analysis", "��s", "���R" },

            ["��P/����"] = new[] { "campaign", "ad", "edm", "��P", "����", "����" },
            ["�Ȥ�/�P��"] = new[] { "crm", "�ȶD", "sales", "proposal", "���" },

            ["�Ϥ�/�ۤ�"] = new[] { "jpg", "jpeg", "png", "heic", "raw", "tiff", "bmp", "photo", "image" },
            ["�v��"] = new[] { "mp4", "mov", "avi", "mkv", "wav", "mp3", "audio", "video" },
            ["�I��"] = new[] { "screenshot", "�I��", "screen shot" },

            ["���Y/�ʦs"] = new[] { "zip", "rar", "7z", "tar", "gz", "tgz" },
            ["�w�˥]/������"] = new[] { "exe", "msi", "dmg", "pkg", "appimage" },

            ["�ӤH���"] = new[] { "�ӤH", "private", "self" },
            ["�о�/�ҵ{"] = new[] { "lesson", "tutorial", "course", "�ҵ{", "�о�" },
        };

        public Task<(string category, double conf)> ClassifyLocalAsync(string fileName)
        {
            var name = fileName?.ToLowerInvariant() ?? "";
            var ext = System.IO.Path.GetExtension(name).Trim('.');

            foreach (var kv in Map)
            {
                if (kv.Value.Any(k =>
                        name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(ext) && string.Equals(ext, k, StringComparison.OrdinalIgnoreCase))))
                {
                    return Task.FromResult((kv.Key, 0.9));
                }
            }
            return Task.FromResult(("��L", 0.3));
        }
    }
}
