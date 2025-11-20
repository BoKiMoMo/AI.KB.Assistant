using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V20.4 (優化 3 - Prompts 設定檔版)
    /// 1. [V20.3] 依需求：移除所有本地模擬/亂數，若 API Key 為空，則拋出 InvalidOperationException。
    /// 2. [V20.4] 移除所有寫死的 `sysPrompt`，改為從 `_prompts` (ConfigService) 讀取。
    /// </summary>
    public sealed class LlmService
    {
        // 為了效能，HttpClient 應為靜態
        private static readonly HttpClient _client = new HttpClient();

        // 服務內部的設定快取
        private string _apiKey = string.Empty;
        private string _modelName = "gemini-2.5-flash-preview-0_2025"; // 預設模型
        // [V20.4] 優化 3：提示詞快取
        private PromptConfig _prompts = new();

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public LlmService()
        {
            // 服務建立時，嘗試從 ConfigService 載入一次
            UpdateConfig(ConfigService.Cfg);
        }

        /// <summary>
        /// 當設定變更時，由 MainWindow 呼叫此方法以更新金鑰
        /// </summary>
        public void UpdateConfig(AppConfig? cfg)
        {
            _apiKey = cfg?.OpenAI?.ApiKey ?? string.Empty;
            _modelName = cfg?.OpenAI?.Model ?? "gemini-2.5-flash-preview-0_2025";
            // [V20.4] 優化 3：快取提示詞
            _prompts = cfg?.Prompts ?? GetDefaultPromptsForFallback();
        }

        /// <summary>
        /// [V20.4] 優化 3：在 ConfigService 尚未載入完成時，提供一個備援的提示詞
        /// </summary>
        private PromptConfig GetDefaultPromptsForFallback()
        {
            return new PromptConfig
            {
                AnalyzeConfidence = "您是一個文件分類信心度分析師。請分析使用者提供的檔名。您**必須**只回傳一個介於 0.0 到 1.0 之間的數字 (JSON 格式的數字)，代表您有多大的信心能正確分類此檔案。範例：0.85",
                Summarize = "您是一個專業的檔案摘要器。請根據使用者提供的文字 (通常是檔名或路徑)，用繁體中文產生一句話的簡潔摘要。",
                SuggestTags = "您是一個檔案標籤專家。請根據使用者提供的文字 (通常是檔名)，產生 3 到 5 個最相關的繁體中文標籤。請只回傳標籤本身，並用逗號 (,) 分隔。範例: '報告,財務,2025,Q3'",
                SuggestProject = "您是一個專業的檔案分類師。請根據使用者提供的檔名或路徑，建議一個最適合的「專案名稱」。**必須**只回傳專案名稱本身 (建議使用 英文/數字/底線)，不要包含任何解釋。範例：'2025_Q3_Marketing' 或 'Project_Alpha'"
            };
        }

        /// <summary>
        /// (V10.2 升級) 信心度分析
        /// </summary>
        public async Task<double> AnalyzeConfidenceAsync(string textToAnalyze)
        {
            // [V20.3] 依需求：若無 API Key，拋出例外
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("AI 服務未設定 API Key，無法產生信心度。");
            }

            // [V20.4] 優化 3：從設定檔讀取提示詞
            var sysPrompt = _prompts.AnalyzeConfidence;
            if (string.IsNullOrWhiteSpace(sysPrompt))
            {
                throw new InvalidOperationException("AI 提示詞 (AnalyzeConfidence) 未在 config.json 中設定。");
            }

            var userQuery = $"請分析此檔名的信心度：\"{textToAnalyze}\"";
            var resultText = await GenerateTextAsync(sysPrompt, userQuery);

            // 嘗試剖析 LLM 回傳的數字
            if (double.TryParse(resultText, NumberStyles.Any, CultureInfo.InvariantCulture, out var score))
            {
                return Math.Clamp(score, 0.0, 1.0);
            }

            // 剖析失敗，回傳低信心度
            return 0.1;
        }

        /// <summary>
        /// V7.6 實作：呼叫 LLM 產生摘要
        /// </summary>
        public async Task<string> SummarizeAsync(string textToAnalyze)
        {
            // [V20.3] 依需求：若無 API Key，拋出例外
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("AI 服務未設定 API Key，無法產生摘要。");
            }

            // [V20.4] 優化 3：從設定檔讀取提示詞
            var sysPrompt = _prompts.Summarize;
            if (string.IsNullOrWhiteSpace(sysPrompt))
            {
                throw new InvalidOperationException("AI 提示詞 (Summarize) 未在 config.json 中設定。");
            }

            var userQuery = $"請為我摘要這段文字: \"{textToAnalyze}\"";

            return await GenerateTextAsync(sysPrompt, userQuery);
        }

        /// <summary>
        /// V7.6 實作：呼叫 LLM 產生標籤
        /// </summary>
        public async Task<List<string>> SuggestTagsAsync(string textToAnalyze)
        {
            // [V20.3] 依需求：若無 API Key，拋出例外
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("AI 服務未設定 API Key，無法產生建議標籤。");
            }

            // [V20.4] 優化 3：從設定檔讀取提示詞
            var sysPrompt = _prompts.SuggestTags;
            if (string.IsNullOrWhiteSpace(sysPrompt))
            {
                throw new InvalidOperationException("AI 提示詞 (SuggestTags) 未在 config.json 中設定。");
            }

            var userQuery = $"請為我產生標籤: \"{textToAnalyze}\"";

            var result = await GenerateTextAsync(sysPrompt, userQuery);

            // 清理並剖析 LLM 的回傳
            return result.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(tag => tag.Trim())
                         .Where(tag => !string.IsNullOrWhiteSpace(tag))
                         .Distinct()
                         .ToList();
        }

        /// <summary>
        /// [V10.2 新增] 呼叫 LLM 產生專案名稱
        /// </summary>
        public async Task<string> SuggestProjectAsync(string textToAnalyze)
        {
            // [V20.3] 依需求：若無 API Key，拋出例外
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("AI 服務未設定 API Key，無法產生建議專案。");
            }

            // [V20.4] 優化 3：從設定檔讀取提示詞
            var sysPrompt = _prompts.SuggestProject;
            if (string.IsNullOrWhiteSpace(sysPrompt))
            {
                throw new InvalidOperationException("AI 提示詞 (SuggestProject) 未在 config.json 中設定。");
            }

            var userQuery = $"請為此檔案建議專案名稱：\"{textToAnalyze}\"";

            var result = await GenerateTextAsync(sysPrompt, userQuery);
            return result.Trim();
        }


        /// <summary>
        /// 呼叫 Gemini API 的核心方法
        /// </summary>
        private async Task<string> GenerateTextAsync(string systemPrompt, string userQuery)
        {
            // [V20.3] 在實際呼叫時再次確認 API Key
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                // V7.6 修正：從 ConfigService 動態重試
                UpdateConfig(ConfigService.Cfg);
                if (string.IsNullOrWhiteSpace(_apiKey))
                {
                    // [V20.3] 依需求：拋出例外
                    throw new InvalidOperationException("AI 服務未設定 API Key。");
                }
            }

            // [Gemini API 指令] 保持 apiKey 為空字串
            // Canvas 將在執行時自動注入金鑰
            const string apiKey = "";
            var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={apiKey}";

            var payload = new
            {
                SystemInstruction = new { Parts = new[] { new { Text = systemPrompt } } },
                Contents = new[] { new { Parts = new[] { new { Text = userQuery } } } }
            };

            try
            {
                var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application.json");

                var response = await _client.PostAsync(apiUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"API 請求失敗 ({(int)response.StatusCode}): {errorBody}");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();

                // 剖析回應
                using var doc = JsonDocument.Parse(jsonResponse);
                var text = doc.RootElement
                              .GetProperty("candidates")[0]
                              .GetProperty("content")
                              .GetProperty("parts")[0]
                              .GetProperty("text")
                              .GetString();

                return text ?? string.Empty;
            }
            catch (Exception ex)
            {
                // 將詳細錯誤傳回給 MainWindow 的 Log
                throw new Exception($"LLM 呼叫失敗: {ex.Message}", ex);
            }
        }
    }
}