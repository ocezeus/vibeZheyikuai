using System;
using System.Collections.Generic;
using System.Globalization;
using WorkCountdown;

// 整理前后的样例输出对照探针：只走模块公开接口，打印固定场景下的既有显示/计算事实。
// 不参与 build-tests.ps1 的两套正式套件；用 csc 单独编译后重定向输出即可对照。
internal static class SampleOutputs
{
    private static Settings BaseSettings()
    {
        var s = new Settings();
        s.mode = "fixed";
        s.startTime = "09:00";
        s.endTime = "18:00";
        s.lunchStart = "11:50";
        s.lunchEnd = "13:30";
        s.lunchDeduct = false;
        s.eveningEnabled = true;
        s.eveningStart = "18:00";
        s.eveningEnd = "18:45";
        s.workdays = new bool[] { false, true, true, true, true, true, false };
        s.lark.enabled = true;
        s.lark.chatId = "oc_sample";
        s.salary.monthly = 10000m;
        s.salary.payday = 15;
        s.Normalize();
        return s;
    }

    private static string Guard(string text)
    {
        return text == null ? "<null>" : text;
    }

    private static void Line(string key, string value)
    {
        Console.WriteLine(key + " = " + Guard(value));
    }

    public static void Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var s = BaseSettings();
        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = new DateTime(2026, 11, 16, 9, 5, 0);
        var theme = Themes.Get("midnight");

        Console.WriteLine("[countdown/status 固定班次 09:00-18:00, 打卡 09:05]");
        DateTime[] times = new DateTime[]
        {
            new DateTime(2026, 11, 16, 8, 30, 0),
            new DateTime(2026, 11, 16, 9, 5, 0),
            new DateTime(2026, 11, 16, 10, 0, 0),
            new DateTime(2026, 11, 16, 11, 30, 0),
            new DateTime(2026, 11, 16, 12, 0, 0),
            new DateTime(2026, 11, 16, 14, 0, 0),
            new DateTime(2026, 11, 16, 17, 59, 0),
            new DateTime(2026, 11, 16, 18, 10, 0),
            new DateTime(2026, 11, 16, 18, 50, 0)
        };
        foreach (var t in times)
        {
            var st = StatusBuilder.Build(s, snap, t, theme);
            Line("status " + t.ToString("HH:mm"),
                st.state + " / " + st.headline + " / big=" + st.big + " / sub=" + st.sub
                + " / extra=" + st.extra + " / lunch=" + st.lunch + "[" + st.lunchLabel + "|" + st.lunchValue + "]");
        }

        Console.WriteLine("[countdown 文案]");
        var ev = new ReminderEvent();
        ev.id = "e1";
        ev.title = "评审";
        ev.start = "2026-11-16 15:00";
        ev.end = "2026-11-16 16:00";
        ev.source = "local";
        Line("countdown 14:55", Reminders.CountdownText(s, ev, new DateTime(2026, 11, 16, 14, 55, 0)));
        Line("countdown 14:59:40", Reminders.CountdownText(s, ev, new DateTime(2026, 11, 16, 14, 59, 40)));
        Line("countdown 15:00", Reminders.CountdownText(s, ev, new DateTime(2026, 11, 16, 15, 0, 0)));
        ev.start = "2026-11-16 17:00";
        Line("countdown 15:00->17:00", Reminders.CountdownText(s, ev, new DateTime(2026, 11, 16, 15, 0, 0)));
        Line("timerange", Reminders.TimeRange(ev));
        ev.end = "";
        Line("timerange 无结束", Reminders.TimeRange(ev));

        Console.WriteLine("[调休 / 休息日]");
        var ex = new Settings();
        ex.lunchDeduct = true;
        ex.lunchStart = "11:50";
        ex.lunchEnd = "13:30";
        ex.eveningEnabled = false;
        ex.workdays = new bool[] { false, true, true, true, true, true, false };
        ex.exceptions.Add(new HolidayEntry { date = "2026-11-21", kind = "work" });
        ex.exceptions.Add(new HolidayEntry { date = "2026-11-18", kind = "off" });
        ex.Normalize();
        Line("调休 周六 2026-11-21", Policy.Build(ex, new DateTime(2026, 11, 21)).isWorkday
            + " / " + Policy.Build(ex, new DateTime(2026, 11, 21)).dayLabel);
        Line("休息 周三 2026-11-18", Policy.Build(ex, new DateTime(2026, 11, 18)).isWorkday
            + " / " + Policy.Build(ex, new DateTime(2026, 11, 18)).dayLabel);
        Line("普通周六 2026-11-14", Policy.Build(ex, new DateTime(2026, 11, 14)).isWorkday
            + " / " + Policy.Build(ex, new DateTime(2026, 11, 14)).dayLabel);
        Line("下次上班（周六休息时从 11-14 起算）",
            Policy.NextWorkdayStart(ex, new DateTime(2026, 11, 14, 10, 0, 0)).ToString("yyyy-MM-dd HH:mm"));

