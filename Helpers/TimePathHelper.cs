using System.Globalization;

namespace AI.KB.Assistant.Helpers
{
    public static class TimePathHelper
    {
        // 預留擴充：若未來要加季、ISO週，可從這裡取片段
        public static (string Year, string Month, string Day) YMD(System.DateTimeOffset when)
        {
            var dt = when.ToLocalTime().DateTime;
            return (dt.ToString("yyyy", CultureInfo.InvariantCulture),
                    dt.ToString("MM", CultureInfo.InvariantCulture),
                    dt.ToString("dd", CultureInfo.InvariantCulture));
        }
    }
}
