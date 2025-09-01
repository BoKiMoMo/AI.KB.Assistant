using System;
using System.IO;
using Newtonsoft.Json;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// ���ѳ]�w��Ū���P�x�s���A��
    /// </summary>
    public static class ConfigService
    {
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// �q���w���|���J�]�w�ɡ]�w�]�� config.json�^
        /// </summary>
        public static AppConfig Load(string path = "config.json")
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"�䤣��]�w�ɡG{path}");

            var json = File.ReadAllText(path);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _jsonSettings) ?? new();

            EnsureDirectories(cfg);
            return cfg;
        }

        /// <summary>
        /// ���ո��J�]�w�ɡA�Y���ѫh�^�ǹw�]��
        /// </summary>
        public static AppConfig TryLoad(string path = "config.json")
        {
            try
            {
                return Load(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService] ���J���ѡG{ex.Message}�A�N�ϥιw�]�ȡC");
                var cfg = new AppConfig();
                EnsureDirectories(cfg);
                return cfg;
            }
        }

        /// <summary>
        /// �x�s�]�w�ɨ���w���|
        /// </summary>
        public static void Save(string path, AppConfig cfg)
        {
            cfg ??= new AppConfig();

            // �T�O���h���� null
            cfg.App ??= new AppSection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();
            cfg.OpenAI ??= new OpenAISection();

            // �T�O�ت��a��Ƨ��s�b
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            // �g�^ JSON
            var json = JsonConvert.SerializeObject(cfg, _jsonSettings);
            File.WriteAllText(full, json);
        }

        /// <summary>
        /// �T�O�]�w�ɫ��w����Ƨ��s�b
        /// </summary>
        private static void EnsureDirectories(AppConfig cfg)
        {
            if (!string.IsNullOrWhiteSpace(cfg.App.DbPath))
            {
                var dbDir = Path.GetDirectoryName(cfg.App.DbPath);
                if (!string.IsNullOrEmpty(dbDir))
                    Directory.CreateDirectory(dbDir);
            }

            if (!string.IsNullOrWhiteSpace(cfg.App.RootDir))
                Directory.CreateDirectory(cfg.App.RootDir);

            if (!string.IsNullOrWhiteSpace(cfg.App.InboxDir))
                Directory.CreateDirectory(cfg.App.InboxDir);
        }
    }
}
