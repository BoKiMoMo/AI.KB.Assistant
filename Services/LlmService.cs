using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;
using Newtonsoft.Json.Linq;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// �ʸˡu�����vLLM�F�S�� API Key �ɷ| fallback ��²��W�h�]���a�^�C
    /// </summary>
    public class LlmService
    {
        private readonly AppConfig _cfg;
        private readonly HttpClient _http = new();

        public LlmService(AppConfig cfg) { _cfg = cfg; }

        public async Task<(string category, double confidence, string reason)> ClassifyAsync(string filename, string text)
        {
            // �S���_ �� �W�h fallback
            if (string.IsNullOrWhiteSpace(_cfg.OpenAI.ApiKey))
                return LocalRules(filename, text);

            try
            {
                var prompt = $@"
�A�O�@�Ӥ���k�ɧU�z�A�Ш��ɦW�P���e�P�_�~�Ȥ����]category�^�C
�u�^ JSON�G{{""category"":string,""confidence"":number,""reason"":string}}�C

�ɦW: {filename}
���e�K�n: {Shorten(text, 800)}
";
                var body = new
                {
                    model = _cfg.OpenAI.Model,
                    messages = new object[]
                    {
                        new { role = "system", content = "You categorize documents." },
                        new { role = "user", content = prompt }
                    },
                    temperature = _cfg.OpenAI.Temperature,
                    max_tokens = _cfg.OpenAI.MaxTokens
                };

                var req = new HttpRequestMessage(HttpMethod.Post, $"{_cfg.OpenAI.BaseUrl}/chat/completions");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.OpenAI.ApiKey);
                req.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                var content = json["choices"]?[0]?["message"]?["content"]?.ToString() ?? "{}";

                var obj = JObject.Parse(content);
                var cat = obj["category"]?.ToString() ?? "��L";
                var conf = obj["confidence"]?.ToObject<double?>() ?? 0.5;
                var reason = obj["reason"]?.ToString() ?? "";

                return (cat.Trim(), Math.Clamp(conf, 0, 1), reason.Trim());
            }
            catch
            {
                return LocalRules(filename, text);
            }
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max);
        }

        private static (string, double, string) LocalRules(string filename, string text)
        {
            string lower = (filename + " " + text).ToLowerInvariant();
            string cat =
                lower.Contains(".ppt") || lower.Contains(".pptx") ? "²��" :
                lower.Contains(".pdf") ? "���i" :
                lower.Contains(".png") || lower.Contains(".jpg") || lower.Contains(".jpeg") ? "�Ϥ�" :
                lower.Contains(".xlsx") || lower.Contains(".csv") ? "�ƾ�" :
                lower.Contains("invoice") || lower.Contains("�o��") ? "�]��" :
                "��L";

            double conf =
                cat is "²��" or "���i" or "�Ϥ�" or "�ƾ�" or "�]��" ? 0.8 : 0.5;

            string reason = $"���ɦW/���ɦW�P����r�P�_���u{cat}�v�C";
            return (cat, conf, reason);
        }
    }
}
