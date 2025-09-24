using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AI.KB.Assistant.Helpers
{
    /// <summary>
    /// 嘗試從檔名推斷日期，若無法解析則回傳檔案建立日期
    /// </summary>
    public static class DateResolver
    {
        private static readonly Regex DatePattern =
            new(@"\b(\d{4})(\d{2})(\d{2})\b", RegexOptions.Compiled);

        public static DateTime Resolve(string filename, FileInfo fi)
        {
            var match = DatePattern.Match(filename);
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out int y) &&
                int.TryParse(match.Groups[2].Value, out int m) &&
                int.TryParse(match.Groups[3].Value, out int d))
            {
                return new DateTime(y, m, d);
            }

            return fi.CreationTime;
        }
    }
}
