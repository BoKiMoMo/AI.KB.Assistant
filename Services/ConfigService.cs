using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public static class ConfigService
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public static AppConfig TryLoad(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();

                    // 與 AppConfig.Load 的相容邏輯保持一致
                    if (!string.IsNullOrWhiteSpace(cfg.Import._compatSingleFolderName) &&
                        (cfg.Import.BlacklistFolderNames == null || cfg.Import.BlacklistFolderNames.Length == 0))
                    {
                        cfg.Import.BlacklistFolderNames = new[] { cfg.Import._compatSingleFolderName! };
                    }
                    if (cfg.Classification._compatUseLlm.HasValue && cfg.Classification._compatUseLlm.Value == false)
                        cfg.OpenAI.EnableWhenLowConfidence = false;
                    if (cfg.OpenAI._compatEnable.HasValue)
                        cfg.OpenAI.EnableWhenLowConfidence = cfg.OpenAI._compatEnable.Value;

                    return cfg;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"載入設定檔錯誤: {ex.Message}");
            }
            return new AppConfig();
        }

        public static void Save(string path, AppConfig config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, Options);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"儲存設定檔錯誤: {ex.Message}");
            }
        }
    }
}
