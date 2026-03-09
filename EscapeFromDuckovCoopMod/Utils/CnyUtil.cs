using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EscapeFromDuckovCoopMod.Utils
{
    public static class CnyUtil
    {
        private static readonly ChineseLunisolarCalendar CLC = new ChineseLunisolarCalendar();

        /// <summary>
        /// 今天是否为：除夕 或 春节（正月初一）
        /// </summary>
        public static bool IsChuxiOrSpringFestivalToday()
        {
            // 中国：UTC+8；如果只用本机时间也可换成 DateTime.Today
            DateTime today = DateTime.UtcNow.AddHours(8).Date;

            // 1) 春节：农历正月初一
            GetLunarMonthDay(today, out int m, out int d, out bool leap);
            if (!leap && m == 1 && d == 1)
                return true;

            // 2) 除夕：用“明天是正月初一”判断最稳（避免腊月有闰月/大小月差异）
            DateTime tomorrow = today.AddDays(1);
            GetLunarMonthDay(tomorrow, out int tm, out int td, out bool tLeap);
            if (!tLeap && tm == 1 && td == 1)
                return true;

            return false;
        }

        private static void GetLunarMonthDay(DateTime date, out int lunarMonth, out int lunarDay, out bool isLeapMonth)
        {
            int y = CLC.GetYear(date);
            int m = CLC.GetMonth(date);       // 可能包含闰月偏移
            int d = CLC.GetDayOfMonth(date);

            int leapMonth = CLC.GetLeapMonth(y); // 0=无闰月；否则 1..13（插入位置）

            isLeapMonth = false;
            lunarMonth = m;
            lunarDay = d;

            if (leapMonth != 0)
            {
                if (m == leapMonth)
                {
                    isLeapMonth = true;
                    lunarMonth = m - 1;
                }
                else if (m > leapMonth)
                {
                    lunarMonth = m - 1;
                }
            }
        }
    }
}
