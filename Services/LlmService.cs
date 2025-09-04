using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
	/// <summary>
	/// 負責分類（可接 OpenAI，也內建關鍵字分類 fallback）
	/// </summary>
	public sealed class LlmService
	{
		private readonly AppConfig _cfg;

		public LlmService(AppConfig cfg) => _cfg = cfg;

		/// <summary>
		/// 是否啟用（有填 API Key 與 Model 才算啟用）
		/// </summary>
		public bool IsEnabled =>
			!string.IsNullOrWhiteSpace(_cfg?.OpenAI?.ApiKey) &&
			!string.IsNullOrWhiteSpace(_cfg?.OpenAI?.Model);

		/// <summary>
		/// 分類：回傳 (category, confidence, summary, reasoning)
		/// 注意：ct 設成可選參數，呼叫端可省略。
		/// </summary>
		public async Task<(string category, double confidence, string summary, string reasoning)>
			ClassifyAsync(string text, CancellationToken ct = default)
		{
			// 1) 有設定 OpenAI 就走 OpenAI（這裡給出骨架；之後你再把真正 API 呼叫放進 TryClassifyByOpenAIAsync）
			if (IsEnabled && _cfg.Classification.Engine?.Equals("llm", StringComparison.OrdinalIgnoreCase) == true)
			{
				var viaLlm = await TryClassifyByOpenAIAsync(text, ct);
				if (viaLlm is not null) return viaLlm.Value;
			}

			// 2) 內建關鍵字規則（穩定可用）
			var viaKeywords = TryClassifyByKeywords(text);
			if (viaKeywords is not null) return viaKeywords.Value;

			// 3) fallback 類別
			var fallback = string.IsNullOrWhiteSpace(_cfg.Classification?.FallbackCategory)
				? "unsorted"
				: _cfg.Classification.FallbackCategory.Trim();

			return (fallback, 0.30, "", "fallback");
		}

		/// <summary>
		/// 內建的關鍵字分類（可立即使用）
		/// </summary>
		private (string category, double confidence, string summary, string reasoning)? TryClassifyByKeywords(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return null;

			var keywords = new Dictionary<string, string[]>
			{
				["finance"] = new[] { "發票", "報價", "請款", "對帳", "匯款", "收據" },
				["resume"] = new[] { "履歷", "CV", "工作經驗", "學歷", "作品集" },
				["contract"] = new[] { "合約", "契約", "條款", "簽署", "保密" },
				["report"] = new[] { "報告", "分析", "統計", "月報", "季報", "年報" },
				["meeting"] = new[] { "會議", "議程", "記錄", "討論", "決議" },
				["personal"] = new[] { "身分證", "戶口", "駕照", "護照" },
				["invoice"] = new[] { "invoice", "inv-", "billing" },
			};

			foreach (var kv in keywords)
			{
				if (kv.Value.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
				{
					return (kv.Key, 0.9, $"偵測到「{kv.Key}」相關關鍵字", string.Join(",", kv.Value));
				}
			}
			return null;
		}

		/// <summary>
		/// 預留：用 OpenAI 進行分類（此處先放骨架避免編譯錯誤）
		/// </summary>
		private Task<(string category, double confidence, string summary, string reasoning)?> TryClassifyByOpenAIAsync(
			string text, CancellationToken ct)
		{
			// TODO：之後把實際呼叫 OpenAI Chat Completions 的程式放這裡
			// 為了不擋住你現在執行，先回傳 null 讓流程走關鍵字或 fallback
			return Task.FromResult<(string, double, string, string)?>(null);
		}
	}
}
