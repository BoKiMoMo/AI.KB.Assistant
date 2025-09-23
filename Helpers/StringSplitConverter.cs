// Helpers/StringSplitConverter.cs
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace AI.KB.Assistant.Helpers
{
    /// <summary>「逗號分隔字串」→ IEnumerable&lt;string&gt;，供標籤徽章顯示</summary>
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
