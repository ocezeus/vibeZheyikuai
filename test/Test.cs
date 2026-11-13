using System;
using System.Collections.Generic;
using System.Globalization;
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
        Check("提前量到点触发", Reminders.IsDue(s16, ev, new DateTime(2026, 11, 4, 10, 0, 0)).ToString(), "True");
        Check("没到提醒时间", Reminders.IsDue(s16, ev, new DateTime(2026, 11, 4, 9, 59, 0)).ToString(), "False");
        Check("开始后 1 分钟内仍提醒", Reminders.IsDue(s16, ev, new DateTime(2026, 11, 4, 10, 5, 30)).ToString(), "True");
        Check("开始 1 分钟后过期", Reminders.IsDue(s16, ev, new DateTime(2026, 11, 4, 10, 7, 0)).ToString(), "False");
        Check("倒计时说法", Reminders.CountdownText(s16, ev, new DateTime(2026, 11, 4, 10, 0, 0)), "还有 5 分钟");
        Check("倒计时-马上开始", Reminders.CountdownText(s16, ev, new DateTime(2026, 11, 4, 10, 4, 40)), "马上开始");
        s16.events.Add(ev);
        Check("手动日程进入待提醒", Reminders.Merge(s16, null).Count.ToString(), "1");
        Check("下一个日程", Reminders.Next(s16, Reminders.Merge(s16, null), new DateTime(2026, 11, 4, 10, 0, 0)).title, "产品部周会");
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
        Check("飞书日程并入待提醒", Reminders.Merge(s16, larkList).Count.ToString(), "2");
        s16.calendarEnabled = false;
        Check("关掉飞书日程后只剩手动", Reminders.Merge(s16, larkList).Count.ToString(), "1");

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
                + "/" + BubbleMenuForm.ExtraHDip(BubbleAction.Update), "168/130/222/208/216");
            Check("长开后整颗高度 = 40 + 额外",
                BubbleMenuForm.ItemHDip(BubbleAction.Theme) + "/" + BubbleMenuForm.ItemHDip(BubbleAction.Clock)
                + "/" + BubbleMenuForm.ItemHDip(BubbleAction.Schedule) + "/" + BubbleMenuForm.ItemHDip(BubbleAction.Preview)
                + "/" + BubbleMenuForm.ItemHDip(BubbleAction.Update), "208/170/262/248/256");

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
            Check("当前版本", UpdateService.CurrentVersion + "/" + AppInfo.Version, "3.6.0/3.6.0");
            Check("程序集版本是 3.6.0.0", typeof(AppInfo).Assembly.GetName().Version.ToString(), "3.6.0.0");

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
            string pollError;
            var polled = CalendarSync.Poll(multiSet.outlook, DateTime.Today, DateTime.Today.AddDays(14), 3000, out pollError);
            Check("两份本地日历都能同步", polled.Count + "/" + pollError, "2/");
            Check("同步时套用每份日历自己的提前量",
                polled[0].leadMinutes + "/" + polled[1].leadMinutes, "7/12");
            Check("每份日历都记了同步时间",
                (multiSet.outlook.calendars[0].lastSync.Length > 0 && multiSet.outlook.calendars[1].lastSync.Length > 0).ToString(),
                "True");

            // 把第二份改成一个不存在的文件：第一份照样成功，第二份保留上次缓存
            multiSet.outlook.calendars[1].path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc-missing-" + Guid.NewGuid().ToString("N") + ".ics");
            var partial = CalendarSync.Poll(multiSet.outlook, DateTime.Today, DateTime.Today.AddDays(14), 3000, out pollError);
            Check("一份失败不影响另一份", partial.Count + "/" + (pollError.IndexOf("个人") >= 0), "2/True");
            Check("失败的那份保留自己的缓存", multiSet.outlook.calendars[1].cached.Count.ToString(), "1");
            try { System.IO.File.Delete(f1); System.IO.File.Delete(f2); } catch { }
        }

        // 10) 同步晚于开始时间时补提醒（Outlook 会开始后才同步下来）
        {
            var late = new Settings();
            late.Normalize();
            var e = new ReminderEvent();
            e.start = "2026-11-11 10:00";
            e.end = "2026-11-11 11:00";
            DateTime start = new DateTime(2026, 11, 11, 10, 0, 0);
            Check("晚同步：开始 10 分钟后补提醒", Reminders.IsLateDue(late, e, start.AddMinutes(10)).ToString(), "True");
            Check("正常提醒窗口内不算补提醒", Reminders.IsLateDue(late, e, start.AddSeconds(30)).ToString(), "False");
            Check("会议结束后不补提醒", Reminders.IsLateDue(late, e, new DateTime(2026, 11, 11, 11, 1, 0)).ToString(), "False");
            var noEnd = new ReminderEvent();
            noEnd.start = "2026-11-11 10:00";
            Check("没结束时间按 60 分钟兜底", Reminders.IsLateDue(late, noEnd, start.AddMinutes(59)).ToString(), "True");
            Check("超过 60 分钟不补提醒", Reminders.IsLateDue(late, noEnd, start.AddMinutes(61)).ToString(), "False");
        }

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

        // ---- 小纸条：ID / 8 条 / 联系人 / MQTT / JSON ----
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
            for (int i = 0; i < 10; i++)
            {
                bool incoming = i % 2 == 0;
                var noteMsg = new NoteMessage(incoming ? "62015" : "48372", incoming ? "48372" : "62015", "消息" + i, 1731400000 + i);
                n.Append(noteMsg, incoming);
            }
            Check("每对 ID 只留 8 条", th.messages.Count + "/" + th.messages[0].text + "/" + th.messages[7].text, "8/消息2/消息9");
            Check("未读只算收到的", th.unread.ToString(), "5");
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

            var pr = new NoteRelay(new NoteSettings());
            string profId = null, profNick = null;
            bool profOnline = false;
            pr.Profile += delegate(string pid, string pnick, bool ponline)
            {
                profId = pid; profNick = pnick; profOnline = ponline;
            };
            var deliver = typeof(NoteRelay).GetMethod("DeliverPresence",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            deliver.Invoke(pr, new object[] { "62015", NoteRelay.PresenceJson("62015", " 阿哲 ") });
            Check("收到在线状态时广播对方的 ID / 昵称 / 在线",
                profId + "/" + profNick + "/" + profOnline, "62015/阿哲/True");
            var idleRelay = new NoteRelay(new NoteSettings());
            idleRelay.RefreshSubscriptions();
            Check("未连接时刷新订阅安静返回", idleRelay.Connected.ToString(), "False");
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURES");
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
