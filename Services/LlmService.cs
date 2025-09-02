using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

// OpenAI .NET SDK v2 �R�W�Ŷ�
using OpenAI;
using OpenAI.Chat;

namespace AI.KB.Assistant.Services
{
	/// <summary>
	/// LLM �����A�ȡG�� API Key �ɨ� OpenAI�A�_�h������r fallback�C
	/// �I�s�ݡ]MainWindow / DbService �K�^���ݭn�ק�C
	/// </summary>
	public class LlmService
	{
		private readonly AppConfig _cfg;

		public LlmService(AppConfig cfg)
		{
			_cfg = cfg;
		}

		/// <summary>�O�_�i�� OpenAI�]�ΨӨM�w�n���n�u���s���ݡ^</summary>
		public bool IsEnabled => !string.IsNullOrWhiteSpace(_cfg?.OpenAI?.ApiKey);

		/// <summary>
		/// ���դ����@�q��r�A�^�� (����, �H�߫�, �K�n, �z��)
		/// </summary>
		public async Task<(string category, double confidence, string summary, string reasoning)?>
			TryClassifyAsync(string text, CancellationToken ct = default)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			// �� Key �� �� OpenAI�F���ѩΨS Key �� fallback
			if (IsEnabled)
			{
				try
				{
					var r = await TryClassifyByOpenAIAsync(text, ct);
					if (r is not null) return r;
				}
				catch
				{
					// ����s�u/�B��/�榡���~���R�q���Ũ� fallback
				}
			}

			return FallbackByKeywords(text);
		}

		// -------------------- OpenAI �����]���\�N�^�ǡF������D�^ null�^ --------------------

		private async Task<(string category, double confidence, string summary, string reasoning)?>
			TryClassifyByOpenAIAsync(string text, CancellationToken ct)
		{
			var apiKey = _cfg?.OpenAI?.ApiKey;
			var model = string.IsNullOrWhiteSpace(_cfg?.OpenAI?.Model) ? "gpt-4o-mini" : _cfg.OpenAI.Model;

			if (string.IsNullOrWhiteSpace(apiKey))
				return null;

			// �إ� ChatClient�]OpenAI SDK v2 �g�k�^
			var chat = new ChatClient(model, apiKey);

			// �n�D�ҫ���X�T�w JSON�A��K�{���ѪR
			var systemPrompt =
				"�A�O�ɮפ����U��C�ЧP�_�ϥΪ̤�r�ݩ���@���A��X JSON�G{\"category\":string, \"confidence\":number, \"summary\":string, \"reasoning\":string}�C"
			  + "category �ɶq�ϥεu�B�@�P���W���]�Ҧp�G���/�]�g/�ͬ�/���/�Ϥ�/���T/�v��/�{���X/����/�X��/�ǳN/�o��/²��/²��/�|ĳ�O��/unsorted�^�C"
			  + "confidence 0~1�Csummary �� 10~30 �r����C�u�^ JSON�A�Ŧ^�h�l��r�C";

			var userPrompt = $"���e�G{text}";

			var response = await chat.CompleteAsync(
				[
					new SystemChatMessage(systemPrompt),
					new UserChatMessage(userPrompt)
				],
				new ChatCompletionOptions
				{
					// �мҫ��ɶq�K�� JSON �榡�]���j�� schema�A�q�Ϋ׸����^
					Temperature = 0.2,
				},
				ct);

			var content = response?.Content?.FirstOrDefault()?.Text;
			if (string.IsNullOrWhiteSpace(content))
				return null;

			// ���ոѪR JSON
			try
			{
				using var doc = JsonDocument.Parse(content);
				var root = doc.RootElement;

				string category = root.GetPropertyOrDefault("category", "unsorted");
				double confidence = root.GetPropertyOrDefault("confidence", 0.6);
				string summary = root.GetPropertyOrDefault("summary", "�L�K�n");
				string reasoning = root.GetPropertyOrDefault("reasoning", "�L����");

				// �M�z�@�U�����r��
				category = (category ?? "unsorted").Trim();
				if (string.IsNullOrEmpty(category)) category = "unsorted";

				// �w���ɽu�]�קK�ҫ��� >1�^
				if (confidence < 0) confidence = 0;
				if (confidence > 1) confidence = 1;

				return (category, confidence, summary, reasoning);
			}
			catch
			{
				return null;
			}
		}

		// -------------------- Fallback�G����r���� --------------------

		private static (string category, double confidence, string summary, string reasoning)
			FallbackByKeywords(string text)
		{
			var keywords = new Dictionary<string, string[]>
			{
				["���"] = new[] { "AI", "�H�u���z", "�����ǲ�", "�t��k", "�޳N", "�{��", "�}�o" },
				["�]�g"] = new[] { "�Ѳ�", "���", "����", "����", "���", "�]��", "�ײv" },
				["�ͬ�"] = new[] { "�ȹC", "����", "���d", "�B��", "�q�v", "����" },
				["���"] = new[] { "�X��", "����", "²��", "²��", "�פ�", "�|ĳ�O��" }
			};

			foreach (var kv in keywords)
			{
				if (kv.Value.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
				{
					return (kv.Key, 0.9, $"������ {kv.Key} �������e", $"�]����r�]�t����r�G{string.Join("�B", kv.Value)}");
				}
			}

			return ("unsorted", 0.5, "�L�k�۰ʧP�_����", "���R����������r");
		}
	}

	// -------- JsonElement ���Ȥp�u�� --------
	internal static class JsonExt
	{
		public static string GetPropertyOrDefault(this JsonElement e, string name, string def)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;

		public static double GetPropertyOrDefault(this JsonElement e, string name, double def)
			=> e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : def;
	}
}
