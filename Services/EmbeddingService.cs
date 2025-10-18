using System.Threading.Tasks;

namespace AI.KB.Assistant.Services
{
    public class EmbeddingService
    {
        public Task<float[]> GetEmbeddingAsync(string text)
        {
            // TODO: 串接 Embedding；先回傳固定向量長度 8
            return Task.FromResult(new float[] { 0, 1, 0, 1, 0, 1, 0, 1 });
        }
    }
}
