using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Web.Script.Serialization;
using WorkCountdown;

internal static class Test
{
    private static int failures = 0;

    private static void Check(string name, string actual, string expected)
    {
        bool ok = actual == expected;
        if (!ok) failures++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + "  => " + actual + (ok ? "" : "   (expected " + expected + ")"));
    }

    private static string NewTempDir()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc-settings-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteTempDir(string dir)
    {
        try { System.IO.Directory.Delete(dir, true); } catch { }
    }

    private static string ReadAllText(string path)
    {
        return System.IO.File.ReadAllText(path);
    }

    private static bool FileBytesEqual(string path, byte[] expected)
    {
        try
        {
            byte[] actual = System.IO.File.ReadAllBytes(path);
            if (actual.Length != expected.Length) return false;
            for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) return false;
            return true;
        }
        catch { return false; }
    }

    private static int TempFileCount(string dir)
    {
        try { return System.IO.Directory.GetFiles(dir, "*.tmp-*").Length; } catch { return -1; }
    }

    private static SettingsStore StoreIn(string dir)
    {
        return new SettingsStore(new FileSettingsStorage(), dir, false);
    }

    private static void Main()
    {
        // 探针只读配置，绝不回写用户自己的设置
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        var s = new Settings();
        s.mode = "flex";
        s.flexMinutes = 630;
        s.startTime = "09:00";
        s.lunchDeduct = false;
        s.lark.enabled = true;              // 这些用例都假设接了飞书，有打卡记录
        s.lark.chatId = "oc_test";

        DayOfWeek dow = new DateTime(2026, 11, 3).DayOfWeek;
        Console.WriteLine("2026-11-03 is " + dow + " / " + Policy.WeekdayName(dow));

        // 1. 弹性工时，09:27 打卡，10:00 时
        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = new DateTime(2026, 11, 3, 9, 27, 0);
        var st = StatusBuilder.Build(s, snap, new DateTime(2026, 11, 3, 10, 0, 0), Themes.Get("midnight"));
        Check("10:00 状态", st.state.ToString(), "Working");
        Check("10:00 倒计时", st.big, "09:57:00");
        Check("10:00 预计下班", st.sub, "上班 09:27 · 预计下班 19:57");

        // 2. 刚到点
        st = StatusBuilder.Build(s, snap, new DateTime(2026, 11, 3, 19, 57, 0), Themes.Get("midnight"));
        Check("19:57 状态", st.state.ToString(), "Overtime");
        Check("19:57 倒计时", st.big, "00:00.00");   // 不足一小时：分:秒.厘秒

        // 3. 已下班 20:09
        snap.clockOut = new DateTime(2026, 11, 3, 20, 9, 0);
        st = StatusBuilder.Build(s, snap, new DateTime(2026, 11, 3, 20, 20, 0), Themes.Get("midnight"));
        Check("20:20 状态", st.state.ToString(), "Done");
        Check("20:20 工时", st.big, "10h42m");
        Check("20:20 区间", st.sub, "09:27 → 20:09");

        // 4. 没打卡且已过上班时间
        st = StatusBuilder.Build(s, null, new DateTime(2026, 11, 3, 9, 40, 0), Themes.Get("midnight"));
        Check("9:40 未打卡", st.state.ToString(), "NotClockedIn");
        Check("9:40 已过", st.big, "40:00.00");

        // 5. 早于上班时间
        st = StatusBuilder.Build(s, null, new DateTime(2026, 11, 3, 8, 30, 0), Themes.Get("midnight"));
        Check("8:30 状态", st.state.ToString(), "BeforeWork");
        Check("8:30 距上班", st.big, "30:00.00");

        // 6. 周六休息
        st = StatusBuilder.Build(s, null, new DateTime(2026, 11, 7, 10, 0, 0), Themes.Get("midnight"));
        Check("周六", st.state.ToString(), "Rest");
        Check("周六文案", st.big, "今天休息");

        // 7. 调休上班（周六设为上班）
        s.exceptions.Add(new HolidayEntry { date = "2026-11-07", kind = "work" });
        var plan = Policy.Build(s, new DateTime(2026, 11, 7));
        Check("周六调休", plan.isWorkday.ToString(), "True");
        s.exceptions.Clear();

        // 8. 工作日设为休息
        s.exceptions.Add(new HolidayEntry { date = "2026-11-04", kind = "off" });
        plan = Policy.Build(s, new DateTime(2026, 11, 4));
        Check("周三设为休息", plan.isWorkday.ToString(), "False");
        s.exceptions.Clear();

        // 9. 固定班次 09:00-18:00
        s.mode = "fixed";
        s.endTime = "18:00";
        var snap2 = new AttendanceSnapshot();
        snap2.ok = true;
        snap2.clockIn = new DateTime(2026, 11, 3, 8, 55, 0);
        st = StatusBuilder.Build(s, snap2, new DateTime(2026, 11, 3, 10, 0, 0), Themes.Get("midnight"));
        Check("固定班次 10:00", st.big, "08:00:00");
        Check("固定班次 预计下班", st.sub, "上班 08:55 · 预计下班 18:00");

        // 10. 固定班次 + 扣除午休（午休 12:00-13:00 落在班次内）
        s.lunchDeduct = true;
        s.lunchStart = "12:00";
        s.lunchEnd = "13:00";
        s.endTime = "18:00";
        st = StatusBuilder.Build(s, snap2, new DateTime(2026, 11, 3, 10, 0, 0), Themes.Get("midnight"));
        Check("固定班次扣午休", st.sub, "上班 08:55 · 预计下班 19:00");

        // 11. 弹性工时扣午休（打卡 09:00，应勤 9 小时，午休在区间内）
        s.mode = "flex";
        s.lunchDeduct = true;
        s.flexMinutes = 540;
        var snap3 = new AttendanceSnapshot();
        snap3.ok = true;
        snap3.clockIn = new DateTime(2026, 11, 3, 9, 0, 0);
        var p3 = Policy.Build(s, new DateTime(2026, 11, 3));
        Check("弹性扣午休下班", p3.TargetEnd(new DateTime(2026, 11, 3, 9, 0, 0)).ToString("HH:mm"), "19:00");

        // 12. 时间解析
        int m;
        Check("解析 9:05", SettingsExtensions.TryParseTime("9:05", out m) + "/" + m, "True/545");
        Check("解析 09：30", SettingsExtensions.TryParseTime("09：30", out m) + "/" + m, "True/570");
        Check("非法 25:00", SettingsExtensions.TryParseTime("25:00", out m).ToString(), "False");
        Check("格式化 545", SettingsExtensions.FormatTime(545), "09:05");

        // 13. 时长与时钟格式
        Check("时钟 25 小时", Policy.Clock(TimeSpan.FromHours(25) + TimeSpan.FromMinutes(3)), "25:03:00");
        Check("紧凑 10h38m", Policy.CompactDuration(TimeSpan.FromMinutes(638)), "10h38m");

        // 14. 午休倒计时（第二段时间）
        var theme = Themes.Get("midnight");
        s.lunchStart = "11:50";
        s.lunchEnd = "13:30";
        var snapL = new AttendanceSnapshot();
        snapL.ok = true;
        snapL.clockIn = new DateTime(2026, 11, 3, 9, 27, 0);
        var stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 10, 15, 0), theme);
        Check("午休前", stL.lunchLabel + " " + stL.lunchValue, "距午休 1h35m");
        Check("午休前在倒数", stL.lunchCounting.ToString(), "True");
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 12, 0, 0), theme);
        Check("午休中", stL.lunchLabel + " " + stL.lunchValue, "午休中 还剩 1h30m");
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 13, 30, 0), theme);
        Check("午休结束转晚休", stL.lunch.ToString(), "EveningBefore");
        Check("午休结束后倒数晚休", stL.lunchLabel + " " + stL.lunchValue, "距晚休 4h30m");
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 14, 0, 0), theme);
        Check("午休结束后仍在倒数", stL.lunchCounting.ToString(), "True");
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 18, 10, 0), theme);
        Check("晚休中", stL.lunchLabel + " " + stL.lunchValue, "晚休中 还剩 35m");
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 18, 45, 0), theme);
        Check("晚休结束显示马上下班", stL.lunchLabel, "马上下班");
        Check("晚休结束不再倒数", stL.lunchValue, "");

        // 关掉晚休：午休结束后这一行改成距下班，到点写「马上下班」
        s.eveningEnabled = false;
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 14, 0, 0), theme);
        Check("无晚休时显示距下班", stL.lunchLabel, "距下班");
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 20, 10, 0), theme);
        Check("无晚休时到点显示马上下班", stL.lunchLabel, "马上下班");
        s.eveningEnabled = true;

        // 到了下班时间还没打卡 → 常驻提醒开关
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 20, 10, 0), theme);
        Check("到点没打卡触发常驻提醒", stL.offAlert.ToString(), "True");
        var snapOut = new AttendanceSnapshot();
        snapOut.ok = true;
        snapOut.clockIn = new DateTime(2026, 11, 3, 9, 27, 0);
        snapOut.clockOut = new DateTime(2026, 11, 3, 20, 12, 0);
        stL = StatusBuilder.Build(s, snapOut, new DateTime(2026, 11, 3, 20, 15, 0), theme);
        Check("打卡后自动收起", stL.offAlert.ToString(), "False");
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 19, 0, 0), theme);
        Check("没到点不提醒", stL.offAlert.ToString(), "False");

        s.lunchStart = "25:00";
        stL = StatusBuilder.Build(s, snapL, new DateTime(2026, 11, 3, 10, 15, 0), theme);
        Check("午休时间非法时晚休仍然显示", stL.lunch.ToString(), "EveningBefore");

        // 15. 主题表
        Check("主题数量", Themes.All.Length.ToString(), "39");
        var ids = new HashSet<string>();
        int dup = 0;
        foreach (var th in Themes.All) if (!ids.Add(th.id)) dup++;
        Check("主题 id 不重复", dup.ToString(), "0");
        Check("按 id 取主题", Themes.Get("hong-fengye").name, "红 · 枫叶");
        Check("未知 id 回退默认", Themes.Get("nope").id, "midnight");

        int lowContrast = 0;
        foreach (var th in Themes.All)
        {
            double la = (0.2126 * th.accent.R + 0.7152 * th.accent.G + 0.0722 * th.accent.B) / 255.0;
            double lb = (0.2126 * th.bgBottom.R + 0.7152 * th.bgBottom.G + 0.0722 * th.bgBottom.B) / 255.0;
            if (Math.Abs(la - lb) < 0.12) lowContrast++;
        }
        Check("主色与底色对比足够", lowContrast.ToString(), "0");

        // 16. 日程提醒
        var s16 = new Settings();
        s16.Normalize();
        s16.calendarLeadMinutes = 5;
        s16.eventLeadMinutes = 5;
        var ev = new ReminderEvent();
        ev.id = "local:1";
        ev.title = "产品部周会";
        ev.start = "2026-11-04 10:05";
        ev.source = "local";
        Check("解析日程时间", Reminders.ParseStart(ev).ToString("HH:mm"), "10:05");
        // 提前量窗口 / 开始后 60 秒的边界已由 20.2「提前量边界」「开始后 59 秒 / 满 60 秒」经
        // ReminderEngine.Advance 覆盖；这里不再直接断言 IsDue 的同一规则。
        Check("倒计时说法", Reminders.CountdownText(s16, ev, new DateTime(2026, 11, 4, 10, 0, 0)), "还有 5 分钟");
        Check("倒计时-马上开始", Reminders.CountdownText(s16, ev, new DateTime(2026, 11, 4, 10, 4, 40)), "马上开始");
        s16.events.Add(ev);
        var eng16 = new ReminderEngine();
        Check("手动日程进入待提醒", eng16.Agenda(s16, new ReminderScheduleSnapshot()).Count.ToString(), "1");
        Check("下一个日程", eng16.NextEvent(s16, new ReminderScheduleSnapshot(), new DateTime(2026, 11, 4, 10, 0, 0)).title, "产品部周会");
        ev.leadMinutes = 30;
        Check("单条日程可覆盖提前量", Reminders.IsDue(s16, ev, new DateTime(2026, 11, 4, 9, 40, 0)).ToString(), "True");
        ev.leadMinutes = 0;
        Check("提前量为 0 也有窗口", Reminders.IsDue(s16, ev, new DateTime(2026, 11, 4, 10, 5, 10)).ToString(), "True");

        var larkList = new List<ReminderEvent>();
        var le = new ReminderEvent();
        le.id = "lark:1";
        le.title = "站会";
        le.start = "2026-11-04 10:30";
        le.source = "lark";
        larkList.Add(le);
        Check("飞书日程并入待提醒", eng16.Agenda(s16, new ReminderScheduleSnapshot(larkList, null)).Count.ToString(), "2");
        s16.calendarEnabled = false;
        Check("关掉飞书日程后只剩手动", eng16.Agenda(s16, new ReminderScheduleSnapshot(larkList, null)).Count.ToString(), "1");

        // 17. 完全没接飞书的人：按班次推算，不能显示「还没打卡」
        var s17 = new Settings();
        Check("默认就是固定班次", s17.mode, "fixed");
        Check("默认不接飞书", s17.lark.enabled.ToString(), "False");
        s17.startTime = "09:00";
        s17.endTime = "18:00";
        var st17 = StatusBuilder.Build(s17, null, new DateTime(2026, 11, 3, 10, 0, 0), Themes.Get("midnight"));
        Check("没接飞书按班次推算", st17.state.ToString(), "Working");
        Check("没接飞书 倒计时", st17.big, "08:00:00");
        Check("没接飞书 班次", st17.sub, "上班 09:00 · 预计下班 18:00");
        var st18 = StatusBuilder.Build(s17, null, new DateTime(2026, 11, 3, 8, 30, 0), Themes.Get("midnight"));
        Check("没接飞书 上班前", st18.state.ToString(), "BeforeWork");
        Check("没接飞书 距上班", st18.big, "30:00.00");

        // 18. 会议与单个日程的提前量各自独立；日程带结束时间 / 会议室 / 内容
        var s18 = new Settings();
        s18.calendarLeadMinutes = 5;
        s18.eventLeadMinutes = 12;
        var meet = new ReminderEvent();
        meet.id = "lark:m";
        meet.title = "产品部周会";
        meet.start = "2026-11-05 15:30";
        meet.end = "2026-11-05 16:00";
        meet.place = "会议室 A · 12 层";
        meet.source = "lark";
        Check("会议用会议提前量", Reminders.LeadOf(s18, meet).ToString(), "5");
        Check("会议到点触发", Reminders.IsDue(s18, meet, new DateTime(2026, 11, 5, 15, 25, 0)).ToString(), "True");
        Check("会议早 6 分钟不触发", Reminders.IsDue(s18, meet, new DateTime(2026, 11, 5, 15, 24, 0)).ToString(), "False");

        var local = new ReminderEvent();
        local.id = "local:m";
        local.title = "需求对齐会";
        local.start = "2026-11-04 14:30";
        local.end = "2026-11-04 15:00";
        local.place = "会议室 A";
        local.detail = "过一遍本周排期";
        local.source = "local";
        Check("手动日程用日程提前量", Reminders.LeadOf(s18, local).ToString(), "12");
        Check("手动日程到点触发", Reminders.IsDue(s18, local, new DateTime(2026, 11, 4, 14, 18, 0)).ToString(), "True");
        Check("手动日程早 1 分钟不触发", Reminders.IsDue(s18, local, new DateTime(2026, 11, 4, 14, 17, 0)).ToString(), "False");
        Check("时间区间", Reminders.TimeRange(local), "14:30–15:00");
        Check("只有开始时间时区间取开始", Reminders.TimeRange(le), "10:30");
        Check("结束时间解析", Reminders.ParseEnd(local).ToString("HH:mm"), "15:00");
        local.leadMinutes = 0;
        Check("0 表示跟随默认", Reminders.LeadOf(s18, local).ToString(), "12");
        local.leadMinutes = 3;
        Check("单条覆盖优先于类型默认", Reminders.LeadOf(s18, local).ToString(), "3");
        Check("会议室算是会议", Reminders.IsMeeting(meet).ToString(), "True");
        Check("手动日程不算会议", Reminders.IsMeeting(local).ToString(), "False");

        // 19. 提醒外观：默认值、旧配置兼容、归一化与 JSON 往返。
        var prefs = new Settings();
        Check("默认提醒特效", prefs.ui.reminderEffect, "glow");
        Check("默认减少动态", prefs.ui.reducedMotion.ToString(), "False");
        Check("默认提醒停留", prefs.ui.reminderHoldSeconds.ToString(), "6");
        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
        var oldPrefs = serializer.Deserialize<Settings>("{\"ui\":{\"theme\":\"midnight\"}}");
        oldPrefs.Normalize();
        Check("旧 JSON 特效默认", oldPrefs.ui.reminderEffect, "glow");
        Check("旧 JSON 减少动态默认", oldPrefs.ui.reducedMotion.ToString(), "False");
        Check("旧 JSON 停留默认", oldPrefs.ui.reminderHoldSeconds.ToString(), "6");
        prefs.ui.reminderEffect = "unknown";
        prefs.Normalize();
        Check("未知特效回退", prefs.ui.reminderEffect, "glow");
        prefs.ui.reminderEffect = null;
        prefs.Normalize();
        Check("空特效回退", prefs.ui.reminderEffect, "glow");
        foreach (string effect in new string[] { "none", "glow", "particles", "aurora" })
        {
            prefs.ui.reminderEffect = effect;
            prefs.Normalize();
            Check("合法特效保留 " + effect, prefs.ui.reminderEffect, effect);
        }
        foreach (int hold in new int[] { int.MinValue, 2, 3, 6, 20, 21, int.MaxValue })
        {
            prefs.ui.reminderHoldSeconds = hold;
            prefs.Normalize();
            Check("停留钳制 " + hold, prefs.ui.reminderHoldSeconds.ToString(), Math.Max(3, Math.Min(20, hold)).ToString());
        }
        foreach (bool reduced in new bool[] { true, false })
        {
            prefs.ui.reminderEffect = "particles";
            prefs.ui.reducedMotion = reduced;
            prefs.ui.reminderHoldSeconds = 12;
            var restored = serializer.Deserialize<Settings>(serializer.Serialize(prefs));
            restored.Normalize();
            Check("减少动态 JSON 往返 " + reduced, restored.ui.reducedMotion.ToString(), reduced.ToString());
            Check("特效 JSON 往返 " + reduced, restored.ui.reminderEffect, "particles");
            Check("停留 JSON 往返 " + reduced, restored.ui.reminderHoldSeconds.ToString(), "12");
        }
        var nullPrefs = serializer.Deserialize<Settings>("{\"lark\":null,\"ui\":null}");
        nullPrefs.Normalize();
        Check("空飞书配置可归一化", (nullPrefs.lark != null && !nullPrefs.lark.enabled).ToString(), "True");
        Check("空 UI 配置恢复默认", nullPrefs.ui.reminderEffect + "/" + nullPrefs.ui.reducedMotion + "/" + nullPrefs.ui.reminderHoldSeconds, "glow/False/6");

        // ---- 19b) v3.6.0 主题背景磨砂玻璃：ui.bgStyle 默认 / 归一化 / Frost.On ----
        {
            var ui36 = new Settings();
            Check("默认背景风格是实心", ui36.ui.bgStyle, "solid");
            Check("实心背景时 Frost.On 关", Frost.On(ui36.ui).ToString(), "False");
            ui36.ui.bgStyle = "glass";
            Check("glass 背景时 Frost.On 开", Frost.On(ui36.ui).ToString(), "True");
            Check("Frost 风格常量", Frost.Solid + "/" + Frost.Glass, "solid/glass");
            Check("Frost.Normalize 只认 glass",
                Frost.Normalize("glass") + "/" + Frost.Normalize("solid") + "/" + Frost.Normalize(null) + "/" + Frost.Normalize("GLASS"),
                "glass/solid/solid/solid");
            Check("Frost.On(null) 不炸", Frost.On(null).ToString(), "False");

            var uiGlassRound = serializer.Deserialize<Settings>(serializer.Serialize(ui36));
            uiGlassRound.Normalize();
            Check("glass 能存能读", uiGlassRound.ui.bgStyle + "/" + Frost.On(uiGlassRound.ui), "glass/True");

            ui36.ui.bgStyle = "  glass ";
            ui36.Normalize();
            Check("非精确 glass 归一化成 solid", ui36.ui.bgStyle, "solid");
            ui36.ui.bgStyle = null;
            ui36.Normalize();
            Check("空背景风格归一化成 solid", ui36.ui.bgStyle, "solid");
            ui36.ui.bgStyle = "solid";
            ui36.Normalize();
            Check("solid 原样保留", ui36.ui.bgStyle, "solid");

            var oldUi36 = serializer.Deserialize<Settings>("{\"ui\":{\"theme\":\"midnight\"}}");
            oldUi36.Normalize();
            Check("旧配置没有 bgStyle 补 solid", oldUi36.ui.bgStyle, "solid");
        }

        // ---- 19c) v3.6.0 右键胶囊菜单：6 颗 / 列高 280 / 长开高度 / 预览通知类型 ----
        {
            Check("胶囊菜单 6 颗", BubbleMenuForm.Labels.Length.ToString(), "6");
            Check("胶囊文案顺序", string.Join("/", BubbleMenuForm.Labels),
                "新增日程/修改主题/触发上下班/窗口置顶/预览通知/检查更新");
            Check("BubbleAction 顺序",
                (int)BubbleAction.Schedule + "/" + (int)BubbleAction.Theme + "/" + (int)BubbleAction.Clock
                + "/" + (int)BubbleAction.Pin + "/" + (int)BubbleAction.Preview + "/" + (int)BubbleAction.Update,
                "0/1/2/3/4/5");
            Check("胶囊列高 6×40+5×8", BubbleMenuForm.ColumnHDip.ToString(), "280");
            Check("长开额外高度 Theme/Clock/Schedule/Preview/Update",
                BubbleMenuForm.ExtraHDip(BubbleAction.Theme) + "/" + BubbleMenuForm.ExtraHDip(BubbleAction.Clock)
                + "/" + BubbleMenuForm.ExtraHDip(BubbleAction.Schedule) + "/" + BubbleMenuForm.ExtraHDip(BubbleAction.Preview)
                + "/" + BubbleMenuForm.ExtraHDip(BubbleAction.Update), "168/130/222/208/330");
            Check("长开后整颗高度 = 40 + 额外",
                BubbleMenuForm.ItemHDip(BubbleAction.Theme) + "/" + BubbleMenuForm.ItemHDip(BubbleAction.Clock)
                + "/" + BubbleMenuForm.ItemHDip(BubbleAction.Schedule) + "/" + BubbleMenuForm.ItemHDip(BubbleAction.Preview)
                + "/" + BubbleMenuForm.ItemHDip(BubbleAction.Update), "208/170/262/248/370");

            Check("预览通知 6 类 key", string.Join("/", BubbleMenuForm.PreviewKeys), "fixed/lunch/lark/outlook/local/off");
            Check("预览通知 名字 / 副标题 与 key 一一对应",
                BubbleMenuForm.PreviewNames.Length + "/" + BubbleMenuForm.PreviewSubs.Length + "/"
                + BubbleMenuForm.PreviewKeys.Length, "6/6/6");
            Check("预览通知名字", string.Join("/", BubbleMenuForm.PreviewNames),
                "上班提醒/午休提醒/飞书会议/Outlook/普通日程/下班提醒");
            Check("预览通知副标题", BubbleMenuForm.PreviewSubs[0] + "/" + BubbleMenuForm.PreviewSubs[5], "固定日程/打卡提醒");
            var previewKeys = new HashSet<string>();
            int dupPreview = 0;
            foreach (var k in BubbleMenuForm.PreviewKeys) if (!previewKeys.Add(k)) dupPreview++;
            Check("预览通知 key 不重复", dupPreview.ToString(), "0");
        }

        // ---- 19d) v3.6.0 自动更新：版本比较 / tag / 大小 / 说明 / 状态机 ----
        {
            Check("更新仓库", UpdateService.Owner + "/" + UpdateService.Repo, "ocezeus/vibeZheyikuai");
            Check("更新页地址", UpdateService.RepoUrl, "https://github.com/ocezeus/vibeZheyikuai");
            Check("当前版本", UpdateService.CurrentVersion + "/" + AppInfo.Version, "4.0.0/4.0.0");
            Check("程序集版本是 4.0.0.0", typeof(AppInfo).Assembly.GetName().Version.ToString(), "4.0.0.0");

            Check("IsNewer 3.6.1 比 3.6.0 新", UpdateService.IsNewer("3.6.1", "3.6.0").ToString(), "True");
            Check("IsNewer 相同版本不算新", UpdateService.IsNewer("3.6.0", "3.6.0").ToString(), "False");
            Check("IsNewer 3.6.0 与 3.6.0.0 等价",
                UpdateService.IsNewer("3.6.0", "3.6.0.0") + "/" + UpdateService.IsNewer("3.6.0.0", "3.6.0"), "False/False");
            Check("IsNewer 第四段大也算新", UpdateService.IsNewer("3.6.0.1", "3.6.0").ToString(), "True");
            Check("IsNewer 带 v 前缀", UpdateService.IsNewer("v3.6.1", "3.6.0").ToString(), "True");
            Check("IsNewer 按数值比（3.10 > 3.9）", UpdateService.IsNewer("3.10.0", "3.9.9").ToString(), "True");
            Check("IsNewer 乱码不升级",
                UpdateService.IsNewer("abc", "3.6.0") + "/" + UpdateService.IsNewer("", "3.6.0")
                + "/" + UpdateService.IsNewer("3.6.1", "garbage") + "/" + UpdateService.IsNewer(null, "3.6.0"),
                "False/False/False/False");
            Check("IsNewer 预发布后缀只取数字段", UpdateService.IsNewer("v3.6.0-beta1", "3.6.0").ToString(), "False");
            Check("IsNewer 3.5.0 不比 3.6.0 新", UpdateService.IsNewer("3.5.0", "3.6.0").ToString(), "False");

            int[] parts36, parts4, badParts;
            Check("TryParseVersion v3.6.0",
                UpdateService.TryParseVersion("v3.6.0", out parts36) + "/" + parts36.Length + "/" + parts36[2], "True/3/0");
            Check("TryParseVersion 四段", UpdateService.TryParseVersion("3.6.0.0", out parts4) + "/" + parts4.Length, "True/4");
            Check("TryParseVersion 乱码 / 空", UpdateService.TryParseVersion("version", out badParts)
                + "/" + UpdateService.TryParseVersion("", out badParts), "False/False");

            Check("VersionFromTag",
                UpdateService.VersionFromTag("v3.6.0") + "/" + UpdateService.VersionFromTag("3.6.1") + "/"
                + UpdateService.VersionFromTag("V3.7") + "/" + UpdateService.VersionFromTag("v3.6.0-beta1") + "/"
                + UpdateService.VersionFromTag("") + "/" + UpdateService.VersionFromTag("abc"),
                "3.6.0/3.6.1/3.7/3.6.0//");

            Check("HumanSize 各档",
                UpdateService.HumanSize(-1) + "/" + UpdateService.HumanSize(0) + "/" + UpdateService.HumanSize(1023) + "/"
                + UpdateService.HumanSize(1024) + "/" + UpdateService.HumanSize(1536) + "/"
                + UpdateService.HumanSize(1024 * 1024) + "/" + UpdateService.HumanSize((long)(3.5 * 1024 * 1024)),
                "?/0 B/1023 B/1.0 KB/1.5 KB/1.0 MB/3.5 MB");

            Check("ShortNotes 空值", UpdateService.ShortNotes(null, 40) + "/" + UpdateService.ShortNotes("", 40), "/");
            Check("ShortNotes 去掉 markdown 记号压成一行",
                UpdateService.ShortNotes("# v3.6.1\r\n\r\n- 新增预览通知\r\n* 磨砂玻璃", 80),
                "v3.6.1 · 新增预览通知 · 磨砂玻璃");
            string short6 = UpdateService.ShortNotes("一二三四五六七八九十", 6);
            Check("ShortNotes 超长截断带省略号", short6.Length + "/" + short6.EndsWith("…").ToString(), "6/True");

            var rel36 = new UpdateRelease();
            Check("空 release 没有可下载的 exe", rel36.HasAsset.ToString(), "False");
            rel36.tag = "v3.6.1";
            rel36.version = UpdateService.VersionFromTag(rel36.tag);
            rel36.assetName = "WorkCountdown.exe";
            rel36.assetSize = 1024 * 1024;
            rel36.assetUrl = "https://example.com/WorkCountdown.exe";
            Check("有下载地址才算有资产", rel36.HasAsset.ToString(), "True");

            Check("UpdatePhase 七个状态",
                Enum.GetNames(typeof(UpdatePhase)).Length + "/" + string.Join("/", Enum.GetNames(typeof(UpdatePhase))),
                "7/Idle/Checking/UpToDate/Available/Downloading/Ready/Error");
            Check("UpdatePhase 序号",
                (int)UpdatePhase.Idle + "/" + (int)UpdatePhase.Checking + "/" + (int)UpdatePhase.UpToDate + "/"
                + (int)UpdatePhase.Available + "/" + (int)UpdatePhase.Downloading + "/" + (int)UpdatePhase.Ready + "/"
                + (int)UpdatePhase.Error, "0/1/2/3/4/5/6");
        }

        // ---------------- 四类日程 + Outlook .ics ----------------

        // 1) 四类日程的分类：固定 / 飞书 / 普通 / Outlook
        Check("分类 固定", Reminders.KindOf(new ReminderEvent { source = "break" }), Reminders.KindFixed);
        Check("分类 下班提醒", Reminders.KindOf(new ReminderEvent { source = "alert" }), Reminders.KindFixed);
        Check("分类 飞书", Reminders.KindOf(new ReminderEvent { source = "lark" }), Reminders.KindLark);
        Check("分类 普通", Reminders.KindOf(new ReminderEvent { source = "local" }), Reminders.KindLocal);
        Check("分类 Outlook", Reminders.KindOf(new ReminderEvent { source = "outlook" }), Reminders.KindOutlook);
        Check("四类的名字", Reminders.KindName(Reminders.KindFixed) + "/" + Reminders.KindName(Reminders.KindLark)
            + "/" + Reminders.KindName(Reminders.KindLocal) + "/" + Reminders.KindName(Reminders.KindOutlook),
            "固定日程/飞书会议/普通日程/Outlook 日程");

        // 2) 每类各有一份通知样式；老配置按旧的全局值补齐
        var oldStyle = new Settings();
        oldStyle.ui = new UiSettings();
        oldStyle.ui.reminderSpread = "screen";
        oldStyle.ui.reminderSpreadStyle = "bar";
        oldStyle.ui.styleFixed = null;
        oldStyle.ui.styleLark = null;
        oldStyle.ui.styleLocal = null;
        oldStyle.ui.styleOutlook = null;
        oldStyle.outlook = null;
        oldStyle.Normalize();
        Check("老配置补齐四类样式", oldStyle.StyleOf(Reminders.KindFixed).spread + "/"
            + oldStyle.StyleOf(Reminders.KindLark).shape + "/" + oldStyle.StyleOf(Reminders.KindOutlook).color,
            "screen/bar/theme");
        oldStyle.SetStyleOf(Reminders.KindOutlook, new ReminderStyleDef("card", "tape", "lv-kongque"));
        Check("单独改 Outlook 的样式", oldStyle.StyleOf(Reminders.KindOutlook).shape + "/"
            + oldStyle.StyleOf(Reminders.KindOutlook).color + "，别的类不受影响 "
            + oldStyle.StyleOf(Reminders.KindLark).shape,
            "tape/lv-kongque，别的类不受影响 bar");
        var styleRound = serializer.Deserialize<Settings>(serializer.Serialize(oldStyle));
        styleRound.Normalize();
        Check("四类样式能存能读", styleRound.StyleOf(Reminders.KindOutlook).shape + "/"
            + styleRound.StyleOf(Reminders.KindOutlook).color, "tape/lv-kongque");

        // 3) Outlook 配置：没填地址就自动关掉
        var ols = new Settings();
        ols.Normalize();
        Check("Outlook 默认关", ols.outlook.enabled.ToString(), "False");
        ols.outlook.enabled = true;
        ols.Normalize();
        Check("没地址的 Outlook 自动关掉", ols.outlook.enabled.ToString(), "False");
        ols.outlook.url = "https://outlook.office365.com/owa/calendar/x/calendar.ics";
        ols.outlook.enabled = true;
        ols.Normalize();
        Check("填了地址就保留开关", ols.outlook.enabled.ToString(), "True");

        // 4) .ics 解析：单次 / 每周重复 + COUNT / EXDATE / 取消 / 全天 / 折行 + 转义
        string ics =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//CN\r\n" +
            "BEGIN:VEVENT\r\nUID:single-1\r\nSUMMARY:产品评审\r\n" +
            "DTSTART:20261110T140000\r\nDTEND:20261110T150000\r\n" +
            "LOCATION:3F 云杉\\, 大会议室\r\nDESCRIPTION:议题：Q4 排期\\n第二行\r\n" +
            "END:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:weekly-1\r\nSUMMARY:周会\r\n" +
            "DTSTART:20261102T100000\r\nDTEND:20261102T103000\r\n" +
            "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=6\r\n" +
            "END:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:weekly-ex\r\nSUMMARY:被排除的那次\r\n" +
            "DTSTART:20261112T160000\r\nEXDATE:20261112T160000\r\n" +
            "END:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:cancel-1\r\nSUMMARY:取消的会\r\n" +
            "DTSTART:20261112T090000\r\nSTATUS:CANCELLED\r\n" +
            "END:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:allday-1\r\nSUMMARY:节假日\r\n" +
            "DTSTART;VALUE=DATE:20261115\r\n" +
            "END:VEVENT\r\n" +
            "BEGIN:VEVENT\r\nUID:fold-1\r\n" +
            "SUMMARY:季度总结：Q4\\n第二行很长很长，折到\r\n" +
            " 下一行\r\n" +
            "DTSTART:20261117T140000\r\nDURATION:PT45M\r\n" +
            "END:VEVENT\r\n" +
            "END:VCALENDAR\r\n";
        var olEvents = Ics.Parse(ics, new DateTime(2026, 11, 9), new DateTime(2026, 11, 22));
        Check("ics 窗口内的事件数", olEvents.Count.ToString(), "5");
        Check("ics 排序后第一条", olEvents[0].title + " @" + olEvents[0].start, "周会 @2026-11-09 10:00");
        Check("ics 单次事件", olEvents[1].title + " @" + olEvents[1].start + "-" + olEvents[1].end,
            "产品评审 @2026-11-10 14:00-2026-11-10 15:00");
        Check("ics 地点转义", olEvents[1].place, "3F 云杉, 大会议室");
        Check("ics 描述换行压平", olEvents[1].detail, "议题：Q4 排期 第二行");
        Check("ics 每周重复 COUNT=6 只取窗口内", olEvents[3].start, "2026-11-13 10:00");
        Check("ics EXDATE 排除", olEvents.FindAll(delegate (ReminderEvent e) { return e.title == "被排除的那次"; }).Count.ToString(), "0");
        Check("ics 取消的不算", olEvents.FindAll(delegate (ReminderEvent e) { return e.title == "取消的会"; }).Count.ToString(), "0");
        Check("ics 全天事件不算", olEvents.FindAll(delegate (ReminderEvent e) { return e.title == "节假日"; }).Count.ToString(), "0");
        Check("ics 折行 + 时长", olEvents[4].title + " / " + olEvents[4].end, "季度总结：Q4 第二行很长很长，折到下一行 / 2026-11-17 14:45");
        Check("ics 事件都带来源", olEvents[0].source, "outlook");
        Check("ics 事件 id 唯一", (olEvents[0].id != olEvents[1].id && olEvents[0].id.StartsWith("ol:")).ToString(), "True");

        // 5) UTC 时间换成本机时间
        var utcIcs = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:utc-1\r\nSUMMARY:UTC 会\r\n" +
            "DTSTART:20261110T060000Z\r\nDTEND:20261110T070000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var utcEvents = Ics.Parse(utcIcs, new DateTime(2026, 11, 9), new DateTime(2026, 11, 22));
        DateTime utcExpect = DateTime.SpecifyKind(new DateTime(2026, 11, 10, 6, 0, 0), DateTimeKind.Utc).ToLocalTime();
        Check("ics UTC 转本机", utcEvents.Count == 1 ? utcEvents[0].start : "none", Reminders.FormatStart(utcExpect));

        // 6) 坏数据不抛异常
        Check("空文本", Ics.Parse("", DateTime.Today, DateTime.Today.AddDays(7)).Count.ToString(), "0");
        Check("不是 ics", Ics.Parse("<html>nope</html>", DateTime.Today, DateTime.Today.AddDays(7)).Count.ToString(), "0");
        string err;
        Check("本地文件不存在给错误", (Ics.ReadText("file", "", @"C:\nope\nope.ics", 1000, out err) == null && err.Length > 0).ToString(), "True");
        Check("没填地址给错误", (Ics.ReadText("url", "", "", 1000, out err) == null && err.IndexOf("订阅地址") >= 0).ToString(), "True");

        // 7) 本地 .ics 文件能读（Outlook 导出 / 另存的那种）
        {
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wc-" + Guid.NewGuid().ToString("N") + ".ics");
            string dt = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "T150000";
            System.IO.File.WriteAllText(tmp,
                "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:file-1\r\nSUMMARY:本地文件里的会\r\n" +
                "DTSTART:" + dt + "\r\nDTEND:" + dt + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n",
                System.Text.Encoding.UTF8);
            string fileErr;
            string fileText = Ics.ReadText("file", "", tmp, 3000, out fileErr);
            var fileEvents = Ics.Parse(fileText, DateTime.Today, DateTime.Today.AddDays(14));
            Check("本地 .ics 文件能读", (fileText != null ? fileEvents.Count : -1) + "/" + fileErr, "1/");
            System.IO.File.Delete(tmp);
        }

        // 8) Graph 登录链路里的纯解析（真网络调用在 OutlookProbe 里手动跑）
        {
            OutlookGraph.DeviceCodeInfo dc;
            string e1;
            Check("设备码解析",
                (OutlookGraph.ParseDeviceCode("{\"device_code\":\"dc123\",\"user_code\":\"AB12CD34\"," +
                    "\"verification_uri\":\"https://microsoft.com/devicelogin\",\"interval\":5,\"expires_in\":900}",
                    out dc, out e1) && dc.userCode == "AB12CD34" && dc.interval == 5 && dc.verifyUrl.Length > 0)
                .ToString(), "True");
            Check("设备码报错带说明",
                (OutlookGraph.ParseDeviceCode("{\"error\":\"invalid_client\",\"error_description\":\"AADSTS700016: bad app\"}",
                    out dc, out e1) == false && e1.IndexOf("invalid_client") >= 0 && e1.IndexOf("AADSTS700016") > 0)
                .ToString(), "True");

            OutlookGraph.TokenInfo tk;
            string e2;
            Check("令牌解析",
                (OutlookGraph.ParseToken("{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600}",
                    out tk, out e2) && tk.accessToken == "at" && tk.refreshToken == "rt" && tk.expiresIn == 3600)
                .ToString(), "True");
            Check("待授权状态认得出", OutlookGraph.ErrorCode("{\"error\":\"authorization_pending\"}"), "authorization_pending");
            Check("授权过期认得出", OutlookGraph.ErrorCode("{\"error\":\"expired_token\"}"), "expired_token");

            string url = OutlookGraph.BuildCalendarUrl(new DateTime(2026, 11, 10, 8, 0, 0), new DateTime(2026, 11, 24, 8, 0, 0));
            Check("calendarView 地址", (url.Contains("/me/calendarView") && url.Contains("startDateTime=")
                && url.Contains("endDateTime=") && url.Contains("$select=") && url.Contains("Z&")).ToString(), "True");

            string next;
            string cal = "{\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/next\",\"value\":[" +
                "{\"id\":\"A1\",\"subject\":\"需求评审\",\"start\":{\"dateTime\":\"2026-11-10T06:00:00.0000000\",\"timeZone\":\"UTC\"}," +
                "\"end\":{\"dateTime\":\"2026-11-10T07:00:00.0000000\",\"timeZone\":\"UTC\"}," +
                "\"location\":{\"displayName\":\"3F 云杉\"},\"isAllDay\":false,\"showAs\":\"busy\"}," +
                "{\"id\":\"A2\",\"subject\":\"节假日\",\"start\":{\"dateTime\":\"2026-11-11T00:00:00.0000000\",\"timeZone\":\"UTC\"}," +
                "\"end\":{\"dateTime\":\"2026-11-12T00:00:00.0000000\",\"timeZone\":\"UTC\"},\"isAllDay\":true}," +
                "{\"id\":\"A3\",\"subject\":\"取消的会\",\"isCancelled\":true," +
                "\"start\":{\"dateTime\":\"2026-11-12T06:00:00.0000000\",\"timeZone\":\"UTC\"}}]}";
            var gEvents = OutlookGraph.ParseCalendar(cal, out next);
            Check("Graph 事件：全天/取消都跳过", gEvents.Count.ToString(), "1");
            Check("Graph 事件标题/地点", gEvents.Count == 1 ? gEvents[0].title + " · " + gEvents[0].place : "none", "需求评审 · 3F 云杉");
            DateTime expect = DateTime.SpecifyKind(new DateTime(2026, 11, 10, 6, 0, 0), DateTimeKind.Utc).ToLocalTime();
            Check("Graph 事件 UTC 转本机", gEvents.Count == 1 ? gEvents[0].start : "none", Reminders.FormatStart(expect));
            Check("Graph 事件来源", gEvents.Count == 1 ? gEvents[0].source : "none", "outlook");
            Check("下一页链接", next, "https://graph.microsoft.com/v1.0/next");

            string sealedToken = OutlookGraph.Protect("rt-secret");
            Check("刷新令牌 DPAPI 加密后能解回来",
                (sealedToken.StartsWith("dpapi:") && sealedToken != "rt-secret" && OutlookGraph.Unprotect(sealedToken) == "rt-secret").ToString(),
                "True");
            Check("乱码令牌解不出来", OutlookGraph.Unprotect("dpapi:bm90LWEtdG9rZW4="), "");
        }

        // 9) 多份日历订阅：老配置迁移 / 每份独立提前量 / 一份失败不影响另一份
        {
            var legacy = new Settings();
            legacy.outlook.url = "https://outlook.office365.com/owa/calendar/x/calendar.ics";
            legacy.outlook.enabled = true;
            legacy.Normalize();
            Check("老单份 .ics 迁移成一份日历",
                legacy.outlook.calendars.Count + "/" + legacy.outlook.calendars[0].source,
                "1/url");
            Check("迁移后日历开关还在", legacy.outlook.HasEnabled.ToString(), "True");

            var two = new Settings();
            two.Normalize();
            two.outlook.leadMinutes = 15;
            two.outlook.calendars.Add(new CalendarSource
            {
                id = "c1", name = "工作日历", source = "url",
                url = "https://example.com/work.ics", enabled = true, leadMinutes = 5
            });
            two.outlook.calendars.Add(new CalendarSource
            {
                id = "c2", name = "个人日历", source = "url",
                url = "https://example.com/me.ics", enabled = true, leadMinutes = 0
            });
            two.Normalize();
            Check("两份日历都能启用", two.outlook.Active.Count.ToString(), "2");
            Check("每份日历有独立提前量",
                two.outlook.calendars[0].leadMinutes + "/" + two.outlook.calendars[1].leadMinutes, "5/0");
            var calRound = serializer.Deserialize<Settings>(serializer.Serialize(two));
            calRound.Normalize();
            Check("多份日历能存能读", calRound.outlook.calendars.Count + "/" + calRound.outlook.Active.Count, "2/2");

            // 两份本地 .ics 一起同步；第二份用全局默认提前量
            string f1 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc-multi-" + Guid.NewGuid().ToString("N") + ".ics");
            string f2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc-multi-" + Guid.NewGuid().ToString("N") + ".ics");
            string d1 = DateTime.Today.AddHours(10).ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            string d2 = DateTime.Today.AddHours(11).ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            System.IO.File.WriteAllText(f1,
                "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:multi-1\r\nSUMMARY:工作日历的会\r\n" +
                "DTSTART:" + d1 + "\r\nDTEND:" + d1 + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n",
                System.Text.Encoding.UTF8);
            System.IO.File.WriteAllText(f2,
                "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:multi-2\r\nSUMMARY:个人日历的会\r\n" +
                "DTSTART:" + d2 + "\r\nDTEND:" + d2 + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n",
                System.Text.Encoding.UTF8);

            var multiSet = new Settings();
            multiSet.Normalize();
            multiSet.outlook.leadMinutes = 12;
            multiSet.outlook.calendars.Add(new CalendarSource
            {
                id = "f1", name = "工作", source = "file", path = f1, enabled = true, leadMinutes = 7
            });
            multiSet.outlook.calendars.Add(new CalendarSource
            {
                id = "f2", name = "个人", source = "file", path = f2, enabled = true, leadMinutes = 0
            });
            var firstFetch = CalendarSync.Fetch(CalendarSync.Request(multiSet.outlook.calendars[0],
                multiSet.outlook, DateTime.Today, DateTime.Today.AddDays(14), 3000));
            var secondFetch = CalendarSync.Fetch(CalendarSync.Request(multiSet.outlook.calendars[1],
                multiSet.outlook, DateTime.Today, DateTime.Today.AddDays(14), 3000));
            Check("两份本地日历都能直接读取", firstFetch.Succeeded + "/" + secondFetch.Succeeded, "True/True");
            Check("同步时套用每份日历自己的提前量",
                firstFetch.Events[0].leadMinutes + "/" + secondFetch.Events[0].leadMinutes, "7/12");
            Check("读取不写共享配置缓存",
                multiSet.outlook.calendars[0].cached.Count + "/" + multiSet.outlook.calendars[1].cached.Count, "0/0");

            // 把第二份改成一个不存在的文件：第一份照样成功，第二份给出失败
            multiSet.outlook.calendars[1].path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc-missing-" + Guid.NewGuid().ToString("N") + ".ics");
            var missingFetch = CalendarSync.Fetch(CalendarSync.Request(multiSet.outlook.calendars[1],
                multiSet.outlook, DateTime.Today, DateTime.Today.AddDays(14), 3000));
            Check("一份失败不影响另一份",
                (firstFetch.Succeeded && !missingFetch.Succeeded && missingFetch.Error.Length > 0).ToString(), "True");
            try { System.IO.File.Delete(f1); System.IO.File.Delete(f2); } catch { }
        }

        // 10) 同步晚于开始时间时补提醒：规则边界改由 20.2 经 ReminderEngine.Advance 断言
        //（开始满 60 秒转补提醒、已结束不补、无结束时间 59 分补 / 60 分不补）。

        // 10.5) 工作日索引迁移（v3.5 修复：旧设置页把「周一..周日」直接按下标写，和内部 0=周日 差一天）
        {
            Func<bool[], string> bits = delegate(bool[] a)
            {
                var outBits = "";
                for (int i = 0; i < a.Length; i++) outBits += a[i] ? "1" : "0";
                return outBits;
            };

            var legacy = new Settings();
            legacy.version = 1;
            legacy.workdays = new bool[] { true, true, true, true, true, false, false };   // 旧版存出来的错位形态
            legacy.Normalize();
            Check("旧配置的工作日错位会迁移回周一~周五（周五上班）",
                bits(legacy.workdays) + "/" + legacy.version, "0111110/2");

            var legacyMonSat = new Settings();
            legacyMonSat.version = 1;
            legacyMonSat.workdays = new bool[] { true, true, true, true, true, true, false };
            legacyMonSat.Normalize();
            Check("旧配置「周一~周六」也能迁移对", bits(legacyMonSat.workdays), "0111111");

            var normal = new Settings();
            normal.version = 1;
            normal.workdays = new bool[] { false, true, true, true, true, true, false };
            normal.Normalize();
            Check("正常的工作日配置不会被迁移误伤", bits(normal.workdays), "0111110");

            var already = new Settings();
            already.version = 2;
            already.workdays = new bool[] { true, true, true, true, true, false, false };
            already.Normalize();
            Check("已经迁移过的配置（version=2）不会被再转一次", bits(already.workdays), "1111100");
        }

        // 11) 工资计算：应出勤 / 日薪 / 时薪 / 今日 / 本月 / 发薪日
        {
            var pay = new Settings();
            pay.lark.enabled = true;
            pay.lark.chatId = "oc_test";
            pay.mode = "fixed";
            pay.startTime = "09:00";
            pay.endTime = "18:00";
            pay.lunchStart = "11:50";
            pay.lunchEnd = "13:30";
            pay.eveningEnabled = false;
            pay.salary = new SalarySettings { monthly = 21000m, payday = 15 };
            pay.Normalize();
            var now = new DateTime(2026, 11, 11, 14, 0, 0);
            Check("本月应出勤按工作日算", SalaryCalc.RequiredWorkdays(pay, now).ToString(), "21");
            Check("发薪日 11/15 还有 4 天",
                SalaryCalc.PaydayDate(now, 15).ToString("yy-MM-dd") + "/" + (SalaryCalc.PaydayDate(now, 15) - now.Date).Days,
                "26-11-15/4");
            Check("小月没有 31 号按最后一天", SalaryCalc.PaydayDate(new DateTime(2026, 2, 15), 31).ToString("yy-MM-dd"), "26-02-28");

            var punch = new AttendanceSnapshot();
            punch.ok = true;
            punch.clockIn = new DateTime(2026, 11, 11, 9, 0, 0);
            var sal = SalaryCalc.Compute(pay, punch, now);
            Check("日薪 = 月薪 / 应出勤", SalaryCalc.Money3(sal.daily), "1,000.000");
            Check("应勤时长扣午休", Math.Round(sal.required.TotalMinutes).ToString(), "440");
            Check("时薪 = 日薪 / 应勤", Math.Round(sal.hourly, 3).ToString("0.000", CultureInfo.InvariantCulture), "136.364");
            Check("今日已工作扣午休", Math.Round(sal.worked.TotalMinutes).ToString(), "200");
            Check("今日当前收入", SalaryCalc.Money3(sal.today), "454.545");
            Check("今日进度", Math.Round(sal.progress * 100).ToString(), "45");
            Check("本月收入 = 已完成工作日 + 今日", SalaryCalc.Money3(sal.month), "7,454.545");
            Check("本月已出勤折算天数", sal.attendedDays.ToString("0.000", CultureInfo.InvariantCulture), "7.455");
            Check("发薪日倒计时", sal.daysToPayday.ToString(), "4");

            var rest = new AttendanceSnapshot();
            var sunday = new DateTime(2026, 11, 15, 12, 0, 0);
            var restSal = SalaryCalc.Compute(pay, rest, sunday);
            Check("休息日今日收入为 0", SalaryCalc.Money3(restSal.today), "0.000");
            Check("休息日本月收入只算过去工作日", SalaryCalc.Money3(restSal.month), "10,000.000");
            Check("休息日不显示已工作", Math.Round(restSal.worked.TotalMinutes).ToString(), "0");

            var noClock = SalaryCalc.Compute(pay, new AttendanceSnapshot(), now);
            Check("没打卡今日收入为 0", SalaryCalc.Money3(noClock.today), "0.000");

            // 面板按 18fps 重绘：收入必须按秒累计，后三位小数才会高速跳
            var frameA = SalaryCalc.Compute(pay, punch, new DateTime(2026, 11, 11, 14, 0, 0, 0));
            var frameB = SalaryCalc.Compute(pay, punch, new DateTime(2026, 11, 11, 14, 0, 0, 500));
            Check("亚秒级重绘收入会变化", (frameA.today != frameB.today).ToString(), "True");
            Check("半秒后第三位小数已变化",
                (SalaryCalc.Money3(frameA.today) != SalaryCalc.Money3(frameB.today)).ToString(), "True");

            var lateNight = SalaryCalc.Compute(pay, punch, new DateTime(2026, 11, 11, 23, 0, 0));
            Check("今日收入最多到日薪", SalaryCalc.Money3(lateNight.today), "1,000.000");
            Check("封顶后标记", lateNight.capped.ToString(), "True");

            var noSalary = new Settings();
            noSalary.Normalize();
            Check("没填月工资不算配置", SalaryCalc.Compute(noSalary, null, now).configured.ToString(), "False");

            var payRound = serializer.Deserialize<Settings>(serializer.Serialize(pay));
            payRound.Normalize();
            Check("工资配置能存能读", SalaryCalc.Money3(payRound.salary.monthly) + "/" + payRound.salary.payday, "21,000.000/15");
        }

        // 12) 字体：中文 / 数字分开存，UiFont 能按选择创建
        {
            UiFont.Configure("Microsoft YaHei UI", "Consolas");
            using (var nf = UiFont.MakeNumber(12f))
                Check("数字字体可切换", nf.FontFamily.Name, "Consolas");
            using (var tf = UiFont.Make(12f))
                Check("中文字体可切换", tf.FontFamily.Name, "Microsoft YaHei UI");
            Check("数字字体族记下来了", UiFont.NumberFamily, "Consolas");
            UiFont.Configure("", "Consolas");
            Check("空中文 = 跟随默认", (UiFont.TextFamily.Length > 0).ToString(), "True");

            var fonts = new Settings();
            fonts.ui.textFont = "Microsoft YaHei UI";
            fonts.ui.numberFont = "Microsoft YaHei";
            fonts.Normalize();
            var fr = serializer.Deserialize<Settings>(serializer.Serialize(fonts));
            fr.Normalize();
            Check("字体配置能存能读", fr.ui.textFont + "/" + fr.ui.numberFont,
                "Microsoft YaHei UI/Microsoft YaHei");
        }

        // ---- 小纸条：ID / 16 条 / 联系人 / MQTT / JSON ----
        {
            var n = new NoteSettings();
            n.Normalize();
            Check("小纸条 ID 是合法 5 位数", (n.id.Length == 5 && NoteSettings.ValidId(n.id)).ToString(), "True");
            Check("0 开头的 5 位数不算合法", NoteSettings.ValidId("01234").ToString(), "False");
            Check("两次生成的 ID 都合法",
                (NoteSettings.ValidId(NoteSettings.GenerateId()) && NoteSettings.ValidId(NoteSettings.GenerateId())).ToString(), "True");

            n.id = "48372";
            Check("按 ID 加联系人", n.AddContact("62015", "阿哲").ToString(), "True");
            Check("重复加只更新备注",
                n.AddContact("62015", "阿哲2").ToString() + "/" + n.Contact("62015").remark, "False/阿哲2");
            n.AddContact("11788", "一二三四五六七八九十");
            Check("备注最多 8 个字", n.Contact("11788").remark.Length.ToString(), "8");

            var th = n.Thread("62015");
            for (int i = 0; i < 20; i++)
            {
                bool incoming = i % 2 == 0;
                var noteMsg = new NoteMessage(incoming ? "62015" : "48372", incoming ? "48372" : "62015", "消息" + i, 1731400000 + i);
                n.Append(noteMsg, incoming);
            }
            Check("每对 ID 只留 16 条", th.messages.Count + "/" + th.messages[0].text + "/" + th.messages[15].text, "16/消息4/消息19");
            Check("历史页一屏 8 条", NoteSettings.HistoryRows.ToString(), "8");
            Check("未读只算收到的", th.unread.ToString(), "10");
            n.MarkRead("62015");
            Check("读完未读清零", th.unread + "/" + n.TotalUnread(), "0/0");

            var json = NoteRelay.BuildMessageJson("48372", "62015", "晚上评审推迟到 8 点", 1731400000);
            var inc = NoteRelay.DecodeMessage(json);
            Check("消息 JSON 能解码", inc == null ? "null" : inc.from + ">" + inc.to + ":" + inc.text,
                "48372>62015:晚上评审推迟到 8 点");
            var inc2 = NoteRelay.DecodeMessage(NoteRelay.BuildMessageJson("48372", "62015", new string('a', 260), 1));
            Check("超长消息截到 200 字", (inc2 != null && inc2.text.Length == 200).ToString(), "True");
            Check("坏 JSON 返回 null", (NoteRelay.DecodeMessage("{oops") == null).ToString(), "True");

            Check("Connect 包第一个字节是 CONNECT", NoteRelay.ConnectPacket()[0].ToString("X2"), "10");
            var pub = NoteRelay.PublishPacket("xzt/v1/msg/62015", "{\"x\":1}", true);
            var body = new byte[pub.Length - 2];
            Array.Copy(pub, 2, body, 0, body.Length);
            string topic, payload;
            bool parsed = NoteRelay.ParsePublish(0x01, body, out topic, out payload);
            Check("PUBLISH 能解回主题 / 负载", parsed + "/" + topic + "/" + payload,
                "True/xzt/v1/msg/62015/{\"x\":1}");

            var s2 = new Settings();
            s2.Normalize();
            s2.note.AddContact("73512", "老李");
            s2.note.Append(new NoteMessage("73512", s2.note.id, "在吗", 1), true);
            var rt = serializer.Deserialize<Settings>(serializer.Serialize(s2));
            rt.Normalize();
            Check("小纸条设置能存能读", rt.note.id + "/" + rt.note.contacts.Count + "/" + rt.note.threads[0].messages.Count,
                s2.note.id + "/1/1");
            Check("老配置没有 note 也能补出来", new Settings().note != null ? "ok" : "null", "ok");

            // ---- 对话码（v3.3.3 起：添加联系人 → 添加对话，任意 5 位字符） ----
            var r = new NoteSettings();
            r.Normalize();
            r.id = "48372";
            Check("对话码要正好 5 个字符（4 位 / 6 位都不行）",
                NoteSettings.ValidCode("AB123") + "/" + NoteSettings.ValidCode("AB12") + "/" + NoteSettings.ValidCode("AB1234"),
                "True/False/False");
            Check("对话码不能含空格 / 空白",
                NoteSettings.ValidCode("AB 12") + "/" + NoteSettings.ValidCode("     "), "False/False");
            Check("5 位数字也是合法码，但算个人对话（不算对话码）",
                (NoteSettings.ValidCode("62015") + "/" + r.IsRoom("62015")).ToLower(), "true/false");
            Check("中文 / 混合字符算对话码", (NoteSettings.ValidCode("项目组#1") + "/" + r.IsRoom("项目组#1")).ToLower(), "true/true");
            Check("按对话码建对话", r.AddConversation("项目组#1", "项目群") + "/" + r.Contact("项目组#1").IsRoom.ToString().ToLower(),
                "True/true");
            Check("对话码显示名：有名字用名字，没名字显示「对话 xxxxx」",
                r.Contact("项目组#1").Display() + "/" + (r.AddConversation("k3v9z", "") ? r.Contact("k3v9z").Display() : "?"),
                "项目群/对话 k3v9z");
            Check("非法对话码加不进去", r.AddConversation("1234", "").ToString(), "False");
            Check("别人发进对话码的消息落到这个对话里（from 还是个人 ID）",
                r.Append(new NoteMessage("93183", "项目组#1", "大家好", 1), true) + "/"
                + r.Thread("项目组#1").messages.Count + "/" + r.Thread("项目组#1").messages[0].from,
                "True/1/93183");
            r.Append(new NoteMessage("48372", "项目组#1", "收到", 2), false);
            Check("自己发进对话码的也落在同一个对话", r.Thread("项目组#1").messages.Count.ToString(), "2");
            Check("1:1 消息还是按对方个人 ID 分会话",
                r.Append(new NoteMessage("62015", "48372", "在吗", 3), true) + "/" + r.Thread("62015").messages.Count,
                "True/1");

            string roomTopic = NoteRelay.RoomTopic("项目组#1");
            string backCode;
            bool okRoom = NoteRelay.ParseRoomTopic(roomTopic, out backCode);
            Check("对话码主题能编码 / 解回", okRoom + "/" + backCode, "True/项目组#1");
            Check("普通收件箱主题不会被当成对话码主题", NoteRelay.ParseRoomTopic("xzt/v1/msg/62015", out backCode).ToString(), "False");
            string weird = "#+/ab";
            string weirdBack;
            Check("带 MQTT 通配符的对话码也能安全放进主题",
                NoteRelay.ParseRoomTopic(NoteRelay.RoomTopic(weird), out weirdBack) + "/" + weirdBack, "True/#+/ab");
            Check("对话码十六进制编码可逆", NoteRelay.HexDecode(NoteRelay.HexEncode("中文AB1")), "中文AB1");
            var roomInc = NoteRelay.DecodeMessage(NoteRelay.BuildMessageJson("93183", "项目组#1", "测试", 4));
            Check("对话码消息 JSON 能解码（from 个人 ID、to 对话码）",
                roomInc == null ? "null" : roomInc.from + ">" + roomInc.to + ":" + roomInc.text, "93183>项目组#1:测试");
            var rj = serializer.Deserialize<NoteSettings>(serializer.Serialize(r));
            rj.Normalize();
            Check("对话码对话能存能读", (rj.Contact("项目组#1") != null && rj.Contact("项目组#1").IsRoom).ToString(), "True");

            // ---- v3.5 个人昵称：本机昵称 normalize / 对方昵称显示 / SetContactNick / JSON 兼容 ----
            var nn = new NoteSettings();
            nn.nickname = "  阿哲  ";
            nn.Normalize();
            Check("昵称 Normalize 去首尾空白", nn.nickname, "阿哲");
            nn.nickname = "一二三四五六七八九十十一十二十三";
            nn.Normalize();
            Check("昵称最多 12 个字", nn.nickname.Length + "/" + nn.nickname, "12/一二三四五六七八九十十一");
            nn.nickname = null;
            nn.Normalize();
            Check("旧配置没有昵称字段补空串", nn.nickname, "");

            var dn = new NoteSettings();
            dn.Normalize();
            dn.AddConversation("62015", "");
            dn.AddConversation("项目组#1", "");
            dn.Contact("62015").nick = "阿哲";
            Check("没本机备注时显示对方的昵称", dn.Contact("62015").Display(), "阿哲");
            dn.Contact("62015").remark = "本机备注";
            Check("本机备注优先于对方昵称", dn.Contact("62015").Display(), "本机备注");
            Check("昵称为空时个人对话还是显示 ID", (new NoteContact { id = "62015" }).Display(), "ID 62015");
            Check("昵称为空时对话码还是显示「对话 xxxxx」", (new NoteContact { id = "项目组#1" }).Display(), "对话 项目组#1");

            var sn = new NoteSettings();
            sn.Normalize();
            sn.AddConversation("62015", "");
            Check("SetContactNick 更新对方昵称（trim）",
                sn.SetContactNick("62015", "  阿哲  ") + "/" + sn.Contact("62015").nick, "True/阿哲");
            Check("SetContactNick 昵称没变化返回 false", sn.SetContactNick("62015", "阿哲").ToString(), "False");
            Check("SetContactNick 昵称最多 12 个字",
                sn.SetContactNick("62015", "一二三四五六七八九十十一十二十三") + "/" + sn.Contact("62015").nick,
                "True/一二三四五六七八九十十一");
            Check("SetContactNick 找不到联系人返回 false", sn.SetContactNick("11788", "老李").ToString(), "False");

            var m4 = new NoteMessage("62015", "48372", "hi", 5);
            var m5 = new NoteMessage("62015", "48372", "hi", 5, "阿哲");
            Check("NoteMessage 4 参构造 fromNick 为空", m4.fromNick, "");
            Check("NoteMessage 5 参构造带 fromNick", m5.fromNick, "阿哲");

            var nickInc = NoteRelay.DecodeMessage(
                NoteRelay.BuildMessageJson("48372", "62015", "带昵称的消息", 1731400000, " 阿哲 "));
            Check("消息 JSON 带昵称能解码（trim）", nickInc == null ? "null" : nickInc.nick, "阿哲");
            var noNickInc = NoteRelay.DecodeMessage(NoteRelay.BuildMessageJson("48372", "62015", "不带昵称", 1731400000));
            Check("不带昵称的消息解码成空串", noNickInc == null ? "null" : (noNickInc.nick ?? "null"), "");
            var longNickInc = NoteRelay.DecodeMessage(
                NoteRelay.BuildMessageJson("48372", "62015", "长昵称", 1731400000, "一二三四五六七八九十十一十二十三"));
            Check("消息里的昵称截到 12 个字", longNickInc == null ? "null" : longNickInc.nick.Length.ToString(), "12");
            var oldMsgInc = NoteRelay.DecodeMessage("{\"v\":1,\"from\":\"48372\",\"to\":\"62015\",\"text\":\"老消息\",\"ts\":1}");
            Check("旧版消息 JSON 没有 nick 字段也能解码",
                oldMsgInc == null ? "null" : oldMsgInc.text + "/" + (oldMsgInc.nick ?? "null"), "老消息/");

            string pj = NoteRelay.PresenceJson("62015", "阿哲");
            Check("PresenceJson(id, nick) 的 JSON 带 ID 和昵称",
                (pj.Contains("\"id\":\"62015\"") && pj.Contains("\"nick\":\"阿哲\"")).ToString(), "True");
            Check("PresenceJson(id) 兼容重载昵称为空",
                serializer.Deserialize<NoteRelay.PresencePayload>(NoteRelay.PresenceJson("62015")).nick, "");

            string oldNoteJson = "{\"id\":\"48372\",\"contacts\":[{\"id\":\"62015\",\"remark\":\"老备注\"}],"
                + "\"threads\":[{\"contact\":\"62015\",\"messages\":[{\"from\":\"62015\",\"to\":\"48372\",\"text\":\"老消息\",\"ts\":1}],\"unread\":0}]}";
            var oldNote = serializer.Deserialize<NoteSettings>(oldNoteJson);
            oldNote.Normalize();
            Check("旧版小纸条 JSON 缺昵称字段 → Normalize 补空串",
                oldNote.nickname + "/" + oldNote.Contact("62015").nick + "/" + oldNote.Thread("62015").messages[0].fromNick,
                "//");

            var ns = new Settings();
            ns.Normalize();
            ns.note.id = "48372";
            ns.note.nickname = "阿哲";
            ns.note.AddConversation("62015", "");
            ns.note.Contact("62015").nick = "老李";
            var nsRt = serializer.Deserialize<Settings>(serializer.Serialize(ns));
            nsRt.Normalize();
            Check("本机昵称 / 对方昵称能存能读",
                nsRt.note.nickname + "/" + nsRt.note.Contact("62015").nick, "阿哲/老李");

            var nz = new NoteSettings();
            var nzc = new NoteContact();
            nzc.id = "62015";
            nzc.nick = "  " + new string('n', 20) + "  ";
            nz.contacts.Add(nzc);
            var nzt = new NoteThread();
            nzt.contact = "62015";
            nzt.messages.Add(new NoteMessage("62015", "48372", "x", 1, "  " + new string('m', 20) + "  "));
            nz.threads.Add(nzt);
            nz.Normalize();
            Check("联系人昵称 Normalize 去空白 / 截 12 字",
                nz.Contact("62015").nick.Length + "/" + nz.Contact("62015").nick, "12/" + new string('n', 12));
            Check("消息昵称 Normalize 去空白 / 截 12 字",
                nz.Thread("62015").messages[0].fromNick.Length + "/" + nz.Thread("62015").messages[0].fromNick,
                "12/" + new string('m', 12));

            // 中继分发的业务验收只走 AcceptPublish（主题 → 收件箱 / 对话码 / 在线状态），不反射私有方法
            var pr = new NoteRelay(new NoteSettings());
            string profId = null, profNick = null;
            bool profOnline = false;
            pr.Profile += delegate(string pid, string pnick, bool ponline)
            {
                profId = pid; profNick = pnick; profOnline = ponline;
            };
            pr.AcceptPublish(NoteRelay.PresenceTopic("62015"), NoteRelay.PresenceJson("62015", " 阿哲 "));
            Check("收到在线状态时广播对方的 ID / 昵称 / 在线",
                profId + "/" + profNick + "/" + profOnline, "62015/阿哲/True");

            var rxSettings = new NoteSettings();
            rxSettings.id = "48372";
            var rx = new NoteRelay(rxSettings);
            NoteIncoming got = null;
            rx.Message += delegate(NoteIncoming incoming) { got = incoming; };
            bool inboxRouted = rx.AcceptPublish(NoteRelay.InboxTopic("48372"),
                NoteRelay.BuildMessageJson("62015", "48372", "你好", 1, "老李"));
            Check("收件箱主题分发给本机并带发送者昵称",
                inboxRouted + "/" + (got == null ? "-" : got.from + "/" + got.text + "/" + got.nick),
                "True/62015/你好/老李");

            var roomRelay = new NoteRelay(rxSettings);
            NoteIncoming roomGot = null;
            roomRelay.Message += delegate(NoteIncoming incoming) { roomGot = incoming; };
            string roomCodeTopic = NoteRelay.RoomTopic("甲乙丙丁戊");
            bool crossed = roomRelay.AcceptPublish(roomCodeTopic,
                NoteRelay.BuildMessageJson("62015", "00000", "串台", 2));
            bool roomed = roomRelay.AcceptPublish(roomCodeTopic,
                NoteRelay.BuildMessageJson("62015", "甲乙丙丁戊", "进群", 3));
            Check("对话码主题拒绝串台、接受同码",
                crossed + "/" + (roomed && roomGot != null ? roomGot.text : "-"), "False/进群");
            Check("无关主题不分发", rx.AcceptPublish("xzt/v1/other/1", "{}").ToString(), "False");

            var idleRelay = new NoteRelay(new NoteSettings());
            idleRelay.RefreshSubscriptions();
            Check("未连接时刷新订阅安静返回", idleRelay.Connected.ToString(), "False");
        }

        // ---- 21) v3.8.1 未读左上角每 5 秒快闪两下 ----
        {
            Check("未读快闪间隔 5 秒", WidgetForm.NoteBlinkIntervalMs.ToString(), "5000");
            Check("一轮两次快闪 420ms 内结束", WidgetForm.NoteBlinkSpanMs.ToString(), "420");
            Check("第 1 下：60ms 亮起", (WidgetForm.CornerFlashAlpha(60) > 0.5) ? "亮" : "灭", "亮");
            Check("第 1 下：75ms 是峰值", (WidgetForm.CornerFlashAlpha(75) > 0.99) ? "亮" : "灭", "亮");
            Check("第 1 下：120ms 仍亮着", (WidgetForm.CornerFlashAlpha(120) > 0.5) ? "亮" : "灭", "亮");
            Check("两下中间有暗隙：200ms 不亮", WidgetForm.CornerFlashAlpha(200).ToString("0.00"), "0.00");
            Check("第 2 下：300ms 又亮起", (WidgetForm.CornerFlashAlpha(300) > 0.5) ? "亮" : "灭", "亮");
            Check("420ms 之后熄灭", WidgetForm.CornerFlashAlpha(420).ToString("0.00") + "/" + WidgetForm.CornerFlashAlpha(900).ToString("0.00"), "0.00/0.00");
            Check("负数/超时也不亮", WidgetForm.CornerFlashAlpha(-5).ToString("0.00") + "/" + WidgetForm.CornerFlashAlpha(421).ToString("0.00"), "0.00/0.00");
        }
        // ---- 20) v3.7.0 六套整体界面主题（皮肤）：表 / 默认与归一化 / 序列化 / 配色 / 令牌 ----
        {
            Func<Color, string> hex = delegate(Color c)
            {
                return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            };
            Func<string, UiSettings> skinUi = delegate(string id)
            {
                var u = new UiSettings();
                u.skin = id;
                return u;
            };
            Func<string, string, bool> glassOf = delegate(string id, string style)
            {
                var u = new UiSettings();
                u.skin = id;
                u.bgStyle = style;
                return Skins.UsesGlass(u);
            };
            Func<string, string> skinAccent = delegate(string id)
            {
                return hex(Skins.ResolveTheme(skinUi(id)).accent);
            };

            Check("皮肤数量", Skins.All.Length.ToString(), "6");
            var skinIds = new HashSet<string>();
            int dupSkin = 0;
            foreach (var sk in Skins.All) if (sk == null || !skinIds.Add(sk.id)) dupSkin++;
            Check("皮肤 id 不重复", dupSkin.ToString(), "0");

            string skinIdList = "", skinNameList = "", skinLightList = "", skinTagList = "";
            foreach (var sk in Skins.All)
            {
                skinIdList += (skinIdList.Length == 0 ? "" : "/") + sk.id;
                skinNameList += (skinNameList.Length == 0 ? "" : "/") + sk.name;
                skinLightList += sk.light ? "1" : "0";
                skinTagList += (skinTagList.Length == 0 ? "" : "/") + (string.IsNullOrWhiteSpace(sk.tagline) ? "0" : "1");
            }
            Check("皮肤 id 顺序", skinIdList, "default/quiet/mica/paper/instrument/neon");
            Check("皮肤名字", skinNameList, "默认/无垠/云母/宣纸/仪表/霓虹");
            Check("只有宣纸是浅色皮肤", skinLightList, "000100");
            Check("每套皮肤都有一句定位说明", skinTagList, "1/1/1/1/1/1");

            Check("Skins.Get 未知 id / null 回退默认",
                Skins.Get("nope").id + "/" + Skins.Get(null).id, "default/default");
            Check("Skins.Get 大小写 / 带空白也认得出（规范成小写 id）",
                Skins.Get("NEON").id + "/" + Skins.Get(" neon ").id + "/" + Skins.Get("Quiet").id,
                "neon/neon/quiet");
            Check("Skins.Current 按 id 取皮肤",
                Skins.Current(skinUi("mica")).name + "/" + Skins.Current(skinUi("paper")).name, "云母/宣纸");
            Check("Skins.Current(null) 回退默认", Skins.Current(null).id, "default");
            Check("Skins.IsDefault 认默认 / 认其它",
                Skins.IsDefault(new UiSettings()).ToString() + "/" + Skins.IsDefault(skinUi("neon")).ToString(),
                "True/False");

            var skinFresh = new Settings();
            Check("新配置默认皮肤", skinFresh.ui.skin, "default");

            // 老配置 / 手改坏的配置：Normalize 把 null / 空 / 未知 / 大小写 / 空白都归到合法 id
            string[] badSkins = new string[] { null, "", "   ", "BOGUS", "NEON", "  neon ", "quiet " };
            string badSkinNorm = "";
            foreach (string bad in badSkins)
            {
                var badSet = new Settings();
                badSet.ui.skin = bad;
                badSet.Normalize();
                badSkinNorm += (badSkinNorm.Length == 0 ? "" : "/") + badSet.ui.skin;
            }
            Check("Normalize 把 null / 空 / 未知 / 大小写错 / 带空白 skin 归一化",
                badSkinNorm, "default/default/default/default/neon/neon/quiet");

            string legalKept = "";
            foreach (var sk in Skins.All)
            {
                var legalSet = new Settings();
                legalSet.ui.skin = sk.id;
                legalSet.Normalize();
                legalKept += legalSet.ui.skin == sk.id ? "1" : "0";
            }
            Check("六个合法 id 原样保留", legalKept, "111111");

            string skinRound = "";
            foreach (var sk in Skins.All)
            {
                var roundSet = new Settings();
                roundSet.ui.skin = sk.id;
                var roundBack = serializer.Deserialize<Settings>(serializer.Serialize(roundSet));
                roundBack.Normalize();
                skinRound += roundBack.ui.skin == sk.id ? "1" : "0";
            }
            Check("六套皮肤 JavaScriptSerializer 往返都保留", skinRound, "111111");

            var oldSkinJson = serializer.Deserialize<Settings>("{\"ui\":{\"theme\":\"midnight\"}}");
            oldSkinJson.Normalize();
            Check("老配置没有 skin 字段补默认", oldSkinJson.ui.skin, "default");
            var badSkinJson = serializer.Deserialize<Settings>("{\"ui\":{\"skin\":\"bogus\"}}");
            badSkinJson.Normalize();
            Check("坏配置的 skin 归一化成默认", badSkinJson.ui.skin, "default");

            // ResolveTheme：默认皮肤跟随 39 套主题；其它五套各自带固定配色
            var followMidnight = new Settings();
            followMidnight.ui.skin = "default";
            followMidnight.ui.theme = "midnight";
            var followIvory = new Settings();
            followIvory.ui.skin = "default";
            followIvory.ui.theme = "ivory";
            Check("默认皮肤跟随 ui.theme（换主题配色跟着变）",
                Skins.ResolveTheme(followMidnight.ui).id + "/" + Skins.ResolveTheme(followIvory.ui).id,
                "midnight/ivory");
            Check("默认皮肤的 ResolveTheme 就是那套主题",
                hex(Skins.ResolveTheme(followIvory.ui).accent) + "/" + hex(Skins.ResolveTheme(followIvory.ui).bgTop),
                hex(Themes.Get("ivory").accent) + "/" + hex(Themes.Get("ivory").bgTop));
            Check("五套皮肤各自带固定 accent",
                skinAccent("quiet") + "/" + skinAccent("mica") + "/" + skinAccent("paper") + "/"
                + skinAccent("instrument") + "/" + skinAccent("neon"),
                "#96A9FF/#6EA8FE/#B23A2C/#FFB02E/#FF3CAC");
            Check("云母 / 宣纸的底色",
                hex(Skins.ResolveTheme(skinUi("mica")).bgTop) + "/" + hex(Skins.ResolveTheme(skinUi("paper")).bgTop),
                "#161D2C/#FDFAF2");
            Check("只有默认皮肤不带固定配色",
                (Skins.Get("default").colors == null) + "/" + (Skins.Get("neon").colors != null), "True/True");

            // UsesGlass：云母恒开；默认皮肤看「背景风格」；其它四套恒关
            Check("磨砂玻璃：云母恒开，默认皮肤看背景风格",
                glassOf("mica", "solid") + "/" + glassOf("default", "glass") + "/" + glassOf("default", "solid"),
                "True/True/False");
            Check("磨砂玻璃：无垠 / 宣纸 / 仪表 / 霓虹恒关（背景风格设玻璃也不开）",
                glassOf("quiet", "glass") + "/" + glassOf("paper", "glass")
                + "/" + glassOf("instrument", "glass") + "/" + glassOf("neon", "glass"),
                "False/False/False/False");
            Check("Skins.UsesGlass(null) 不炸", Skins.UsesGlass(null).ToString(), "False");

            // ---- 形态 / 装饰令牌抽查 ----
            var skinQuiet = Skins.Get("quiet");
            Check("无垠：圆角 10/10 + 细线进度条 + 数字不发光 + 细线卡片",
                skinQuiet.radiusCollapsed + "/" + skinQuiet.radiusExpanded + "/" + skinQuiet.progress
                + "/" + skinQuiet.numberGlow + "/" + skinQuiet.hairlineCards,
                "10/10/Hairline/False/True");
            var skinMica = Skins.Get("mica");
            Check("云母：玻璃 + 胶囊进度条 + 顶部高光",
                skinMica.glass + "/" + skinMica.progress + "/" + skinMica.sheenAlpha.ToString("0.00", CultureInfo.InvariantCulture),
                "True/Pill/0.15");
            var skinPaper = Skins.Get("paper");
            Check("宣纸：圆角 5 + 笔触进度条 + 宋体标题 + 纸纹 + 印章",
                skinPaper.radiusCollapsed + "/" + skinPaper.radiusExpanded + "/" + skinPaper.progress
                + "/" + skinPaper.headingFont + "/" + skinPaper.paperGrain + "/" + skinPaper.seal,
                "5/5/Brush/Noto Serif SC/True/True");
            var skinInstrument = Skins.Get("instrument");
            Check("仪表：圆角 6 + 分刻进度条 + 四角螺丝 + 琥珀数字 + 数字发光",
                skinInstrument.radiusCollapsed + "/" + skinInstrument.radiusExpanded + "/" + skinInstrument.progress
                + "/" + skinInstrument.screws + "/" + hex(skinInstrument.numberColor.Value) + "/" + skinInstrument.numberGlow,
                "6/6/Segmented/True/#FFC24A/True");
            var skinNeon = Skins.Get("neon");
            Check("霓虹：圆角 14 + 霓虹进度条 + 渐变描边 + 数字发光",
                skinNeon.radiusCollapsed + "/" + skinNeon.radiusExpanded + "/" + skinNeon.progress
                + "/" + skinNeon.neonEdge + "/" + skinNeon.numberGlow,
                "14/14/Neon/True/True");
            var skinDefault = Skins.Get("default");
            Check("默认皮肤形态和 v3.6 一致（14/18 + 默认进度条 + 不发光）",
                skinDefault.radiusCollapsed + "/" + skinDefault.radiusExpanded + "/" + skinDefault.progress
                + "/" + skinDefault.numberGlow + "/" + skinDefault.glass,
                "14/18/Default/False/False");
            string skinWinRadius = "";
            foreach (var sk in Skins.All) skinWinRadius += (skinWinRadius.Length == 0 ? "" : "/") + sk.radiusWindow;
            Check("六套皮肤的窗口圆角", skinWinRadius, "12/14/20/8/10/16");
            string skinDefaultTheme = "";
            foreach (var sk in Skins.All) skinDefaultTheme += string.IsNullOrEmpty(sk.defaultThemeId) ? "0" : "1";
            Check("每套皮肤都有默认主题 id（色卡高亮用）", skinDefaultTheme, "111111");

            // CardBorderColor / GlowColor：皮肤指定了就用，否则回到主题
            var midnightTheme = Themes.Get("midnight");
            Check("默认皮肤卡片描边 / 外光 = 主题 edge / accent",
                hex(Skins.CardBorderColor(skinDefault, midnightTheme)) + "/" + hex(Skins.GlowColor(skinDefault, midnightTheme)),
                hex(midnightTheme.edge) + "/" + hex(midnightTheme.accent));
            Check("皮肤自带的描边 / 外光优先于主题",
                hex(Skins.CardBorderColor(skinInstrument, midnightTheme)) + "/"
                + hex(Skins.GlowColor(skinInstrument, midnightTheme)) + "/"
                + hex(Skins.GlowColor(skinNeon, midnightTheme)),
                "#3E3F35/#FFB02E/#FF3CAC");
        }

        // 20. 提醒引擎：日程视图、提醒候选、去重与展示反馈（业务测试只走模块接口）
        {
            Func<string, string, string, string, string, ReminderEvent> Ev = delegate(string id, string title, string start, string end, string source)
            {
                var e = new ReminderEvent();
                e.id = id; e.title = title; e.start = start; e.end = end; e.source = source;
                return e;
            };
            var emptySnap = new ReminderScheduleSnapshot();
            var noSignals = new ReminderSignals();
            var engine = new ReminderEngine();

            // 20.1 日程视图：调用方不排序、不先 Merge 再 Next
            var sAgenda = new Settings();
            sAgenda.Normalize();
            sAgenda.events.Add(Ev("local:2", "下午", "2026-11-16 15:00", "2026-11-16 15:30", "local"));
            sAgenda.events.Add(Ev("local:1", "上午", "2026-11-16 10:00", "2026-11-16 10:30", "local"));
            sAgenda.events.Add(Ev("local:bad", "没有开始", "", "", "local"));
            var lark = new List<ReminderEvent>();
            lark.Add(Ev("lark:2", "午后", "2026-11-16 14:00", "2026-11-16 14:30", "lark"));
            lark.Add(Ev("lark:1", "最早", "2026-11-16 09:30", "2026-11-16 10:00", "lark"));
            var ol = new List<ReminderEvent>();
            ol.Add(Ev("ol:1", "更早", "2026-11-16 08:45", "2026-11-16 09:15", "outlook"));
            sAgenda.outlook.calendars.Add(new CalendarSource { id = "cal-1", name = "工作日历", source = "url", url = "https://example.com/x.ics", enabled = true });
            sAgenda.outlook.enabled = true;
            var snapAgenda = new ReminderScheduleSnapshot(lark, ol);
            string order = "";
            foreach (var e in engine.Agenda(sAgenda, snapAgenda)) order += (order.Length == 0 ? "" : "/") + e.title;
            Check("无序输入下 Agenda 仍按开始时间排序并过滤无效项", order, "更早/最早/上午/午后/下午");
            var next = engine.NextEvent(sAgenda, snapAgenda, new DateTime(2026, 11, 16, 8, 0, 0));
            Check("无序输入下 NextEvent 返回最早的未来日程", next == null ? "null" : next.title, "更早");
            next = engine.NextEvent(sAgenda, snapAgenda, new DateTime(2026, 11, 16, 9, 45, 0));
            Check("NextEvent 跳过已经开始的事项", next == null ? "null" : next.title, "上午");
            sAgenda.calendarEnabled = false;
            next = engine.NextEvent(sAgenda, snapAgenda, new DateTime(2026, 11, 16, 9, 0, 0));
            Check("关掉飞书日程后 NextEvent 只剩手动 + Outlook", next == null ? "null" : next.title, "上午");

            // 20.2 提前量边界、开始后 60 秒、补提醒 60 分钟上限、无结束时间兜底
            var sDue = new Settings();
            sDue.Normalize();
            sDue.eventLeadMinutes = 10;
            sDue.events.Add(Ev("local:due", "周会", "2026-11-16 10:00", "2026-11-16 11:00", "local"));
            var engDue = new ReminderEngine();
            bool beforeDue = engDue.Advance(sDue, emptySnap, new DateTime(2026, 11, 16, 9, 49, 59), noSignals).Started;
            var dueOut = engDue.Advance(sDue, emptySnap, new DateTime(2026, 11, 16, 9, 50, 0), noSignals);
            Check("提前量边界：差 1 秒不提醒、正点提醒",
                beforeDue + "/" + dueOut.Decisions.Count + "/" + (dueOut.Decisions.Count > 0 ? dueOut.Decisions[0].Reason : "-"), "False/1/due");
            Check("重复推进不重复产生同一提醒",
                engDue.Advance(sDue, emptySnap, new DateTime(2026, 11, 16, 9, 50, 0), noSignals).Started.ToString(), "False");
            Check("开始后 59 秒仍在提前提醒窗口",
                new ReminderEngine().Advance(sDue, emptySnap, new DateTime(2026, 11, 16, 10, 0, 59), noSignals).Started.ToString(), "True");
            var lateOut = new ReminderEngine().Advance(sDue, emptySnap, new DateTime(2026, 11, 16, 10, 1, 0), noSignals);
            Check("开始满 60 秒转为补提醒",
                lateOut.Decisions.Count + "/" + (lateOut.Decisions.Count > 0 ? lateOut.Decisions[0].Reason : "-"), "1/late");
            var sNoEnd = new Settings();
            sNoEnd.Normalize();
            sNoEnd.eventLeadMinutes = 10;
            sNoEnd.events.Add(Ev("local:noend", "取快递", "2026-11-16 10:00", "", "local"));
            bool noEndBefore = new ReminderEngine().Advance(sNoEnd, emptySnap, new DateTime(2026, 11, 16, 10, 59, 59), noSignals).Started;
            bool noEndAfter = new ReminderEngine().Advance(sNoEnd, emptySnap, new DateTime(2026, 11, 16, 11, 0, 0), noSignals).Started;
            Check("补提醒：无结束时间 60 分钟兜底（59 分补、60 分不补）", noEndBefore + "/" + noEndAfter, "True/False");
            var sEnded = new Settings();
            sEnded.Normalize();
            sEnded.events.Add(Ev("local:ended", "已结束的会", "2026-11-16 10:00", "2026-11-16 10:30", "local"));
            bool endedOnly = new ReminderEngine().Advance(sEnded, emptySnap, new DateTime(2026, 11, 16, 10, 31, 0), noSignals).Started;
            var sStillOn = new Settings();
            sStillOn.Normalize();
            sStillOn.events.Add(Ev("local:on", "还开着的会", "2026-11-16 10:20", "2026-11-16 10:50", "local"));
            bool stillOn = new ReminderEngine().Advance(sStillOn, emptySnap, new DateTime(2026, 11, 16, 10, 31, 0), noSignals).Started;
            Check("已结束事项不再提醒、未结束事项仍补", endedOnly + "/" + stillOn, "False/True");
            var sLead = new Settings();
            sLead.Normalize();
            sLead.eventLeadMinutes = 5;
            var leadEv = Ev("local:lead", "单条提前", "2026-11-16 10:00", "2026-11-16 10:30", "local");
            leadEv.leadMinutes = 30;
            sLead.events.Add(leadEv);
            Check("单条日程提前量覆盖默认值",
                new ReminderEngine().Advance(sLead, emptySnap, new DateTime(2026, 11, 16, 9, 30, 0), noSignals).Started.ToString(), "True");

            // 20.3 常驻时长（展示所需的业务信息，由模块给出）
            var sHold = new Settings();
            sHold.Normalize();
            sHold.ui.reminderHoldSeconds = 6;
            var holdEngine = new ReminderEngine();
            var hBase = new DateTime(2026, 11, 16, 10, 0, 0);
            Check("常驻到日程结束",
                holdEngine.HoldUntil(sHold, Ev("lark:h", "会", "2026-11-16 09:50", "2026-11-16 11:00", "lark"), hBase).ToString("HH:mm:ss"), "11:00:00");
            Check("无结束时间按 30 分钟",
                holdEngine.HoldUntil(sHold, Ev("local:h", "取快递", "2026-11-16 10:00", "", "local"), hBase).ToString("HH:mm:ss"), "10:30:00");
            Check("休息结束提醒只停最短停留",
                holdEngine.HoldUntil(sHold, Ev("fix:lunch:end", "午休结束", "2026-11-16 13:30", "", "break"), hBase).ToString("HH:mm:ss"), "10:00:06");
            Check("下班提醒一直常驻到打卡",
                holdEngine.HoldUntil(sHold, Ev("alert:offwork", "该下班啦", "2026-11-16 18:00", "", "alert"), hBase).ToString(), DateTime.MaxValue.ToString());
            Check("预览只停最短停留",
                holdEngine.HoldUntil(sHold, Ev("preview", "预览", "2026-11-16 10:00", "", "local"), hBase).ToString("HH:mm:ss"), "10:00:06");

            // 20.4 预览不写真实提醒决策状态：按之后的实际行为断言，不看关闭返回的标志位
            var pvEngine = new ReminderEngine();
            pvEngine.Dismiss(Ev("preview", "预览", "2026-11-16 10:00", "", "alert"), hBase);
            Check("预览关闭不写去重状态", pvEngine.NotifiedCount.ToString(), "0");
            pvEngine.Dismiss(Ev("local:dismiss", "普通", "2026-11-16 10:00", "2026-11-16 10:30", "local"), hBase);
            Check("普通关闭写去重状态", pvEngine.NotifiedCount.ToString(), "1");
            var sPv = new Settings();
            sPv.Normalize();
            foreach (var f in sPv.schedules) if (!f.IsOffWork) { f.enabled = false; f.notify = false; }
            var pvSig = new ReminderSignals();
            pvSig.OffWorkDue = true;
            pvSig.OffWorkTarget = "2026-11-16T20:04";
            Check("预览关闭不会静音真实下班提醒",
                pvEngine.Advance(sPv, emptySnap, hBase, pvSig).Started.ToString(), "True");
        }

        // 21. 提醒引擎：固定日程、下班提醒状态机、关闭反馈与去重清理
        {
            Func<string, string, string, string, string, ReminderEvent> Ev = delegate(string id, string title, string start, string end, string source)
            {
                var e = new ReminderEvent();
                e.id = id; e.title = title; e.start = start; e.end = end; e.source = source;
                return e;
            };
            var emptySnap = new ReminderScheduleSnapshot();
            var noSignals = new ReminderSignals();

            // 21.1 固定日程：开始 / 结束 / 中途启动恢复
            var sFix = new Settings();
            sFix.Normalize();
            sFix.workdays = new bool[] { true, true, true, true, true, true, true };
            sFix.exceptions.Clear();
            sFix.ScheduleOf("lunch").start = "11:50";
            sFix.ScheduleOf("lunch").end = "13:30";
            sFix.ScheduleOf("lunch").notify = true;
            sFix.ScheduleOf("lunch").endNotify = true;
            sFix.ScheduleOf("lunch").enabled = true;
            sFix.ScheduleOf("evening").enabled = false;
            sFix.NormalizeSchedules();
            var engFix = new ReminderEngine();
            var fixStart = engFix.Advance(sFix, emptySnap, new DateTime(2026, 11, 16, 11, 50, 20), noSignals);
            Check("固定日程开始提醒",
                fixStart.Decisions.Count + "/" + (fixStart.Decisions.Count > 0 ? fixStart.Decisions[0].Reason : "-")
                + "/" + (fixStart.Decisions.Count > 0 ? fixStart.Decisions[0].Event.title : "-")
                + "/" + (fixStart.Decisions.Count > 0 ? fixStart.Decisions[0].Event.end : "-"),
                "1/fixed-start/午休时间到/2026-11-16 13:30");
            Check("固定日程同一天不重复",
                engFix.Advance(sFix, emptySnap, new DateTime(2026, 11, 16, 11, 50, 40), noSignals).Started.ToString(), "False");
            Check("固定日程未到点不响",
                new ReminderEngine().Advance(sFix, emptySnap, new DateTime(2026, 11, 16, 11, 49, 0), noSignals).Started.ToString(), "False");
            var fixEnd = engFix.Advance(sFix, emptySnap, new DateTime(2026, 11, 16, 13, 30, 0), noSignals);
            Check("固定日程结束提醒",
                fixEnd.Decisions.Count + "/" + (fixEnd.Decisions.Count > 0 ? fixEnd.Decisions[0].Reason : "-")
                + "/" + (fixEnd.Decisions.Count > 0 ? fixEnd.Decisions[0].Event.title : "-"),
                "1/fixed-end/午休结束");
            var engResume = new ReminderEngine();
            var fixResume = engResume.Advance(sFix, emptySnap, new DateTime(2026, 11, 16, 11, 55, 0), noSignals);
            Check("固定日程中途启动恢复",
                fixResume.Decisions.Count + "/" + (fixResume.Decisions.Count > 0 ? fixResume.Decisions[0].Reason : "-")
                + "/" + (fixResume.Decisions.Count > 0 ? fixResume.Decisions[0].Event.title : "-"),
                "1/fixed-resume/午休中");
            Check("中途启动同一天不重复",
                engResume.Advance(sFix, emptySnap, new DateTime(2026, 11, 16, 11, 56, 0), noSignals).Started.ToString(), "False");
            var sRest = new Settings();
            sRest.Normalize();
            sRest.workdays = new bool[] { false, false, false, false, false, false, false };
            sRest.ScheduleOf("lunch").start = "11:50";
            sRest.ScheduleOf("lunch").end = "13:30";
            sRest.ScheduleOf("evening").enabled = false;
            Check("休息日固定日程不响",
                new ReminderEngine().Advance(sRest, emptySnap, new DateTime(2026, 11, 16, 11, 50, 20), noSignals).Started.ToString(), "False");

            // 21.2 常驻下班提醒：5 分钟重播 / 被普通日程打断后恢复 / 打卡同步收起
            var sOff = new Settings();
            sOff.Normalize();
            foreach (var f in sOff.schedules) if (!f.IsOffWork) { f.enabled = false; f.notify = false; }
            var t0 = new DateTime(2026, 11, 16, 18, 30, 0);
            var offSig = new ReminderSignals();
            offSig.OffWorkDue = true;
            offSig.OffWorkTarget = "2026-11-16T20:04";
            var engOff = new ReminderEngine();
            var offStart = engOff.Advance(sOff, emptySnap, t0, offSig);
            Check("下班提醒开始",
                offStart.Decisions.Count + "/" + (offStart.Decisions.Count > 0 ? offStart.Decisions[0].Reason : "-")
                + "/" + (offStart.Decisions.Count > 0 ? offStart.Decisions[0].Event.title : "-")
                + "/" + (offStart.Decisions.Count > 0 ? offStart.Decisions[0].Event.start : "-"),
                "1/offwork-start/该下班啦/2026-11-16 20:04");
            var showSig = new ReminderSignals();
            showSig.OffWorkDue = true;
            showSig.OffWorkTarget = "2026-11-16T20:04";
            showSig.Showing = true;
            Check("下班提醒 5 分钟内不重播",
                engOff.Advance(sOff, emptySnap, t0.AddMinutes(4), showSig).Started.ToString(), "False");
            var offRepeat = engOff.Advance(sOff, emptySnap, t0.AddMinutes(5), showSig);
            Check("下班提醒每 5 分钟重播",
                offRepeat.Decisions.Count + "/" + (offRepeat.Decisions.Count > 0 ? offRepeat.Decisions[0].Reason : "-"),
                "1/offwork-repeat");
            Check("被普通日程打断时不重立常驻",
                engOff.Advance(sOff, emptySnap, t0.AddMinutes(6), showSig).Started.ToString(), "False");
            var endedSig = new ReminderSignals();
            endedSig.OffWorkDue = true;
            endedSig.OffWorkTarget = "2026-11-16T20:04";
            endedSig.Showing = false;
            var offResume = engOff.Advance(sOff, emptySnap, t0.AddMinutes(6), endedSig);
            Check("普通日程结束恢复常驻下班提醒",
                offResume.Decisions.Count + "/" + (offResume.Decisions.Count > 0 ? offResume.Decisions[0].Reason : "-"),
                "1/offwork-resume");
            var noOffSig = new ReminderSignals();
            var offStop = engOff.Advance(sOff, emptySnap, t0.AddMinutes(7), noOffSig);
            Check("下班打卡同步后收起常驻",
                offStop.Decisions.Count + "/" + (offStop.Decisions.Count > 0 ? offStop.Decisions[0].Reason : "-"),
                "1/offwork-stopped");
            Check("收起后不再出现",
                engOff.Advance(sOff, emptySnap, t0.AddMinutes(8), noOffSig).Started.ToString(), "False");

            var engMute = new ReminderEngine();
            var muteStart = engMute.Advance(sOff, emptySnap, t0, offSig);
            engMute.Dismiss(muteStart.Decisions[0].Event, t0);
            Check("关闭下班提醒记入去重", engMute.NotifiedCount.ToString(), "1");
            Check("忽略今天后不再弹",
                engMute.Advance(sOff, emptySnap, t0.AddMinutes(1), offSig).Started.ToString(), "False");
            var nextDay = engMute.Advance(sOff, emptySnap, t0.AddDays(1), offSig);
            Check("第二天自动恢复下班提醒",
                nextDay.Decisions.Count + "/" + (nextDay.Decisions.Count > 0 ? nextDay.Decisions[0].Reason : "-"),
                "1/offwork-start");

            // 21.3 关闭普通 / 固定提醒的反馈与去重
            var sDis = new Settings();
            sDis.Normalize();
            sDis.eventLeadMinutes = 10;
            sDis.events.Add(Ev("local:dis", "要关掉的会", "2026-11-16 10:00", "2026-11-16 11:00", "local"));
            var engDis = new ReminderEngine();
            var disOut = engDis.Advance(sDis, emptySnap, new DateTime(2026, 11, 16, 9, 50, 0), noSignals);
            engDis.Dismiss(disOut.Decisions[0].Event, new DateTime(2026, 11, 16, 9, 50, 0));
            Check("关闭普通提醒记入去重", engDis.NotifiedCount.ToString(), "1");
            Check("关闭后同一日程不再产生",
                engDis.Advance(sDis, emptySnap, new DateTime(2026, 11, 16, 9, 51, 0), noSignals).Started.ToString(), "False");
            var engFd = new ReminderEngine();
            var fdOut = engFd.Advance(sFix, emptySnap, new DateTime(2026, 11, 16, 11, 50, 20), noSignals);
            int fdBefore = engFd.NotifiedCount;
            if (fdOut.Decisions.Count > 0) engFd.Dismiss(fdOut.Decisions[0].Event, new DateTime(2026, 11, 16, 11, 50, 20));
            Check("关闭固定日程提醒记入去重", (engFd.NotifiedCount - fdBefore).ToString(), "1");
            Check("关闭后固定日程不重复",
                engFd.Advance(sFix, emptySnap, new DateTime(2026, 11, 16, 11, 50, 40), noSignals).Started.ToString(), "False");

            // 21.4 同刻多事项：一次推进只起一条，重复推进不重复
            var sSame = new Settings();
            sSame.Normalize();
            sSame.eventLeadMinutes = 10;
            sSame.events.Add(Ev("local:same-a", "同刻 A", "2026-11-16 10:00", "2026-11-16 10:30", "local"));
            sSame.events.Add(Ev("local:same-b", "同刻 B", "2026-11-16 10:00", "2026-11-16 10:30", "local"));
            var engSame = new ReminderEngine();
            var same1 = engSame.Advance(sSame, emptySnap, new DateTime(2026, 11, 16, 10, 0, 0), noSignals);
            var same2 = engSame.Advance(sSame, emptySnap, new DateTime(2026, 11, 16, 10, 0, 0), noSignals);
            var same3 = engSame.Advance(sSame, emptySnap, new DateTime(2026, 11, 16, 10, 0, 0), noSignals);
            string sameTitles = same1.Decisions.Count == 1 && same2.Decisions.Count == 1
                ? same1.Decisions[0].Event.title + "/" + same2.Decisions[0].Event.title : "count";
            Check("同刻多事项每 tick 只起一条、各起一次",
                (sameTitles == "同刻 A/同刻 B" || sameTitles == "同刻 B/同刻 A").ToString(), "True");
            Check("同刻多事项重复推进不重复", same3.Started.ToString(), "False");

            // 21.5 去重清理：跨日、超过阈值都只删已经不可能再触发的键
            var engDay = new ReminderEngine();
            var day1 = engDay.Advance(sFix, emptySnap, new DateTime(2026, 11, 16, 11, 50, 20), noSignals);
            var day2 = engDay.Advance(sFix, emptySnap, new DateTime(2026, 11, 17, 11, 50, 20), noSignals);
            Check("固定日程跨日仍会提醒", day1.Started + "/" + day2.Started, "True/True");
            Check("跨日清理只删过期键", engDay.NotifiedCount.ToString(), "1");

            var sMany = new Settings();
            sMany.Normalize();
            sMany.eventLeadMinutes = 10;
            for (int i = 0; i < 300; i++)
                sMany.events.Add(Ev("local:many" + i, "批量 " + i, "2026-11-16 10:00", "2026-11-16 10:30", "local"));
            var engMany = new ReminderEngine();
            for (int i = 0; i < 300; i++)
                engMany.Advance(sMany, emptySnap, new DateTime(2026, 11, 16, 10, 0, 0), noSignals);
            Check("批量提醒都记入去重", engMany.NotifiedCount.ToString(), "300");
            sMany.events.Add(Ev("local:midnight", "跨夜会", "2026-11-16 23:59", "2026-11-17 01:00", "local"));
            engMany.Advance(sMany, emptySnap, new DateTime(2026, 11, 16, 23, 50, 0), noSignals);
            Check("跨夜补提醒进入去重", engMany.NotifiedCount.ToString(), "301");
            engMany.Advance(sMany, emptySnap, new DateTime(2026, 11, 16, 23, 55, 0), noSignals);
            Check("超过阈值只清理过期键、保留仍有效键", engMany.NotifiedCount.ToString(), "1");
            var midnightOut = engMany.Advance(sMany, emptySnap, new DateTime(2026, 11, 17, 0, 30, 0), noSignals);
            Check("清理后仍有效的事项不重新提醒",
                midnightOut.Started + "/" + engMany.NotifiedCount, "False/1");

            Check("没有未来日程时 NextEvent 返回 null",
                (new ReminderEngine().NextEvent(sDis, emptySnap, new DateTime(2026, 11, 16, 23, 0, 0)) == null).ToString(), "True");

        }

        // 22. 同步模块：请求生命周期、配置代次、按来源缓存与结果发布
        {
            Func<string, string, string, string, ReminderEvent> Ev = delegate(string id, string title, string start, string source)
            {
                return SyncTestKit.Ev(id, title, start, source);
            };
            var fixedNow = new DateTime(2026, 11, 16, 9, 0, 0);
            Func<DateTime> clock = delegate { return fixedNow; };

            // 22.1 启动缓存与来源过滤 / 去重（AC4）
            {
                var sCache = new Settings();
                sCache.Normalize();
                sCache.lark.enabled = false;
                sCache.calendarEnabled = false;
                sCache.outlook.leadMinutes = 10;
                var cacheA = new CalendarSource { id = "cA", name = "工作", source = "url", url = "https://example.com/a.ics", enabled = true };
                cacheA.cached.Add(Ev("ol:dup", "共享的会", "2026-11-16 10:00", "outlook"));
                cacheA.cached.Add(Ev("ol:a", "工作 11 点", "2026-11-16 11:00", "outlook"));
                cacheA.lastSync = "2026-11-15 12:00";
                var cacheB = new CalendarSource { id = "cB", name = "个人", source = "url", url = "https://example.com/b.ics", enabled = true };
                cacheB.cached.Add(Ev("ol:dup", "共享的会", "2026-11-16 10:00", "outlook"));
                cacheB.cached.Add(Ev("ol:b", "个人 9 点", "2026-11-16 09:00", "outlook"));
                cacheB.lastSync = "2026-11-15 13:00";
                var cacheOff = new CalendarSource { id = "cOff", name = "停用的", source = "url", url = "https://example.com/off.ics", enabled = false };
                cacheOff.cached.Add(Ev("ol:off", "停用的会", "2026-11-16 12:00", "outlook"));
                cacheOff.lastError = "停用来源的旧错误";
                sCache.outlook.calendars.Add(cacheA);
                sCache.outlook.calendars.Add(cacheB);
                sCache.outlook.calendars.Add(cacheOff);
                sCache.Normalize();

                var cacheSched = new ManualSyncScheduler();
                var cacheDisp = new ImmediateSyncDispatcher();
                var cacheAtt = new ControlledAttendanceReader();
                var cacheCal = new ControlledCalendarReader();
                var cacheSink = new RecordingSyncConfigSink();
                var cacheEngine = new SyncEngine(cacheSched, cacheDisp, cacheAtt, cacheCal, clock);
                cacheEngine.ConfigSink = cacheSink;
                cacheEngine.Configure(sCache);
                var cacheSnap = cacheEngine.Snapshot();
                string cacheOrder = "";
                foreach (var e in cacheSnap.OutlookEvents) cacheOrder += (cacheOrder.Length == 0 ? "" : "/") + e.title;
                Check("AC4 启动缓存只启用且有效的来源参与", cacheSnap.OutlookEvents.Count.ToString(), "3");
                Check("AC4 启动缓存跨来源去重并按开始时间排序", cacheOrder, "个人 9 点/共享的会/工作 11 点");
                Check("AC4 禁用来源不参与且错误不进入视图",
                    (cacheSnap.OutlookError == null && cacheSnap.OutlookLastSync == "2026-11-15 13:00").ToString(), "True");

                // 禁用 / 删除来源后，同一份视图规则立即生效
                cacheB.enabled = false;
                cacheEngine.Configure(sCache);
                cacheSnap = cacheEngine.Snapshot();
                Check("AC4 请求前禁用来源立即移出视图",
                    cacheSnap.OutlookEvents.Count + "/" + (cacheSnap.OutlookEvents.FindAll(delegate(ReminderEvent e) { return e.id == "ol:b"; }).Count), "2/0");
                cacheB.enabled = true;
                sCache.outlook.calendars.Remove(cacheA);
                cacheEngine.Configure(sCache);
                cacheSnap = cacheEngine.Snapshot();
                Check("AC4 删除来源后不再参与视图",
                    cacheSnap.OutlookEvents.Count + "/" + (cacheSnap.OutlookEvents.FindAll(delegate(ReminderEvent e) { return e.id == "ol:a"; }).Count), "2/0");
            }

            // 22.1b 成功 / 部分失败 / 全部失败共用同一来源过滤与去重（AC4）
            {
                var sView = new Settings();
                sView.Normalize();
                sView.lark.enabled = false;
                sView.calendarEnabled = false;
                var viewA = new CalendarSource { id = "va", name = "工作", source = "url", url = "https://example.com/va.ics", enabled = true };
                viewA.cached.Add(Ev("ol:dup", "共享的会", "2026-11-16 10:00", "outlook"));
                viewA.cached.Add(Ev("ol:a", "工作 11 点", "2026-11-16 11:00", "outlook"));
                var viewB = new CalendarSource { id = "vb", name = "个人", source = "url", url = "https://example.com/vb.ics", enabled = true };
                viewB.cached.Add(Ev("ol:dup", "共享的会", "2026-11-16 10:00", "outlook"));
                viewB.cached.Add(Ev("ol:b", "个人 9 点", "2026-11-16 09:00", "outlook"));
                sView.outlook.calendars.Add(viewA);
                sView.outlook.calendars.Add(viewB);
                sView.Normalize();

                var viewSched = new ManualSyncScheduler();
                var viewAtt = new ControlledAttendanceReader();
                var viewCal = new ControlledCalendarReader();
                var viewSink = new RecordingSyncConfigSink();
                var viewEngine = new SyncEngine(viewSched, new ImmediateSyncDispatcher(), viewAtt, viewCal, clock);
                viewEngine.ConfigSink = viewSink;
                viewEngine.Configure(sView);
                var viewPubs = new List<SyncPublication>();
                viewEngine.Published += delegate(SyncPublication p) { viewPubs.Add(p); };

                Func<List<ReminderEvent>, string> Titles = delegate(List<ReminderEvent> list)
                {
                    string order = "";
                    foreach (var e in list) order += (order.Length == 0 ? "" : "/") + e.title;
                    return order;
                };

                // 成功：两份来源都返回，其中共享了同一个 id
                viewCal.Respond = delegate(CalendarFetchRequest r)
                {
                    if (r.SourceId == "va")
                        return SyncTestKit.CalendarOk("va",
                            Ev("ol:dup", "共享的会", "2026-11-16 10:00", "outlook"),
                            Ev("ol:newa", "新 A", "2026-11-16 12:00", "outlook"));
                    return SyncTestKit.CalendarOk("vb",
                        Ev("ol:dup", "共享的会", "2026-11-16 10:00", "outlook"),
                        Ev("ol:newb", "新 B", "2026-11-16 08:00", "outlook"));
                };
                viewEngine.RequestOutlook(false);
                viewSched.RunAll();
                Check("AC4 成功后仍按 id 去重并排序",
                    Titles(viewEngine.Snapshot().OutlookEvents), "新 B/共享的会/新 A");
                Check("AC4 成功状态与无错误",
                    (viewPubs.Count == 1 ? viewPubs[0].Status : "count") + "/" + (viewEngine.Snapshot().OutlookError == null),
                    "success/True");

                // 部分失败：A 成功，B 失败；B 保留上次成功缓存
                viewCal.Respond = delegate(CalendarFetchRequest r)
                {
                    if (r.SourceId == "va")
                        return SyncTestKit.CalendarOk("va", Ev("ol:newa2", "新 A2", "2026-11-16 13:00", "outlook"));
                    return SyncTestKit.CalendarFail("vb", "B 挂了");
                };
                viewEngine.RequestOutlook(false);
                viewSched.RunAll();
                Check("AC4 部分失败保留失败来源的最后成功缓存",
                    Titles(viewEngine.Snapshot().OutlookEvents), "新 B/共享的会/新 A2");
                string partialError = viewEngine.Snapshot().OutlookError ?? "";
                Check("AC4 部分失败状态与错误带来源名",
                    (viewPubs.Count == 2 ? viewPubs[1].Status : "count") + "/"
                    + (partialError.IndexOf("个人") >= 0 && partialError.IndexOf("B 挂了") >= 0),
                    "partial/True");

                // 全部失败：视图仍用各来源最后一次成功缓存，错误列出两份来源
                viewCal.Respond = delegate(CalendarFetchRequest r)
                {
                    return SyncTestKit.CalendarFail(r.SourceId, r.SourceId + " 挂了");
                };
                viewEngine.RequestOutlook(false);
                viewSched.RunAll();
                string failedError = viewEngine.Snapshot().OutlookError ?? "";
                Check("AC4 全部失败仍显示各来源最后成功缓存",
                    Titles(viewEngine.Snapshot().OutlookEvents), "新 B/共享的会/新 A2");
                Check("AC4 全部失败状态与错误",
                    (viewPubs.Count == 3 ? viewPubs[2].Status : "count") + "/"
                    + (failedError.IndexOf("工作") >= 0 && failedError.IndexOf("个人") >= 0),
                    "failed/True");
            }

            // 22.2 生命周期：同来源去重 / 不同来源并发隔离 / 异常后可刷新（AC1）
            {
                var sLive = new Settings();
                sLive.Normalize();
                sLive.lark.enabled = true;
                sLive.lark.chatId = "oc_live";
                sLive.lark.command = "lark-live";
                sLive.calendarEnabled = true;
                sLive.outlook.calendars.Add(new CalendarSource
                {
                    id = "live", name = "工作", source = "url",
                    url = "https://example.com/live.ics", enabled = true
                });
                sLive.Normalize();

                var sched = new ManualSyncScheduler();
                var disp = new ImmediateSyncDispatcher();
                var att = new ControlledAttendanceReader();
                var cal = new ControlledCalendarReader();
                var sink = new RecordingSyncConfigSink();
                var eng = new SyncEngine(sched, disp, att, cal, clock);
                eng.ConfigSink = sink;
                eng.Configure(sLive);
                var pubs = new List<SyncPublication>();
                eng.Published += delegate(SyncPublication p) { pubs.Add(p); };

                var t1 = eng.RequestAttendance(false);
                var t2 = eng.RequestAttendance(true);
                Check("AC1 同来源在途请求去重",
                    (t1 != null) + "/" + (t2 == null) + "/" + sched.Pending.Count, "True/True/1");
                Check("AC6 快照暴露在途状态而不是私有字段", eng.Snapshot().AttendanceFetching.ToString(), "True");

                att.Respond = delegate(AttendanceReadRequest r) { return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 5, 0)); };
                sched.RunAll();
                Check("AC1 完成后清掉在途状态并可再次刷新",
                    (eng.Snapshot().AttendanceFetching == false) + "/" + att.Calls + "/" + (eng.RequestAttendance(false) != null),
                    "True/1/True");
                sched.RunAll();
                pubs.Clear();
                var manualTicket = eng.RequestAttendance(true);
                sched.RunAll();
                Check("AC1 手动与自动共用同一生命周期",
                    (manualTicket != null) + "/" + (pubs.Count == 1 ? pubs[0].Manual.ToString() : "count" + pubs.Count),
                    "True/True");

                // 三个来源同时跑，按相反顺序完成，各自结果都保留
                pubs.Clear();
                var la = eng.RequestAttendance(false);
                var ll = eng.RequestLarkCalendar(false);
                var lo = eng.RequestOutlook(false);
                Check("AC1 不同来源各自启动且互不阻塞",
                    (la != null) + "/" + (ll != null) + "/" + (lo != null) + "/" + sched.Pending.Count,
                    "True/True/True/3");
                cal.Respond = delegate(CalendarFetchRequest r)
                {
                    if (r.Kind == "lark")
                        return SyncTestKit.CalendarOk("lark:calendar", Ev("lark:1", "飞书会", "2026-11-16 14:00", "lark"));
                    return SyncTestKit.CalendarOk(r.SourceId, Ev("ol:live", "Outlook 会", "2026-11-16 15:00", "outlook"));
                };
                sched.Run(2);
                Check("AC1 乱序完成不覆盖其它来源的在途状态",
                    (eng.Snapshot().AttendanceFetching && eng.Snapshot().LarkFetching
                     && !eng.Snapshot().OutlookFetching && eng.Snapshot().OutlookEvents.Count == 1).ToString(), "True");
                sched.Run(1);
                sched.Run(0);
                Check("AC1 三个来源的结果各自发布",
                    eng.Snapshot().Attendance.ok + "/" + eng.Snapshot().LarkEvents.Count + "/"
                    + eng.Snapshot().OutlookEvents.Count + "/" + pubs.Count,
                    "True/1/1/3");

                // 飞书日历失败时保留上次成功的那一份，不把已有日程抹掉
                cal.Respond = delegate(CalendarFetchRequest r)
                {
                    if (r.Kind == "lark") return SyncTestKit.CalendarFail("lark:calendar", "飞书日程服务超时");
                    return SyncTestKit.CalendarOk(r.SourceId, Ev("ol:live", "Outlook 会", "2026-11-16 15:00", "outlook"));
                };
                eng.RequestLarkCalendar(false);
                sched.RunAll();
                Check("AC4 飞书日程失败保留上次成功缓存",
                    eng.Snapshot().LarkEvents.Count + "/"
                    + (eng.Snapshot().LarkError != null && eng.Snapshot().LarkError.IndexOf("超时") >= 0),
                    "1/True");

                // adapter 异常：状态失败、清掉在途，之后还能再刷新
                pubs.Clear();
                att.Respond = delegate(AttendanceReadRequest r) { throw new Exception("adapter 崩了"); };
                eng.RequestAttendance(false);
                sched.RunAll();
                Check("AC1 后台异常转为失败结果并清掉在途",
                    (pubs.Count == 1 ? pubs[0].Status : "count") + "/"
                    + (eng.Snapshot().Attendance != null && eng.Snapshot().Attendance.error != null
                        && eng.Snapshot().Attendance.error.IndexOf("adapter 崩了") >= 0)
                    + "/" + eng.Snapshot().AttendanceFetching,
                    "failed/True/False");
                att.Respond = delegate(AttendanceReadRequest r) { return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 6, 0)); };
                var afterCrash = eng.RequestAttendance(false);
                sched.RunAll();
                Check("AC1 异常后可再次刷新", (afterCrash != null) + "/" + eng.Snapshot().Attendance.ok, "True/True");

                // 超时（adapter 返回失败）同样不粘住生命周期
                pubs.Clear();
                att.Respond = delegate(AttendanceReadRequest r) { return SyncTestKit.AttendanceFailed("lark-cli 响应超时"); };
                eng.RequestAttendance(false);
                sched.RunAll();
                Check("AC1 超时后清掉在途并可重试",
                    (pubs.Count == 1 ? pubs[0].Status : "count") + "/" + (eng.RequestAttendance(false) != null),
                    "failed/True");
                sched.RunAll();
            }

            // 22.3 请求 A 进行中配置变化：旧结果不得发布 / 写缓存 / 改令牌（AC2）
            {
                var sStale = new Settings();
                sStale.Normalize();
                sStale.lark.enabled = true;
                sStale.lark.chatId = "oc_old";
                sStale.calendarEnabled = true;
                sStale.outlook.calendars.Add(new CalendarSource
                {
                    id = "c1", name = "工作", source = "url",
                    url = "https://example.com/old.ics", enabled = true
                });
                sStale.outlook.calendars.Add(new CalendarSource
                {
                    id = "c2", name = "个人", source = "url",
                    url = "https://example.com/me.ics", enabled = true
                });
                sStale.Normalize();
                sStale.outlook.calendars[0].cached.Add(Ev("ol:old1", "工作旧缓存", "2026-11-16 10:00", "outlook"));
                sStale.outlook.calendars[1].cached.Add(Ev("ol:old2", "个人旧缓存", "2026-11-16 11:00", "outlook"));

                var sched = new ManualSyncScheduler();
                var disp = new ImmediateSyncDispatcher();
                var att = new ControlledAttendanceReader();
                var cal = new ControlledCalendarReader();
                var sink = new RecordingSyncConfigSink();
                var eng = new SyncEngine(sched, disp, att, cal, clock);
                eng.ConfigSink = sink;
                eng.Configure(sStale);
                var pubs = new List<SyncPublication>();
                eng.Published += delegate(SyncPublication p) { pubs.Add(p); };

                // 考勤：请求后改会话 ID，再让旧请求成功
                att.Respond = delegate(AttendanceReadRequest r)
                {
                    if (r.ChatId != "oc_old" && r.ChatId != "oc_new")
                        throw new Exception("后台拿到了未知配置: " + r.ChatId);
                    return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 10, 0));
                };
                var oldAttendance = eng.RequestAttendance(true);
                long oldRevision = eng.ConfigVersion;
                long oldAttendanceVersion = oldAttendance.ConfigVersion;
                sStale.lark.chatId = "oc_new";
                eng.Configure(sStale);
                Check("AC2 配置变化使全局修订号前进", (eng.ConfigVersion > oldRevision).ToString(), "True");
                Check("AC2 旧请求被标记为取消", oldAttendance.Cancelled.ToString(), "True");
                var freshAttendance = eng.RequestAttendance(false);
                Check("AC2 旧请求被取消后新请求可以启动",
                    (freshAttendance != null && sched.Pending.Count == 2
                        && freshAttendance.ConfigVersion > oldAttendanceVersion).ToString(), "True");
                sched.Run(0);
                Check("AC2 旧考勤结果不发布、不更新当前状态",
                    (pubs.Count == 0) + "/" + (eng.Snapshot().Attendance == null) + "/" + att.Requests[0].ChatId,
                    "True/True/oc_old");
                sched.RunAll();
                Check("AC2 新配置的请求正常发布",
                    (pubs.Count == 1) + "/" + (eng.Snapshot().Attendance != null ? eng.Snapshot().Attendance.ok.ToString() : "null")
                    + "/" + att.Requests[1].ChatId,
                    "True/True/oc_new");

                // 飞书日历：请求后关掉日历开关，旧结果不得写回日程或错误
                pubs.Clear();
                cal.Respond = delegate(CalendarFetchRequest r)
                {
                    return SyncTestKit.CalendarOk("lark:calendar", Ev("lark:stale", "旧飞书日程", "2026-11-16 16:00", "lark"));
                };
                eng.RequestLarkCalendar(true);
                sStale.calendarEnabled = false;
                eng.Configure(sStale);
                sched.RunAll();
                Check("AC2 关掉飞书日历后旧结果不写日程/错误",
                    (pubs.Count == 0) + "/" + eng.Snapshot().LarkEvents.Count + "/" + (eng.Snapshot().LarkError == null),
                    "True/0/True");
                sStale.calendarEnabled = true;
                eng.Configure(sStale);

                // Outlook：请求后禁用一份来源，旧结果不得更新任何来源的缓存
                pubs.Clear();
                sink.Updates.Clear();
                cal.Respond = delegate(CalendarFetchRequest r)
                {
                    if (r.SourceId == "c1") return SyncTestKit.CalendarOk("c1", Ev("ol:new1", "工作新日程", "2026-11-16 12:00", "outlook"));
                    return SyncTestKit.CalendarOk("c2", Ev("ol:new2", "个人新日程", "2026-11-16 13:00", "outlook"));
                };
                eng.RequestOutlook(true);
                sStale.outlook.calendars[0].enabled = false;
                eng.Configure(sStale);
                sched.RunAll();
                var disabledSnap = eng.Snapshot();
                string disabledTitles = "";
                foreach (var e in disabledSnap.OutlookEvents) disabledTitles += (disabledTitles.Length == 0 ? "" : "/") + e.title;
                Check("AC2 禁用来源后旧结果不更新当前日程/缓存/错误",
                    (pubs.Count == 0) + "/" + (sink.Updates.Count == 0) + "/" + disabledTitles,
                    "True/True/个人旧缓存");

                // Outlook：删除一份来源，旧结果同样作废
                pubs.Clear();
                sink.Updates.Clear();
                sStale.outlook.calendars[0].enabled = true;
                sStale.outlook.calendars.RemoveAt(0);
                eng.Configure(sStale);
                eng.RequestOutlook(true);
                sStale.outlook.calendars.RemoveAt(0);
                eng.Configure(sStale);
                sched.RunAll();
                Check("AC2 删除来源后旧结果不进入视图",
                    (pubs.Count == 0) + "/" + (eng.Snapshot().OutlookEvents.Count == 0), "True/True");

                // 切换账号：旧请求带回来的轮换令牌不得覆盖当前令牌
                var sAccount = new Settings();
                sAccount.Normalize();
                sAccount.lark.enabled = false;
                sAccount.calendarEnabled = false;
                sAccount.outlook.refreshToken = OutlookGraph.Protect("rt-old");
                sAccount.outlook.account = "old@example.com";
                var graph = new CalendarSource { id = "g1", name = "Outlook 登录", source = "graph", enabled = true };
                graph.cached.Add(Ev("ol:g", "账号旧缓存", "2026-11-16 10:00", "outlook"));
                sAccount.outlook.calendars.Add(graph);
                sAccount.Normalize();
                var accountSched = new ManualSyncScheduler();
                var accountAtt = new ControlledAttendanceReader();
                var accountCal = new ControlledCalendarReader();
                var accountSink = new RecordingSyncConfigSink();
                var accountEngine = new SyncEngine(accountSched, new ImmediateSyncDispatcher(), accountAtt, accountCal, clock);
                accountEngine.ConfigSink = accountSink;
                accountEngine.Configure(sAccount);
                var accountPubs = new List<SyncPublication>();
                accountEngine.Published += delegate(SyncPublication p) { accountPubs.Add(p); };
                string oldProtected = sAccount.outlook.refreshToken;
                accountCal.Respond = delegate(CalendarFetchRequest r)
                {
                    var ok = SyncTestKit.CalendarOk("g1", Ev("ol:gnew", "新账号日程", "2026-11-16 14:00", "outlook"));
                    ok.RotatedRefreshToken = OutlookGraph.Protect("rt-rotated-old-account");
                    return ok;
                };
                accountEngine.RequestOutlook(true);
                sAccount.outlook.refreshToken = OutlookGraph.Protect("rt-new");
                sAccount.outlook.account = "new@example.com";
                accountEngine.Configure(sAccount);
                accountSched.RunAll();
                Check("AC2 切换账号后旧令牌不覆盖当前令牌",
                    (accountPubs.Count == 0) + "/" + (accountSink.Updates.Count == 0) + "/"
                    + (sAccount.outlook.refreshToken == oldProtected) + "/"
                    + accountEngine.Snapshot().OutlookEvents.Count,
                    "True/True/False/1");

                // 旧请求回来的是失败：同样不能把错误盖到当前配置上
                var sOldError = new Settings();
                sOldError.Normalize();
                sOldError.lark.enabled = false;
                sOldError.calendarEnabled = false;
                var errSource = new CalendarSource
                {
                    id = "err", name = "错误日历", source = "url",
                    url = "https://example.com/err.ics", enabled = true
                };
                errSource.cached.Add(Ev("ol:err-old", "错误前缓存", "2026-11-16 10:00", "outlook"));
                sOldError.outlook.calendars.Add(errSource);
                sOldError.Normalize();
                var errSched = new ManualSyncScheduler();
                var errCal = new ControlledCalendarReader();
                var errSink = new RecordingSyncConfigSink();
                var errEngine = new SyncEngine(errSched, new ImmediateSyncDispatcher(),
                    new ControlledAttendanceReader(), errCal, clock);
                errEngine.ConfigSink = errSink;
                errEngine.Configure(sOldError);
                var errPubs = new List<SyncPublication>();
                errEngine.Published += delegate(SyncPublication p) { errPubs.Add(p); };
                errCal.Respond = delegate(CalendarFetchRequest r)
                {
                    return SyncTestKit.CalendarFail("err", "旧请求的失败");
                };
                errEngine.RequestOutlook(true);
                sOldError.outlook.leadMinutes = 5;
                errEngine.Configure(sOldError);
                errSched.RunAll();
                Check("AC2 旧失败结果不改当前错误/缓存",
                    (errPubs.Count == 0) + "/" + (errSink.Updates.Count == 0) + "/"
                    + (errEngine.Snapshot().OutlookError == null) + "/"
                    + errEngine.Snapshot().OutlookEvents.Count,
                    "True/True/True/1");
            }

            // 22.3a 只改 Outlook 账号：在途考勤不被取消并照常发布（#5 AC3）
            {
                var sA = new Settings();
                sA.Normalize();
                sA.lark.enabled = true;
                sA.lark.command = "lark-scope-a";
                sA.lark.chatId = "oc_scope_a";
                sA.calendarEnabled = false;
                sA.outlook.refreshToken = OutlookGraph.Protect("rt-a-old");
                sA.outlook.account = "old@example.com";
                var sourceA = new CalendarSource { id = "a1", name = "Outlook 登录", source = "graph", enabled = true };
                sourceA.cached.Add(Ev("ol:a-old", "改动前缓存", "2026-11-16 10:00", "outlook"));
                sA.outlook.calendars.Add(sourceA);
                sA.Normalize();

                var schedA = new ManualSyncScheduler();
                var attA = new ControlledAttendanceReader();
                var calA = new ControlledCalendarReader();
                var sinkA = new RecordingSyncConfigSink();
                var engA = new SyncEngine(schedA, new ImmediateSyncDispatcher(), attA, calA, clock);
                engA.ConfigSink = sinkA;
                engA.Configure(sA);
                var pubsA = new List<SyncPublication>();
                engA.Published += delegate(SyncPublication p) { pubsA.Add(p); };
                attA.Respond = delegate(AttendanceReadRequest r) { return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 12, 0)); };
                calA.Respond = delegate(CalendarFetchRequest r)
                {
                    var ok = SyncTestKit.CalendarOk("a1", Ev("ol:a-new", "旧账号返回的日程", "2026-11-16 14:00", "outlook"));
                    ok.RotatedRefreshToken = OutlookGraph.Protect("rt-a-rotated");
                    return ok;
                };

                var oldAttendance = engA.RequestAttendance(true);
                var oldOutlook = engA.RequestOutlook(true);
                Check("AC3 考勤与 Outlook 可同时在途",
                    (oldAttendance != null) + "/" + (oldOutlook != null) + "/" + schedA.Pending.Count, "True/True/2");

                sA.outlook.account = "new@example.com";
                sA.outlook.refreshToken = OutlookGraph.Protect("rt-a-new");
                engA.Configure(sA);
                Check("AC3 只改 Outlook 账号不取消在途考勤",
                    oldAttendance.Cancelled + "/" + engA.Snapshot().AttendanceFetching, "False/True");
                Check("AC3 只改 Outlook 账号取消在途 Outlook", oldOutlook.Cancelled.ToString(), "True");

                schedA.Run(0);
                Check("AC3 未受影响的考勤结果照常发布",
                    (pubsA.Count == 1 ? pubsA[0].Scope : "count") + "/"
                    + (engA.Snapshot().Attendance != null && engA.Snapshot().Attendance.ok),
                    "attendance/True");
                schedA.RunAll();
                Check("AC3 旧 Outlook 结果不发布、不写缓存与令牌",
                    (pubsA.Count == 1) + "/" + (sinkA.Updates.Count == 0) + "/"
                    + engA.Snapshot().OutlookEvents.Count + "/"
                    + (OutlookGraph.Unprotect(sA.outlook.refreshToken) == "rt-a-new"),
                    "True/True/1/True");
            }

            // 22.3a2 只改 Outlook 来源：Outlook 在途旧结果被作废（#5 AC3）
            {
                var sA2 = new Settings();
                sA2.Normalize();
                sA2.lark.enabled = false;
                sA2.calendarEnabled = false;
                var sourceA2 = new CalendarSource
                {
                    id = "a2", name = "工作日历", source = "url",
                    url = "https://example.com/a2-old.ics", enabled = true
                };
                sourceA2.cached.Add(Ev("ol:a2-old", "来源旧缓存", "2026-11-16 10:00", "outlook"));
                sA2.outlook.calendars.Add(sourceA2);
                sA2.Normalize();

                var schedA2 = new ManualSyncScheduler();
                var calA2 = new ControlledCalendarReader();
                var sinkA2 = new RecordingSyncConfigSink();
                var engA2 = new SyncEngine(schedA2, new ImmediateSyncDispatcher(),
                    new ControlledAttendanceReader(), calA2, clock);
                engA2.ConfigSink = sinkA2;
                engA2.Configure(sA2);
                var pubsA2 = new List<SyncPublication>();
                engA2.Published += delegate(SyncPublication p) { pubsA2.Add(p); };
                calA2.Respond = delegate(CalendarFetchRequest r)
                {
                    return SyncTestKit.CalendarOk("a2", Ev("ol:a2-new", "旧来源返回的日程", "2026-11-16 15:00", "outlook"));
                };

                var staleA2 = engA2.RequestOutlook(true);
                sA2.outlook.calendars[0].url = "https://example.com/a2-new.ics";
                engA2.Configure(sA2);
                Check("AC3 只改 Outlook 来源取消在途 Outlook", staleA2.Cancelled.ToString(), "True");
                schedA2.RunAll();
                Check("AC3 旧来源结果不发布、不写缓存",
                    (pubsA2.Count == 0) + "/" + (sinkA2.Updates.Count == 0) + "/"
                    + engA2.Snapshot().OutlookEvents.Count + "/" + (engA2.Snapshot().OutlookError == null),
                    "True/True/1/True");
            }

            // 22.3b 改飞书凭证：考勤与飞书日程失效，Outlook 在途不受影响（#5 AC3）
            {
                var sB = new Settings();
                sB.Normalize();
                sB.lark.enabled = true;
                sB.lark.command = "lark-scope-b";
                sB.lark.chatId = "oc_scope_b";
                sB.calendarEnabled = true;
                var sourceB = new CalendarSource
                {
                    id = "b1", name = "工作日历", source = "url",
                    url = "https://example.com/b.ics", enabled = true
                };
                sB.outlook.calendars.Add(sourceB);
                sB.Normalize();

                var schedB = new ManualSyncScheduler();
                var attB = new ControlledAttendanceReader();
                var calB = new ControlledCalendarReader();
                var sinkB = new RecordingSyncConfigSink();
                var engB = new SyncEngine(schedB, new ImmediateSyncDispatcher(), attB, calB, clock);
                engB.ConfigSink = sinkB;
                engB.Configure(sB);
                var pubsB = new List<SyncPublication>();
                engB.Published += delegate(SyncPublication p) { pubsB.Add(p); };
                attB.Respond = delegate(AttendanceReadRequest r) { return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 18, 0)); };
                calB.Respond = delegate(CalendarFetchRequest r)
                {
                    if (r.Kind == "lark")
                        return SyncTestKit.CalendarOk("lark:calendar", Ev("lark:b", "旧飞书日程", "2026-11-16 11:00", "lark"));
                    return SyncTestKit.CalendarOk("b1", Ev("ol:b", "Outlook 日程", "2026-11-16 15:00", "outlook"));
                };

                var oldAttB = engB.RequestAttendance(true);
                var oldLarkB = engB.RequestLarkCalendar(true);
                var oldOutlookB = engB.RequestOutlook(true);
                Check("AC3 三个范围可同时在途",
                    (oldAttB != null) + "/" + (oldLarkB != null) + "/" + (oldOutlookB != null) + "/" + schedB.Pending.Count,
                    "True/True/True/3");

                sB.lark.chatId = "oc_scope_b2";
                engB.Configure(sB);
                Check("AC3 改飞书凭证同时作废考勤与飞书日程",
                    oldAttB.Cancelled + "/" + oldLarkB.Cancelled + "/" + oldOutlookB.Cancelled + "/"
                    + engB.Snapshot().OutlookFetching,
                    "True/True/False/True");

                schedB.RunAll();
                Check("AC3 改飞书凭证不影响 Outlook 结果发布",
                    (pubsB.Count == 1 ? pubsB[0].Scope : "count") + "/" + engB.Snapshot().OutlookEvents.Count
                    + "/" + (engB.Snapshot().Attendance == null) + "/" + engB.Snapshot().LarkEvents.Count
                    + "/" + sinkB.Updates.Count,
                    "outlook/1/True/0/1");
            }

            // 22.3c 配置无变化：任何范围的在途都不被取消（#5 AC3）
            {
                var sC = new Settings();
                sC.Normalize();
                sC.lark.enabled = true;
                sC.lark.command = "lark-scope-c";
                sC.lark.chatId = "oc_scope_c";
                sC.calendarEnabled = true;
                var sourceC = new CalendarSource
                {
                    id = "c1", name = "工作日历", source = "url",
                    url = "https://example.com/c.ics", enabled = true
                };
                sC.outlook.calendars.Add(sourceC);
                sC.Normalize();

                var schedC = new ManualSyncScheduler();
                var attC = new ControlledAttendanceReader();
                var calC = new ControlledCalendarReader();
                var engC = new SyncEngine(schedC, new ImmediateSyncDispatcher(), attC, calC, clock);
                engC.Configure(sC);
                var pubsC = new List<SyncPublication>();
                engC.Published += delegate(SyncPublication p) { pubsC.Add(p); };
                attC.Respond = delegate(AttendanceReadRequest r) { return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 22, 0)); };
                calC.Respond = delegate(CalendarFetchRequest r)
                {
                    if (r.Kind == "lark")
                        return SyncTestKit.CalendarOk("lark:calendar", Ev("lark:c", "飞书日程", "2026-11-16 11:30", "lark"));
                    return SyncTestKit.CalendarOk("c1", Ev("ol:c", "Outlook 日程", "2026-11-16 15:30", "outlook"));
                };

                var liveAttC = engC.RequestAttendance(false);
                var liveLarkC = engC.RequestLarkCalendar(false);
                var liveOutlookC = engC.RequestOutlook(false);
                long revisionC = engC.ConfigVersion;
                engC.Configure(sC.Clone());
                Check("AC3 配置无变化不取消任何范围的在途",
                    (!liveAttC.Cancelled) + "/" + (!liveLarkC.Cancelled) + "/" + (!liveOutlookC.Cancelled)
                    + "/" + schedC.Pending.Count + "/" + (engC.ConfigVersion == revisionC),
                    "True/True/True/3/True");
                schedC.RunAll();
                Check("AC3 配置无变化时三份结果都发布",
                    pubsC.Count + "/" + (pubsC.Count == 3
                        ? pubsC[0].Scope + "/" + pubsC[1].Scope + "/" + pubsC[2].Scope : "count"),
                    "3/attendance/lark-calendar/outlook");
            }

            // 22.3d 过期结果仍不更新状态 / 缓存 / 令牌 / 错误（保留 #3 AC2）
            {
                var sD = new Settings();
                sD.Normalize();
                sD.lark.enabled = true;
                sD.lark.chatId = "oc_scope_d";
                sD.calendarEnabled = false;

                var schedD = new ManualSyncScheduler();
                var attD = new ControlledAttendanceReader();
                var engD = new SyncEngine(schedD, new ImmediateSyncDispatcher(), attD,
                    new ControlledCalendarReader(), clock);
                engD.Configure(sD);
                var pubsD = new List<SyncPublication>();
                engD.Published += delegate(SyncPublication p) { pubsD.Add(p); };
                attD.Respond = delegate(AttendanceReadRequest r)
                {
                    return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 0, 0));
                };
                engD.RequestAttendance(false);
                schedD.RunAll();
                Check("AC2 先发布一份考勤作为当前状态",
                    (pubsD.Count == 1 && engD.Snapshot().Attendance != null && engD.Snapshot().Attendance.ok).ToString(),
                    "True");

                pubsD.Clear();
                attD.Respond = delegate(AttendanceReadRequest r)
                {
                    return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 10, 0, 0));
                };
                var staleD = engD.RequestAttendance(true);
                sD.lark.chatId = "oc_scope_d2";
                engD.Configure(sD);
                schedD.RunAll();
                DateTime? keptD = engD.Snapshot().Attendance == null
                    ? (DateTime?)null : engD.Snapshot().Attendance.clockIn;
                Check("AC2 过期考勤结果不更新状态、不触发成功提示",
                    staleD.Cancelled + "/" + pubsD.Count + "/" + (keptD == new DateTime(2026, 11, 16, 9, 0, 0)).ToString(),
                    "True/0/True");

                // Outlook：切换账号后旧请求返回的成功（含令牌轮换）与失败都不得落地
                var sE = new Settings();
                sE.Normalize();
                sE.lark.enabled = false;
                sE.calendarEnabled = false;
                sE.outlook.refreshToken = OutlookGraph.Protect("rt-e-old");
                sE.outlook.account = "e-old@example.com";
                var sourceE = new CalendarSource { id = "e1", name = "Outlook 登录", source = "graph", enabled = true };
                sourceE.cached.Add(Ev("ol:e-old", "过期前缓存", "2026-11-16 10:00", "outlook"));
                sE.outlook.calendars.Add(sourceE);
                sE.Normalize();

                var schedE = new ManualSyncScheduler();
                var calE = new ControlledCalendarReader();
                var sinkE = new RecordingSyncConfigSink();
                var engE = new SyncEngine(schedE, new ImmediateSyncDispatcher(),
                    new ControlledAttendanceReader(), calE, clock);
                engE.ConfigSink = sinkE;
                engE.Configure(sE);
                var pubsE = new List<SyncPublication>();
                engE.Published += delegate(SyncPublication p) { pubsE.Add(p); };
                calE.Respond = delegate(CalendarFetchRequest r)
                {
                    var ok = SyncTestKit.CalendarOk("e1", Ev("ol:e-new", "旧账号返回的日程", "2026-11-16 14:00", "outlook"));
                    ok.RotatedRefreshToken = OutlookGraph.Protect("rt-e-rotated");
                    return ok;
                };
                var staleE = engE.RequestOutlook(true);
                sE.outlook.account = "e-new@example.com";
                sE.outlook.refreshToken = OutlookGraph.Protect("rt-e-new");
                engE.Configure(sE);
                string tokenE = sE.outlook.refreshToken;
                schedE.RunAll();
                Check("AC2 过期 Outlook 结果不更新缓存 / 令牌 / 错误、不提示",
                    staleE.Cancelled + "/" + (pubsE.Count == 0) + "/" + (sinkE.Updates.Count == 0) + "/"
                    + engE.Snapshot().OutlookEvents.Count + "/" + (engE.Snapshot().OutlookError == null) + "/"
                    + (sE.outlook.refreshToken == tokenE),
                    "True/True/True/1/True/True");

                calE.Respond = delegate(CalendarFetchRequest r)
                {
                    return SyncTestKit.CalendarFail("e1", "旧账号返回的失败");
                };
                var staleFailE = engE.RequestOutlook(true);
                sE.outlook.account = "e-newer@example.com";
                engE.Configure(sE);
                schedE.RunAll();
                Check("AC2 过期 Outlook 失败结果不改当前错误",
                    staleFailE.Cancelled + "/" + (pubsE.Count == 0) + "/" + (sinkE.Updates.Count == 0) + "/"
                    + (engE.Snapshot().OutlookError == null),
                    "True/True/True/True");
            }

            // 22.3e 与同步无关的设置变化不推进同步代次（ADR-0002「按受影响范围生效」）
            {
                var sTheme = new Settings();
                sTheme.Normalize();
                sTheme.lark.enabled = true;
                sTheme.lark.chatId = "oc_theme";
                sTheme.calendarEnabled = false;
                var themeSched = new ManualSyncScheduler();
                var themeAtt = new ControlledAttendanceReader();
                var themeEngine = new SyncEngine(themeSched, new ImmediateSyncDispatcher(),
                    themeAtt, new ControlledCalendarReader(), clock);
                themeEngine.Configure(sTheme);
                var themePubs = new List<SyncPublication>();
                themeEngine.Published += delegate(SyncPublication p) { themePubs.Add(p); };
                themeAtt.Respond = delegate(AttendanceReadRequest r)
                {
                    return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 30, 0));
                };
                long themeVersion = themeEngine.ConfigVersion;
                themeEngine.RequestAttendance(false);
                sTheme.ui.theme = "ivory";
                themeEngine.Configure(sTheme);
                Check("AC2 无关设置变化不推进同步代次",
                    (themeEngine.ConfigVersion == themeVersion).ToString(), "True");
                themeSched.RunAll();
                Check("AC2 无关设置变化不丢弃已开始的同步结果",
                    (themePubs.Count == 1) + "/" + themeEngine.Snapshot().Attendance.ok,
                    "True/True");
            }

            // 22.4 释放模块 / 关闭窗口：在途结果不发布、不写配置（AC3）
            {
                var sClose = new Settings();
                sClose.Normalize();
                sClose.lark.enabled = true;
                sClose.lark.chatId = "oc_close";
                sClose.calendarEnabled = true;
                sClose.outlook.refreshToken = OutlookGraph.Protect("rt-close");
                sClose.outlook.calendars.Add(new CalendarSource
                {
                    id = "gc", name = "Outlook 登录", source = "graph", enabled = true
                });
                sClose.Normalize();
                var sched = new ManualSyncScheduler();
                var att = new ControlledAttendanceReader();
                var cal = new ControlledCalendarReader();
                var sink = new RecordingSyncConfigSink();
                var eng = new SyncEngine(sched, new ImmediateSyncDispatcher(), att, cal, clock);
                eng.ConfigSink = sink;
                eng.Configure(sClose);
                var pubs = new List<SyncPublication>();
                eng.Published += delegate(SyncPublication p) { pubs.Add(p); };

                att.Respond = delegate(AttendanceReadRequest r) { return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 0, 0)); };
                cal.Respond = delegate(CalendarFetchRequest r)
                {
                    var ok = SyncTestKit.CalendarOk("gc", Ev("ol:late", "关闭后返回的日程", "2026-11-16 15:00", "outlook"));
                    ok.RotatedRefreshToken = OutlookGraph.Protect("rt-after-close");
                    return ok;
                };
                eng.RequestAttendance(true);
                eng.RequestOutlook(true);
                eng.Dispose();
                sched.RunAll();
                Check("AC3 释放后在途 adapter 仍可自然结束",
                    (att.Calls == 1 && cal.Calls == 1) + "/" + eng.Snapshot().AttendanceFetching + "/" + eng.Snapshot().OutlookFetching,
                    "True/False/False");
                Check("AC3 释放后不发布界面回调、不写缓存与令牌",
                    (pubs.Count == 0) + "/" + (sink.Updates.Count == 0) + "/" + (eng.Snapshot().Attendance == null)
                    + "/" + eng.Snapshot().OutlookEvents.Count + "/"
                    + (sClose.outlook.refreshToken == OutlookGraph.Protect("rt-close") || OutlookGraph.Unprotect(sClose.outlook.refreshToken) == "rt-close"),
                    "True/True/True/0/True");
                Check("AC3 释放后不再接受新请求", (eng.RequestAttendance(false) == null).ToString(), "True");
                long versionBefore = eng.ConfigVersion;
                eng.Configure(sClose);
                Check("AC3 释放后配置不再变化", (eng.ConfigVersion == versionBefore).ToString(), "True");
            }

            // 22.5 后台 adapter 只读请求快照；接受结果时由状态拥有者应用配置更新（AC5）
            {
                var sSnap = new Settings();
                sSnap.Normalize();
                sSnap.lark.enabled = true;
                sSnap.lark.chatId = "oc_snapshot";
                sSnap.calendarEnabled = false;
                sSnap.outlook.refreshToken = OutlookGraph.Protect("rt-old");
                sSnap.outlook.account = "snapshot@example.com";
                var graph = new CalendarSource { id = "gs", name = "Outlook 登录", source = "graph", enabled = true };
                graph.cached.Add(Ev("ol:before", "同步前缓存", "2026-11-16 09:00", "outlook"));
                sSnap.outlook.calendars.Add(graph);
                sSnap.Normalize();

                var sched = new ManualSyncScheduler();
                var att = new ControlledAttendanceReader();
                var cal = new ControlledCalendarReader();
                var sink = new RecordingSyncConfigSink();
                var eng = new SyncEngine(sched, new ImmediateSyncDispatcher(), att, cal, clock);
                eng.ConfigSink = sink;
                eng.Configure(sSnap);
                var pubs = new List<SyncPublication>();
                eng.Published += delegate(SyncPublication p) { pubs.Add(p); };

                // Configure 之后再改共享 Settings：请求仍使用请求时的快照
                string oldChat = sSnap.lark.chatId;
                string oldUrl = sSnap.outlook.calendars[0].url;
                sSnap.lark.chatId = "oc_changed_after_configure";
                sSnap.outlook.calendars[0].url = "https://example.com/changed.ics";
                att.Respond = delegate(AttendanceReadRequest r)
                {
                    if (r.ChatId != oldChat) throw new Exception("考勤 adapter 读到了改后的配置");
                    return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 20, 0));
                };
                eng.RequestAttendance(false);
                sched.RunAll();
                Check("AC5 考勤 adapter 只读请求时快照", att.Requests[0].ChatId, oldChat);

                cal.Respond = delegate(CalendarFetchRequest r)
                {
                    if (r.Url != oldUrl) throw new Exception("日历 adapter 读到了改后的地址");
                    var ok = SyncTestKit.CalendarOk("gs", Ev("ol:after", "同步后日程", "2026-11-16 10:30", "outlook"));
                    ok.RotatedRefreshToken = OutlookGraph.Protect("rt-new");
                    ok.TokenAt = "2026-11-16 09:20";
                    return ok;
                };
                string settingsTokenBefore = sSnap.outlook.refreshToken;
                string settingsCachedBefore = sSnap.outlook.calendars[0].cached[0].id;
                eng.RequestOutlook(true);
                sched.RunAll();
                Check("AC5 后台 adapter 不原地修改共享 Settings",
                    (sSnap.outlook.refreshToken == settingsTokenBefore) + "/" + (sSnap.outlook.calendars[0].cached[0].id == settingsCachedBefore)
                    + "/" + (sink.Updates.Count == 1),
                    "True/True/True");
                Check("AC5 接受结果时把配置更新交给状态拥有者",
                    (pubs.Count == 2) + "/" + (pubs[1].Status) + "/" + (pubs[1].Snapshot.OutlookEvents.Count),
                    "True/success/1");
                var update = sink.Updates.Count == 0 ? null : sink.Updates[0];
                Check("AC5 令牌轮换以受保护值交给拥有者",
                    update == null ? "null" : (update.RefreshToken != null && update.RefreshToken.StartsWith("dpapi:")
                        && OutlookGraph.Unprotect(update.RefreshToken) == "rt-new") + "/" + update.TokenAt,
                    "True/2026-11-16 09:20");
                Check("AC5 每份来源的缓存与同步时间在更新里",
                    update == null ? "null" : (update.Calendars.Count + "/" + update.Calendars[0].Succeeded + "/"
                        + update.Calendars[0].Events.Count + "/" + update.Calendars[0].LastSync),
                    "1/True/1/2026-11-16 09:00");
                Check("AC5 汇总缓存也在配置更新里",
                    update == null ? "null" : (update.OutlookCached.Count + "/" + update.OutlookLastSync + "/" + (update.OutlookLastError == null || update.OutlookLastError.Length == 0)),
                    "1/2026-11-16 09:00/True");

                // 轮换后的受保护令牌要用于下一次刷新，而不是退回请求时的旧令牌
                cal.Respond = delegate(CalendarFetchRequest r)
                {
                    return SyncTestKit.CalendarOk("gs", Ev("ol:next", "下一次日程", "2026-11-16 12:30", "outlook"));
                };
                eng.RequestOutlook(false);
                sched.RunAll();
                string nextToken = cal.Requests[cal.Requests.Count - 1].RefreshToken;
                Check("AC5 轮换后的令牌用于下一次刷新",
                    OutlookGraph.Unprotect(nextToken), "rt-new");
            }

            // 22.6 CalendarSync.Fetch：认证/解析/失败语义仍集中在原处，且不再写共享配置
            {
                string icsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc-fetch-" + Guid.NewGuid().ToString("N") + ".ics");
                string stamp = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                System.IO.File.WriteAllText(icsPath,
                    "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:fetch-1\r\nSUMMARY:直接读取的会\r\n" +
                    "DTSTART:" + stamp + "T100000\r\nDTEND:" + stamp + "T103000\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n",
                    System.Text.Encoding.UTF8);
                var fetchReq = new CalendarFetchRequest();
                fetchReq.Kind = "file";
                fetchReq.SourceId = "fetch";
                fetchReq.SourceName = "直接读取";
                fetchReq.Path = icsPath;
                fetchReq.LeadMinutes = 0;
                fetchReq.DefaultLeadMinutes = 12;
                fetchReq.From = DateTime.Today;
                fetchReq.To = DateTime.Today.AddDays(14);
                fetchReq.TimeoutMs = 3000;
                var fetched = CalendarSync.Fetch(fetchReq);
                Check("AC5 Fetch 成功解析并套用默认提前量",
                    fetched.Succeeded + "/" + (fetched.Events == null ? -1 : fetched.Events.Count) + "/"
                    + (fetched.Events != null && fetched.Events.Count > 0 ? fetched.Events[0].leadMinutes : -1),
                    "True/1/12");
                fetchReq.Path = icsPath + ".missing";
                var fetchMissing = CalendarSync.Fetch(fetchReq);
                Check("AC5 Fetch 失败保留真实失败语义", (fetchMissing.Succeeded == false && fetchMissing.Error.Length > 0).ToString(), "True");
                var fetchGraph = new CalendarFetchRequest();
                fetchGraph.Kind = "graph";
                fetchGraph.SourceId = "g";
                fetchGraph.From = DateTime.Today;
                fetchGraph.To = DateTime.Today.AddDays(1);
                fetchGraph.TimeoutMs = 100;
                var fetchNoToken = CalendarSync.Fetch(fetchGraph);
                Check("AC5 Fetch 没登录时给可读错误", fetchNoToken.Error, "还没登录 Outlook");

                // 生产日历 adapter 与可控 adapter 走同一个 ICalendarSourceReader seam
                var sProd = new Settings();
                sProd.Normalize();
                sProd.lark.enabled = false;
                sProd.calendarEnabled = false;
                sProd.outlook.calendars.Add(new CalendarSource
                {
                    id = "prod", name = "生产 adapter", source = "file",
                    path = icsPath, enabled = true, leadMinutes = 3
                });
                sProd.Normalize();
                var prodSched = new ManualSyncScheduler();
                var prodSink = new RecordingSyncConfigSink();
                var prodEngine = new SyncEngine(prodSched, new ImmediateSyncDispatcher(),
                    new ControlledAttendanceReader(),
                    new LiveCalendarSourceReader(new LarkClient(new SettingsStore())), clock);
                prodEngine.ConfigSink = prodSink;
                prodEngine.Configure(sProd);
                var prodTicket = prodEngine.RequestOutlook(false);
                prodSched.RunAll();
                Check("生产日历 adapter 走同一 seam",
                    (prodTicket != null) + "/" + prodEngine.Snapshot().OutlookEvents.Count + "/"
                    + (prodSink.Updates.Count == 1 ? prodSink.Updates[0].Calendars[0].Succeeded.ToString() : "no-update"),
                    "True/1/True");
                try { System.IO.File.Delete(icsPath); } catch { }
            }
        }

        // 23. 配置模块：先锁定 SettingsStore 现有公开行为（只读路径信息 + 显式临时文件，不写用户真实配置）
        {
            string root = NewTempDir();
            try
            {
                var store = new SettingsStore();
                Check("现状 历史配置文件名保持不变", System.IO.Path.GetFileName(store.SettingsPath), "settings.json");
                Check("现状 日志仍在配置目录", System.IO.Path.GetFileName(store.LogPath), "widget.log");
                Check("现状 测试程序默认不是便携模式", store.Portable.ToString(), "False");

                var cfg = new Settings();
                cfg.Normalize();
                cfg.mode = "flex";
                cfg.flexMinutes = 615;
                cfg.ui.theme = "ivory";
                cfg.customThemes.Add(new CustomTheme { id = "my-keep", name = "保留", accent = "#6F9E6A" });
                string export = System.IO.Path.Combine(root, "roundtrip.json");
                var exported = store.Export(cfg, export);
                Check("现状 导出显式路径成功", exported.Succeeded.ToString(), "True");
                Check("现状 导出文件完整", System.IO.File.Exists(export) + "/" + ReadAllText(export).TrimEnd().EndsWith("}").ToString(), "True/True");
                var imported = store.Import(export);
                var back = imported.Succeeded ? imported.Settings : null;
                Check("现状 导入导出往返保留日程/主题",
                    (back != null) + "/" + (back == null ? -1 : back.schedules.Count) + "/" +
                    (back == null || back.customThemes.Count != 1 ? "-" : back.customThemes[0].id),
                    "True/3/my-keep");

                // Normalize 的迁移/规范化现状（Save 以后要克隆快照，这些规则本身不变）
                var fresh = new Settings();
                fresh.Normalize();
                Check("现状 固定日程从旧字段长出来", fresh.schedules.Count.ToString(), "3");
                Check("现状 固定日程同步回旧字段", fresh.lunchStart + "/" + fresh.lunchEnd + "/" + fresh.eveningStart, "11:50/13:30/18:00");
            }
            finally
            {
                DeleteTempDir(root);
            }
        }

        // 24. 配置模块：保存结果、快照规范化、安全写入、迁移与调用点反馈（工单 #4）
        {
            string savedNoSave = Environment.GetEnvironmentVariable("WC_NO_SAVE");
            Environment.SetEnvironmentVariable("WC_NO_SAVE", "0");
            Func<bool[], string> Bits = delegate(bool[] a)
            {
                string text = "";
                for (int i = 0; i < a.Length; i++) text += a[i] ? "1" : "0";
                return text;
            };
            try
            {
                // 24.1 首次保存 / 已有文件：完整写入，保存成功即完整落盘
                {
                    string dir = NewTempDir();
                    try
                    {
                        var store = StoreIn(dir);
                        var first = new Settings();
                        first.mode = "fixed";
                        first.startTime = "08:30";
                        first.endTime = "17:30";
                        first.ui.theme = "ivory";
                        first.note.nickname = "保存测试";
                        var save1 = store.Save(first);
                        Check("AC1 首次保存返回成功", save1.Status.ToString(), "Saved");
                        Check("AC1 首次保存落盘完整",
                            System.IO.File.Exists(store.SettingsPath) + "/" + ReadAllText(store.SettingsPath).TrimEnd().EndsWith("}").ToString(),
                            "True/True");
                        Check("AC1 首次保存无临时文件残留", TempFileCount(dir).ToString(), "0");
                        var load1 = store.Load();
                        Check("AC1 保存成功后 Load 结果一致",
                            load1.Status + "/" + load1.Settings.mode + "/" + load1.Settings.startTime + "/" + load1.Settings.ui.theme,
                            "Loaded/fixed/08:30/ivory");
                        Check("AC1 成功代表完整配置已落盘",
                            load1.Settings.schedules.Count + "/" + load1.Settings.note.nickname, "3/保存测试");
                        var disk = new JavaScriptSerializer().Deserialize<Settings>(ReadAllText(store.SettingsPath));
                        Check("AC2 持久化内容与 Load 结果一致",
                            new JavaScriptSerializer().Serialize(disk) == new JavaScriptSerializer().Serialize(load1.Settings) ? "same" : "different",
                            "same");

                        var second = new Settings();
                        second.mode = "flex";
                        second.flexMinutes = 600;
                        second.note.nickname = "第二份";
                        var save2 = store.Save(second);
                        Check("AC1 已有文件覆盖保存返回成功", save2.Status.ToString(), "Saved");
                        var load2 = store.Load();
                        Check("AC1 覆盖后是完整的新配置",
                            load2.Settings.mode + "/" + load2.Settings.flexMinutes + "/" + load2.Settings.note.nickname,
                            "flex/600/第二份");
                        Check("AC1 覆盖后无临时文件残留", TempFileCount(dir).ToString(), "0");
                    }
                    finally { DeleteTempDir(dir); }
                }

                // 24.2 保存只写独立快照：不原地规范化调用者，落盘内容 = Load 结果
                {
                    string dir = NewTempDir();
                    try
                    {
                        var store = StoreIn(dir);
                        var cfg = new Settings();
                        cfg.version = 1;
                        cfg.mode = "bogus-mode";
                        cfg.workdays = new bool[] { true, true, true, true, true, false, false };
                        cfg.ui.opacity = 1;
                        cfg.outlook.url = "https://example.com/legacy.ics";
                        cfg.outlook.source = "url";
                        cfg.outlook.enabled = true;
                        cfg.schedules = null;
                        object uiBefore = cfg.ui;
                        object calendarsBefore = cfg.outlook.calendars;
                        bool[] workdaysBefore = cfg.workdays;
                        var result = store.Save(cfg);
                        Check("AC2 Save 不改调用者顶层字段", result.Status + "/" + cfg.mode + "/" + cfg.version, "Saved/bogus-mode/1");
                        Check("AC2 Save 不改调用者嵌套对象",
                            cfg.ui.opacity + "/" + object.ReferenceEquals(cfg.ui, uiBefore) + "/" + (cfg.schedules == null),
                            "1/True/True");
                        Check("AC2 Save 不改调用者工作日数组",
                            object.ReferenceEquals(cfg.workdays, workdaysBefore) + "/" + Bits(cfg.workdays), "True/1111100");
                        Check("AC2 Save 不改调用者日历列表",
                            object.ReferenceEquals(cfg.outlook.calendars, calendarsBefore) + "/" + cfg.outlook.calendars.Count,
                            "True/0");
                        var loaded = store.Load();
                        Check("AC2 持久化的是规范化快照",
                            loaded.Settings.version + "/" + loaded.Settings.mode + "/" + loaded.Settings.ui.opacity + "/" + Bits(loaded.Settings.workdays),
                            "2/flex/94/0111110");
                        Check("AC2 固定日程与历史日历来源在快照里补齐",
                            loaded.Settings.schedules.Count + "/" + loaded.Settings.outlook.calendars.Count + "/" + loaded.Settings.outlook.calendars[0].url,
                            "3/1/https://example.com/legacy.ics");
                    }
                    finally { DeleteTempDir(dir); }
                }

                // 24.3 序列化 / 临时写 / 替换失败：返回失败，旧配置字节不变；首次失败不留半文件
                {
                    string dir = NewTempDir();
                    try
                    {
                        var writer = StoreIn(dir);
                        var good = new Settings();
                        good.mode = "fixed";
                        good.note.nickname = "上一份有效";
                        writer.Save(good);
                        byte[] before = System.IO.File.ReadAllBytes(writer.SettingsPath);
                        string[] stages = new string[] { "serialize", "temp", "commit" };
                        for (int i = 0; i < stages.Length; i++)
                        {
                            var storage = new ControlledSettingsStorage();
                            var store = new SettingsStore(storage, dir, false);
                            storage.FailStage = stages[i];
                            var changed = new Settings();
                            changed.mode = "flex";
                            changed.flexMinutes = 600;
                            changed.note.nickname = "不该落盘";
                            var result = store.Save(changed);
                            Check("AC1 " + stages[i] + " 失败返回失败", result.Status.ToString(), "Failed");
                            Check("AC1 " + stages[i] + " 失败旧配置字节不变", FileBytesEqual(writer.SettingsPath, before).ToString(), "True");
                            Check("AC1 " + stages[i] + " 失败无临时文件残留", TempFileCount(dir).ToString(), "0");
                        }

                        for (int i = 0; i < stages.Length; i++)
                        {
                            string freshDir = NewTempDir();
                            try
                            {
                                var storage = new ControlledSettingsStorage();
                                var store = new SettingsStore(storage, freshDir, false);
                                storage.FailStage = stages[i];
                                var firstCfg = new Settings();
                                firstCfg.note.nickname = "首次失败";
                                var firstFail = store.Save(firstCfg);
                                Check("AC1 首次保存 " + stages[i] + " 失败返回失败", firstFail.Status.ToString(), "Failed");
                                Check("AC1 首次保存 " + stages[i] + " 失败不留目标文件", System.IO.File.Exists(store.SettingsPath).ToString(), "False");
                                Check("AC1 首次保存 " + stages[i] + " 失败不留临时文件", TempFileCount(freshDir).ToString(), "0");
                                var freshLoad = store.Load();
                                Check("AC1 首次保存 " + stages[i] + " 失败不会被当成有效配置", freshLoad.Status.ToString(), "Fresh");
                            }
                            finally { DeleteTempDir(freshDir); }
                        }

                        string nullDir = NewTempDir();
                        try
                        {
                            var store = StoreIn(nullDir);
                            Check("AC1 没有配置对象时返回失败而不是抛异常", store.Save(null).Status.ToString(), "Failed");
                            Check("AC1 没有配置对象时不产生文件", System.IO.File.Exists(store.SettingsPath).ToString(), "False");
                        }
                        finally { DeleteTempDir(nullDir); }

                        string secretDir = NewTempDir();
                        try
                        {
                            var storage = new ControlledSettingsStorage();
                            storage.FailStage = "commit";
                            storage.FailMessage = "refreshToken=SECRET-VALUE";
                            var store = new SettingsStore(storage, secretDir, false);
                            var secretCfg = new Settings();
                            secretCfg.outlook.refreshToken = "dpapi:SECRET-VALUE";
                            var result = store.Save(secretCfg);
                            Check("AC1 失败信息不含凭据",
                                (result.Message.IndexOf("SECRET-VALUE") < 0 && result.Message.IndexOf("dpapi:") < 0).ToString(), "True");
                        }
                        finally { DeleteTempDir(secretDir); }
                    }
                    finally { DeleteTempDir(dir); }
                }

                // 24.4 旧工作日 / 固定日程 / 日历来源迁移：临时目录内加载并落盘，重复加载不重复变更
                {
                    string dir = NewTempDir();
                    try
                    {
                        var store = StoreIn(dir);
                        var legacy = new Settings();
                        legacy.version = 1;
                        legacy.mode = "flex";
                        legacy.workdays = new bool[] { true, true, true, true, true, false, false };
                        legacy.outlook.url = "https://example.com/legacy.ics";
                        legacy.outlook.source = "url";
                        legacy.outlook.enabled = true;
                        legacy.schedules = null;
                        string legacyJson = new JavaScriptSerializer().Serialize(legacy);
                        System.IO.File.WriteAllText(store.SettingsPath, legacyJson, new System.Text.UTF8Encoding(false));
                        var first = store.Load();
                        string afterFirst = ReadAllText(store.SettingsPath);
                        Check("AC2 旧配置加载后迁移并落盘",
                            first.Status + "/" + first.Settings.version + "/" + Bits(first.Settings.workdays),
                            "Loaded/2/0111110");
                        Check("AC2 迁移后文件被规范化为有效快照",
                            (afterFirst != legacyJson) + "/" + first.Settings.outlook.calendars.Count,
                            "True/1");
                        string calendarId = first.Settings.outlook.calendars[0].id;
                        int scheduleCount = first.Settings.schedules.Count;
                        var second = store.Load();
                        Check("AC2 迁移重复加载不重复变更",
                            (ReadAllText(store.SettingsPath) == afterFirst) + "/" + (second.Settings.outlook.calendars[0].id == calendarId)
                            + "/" + second.Settings.schedules.Count + "/" + Bits(second.Settings.workdays),
                            "True/True/" + scheduleCount + "/0111110");
                    }
                    finally { DeleteTempDir(dir); }
                }

                // 24.4b 加载结果区分首次 / 已有 / 坏文件；坏文件不被当成有效配置覆盖
                {
                    string dir = NewTempDir();
                    try
                    {
                        var store = StoreIn(dir);
                        Check("AC2 没有配置时加载结果是首次", store.Load().Status.ToString(), "Fresh");
                        System.IO.File.WriteAllText(store.SettingsPath, "{ not a config", new System.Text.UTF8Encoding(false));
                        byte[] corrupt = System.IO.File.ReadAllBytes(store.SettingsPath);
                        var fallback = store.Load();
                        Check("AC2 坏配置加载回退默认值并标记",
                            fallback.Status + "/" + fallback.Settings.mode, "Fallback/fixed");
                        Check("AC2 坏配置加载不覆盖原文件", FileBytesEqual(store.SettingsPath, corrupt).ToString(), "True");
                    }
                    finally { DeleteTempDir(dir); }
                }

                // 24.5 普通 / 便携目录、历史文件名与导入导出（临时目录）
                {
                    string dir = NewTempDir();
                    try
                    {
                        string exeDir = System.IO.Path.Combine(dir, "exe");
                        string appData = System.IO.Path.Combine(dir, "appdata");
                        System.IO.Directory.CreateDirectory(exeDir);
                        System.IO.Directory.CreateDirectory(appData);
                        System.IO.File.WriteAllText(System.IO.Path.Combine(exeDir, "portable.txt"), "portable", new System.Text.UTF8Encoding(false));
                        Check("AC4 便携标记识别", SettingsStore.DetectPortable(exeDir).ToString(), "True");
                        Check("AC4 便携目录", SettingsStore.ResolveSettingsDir(exeDir, appData, true), System.IO.Path.Combine(exeDir, "data"));
                        Check("AC4 普通目录", SettingsStore.ResolveSettingsDir(exeDir, appData, false), System.IO.Path.Combine(appData, "WorkCountdown"));
                        var portStore = new SettingsStore(new FileSettingsStorage(), System.IO.Path.Combine(exeDir, "data"), true);
                        Check("AC4 历史配置文件名与便携目录保持",
                            portStore.Portable + "/" + System.IO.Path.GetFileName(portStore.SettingsPath) + "/" + System.IO.Path.GetFileName(portStore.LogPath),
                            "True/settings.json/widget.log");

                        string configDir = System.IO.Path.Combine(dir, "config");
                        System.IO.Directory.CreateDirectory(configDir);
                        var store = StoreIn(configDir);
                        var cfg = new Settings();
                        cfg.mode = "bogus";
                        cfg.schedules = null;
                        cfg.customThemes.Add(new CustomTheme { id = "my-io", name = "导入导出", accent = "#6F9E6A" });
                        string outPath = System.IO.Path.Combine(configDir, "export.json");
                        var export = store.Export(cfg, outPath);
                        Check("AC4 导出成功", export.Status.ToString(), "Exported");
                        Check("AC4 导出不修改调用者",
                            cfg.mode + "/" + (cfg.schedules == null) + "/" + cfg.customThemes.Count,
                            "bogus/True/1");
                        var imported = store.Import(outPath);
                        Check("AC4 导入导出往返且内容已规范化",
                            imported.Status + "/" + imported.Settings.mode + "/" + imported.Settings.schedules.Count + "/" + imported.Settings.customThemes[0].id,
                            "Imported/flex/3/my-io");

                        string badPath = System.IO.Path.Combine(configDir, "bad.json");
                        System.IO.File.WriteAllText(badPath, "{ not json", new System.Text.UTF8Encoding(false));
                        Check("AC4 导入坏文件返回失败", store.Import(badPath).Status.ToString(), "Failed");

                        string oldExport = ReadAllText(outPath);
                        var failStorage = new ControlledSettingsStorage();
                        failStorage.FailStage = "commit";
                        var failStore = new SettingsStore(failStorage, configDir, false);
                        var exportFail = failStore.Export(cfg, outPath);
                        Check("AC4 导出失败返回失败且旧导出文件不变",
                            exportFail.Status + "/" + (ReadAllText(outPath) == oldExport) + "/" + TempFileCount(configDir),
                            "Failed/True/0");
                    }
                    finally { DeleteTempDir(dir); }
                }

                // 24.6 WC_NO_SAVE：保存返回跳过，且完全不碰存储
                {
                    string dir = NewTempDir();
                    try
                    {
                        var storage = new ControlledSettingsStorage();
                        var store = new SettingsStore(storage, dir, false);
                        string previous = Environment.GetEnvironmentVariable("WC_NO_SAVE");
                        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
                        try
                        {
                            var result = store.Save(new Settings());
                            Check("AC6 WC_NO_SAVE 保存返回跳过而不是成功", result.Status.ToString(), "Skipped");
                            Check("AC6 WC_NO_SAVE 不碰存储",
                                storage.SerializeCalls + "/" + storage.TempWrites + "/" + storage.Commits, "0/0/0");
                            Check("AC6 WC_NO_SAVE 不产生配置文件",
                                System.IO.File.Exists(store.SettingsPath).ToString() + "/" + TempFileCount(dir), "False/0");

                            var legacy = new Settings();
                            legacy.version = 1;
                            legacy.workdays = new bool[] { true, true, true, true, true, false, false };
                            System.IO.File.WriteAllText(store.SettingsPath,
                                new JavaScriptSerializer().Serialize(legacy), new System.Text.UTF8Encoding(false));
                            byte[] probeBefore = System.IO.File.ReadAllBytes(store.SettingsPath);
                            var probeLoad = store.Load();
                            Check("AC6 探针加载只在内存迁移、不写盘",
                                (probeLoad.Settings.version == 2) + "/" + FileBytesEqual(store.SettingsPath, probeBefore), "True/True");
                        }
                        finally { Environment.SetEnvironmentVariable("WC_NO_SAVE", previous); }
                    }
                    finally { DeleteTempDir(dir); }
                }

                // 24.7 并发保存被串行化；原子写入只保证单次完整，不仲裁旧快照
                {
                    string dir = NewTempDir();
                    var storage = new ControlledSettingsStorage();
                    try
                    {
                        var storeA = new SettingsStore(storage, dir, false);
                        var storeB = new SettingsStore(storage, dir, false);
                        storage.BlockCommit = new System.Threading.ManualResetEvent(false);
                        storage.CommitEntered = new System.Threading.ManualResetEvent(false);
                        SettingsSaveResult resultA = null;
                        SettingsSaveResult resultB = null;
                        var cfgA = new Settings();
                        cfgA.mode = "flex";
                        cfgA.flexMinutes = 600;
                        cfgA.note.nickname = "并发 A";
                        var cfgB = new Settings();
                        cfgB.mode = "fixed";
                        cfgB.endTime = "19:00";
                        cfgB.note.nickname = "并发 B";
                        var threadA = new System.Threading.Thread(delegate() { resultA = storeA.Save(cfgA); });
                        threadA.IsBackground = true;
                        threadA.Start();
                        storage.CommitEntered.WaitOne(3000);
                        var threadB = new System.Threading.Thread(delegate() { resultB = storeB.Save(cfgB); });
                        threadB.IsBackground = true;
                        threadB.Start();
                        System.Threading.Thread.Sleep(120);
                        string activeDuring = storage.MaxActiveWrites.ToString();
                        storage.BlockCommit.Set();
                        Check("AC5 并发保存被串行化", activeDuring, "1");
                        Check("AC5 并发保存都能完成", (threadA.Join(3000) && threadB.Join(3000)).ToString()
                            + "/" + (resultA == null ? "null" : resultA.Status.ToString())
                            + "/" + (resultB == null ? "null" : resultB.Status.ToString()), "True/Saved/Saved");
                        var loaded = storeA.Load();
                        Check("AC5 并发后文件仍是完整配置", loaded.Status + "/" + loaded.Settings.schedules.Count, "Loaded/3");
                        Check("AC5 并发后无临时文件残留", TempFileCount(dir).ToString(), "0");

                        // 原子写入只保证单次写入不撕裂；旧快照覆盖新快照不由它解决（代次检查属于调用层 / ADR-0002）
                        var newer = new Settings();
                        newer.note.nickname = "新快照";
                        var older = new Settings();
                        older.note.nickname = "旧快照";
                        storeA.Save(newer);
                        storeA.Save(older);
                        Check("AC5 原子写入不解决旧快照覆盖（后写覆盖先写是明确契约）",
                            storeA.Load().Settings.note.nickname, "旧快照");
                    }
                    finally
                    {
                        storage.BlockCommit.Set();
                        DeleteTempDir(dir);
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("WC_NO_SAVE", savedNoSave);
            }
        }

        // 25. 配置应用：变化计划、按变化通知与提交失败语义（工单 #5）
        {
            Func<Settings> Base = delegate
            {
                var cfg = new Settings();
                cfg.Normalize();
                cfg.lark.enabled = false;
                cfg.calendarEnabled = false;
                return cfg;
            };

            // 25.1 首次启动初始化一次；相同配置重复应用零副作用（AC1）
            {
                var cfg = Base();
                cfg.lark.enabled = true;
                cfg.lark.chatId = "oc_apply";
                cfg.calendarEnabled = true;
                cfg.outlook.calendars.Add(new CalendarSource
                {
                    id = "apply-cal", name = "工作日历", source = "url",
                    url = "https://example.com/apply.ics", enabled = true
                });
                cfg.Normalize();
                var target = new RecordingSettingsApplyTarget();
                var apply = new SettingsApplyCoordinator(target);
                var first = apply.Apply(cfg);
                Check("AC1 首次应用表达所有需要初始化的模块",
                    first.FirstApply + "/" + first.AttendancePollChanged + "/" + first.CalendarPollChanged
                    + "/" + first.OutlookPollChanged + "/" + first.SyncConfigChanged + "/" + first.NoteRelayRestart
                    + "/" + first.FontsChanged + "/" + first.DisplayChanged,
                    "True/True/True/True/True/True/True/True");
                Check("AC1 首次应用只按计划通知一次",
                    target.Counts(), "sync=1 attendance=1 calendar=1 outlook=1 fonts=1 display=1 relayRestart=1 relayRefresh=0 panel=0 panelRelayout=0 salary=0 after=1");
                Check("AC1 首次应用把配置交给状态拥有者", target.AdoptCalls + "/" + (target.Adopted == cfg), "1/True");

                target.ResetCounts();
                var same = apply.Apply(cfg.Clone());
                Check("AC1 相同配置重复应用计划为空", same.HasChanges.ToString(), "False");
                Check("AC1 相同配置重复应用不重建连接 / 不重启计时器 / 不关闭面板",
                    target.Counts() + "/relayout=" + target.NotePanelRelayoutCalls,
                    "sync=0 attendance=0 calendar=0 outlook=0 fonts=0 display=0 relayRestart=0 relayRefresh=0 panel=0 panelRelayout=0 salary=0 after=0/relayout=0");
                Check("AC1 相同配置重复应用只更新已应用快照", target.AdoptCalls.ToString(), "1");
            }

            // 25.2 外观变化只刷新显示资源（AC1）
            {
                var cfg = Base();
                var target = new RecordingSettingsApplyTarget();
                var apply = new SettingsApplyCoordinator(target);
                apply.Apply(cfg);
                target.ResetCounts();

                var next = cfg.Clone();
                next.ui.opacity = 55;
                next.ui.skin = "neon";
                var change = apply.Apply(next);
                Check("AC1 外观变化只标记显示资源",
                    change.DisplayChanged + "/" + change.FontsChanged + "/" + change.AttendancePollChanged
                    + "/" + change.CalendarPollChanged + "/" + change.OutlookPollChanged
                    + "/" + change.SyncConfigChanged + "/" + change.NoteRelayRestart,
                    "True/False/False/False/False/False/False");
                Check("AC1 外观变化只调用显示刷新",
                    target.Counts(), "sync=0 attendance=0 calendar=0 outlook=0 fonts=0 display=1 relayRestart=0 relayRefresh=0 panel=1 panelRelayout=0 salary=0 after=1");
            }

            // 25.3 字体变化只重建字体（AC1）
            {
                var cfg = Base();
                var target = new RecordingSettingsApplyTarget();
                var apply = new SettingsApplyCoordinator(target);
                apply.Apply(cfg);
                target.ResetCounts();

                var next = cfg.Clone();
                next.ui.textFont = "Microsoft YaHei UI";
                next.ui.numberFont = "Courier New";
                var change = apply.Apply(next);
                Check("AC1 字体变化只标记字体资源",
                    change.FontsChanged + "/" + change.DisplayChanged + "/" + change.AttendancePollChanged + "/" + change.NoteRelayRestart,
                    "True/False/False/False");
                Check("AC1 字体变化只重建字体（面板字体也随显示资源刷新）",
                    target.Counts(), "sync=0 attendance=0 calendar=0 outlook=0 fonts=1 display=0 relayRestart=0 relayRefresh=0 panel=1 panelRelayout=0 salary=0 after=1");
            }

            // 25.4 置顶 / 位置：置顶按变化应用，位置保持既有独立即时契约（AC5）
            {
                var cfg = Base();
                var target = new RecordingSettingsApplyTarget();
                var apply = new SettingsApplyCoordinator(target);
                apply.Apply(cfg);
                target.ResetCounts();

                var pinned = cfg.Clone();
                pinned.ui.alwaysOnTop = false;
                var change = apply.Apply(pinned);
                Check("AC5 置顶变化只通知置顶模块",
                    change.TopMostChanged + "/" + change.DisplayChanged + "/" + change.AttendancePollChanged
                    + "/" + change.NoteRelayRestart + "/" + target.TopMostCalls,
                    "True/False/False/False/1");

                target.ResetCounts();
                var moved = pinned.Clone();
                moved.ui.edge = "free";
                moved.ui.x = 321;
                moved.ui.y = 654;
                var movedChange = apply.Apply(moved);
                Check("AC5 窗口位置变化不引入全局事务、不触发模块副作用",
                    movedChange.HasChanges + "/" + target.Counts(), "False/sync=0 attendance=0 calendar=0 outlook=0 fonts=0 display=0 relayRestart=0 relayRefresh=0 panel=0 panelRelayout=0 salary=0 after=0");
            }

            // 25.5 轮询间隔只影响对应计时器（AC3）
            {
                var cfg = Base();
                cfg.lark.enabled = true;
                cfg.lark.chatId = "oc_poll";
                cfg.calendarEnabled = true;
                cfg.Normalize();
                var target = new RecordingSettingsApplyTarget();
                var apply = new SettingsApplyCoordinator(target);
                apply.Apply(cfg);
                target.ResetCounts();

                var poll = cfg.Clone();
                poll.lark.pollMinutes = 9;
                var change = apply.Apply(poll);
                Check("AC3 考勤轮询间隔只标记考勤计时器",
                    change.AttendancePollChanged + "/" + change.CalendarPollChanged + "/" + change.OutlookPollChanged
                    + "/" + change.SyncConfigChanged + "/" + target.Counts(),
                    "True/False/False/False/sync=0 attendance=1 calendar=0 outlook=0 fonts=0 display=0 relayRestart=0 relayRefresh=0 panel=0 panelRelayout=0 salary=0 after=1");

                target.ResetCounts();
                var calendarPoll = poll.Clone();
                calendarPoll.calendarPollMinutes = 21;
                var calendarChange = apply.Apply(calendarPoll);
                Check("AC3 日历轮询间隔只影响日历类计时器",
                    calendarChange.AttendancePollChanged + "/" + calendarChange.CalendarPollChanged
                    + "/" + calendarChange.OutlookPollChanged + "/" + calendarChange.SyncConfigChanged
                    + "/" + target.Counts(),
                    "False/True/True/False/sync=0 attendance=0 calendar=1 outlook=1 fonts=0 display=0 relayRestart=0 relayRefresh=0 panel=0 panelRelayout=0 salary=0 after=1");
            }

            // 25.6 来源启停 / 凭据只影响同步契约（AC3）
            {
                var cfg = Base();
                cfg.lark.enabled = true;
                cfg.lark.chatId = "oc_sync";
                cfg.calendarEnabled = true;
                cfg.outlook.calendars.Add(new CalendarSource
                {
                    id = "sync-cal", name = "工作日历", source = "url",
                    url = "https://example.com/sync.ics", enabled = true
                });
                cfg.Normalize();
                var target = new RecordingSettingsApplyTarget();
                var apply = new SettingsApplyCoordinator(target);
                apply.Apply(cfg);
                target.ResetCounts();

                var account = cfg.Clone();
                account.outlook.account = "new@example.com";
                var accountChange = apply.Apply(account);
                Check("AC3 日历账号变化推进同步配置、不动轮询计时器",
                    accountChange.SyncConfigChanged + "/" + accountChange.AttendancePollChanged
                    + "/" + accountChange.CalendarPollChanged + "/" + accountChange.OutlookPollChanged
                    + "/" + target.Counts(),
                    "True/False/False/False/sync=1 attendance=0 calendar=0 outlook=0 fonts=0 display=0 relayRestart=0 relayRefresh=0 panel=0 panelRelayout=0 salary=0 after=1");

                target.ResetCounts();
                var chat = account.Clone();
                chat.lark.chatId = "oc_sync_2";
                var chatChange = apply.Apply(chat);
                Check("AC3 来源账号变化只交给同步模块，不重启无关轮询",
                    chatChange.SyncConfigChanged + "/" + chatChange.AttendancePollChanged
                    + "/" + chatChange.CalendarPollChanged + "/" + chatChange.OutlookPollChanged
                    + "/" + target.Counts(),
                    "True/False/False/False/sync=1 attendance=0 calendar=0 outlook=0 fonts=0 display=0 relayRestart=0 relayRefresh=0 panel=0 panelRelayout=0 salary=0 after=1");
            }

            // 25.7 小纸条：昵称原地刷新，连接身份变化才重建（AC2）
            {
                var cfg = Base();
                cfg.note.nickname = "阿哲";
                cfg.Normalize();
                var target = new RecordingSettingsApplyTarget();
                var apply = new SettingsApplyCoordinator(target);
                apply.Apply(cfg);
                target.ResetCounts();

                var nick = cfg.Clone();
                nick.note.nickname = "阿哲新";
                var nickChange = apply.Apply(nick);
                Check("AC2 昵称变化只原地刷新 presence",
                    nickChange.NoteRelayRestart + "/" + nickChange.NoteSubscriptionsRefresh
                    + "/" + target.RelayRestartCalls + "/" + target.RelayRefreshCalls,
                    "False/True/0/1");

                target.ResetCounts();
                var moved = nick.Clone();
                moved.note.relayHost = "broker.example.com";
                var movedChange = apply.Apply(moved);
                Check("AC2 中继地址变化才重建连接",
                    movedChange.NoteRelayRestart + "/" + movedChange.NoteSubscriptionsRefresh
                    + "/" + target.RelayRestartCalls + "/" + target.RelayRefreshCalls,
                    "True/False/1/0");

                target.ResetCounts();
                var style = moved.Clone();
                style.note.style = "columns";
                var styleChange = apply.Apply(style);
                Check("AC2 面板样式变化只重建面板布局",
                    styleChange.NoteRelayRestart + "/" + styleChange.NotePanelRelayout
                    + "/" + target.RelayRestartCalls + "/" + target.NotePanelRelayoutCalls,
                    "False/True/0/1");

                target.ResetCounts();
                var contacts = style.Clone();
                contacts.note.AddConversation("62015", "小满");
                var contactsChange = apply.Apply(contacts);
                Check("AC2 新增对话原地刷新订阅、不重建连接",
                    contactsChange.NoteRelayRestart + "/" + contactsChange.NoteSubscriptionsRefresh
                    + "/" + target.RelayRestartCalls + "/" + target.RelayRefreshCalls,
                    "False/True/0/1");
            }

            // 25.8 工资清空只收起工资面板（AC1）
            {
                var cfg = Base();
                cfg.salary.monthly = 21000m;
                var target = new RecordingSettingsApplyTarget();
                var apply = new SettingsApplyCoordinator(target);
                apply.Apply(cfg);
                target.ResetCounts();

                var raised = cfg.Clone();
                raised.salary.monthly = 22000m;
                var raisedChange = apply.Apply(raised);
                Check("AC1 工资变化会重算卡片内容",
                    raisedChange.CardContentChanged + "/" + raisedChange.DisplayChanged
                    + "/" + raisedChange.NoteRelayRestart + "/" + target.AfterCalls,
                    "True/False/False/1");

                target.ResetCounts();
                var cleared = raised.Clone();
                cleared.salary.monthly = 0m;
                var change = apply.Apply(cleared);
                Check("AC1 工资清空只收起工资面板",
                    change.SalaryPanelClose + "/" + change.CardContentChanged + "/" + change.DisplayChanged
                    + "/" + change.NoteRelayRestart + "/" + target.SalaryCalls,
                    "True/True/False/False/1");
            }

            // 25.9 配置提交：只有 Saved 才应用；失败 / 跳过保留编辑（AC4）
            {
                string dir = NewTempDir();
                try
                {
                    var storage = new ControlledSettingsStorage();
                    var store = new SettingsStore(storage, dir, false);
                    var target = new RecordingSettingsApplyTarget();
                    var apply = new SettingsApplyCoordinator(target);
                    var commit = new SettingsCommitCoordinator(store, apply);

                    string previousNoSave = Environment.GetEnvironmentVariable("WC_NO_SAVE");
                    Environment.SetEnvironmentVariable("WC_NO_SAVE", "0");
                    try
                    {
                        var candidate = new Settings();
                        candidate.Normalize();
                        candidate.ui.skin = "neon";
                        var ok = commit.Commit(candidate, "测试保存");
                        Check("AC4 保存成功后才应用变化",
                            ok.IsSuccess + "/" + ok.Applied + "/" + target.AdoptCalls + "/" + target.DisplayCalls + "/" + target.AfterCalls,
                            "True/True/1/1/1");
                        Check("AC4 保存成功落盘的是候选配置", store.Load().Settings.ui.skin, "neon");

                        // 导入与普通保存走同一个提交 + 变化计划；都只在 Saved 之后应用。
                        var importedCandidate = candidate.Clone();
                        importedCandidate.ui.skin = "quiet";
                        var importedResult = commit.Commit(importedCandidate, "导入配置");
                        Check("AC5 导入与普通保存共用同一提交 / 应用约定",
                            importedResult.IsSuccess + "/" + importedResult.Plan.HasChanges
                            + "/" + target.AdoptCalls + "/" + importedCandidate.ui.skin,
                            "True/True/2/quiet");

                        storage.FailStage = "commit";
                        var edited = candidate.Clone();
                        edited.ui.skin = "quiet";
                        edited.note.nickname = "保留的编辑";
                        int adoptBefore = target.AdoptCalls;
                        int displayBefore = target.DisplayCalls;
                        var failed = commit.Commit(edited, "测试失败");
                        Check("AC4 保存失败不应用、不切换已应用配置",
                            failed.IsFailed + "/" + failed.Applied + "/" + (!failed.IsSuccess)
                            + "/" + (target.AdoptCalls == adoptBefore) + "/" + (target.DisplayCalls == displayBefore),
                            "True/False/True/True/True");
                        Check("AC4 保存失败保留待修改内容",
                            edited.ui.skin + "/" + edited.note.nickname, "quiet/保留的编辑");
                        Check("AC4 保存失败显示的是失败消息而不是成功",
                            (failed.Message.Length > 0 && failed.Message.IndexOf("失败") >= 0).ToString(), "True");

                        storage.FailStage = null;
                        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
                        var skipped = commit.Commit(edited, "探针跳过");
                        Check("AC4 WC_NO_SAVE 跳过不显示成功、不应用变化",
                            skipped.IsSkipped + "/" + skipped.IsSuccess + "/" + skipped.Applied
                            + "/" + (target.AdoptCalls == adoptBefore),
                            "True/False/False/True");
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable("WC_NO_SAVE", previousNoSave);
                    }
                }
                finally
                {
                    DeleteTempDir(dir);
                }
            }

            // 25.10 与同步模块的契约：配置变化拒绝旧结果；无关变化保留在途结果（AC3）
            {
                Func<DateTime> clock = delegate { return new DateTime(2026, 11, 16, 9, 0, 0); };
                var cfg = Base();
                cfg.lark.enabled = true;
                cfg.lark.chatId = "oc_contract";
                cfg.calendarEnabled = true;
                cfg.outlook.calendars.Add(new CalendarSource
                {
                    id = "contract-cal", name = "工作日历", source = "url",
                    url = "https://example.com/contract.ics", enabled = true
                });
                cfg.Normalize();

                var sched = new ManualSyncScheduler();
                var att = new ControlledAttendanceReader();
                var cal = new ControlledCalendarReader();
                var engine = new SyncEngine(sched, new ImmediateSyncDispatcher(), att, cal, clock);
                var target = new RecordingSettingsApplyTarget();
                target.Engine = engine;
                var apply = new SettingsApplyCoordinator(target);
                var pubs = new List<SyncPublication>();
                engine.Published += delegate(SyncPublication p) { pubs.Add(p); };
                cal.Respond = delegate(CalendarFetchRequest r) { return SyncTestKit.CalendarOk(r.SourceId); };
                att.Respond = delegate(AttendanceReadRequest r) { return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 5, 0)); };

                apply.Apply(cfg);
                long version = engine.ConfigVersion;
                var stale = engine.RequestOutlook(false);
                var next = cfg.Clone();
                next.outlook.calendars[0].enabled = false;
                var change = apply.Apply(next);
                Check("AC3 来源启停推进同步代次",
                    change.SyncConfigChanged + "/" + (engine.ConfigVersion > version), "True/True");
                sched.RunAll();
                Check("AC3 配置变化后旧同步结果被同步模块拒绝",
                    (stale != null) + "/" + pubs.Count + "/" + engine.Snapshot().OutlookEvents.Count,
                    "True/0/0");

                // 轮询间隔不属于同步结果配置：不推进代次，在途结果照常发布
                var cfg2 = Base();
                cfg2.lark.enabled = true;
                cfg2.lark.chatId = "oc_contract_2";
                cfg2.Normalize();
                var sched2 = new ManualSyncScheduler();
                var att2 = new ControlledAttendanceReader();
                var cal2 = new ControlledCalendarReader();
                var engine2 = new SyncEngine(sched2, new ImmediateSyncDispatcher(), att2, cal2, clock);
                var target2 = new RecordingSettingsApplyTarget();
                target2.Engine = engine2;
                var apply2 = new SettingsApplyCoordinator(target2);
                var pubs2 = new List<SyncPublication>();
                engine2.Published += delegate(SyncPublication p) { pubs2.Add(p); };
                att2.Respond = delegate(AttendanceReadRequest r) { return SyncTestKit.Attendance(new DateTime(2026, 11, 16, 9, 6, 0)); };
                apply2.Apply(cfg2);
                long version2 = engine2.ConfigVersion;
                var live = engine2.RequestAttendance(false);
                var polled = cfg2.Clone();
                polled.lark.pollMinutes = 12;
                var pollChange = apply2.Apply(polled);
                Check("AC3 轮询间隔变化不推进同步代次",
                    pollChange.SyncConfigChanged + "/" + pollChange.AttendancePollChanged
                    + "/" + (engine2.ConfigVersion == version2), "False/True/True");
                sched2.RunAll();
                Check("AC3 无关变化不丢弃在途同步结果",
                    (live != null) + "/" + pubs2.Count + "/" + engine2.Snapshot().Attendance.ok,
                    "True/1/True");
            }

            // 25.11 变化计划使用独立快照，不原地改调用者
            {
                var original = new Settings();
                original.Normalize();
                original.note.contacts.Add(new NoteContact { id = "62015", remark = "阿哲" });
                var copy = original.Clone();
                copy.note.contacts[0].remark = "小满";
                Check("变化计划使用独立快照，不原地改调用者", original.note.contacts[0].remark, "阿哲");
            }
        }

            // 26. 检查更新页的变更记录（v4.0.0）：每个大版本一条，新的在前
            {
                var entries = UpdateChangelog.Entries;
                Check("26.1 变更记录非空", (entries != null && entries.Count > 0).ToString(), "True");

                Func<string, int[]> parts = delegate(string v)
                {
                    string[] seg = v.Split('.');
                    int[] n = new int[seg.Length];
                    for (int i = 0; i < seg.Length; i++) n[i] = int.Parse(seg[i], CultureInfo.InvariantCulture);
                    return n;
                };
                Func<string, string, int> cmp = delegate(string a, string b)
                {
                    int[] x = parts(a), y = parts(b);
                    for (int i = 0; i < 3; i++)
                    {
                        int xa = i < x.Length ? x[i] : 0;
                        int yb = i < y.Length ? y[i] : 0;
                        if (xa != yb) return xa < yb ? -1 : 1;
                    }
                    return 0;
                };
                bool ordered = true, unique = true;
                var seen = new List<string>();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i > 0 && cmp(entries[i - 1].Version, entries[i].Version) <= 0) ordered = false;
                    if (seen.Contains(entries[i].Version)) unique = false;
                    seen.Add(entries[i].Version);
                }
                Check("26.2 版本从新到旧", ordered.ToString(), "True");
                Check("26.3 版本唯一", unique.ToString(), "True");
                Check("26.4 Latest 就是当前版本", UpdateChangelog.Latest == null ? "" : UpdateChangelog.Latest.Version,
                    AppInfo.Version);

                bool allTitled = true, allBullets = true, allDates = true, allFormats = true;
                for (int i = 0; i < entries.Count; i++)
                {
                    ChangelogEntry e = entries[i];
                    if (string.IsNullOrEmpty(e.Title)) allTitled = false;
                    if (e.Bullets == null || e.Bullets.Length < 2 || e.Bullets.Length > 5) allBullets = false;
                    else for (int j = 0; j < e.Bullets.Length; j++) if (string.IsNullOrEmpty(e.Bullets[j])) allBullets = false;
                    if (e.Date == null || e.Date.Length != 10 || e.Date[4] != '-' || e.Date[7] != '-') allDates = false;
                    string[] seg = e.Version.Split('.');
                    if (seg.Length != 2 && seg.Length != 3) allFormats = false;
                    else for (int j = 0; j < seg.Length; j++)
                    {
                        int num;
                        if (!int.TryParse(seg[j], out num) || num < 0) allFormats = false;
                    }
                }
                Check("26.5 每条都有标题", allTitled.ToString(), "True");
                Check("26.6 每条 2-5 条要点", allBullets.ToString(), "True");
                Check("26.7 日期格式 yyyy-MM-dd", allDates.ToString(), "True");
                Check("26.8 版本号格式 x.y 或 x.y.z", allFormats.ToString(), "True");

                string[] required = new string[] { "4.0.0", "3.8", "3.7", "3.6", "3.5", "3.4", "3.3", "3.2" };
                bool hasAll = true;
                for (int i = 0; i < required.Length; i++)
                    if (entries.FindIndex(delegate(ChangelogEntry e) { return e.Version == required[i]; }) < 0)
                        hasAll = false;
                Check("26.9 4.0.0 与 3.2~3.8 大版本都在", hasAll.ToString(), "True");

                ChangelogEntry v4 = entries.Find(delegate(ChangelogEntry e) { return e.Version == "4.0.0"; });
                string text4 = v4 == null ? "" : string.Join(" | ", v4.Bullets);
                Check("26.10 4.0.0 记的是本轮重构（提醒 / 同步 / 更新记录）",
                    (text4.IndexOf("ReminderEngine", StringComparison.Ordinal) >= 0
                    && text4.IndexOf("SyncEngine", StringComparison.Ordinal) >= 0
                    && text4.IndexOf("更新记录", StringComparison.Ordinal) >= 0).ToString(),
                    "True");
            }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURES");
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
