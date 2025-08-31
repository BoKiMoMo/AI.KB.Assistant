using AI.KB.Assistant.Models;
using System.IO;

namespace AI.KB.Assistant.Services;

public class RoutingService
{
	private readonly AppConfig _cfg;
	public RoutingService(AppConfig cfg) => _cfg = cfg;

	public string BuildDestination(string srcPath, string category, DateTime when)
	{
		string safeCat = _cfg.Routing.SafeCategories ? Safe(category) : category;

		var targetDir = _cfg.Routing.PathTemplate
			.Replace("{root}", _cfg.App.RootDir)
			.Replace("{category}", safeCat)
			.Replace("{yyyy}", when.Year.ToString("0000"))
			.Replace("{mm}", when.Month.ToString("00"))
			.Replace("{dd}", when.Day.ToString("00"));

		Directory.CreateDirectory(targetDir);
		return Path.Combine(targetDir, Path.GetFileName(srcPath)); // 檔名不動
	}

	public string ResolveCollision(string destPath)
	{
		if (!File.Exists(destPath)) return destPath;

		return _cfg.App.Overwrite switch
		{
			"overwrite" => destPath,
			"skip" => destPath,   // 由上層決定不搬
			_ => NextAvailable(destPath) // rename
		};
	}

	private static string NextAvailable(string path)
	{
		var dir = Path.GetDirectoryName(path)!;
		var name = Path.GetFileNameWithoutExtension(path);
		var ext = Path.GetExtension(path);

		int i = 1; string candidate;
		do { candidate = Path.Combine(dir, $"{name}_{i}{ext}"); i++; }
		while (File.Exists(candidate));
		return candidate;
	}

	public static string Safe(string name)
	{
		foreach (var c in Path.GetInvalidFileNameChars())
			name = name.Replace(c, '_');
		return name.Trim();
	}
}
