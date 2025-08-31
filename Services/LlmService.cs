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
		// taxonomy（你可在 config.json 的 Classification.CustomTaxonomy 自訂）
		var taxonomy = _cfg.Classification.CustomTaxonomy ?? new List<string> { "需求分析", "風險管理", "會議紀錄", "流程文件", "學習筆記" };

		string sys = """
        你是嚴謹的知識庫分類員。請僅以 JSON 回覆，鍵包含：
        - primary_category：從 taxonomy 選一個，若不適用可用 "unsorted"。
        - confidence：0~100 的數值。
        - summary：<=80 字中文摘要。
        - reasoning：<=100 字中文說明。
        僅輸出 JSON，不要其他文字。
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

			// 有些模型會在最外層包 ```json ...```，先剝掉
			json = json.Trim().Trim('`');
			json = json.Replace("json", "").Trim().Trim('`');

			var parsed = JsonConvert.DeserializeObject<LlmResult>(json);
			if (parsed == null) throw new Exception("LLM 回傳 JSON 解析失敗");

			// 信心轉 0~1 比較容易用，但先保留 0~100 給 UI
			return parsed;
		}
		catch
		{
			// 失敗時退回簡單規則，避免流程中斷
			var fallback = text.Contains("會議") ? "會議紀錄" :
						   text.Contains("流程") ? "流程文件" :
						   text.Contains("風險") ? "風險管理" :
						   _cfg.Classification.FallbackCategory;
			return new LlmResult(fallback, 60, "（AI 失敗使用保底分類）", "fallback-keyword");
		}
	}
}
