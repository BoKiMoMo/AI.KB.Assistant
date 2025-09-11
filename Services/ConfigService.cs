using System;
using System.IO;
using Newtonsoft.Json;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// �]�w�ɦs���A�ȡ]Ū/�g config.json�^
    /// </summary>
    public static class ConfigService
    {
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        /// <summary>
        /// Ū���]�w�F�Y�ɮפ��s�b�^�ǹw�]����C
        /// ���ѷ|�Y�X�ҥ~�C
        /// </summary>
        public static AppConfig Load(string path)
        {
            if (!File.Exists(path)) return new AppConfig();

            var json = File.ReadAllText(path);
            var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _jsonSettings) ?? new AppConfig();
            EnsureSections(cfg);
            return cfg;
        }

        /// <summary>
        /// ����Ū���]�w�F������~���^�ǹw�]����A���Y�ҥ~�C
        /// </summary>
        public static AppConfig TryLoad(string path)
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

        /// <summary>
        /// �x�s�]�w�� UTF-8 (BOM)�F�|�۰ʫإߥؿ��C
        /// </summary>
        public static void Save(string path, AppConfig cfg)
        {
            EnsureSections(cfg);

            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir!);

            var json = JsonConvert.SerializeObject(cfg, _jsonSettings);
            using var sw = new StreamWriter(full, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            sw.Write(json);
        }

        /// <summary>
        /// �T�O�U�l�϶����� null�]�קK���ɩΤ�ʽs��y���ŭȡ^
        /// </summary>
        private static void EnsureSections(AppConfig cfg)
        {
            cfg.App ??= new AppSection();
            cfg.OpenAI ??= new OpenAISection();
            cfg.Routing ??= new RoutingSection();
            cfg.Classification ??= new ClassificationSection();

            // ������쪺�O���ȡ]�קK�Ŧr��^
            cfg.Classification.ClassificationMode ??= "category";
            cfg.Classification.TimeGranularity ??= "month";
            cfg.Classification.FallbackCategory ??= "��L";
            cfg.App.MoveMode ??= "copy";
        }
    }
}
