namespace AI.KB.Assistant.Models
{
    public class OpenAIConfig
    {
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
        public string EmbeddingModel { get; set; } = "text-embedding-3-small";
        public int MaxTokens { get; set; } = 800;
        public double Temperature { get; set; } = 0.2;
    }
}
