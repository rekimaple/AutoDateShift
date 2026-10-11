// 时间核心：SetSystemTime 封装 + 偏移量引擎（引擎纯逻辑，可注入假时钟测试）
using System;
using System.Runtime.InteropServices;

namespace AutoDateShift
{
    /// <summary>系统时钟抽象：Worker 用真实时钟，SelfTest 用假时钟</summary>
    internal interface ISystemClock
    {
        DateTime GetLocalNow();
        /// <summary>把系统时间整体平移 deltaSeconds（正=调快，负=调慢）</summary>
        void AddSeconds(double deltaSeconds);
    }

    internal class RealSystemClock : ISystemClock
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTime(out SYSTEMTIME st);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemTime(ref SYSTEMTIME st);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpPrivilegeName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        public DateTime GetLocalNow() { return DateTime.Now; }

        public void AddSeconds(double deltaSeconds)
        {
            EnableSystemTimePrivilege();
            SYSTEMTIME st;
            if (!GetSystemTime(out st))
                throw new InvalidOperationException("GetSystemTime 失败: " + Marshal.GetLastWin32Error());
            var utc = new DateTime(st.Year, st.Month, st.Day, st.Hour, st.Minute, st.Second, st.Milliseconds, DateTimeKind.Utc);
            utc = utc.AddSeconds(deltaSeconds);
            st.Year = (ushort)utc.Year; st.Month = (ushort)utc.Month; st.Day = (ushort)utc.Day;
            st.Hour = (ushort)utc.Hour; st.Minute = (ushort)utc.Minute; st.Second = (ushort)utc.Second;
            st.Milliseconds = (ushort)utc.Millisecond;
            if (!SetSystemTime(ref st))
                throw new UnauthorizedAccessException("SetSystemTime 失败（权限不足）: " + Marshal.GetLastWin32Error());
        }

        private static void EnableSystemTimePrivilege()
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token)) return;
            try
            {
                LUID luid;
                if (LookupPrivilegeValue(null, "SeSystemtimePrivilege", out luid))
                {
                    var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                    AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
            }
            finally { CloseHandle(token); }
        }
    }

    /// <summary>
    /// 偏移引擎：比对"今日应有偏移"与"已应用偏移"，不一致则平移。
    /// 约定：appliedOffset = 系统显示时间 - 真实时间（秒）。伪装周六为周三 → appliedOffset = -3*86400。
    /// </summary>
    internal static class ShiftEngine
    {
        public const int SecondsPerDay = 86400;

        /// <summary>计算某真实日期应有的偏移量（秒）。无规则 → 0；按星期 → 最近的同星期日期（±3天）；按日期 → 任意目标日期</summary>
        public static long DesiredOffset(Config cfg, DateTime realDate)
        {
            var e = cfg.FindByDate(realDate);
            if (e == null) return 0;
            if (e.IsDateMode)
            {
                DateTime td;
                if (!DateTime.TryParseExact(e.TargetDate, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out td)) return 0;
                return (long)(td.Date - realDate.Date).Days * SecondsPerDay;
            }
            return (long)NearestDayDiff(e.TargetWeekday, Weekdays.Num(realDate)) * SecondsPerDay;
        }

        /// <summary>从当前星期到目标星期的最近平移天数（-3..+3，0 表示相同）</summary>
        public static int NearestDayDiff(int targetWeekday, int currentWeekday)
        {
            int diff = ((targetWeekday - currentWeekday) % 7 + 7) % 7; // 0..6
            if (diff > 3) diff -= 7; // 取最近方向
            return diff;
        }

        /// <summary>
        /// 执行一次自检。返回动作描述（无动作返回 null）。抛异常由调用方记日志。
        /// </summary>
        public static string Tick(ISystemClock clock, Config cfg, State state, string source)
        {
            DateTime fakeNow = clock.GetLocalNow();
            DateTime realNow = fakeNow.AddSeconds(-state.AppliedOffsetSeconds);
            long desired = DesiredOffset(cfg, realNow.Date);
            if (desired == state.AppliedOffsetSeconds) return null;

            long delta = desired - state.AppliedOffsetSeconds;
            string before = fakeNow.ToString("yyyy-MM-dd HH:mm:ss");
            clock.AddSeconds(delta);
            DateTime after = clock.GetLocalNow();

            var entry = cfg.FindByDate(realNow.Date);
            // 日志用英文（留痕口径统一），返回值用中文（GUI 弹窗展示用）
            string actionEn = desired == 0 ? "Real date restored" : (state.AppliedOffsetSeconds == 0 ? "Shift applied" : "Shift switched");
            string actionZh = desired == 0 ? "还原真实日期" : (state.AppliedOffsetSeconds == 0 ? "应用调整" : "切换调整");
            string shiftDate = entry != null ? entry.Date : "";
            string targetWd = entry != null ? entry.TargetDisplayEn() : "";
            string note = entry != null ? entry.Note : "";

            CsvLogger.Log(DateTime.Now, actionEn, shiftDate, targetWd, before,
                after.ToString("yyyy-MM-dd HH:mm:ss"), realNow.ToString("yyyy-MM-dd HH:mm:ss"),
                desired, source, note);

            state.AppliedOffsetSeconds = desired;
            state.AppliedSince = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            JsonStore.Write(Paths.StateFile, state);
            return actionZh + "（偏移 " + desired / SecondsPerDay + " 天）";
        }
    }
}
