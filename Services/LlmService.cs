using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// LLM 相關服務層。此版為「設定安全」與「開關保護」版本，
    /// 沒有 API Key 或未啟用時，所有對外方法皆回傳空集合/不進行呼叫。
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

            // OpenAI 區段可能不存在，全部以 null-safe 方式取值
            _enabled = cfg.OpenAI?.EnableWhenLowConfidence ?? false;
            _apiKey = cfg.OpenAI?.ApiKey ?? string.Empty;

            // 預設端點與模型
            _baseUrl = string.IsNullOrWhiteSpace(cfg.OpenAI?.BaseUrl)
                ? "https://api.openai.com/v1"
                : cfg.OpenAI!.BaseUrl!;
            _model = string.IsNullOrWhiteSpace(cfg.OpenAI?.Model)
                ? "gpt-4o-mini"
                : cfg.OpenAI!.Model!;
        }

        public void Dispose()
        {
            // 若未來有 HttpClient 等資源，於此處釋放
        }

        /// <summary>
        /// 依檔名清單，給出「可能的專案名稱」建議。
        /// 未啟用或無 APIKey 時，回傳空集合。
        /// </summary>
        public async Task<string[]> SuggestProjectNamesAsync(string[] filenames, CancellationToken ct)
        {
            // 安全開關：沒啟用或沒 key 直接返回
            if (!_enabled || string.IsNullOrWhiteSpace(_apiKey))
                return Array.Empty<string>();

            // 這裡留白：實際串接你現有的 LLM Client
            // 下方為示意假資料，確保 UI 可運作不拋例外
            await Task.Delay(50, ct);

            if (filenames == null || filenames.Length == 0)
                return Array.Empty<string>();

            // 簡易的「字面規則」回傳，避免阻塞你的整體流程
            var hint = filenames
                .Select(n => (n ?? "").Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => System.IO.Path.GetFileNameWithoutExtension(n))
                .ToArray();

            // 假設把常見前綴做個聚合（你可改成真正的 LLM 結果）
            var guess = hint.Length == 0 ? "一般專案" :
                        hint[0].Length <= 8 ? hint[0] :
                        hint[0].Substring(0, 8);

            return new[] { guess, "一般專案", "臨時專案" };
        }

        /// <summary>
        /// 範例：低信心時進行分類輔助（僅保留介面；若未啟用則回傳 null）。
        /// </summary>
        public async Task<(string? Category, double Confidence, string Reasoning)?> ClassifyAsync(
            string filePath, CancellationToken ct)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_apiKey))
                return null;

            // TODO: 串接你的實際 LLM 推論程式碼
            await Task.Delay(50, ct);

            // 示意回傳
            return ("文件", 0.65, "名稱包含 proposal/plan，推定為文件類");
        }
    }
}
