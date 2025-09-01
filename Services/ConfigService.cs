using System;
using System.IO;
using Newtonsoft.Json;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
	/// <summary>
	/// �]�w�ɦs���A�ȡGŪ�g config.json�B�ɻ��w�]�ȡB�إߥ��n���|
	/// </summary>
	public static class ConfigService
	{
		public static string DefaultPath = "config.json";

		/// <summary>
		/// Ū���]�w�ɡF�Y���s�b�h�� FileNotFoundException
		/// </summary>
		public static AppConfig Load(string? path = null)
		{
			path ??= DefaultPath;

			if (!File.Exists(path))
				throw new FileNotFoundException($"�䤣��]�w�ɡG{Path.GetFullPath(path)}", path);

			var json = File.ReadAllText(path);
			var cfg = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();

			NormalizeAndFillDefaults(cfg);
			EnsurePaths(cfg);

			return cfg;
		}

		/// <summary>
		/// ����Ū���]�w�ɡF���Ѯɦ^�� false �õ��X�@���w�]�]�w
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
		/// �x�s�]�w�ɡ]�|�۰ʫإߥؿ��^
		/// </summary>
		public static void Save(string? path, AppConfig cfg)
		{
			path ??= DefaultPath;

			NormalizeAndFillDefaults(cfg); // �s�ɫe�]���@�����W��
			var full = Path.GetFullPath(path);
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);

			var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
			File.WriteAllText(full, json);
		}

		/* ---------------- �����u�� ---------------- */

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

			// ���\ 0~1 �� 0~100�F�Τ@�s�b 0~1
			if (cfg.Classification.ConfidenceThreshold > 1.0)
				cfg.Classification.ConfidenceThreshold /= 100.0;
			if (cfg.Classification.ConfidenceThreshold <= 0)
				cfg.Classification.ConfidenceThreshold = 0.7;

			if (string.IsNullOrWhiteSpace(cfg.Classification.FallbackCategory))
				cfg.Classification.FallbackCategory = "������";

			// OpenAI
			if (string.IsNullOrWhiteSpace(cfg.OpenAI.Model))
				cfg.OpenAI.Model = "gpt-4o-mini";
			cfg.OpenAI.ApiKey ??= string.Empty;

			// ���|���W��
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
			catch { /* �����إ߸�Ƨ����� */ }
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
