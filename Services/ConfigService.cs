using AI.KB.Assistant.Models;
using Newtonsoft.Json;
using System.IO;


namespace AI.KB.Assistant.Services;

public static class ConfigService
{
	public static AppConfig Load(string path = "config.json")
	{
		if (!File.Exists(path))
			throw new FileNotFoundException($"找不到設定檔：{path}");

		var json = File.ReadAllText(path);
		var cfg = JsonConvert.DeserializeObject<AppConfig>(json) ?? new();

		// 預先建立必要資料夾
		if (!string.IsNullOrWhiteSpace(cfg.App.DbPath))
			Directory.CreateDirectory(Path.GetDirectoryName(cfg.App.DbPath)!);
		if (!string.IsNullOrWhiteSpace(cfg.App.RootDir))
			Directory.CreateDirectory(cfg.App.RootDir);
		if (!string.IsNullOrWhiteSpace(cfg.App.InboxDir))
			Directory.CreateDirectory(cfg.App.InboxDir);

		return cfg;
	}
}
