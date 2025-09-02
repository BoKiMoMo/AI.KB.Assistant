using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AI.KB.Assistant.Helpers
{
	public static class DateResolver
	{
		static readonly Regex RxYmd = new(@"\b(20\d{2})[-_\.]?(0[1-9]|1[0-2])[-_\.]?(0[1-9]|[12]\d|3[01])\b",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static DateTime FromFilenameOrNow(string path)
		{
			var name = Path.GetFileNameWithoutExtension(path) ?? "";
			var m = RxYmd.Match(name);
			if (m.Success &&
				int.TryParse(m.Groups[1].Value, out var y) &&
				int.TryParse(m.Groups[2].Value, out var mo) &&
				int.TryParse(m.Groups[3].Value, out var d))
			{
				try { return new DateTime(y, mo, d); }
				catch { }
			}
			return DateTime.Today;
		}
	}
}
