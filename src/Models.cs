// 数据模型与路径管理
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace AutoDateShift
{
    /// <summary>
    /// 数据目录：优先级 --datadir 参数 > 注册表 HKLM\SOFTWARE\AutoDateShift\DataDir（GUI 自定义）>
    /// 默认 C:\ProgramData\AutoDateShift。注册表方式让 SYSTEM 账户的 Worker 与 GUI 读到同一路径。
    /// </summary>
    internal static class Paths
    {
        private static string _overrideDir; // --datadir 指定
        private static string _cachedDir;

        public static string DataDir
        {
            get
            {
                if (_cachedDir == null)
                    _cachedDir = _overrideDir ?? ReadRegistryDataDir() ?? DefaultDataDir();
                return _cachedDir;
            }
        }

        public static string DefaultDataDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AutoDateShift");
        }

        public static void OverrideDataDir(string dir) { _overrideDir = dir; _cachedDir = null; }

        /// <summary>GUI 自定义数据目录（写注册表；null = 恢复默认）</summary>
        public static void SetCustomDataDir(string dir)
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\AutoDateShift"))
            {
                if (dir == null) k.DeleteValue("DataDir", false);
                else k.SetValue("DataDir", dir);
            }
            _cachedDir = null;
        }

        public static bool IsCustomDataDir()
        {
            return _overrideDir == null && ReadRegistryDataDir() != null;
        }

        private static string ReadRegistryDataDir()
        {
            try
            {
                var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AutoDateShift");
                if (k == null) return null;
                var v = k.GetValue("DataDir") as string;
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
            catch { return null; }
        }

        public static string ConfigFile { get { return Path.Combine(DataDir, "config.json"); } }
        public static string StateFile { get { return Path.Combine(DataDir, "state.json"); } }
        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }
        public static string LogDir { get { return Path.Combine(DataDir, "logs"); } }

        public static void EnsureDirs()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LogDir);
        }
    }

    /// <summary>一条调整规则：某天伪装成星期几（按星期）或某个具体日期（按日期）</summary>
    [DataContract]
    internal class ConfigEntry
    {
        [DataMember(Name = "date")] public string Date;            // yyyy-MM-dd（真实日期）
        [DataMember(Name = "targetWeekday")] public int TargetWeekday; // 1=周一 … 7=周日（按星期模式）
        [DataMember(Name = "note")] public string Note;
        /// <summary>"date"=按日期模式；null/"weekday"=按星期模式（兼容旧配置）</summary>
        [DataMember(Name = "mode")] public string Mode;
        [DataMember(Name = "targetDate")] public string TargetDate;    // yyyy-MM-dd（按日期模式）

        public bool IsDateMode { get { return Mode == "date"; } }

        public DateTime GetDate() { return DateTime.ParseExact(Date, "yyyy-MM-dd", null); }

        /// <summary>调整目标的显示文本（周三 / 2026-10-20）</summary>
        public string TargetDisplay()
        {
            return IsDateMode ? TargetDate : Weekdays.Name(TargetWeekday);
        }

        /// <summary>英文目标显示（日志记录用）</summary>
        public string TargetDisplayEn()
        {
            return IsDateMode ? TargetDate : Weekdays.NameEn(TargetWeekday);
        }
    }

    [DataContract]
    internal class Config
    {
        [DataMember(Name = "entries")] public List<ConfigEntry> Entries = new List<ConfigEntry>();

        public ConfigEntry FindByDate(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            foreach (var e in Entries)
                if (e.Date == key) return e;
            return null;
        }
    }

    /// <summary>GUI 偏好设置（实时保存）</summary>
    [DataContract]
    internal class Settings
    {
        [DataMember(Name = "closeToTray")] public bool CloseToTray = true;
        [DataMember(Name = "useNtp")] public bool UseNtp;
        [DataMember(Name = "ntpServer")] public string NtpServer = "ntp.aliyun.com";
        /// <summary>断电重启后服务策略：Memory=记忆上次状态 / AlwaysOn=自动开启 / AlwaysOff=保持关闭</summary>
        [DataMember(Name = "servicePolicy")] public string ServicePolicy = "Memory";
        /// <summary>界面语言：zh / en</summary>
        [DataMember(Name = "language")] public string Language = "zh";
        /// <summary>自检间隔（秒），默认 60；有效范围 5~86400</summary>
        [DataMember(Name = "checkIntervalSeconds")] public int CheckIntervalSeconds = 60;
        /// <summary>仅在每日零点后自检一次（勾选后忽略自检间隔）</summary>
        [DataMember(Name = "dailyOnce")] public bool DailyOnce;
    }

    /// <summary>运行状态：已应用的偏移量（伪装时间 - 真实时间，单位秒）</summary>
    [DataContract]
    internal class State
    {
        [DataMember(Name = "appliedOffsetSeconds")] public long AppliedOffsetSeconds;
        [DataMember(Name = "appliedSince")] public string AppliedSince; // 本地时间字符串，可空
        [DataMember(Name = "lastTick")] public string LastTick;         // Worker 心跳（系统时间域，仅展示用）
        /// <summary>Worker 心跳（真实时间域）：伪装状态下也能正确判断服务存活</summary>
        [DataMember(Name = "lastTickReal")] public string LastTickReal;
        /// <summary>后台服务是否应运行（停止后规则不再生效）</summary>
        [DataMember(Name = "serviceEnabled")] public bool ServiceEnabled = true;
        /// <summary>Worker 进程 PID（供"停止"按钮精确结束进程）</summary>
        [DataMember(Name = "workerPid")] public int WorkerPid;
        /// <summary>每日单次自检模式下，最近一次已自检的真实日期（yyyy-MM-dd）</summary>
        [DataMember(Name = "lastDailyCheck")] public string LastDailyCheck;
    }

    internal static class Weekdays
    {
        private static readonly string[] Names = { "", "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        private static readonly string[] NamesEn = { "", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        /// <summary>Monday=1 … Sunday=7</summary>
        public static int Num(DateTime d)
        {
            int n = (int)d.DayOfWeek; // Sunday=0
            return n == 0 ? 7 : n;
        }

        public static string Name(int num)
        {
            return (num >= 1 && num <= 7) ? Names[num] : "?";
        }

        public static string NameEn(int num)
        {
            return (num >= 1 && num <= 7) ? NamesEn[num] : "?";
        }
    }
}
