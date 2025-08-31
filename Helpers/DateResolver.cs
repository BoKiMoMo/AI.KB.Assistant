using System.Text.RegularExpressions;
using System.IO;

namespace AI.KB.Assistant.Helpers;

public static class DateResolver
{
	static readonly Regex[] Patterns = new[] {
		new Regex(@"(?<y>\d{4})[-_./]?(?<m>\d{2})[-_./]?(?<d>\d{2})", RegexOptions.Compiled),
		new Regex(@"(?<y>\d{4})年(?<m>\d{1,2})月(?<d>\d{1,2})日", RegexOptions.Compiled),
		new Regex(@"(?<y>\d{4})[-_./]?(?<m>\d{2})", RegexOptions.Compiled) // 年月
    };

	public static DateTime FromFilenameOrNow(string path)
	{
		var name = Path.GetFileNameWithoutExtension(path);
		foreach (var rx in Patterns)
		{
			var m = rx.Match(name);
			if (m.Success)
			{
				int y = int.Parse(m.Groups["y"].Value);
				int mth = int.Parse(m.Groups["m"].Value);
				int d = m.Groups["d"].Success ? int.Parse(m.Groups["d"].Value) : 1;
				mth = Math.Clamp(mth, 1, 12);
				d = Math.Clamp(d, 1, DateTime.DaysInMonth(y, mth));
				return new DateTime(y, mth, d);
			}
		}
		// 退而求其次：用檔案建立時間；再不行用現在
		try { return File.GetCreationTime(path); } catch { return DateTime.Now; }
	}
}
