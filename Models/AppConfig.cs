using System;
using System.IO;
using Newtonsoft.Json;

namespace AI.KB.Assistant.Models
{
    public class AppConfig
    {
        public string RootPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public string Project { get; set; } = "default";

        // Routing
        public string RoutingMode { get; set; } = "Copy"; // Copy | Move
        public bool AddYearFolder { get; set; } = true;

        // Classification
        public bool AutoClassify { get; set; } = false;
        public double ConfidenceThreshold { get; set; } = 0.6;

        // OpenAI / LLM
        public OpenAIConfig OpenAI { get; set; } = new();

        // Semantic Search
        public SemanticConfig Semantic { get; set; } = new();

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                var cfg = new AppConfig();
                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                return cfg;
            }
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
        }

        public void Save(string path)
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }

    public class OpenAIConfig
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
        public string EmbeddingModel { get; set; } = "text-embedding-3-small";
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public int MaxTokens { get; set; } = 500;
        public double Temperature { get; set; } = 0.2;
    }

    public class SemanticConfig
    {
        public bool Enabled { get; set; } = true;
        public int TopK { get; set; } = 1000;   // 拿多少筆進記憶體計算相似度
    }
}
