using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AI.KB.Assistant.Common
{
    /// <summary>舊版相容與小工具延伸。</summary>
    public static class CompatShims
    {
        public static string GetDirectoryName(this string path)
            => string.IsNullOrEmpty(path) ? string.Empty : (Path.GetDirectoryName(path) ?? string.Empty);

        public static string GetFileName(this string path)
            => string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileName(path);

        public static string GetExtension(this string path)
            => string.IsNullOrEmpty(path) ? string.Empty : (Path.GetExtension(path)?.TrimStart('.') ?? string.Empty);

        public static List<string> ToSafeList(this IEnumerable<string>? src)
            => src?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList()
               ?? new List<string>();
    }
}
