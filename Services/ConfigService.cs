using System;
using System.IO;
using Newtonsoft.Json;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
	/// <summary>
	/// 設定檔存取服務：讀寫 config.json、補齊預設值、建立必要路徑
	/// </summary>
	public static class ConfigService
	{
		public static string DefaultPath = "config.json";

		/// <summary>
		/// 讀取設定檔；若不存在則丟 FileNotFoundException
		/// </summary>
		public static AppConfig Load(string? path = null)
		{
			path ??= DefaultPath;

			if (!File.Exists(path))
				throw new FileNotFoundException($"找不到設定檔：{Path.GetFullPath(path)}", path);

			var json = File.ReadAllText(path);
			var cfg = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();

			NormalizeAndFillDefaults(cfg);
			EnsurePaths(cfg);

			return cfg;
		}

		/// <summary>
		/// 嘗試讀取設定檔；失敗時回傳 false 並給出一份預設設定
		/// </summary>
		public static bool TryLoad(out AppConfig cfg, string? path = null)
		{
			try
			{
				cfg = Load(path);
				return true;
			}
			catch
			{
				cfg = Default();
				return false;
			}
		}

		/// <summary>
		/// 儲存設定檔（會自動建立目錄）
		/// </summary>
		public static void Save(string? path, AppConfig cfg)
		{
			path ??= DefaultPath;

			NormalizeAndFillDefaults(cfg); // 存檔前也做一次正規化
			var full = Path.GetFullPath(path);
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);

			var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
			File.WriteAllText(full, json);
		}

		/* ---------------- 內部工具 ---------------- */

		private static void NormalizeAndFillDefaults(AppConfig cfg)
		{
			cfg.App ??= new AppSection();
			cfg.Routing ??= new RoutingSection();
			cfg.Classification ??= new ClassificationSection();
			cfg.OpenAI ??= new OpenAISection();

			// App
			if (string.IsNullOrWhiteSpace(cfg.App.RootDir))
				cfg.App.RootDir = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
					"KnowledgeBase");

			if (string.IsNullOrWhiteSpace(cfg.App.InboxDir))
				cfg.App.InboxDir = Path.Combine(cfg.App.RootDir, "_inbox");

			if (string.IsNullOrWhiteSpace(cfg.App.DbPath))
				cfg.App.DbPath = Path.Combine("data", "knowledge.db");

			if (string.IsNullOrWhiteSpace(cfg.App.MoveMode))
				cfg.App.MoveMode = "move";              // move | copy

			if (string.IsNullOrWhiteSpace(cfg.App.Overwrite))
				cfg.App.Overwrite = "rename";           // skip | rename | overwrite

			// Routing
			if (string.IsNullOrWhiteSpace(cfg.Routing.PathTemplate))
				cfg.Routing.PathTemplate = "{root}/{category}/{yyyy}/{mm}/";

			// Classification
			if (string.IsNullOrWhiteSpace(cfg.Classification.Engine))
				cfg.Classification.Engine = "llm";      // llm | dummy | hybrid

			if (string.IsNullOrWhiteSpace(cfg.Classification.Style))
				cfg.Classification.Style = "topic";

			// 允許 0~1 或 0~100；統一存在 0~1
			if (cfg.Classification.ConfidenceThreshold > 1.0)
				cfg.Classification.ConfidenceThreshold /= 100.0;
			if (cfg.Classification.ConfidenceThreshold <= 0)
				cfg.Classification.ConfidenceThreshold = 0.7;

			if (string.IsNullOrWhiteSpace(cfg.Classification.FallbackCategory))
				cfg.Classification.FallbackCategory = "未分類";

			// OpenAI
			if (string.IsNullOrWhiteSpace(cfg.OpenAI.Model))
				cfg.OpenAI.Model = "gpt-4o-mini";
			cfg.OpenAI.ApiKey ??= string.Empty;

			// 路徑正規化
			cfg.App.RootDir = cfg.App.RootDir.Trim();
			cfg.App.InboxDir = cfg.App.InboxDir.Trim();
			cfg.App.DbPath = cfg.App.DbPath.Trim();
		}

		private static void EnsurePaths(AppConfig cfg)
		{
			TryCreateDir(cfg.App.RootDir);
			TryCreateDir(cfg.App.InboxDir);

			var dbDir = Path.GetDirectoryName(cfg.App.DbPath);
			if (!string.IsNullOrWhiteSpace(dbDir))
				TryCreateDir(dbDir!);
		}

		private static void TryCreateDir(string path)
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(path))
					Directory.CreateDirectory(path);
			}
			catch { /* 忽略建立資料夾失敗 */ }
		}

		private static AppConfig Default()
		{
			var c = new AppConfig();
			NormalizeAndFillDefaults(c);
			EnsurePaths(c);
			return c;
		}
	}
}
