using System;
using System.Globalization;
using System.Windows.Data;

namespace AI.KB.Assistant.Helpers
{
	/// <summary>
	/// 將 Unix 秒數 (long) 轉人類可讀日期字串。
	/// 參數 parameter 可傳入自定格式字串，預設 "yyyy-MM-dd"。
	/// </summary>
	public sealed class UnixToDateConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			try
			{
				if (value is null) return string.Empty;

				long ts = System.Convert.ToInt64(value);
				var dt = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime();

				string fmt = (parameter as string) ?? "yyyy-MM-dd";
				return dt.ToString(fmt, CultureInfo.InvariantCulture);
			}
			catch
			{
				return string.Empty;
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> throw new NotSupportedException();
	}
}