        Console.WriteLine("[午休扣减 / 预计下班]");
        var lunch = BaseSettings();
        lunch.lunchDeduct = true;
        lunch.mode = "flex";
        lunch.flexMinutes = 480;
        var plan = Policy.Build(lunch, new DateTime(2026, 11, 16));
        Line("flex 480 打卡 09:30 预计下班", plan.TargetEnd(new DateTime(2026, 11, 16, 9, 30, 0)).ToString("HH:mm"));
        var work = BaseSettings();
        work.mode = "fixed";
        Line("固定班次 09:00-18:00 不扣午休 预计下班",
            Policy.Build(work, new DateTime(2026, 11, 16)).TargetEnd(new DateTime(2026, 11, 16, 9, 0, 0)).ToString("HH:mm"));
        work.lunchDeduct = true;
        Line("固定班次 扣午休 预计下班",
            Policy.Build(work, new DateTime(2026, 11, 16)).TargetEnd(new DateTime(2026, 11, 16, 9, 0, 0)).ToString("HH:mm"));

        Console.WriteLine("[工资估算 月薪 10000 发薪日 15]");
        var salary = BaseSettings();
        salary.salary.monthly = 10000m;
        salary.salary.payday = 15;
        var sal = SalaryCalc.Compute(salary, snap, new DateTime(2026, 11, 16, 15, 30, 0));
        Line("daily", SalaryCalc.Money3(sal.daily));
        Line("hourly", SalaryCalc.Money3(sal.hourly));
        Line("requiredWorkdays", sal.requiredWorkdays.ToString(CultureInfo.InvariantCulture));
        Line("elapsedWorkdays", sal.elapsedWorkdays.ToString(CultureInfo.InvariantCulture));
        Line("attendedDays", sal.attendedDays.ToString("0.###", CultureInfo.InvariantCulture));
        Line("today", SalaryCalc.Money3(sal.today));
        Line("month", SalaryCalc.Money3(sal.month));
        Line("worked", Policy.CompactDuration(sal.worked));
        Line("progress", sal.progress.ToString("0.0000", CultureInfo.InvariantCulture));
        Line("paydayDate", sal.paydayDate.ToString("yyyy-MM-dd"));
        Line("daysToPayday", sal.daysToPayday.ToString(CultureInfo.InvariantCulture));
        Line("note", sal.note);

        var noClock = BaseSettings();
        noClock.salary.monthly = 10000m;
        noClock.lark.enabled = false;
        noClock.lark.chatId = "";
        noClock.Normalize();
        var sal2 = SalaryCalc.Compute(noClock, null, new DateTime(2026, 11, 16, 16, 0, 0));
        Line("未接飞书 today(16:00)", SalaryCalc.Money3(sal2.today));
        Line("未接飞书 month(16:00)", SalaryCalc.Money3(sal2.month));
        Line("未接飞书 worked", Policy.CompactDuration(sal2.worked));

        Console.WriteLine("[提醒常驻时长]");
        var engine = new ReminderEngine();
        var brk = new ReminderEvent();
        brk.id = "lunch";
        brk.title = "午休";
        brk.start = "2026-11-16 11:50";
        brk.end = "2026-11-16 13:30";
        brk.source = "break";
        Line("break 12:00 常驻到", engine.HoldUntil(s, brk, new DateTime(2026, 11, 16, 12, 0, 0)).ToString("HH:mm"));
        var alert = new ReminderEvent();
        alert.id = "alert:offwork";
        alert.source = "alert";
        alert.start = "2026-11-16 18:00";
        Line("下班提醒常驻到", engine.HoldUntil(s, alert, new DateTime(2026, 11, 16, 18, 0, 0)) == DateTime.MaxValue ? "MaxValue" : "not-max");
        var meeting = new ReminderEvent();
        meeting.id = "m1";
        meeting.title = "评审";
        meeting.start = "2026-11-16 20:00";
        meeting.end = "2026-11-16 21:00";
        meeting.source = "lark";
        Line("会议 19:00 常驻到", engine.HoldUntil(s, meeting, new DateTime(2026, 11, 16, 19, 0, 0)).ToString("HH:mm"));
        Console.WriteLine("[END]");
    }
}
