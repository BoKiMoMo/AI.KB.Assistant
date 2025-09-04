using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AI.KB.Assistant.Helpers
{
	/// <summary>
	/// 從檔名猜測日期，失敗則退回檔案建立時間或 Now。
	/// 支援格式（可混用 -, _, / 或沒有分隔）：
	///   2025-08-29、20250829、2025_08_29
	///   202508291530、2025-08-29-1530、20250829153045
	/// </summary>
	public static class DateResolver
	{
		// yyyy MM dd [HH mm [ss]]
		// ex: 20250829, 2025-08-29, 2025_08_29, 202508291530, 2025-08-29-153045
		private static readonly Regex RxDate =
			new(@"(?<!\d)(20\d{2})[-_\/]?(0[1-9]|1[0-2])[-_\/]?([0-2]\d|3[01])(?:[-_ ]?([01]\d|2[0-3])([0-5]\d)?([0-5]\d)?)?(?!\d)",
				RegexOptions.Compiled);

		/// <summary>
		/// 從檔名或檔案屬性解析時間；全失敗則回傳 Now。
		/// </summary>
		public static DateTimeOffset FromFilenameOrNow(string path)
		{
			var name = Path.GetFileNameWithoutExtension(path);

			if (TryFromFileName(name, out var dto))
				return dto;

			// 再試檔案建立/寫入時間
			try
			{
				var fi = new FileInfo(path);
				if (fi.Exists)
				{
					var t = fi.CreationTimeUtc;
					if (t.Year < 2000) t = fi.LastWriteTimeUtc;
					if (t.Year >= 2000) return new DateTimeOffset(t);
				}
			}
			catch { /* ignore */ }

			return DateTimeOffset.Now;
		}

		/// <summary>
		/// 僅從檔名嘗試解析。
		/// </summary>
		public static bool TryFromFileName(string fileNameWithoutExt, out DateTimeOffset result)
		{
			result = default;

			var m = RxDate.Match(fileNameWithoutExt);
			if (!m.Success) return false;

			int year = SafeInt(m.Groups[1].Value);
			int month = SafeInt(m.Groups[2].Value);
			int day = SafeInt(m.Groups[3].Value);

			int hour = m.Groups[4].Success ? SafeInt(m.Groups[4].Value) : 0;
			int minute = m.Groups[5].Success ? SafeInt(m.Groups[5].Value) : 0;
			int second = m.Groups[6].Success ? SafeInt(m.Groups[6].Value) : 0;

			try
			{
				var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
				result = new DateTimeOffset(local);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static int SafeInt(string s)
		{
			if (int.TryParse(s, out var v)) return v;
			return 0;
		}
	}
}
