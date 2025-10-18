using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// LLM �����A�ȼh�C�������u�]�w�w���v�P�u�}���O�@�v�����A
    /// �S�� API Key �Υ��ҥήɡA�Ҧ���~��k�Ҧ^�ǪŶ��X/���i��I�s�C
    /// </summary>
    public sealed class LlmService : IDisposable
    {
        private readonly AppConfig _cfg;

        private readonly bool _enabled;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        public LlmService(AppConfig cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));

            // OpenAI �Ϭq�i�ण�s�b�A�����H null-safe �覡����
            _enabled = cfg.OpenAI?.EnableWhenLowConfidence ?? false;
            _apiKey = cfg.OpenAI?.ApiKey ?? string.Empty;

            // �w�]���I�P�ҫ�
            _baseUrl = string.IsNullOrWhiteSpace(cfg.OpenAI?.BaseUrl)
                ? "https://api.openai.com/v1"
                : cfg.OpenAI!.BaseUrl!;
            _model = string.IsNullOrWhiteSpace(cfg.OpenAI?.Model)
                ? "gpt-4o-mini"
                : cfg.OpenAI!.Model!;
        }

        public void Dispose()
        {
            // �Y���Ӧ� HttpClient ���귽�A�󦹳B����
        }

        /// <summary>
        /// ���ɦW�M��A���X�u�i�઺�M�צW�١v��ĳ�C
        /// ���ҥΩεL APIKey �ɡA�^�ǪŶ��X�C
        /// </summary>
        public async Task<string[]> SuggestProjectNamesAsync(string[] filenames, CancellationToken ct)
        {
            // �w���}���G�S�ҥΩΨS key ������^
            if (!_enabled || string.IsNullOrWhiteSpace(_apiKey))
                return Array.Empty<string>();

            // �o�̯d�աG��ڦ걵�A�{���� LLM Client
            // �U�謰�ܷN����ơA�T�O UI �i�B�@���ߨҥ~
            await Task.Delay(50, ct);

            if (filenames == null || filenames.Length == 0)
                return Array.Empty<string>();

            // ²�����u�r���W�h�v�^�ǡA�קK����A������y�{
            var hint = filenames
                .Select(n => (n ?? "").Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => System.IO.Path.GetFileNameWithoutExtension(n))
                .ToArray();

            // ���]��`���e�󰵭ӻE�X�]�A�i�令�u���� LLM ���G�^
            var guess = hint.Length == 0 ? "�@��M��" :
                        hint[0].Length <= 8 ? hint[0] :
                        hint[0].Substring(0, 8);

            return new[] { guess, "�@��M��", "�{�ɱM��" };
        }

        /// <summary>
        /// �d�ҡG�C�H�߮ɶi��������U�]�ȫO�d�����F�Y���ҥΫh�^�� null�^�C
        /// </summary>
        public async Task<(string? Category, double Confidence, string Reasoning)?> ClassifyAsync(
            string filePath, CancellationToken ct)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_apiKey))
                return null;

            // TODO: �걵�A����� LLM ���׵{���X
            await Task.Delay(50, ct);

            // �ܷN�^��
            return ("���", 0.65, "�W�٥]�t proposal/plan�A���w�������");
        }
    }
}
