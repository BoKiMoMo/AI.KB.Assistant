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
    /// 封裝「分類」LLM；沒有 API Key 時會 fallback 到簡單規則（本地）。
    /// </summary>
    public class LlmService
    {
        private readonly AppConfig _cfg;
        private readonly HttpClient _http = new();

        public LlmService(AppConfig cfg) { _cfg = cfg; }

        public async Task<(string category, double confidence, string reason)> ClassifyAsync(string filename, string text)
        {
            // 沒金鑰 → 規則 fallback
            if (string.IsNullOrWhiteSpace(_cfg.OpenAI.ApiKey))
                return LocalRules(filename, text);

            try
            {
                var prompt = $@"
你是一個文件歸檔助理，請依檔名與內容判斷業務分類（category）。
只回 JSON：{{""category"":string,""confidence"":number,""reason"":string}}。

檔名: {filename}
內容摘要: {Shorten(text, 800)}
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
                var cat = obj["category"]?.ToString() ?? "其他";
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
                lower.Contains(".ppt") || lower.Contains(".pptx") ? "簡報" :
                lower.Contains(".pdf") ? "報告" :
                lower.Contains(".png") || lower.Contains(".jpg") || lower.Contains(".jpeg") ? "圖片" :
                lower.Contains(".xlsx") || lower.Contains(".csv") ? "數據" :
                lower.Contains("invoice") || lower.Contains("發票") ? "財務" :
                "其他";

            double conf =
                cat is "簡報" or "報告" or "圖片" or "數據" or "財務" ? 0.8 : 0.5;

            string reason = $"依檔名/副檔名與關鍵字判斷為「{cat}」。";
            return (cat, conf, reason);
        }
    }
}
