using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;
using Newtonsoft.Json.Linq;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 語意嵌入與相似度；無 API Key 時提供可替代的本地 hash 向量，流程可完整運作。
    /// </summary>
    public class EmbeddingService
    {
        private readonly AppConfig _cfg;
        private readonly HttpClient _http = new();

        public EmbeddingService(AppConfig cfg) { _cfg = cfg; }

        public async Task<float[]> EmbedAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(_cfg.OpenAI.ApiKey))
                return FakeEmbed(text);

            try
            {
                var body = new
                {
                    model = _cfg.OpenAI.EmbeddingModel,
                    input = text
                };
                var req = new HttpRequestMessage(HttpMethod.Post, $"{_cfg.OpenAI.BaseUrl}/embeddings");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.OpenAI.ApiKey);
                req.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
                var json = JObject.Parse(await resp.Content.ReadAsStringAsync());

                var arr = (json["data"]?[0]?["embedding"] as JArray)?.Select(x => (float)x!).ToArray();
                if (arr is { Length: > 0 }) return arr!;
            }
            catch { /* fall through */ }

            return FakeEmbed(text);
        }

        public static double Cosine(float[] a, float[] b)
        {
            double dot = 0, na = 0, nb = 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            if (na == 0 || nb == 0) return 0;
            return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }

        // 極簡 hash 向量（無 Key 時使用，確保功能跑通）
        private static float[] FakeEmbed(string text)
        {
            const int dim = 128;
            var v = new float[dim];
            unchecked
            {
                int seed = 17;
                foreach (char c in text ?? "")
                    seed = seed * 31 + c;
                var rnd = new Random(seed);
                for (int i = 0; i < dim; i++)
                    v[i] = (float)(rnd.NextDouble() - 0.5);
            }
            return v;
        }

        public static string SerializeVector(float[] v) => string.Join(",", v.Select(x => x.ToString("G17")));
        public static float[] DeserializeVector(string s)
            => s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x)).ToArray();
    }
}
