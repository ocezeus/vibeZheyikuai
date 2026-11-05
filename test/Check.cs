using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 独立验收：主题色卡占比、浅色主题提醒动画不留亮边、休息提醒触发、日程快捷时间。
class Check
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static int pass, fail;

    static void Ok(string name, bool cond, string detail)
    {
        if (cond) { pass++; Console.WriteLine("PASS  " + name + "  " + detail); }
        else { fail++; Console.WriteLine("FAIL  " + name + "  " + detail); }
    }

    static object Call(object o, string name, params object[] args)
    {
        var m = o.GetType().GetMethod(name, F);
        if (m == null) throw new Exception("no method " + name);
        return m.Invoke(o, args);
    }

    static void CollectQuickChips(Control root, System.Collections.Generic.List<Button> into)
    {
        foreach (Control c in root.Controls)
        {
            var b = c as Button;
            if (b != null && b.Tag is int) into.Add(b);
            if (c.Controls.Count > 0) CollectQuickChips(c, into);
        }
    }

    static void Set(object o, string name, object value)
    {
        var f = o.GetType().GetField(name, F);
        if (f == null) throw new Exception("no field " + name);
        f.SetValue(o, value);
    }

    static object Get(object o, string name)
    {
        var f = o.GetType().GetField(name, F);
        if (f == null) throw new Exception("no field " + name);
        return f.GetValue(o);
    }

    static int Brightness(Color c) { return Math.Max(c.R, Math.Max(c.G, c.B)); }

    static int Distance(Color a, Color b)
    {
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    static string fmt(Color c) { return c.R + "," + c.G + "," + c.B; }

    static double RelLum(Color c)
    {
        double[] v = new double[] { c.R / 255.0, c.G / 255.0, c.B / 255.0 };
        for (int i = 0; i < 3; i++)
            v[i] = v[i] <= 0.03928 ? v[i] / 12.92 : Math.Pow((v[i] + 0.055) / 1.055, 2.4);
        return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2];
    }

    static double Contrast(Color a, Color b)
    {
        double la = RelLum(a), lb = RelLum(b);
        double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    [STAThread]
    static int Main()
    {
        try
        {
            return RunAll();
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION: " + ex.GetType().FullName);
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 99;
        }
    }

    static int RunAll()
    {
        Application.EnableVisualStyles();

        // ---- 1. 主题色卡：底色为主，高亮色只占一条 ----
        {
            var picker = new ThemePicker();
            picker.Size = new Size(456, 104);
            picker.SelectedId = "";
            picker.CreateControl();
            int bad = 0;
            double minShare = 1, maxShare = 0;
            using (var bmp = new Bitmap(picker.Width, picker.Height))
            {
                picker.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                for (int i = 0; i < Themes.All.Length; i++)
                {
                    var t = Themes.All[i];
                    var r = (Rectangle)Call(picker, "ChipRect", i);
                    int accent = 0, total = 0;
                    for (int y = r.Top + 1; y < r.Bottom - 1; y++)
                        for (int x = r.Left + 1; x < r.Right - 1; x++)
                        {
                            Color c = bmp.GetPixel(x, y);
                            total++;
                            if (Math.Abs(c.R - t.accent.R) < 30 && Math.Abs(c.G - t.accent.G) < 30 && Math.Abs(c.B - t.accent.B) < 30)
                                accent++;
                        }
                    if (total == 0) continue;
                    double share = (double)accent / total;
                    if (share < minShare) minShare = share;
                    if (share > maxShare) maxShare = share;
                    if (share < 0.08 || share > 0.42)
                    {
                        bad++;
                        Ok("色卡占比 " + t.name, false, (share * 100).ToString("0.0") + "%");
                    }
                }
            }
            picker.Dispose();
            Ok("主题色卡占比", bad == 0,
                "39 套高亮色占比 " + (minShare * 100).ToString("0") + "%~" + (maxShare * 100).ToString("0") + "%（底色为主）");
        }

        // ---- 2. 浅色主题提醒动画不留亮边 ----
        foreach (string themeId in new string[] { "midnight", "lv-kongque", "ivory" })
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.reminderEffect = "none";   // 取色断言只看色块本身，不要氛围层
            s.ui.theme = themeId;
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 315);
                Call(form, "Recompute");
                var t = Themes.Get(themeId);
                Set(form, "remindEvent", new ReminderEvent
                {
                    title = "长标题占位：产品需求评审与下一阶段排期",
                    detail = "确认交互方案",
                    place = "12F · 会议室 A",
                    start = Reminders.FormatStart(DateTime.Now.AddMinutes(5)),
                    end = Reminders.FormatStart(DateTime.Now.AddMinutes(35)),
                    source = "local"
                });
                Set(form, "reminding", true);
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-2000));   // 完全覆盖的停留段
                using (var bmp = new Bitmap(420, 315))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 315)));

                    bool light = Brightness(t.bgBottom) > 200;
                    // 整个卡片都是提醒的色块：边上不该露出任何卡片底色。
                    // 日程提醒跟主题走，所以这里比对「这套主题折算出的两层色」，而不是死看深浅。
                    Color expectA, expectB, expectInk;
                    ReminderStyles.Resolve(ReminderStyles.Follow, t, out expectA, out expectB, out expectInk);
                    Color rightEdge = bmp.GetPixel(419, 150);
                    Color bottomEdge = bmp.GetPixel(210, 313);
                    Color inner = bmp.GetPixel(300, 150);
                    bool covered = (Distance(rightEdge, expectA) <= 10 || Distance(rightEdge, expectB) <= 10)
                        && (Distance(bottomEdge, expectA) <= 10 || Distance(bottomEdge, expectB) <= 10)
                        && (Distance(inner, expectA) <= 10 || Distance(inner, expectB) <= 10);
                    Ok("提醒覆盖 " + themeId + (light ? "(浅色)" : "(深色)"), covered,
                        "右边=" + rightEdge.R + "," + rightEdge.G + "," + rightEdge.B
                        + " 下边=" + bottomEdge.R + "," + bottomEdge.G + "," + bottomEdge.B
                        + " 期望=" + expectA.R + "," + expectA.G + "," + expectA.B
                        + " / " + expectB.R + "," + expectB.G + "," + expectB.B);
                }
                Set(form, "reminding", false);
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        // ---- 3. 午休 / 晚休到点触发提醒动画 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.reminderEffect = "none";   // 取色断言只看色块本身，不要氛围层
            s.mode = "fixed";
            s.startTime = "09:00";
            s.endTime = "18:00";
            s.workdays = new bool[] { true, true, true, true, true, true, true };
            s.exceptions = new System.Collections.Generic.List<HolidayEntry>();
            s.lunchStart = "11:50";
            s.lunchEnd = "13:30";
            s.eveningEnabled = true;
            s.eveningStart = "18:00";
            s.eveningEnd = "18:45";
            s.breakNotify = true;
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "reminding", false);
                object moved = Call(form, "CheckBreaks", DateTime.Today.AddHours(11).AddMinutes(50).AddSeconds(20));
                var ev = (ReminderEvent)Get(form, "remindEvent");
                Ok("午休开始触发", (bool)moved && ev != null && ev.title == "午休时间到" && ev.source == "break",
                    "title=" + (ev == null ? "null" : ev.title) + " source=" + (ev == null ? "" : ev.source));

                Set(form, "reminding", false);
                object again = Call(form, "CheckBreaks", DateTime.Today.AddHours(11).AddMinutes(50).AddSeconds(40));
                Ok("同一天不重复响", !(bool)again, "再次检查 => " + again);

                Set(form, "reminding", false);
                object early = Call(form, "CheckBreaks", DateTime.Today.AddHours(11).AddMinutes(49));
                Ok("未到点不响", !(bool)early, "11:49 => " + early);

                Set(form, "reminding", false);
                object lunchEnd = Call(form, "CheckBreaks", DateTime.Today.AddHours(13).AddMinutes(30));
                var evEnd = (ReminderEvent)Get(form, "remindEvent");
                Ok("午休结束触发", (bool)lunchEnd && evEnd != null && evEnd.title == "午休结束",
                    "title=" + (evEnd == null ? "null" : evEnd.title));

                Set(form, "reminding", false);
                object evening = Call(form, "CheckBreaks", DateTime.Today.AddHours(18));
                var evEve = (ReminderEvent)Get(form, "remindEvent");
                Ok("晚休开始触发", (bool)evening && evEve != null && evEve.title == "晚休时间到",
                    "title=" + (evEve == null ? "null" : evEve.title));

                // 关掉晚休后不应再响（现在真相在固定日程里）
                s.eveningEnabled = false;
                s.ScheduleOf("evening").enabled = false;
                Set(form, "settings", s);
                Set(form, "reminding", false);
                object noEvening = Call(form, "CheckBreaks", DateTime.Today.AddHours(18).AddMinutes(45));
                Ok("关掉晚休后不响", !(bool)noEvening, "18:45 => " + noEvening);

                // 自己加的固定日程（非午休/晚休）也要能响，并且用这条自己的名字和颜色
                var extra = FixedSchedule.Make("my-standup", "custom", "站会", "10:30", "", "lan-yuming");
                extra.repeat = "daily";
                extra.styleId = "zi-jiegeng";
                s.schedules.Add(extra);
                Set(form, "settings", s);
                Set(form, "reminding", false);
                Set(form, "remindEvent", null);
                object custom = Call(form, "CheckBreaks", DateTime.Today.AddHours(10).AddMinutes(30).AddSeconds(5));
                var evCustom = (ReminderEvent)Get(form, "remindEvent");
                Ok("自建固定日程也会响", (bool)custom && evCustom != null && evCustom.title == "站会时间到"
                    && evCustom.source == "break" && evCustom.id.StartsWith("fix:my-standup"),
                    "title=" + (evCustom == null ? "null" : evCustom.title) + " id=" + (evCustom == null ? "-" : evCustom.id));
                var schedOf = (FixedSchedule)Call(form, "FixedOf", evCustom);
                Ok("提醒带的是这条日程的配色", schedOf != null && schedOf.styleId == "zi-jiegeng",
                    schedOf == null ? "找不到日程" : schedOf.styleId);

                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        // ---- 4. 日程时间输入：快捷按钮 + 自动算结束 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            using (var dlg = new SettingsForm(new SettingsStore(), s))
            {
                Call(dlg, "QuickEvent", -1, 9 * 60 + 30);
                // out 参数不好走反射，直接读控件确认结果
                var dp = (DateTimePicker)Get(dlg, "dpEvent");
                var cmbTime = (ComboBox)Get(dlg, "cmbEventTime");
                var cmbDur = (ComboBox)Get(dlg, "cmbEventDur");
                var lbl = (Label)Get(dlg, "lblEventEnd");
                Ok("明天 09:30 快捷填入",
                    dp.Value.Date == DateTime.Today.AddDays(1) && cmbTime.Text == "09:30",
                    "日期=" + dp.Value.ToString("yyyy-MM-dd") + " 时间=" + cmbTime.Text);

                cmbDur.SelectedIndex = 3;   // 60 分钟
                Ok("结束时间自动推算", lbl.Text == "结束 10:30", "标签=" + lbl.Text);

                cmbTime.Text = "23:50";
                cmbDur.SelectedIndex = 6;   // 180 分钟 -> 跨天
                Ok("跨天也能算", lbl.Text == "结束 02:50", "标签=" + lbl.Text);

                Call(dlg, "QuickEvent", 30, -1);
                DateTime soon = DateTime.Now.AddMinutes(29);
                Ok("30 分钟后快捷填入", dp.Value.Date == soon.Date && cmbTime.Text == DateTime.Now.AddMinutes(30).ToString("HH:mm"),
                    "日期=" + dp.Value.ToString("yyyy-MM-dd") + " 时间=" + cmbTime.Text);

                // 五个快速选项：标签、Tag 和点击后算出来的时间
                {
                    var chips = new System.Collections.Generic.List<Button>();
                    CollectQuickChips(dlg, chips);
                    bool labelsOk = chips.Count == 5;
                    if (labelsOk)
                    {
                        string[] wants = new string[] { "3 分钟后", "5 分钟后", "15 分钟后", "30 分钟后", "1 小时后" };
                        int[] tags = new int[] { 3, 5, 15, 30, 60 };
                        for (int qi = 0; qi < chips.Count; qi++)
                        {
                            if (chips[qi].Text != wants[qi] || (int)chips[qi].Tag != tags[qi]) labelsOk = false;
                        }
                    }
                    Ok("五个快速选项就位", labelsOk, chips.Count + " 个");
                    bool allMoved = true;
                    foreach (var chip in chips)
                    {
                        cmbTime.Text = "00:05";
                        dp.Value = new DateTime(2026, 1, 1);
                        Call(dlg, "QuickEvent", (int)chip.Tag, -1);
                        DateTime want = DateTime.Now.AddMinutes((int)chip.Tag);
                        if (dp.Value.Date != want.Date || cmbTime.Text != want.ToString("HH:mm")) allMoved = false;
                    }
                    Ok("每个快速选项都把时间挪到对的地方", allMoved, "");
                }

                var tabs = (TabControl)Get(dlg, "tabs");
                foreach (TabPage page in tabs.TabPages) LayoutCheck(page, page.Text);
            }
        }

        // ---- 5. 四类提醒配色互不相同，且每类双层色块 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.reminderEffect = "none";   // 取色断言只看色块本身，不要氛围层
            s.ui.theme = "lv-kongque";
            var kinds = new string[] { "meeting", "lunch", "evening", "event" };
            var bandA = new Color[kinds.Length];
            var bandB = new Color[kinds.Length];
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 273);
                Call(form, "Recompute");
                for (int i = 0; i < kinds.Length; i++)
                {
                    var e = new ReminderEvent();
                    e.title = "产品需求评审与排期";
                    e.detail = "确认交互方案";
                    e.place = "12F · 会议室 A";
                    e.start = Reminders.FormatStart(DateTime.Now.AddMinutes(5));
                    e.end = Reminders.FormatStart(DateTime.Now.AddMinutes(35));
                    if (kinds[i] == "meeting") { e.source = "lark"; e.id = "lark:1"; }
                    else if (kinds[i] == "lunch") { e.source = "break"; e.id = "fix:lunch"; }
                    else if (kinds[i] == "evening") { e.source = "break"; e.id = "fix:evening"; }
                    else { e.source = "local"; e.id = "local:1"; }
                    Set(form, "remindEvent", e);
                    Set(form, "reminding", true);
                    Set(form, "remindAt", DateTime.Now.AddMilliseconds(-2000));
                    using (var bmp = new Bitmap(420, 273))
                    {
                        bmp.SetResolution(144, 144);
                        using (var g = Graphics.FromImage(bmp))
                            Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 273)));
                        bandA[i] = bmp.GetPixel(300, 40);    // 分界线之上
                        bandB[i] = bmp.GetPixel(300, 262);   // 分界线之下
                        string outDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
                        System.IO.Directory.CreateDirectory(outDir);
                        bmp.Save(System.IO.Path.Combine(outDir, "reminder-kind-" + kinds[i] + ".png"));
                    }
                    Ok("双层色块 " + kinds[i],
                        Distance(bandA[i], bandB[i]) > 25,
                        "A=" + fmt(bandA[i]) + " B=" + fmt(bandB[i]));
                    // 文字压在色块上要看得清（会议橙黄用深色字，休息/日程用各自预设的墨色）
                    {
                        Color ink;
                        if (kinds[i] == "meeting")
                        {
                            Color ma = Color.FromArgb(244, 145, 53), mb2 = Color.FromArgb(255, 205, 105);
                            ink = Color.FromArgb(52, 30, 15);
                            ReminderStyles.FitToTheme(Themes.Get(s.ui.theme), ref ma, ref mb2, ref ink);
                        }
                        else ink = Color.FromArgb(52, 30, 15);
                        if (kinds[i] != "meeting")
                        {
                            string sid = kinds[i] == "lunch" ? s.ui.lunchReminderStyle
                                : kinds[i] == "evening" ? s.ui.eveningReminderStyle : "theme";
                            Color ia, ib;
                            ReminderStyles.Resolve(sid, Themes.Get(s.ui.theme), out ia, out ib, out ink);
                        }
                        double cA = Contrast(ink, bandA[i]);
                        double cB = Contrast(ink, bandB[i]);
                        Ok("文字对比度 " + kinds[i], cA >= 4.5 && cB >= 4.5,
                            "对比 " + cA.ToString("0.0") + ":1 / " + cB.ToString("0.0") + ":1");
                    }
                }
            }
            bool distinct = true;
            for (int i = 0; i < kinds.Length; i++)
                for (int j = i + 1; j < kinds.Length; j++)
                {
                    int d = Distance(bandA[i], bandA[j]);
                    if (d <= 40) { distinct = false; Ok("配色区分 " + kinds[i] + "/" + kinds[j], false, "距离 " + d); }
                }
            Ok("四类提醒配色可区分", distinct, "会议橙黄 / 午休青绿 / 晚休靛蓝 / 日程主题色");
        }

        // ---- 5b. 午休/晚休/下班的配色从已有主题里挑 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.reminderEffect = "none";   // 取色断言只看色块本身，不要氛围层
            Ok("默认配色取自主题（玉茗/桔梗/松林绿）",
                s.ui.lunchReminderStyle == "lan-yuming" && s.ui.eveningReminderStyle == "zi-jiegeng"
                && s.ui.offReminderStyle == "forest",
                s.ui.lunchReminderStyle + " / " + s.ui.eveningReminderStyle + " / " + s.ui.offReminderStyle);

            // 选项 = 跟随当前主题 + 全部主题，一个都不多
            var ids = ReminderStyles.Ids;
            bool allTheme = ids.Length == Themes.All.Length + 1 && ids[0] == ReminderStyles.Follow;
            foreach (var t in Themes.All)
            {
                bool found = false;
                foreach (var id in ids) if (id == t.id) found = true;
                if (!found) allTheme = false;
            }
            Ok("下拉里就是 39 套主题 + 跟随当前主题", allTheme, ids.Length + " 项");

            // 每一套主题折算出来都得能看清、两块得能分开
            Color badA = Color.Empty, badB = Color.Empty, badInk = Color.Empty;
            double worstContrast = 99, worstGap = 999;
            string worstName = "-";
            foreach (var id in ids)
            {
                Color a2, b2, ink2;
                var probe = Themes.Get("midnight");
                ReminderStyles.Resolve(id, probe, out a2, out b2, out ink2);
                double c1 = Contrast(ink2, a2), c2 = Contrast(ink2, b2);
                double gap = Distance(a2, b2);
                if (Math.Min(c1, c2) < worstContrast) { worstContrast = Math.Min(c1, c2); worstName = ReminderStyles.NameOf(id); }
                if (gap < worstGap) worstGap = gap;
                if (Math.Min(c1, c2) < 4.5) { badA = a2; badB = b2; badInk = ink2; }
            }
            Ok("40 套折算出来文字都够清楚", badA == Color.Empty, "最低 " + worstContrast.ToString("0.0") + ":1（" + worstName + "）");
            Ok("40 套的两块都分得开", worstGap > 25, "最小色距 " + worstGap.ToString("0"));

            Color a, b, ink;
            ReminderStyles.Resolve("huang-hupo", Themes.Get(s.ui.theme), out a, out b, out ink);
            Color hupoAccent = Themes.Get("huang-hupo").accent;
            Ok("黄·琥珀能折成卡片色", a.R > a.B + 15 && Contrast(ink, a) >= 4.5 && Distance(a, Themes.Get(s.ui.theme).bgTop) > 24,
                "主色 " + fmt(hupoAccent) + " -> " + fmt(a) + " / " + fmt(b));

            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 273);
                Call(form, "Recompute");

                // 午休挑黄·琥珀、下班挑蓝·藏青，看画出来的像素是不是那套主题
                s.ScheduleOf("lunch").styleId = "huang-hupo";
                s.ui.reminderEffect = "none";
                s.ScheduleOf("offwork").styleId = "lan-zangqing";
                Call(form, "Recompute");
                Color expectLunchA, expectLunchB, expectInk;
                ReminderStyles.Resolve("huang-hupo", Themes.Get(s.ui.theme), out expectLunchA, out expectLunchB, out expectInk);
                var lunch = new ReminderEvent();
                lunch.id = "fix:lunch"; lunch.source = "break"; lunch.title = "午休时间到";
                lunch.start = Reminders.FormatStart(DateTime.Now.AddMinutes(-1));
                Set(form, "remindEvent", lunch);
                Set(form, "reminding", true);
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-2000));
                Color lunchBand;
                using (var bmp = new Bitmap(420, 273))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 273)));
                    lunchBand = bmp.GetPixel(300, 40);
                }
                Ok("午休换成 黄·琥珀", Distance(lunchBand, expectLunchA) <= 6,
                    fmt(lunchBand) + " 期望 " + fmt(expectLunchA));

                Color expectAlertA, expectAlertB, expectAlertInk;
                ReminderStyles.ResolveNoFit("lan-zangqing", Themes.Get(s.ui.theme), out expectAlertA, out expectAlertB, out expectAlertInk);
                var alert = new ReminderEvent();
                alert.id = "alert:offwork"; alert.source = "alert"; alert.title = "该下班啦";
                alert.detail = "在飞书发「2」打卡后自动消失";
                alert.start = Reminders.FormatStart(DateTime.Now);
                Set(form, "remindEvent", alert);
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-2000));
                Color alertBand;
                using (var bmp = new Bitmap(420, 273))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 273)));
                    alertBand = bmp.GetPixel(300, 40);
                }
                Ok("下班换成 蓝·藏青", Distance(alertBand, expectAlertA) <= 6,
                    fmt(alertBand) + " 期望 " + fmt(expectAlertA));

                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }

            // 存了非法 id 时回落到默认，不至于画成黑色
            var bad = new Settings();
            bad.ui.lunchReminderStyle = "no-such-style";
            bad.ui.offReminderStyle = null;
            bad.ui.eveningReminderStyle = "";
            bad.Normalize();
            Ok("非法配色 id 回落默认",
                bad.ui.lunchReminderStyle == "lan-yuming" && bad.ui.eveningReminderStyle == "zi-jiegeng"
                && bad.ui.offReminderStyle == "forest",
                bad.ui.lunchReminderStyle + " / " + bad.ui.eveningReminderStyle + " / " + bad.ui.offReminderStyle);

            // 存盘再读回来，三个配色不会丢
            var pick = new Settings();
            pick.ui.lunchReminderStyle = "hong-fengye";
            pick.ui.eveningReminderStyle = "sand";
            pick.ui.offReminderStyle = "lan-zangqing";
            pick.ui.eveningReminderStyle = "bai-zhenzhu";
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            var back = ser.Deserialize<Settings>(ser.Serialize(pick));
            back.Normalize();
            Ok("配色设置能存能读",
                back.ui.lunchReminderStyle == "hong-fengye" && back.ui.offReminderStyle == "lan-zangqing"
                && back.ui.eveningReminderStyle == "bai-zhenzhu",
                back.ui.lunchReminderStyle + " / " + back.ui.eveningReminderStyle + " / " + back.ui.offReminderStyle);
        }

        // ---- 6. 展开卡片更紧凑，且提醒分界线落在两行之间 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.reminderEffect = "none";   // 取色断言只看色块本身，不要氛围层
            s.ui.showLunch = true;
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                Call(form, "Recompute");
                // 卡片高度由「行高之和 + 行距」算出来；行距只有一个值，所以每行之间必然等距
                int gap = (int)typeof(WidgetForm).GetField("RowGapDip", F).GetValue(null);
                int big = (int)typeof(WidgetForm).GetField("BigDip", F).GetValue(null);
                int hLunch = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
                int wantLunch = (int)Call(form, "S", 12f + (20 + 18 + big + 4 + 18 + 18 + 16) + gap * 6 + 12f);
                s.ui.showLunch = false;
                Set(form, "settings", s);
                Call(form, "Recompute");
                int hPlain = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
                int wantPlain = (int)Call(form, "S", 12f + (20 + 18 + big + 4 + 18 + 16) + gap * 5 + 12f);
                Ok("卡片高度按行距算", hLunch == wantLunch && hPlain == wantPlain,
                    "含午休 " + hLunch + "/" + wantLunch + "px，不含 " + hPlain + "/" + wantPlain + "px（行距 " + gap + " / 大号行 " + big + " DIP）");
                form.Size = new Size(420, hPlain);
                // 分界线 64 DIP：标题行 30~62，时间行 64~86，理应压在行间
                int seam = (int)Call(form, "S", 64f);
                Ok("分界线在行间", seam == 96 && seam > (int)Call(form, "S", 62f) && seam <= (int)Call(form, "S", 64f),
                    "seam=" + seam + "px，标题行止于 " + (int)Call(form, "S", 62f) + "px");
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        // ---- 7. 常驻「该下班啦」：到点触发、每 5 分钟重播、打卡后收起 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.reminderEffect = "none";   // 取色断言只看色块本身，不要氛围层
            s.ui.theme = "midnight";
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 273);
                Call(form, "Recompute");

                var st = (CardStatus)Get(form, "status");
                st.offAlert = true;
                st.targetIso = DateTime.Today.ToString("yyyy-MM-dd") + "T20:04";
                Call(form, "CheckOffWork");

                bool reminding = (bool)Get(form, "reminding");
                var ev = (ReminderEvent)Get(form, "remindEvent");
                Ok("到点触发常驻提醒", reminding && ev != null && ev.source == "alert",
                    "reminding=" + reminding + " source=" + (ev == null ? "-" : ev.source));
                string kindText = WidgetForm.KindLabel(ev, false, null);
                Ok("第一行 注意", kindText == "注意", kindText);
                string titleText = ev == null ? "-" : ev.title;
                Ok("第二行 该下班啦", titleText == "该下班啦", titleText);

                // 色块
                DateTime stamp = DateTime.Now.AddMilliseconds(-2000);
                Set(form, "remindAt", stamp);
                using (var bmp = new Bitmap(420, 273))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 273)));
                    Color a = bmp.GetPixel(300, 40), b = bmp.GetPixel(300, 262);
                    Color expA, expB, expInk;
                    ReminderStyles.ResolveNoFit(s.ScheduleOf("offwork").styleId, Themes.Get(s.ui.theme), out expA, out expB, out expInk);
                    Ok("下班提醒是绿色系", a.G > a.R + 40 && a.G > a.B + 20,
                        "A=" + fmt(a) + " B=" + fmt(b));
                    Ok("不用红色", a.R < 120 && b.R < 200, "红分量 " + a.R + " / " + b.R);
                    Ok("色块和卡面明显不同", Distance(a, b) >= 60, "色距 " + Distance(a, b));
                    double ca = Contrast(expInk, a);
                    double cb = Contrast(expInk, b);
                    Ok("深绿字看得清", ca >= 4.5 && cb >= 4.5,
                        ca.ToString("0.0") + ":1 / " + cb.ToString("0.0") + ":1");
                    Ok("全天候都在（不随时间退回卡片）",
                    Distance(a, expA) <= 6, "t=2s 仍是 " + fmt(a));
                    string vdir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
                    System.IO.Directory.CreateDirectory(vdir);
                    bmp.Save(System.IO.Path.Combine(vdir, "reminder-kind-alert.png"));
                }

                // 右上角的 × 画出来了，点它能把这条提醒关掉
                var box = (RectangleF)Get(form, "rectDismiss");
                Ok("提醒上有关闭按钮", box.Width > 20 && box.Right <= 420 && box.Top < 40,
                    "x=" + (int)box.X + " y=" + (int)box.Y + " " + (int)box.Width + "px");

                // 顺带把「距晚休」那一行的样子存下来
                st.lunch = LunchState.EveningBefore;
                st.lunchLabel = "距晚休";
                st.lunchValue = "45m";
                st.lunchCounting = true;
                st.headline = "工作中";
                st.bigLabel = "距离下班";
                st.big = "02:45:00";
                st.sub = "上班 09:34 · 预计下班 20:04";
                st.extra = "已工作 7h41m · 应勤 10h30m";
                st.progress = 0.73;
                st.offAlert = false;
                Set(form, "remindEvent", null);
                Set(form, "reminding", false);
                using (var bmp = new Bitmap(420, 273))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 273)));
                    string vdir2 = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
                    bmp.Save(System.IO.Path.Combine(vdir2, "lunch-evening-row.png"));
                }

                // 下一次重播安排在 5 分钟后
                var next = (DateTime)Get(form, "offAlertNext");
                double minutes = (next - DateTime.Now).TotalMinutes;
                Ok("下一次重播在 5 分钟内", minutes > 4.5 && minutes <= 5.1, minutes.ToString("0.0") + " 分钟");

                // 打卡后收起
                st.offAlert = false;
                Call(form, "CheckOffWork");
                Ok("打卡后收起常驻提醒",
                    !(bool)Get(form, "reminding") && Get(form, "remindEvent") == null, "reminding=False");

                // 手动「忽略今天」也能收掉，且第二天自动恢复
                st.offAlert = true;
                Call(form, "CheckOffWork");
                Call(form, "MuteOffAlert");
                bool muted = !(bool)Get(form, "reminding");
                Call(form, "CheckOffWork");
                Ok("忽略今天后不再弹", muted && !(bool)Get(form, "reminding"), "reminding=False");
                Set(form, "offAlertMutedDay", DateTime.Today.AddDays(-1));
                Call(form, "CheckOffWork");
                Ok("第二天自动恢复", (bool)Get(form, "reminding"), "reminding=True");
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        // ---- 8. 所有提醒常驻到时段结束 + × 手动关闭 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.reminderEffect = "none";   // 取色断言只看色块本身，不要氛围层
            s.ui.reminderHoldSeconds = 6;
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 273);
                Call(form, "Recompute");

                DateTime now = DateTime.Now;

                // 会议：还有 50 分钟结束，到点提醒后应当撑到结束，而不是 6 秒
                var meet = new ReminderEvent();
                meet.id = "lark:1"; meet.source = "lark"; meet.title = "需求评审";
                meet.start = Reminders.FormatStart(now.AddMinutes(-10));
                meet.end = Reminders.FormatStart(now.AddMinutes(50));
                meet.place = "12F";
                Call(form, "StartReminder", meet);
                var until = (DateTime)Get(form, "remindUntil");
                double keepMinutes = (until - DateTime.Now).TotalMinutes;
                Ok("会议常驻到结束时间", keepMinutes > 48 && keepMinutes <= 51,
                    "常驻 " + keepMinutes.ToString("0") + " 分钟（到 " + until.ToString("HH:mm") + "）");
                Set(form, "remindAt", now.AddSeconds(-30));       // 已经播完入场 30 秒
                Call(form, "StepReminder");
                Ok("30 秒后还在（不再 6 秒就走）", (bool)Get(form, "reminding"), "reminding=True");

                // 时段过了才退场
                Set(form, "remindUntil", now.AddSeconds(-1));
                Call(form, "CheckSticky");
                Ok("时段结束开始退场", (bool)Get(form, "remindExiting") && (bool)Get(form, "reminding"), "exiting=True");
                Set(form, "remindExitAt", now.AddSeconds(-3));
                Call(form, "StepReminder");
                Ok("退场动画放完就收起", !(bool)Get(form, "reminding"), "reminding=False");

                // 午休：还有 55 分钟结束，常驻到那一刻
                var lunch = new ReminderEvent();
                lunch.id = "fix:lunch"; lunch.source = "break"; lunch.title = "午休时间到";
                lunch.start = Reminders.FormatStart(now.AddMinutes(-5));
                lunch.end = Reminders.FormatStart(now.AddMinutes(55));
                Call(form, "StartReminder", lunch);
                var lunchKeep = ((DateTime)Get(form, "remindUntil") - DateTime.Now).TotalMinutes;
                Ok("午休常驻到休息结束", lunchKeep > 53 && lunchKeep <= 56,
                    "常驻 " + lunchKeep.ToString("0") + " 分钟");

                // 午休「结束」那一刻的提醒没有结束时间：只按最短停留
                var lunchEnd = new ReminderEvent();
                lunchEnd.id = "fix:lunch:end"; lunchEnd.source = "break"; lunchEnd.title = "午休结束";
                lunchEnd.start = Reminders.FormatStart(now.Date.AddHours(13).AddMinutes(30));
                Call(form, "StartReminder", lunchEnd);
                var endUntil = (DateTime)Get(form, "remindUntil");
                Ok("休息结束提醒只停最短时间", (endUntil - DateTime.Now).TotalSeconds <= 7,
                    Math.Round((endUntil - DateTime.Now).TotalSeconds) + " 秒");

                // 日程没有结束时间：给 30 分钟
                var noEnd = new ReminderEvent();
                noEnd.id = "local:9"; noEnd.source = "local"; noEnd.title = "取快递";
                noEnd.start = Reminders.FormatStart(now);
                Call(form, "StartReminder", noEnd);
                var half = ((DateTime)Get(form, "remindUntil") - DateTime.Now).TotalMinutes;
                Ok("没有结束时间的日程给 30 分钟", half > 29 && half <= 30, half.ToString("0") + " 分钟");

                // × 关掉：立刻进退场，并且今天不再弹同一条
                Call(form, "OnPaint", new PaintEventArgs(Graphics.FromImage(new Bitmap(420, 273)), new Rectangle(0, 0, 420, 273)));
                var hitBox = (RectangleF)Get(form, "rectDismiss");
                int hx = (int)(hitBox.X + hitBox.Width / 2), hy = (int)(hitBox.Y + hitBox.Height / 2);
                Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, hx, hy, 0));
                Ok("点 × 真的能关", (bool)Get(form, "remindExiting"), "点击 (" + hx + "," + hy + ") → exiting=True");
                Set(form, "remindExitAt", now.AddSeconds(-3));
                Call(form, "StepReminder");
                Ok("关掉后回到普通卡片", !(bool)Get(form, "reminding") && Get(form, "remindEvent") == null, "reminding=False");

                var notified = (System.Collections.Generic.IEnumerable<string>)Get(form, "notified");
                bool recorded = false;
                foreach (var k in notified) if (k.StartsWith("local:9@")) recorded = true;
                Ok("关掉后今天不再重复弹", recorded, "已记入 notified");

                // 中途启动（比如刚开机）正处在午休里：直接立起「午休中」的常驻卡片
                var s2 = new Settings();
                s2.lark.enabled = false;
                s2.ui.edge = "free";
                s2.breakNotify = true;
                s2.lunchStart = DateTime.Now.AddMinutes(-30).ToString("HH:mm");
                s2.lunchEnd = DateTime.Now.AddMinutes(30).ToString("HH:mm");
                s2.eveningEnabled = false;
                Set(form, "settings", s2);
                bool fired = (bool)Call(form, "CheckBreaks", DateTime.Now);
                var rev = (ReminderEvent)Get(form, "remindEvent");
                Ok("休息中途启动也有常驻卡片", fired && rev != null && rev.title == "午休中",
                    rev == null ? "没触发" : rev.title);
                bool again = (bool)Call(form, "CheckBreaks", DateTime.Now);
                Ok("同一天不会重复立卡片", !again, "第二次 " + again);
                Set(form, "reminding", false);
                Set(form, "remindEvent", null);
                Set(form, "settings", s);

                // 下班提醒的 × = 忽略今天
                var st2 = (CardStatus)Get(form, "status");
                st2.offAlert = true;
                st2.targetIso = now.Date.ToString("yyyy-MM-dd") + "T20:04";
                Call(form, "CheckOffWork");
                Call(form, "DismissReminder");
                Ok("下班提醒 × = 忽略今天",
                    !(bool)Get(form, "reminding") && (DateTime)Get(form, "offAlertMutedDay") == DateTime.Today,
                    "muted=" + ((DateTime)Get(form, "offAlertMutedDay")).ToString("MM-dd"));

                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        // ---- 9. 固定日程：重复规则 / 重复至 / 自定义主题存取 / 「位置」文案 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.NormalizeSchedules();

            var lunch = s.ScheduleOf("lunch");
            var evening = s.ScheduleOf("evening");
            var off = s.ScheduleOf("offwork");
            Ok("内置三条固定日程", lunch != null && evening != null && off != null,
                (lunch == null ? "-" : lunch.name) + " / " + (evening == null ? "-" : evening.name)
                + " / " + (off == null ? "-" : off.name));
            Ok("下班这条不填时间、按班次算", off != null && off.IsOffWork && off.start.Length == 0,
                "start=空 role=" + (off == null ? "-" : off.role));

            // 单双周：同一个周一下周必然换一边
            DateTime monday = new DateTime(2026, 1, 5);
            var odd = FixedSchedule.Make("t-odd", "custom", "单周会", "10:00", "", "theme");
            var even = FixedSchedule.Make("t-even", "custom", "双周会", "10:00", "", "theme");
            var daily = FixedSchedule.Make("t-daily", "custom", "每天都开", "10:00", "", "theme");
            s.schedules.Add(odd);
            s.schedules.Add(even);
            s.schedules.Add(daily);
            odd.repeat = "odd";
            even.repeat = "even";
            bool oddNow = odd.OccursOn(monday), oddNext = odd.OccursOn(monday.AddDays(7));
            Ok("单数周隔一周响一次", oddNow != oddNext, monday.ToString("yyyy-MM-dd") + "=" + oddNow + " 下周=" + oddNext);
            Ok("双数周正好反过来", even.OccursOn(monday) == oddNext && even.OccursOn(monday.AddDays(7)) == oddNow,
                "周一=" + even.OccursOn(monday) + " 下周=" + even.OccursOn(monday.AddDays(7)));
            Ok("每天两天都响", daily.OccursOn(monday) && daily.OccursOn(monday.AddDays(7)), "两天都 True");

            odd.until = monday.ToString("yyyy-MM-dd");
            Ok("重复至当天照常响", odd.OccursOn(monday) == oddNow, "含当天");
            Ok("过了重复至就停", !odd.OccursOn(monday.AddDays(14)) && !odd.OccursOn(monday.AddDays(21)),
                "两周后 False");
            Ok("重复标签带截止日", odd.RepeatLabel.Contains("单数周") && odd.RepeatLabel.Contains("至"),
                odd.RepeatLabel);

            var at = odd.StartAt(monday);
            Ok("开始时间贴到当天", at == monday.Date.AddHours(10), at.ToString("yyyy-MM-dd HH:mm"));

            // 午休扣工时要同步回旧字段，Policy 还是看旧字段
            lunch.deduct = true;
            s.NormalizeSchedules();
            Ok("午休扣工时同步给班次计算", s.lunchDeduct, "lunchDeduct=" + s.lunchDeduct);

            // 自定义主题：选完存进设置，再导出导入一轮不能丢
            s.customThemes.Add(new CustomTheme { id = "my-test", name = "苔绿", accent = "#6F9E6A" });
            Themes.SetCustom(s.customThemes);
            var mine = Themes.Get("my-test");
            Ok("自定义主题能取到", Themes.IsCustom("my-test") && mine.name == "苔绿",
                mine.name + " " + mine.accent.R + "," + mine.accent.G + "," + mine.accent.B);

            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc-check-" + Guid.NewGuid().ToString("N") + ".json");
            var store = new SettingsStore();
            bool exported = store.Export(s, tmp);
            var back = exported ? store.Import(tmp) : null;
            Ok("固定日程和自定义主题能存能读",
                back != null && back.schedules != null && back.schedules.Count >= 4
                && back.customThemes != null && back.customThemes.Count == 1
                && back.customThemes[0].accent == "#6F9E6A",
                back == null ? "读不出来" : ("日程 " + back.schedules.Count + " 条，主题 " + back.customThemes.Count + " 个"));
            try { System.IO.File.Delete(tmp); } catch { }

            // 设置窗口：改一条再存回去，不能把改动丢了
            using (var dlg = new SettingsForm(new SettingsStore(), s))
            {
                var list = (ScheduleList)Get(dlg, "listSched");
                Ok("列表里能看到全部固定日程", list != null && s.schedules.Count >= 4, s.schedules.Count + " 条");

                int index = -1;
                for (int i = 0; i < s.schedules.Count; i++) if (s.schedules[i].id == "t-odd") index = i;
                Call(dlg, "SelectSched", index);
                ((TextBox)Get(dlg, "tbSchedStart")).Text = "10:20";
                ((TextBox)Get(dlg, "tbSchedName")).Text = "单周站会";
                Call(dlg, "StoreSchedEditor");
                Ok("编辑器改动写回日程", s.schedules[index].start == "10:20" && s.schedules[index].name == "单周站会",
                    s.schedules[index].name + " " + s.schedules[index].start);

                var cmbRepeat = (ComboBox)Get(dlg, "cmbRepeat");
                Ok("重复下拉是每天/单数周/双数周",
                    cmbRepeat.Items.Count == 3 && cmbRepeat.Items[1].ToString() == "单数周",
                    string.Join("/", new string[] { cmbRepeat.Items[0].ToString(), cmbRepeat.Items[1].ToString(), cmbRepeat.Items[2].ToString() }));

                var tabs = (TabControl)Get(dlg, "tabs");
                Ok("四个页签", tabs.TabPages.Count == 4,
                    tabs.TabPages[0].Text + "/" + tabs.TabPages[1].Text + "/" + tabs.TabPages[2].Text + "/" + tabs.TabPages[3].Text);

                // 「会议室」改名「位置」
                bool hasPlace = false, hasRoom = false;
                foreach (Control c in tabs.TabPages[2].Controls)
                    foreach (Control ch in c.Controls)
                        if (ch is Label)
                        {
                            if (ch.Text == "位置") hasPlace = true;
                            if (ch.Text == "会议室") hasRoom = true;
                        }
                Ok("日程字段写「位置」", hasPlace && !hasRoom, "位置=" + hasPlace + " 会议室=" + hasRoom);
            }
        }

        // ---- 10. 自建主题进列表 + 小卡片点开放大 ----
        {
            Themes.SetCustom(new System.Collections.Generic.List<CustomTheme>());   // 先清掉前面用例留下的
            int builtinThemes = Themes.All.Length;
            var s = new Settings();
            s.lark.enabled = false;
            s.customThemes.Add(new CustomTheme { id = "my-moss", name = "苔绿", accent = "#6F9E6A" });
            Themes.SetCustom(s.customThemes);
            Ok("自建主题进了色卡", Themes.All.Length == builtinThemes + 1 && Themes.Get("my-moss").name == "苔绿",
                "色卡 " + builtinThemes + " -> " + Themes.All.Length + " 格");
            int at = Array.IndexOf(ReminderStyles.Ids, "my-moss");
            Ok("自建主题也在提醒配色下拉里", at >= 0 && ReminderStyles.ShortNameOf("my-moss").Length > 0,
                "位置 " + at + " / 共 " + ReminderStyles.Ids.Length + " 项");

            var picker = new ThemePicker();
            picker.SetBounds(0, 0, 456, 122);
            picker.SelectedId = "my-moss";
            var chip = (Rectangle)Call(picker, "ChipRect", Themes.All.Length - 1);
            Ok("最后一格色卡画在框内", chip.Width > 8 && chip.Height > 8 && chip.Bottom <= picker.Height,
                "格子 " + chip.Width + "x" + chip.Height + " 底部 " + chip.Bottom + "/" + picker.Height);

            var e1 = new ReminderEvent();
            e1.id = "local:a"; e1.source = "local"; e1.title = "取快递";
            e1.place = "1F 大厅";
            e1.start = Reminders.FormatStart(DateTime.Today.AddHours(18).AddMinutes(10));
            e1.end = Reminders.FormatStart(DateTime.Today.AddHours(18).AddMinutes(25));
            var e2 = new ReminderEvent();
            e2.id = "local:b"; e2.source = "local"; e2.title = "提交周报";
            e2.start = Reminders.FormatStart(DateTime.Today.AddDays(1).AddHours(17));
            var cards = new EventCardList();
            cards.SetBounds(0, 0, 232, 236);
            cards.Items = new System.Collections.Generic.List<ReminderEvent>(new ReminderEvent[] { e1, e2 });
            var down = typeof(EventCardList).GetMethod("OnMouseDown", F);
            down.Invoke(cards, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0) });
            Ok("日程小卡片点一下放大", (int)Get(cards, "expanded") == 0 && (int)Get(cards, "selected") == 0,
                "expanded=" + Get(cards, "expanded") + " selected=" + Get(cards, "selected"));
            down.Invoke(cards, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0) });
            Ok("再点一下收起", (int)Get(cards, "expanded") == -1, "expanded=" + Get(cards, "expanded"));

            var sc = new Settings();
            sc.NormalizeSchedules();
            var rows = new ScheduleList();
            rows.SetBounds(0, 0, 466, 150);
            rows.Items = sc.schedules;
            bool raised = false;
            rows.SelectedIndexChanged += delegate { raised = true; };
            var rowDown = typeof(ScheduleList).GetMethod("OnMouseDown", F);
            rowDown.Invoke(rows, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0) });
            Ok("固定日程列表点第一条", raised && rows.SelectedIndex == 0, "selected=" + rows.SelectedIndex);
        }

        // ---- 11. 固定日程列表：超过三条也能看全，新建不会顶掉旧的 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.NormalizeSchedules();
            for (int i = 0; i < 4; i++)
                s.schedules.Add(FixedSchedule.Make("more-" + i, "custom", "自建" + (i + 1), "10:0" + i, "", "theme"));

            using (var dlg = new SettingsForm(new SettingsStore(), s))
            {
                var list = (ScheduleList)Get(dlg, "listSched");
                Ok("七条固定日程一条不少", list.Items.Count == 7, list.Items.Count + " 条");

                int last = list.Items.Count - 1;
                Ok("新加的排在最后，不顶掉内置三条",
                    list.Items[0].role == "lunch" && list.Items[last].id == "more-3",
                    list.Items[0].name + " … " + list.Items[last].name);
                Ok("内容超出一屏时会滚动", list.MaxScroll > 0,
                    "可滚 " + list.MaxScroll + "px / 控件高 " + list.Height + "px");

                var r0 = (Rectangle)Call(list, "RowRect", 0);
                var rLast = (Rectangle)Call(list, "RowRect", last);
                Ok("没滚动时最后一条在屏幕外", rLast.Bottom > list.Height, "bottom=" + rLast.Bottom + " > " + list.Height);

                list.SelectedIndex = last;      // 选中会自动滚到可见
                rLast = (Rectangle)Call(list, "RowRect", last);
                r0 = (Rectangle)Call(list, "RowRect", 0);
                Ok("选中最后一条会自动滚过去", rLast.Top >= 0 && rLast.Bottom <= list.Height,
                    "top=" + rLast.Top + " bottom=" + rLast.Bottom + " / 高 " + list.Height);
                Ok("滚下去后第一条移出视野", r0.Bottom <= 0, "第一条 bottom=" + r0.Bottom);

                list.Scroll = 0;
                var wheel = typeof(ScheduleList).GetMethod("OnMouseWheel", F);
                wheel.Invoke(list, new object[] { new MouseEventArgs(MouseButtons.None, 0, 20, 20, -120) });
                int afterWheel = list.Scroll;
                Ok("滚轮能往下滚", afterWheel < 0, "scroll=" + afterWheel);
                wheel.Invoke(list, new object[] { new MouseEventArgs(MouseButtons.None, 0, 20, 20, 120) });
                Ok("滚轮能滚回来，且不会滚过头", list.Scroll > afterWheel && list.Scroll <= 0, "scroll=" + list.Scroll);

                int before = list.Items.Count;
                Call(dlg, "AddSched");
                Call(dlg, "AddSched");
                var added = (Rectangle)Call(list, "RowRect", list.Items.Count - 1);
                Ok("连点两次新建后旧条目都还在", list.Items.Count == before + 2,
                    before + " -> " + list.Items.Count + " 条");
                Ok("新建的自动滚到眼前", added.Top >= 0 && added.Bottom <= list.Height,
                    "top=" + added.Top + " bottom=" + added.Bottom + " / 高 " + list.Height);

                // 滚动之后点哪条就选哪条（行位置带偏移）
                list.Scroll = -list.MaxScroll;   // 滚到底
                var target = (Rectangle)Call(list, "RowRect", last);
                typeof(ScheduleList).GetMethod("OnMouseDown", F).Invoke(list,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 1, 20, target.Top + target.Height / 2, 0) });
                Ok("滚动后点击命中正确的那条", list.SelectedIndex == last, "selected=" + list.SelectedIndex);
            }
        }

        // ---- 13. 行距 6 DIP + 午休行加粗 + 进度条最后 30 分钟变彩色 ----
        {
            int gap = (int)typeof(WidgetForm).GetField("RowGapDip", F).GetValue(null);
            Ok("行距是 6 DIP", gap == 6, gap + " DIP");

            bool boldOk = false;
            using (var form = new WidgetForm(new SettingsStore(), new Settings()))
            {
                var f = typeof(WidgetForm).GetField("fontSmallBold", F).GetValue(form) as Font;
                var plain = typeof(WidgetForm).GetField("fontSmall", F).GetValue(form) as Font;
                // 苹方有独立的「粗体」族，这种字体的 Style 是 Regular，靠族名区分
                boldOk = f != null && plain != null
                    && ((f.Style & FontStyle.Bold) != 0 || f.FontFamily.Name != plain.FontFamily.Name);
            }
            Ok("午休那一行有专门的粗体字", boldOk, "fontSmallBold");

            Func<double, bool> hot = delegate(double m)
            {
                return (bool)typeof(WidgetForm).GetMethod("HotBar", F).Invoke(null, new object[] { m });
            };
            Ok("剩 30 分钟整开始变彩色", hot(30), "30 分钟");
            Ok("剩 30 分钟多一点还不算", !hot(30.5), "30.5 分钟");
            Ok("刚到点不再算彩色", !hot(0) && !hot(-3), "0 / -3 分钟");
            Ok("还剩几小时不彩色", !hot(240), "240 分钟");
        }

        // ---- 12. 提醒出场：两张色块先后落定，退场时下面的先走 ----
        {
            var s = new Settings();
            s.ui.theme = "midnight";
            s.ui.edge = "free";
            s.ui.reducedMotion = false;
            s.ui.reminderHoldSeconds = 5;
            Func<double, bool, Bitmap> shot = delegate(double ms, bool exiting)
            {
                using (var form = new WidgetForm(new SettingsStore(), s))
                {
                    Set(form, "scale", 1.5f);
                    Set(form, "collapsed", false);
                    Set(form, "reminding", true);
                    form.MinimumSize = Size.Empty;
                    form.Size = new Size(420, 282);
                    var ev = new ReminderEvent();
                    ev.title = "需求对齐会"; ev.place = "位置 A"; ev.source = "meeting";
                    Set(form, "remindEvent", ev);
                    Call(form, "Recompute");
                    if (exiting)
                    {
                        Set(form, "remindAt", DateTime.Now.AddMilliseconds(-6000));
                        Set(form, "remindExiting", true);
                        Set(form, "remindExitAt", DateTime.Now.AddMilliseconds(-ms));
                    }
                    else Set(form, "remindAt", DateTime.Now.AddMilliseconds(-ms));
                    var bmp = new Bitmap(420, 282);
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                    Set(form, "reminding", false);
                    Set(form, "remindExiting", false);
                    Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
                    return bmp;
                }
            };
            // 150% 下分界线在 96px：上块盖 0..96，下块盖 96..282
            Color bandA, bandB, plainA, plainB;
            using (var b0 = shot(0, false))
            {
                plainA = b0.GetPixel(300, 20);
                plainB = b0.GetPixel(300, 200);
            }
            using (var early = shot(120, false))
            using (var landed = shot(560, false))
            {
                Color nearSeamEarly = early.GetPixel(300, 90);
                bool moved = Distance(nearSeamEarly, plainA) < 30
                          && Distance(landed.GetPixel(300, 90), plainA) > 40;
                Ok("上面的色块是落下来的（不是一下子出现）", moved,
                    "接缝上方 120ms=" + fmt(nearSeamEarly) + " 560ms=" + fmt(landed.GetPixel(300, 90)));
                Ok("两块都到齐后整张卡都是色块", Distance(landed.GetPixel(300, 6), plainA) > 25
                    && Distance(landed.GetPixel(300, 260), plainB) > 25, "");
                bandA = landed.GetPixel(300, 6);
                bandB = landed.GetPixel(300, 260);
            }
            // 退场：先收字，然后下面的色块先抽走，上面的后走
            using (var e1 = shot(320, true))
            using (var e2 = shot(760, true))
            {
                Color underSeam = e1.GetPixel(300, 108);
                bool bottomGone = Distance(underSeam, plainB) < 45;
                bool topStill = Distance(e1.GetPixel(300, 20), plainA) > 20;
                bool topGone = Distance(e2.GetPixel(300, 20), plainA) < 45;
                Ok("退场时下面的先走、上面的还在", bottomGone && topStill,
                    "接缝下 " + fmt(underSeam) + " 顶部 " + fmt(e1.GetPixel(300, 20)));
                Ok("最后整张卡回到普通卡片", topGone, fmt(e2.GetPixel(300, 20)) + " / " + fmt(plainA));
            }
            Ok("两块色块颜色不同", Distance(bandA, bandB) > 20, fmt(bandA) + " / " + fmt(bandB));
        }


        Console.WriteLine(fail == 0 ? ("ALL PASS  (" + pass + " checks)") : ("FAILED  " + fail + " of " + (pass + fail)));
        return fail == 0 ? 0 : 1;
    }

    /// <summary>每个卡片里的控件必须完整落在卡片内，避免新版挪位置后出现越界/压边。</summary>
    static void LayoutCheck(Control page, string pageName)
    {
        foreach (Control card in page.Controls)
        {
            if (!(card is CardPanel)) continue;
            foreach (Control child in card.Controls)
            {
                var b = child.Bounds;
                var inner = new Rectangle(2, 2, card.Width - 4, card.Height - 4);
                if (!inner.Contains(b))
                    Ok(pageName + " / " + card.Text + " / " + (child.Text.Length > 12 ? child.Text.Substring(0, 12) : child.Text),
                        false, "越界 " + b + " 卡片 " + card.Width + "x" + card.Height);
            }
        }
        Ok(pageName + " 布局", true, page.Controls.Count + " 个卡片内控件均未越界");
    }
}
