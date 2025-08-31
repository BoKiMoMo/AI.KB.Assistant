using AI.KB.Assistant.Models;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;

namespace AI.KB.Assistant.Services;

public record LlmResult(string primary_category, double confidence, string summary, string reasoning);

class LlmRequest
{
	public string model { get; set; } = "gpt-4o-mini";
	public List<object> messages { get; set; } = new();
	public LlmRequest(string model, string system, string user)
	{
		this.model = model;
		messages.Add(new { role = "system", content = system });
		messages.Add(new { role = "user", content = user });
	}
}

class LlmResponse
{
	public List<Choice> choices { get; set; } = new();
	public class Choice { public Message message { get; set; } = new(); }
	public class Message { public string content { get; set; } = ""; }
}

public class LlmService
{
	private readonly AppConfig _cfg;
	private readonly HttpClient _http = new();

	public LlmService(AppConfig cfg)
	{
		_cfg = cfg;
		_http.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", _cfg.OpenAI.ApiKey);
		_http.Timeout = TimeSpan.FromSeconds(60);
	}

	public async Task<LlmResult> ClassifyAsync(string text)
	{
		// taxonomy�]�A�i�b config.json �� Classification.CustomTaxonomy �ۭq�^
		var taxonomy = _cfg.Classification.CustomTaxonomy ?? new List<string> { "�ݨD���R", "���I�޲z", "�|ĳ����", "�y�{���", "�ǲߵ��O" };

		string sys = """
        �A�O�Y�Ԫ����Ѯw�������C�жȥH JSON �^�СA��]�t�G
        - primary_category�G�q taxonomy ��@�ӡA�Y���A�Υi�� "unsorted"�C
        - confidence�G0~100 ���ƭȡC
        - summary�G<=80 �r����K�n�C
        - reasoning�G<=100 �r���廡���C
        �ȿ�X JSON�A���n��L��r�C
        """;

		string user = $"taxonomy:{string.Join(",", taxonomy)}\nstyle:{_cfg.Classification.Style}\ncontent:\n{text}";

		var body = new LlmRequest(_cfg.OpenAI.Model, sys, user);
		var reqContent = new StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8, "application/json");

		try
		{
			var resp = await _http.PostAsync("https://api.openai.com/v1/chat/completions", reqContent);
			resp.EnsureSuccessStatusCode();
			var raw = await resp.Content.ReadAsStringAsync();

			var obj = JsonConvert.DeserializeObject<LlmResponse>(raw);
			var json = obj?.choices?.FirstOrDefault()?.message?.content ?? "{}";

			// ���Ǽҫ��|�b�̥~�h�] ```json ...```�A���鱼
			json = json.Trim().Trim('`');
			json = json.Replace("json", "").Trim().Trim('`');

			var parsed = JsonConvert.DeserializeObject<LlmResult>(json);
			if (parsed == null) throw new Exception("LLM �^�� JSON �ѪR����");

			// �H���� 0~1 ����e���ΡA�����O�d 0~100 �� UI
			return parsed;
		}
		catch
		{
			// ���Ѯɰh�^²��W�h�A�קK�y�{���_
			var fallback = text.Contains("�|ĳ") ? "�|ĳ����" :
						   text.Contains("�y�{") ? "�y�{���" :
						   text.Contains("���I") ? "���I�޲z" :
						   _cfg.Classification.FallbackCategory;
			return new LlmResult(fallback, 60, "�]AI ���ѨϥΫO�������^", "fallback-keyword");
		}
	}
}
