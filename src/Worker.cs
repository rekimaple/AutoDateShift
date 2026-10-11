// Worker：无窗口后台自检循环（由任务计划程序或 GUI"启动后台服务"拉起）
// 两种模式：固定间隔自检（默认 60 秒，可配置）/ 仅在每日零点后自检一次
using System;
using System.Threading;

namespace AutoDateShift
{
    internal static class Worker
    {
        private const int DefaultIntervalSec = 60;

        public static int Run()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Global\AutoDateShiftWorker", out createdNew))
            {
                if (!createdNew) return 0; // 已有实例在跑，静默退出
                Paths.EnsureDirs();

                // 断电重启策略：保持关闭 / 记忆状态(上次为停止) → 不启动
                var settings = JsonStore.Read<Settings>(Paths.SettingsFile);
                var st0 = JsonStore.Read<State>(Paths.StateFile);
                string pol = string.IsNullOrEmpty(settings.ServicePolicy) ? "Memory" : settings.ServicePolicy;
                bool shouldRun = pol == "AlwaysOn" || (pol == "Memory" && st0.ServiceEnabled);
                if (!shouldRun)
                {
                    CsvLogger.Info("Worker", "Worker not started: power-loss policy=" + PolicyNameEn(pol) + ", skipped by policy");
                    return 0;
                }

                CsvLogger.Info("Worker", "Worker started: PID=" + System.Diagnostics.Process.GetCurrentProcess().Id +
                    ", policy=" + PolicyNameEn(pol) + ", mode=" + (settings.DailyOnce ? "daily-once" : "interval " + EffectiveInterval(settings) + "s"));

                while (true)
                {
                    int sleepSec = DefaultIntervalSec;
                    try
                    {
                        // 每轮重读设置，间隔/模式修改即时生效，无需重启服务
                        var cfgSettings = JsonStore.Read<Settings>(Paths.SettingsFile);

                        // 服务被"停止"（规则不再生效）→ 退出进程
                        var st = JsonStore.Read<State>(Paths.StateFile);
                        if (!st.ServiceEnabled)
                        {
                            CsvLogger.Info("Worker", "Service stopped: switch turned off, rules deactivated, Worker exiting");
                            return 0;
                        }

                        if (cfgSettings.DailyOnce)
                        {
                            // 每日单次模式：心跳照常（供 GUI 判断存活），仅在真实日期跨天后自检一次
                            Heartbeat();
                            DateTime realNow = DateTime.Now.AddSeconds(-st.AppliedOffsetSeconds);
                            string today = realNow.ToString("yyyy-MM-dd");
                            if (st.LastDailyCheck != today)
                            {
                                TickOnce(false);
                                var st2 = JsonStore.Read<State>(Paths.StateFile);
                                st2.LastDailyCheck = today;
                                JsonStore.Write(Paths.StateFile, st2);
                            }
                            // 固定 1 分钟唤醒一次，保证零点后 1 分钟内触发
                        }
                        else
                        {
                            sleepSec = EffectiveInterval(cfgSettings);
                            TickOnce(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        CsvLogger.Error("Worker", ex.Message);
                    }
                    Thread.Sleep(sleepSec * 1000);
                }
            }
        }

        /// <summary>有效自检间隔（秒）：读取设置并钳制在 5~86400，默认 60</summary>
        internal static int EffectiveInterval(Settings s)
        {
            int v = s.CheckIntervalSeconds;
            if (v <= 0) return DefaultIntervalSec;
            if (v < 5) return 5;
            if (v > 86400) return 86400;
            return v;
        }

        /// <summary>更新心跳与 PID（真实时间域，伪装状态下也能正确判断存活）</summary>
        private static void Heartbeat()
        {
            var s2 = JsonStore.Read<State>(Paths.StateFile);
            DateTime now = DateTime.Now;
            s2.LastTick = now.ToString("yyyy-MM-dd HH:mm:ss");
            s2.LastTickReal = now.AddSeconds(-s2.AppliedOffsetSeconds).ToString("yyyy-MM-dd HH:mm:ss");
            s2.WorkerPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            JsonStore.Write(Paths.StateFile, s2);
        }

        internal static string PolicyName(string pol)
        {
            if (pol == "AlwaysOn") return "自动开启";
            if (pol == "AlwaysOff") return "保持关闭";
            return "记忆上次状态";
        }

        /// <summary>英文策略名（日志记录用）</summary>
        internal static string PolicyNameEn(string pol)
        {
            if (pol == "AlwaysOn") return "AlwaysOn";
            if (pol == "AlwaysOff") return "AlwaysOff";
            return "Memory";
        }

        /// <summary>单次自检。updateHeartbeat=true 时才更新心跳与 PID（GUI"立即自检"传 false，避免冒充后台服务存活）</summary>
        public static string TickOnce(bool updateHeartbeat)
        {
            Config cfg;
            if (!JsonStore.TryRead(Paths.ConfigFile, out cfg))
            {
                CsvLogger.Error("Worker", "Failed to parse config.json, no action taken this round");
                return "配置解析失败，未动作";
            }
            var state = JsonStore.Read<State>(Paths.StateFile);
            string result = ShiftEngine.Tick(new RealSystemClock(), cfg, state, "Worker");

            if (updateHeartbeat) Heartbeat();
            return result ?? "今日无需调整";
        }
    }
}
