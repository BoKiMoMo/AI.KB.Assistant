using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 第三階段：接 LLM 做語意分類（目前只是 stub）
    /// </summary>
    public static class LlmService
    {
        public static Task<(string category, double confidence, string reason)> ClassifyAsync(Item item, AppConfig cfg)
        {
            // TODO: 串接 OpenAI API
            return Task.FromResult(("LLM分類", 0.9, "Stub: 模擬結果"));
        }
    }
}
