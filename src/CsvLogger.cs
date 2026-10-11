// 运行日志：一行一条，按月分文件，落盘于 logs\yyyy-MM.log
// 格式：[2026-10-12 00:29:53.892] [GUI] message        —— 信息级不显示级别标签
//       [2026-10-12 00:29:53.892] [GUI] [ERROR] [v1.7] message —— 错误含版本号
using System;
using System.IO;
using System.Text;

namespace AutoDateShift
{
    internal static class CsvLogger
    {
        internal const string Version = "v1.8";

        public static void Info(string source, string message) { Write(source, null, message); }
        public static void Warn(string source, string message) { Write(source, "WARN", message); }

        /// <summary>错误日志：自动附加软件版本，便于定位"哪个版本、什么操作、什么原因"</summary>
        public static void Error(string source, string message)
        {
            Write(source, "ERROR", "[" + Version + "] " + message);
        }

        private static void Write(string source, string level, string message)
        {
            try
            {
                Paths.EnsureDirs();
                string file = Path.Combine(Paths.LogDir, DateTime.Now.ToString("yyyy-MM") + ".log");
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] [" +
                    (source ?? "-") + "] " +
                    (level == null ? "" : "[" + level + "] ") +
                    (message ?? "").Replace("\r", " ").Replace("\n", " ");
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* 日志失败不影响主流程 */ }
        }

        /// <summary>兼容旧签名：把时间调整类字段拼成一行可读文本（英文记录）</summary>
        public static void Log(DateTime now, string action, string shiftDate, string targetWeekday,
            string beforeSys, string afterSys, string realTime, long offsetSeconds, string source, string remark)
        {
            var sb = new StringBuilder(action ?? "Operation");
            if (!string.IsNullOrEmpty(shiftDate))
            {
                sb.Append(": rule date=").Append(shiftDate);
                if (!string.IsNullOrEmpty(targetWeekday)) sb.Append(", target=").Append(targetWeekday);
            }
            if (!string.IsNullOrEmpty(beforeSys) || !string.IsNullOrEmpty(afterSys))
            {
                sb.Append(", system time ").Append(beforeSys).Append(" -> ").Append(afterSys);
                if (!string.IsNullOrEmpty(realTime)) sb.Append(" (real time ").Append(realTime).Append(")");
            }
            if (offsetSeconds != 0)
                sb.Append(", offset ").Append(offsetSeconds / 86400).Append(" day(s)");
            if (!string.IsNullOrEmpty(remark)) sb.Append("; note: ").Append(remark);
            Info(source, sb.ToString());
        }
    }
}
