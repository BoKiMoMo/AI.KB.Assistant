using System;
using System.Globalization;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Helpers
{
    /// <summary>
    /// 提供年/季/月/週的路徑片段計算（TimePeriod 分類風格使用）
    /// </summary>
    public static class TimePathHelper
    {
        /// <summary>
        /// 取得年、季、月、週（ISO 週）字串。
        /// 年：yyyy；季：Q1..Q4；月：MM；週：W01..W53
        /// </summary>
        public static (string Year, string Quarter, string Month, string Week) Parts(DateTimeOffset when)
        {
            var year = when.Year.ToString();
            var month = when.Month.ToString("00");
            var quarter = "Q" + (((when.Month - 1) / 3) + 1); // Q1..Q4
            var weekNum = ISOWeek.GetWeekOfYear(when.DateTime);
            var week = "W" + weekNum.ToString("00");
            return (year, quarter, month, week);
        }
    }
}
