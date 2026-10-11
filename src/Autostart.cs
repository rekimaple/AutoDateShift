// 开机自启管理：通过 schtasks 创建/删除/查询 SYSTEM + 最高权限的任务计划
using System;
using System.Diagnostics;
using System.Reflection;

namespace AutoDateShift
{
    internal static class Autostart
    {
        public const string TaskName = "AutoDateShiftWorker";

        public static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        public static bool IsInstalled()
        {
            int code; string output;
            Run("schtasks", "/query /tn \"" + TaskName + "\"", out code, out output);
            return code == 0;
        }

        public static string Install()
        {
            string args = "/create /f /tn \"" + TaskName + "\" /sc onstart /ru SYSTEM /rl HIGHEST /tr \"\\\"" + ExePath + "\\\" --worker\"";
            int code; string output;
            Run("schtasks", args, out code, out output);
            return code == 0 ? null : output;
        }

        public static string Uninstall()
        {
            int code; string output;
            Run("schtasks", "/delete /f /tn \"" + TaskName + "\"", out code, out output);
            return code == 0 ? null : output;
        }

        /// <summary>立即触发一次任务（验证任务可用）</summary>
        public static string RunNow()
        {
            int code; string output;
            Run("schtasks", "/run /tn \"" + TaskName + "\"", out code, out output);
            return code == 0 ? null : output;
        }

        /// <summary>结束正在运行的任务实例</summary>
        public static string EndTask()
        {
            int code; string output;
            Run("schtasks", "/end /tn \"" + TaskName + "\"", out code, out output);
            return code == 0 ? null : output;
        }

        private static void Run(string exe, string args, out int exitCode, out string output)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    exitCode = p.ExitCode;
                    output = (o + e).Trim();
                }
            }
            catch (Exception ex)
            {
                exitCode = -1;
                output = ex.Message;
            }
        }
    }
}
