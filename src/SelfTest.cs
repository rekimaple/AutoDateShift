// SelfTest：假时钟仿真，不改动真实系统时间。覆盖 DESIGN.md 第 6 节核心场景。
// 用法：BellDateShift.exe --selftest --datadir <临时目录>
using System;

namespace AutoDateShift
{
    internal class FakeClock : ISystemClock
    {
        public DateTime Now;
        public DateTime GetLocalNow() { return Now; }
        public void AddSeconds(double delta) { Now = Now.AddSeconds(delta); }
    }

    internal static class SelfTest
    {
        private static int _passed, _failed;

        public static int Run()
        {
            Console.WriteLine("== AutoDateShift 自检（假时钟，不改真实系统时间）==");
            Paths.EnsureDirs();

            // 2026-10-10 是周六，2026-10-11 是周日，2026-10-12 是周一
            var cfg = new Config();
            cfg.Entries.Add(new ConfigEntry { Date = "2026-10-10", TargetWeekday = 3, Note = "调休：周六上周三的课" });
            cfg.Entries.Add(new ConfigEntry { Date = "2026-10-11", TargetWeekday = 4, Note = "调休：周日上周四的课" });

            var state = new State();
            var clock = new FakeClock { Now = new DateTime(2026, 10, 10, 0, 0, 30) }; // 真实周六凌晨

            // 场景1：调休日过零点 → 伪装周六为周三（-3天）
            string r = ShiftEngine.Tick(clock, cfg, state, "SelfTest");
            Check("S1 应用调整动作", r != null && r.StartsWith("应用调整"));
            Check("S1 系统显示周三 2026-10-07", clock.Now == new DateTime(2026, 10, 7, 0, 0, 30));
            Check("S1 偏移 -3 天", state.AppliedOffsetSeconds == -3L * ShiftEngine.SecondsPerDay);

            // 场景2：当天内重复自检 → 无动作（幂等）
            r = ShiftEngine.Tick(clock, cfg, state, "SelfTest");
            Check("S2 重复自检无动作", r == null);
            Check("S2 时钟未被二次平移", clock.Now == new DateTime(2026, 10, 7, 0, 0, 30));

            // 场景3：相邻调休日（周日→周四，同为 -3 天偏移）→ 跨日无需再平移，星期自然正确
            clock.Now = clock.Now.AddHours(23.99); // 推进到真实周日凌晨（显示时间接近 10-08 00:00）
            clock.Now = new DateTime(2026, 10, 8, 0, 0, 40);   // 显示周四凌晨 = 真实周日
            r = ShiftEngine.Tick(clock, cfg, state, "SelfTest");
            Check("S3 相邻调休同偏移不动作", r == null);
            Check("S3 显示日期为周四", Weekdays.Num(clock.Now) == 4);

            // 场景4：调休结束（真实周一，无条目）→ 还原真实日期
            clock.Now = new DateTime(2026, 10, 9, 0, 1, 0);    // 显示周五凌晨 = 真实周一 10-12
            r = ShiftEngine.Tick(clock, cfg, state, "SelfTest");
            Check("S4 自动还原动作", r != null && r.StartsWith("还原真实日期"));
            Check("S4 还原到真实周一 2026-10-12", clock.Now == new DateTime(2026, 10, 12, 0, 1, 0));
            Check("S4 偏移归零", state.AppliedOffsetSeconds == 0);

            // 场景5：伪装中途"重启"——从磁盘恢复 state，不重复平移
            JsonStore.Write(Paths.StateFile, state); // 先落一个伪装中状态
            var state2 = JsonStore.Read<State>(Paths.StateFile);
            var clock2 = new FakeClock { Now = new DateTime(2026, 10, 7, 8, 30, 0) }; // 伪装中的周三早晨
            state2.AppliedOffsetSeconds = -3L * ShiftEngine.SecondsPerDay;
            r = ShiftEngine.Tick(clock2, cfg, state2, "SelfTest");
            Check("S5 重启后续跑不误判", r == null); // 真实=10-10 周六有条目 desired=-3d 与 state 一致
            Check("S5 时钟不变", clock2.Now == new DateTime(2026, 10, 7, 8, 30, 0));

            // 场景6：偏移量计算——最近的同星期日期（±3天内自动取最近方向）
            Check("S6 周六→周五 = -1天", ShiftEngine.DesiredOffset(Mk("2026-10-10", 5), new DateTime(2026, 10, 10)) == -1L * ShiftEngine.SecondsPerDay);
            Check("S6 周日→周五 = -2天", ShiftEngine.DesiredOffset(Mk("2026-10-11", 5), new DateTime(2026, 10, 11)) == -2L * ShiftEngine.SecondsPerDay);
            Check("S6 周日→周三 = +3天(取最近)", ShiftEngine.DesiredOffset(Mk("2026-10-11", 3), new DateTime(2026, 10, 11)) == 3L * ShiftEngine.SecondsPerDay);
            Check("S6 周六→周三 = -3天", ShiftEngine.DesiredOffset(Mk("2026-10-10", 3), new DateTime(2026, 10, 10)) == -3L * ShiftEngine.SecondsPerDay);
            Check("S6 无条目 = 0", ShiftEngine.DesiredOffset(Mk("2026-10-10", 5), new DateTime(2026, 10, 13)) == 0);

            // 场景7：按日期模式——任意目标日期
            var cfgDate = new Config();
            cfgDate.Entries.Add(new ConfigEntry { Date = "2026-10-10", Mode = "date", TargetDate = "2026-10-20" });
            Check("S7 按日期 = +10天", ShiftEngine.DesiredOffset(cfgDate, new DateTime(2026, 10, 10)) == 10L * ShiftEngine.SecondsPerDay);
            cfgDate.Entries[0].TargetDate = "2026-10-01";
            Check("S7 按日期 = -9天", ShiftEngine.DesiredOffset(cfgDate, new DateTime(2026, 10, 10)) == -9L * ShiftEngine.SecondsPerDay);

            Console.WriteLine("== 结果: 通过 " + _passed + " / 失败 " + _failed + " ==");
            return _failed == 0 ? 0 : 1;
        }

        private static Config Mk(string date, int wd)
        {
            var c = new Config();
            c.Entries.Add(new ConfigEntry { Date = date, TargetWeekday = wd });
            return c;
        }

        private static void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (ok) _passed++; else _failed++;
        }
    }
}
