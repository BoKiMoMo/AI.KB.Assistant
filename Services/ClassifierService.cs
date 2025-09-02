using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AI.KB.Assistant.Services
{
	public sealed class ClassifierService
	{
		private readonly LlmService _llm;

		public ClassifierService(LlmService llm) => _llm = llm;

		// --- 規則式（fallback） ---
		private static readonly Dictionary<string, string[]> KeywordMap = new(StringComparer.OrdinalIgnoreCase)
		{
			["invoice"] = new[] { "invoice", "發票", "收據", "receipt", "對帳" },
			["report"] = new[] { "report", "報告", "週報", "月報", "年報" },
			["contract"] = new[] { "contract", "合約", "agreement", "nda" },
			["resume"] = new[] { "resume", "cv", "履歷" },
			["meeting"] = new[] { "meeting", "會議", "紀錄", "minutes" },
			["spec"] = new[] { "spec", "規格", "需求" },
			["design"] = new[] { "design", "figma", "設計", "ui", "ux" },
			["planning"] = new[] { "plan", "企劃", "規劃", "roadmap" },
			["finance"] = new[] { "budget", "費用", "請款", "出納" },
			["legal"] = new[] { "law", "法律", "條款", "授權" },
		};

		private static readonly Dictionary<string, string> ExtMap = new(StringComparer.OrdinalIgnoreCase)
		{
			[".pdf"] = "pdf",
			[".doc"] = "word",
			[".docx"] = "word",
			[".ppt"] = "slides",
			[".pptx"] = "slides",
			[".xls"] = "excel",
			[".xlsx"] = "excel",
			[".csv"] = "excel",
			[".png"] = "image",
			[".jpg"] = "image",
			[".jpeg"] = "image",
			[".gif"] = "image",
			[".mp3"] = "audio",
			[".wav"] = "audio",
			[".m4a"] = "audio",
			[".mp4"] = "video",
			[".mov"] = "video",
			[".mkv"] = "video",
			[".zip"] = "archive",
			[".rar"] = "archive",
			[".7z"] = "archive",
		};

		private static readonly Regex RxInvoice =
			new(@"(發票|invoice|receipt|收據)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		public async System.Threading.Tasks.Task<string> ClassifyAsync(string path, IEnumerable<string>? taxonomy)
		{
			// 優先：自訂 taxonomy（檔名包含就套用）
			var name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
			var lower = name.ToLowerInvariant();
			if (taxonomy != null)
			{
				foreach (var tag in taxonomy.Where(t => !string.IsNullOrWhiteSpace(t)))
				{
					var t = tag.Trim().ToLowerInvariant();
					if (lower.Contains(t)) return tag.Trim();
				}
			}

			// 若啟用 LLM → 試一次
			if (_llm.IsEnabled)
			{
				var ai = await _llm.ClassifyAsync(name, taxonomy);
				if (!string.IsNullOrWhiteSpace(ai) && ai != "unsorted")
					return ai!;
			}

			// 規則式 fallback
			foreach (var kv in KeywordMap)
				if (kv.Value.Any(k => lower.Contains(k))) return kv.Key;

			if (RxInvoice.IsMatch(name)) return "invoice";

			var ext = Path.GetExtension(path) ?? string.Empty;
			if (ExtMap.TryGetValue(ext, out var catByExt)) return catByExt;

			// 最後：LLM 也失敗 → unsorted
			return "unsorted";
		}
	}
}
