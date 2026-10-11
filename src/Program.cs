// 入口：默认 GUI（单实例）；--worker 后台模式；--selftest 仿真自检；--datadir <路径> 覆盖数据目录
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AutoDateShift
{
    internal static class Program
    {
        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        /// <summary>GUI 唤起事件：第二个实例通过它通知首个实例弹出窗口</summary>
        internal const string GuiShowEventName = @"Local\AutoDateShiftGuiShow";

        [STAThread]
        private static int Main(string[] args)
        {
            bool worker = false, selftest = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--worker") worker = true;
                else if (a == "--selftest") selftest = true;
                else if (a == "--datadir" && i + 1 < args.Length) Paths.OverrideDataDir(args[++i]);
            }

            if (selftest)
            {
                AttachConsole(ATTACH_PARENT_PROCESS); // winexe 无控制台，附着到父 cmd 以显示测试结果
                int code = SelfTest.Run();
                Console.WriteLine();
                return code;
            }
            if (worker) return Worker.Run();

            // 单实例：已运行则唤起已有窗口后退出，防止多开造成数据冲突
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\AutoDateShiftGui", out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        var ev = EventWaitHandle.OpenExisting(GuiShowEventName);
                        ev.Set();
                    }
                    catch { /* 首个实例尚未就绪 */ }
                    // 兼容"以管理员身份重新启动"场景：旧实例退出后，本实例等待并接管单实例锁
                    try { createdNew = mutex.WaitOne(3000); }
                    catch (AbandonedMutexException) { createdNew = true; }
                    if (!createdNew) return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}
