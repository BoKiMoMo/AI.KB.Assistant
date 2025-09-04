using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
	/// <summary>
	/// �t�d�����]�i�� OpenAI�A�]��������r���� fallback�^
	/// </summary>
	public sealed class LlmService
	{
		private readonly AppConfig _cfg;

		public LlmService(AppConfig cfg) => _cfg = cfg;

		/// <summary>
		/// �O�_�ҥΡ]���� API Key �P Model �~��ҥΡ^
		/// </summary>
		public bool IsEnabled =>
			!string.IsNullOrWhiteSpace(_cfg?.OpenAI?.ApiKey) &&
			!string.IsNullOrWhiteSpace(_cfg?.OpenAI?.Model);

		/// <summary>
		/// �����G�^�� (category, confidence, summary, reasoning)
		/// �`�N�Gct �]���i��ѼơA�I�s�ݥi�ٲ��C
		/// </summary>
		public async Task<(string category, double confidence, string summary, string reasoning)>
			ClassifyAsync(string text, CancellationToken ct = default)
		{
			// 1) ���]�w OpenAI �N�� OpenAI�]�o�̵��X���[�F����A�A��u�� API �I�s��i TryClassifyByOpenAIAsync�^
			if (IsEnabled && _cfg.Classification.Engine?.Equals("llm", StringComparison.OrdinalIgnoreCase) == true)
			{
				var viaLlm = await TryClassifyByOpenAIAsync(text, ct);
				if (viaLlm is not null) return viaLlm.Value;
			}

			// 2) ��������r�W�h�]í�w�i�Ρ^
			var viaKeywords = TryClassifyByKeywords(text);
			if (viaKeywords is not null) return viaKeywords.Value;

			// 3) fallback ���O
			var fallback = string.IsNullOrWhiteSpace(_cfg.Classification?.FallbackCategory)
				? "unsorted"
				: _cfg.Classification.FallbackCategory.Trim();

			return (fallback, 0.30, "", "fallback");
		}

		/// <summary>
		/// ���ت�����r�����]�i�ߧY�ϥΡ^
		/// </summary>
		private (string category, double confidence, string summary, string reasoning)? TryClassifyByKeywords(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return null;

			var keywords = new Dictionary<string, string[]>
			{
				["finance"] = new[] { "�o��", "����", "�д�", "��b", "�״�", "����" },
				["resume"] = new[] { "�i��", "CV", "�u�@�g��", "�Ǿ�", "�@�~��" },
				["contract"] = new[] { "�X��", "����", "����", "ñ�p", "�O�K" },
				["report"] = new[] { "���i", "���R", "�έp", "���", "�u��", "�~��" },
				["meeting"] = new[] { "�|ĳ", "ĳ�{", "�O��", "�Q��", "�Mĳ" },
				["personal"] = new[] { "������", "��f", "�r��", "�@��" },
				["invoice"] = new[] { "invoice", "inv-", "billing" },
			};

			foreach (var kv in keywords)
			{
				if (kv.Value.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
				{
					return (kv.Key, 0.9, $"������u{kv.Key}�v��������r", string.Join(",", kv.Value));
				}
			}
			return null;
		}

		/// <summary>
		/// �w�d�G�� OpenAI �i������]���B���񰩬[�קK�sĶ���~�^
		/// </summary>
		private Task<(string category, double confidence, string summary, string reasoning)?> TryClassifyByOpenAIAsync(
			string text, CancellationToken ct)
		{
			// TODO�G������کI�s OpenAI Chat Completions ���{����o��
			// ���F���צ�A�{�b����A���^�� null ���y�{������r�� fallback
			return Task.FromResult<(string, double, string, string)?>(null);
		}
	}
}
