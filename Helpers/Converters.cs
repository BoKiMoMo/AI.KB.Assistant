using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace AI.KB.Assistant.Helpers
{
    /// <summary>Unix 秒 → yyyy-MM-dd（本地時區）</summary>
    public class UnixToDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value == null) return "";
                long ts = System.Convert.ToInt64(value);
                return DateTimeOffset.FromUnixTimeSeconds(ts)
                                     .ToLocalTime()
                                     .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch { return ""; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>「逗號分隔字串」→ IEnumerable&lt;string&gt;（供標籤徽章顯示）</summary>
    public class StringSplitConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value?.ToString() ?? "";
            return s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
