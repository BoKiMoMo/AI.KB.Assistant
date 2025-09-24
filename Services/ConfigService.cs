using System;
using System.IO;
using Newtonsoft.Json;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 管理設定檔（config.json）的讀寫
    /// </summary>
    public static class ConfigService
    {
        public static AppConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var cfg = JsonConvert.DeserializeObject<AppConfig>(json);
                    return cfg ?? new AppConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"讀取設定檔失敗：{ex.Message}");
            }
            return new AppConfig();
        }

        public static void Save(string path, AppConfig cfg)
        {
            try
            {
                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"寫入設定檔失敗：{ex.Message}");
            }
        }
    }
}
