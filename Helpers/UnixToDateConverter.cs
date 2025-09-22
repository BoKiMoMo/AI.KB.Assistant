using System;
using System.Globalization;
using System.Windows.Data;

namespace AI.KB.Assistant
{
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
}
