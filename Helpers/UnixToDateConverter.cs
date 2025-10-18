using System;
using System.Globalization;
using System.Windows.Data;

namespace AI.KB.Assistant.Helpers
{
    /// <summary>
    /// 將 Unix 時間秒數 (自 1970-01-01 起的秒) 轉換為本地時間字串。
    /// 支援輸入型別：double、long、int、string（可解析為上述型別）。
    /// </summary>
    public class UnixToDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return string.Empty;

            try
            {
                double seconds;
                switch (value)
                {
                    case double d:
                        seconds = d; break;
                    case float f:
                        seconds = f; break;
                    case long l:
                        seconds = l; break;
                    case int i:
                        seconds = i; break;
                    case string s when double.TryParse(s, out var parsed):
                        seconds = parsed; break;
                    default:
                        return string.Empty;
                }

                var epoch = DateTime.UnixEpoch; // 1970/1/1 UTC
                var utc = epoch.AddSeconds(seconds);
                var local = utc.ToLocalTime();
                return local.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 不支援回轉
            return Binding.DoNothing;
        }
    }
}
