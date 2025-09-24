using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// �ĤT���q�G�� LLM ���y�N�����]�ثe�u�O stub�^
    /// </summary>
    public static class LlmService
    {
        public static Task<(string category, double confidence, string reason)> ClassifyAsync(Item item, AppConfig cfg)
        {
            // TODO: �걵 OpenAI API
            return Task.FromResult(("LLM����", 0.9, "Stub: �������G"));
        }
    }
}
