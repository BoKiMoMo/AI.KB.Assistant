// Services/ConfigService.cs
using System;
using System.IO;
using AI.KB.Assistant.Models;
using Newtonsoft.Json;

namespace AI.KB.Assistant.Services
{
    public static class ConfigService
    {
        // ��ĳ�βΤ@�]�w�G�Y�ơϩ��� null�B���\����
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static AppConfig TryLoad(string path = "config.json")
        {
            try
            {
                return Load(path);
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
                return new AppConfig();

            var json = File.ReadAllText(path);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _jsonSettings) ?? new AppConfig();

            // �T�O�U�϶����� null
            cfg.App ??= new AppSection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();
            cfg.OpenAI ??= new OpenAISection();

            return cfg;
        }

        public static void Save(string path, AppConfig cfg)
        {
            cfg ??= new AppConfig();

            // �T�O�U�϶����� null�]�קK�g�X�ӯʬq�^
            cfg.App ??= new AppSection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();
            cfg.OpenAI ??= new OpenAISection();

            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            var json = JsonConvert.SerializeObject(cfg, _jsonSettings);

            // �� UTF-8 (�t BOM) �g�X�H�קK����ýX
            using var sw = new StreamWriter(full, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            sw.Write(json);
        }
    }
}
