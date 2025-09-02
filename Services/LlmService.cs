using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

// OpenAI .NET SDK v2 命名空間
using OpenAI;
using OpenAI.Chat;

namespace AI.KB.Assistant.Services
{
	/// <summary>
	/// LLM 分類服務：有 API Key 時走 OpenAI，否則走關鍵字 fallback。
	/// 呼叫端（MainWindow / DbService …）不需要修改。
	/// </summary>
	public class LlmService
	{
		private readonly AppConfig _cfg;

		public LlmService(AppConfig cfg)
		{
			_cfg = cfg;
		}

		/// <summary>是否可用 OpenAI（用來決定要不要真的叫雲端）</summary>
		public bool IsEnabled => !string.IsNullOrWhiteSpace(_cfg?.OpenAI?.ApiKey);

		/// <summary>
		/// 嘗試分類一段文字，回傳 (分類, 信心度, 摘要, 理由)
		/// </summary>
		public async Task<(string category, double confidence, string summary, string reasoning)?>
			TryClassifyAsync(string text, CancellationToken ct = default)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			// 有 Key → 試 OpenAI；失敗或沒 Key → fallback
			if (IsEnabled)
			{
				try
				{
					var r = await TryClassifyByOpenAIAsync(text, ct);
					if (r is not null) return r;
				}
				catch
				{
					// 任何連線/額度/格式錯誤都靜默降級到 fallback
				}
			}

			return FallbackByKeywords(text);
		}

		// -------------------- OpenAI 版本（成功就回傳；任何問題回 null） --------------------

		private async Task<(string category, double confidence, string summary, string reasoning)?>
			TryClassifyByOpenAIAsync(string text, CancellationToken ct)
		{
			var apiKey = _cfg?.OpenAI?.ApiKey;
			var model = string.IsNullOrWhiteSpace(_cfg?.OpenAI?.Model) ? "gpt-4o-mini" : _cfg.OpenAI.Model;

			if (string.IsNullOrWhiteSpace(apiKey))
				return null;

			// 建立 ChatClient（OpenAI SDK v2 寫法）
			var chat = new ChatClient(model, apiKey);

			// 要求模型輸出固定 JSON，方便程式解析
			var systemPrompt =
				"你是檔案分類助手。請判斷使用者文字屬於哪一類，輸出 JSON：{\"category\":string, \"confidence\":number, \"summary\":string, \"reasoning\":string}。"
			  + "category 盡量使用短且一致的名詞（例如：科技/財經/生活/文件/圖片/音訊/影片/程式碼/報表/合約/學術/發票/簡歷/簡報/會議記錄/unsorted）。"
			  + "confidence 0~1。summary 用 10~30 字中文。只回 JSON，勿回多餘文字。";

			var userPrompt = $"內容：{text}";

			var response = await chat.CompleteAsync(
				[
					new SystemChatMessage(systemPrompt),
					new UserChatMessage(userPrompt)
				],
				new ChatCompletionOptions
				{
					// 請模型盡量貼近 JSON 格式（不強制 schema，通用度較高）
					Temperature = 0.2,
				},
				ct);

			var content = response?.Content?.FirstOrDefault()?.Text;
			if (string.IsNullOrWhiteSpace(content))
				return null;

			// 嘗試解析 JSON
			try
			{
				using var doc = JsonDocument.Parse(content);
				var root = doc.RootElement;

				string category = root.GetPropertyOrDefault("category", "unsorted");
				double confidence = root.GetPropertyOrDefault("confidence", 0.6);
				string summary = root.GetPropertyOrDefault("summary", "無摘要");
				string reasoning = root.GetPropertyOrDefault("reasoning", "無說明");

				// 清理一下分類字串
				category = (category ?? "unsorted").Trim();
				if (string.IsNullOrEmpty(category)) category = "unsorted";

				// 安全界線（避免模型給 >1）
				if (confidence < 0) confidence = 0;
				if (confidence > 1) confidence = 1;

				return (category, confidence, summary, reasoning);
			}
			catch
			{
				return null;
			}
		}

		// -------------------- Fallback：關鍵字分類 --------------------

		private static (string category, double confidence, string summary, string reasoning)
			FallbackByKeywords(string text)
		{
			var keywords = new Dictionary<string, string[]>
			{
				["科技"] = new[] { "AI", "人工智慧", "機器學習", "演算法", "技術", "程式", "開發" },
				["財經"] = new[] { "股票", "投資", "市場", "美元", "基金", "財報", "匯率" },
				["生活"] = new[] { "旅遊", "食物", "健康", "運動", "電影", "音樂" },
				["文件"] = new[] { "合約", "報表", "簡報", "簡歷", "論文", "會議記錄" }
			};

			foreach (var kv in keywords)
			{
				if (kv.Value.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
				{
					return (kv.Key, 0.9, $"偵測到 {kv.Key} 相關內容", $"因為文字包含關鍵字：{string.Join("、", kv.Value)}");
				}
			}

			return ("unsorted", 0.5, "無法自動判斷分類", "未命中任何關鍵字");
		}
	}

	// -------- JsonElement 取值小工具 --------
	internal static class JsonExt
	{
		public static string GetPropertyOrDefault(this JsonElement e, string name, string def)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;

		public static double GetPropertyOrDefault(this JsonElement e, string name, double def)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : def;
	}
}
