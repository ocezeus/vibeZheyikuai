using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WorkCountdown;

// 独立验收：主题色卡占比、浅色主题提醒动画不留亮边、休息提醒触发、日程快捷时间。
class Check
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
    static int pass, fail;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
        int x, int y, int cx, int cy, uint flags);
    static readonly IntPtr HwndTopMost = new IntPtr(-1);
    static readonly IntPtr HwndNotTopMost = new IntPtr(-2);
    const int GwlExStyle = -20;
    const int WsExTopMost = 0x8;
    const uint SwpNoMoveNoSizeNoActivate = 0x1 | 0x2 | 0x10 | 0x200;

    static void Ok(string name, bool cond, string detail)
    {
        if (cond) { pass++; Console.WriteLine("PASS  " + name + "  " + detail); }
        else { fail++; Console.WriteLine("FAIL  " + name + "  " + detail); }
    }

    static object Call(object o, string name, params object[] args)
    {
        MethodInfo found = null;
        foreach (var mi in o.GetType().GetMethods(F))
        {
            if (mi.Name != name) continue;
            if (mi.GetParameters().Length != args.Length) continue;
            found = mi;
            break;
        }
        if (found == null) throw new Exception("no method " + name + "/" + args.Length);
        return found.Invoke(o, args);
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

    static TextBox FindTextBox(Control root)
    {
        if (root == null) return null;
        foreach (Control c in root.Controls)
        {
            var tb = c as TextBox;
            if (tb != null) return tb;
            var inner = FindTextBox(c);
            if (inner != null) return inner;
        }
        return null;
    }

    /// <summary>把面板离屏画到 bitmap（和 NoteShot 一样直接调 OnPaint），用于像素级验收。</summary>
    static Bitmap RenderPanel(NotePanelForm p)
    {
        int w = (int)Get(p, "winWidth");
        int h = (int)(float)Get(p, "heightNow");
        var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h));
        bmp.SetResolution(144, 144);
        using (var g = Graphics.FromImage(bmp))
            Call(p, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height)));
        return bmp;
    }

    /// <summary>把任意分层窗（卡片 / 气泡）离屏画到 bitmap。</summary>
    static Bitmap RenderForm(Form form, int w, int h)
    {
        var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h));
        bmp.SetResolution(144, 144);
        using (var g = Graphics.FromImage(bmp))
            Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height)));
        return bmp;
    }

    /// <summary>
    /// 统计矩形里的「硬不透明」（a ≥ 250）和「半透明」（120 ≤ a &lt; 250）像素数：
    /// 实心背景整块是 255，磨砂玻璃（Frost）底色是半透明，用来判断背景风格真的走了不同的绘制。
    /// </summary>
    static void AlphaSpread(Bitmap bmp, Rectangle r, out int soft, out int hard)
    {
        soft = hard = 0;
        int top = Math.Max(0, r.Top), bottom = Math.Min(bmp.Height, r.Bottom);
        int left = Math.Max(0, r.Left), right = Math.Min(bmp.Width, r.Right);
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            {
                int a = bmp.GetPixel(x, y).A;
                if (a >= 250) hard++;
                else if (a >= 120) soft++;
            }
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
        // 探针只读配置，绝不回写用户自己的设置
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
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
                    // 会议/日程跟随主题；午休、晚休用固定日程自己的配色
                    {
                        Color ink;
                        string sid = kinds[i] == "lunch" ? s.ui.lunchReminderStyle
                            : kinds[i] == "evening" ? s.ui.eveningReminderStyle : "theme";
                        Color ia, ib;
                        ReminderStyles.Resolve(sid, Themes.Get(s.ui.theme), out ia, out ib, out ink);
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
                    // 会议和日程现在都跟随主题，本来就是同一个颜色
                    if ((kinds[i] == "meeting" && kinds[j] == "event")
                        || (kinds[i] == "event" && kinds[j] == "meeting")) continue;
                    int d = Distance(bandA[i], bandA[j]);
                    if (d <= 40) { distinct = false; Ok("配色区分 " + kinds[i] + "/" + kinds[j], false, "距离 " + d); }
                }
            Ok("主题色与固定日程配色可区分", distinct, "午休青绿 / 晚休靛蓝 / 会议与日程=主题色");
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

        // ---- 5c. 提醒扩散范围 / 样式：默认 + 回落 + 存读 ----
        {
            var s = new Settings();
            Ok("提醒扩散默认（仅卡片 / 圆形）",
                s.ui.reminderSpread == "card" && s.ui.reminderSpreadStyle == "circle",
                s.ui.reminderSpread + " / " + s.ui.reminderSpreadStyle);

            var bad = new Settings();
            bad.ui.reminderSpread = "bogus";
            bad.ui.reminderSpreadStyle = "bogus";
            bad.Normalize();
            Ok("非法扩散设置回落默认",
                bad.ui.reminderSpread == "card" && bad.ui.reminderSpreadStyle == "circle",
                bad.ui.reminderSpread + " / " + bad.ui.reminderSpreadStyle);

            var pick = new Settings();
            pick.ui.reminderSpread = "screen";
            pick.ui.reminderSpreadStyle = "bar";
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            var back = ser.Deserialize<Settings>(ser.Serialize(pick));
            back.Normalize();
            Ok("扩散设置能存能读",
                back.ui.reminderSpread == "screen" && back.ui.reminderSpreadStyle == "bar",
                back.ui.reminderSpread + " / " + back.ui.reminderSpreadStyle);

            // 警戒带（tape）是第三种扩散样式：要能存能读，也不能被 Normalize 打回 circle
            var tape = new Settings();
            tape.ui.reminderSpread = "screen";
            tape.ui.reminderSpreadStyle = "tape";
            var tapeBack = ser.Deserialize<Settings>(ser.Serialize(tape));
            tapeBack.Normalize();
            Ok("警戒带样式能存能读",
                tapeBack.ui.reminderSpread == "screen" && tapeBack.ui.reminderSpreadStyle == "tape",
                tapeBack.ui.reminderSpread + " / " + tapeBack.ui.reminderSpreadStyle);

            var tapeBad = new Settings();
            tapeBad.ui.reminderSpreadStyle = "tape ";
            tapeBad.Normalize();
            Ok("警戒带只认精确值（多余空格回落圆形）",
                tapeBad.ui.reminderSpreadStyle == "circle", tapeBad.ui.reminderSpreadStyle);

            Ok("警戒带几何：60 DIP、斜纹 8 DIP，按 layout 缩放",
                ScreenReminderForm.TapeBandHeight(1f) == 60 && ScreenReminderForm.TapeBandHeight(2f) == 120 &&
                ScreenReminderForm.TapeRailHeight(1f) == 8,
                ScreenReminderForm.TapeBandHeight(1f) + " / " + ScreenReminderForm.TapeBandHeight(2f) +
                " / " + ScreenReminderForm.TapeRailHeight(1f));
        }

        // ---- 5e. 背景光效设置 + 自建主题的背景/进度条分开 ----
        {
            var s = new Settings();
            Ok("背景光效默认原样式", s.ui.cardGlow == "classic", s.ui.cardGlow);
            var bad = new Settings();
            bad.ui.cardGlow = "bogus";
            bad.Normalize();
            Ok("非法背景光效回落原样式", bad.ui.cardGlow == "classic", bad.ui.cardGlow);

            Color darkThemeA, darkThemeB, darkInk;
            ReminderStyles.Resolve(ReminderStyles.Follow, Themes.Get("zi-fengxin"), out darkThemeA, out darkThemeB, out darkInk);
            Color accent = Themes.Get("zi-fengxin").accent;
            Func<string, Color> corner = delegate (string mode)
            {
                var st = new Settings();
                st.lark.enabled = false;
                st.calendarEnabled = false;
                st.ui.edge = "free";
                st.ui.theme = "zi-fengxin";
                st.ui.cardGlow = mode;
                using (var form = new WidgetForm(new SettingsStore(), st))
                {
                    Set(form, "scale", 1.5f);
                    Set(form, "collapsed", false);
                    form.MinimumSize = Size.Empty;
                    form.Size = new Size(420, 273);
                    Set(form, "snapshot", new AttendanceSnapshot { ok = true, clockIn = DateTime.Today.AddHours(9) });
                    Call(form, "Recompute");
                    using (var bmp = new Bitmap(420, 273))
                    {
                        bmp.SetResolution(144, 144);
                        using (var g = Graphics.FromImage(bmp))
                            Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 273)));
                        var px = bmp.GetPixel(12, 12);
                        Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
                        return px;
                    }
                }
            };
            Color plainCorner = corner("none");
            Color litCorner = corner("corner");
            Ok("角落光效确实让角落更接近主题色",
                Distance(litCorner, accent) + 8 < Distance(plainCorner, accent),
                fmt(plainCorner) + " -> " + fmt(litCorner) + " 主题 " + fmt(accent));

            var theme = Themes.FromColors("my-test", "夜航橙", "#101C2E", "#FFB454");
            Ok("自建主题背景/进度条分开",
                theme.bgTop.R == 0x10 && theme.bgTop.G == 0x1C && theme.bgTop.B == 0x2E
                && theme.accent.R == 0xFF && theme.accent.G == 0xB4 && theme.accent.B == 0x54,
                fmt(theme.bgTop) + " / " + fmt(theme.accent));

            var pack = new Settings();
            pack.customThemes.Add(new CustomTheme
            {
                id = "my-pack", name = "存读", bg = "#101C2E", bar = "#FFB454", accent = "#FFB454"
            });
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            var back = ser.Deserialize<Settings>(ser.Serialize(pack));
            back.Normalize();
            var ct = back.customThemes[0];
            var loaded = Themes.Get("my-pack");
            Ok("自建主题能存能读（bg/bar）",
                ct.bg == "#101C2E" && ct.bar == "#FFB454"
                && loaded.accent.R == 0xFF && loaded.bgTop.B == 0x2E,
                ct.bg + " / " + ct.bar + " loaded=" + loaded.accent);

            // 调色方块点一下要真的改当前颜色（之前 Changed 只同步面板，没把 sv 的 sat/val 收回表单，点了会弹回去）
            using (var dlg = new ColorWheelForm("测试", "测试",
                Color.FromArgb(16, 28, 46), Color.FromArgb(255, 180, 84)))
            {
                var sv = (Control)Get(dlg, "sv");
                var hex = (Label)Get(dlg, "lblHex");
                string before = hex.Text;
                sv.GetType().GetMethod("Pick", F).Invoke(sv, new object[] { new Point(sv.Width / 2, sv.Height / 2) });
                Ok("调色方块点击能改色", hex.Text != before, before.Replace("\r\n", " | ") + " -> " + hex.Text.Replace("\r\n", " | "));
            }
        }

        // ---- 5d. 新提醒动画：扩散从条子末端开始，停留时两块色 + 全屏模式 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.theme = "lv-kongque";
            s.ui.reminderSpread = "card";
            s.ui.reminderSpreadStyle = "circle";
            s.ui.cardGlow = "none";
            var ev = new ReminderEvent();
            ev.title = "需求对齐会";
            ev.place = "3F · 会议室 A";
            ev.detail = "带上排期";
            ev.source = "local";
            ev.start = Reminders.FormatStart(DateTime.Now.AddMinutes(12));
            ev.end = Reminders.FormatStart(DateTime.Now.AddMinutes(40));
            Color a, b, ink;
            ReminderStyles.Resolve(ReminderStyles.Follow, Themes.Get(s.ui.theme), out a, out b, out ink);
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Set(form, "snapshot", new AttendanceSnapshot { ok = true, clockIn = DateTime.Today.AddHours(9) });
                Set(form, "remindEvent", ev);
                Set(form, "reminding", true);
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-300));
                Call(form, "Recompute");
                var st = Get(form, "status");
                st.GetType().GetProperty("progress").SetValue(st, 0.85, null);
                using (var bmp = new Bitmap(420, 282))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                    Color nearTip = bmp.GetPixel(380, 240);
                    Color far = bmp.GetPixel(10, 10);
                    Ok("卡片扩散从条子末端开始", Distance(nearTip, a) < 14 && Distance(far, a) > 30,
                        "近端=" + nearTip.R + "," + nearTip.G + "," + nearTip.B + " 远端=" + far.R + "," + far.G + "," + far.B);
                }
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-900));
                using (var bmp = new Bitmap(420, 282))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                    Ok("卡片停留 = 两块主题色", Distance(bmp.GetPixel(300, 40), a) < 12 && Distance(bmp.GetPixel(300, 262), b) < 12,
                        "A=" + bmp.GetPixel(300, 40) + " B=" + bmp.GetPixel(300, 262));
                }
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }

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
                Ok("七个页签", tabs.TabPages.Count == 7,
                    string.Join("/", new string[] { tabs.TabPages[0].Text, tabs.TabPages[1].Text,
                        tabs.TabPages[2].Text, tabs.TabPages[3].Text, tabs.TabPages[4].Text, tabs.TabPages[5].Text, tabs.TabPages[6].Text }));

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
        // ---- 11b. 最后 60 分钟走厘秒，刷新率跟着提上来 ----
        {
            var s = new Settings();
            var snap = new AttendanceSnapshot();
            snap.ok = true;
            snap.clockIn = DateTime.Now.AddHours(-9).AddMinutes(-40);
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "snapshot", snap);
                Call(form, "Recompute");
                object st = Get(form, "status");
                st.GetType().GetProperty("targetIso").SetValue(st, DateTime.Now.AddSeconds(95).ToString("s"), null);
                st.GetType().GetProperty("big").SetValue(st, Policy.Clock(TimeSpan.FromSeconds(95)), null);
                st.GetType().GetProperty("bigIsClock").SetValue(st, true, null);
                Call(form, "UpdateTickRate");
                string big = (string)st.GetType().GetProperty("big").GetValue(st, null);
                var tick = (System.Windows.Forms.Timer)Get(form, "tick");
                Ok("不足一小时换成分:秒.厘秒", big == "01:35.00", big);
                Ok("厘秒模式把刷新提到 50ms", tick.Interval == 50, tick.Interval + "ms");
                st.GetType().GetProperty("big").SetValue(st, Policy.Clock(TimeSpan.FromHours(5)), null);
                Call(form, "UpdateTickRate");
                Ok("一小时以上还是一秒一跳", tick.Interval == 1000, tick.Interval + "ms");
            }
        }

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
            Color bandA, bandB, plainA, floodA, floodB, floodInk;
            ReminderStyles.Resolve(ReminderStyles.Follow, Themes.Get(s.ui.theme), out floodA, out floodB, out floodInk);
            using (var b0 = shot(0, false))
            {
                plainA = b0.GetPixel(300, 20);
            }
            using (var early = shot(120, false))
            using (var flooded = shot(520, false))
            using (var hold = shot(1200, false))
            {
                Color nearSeamEarly = early.GetPixel(300, 90);
                bool moved = Distance(nearSeamEarly, plainA) < 30
                          && Distance(flooded.GetPixel(300, 90), floodA) < 20;
                Ok("扩散是逐渐铺开的（不是一下子出现）", moved,
                    "120ms=" + fmt(nearSeamEarly) + " 520ms=" + fmt(flooded.GetPixel(300, 90)));
                Ok("铺满后整张卡都是色块", Distance(flooded.GetPixel(300, 6), floodA) < 20
                    && Distance(flooded.GetPixel(300, 260), floodA) < 20, "");
                bandA = hold.GetPixel(300, 6);
                bandB = hold.GetPixel(300, 260);
                Ok("停留时上下两块色不同", Distance(bandA, bandB) > 20, fmt(bandA) + " / " + fmt(bandB));
            }
            // 退场：先擦字，然后整块色收回去
            using (var e1 = shot(320, true))
            using (var e2 = shot(900, true))
            {
                bool stillFlooded = Distance(e1.GetPixel(300, 20), floodA) < 20;
                bool topGone = Distance(e2.GetPixel(300, 20), plainA) < 20;
                Ok("退场先擦内容、色块还铺着", stillFlooded,
                    "顶部 " + fmt(e1.GetPixel(300, 20)) + " / 色块 " + fmt(floodA));
                Ok("最后整张卡回到普通卡片", topGone, fmt(e2.GetPixel(300, 20)) + " / " + fmt(plainA));
            }

            // 圆角是像素 alpha 收的边：角上应该半透明，中间完全不透明
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Call(form, "Recompute");
                using (var canvas = new Bitmap(420, 282))
                using (var g = Graphics.FromImage(canvas))
                    Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                var frame = (Bitmap)Get(form, "frame");
                // 沿对角线扫一圈，弧线上一定有几个像素是「半透明」的——那才是抗锯齿
                int partial = 0, hard = 0;
                for (int yy = 0; yy < 40; yy++)
                for (int xx = 0; xx < 40; xx++)
                {
                    int a = frame.GetPixel(xx, yy).A;
                    if (a > 8 && a < 248) partial++;
                }
                for (int i = 0; i < 0; i++)
                {
                    int a = frame.GetPixel(i, i).A;
                    if (a > 8 && a < 248) partial++;
                }
                Color corner = frame.GetPixel(0, 0);
                if (corner.A > 250) hard++;
                Color mid = frame.GetPixel(210, 140);
                Ok("圆角是抗锯齿的（不是硬边）", partial >= 3 && hard == 0, "半透明像素 " + partial + " 个，纯色角 " + hard + " 个");
                Ok("卡面本身不透明", mid.A == 255, "alpha=" + mid.A);
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }

            // 展开错峰：时间/进度条先动，最后一行要晚好几帧才出现
            Func<int, Bitmap> stage = delegate(int ms)
            {
                using (var form = new WidgetForm(new SettingsStore(), s))
                {
                    Set(form, "scale", 1.5f);
                    Set(form, "collapsed", false);
                    form.MinimumSize = Size.Empty;
                    form.Size = new Size(420, 282);
                    Call(form, "Recompute");
                    Set(form, "expandAnim", true);
                    Set(form, "expandStart", Environment.TickCount - ms);
                    var bmp = new Bitmap(420, 282);
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                    Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
                    return bmp;
                }
            };
            using (var first = stage(0))
            using (var early = stage(60))
            using (var done = stage(700))
            {
                int focalStep = 0, footStep = 0;
                for (int y = 108; y < 158; y++)
                    for (int x = 24; x < 380; x++)
                        if (Distance(first.GetPixel(x, y), early.GetPixel(x, y)) > 12) focalStep++;
                for (int y = 242; y < 276; y++)
                    for (int x = 24; x < 380; x++)
                        if (Distance(first.GetPixel(x, y), early.GetPixel(x, y)) > 12) footStep++;
                int footDiff = 0;
                for (int y = 242; y < 276; y++)
                    for (int x = 24; x < 380; x++)
                        if (Distance(early.GetPixel(x, y), done.GetPixel(x, y)) > 12) footDiff++;
                Ok("展开时时间先出来、最后一行后出来", focalStep > 200 && footStep == 0 && footDiff > 200,
                    "主视觉变了 " + focalStep + " 点，末行此时还没动，末尾共变 " + footDiff + " 点");
            }
        }


            // ── 13. 展开/收起的点击路径（v2.6.1 修的就是这里） ──
            {
                // 缩起来时点一下必须变大；即使 collapsed 字段和窗口尺寸不同步也要自愈
                var s = new Settings();
                using (var form = new WidgetForm(new SettingsStore(), s))
                {
                    Set(form, "scale", 1.5f);
                    form.MinimumSize = Size.Empty;
                    Call(form, "Recompute");
                    Set(form, "collapsed", false);        // 故意让字段和尺寸对不上
                    form.Size = new Size(336, 102);
                    Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 168, 51, 0));
                    bool started = (bool)Get(form, "animating");
                    Set(form, "animStart", Environment.TickCount - 900);
                    Call(form, "StepAnim");
                    Ok("点小卡片能展开（尺寸不同步也能救回来）",
                        started && form.Width == 420 && form.Height == 282 && !(bool)Get(form, "collapsed"),
                        "animating=" + started + " 现在 " + form.Width + "x" + form.Height);
                }

                // 展开状态下点右上角箭头要收起来；没画过（命中区为空）也要能点中
                using (var form = new WidgetForm(new SettingsStore(), s))
                {
                    Set(form, "scale", 1.5f);
                    form.MinimumSize = Size.Empty;
                    Call(form, "Recompute");
                    Set(form, "collapsed", false);
                    form.Size = new Size(420, 282);
                    Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 381, 30, 0));
                    bool started = (bool)Get(form, "animating");
                    Set(form, "animStart", Environment.TickCount - 900);
                    Call(form, "StepAnim");
                    Ok("点右上角箭头能收起（没画过也算数）",
                        started && form.Width == 336 && form.Height == 102 && (bool)Get(form, "collapsed"),
                        "animating=" + started + " 现在 " + form.Width + "x" + form.Height);
                }

                // 动画被卡住时，点击不能失灵：SetCollapsed 会先把上一段收尾
                using (var form = new WidgetForm(new SettingsStore(), s))
                {
                    Set(form, "scale", 1.5f);
                    form.MinimumSize = Size.Empty;
                    Call(form, "Recompute");
                    Set(form, "collapsed", true);
                    form.Size = new Size(336, 102);
                    Set(form, "animating", true);           // 假装上一段动画还没走完
                    Set(form, "animToW", 336);
                    Set(form, "animToH", 102);
                    Call(form, "SetCollapsed", false);
                    bool started = (bool)Get(form, "animating");
                    Set(form, "animStart", Environment.TickCount - 900);
                    Call(form, "StepAnim");
                    Ok("上一段动画卡住时点击仍然有效",
                        started && form.Width == 420 && !(bool)Get(form, "collapsed"),
                        "animating=" + started + " 现在 " + form.Width + "x" + form.Height);
                }

                // v2.6.3 的老毛病：分层窗口一旦开双缓冲，WinForms 就再也不调 OnPaint，
                // 第一帧永远出不来 → 窗口全透明，既看不见也点不动。这条守住它。
                using (var form = new WidgetForm(new SettingsStore(), s))
                {
                    bool dbl = (bool)Call(form, "GetStyle", ControlStyles.OptimizedDoubleBuffer);
                    Ok("分层窗口没有开双缓冲（开了就永远不画第一帧）", !dbl,
                        "OptimizedDoubleBuffer=" + dbl);
                }

                // 手抖：按下后只挪了 2 像素就松手，仍然算点击——窗口要挪回原位，不能变成拖动
                using (var form = new WidgetForm(new SettingsStore(), s))
                {
                    Set(form, "scale", 1.5f);
                    form.MinimumSize = Size.Empty;
                    Call(form, "Recompute");
                    Set(form, "collapsed", true);
                    form.Size = new Size(336, 102);
                    var origin = new Point(900, 500);
                    form.Location = origin;
                    Call(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 168, 51, 0));
                    form.Location = new Point(origin.X + 2, origin.Y + 1);   // 手抖 2 像素
                    Set(form, "moved", true);                                // 模拟 OnMouseMove 已经置过
                    Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 168, 51, 0));
                    Ok("手抖拖 2 像素仍算点击（窗口挪回原位）",
                        form.Location == origin && (bool)Get(form, "animating"),
                        "位置 " + form.Location + " animating=" + Get(form, "animating"));
                }

                // 对称面：真拖动（挪了 40 像素）不能被当成点击，也不该展开
                using (var form = new WidgetForm(new SettingsStore(), s))
                {
                    Set(form, "scale", 1.5f);
                    form.MinimumSize = Size.Empty;
                    Call(form, "Recompute");
                    Set(form, "collapsed", true);
                    form.Size = new Size(336, 102);
                    var origin = new Point(600, 400);
                    form.Location = origin;
                    Call(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 168, 51, 0));
                    form.Location = new Point(origin.X + 40, origin.Y + 30);
                    Set(form, "moved", true);
                    Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 168, 51, 0));
                    Ok("真拖动 40 像素不当成点击",
                        !(bool)Get(form, "animating") && (bool)Get(form, "collapsed"),
                        "animating=" + Get(form, "animating") + " collapsed=" + Get(form, "collapsed"));
                }
            }

            // ── 14. 置顶看门狗 + 流光在尽头淡出（v2.6.4） ──
            {
                // 设置里默认「窗口置顶」：建句柄后必须真的带 WS_EX_TOPMOST；
                // 被系统 / 别的程序摘掉后，看门狗要能补回来；关掉开关就不再强制。
                var s = new Settings();
                using (var form = new WidgetForm(new SettingsStore(), s))
                {
                    form.MinimumSize = Size.Empty;
                    IntPtr h = form.Handle;
                    Call(form, "KeepTopMost");
                    bool on = (GetWindowLong(h, GwlExStyle) & WsExTopMost) != 0;
                    SetWindowPos(h, HwndNotTopMost, 0, 0, 0, 0, SwpNoMoveNoSizeNoActivate);
                    bool lost = (GetWindowLong(h, GwlExStyle) & WsExTopMost) == 0;
                    Call(form, "KeepTopMost");
                    bool back = (GetWindowLong(h, GwlExStyle) & WsExTopMost) != 0;
                    Ok("置顶被摘掉后看门狗能补回来", on && lost && back,
                        "初始置顶=" + on + " 摘掉后=" + (!lost) + " 补回=" + back);

                    s.ui.alwaysOnTop = false;
                    Call(form, "ApplySettings");
                    bool off = (GetWindowLong(h, GwlExStyle) & WsExTopMost) == 0;
                    Ok("关掉置顶后不再强制最上层", off, "WS_EX_TOPMOST=" + (!off));
                }

                // 流光不透明度曲线：中段全亮，两头（尤其走到尽头）平滑降到 0
                double o02 = WidgetForm.FlowOpacity(0.02);
                double o18 = WidgetForm.FlowOpacity(0.18);
                double o50 = WidgetForm.FlowOpacity(0.50);
                double o80 = WidgetForm.FlowOpacity(0.80);
                double o90 = WidgetForm.FlowOpacity(0.90);
                double o995 = WidgetForm.FlowOpacity(0.995);
                Ok("流光两头淡入淡出（尽头不是突然消失）",
                    WidgetForm.FlowOpacity(0) == 0 && WidgetForm.FlowOpacity(1) == 0 &&
                    o50 == 1 && o80 > o90 && o995 < 0.02 && o02 < 0.06 && o18 > 0.99,
                    "0.02=" + o02.ToString("0.000") + " 0.18=" + o18.ToString("0.000") +
                    " 0.50=" + o50.ToString("0.000") + " 0.80=" + o80.ToString("0.000") +
                    " 0.90=" + o90.ToString("0.000") + " 0.995=" + o995.ToString("0.000"));

                // 真正画出来也要对：中段有亮带，走到尽头时亮带应该已经淡没了
                Func<double, Bitmap> flowShot = delegate(double phase)
                {
                    var fs = new Settings();
                    fs.ui.reducedMotion = false;
                    var snap = new AttendanceSnapshot();
                    snap.ok = true;
                    snap.clockIn = DateTime.Today.AddHours(9);
                    using (var form = new WidgetForm(new SettingsStore(), fs))
                    {
                        Set(form, "scale", 1.5f);
                        Set(form, "snapshot", snap);
                        Set(form, "collapsed", false);
                        form.MinimumSize = Size.Empty;
                        form.Size = new Size(420, 282);
                        Call(form, "Recompute");
                        var st = Get(form, "status");
                        st.GetType().GetProperty("progress").SetValue(st, 0.6, null);
                        Set(form, "flowPhase", phase);
                        var bmp = new Bitmap(420, 282);
                        using (var g = Graphics.FromImage(bmp))
                            Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                        Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
                        return bmp;
                    }
                };
                int mid = 0, end = 0;
                using (var a = flowShot(0.50))
                using (var b = flowShot(0.995))
                {
                    for (int y = 153; y < 159; y++)
                        for (int x = 30; x < 240; x++)
                        {
                            mid = Math.Max(mid, (int)Math.Round(RelLum(a.GetPixel(x, y)) * 255));
                            end = Math.Max(end, (int)Math.Round(RelLum(b.GetPixel(x, y)) * 255));
                        }
                }
                Ok("流光淡出已经接进绘制（尽头不再有亮带）", mid - end >= 8,
                    "中段最亮 " + mid + " / 尽头 " + end);
            }

        // ---- 5f. 警戒带之后的抽屉卡（方案 A：实心黄 + 同色系三阶文字） ----
        {
            Ok("抽屉卡几何：440×68、压进卡片 24、内容宽 382",
                DrawerForm.WidthDip == 440 && DrawerForm.HeightDip == 68
                && DrawerForm.OverlapDip == 24 && DrawerForm.GapDip == 12 && DrawerForm.FarPadDip == 22
                && DrawerForm.WidthDip - DrawerForm.OverlapDip - DrawerForm.GapDip - DrawerForm.FarPadDip == 382,
                DrawerForm.WidthDip + " x " + DrawerForm.HeightDip + "，内容 " +
                (DrawerForm.WidthDip - DrawerForm.OverlapDip - DrawerForm.GapDip - DrawerForm.FarPadDip));

            double cTitle = Contrast(DrawerForm.InkColor, DrawerForm.TapeColor);
            double cMeta = Contrast(DrawerForm.Ink2Color, DrawerForm.TapeColor);
            double cLeft = Contrast(DrawerForm.Ink3Color, DrawerForm.TapeColor);
            Ok("抽屉卡三阶文字都在暖色系且够读",
                cTitle >= 8.5 && cMeta >= 4.5 && cLeft >= 4.5 && cTitle > cMeta && cMeta > cLeft,
                "标题 " + cTitle.ToString("0.0") + ":1 副行 " + cMeta.ToString("0.0") + ":1 剩余 " + cLeft.ToString("0.0") + ":1");
            Ok("抽屉卡底色就是警示黄 #F5C400",
                DrawerForm.TapeColor.R == 245 && DrawerForm.TapeColor.G == 196 && DrawerForm.TapeColor.B == 0,
                fmt(DrawerForm.TapeColor));

            // 滑出节奏照抄 HTML 预览：0.9s + cubic-bezier(.22,.9,.28,1)（先窜出去再落定）
            var ease = typeof(DrawerForm).GetMethod("CssEase", F);
            double e20 = (double)ease.Invoke(null, new object[] { 0.2 });
            double e50 = (double)ease.Invoke(null, new object[] { 0.5 });
            double e100 = (double)ease.Invoke(null, new object[] { 1.0 });
            Ok("抽屉滑出 0.55s、先快后慢（和预览同一条曲线）",
                DrawerForm.SlideMs == 550 && e20 > 0.5 && e50 > 0.85 && e100 >= 0.999,
                "0.2->" + e20.ToString("0.000") + " 0.5->" + e50.ToString("0.000") + " 1.0->" + e100.ToString("0.000"));

            var ds = new Settings();
            ds.lark.enabled = false;
            ds.calendarEnabled = false;
            var dev = new ReminderEvent();
            dev.title = "产品评审会 · Q4 排期";
            dev.place = "3F · 云杉会议室";
            dev.source = "lark";
            dev.start = Reminders.FormatStart(DateTime.Now.AddMinutes(10));
            dev.end = Reminders.FormatStart(DateTime.Now.AddMinutes(40));
            var cardRect = new Rectangle(800, 200, 224, 68);
            using (var d = new DrawerForm(ds, dev, cardRect, false, 1f, false, null))
            {
                var full = (Bitmap)Call(d, "BuildFrame", 440);
                var center = full.GetPixel(300, 34);
                Ok("抽屉卡画的是实心黄",
                    center.R == 245 && center.G == 196 && center.B == 0, fmt(center));
                Ok("抽屉卡外角是圆的（左上角透明）", full.GetPixel(1, 1).A < 40,
                    "alpha=" + full.GetPixel(1, 1).A);

                int dark = 0;
                for (int y = 6; y < 62; y++)
                    for (int x = 24; x < 436; x++)
                    {
                        var px = full.GetPixel(x, y);
                        if (px.R < 110 && px.G < 95) dark++;
                    }
                Ok("抽屉卡有标题和副行（有字，没留白）", dark > 300, dark + " 个深色像素");

                var thin = (Bitmap)Call(d, "BuildFrame", 20);
                int leak = 0;
                for (int y = 0; y < thin.Height; y++)
                    for (int x = 0; x < thin.Width; x++)
                    {
                        var px = thin.GetPixel(x, y);
                        if (px.A > 40 && px.R < 110 && px.G < 95) leak++;
                    }
                Ok("抽屉卡没拉够时不提前露内容", leak == 0, leak + " 个提前露出的深色像素");
            }

            // 警戒带模式下卡片只画普通卡片：不做色块覆盖
            Func<bool, Color> cardShot = delegate(bool tape)
            {
                var st = new Settings();
                st.lark.enabled = false;
                st.calendarEnabled = false;
                st.ui.edge = "free";
                st.ui.theme = "lv-kongque";
                st.ui.reminderSpread = "screen";
                st.ui.reminderSpreadStyle = "tape";
                st.ui.cardGlow = "none";
                using (var form = new WidgetForm(new SettingsStore(), st))
                {
                    Set(form, "scale", 1.5f);
                    Set(form, "collapsed", false);
                    form.MinimumSize = Size.Empty;
                    form.Size = new Size(420, 282);
                    Set(form, "snapshot", new AttendanceSnapshot { ok = true, clockIn = DateTime.Today.AddHours(9) });
                    Set(form, "remindEvent", dev);
                    Set(form, "reminding", true);
                    Set(form, "tapeActive", tape);
                    Set(form, "remindAt", DateTime.Now.AddMilliseconds(-900));
                    Call(form, "Recompute");
                    using (var bmp = new Bitmap(420, 282))
                    {
                        bmp.SetResolution(144, 144);
                        using (var g = Graphics.FromImage(bmp))
                            Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                        Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
                        return bmp.GetPixel(210, 140);
                    }
                }
            };
            Color tapeCard = cardShot(true);
            Color plainTape = cardShot(false);
            Color a, b, ink;
            ReminderStyles.Resolve(ReminderStyles.Follow, Themes.Get("lv-kongque"), out a, out b, out ink);
            Ok("警戒带模式下卡片不做覆盖", Distance(tapeCard, a) > 60,
                "卡片中心 " + fmt(tapeCard) + " / 提醒色 " + fmt(a));
            Ok("非警戒带仍然做覆盖（对照）", Distance(plainTape, a) < 60,
                "卡片中心 " + fmt(plainTape) + " / 提醒色 " + fmt(a));

            // 提醒触发时先展开再放全屏：StartReminder 之后立刻就是「展开中 + 等全屏」
            var fs = new Settings();
            fs.lark.enabled = false;
            fs.calendarEnabled = false;
            fs.ui.edge = "right";
            // 警戒带现在是「飞书会议」这一类自己的样式（四类各一套）
            fs.SetStyleOf(Reminders.KindLark, new ReminderStyleDef("screen", "tape", ReminderStyles.Follow));
            using (var form = new WidgetForm(new SettingsStore(), fs))
            {
                Set(form, "scale", 1f);
                Set(form, "collapsed", true);
                Set(form, "snapshot", new AttendanceSnapshot { ok = true, clockIn = DateTime.Today.AddHours(9) });
                Call(form, "Recompute");
                Call(form, "StartReminder", dev);
                bool expanding = (bool)Get(form, "animating")
                    && (int)Get(form, "animToW") == (int)Call(form, "S", 280f);
                bool waiting = (int)Get(form, "tapePhase") == 1 && (bool)Get(form, "tapeActive");
                Ok("提醒触发时先把小卡展开，再等全屏", expanding && waiting,
                    "animating=" + Get(form, "animating") + " 目标宽=" + Get(form, "animToW") +
                    " phase=" + Get(form, "tapePhase"));
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        // ---- 5f2. 四类日程各自的样式真的进了渲染（ScreenReminderForm 里取到的值） ----
        {
            var st = new Settings();
            st.lark.enabled = false;
            st.calendarEnabled = false;
            st.Normalize();
            st.SetStyleOf(Reminders.KindOutlook, new ReminderStyleDef("screen", "tape", "lv-kongque"));
            st.SetStyleOf(Reminders.KindLark, new ReminderStyleDef("screen", "bar", "lan-yuming"));
            st.SetStyleOf(Reminders.KindLocal, new ReminderStyleDef("card", "circle", ReminderStyles.Follow));
            st.SetStyleOf(Reminders.KindFixed, new ReminderStyleDef("card", "circle", ReminderStyles.Follow));

            var theme = Themes.Get("midnight");
            var scr = Screen.PrimaryScreen.Bounds;
            var tip = new Point(scr.Left + 20, scr.Top + 20);

            Func<string, string, ReminderEvent> mk = delegate(string source, string title)
            {
                var e = new ReminderEvent();
                e.title = title;
                e.source = source;
                e.start = Reminders.FormatStart(DateTime.Now.AddMinutes(10));
                e.end = Reminders.FormatStart(DateTime.Now.AddMinutes(40));
                return e;
            };

            Color ea, eb, eink;
            ReminderStyles.Resolve("lv-kongque", theme, out ea, out eb, out eink);
            using (var f = new ScreenReminderForm(st, mk("outlook", "Outlook 的会"), theme, scr, tip, null))
            {
                Ok("Outlook 类走它自己的警戒带", (bool)Get(f, "tape") && !(bool)Get(f, "bar"),
                    "tape=" + Get(f, "tape") + " bar=" + Get(f, "bar"));
                Ok("Outlook 类用孔雀配色", Distance((Color)Get(f, "a"), ea) < 4 && Distance((Color)Get(f, "b"), eb) < 4,
                    fmt((Color)Get(f, "a")) + " / 期望 " + fmt(ea));
                Ok("Outlook 的类型标签", (string)Get(f, "kind") == "Outlook 日程", (string)Get(f, "kind"));
            }

            ReminderStyles.Resolve("lan-yuming", theme, out ea, out eb, out eink);
            using (var f = new ScreenReminderForm(st, mk("lark", "飞书的会"), theme, scr, tip, null))
            {
                Ok("飞书类走进度条拉伸", (bool)Get(f, "bar") && !(bool)Get(f, "tape"),
                    "bar=" + Get(f, "bar") + " tape=" + Get(f, "tape"));
                Ok("飞书类用玉茗配色", Distance((Color)Get(f, "a"), ea) < 4, fmt((Color)Get(f, "a")) + " / 期望 " + fmt(ea));
            }

            ReminderStyles.Resolve(ReminderStyles.Follow, theme, out ea, out eb, out eink);
            using (var f = new ScreenReminderForm(st, mk("local", "普通日程"), theme, scr, tip, null))
            {
                Ok("普通日程走圆形柔光、跟随主题", !(bool)Get(f, "bar") && !(bool)Get(f, "tape")
                    && Distance((Color)Get(f, "a"), ea) < 4,
                    "bar=" + Get(f, "bar") + " tape=" + Get(f, "tape") + " " + fmt((Color)Get(f, "a")));
            }

            // 固定日程：每条自己的 styleId 优先于「固定日程」类的配色（旧规则不回退）
            var lunch = st.ScheduleOf("lunch");
            lunch.styleId = "lv-kongque";
            var fixEv = mk("break", "午休");
            fixEv.id = "fix:" + lunch.id;
            ReminderStyles.Resolve("lv-kongque", theme, out ea, out eb, out eink);
            using (var f = new ScreenReminderForm(st, fixEv, theme, scr, tip, null))
            {
                Ok("固定日程的自己配色优先于类别配色", Distance((Color)Get(f, "a"), ea) < 4,
                    fmt((Color)Get(f, "a")) + " / 期望 " + fmt(ea));
            }
        }

        // ---- 5g. 卡片第二行：午休没配好，也不能把晚休一起吞掉 ----
        {
            Func<Settings, DateTime, CardStatus> build = delegate(Settings st2, DateTime at)
            {
                var snap = new AttendanceSnapshot();
                snap.ok = true;
                snap.clockIn = at.Date.AddHours(9);
                return StatusBuilder.Build(st2, snap, at, Themes.Get("ivory"));
            };

            // 内置午休只填了开始时间、没有结束时间（用户把午休改成 17:00 就是这个状态）
            var broken = new Settings();
            broken.lark.enabled = false;
            broken.calendarEnabled = false;
            broken.NormalizeSchedules();
            broken.lunchStart = "17:00";
            broken.lunchEnd = "17:00";
            broken.ScheduleOf("lunch").start = "17:00";
            broken.ScheduleOf("lunch").end = "";
            broken.eveningEnabled = true;
            broken.eveningStart = "17:50";
            broken.eveningEnd = "19:00";
            broken.ScheduleOf("evening").start = "17:50";
            broken.ScheduleOf("evening").end = "19:00";
            var nowAt = DateTime.Today.AddHours(11).AddMinutes(16);
            var stb = build(broken, nowAt);
            Ok("午休没配好时晚休照样显示",
                stb.lunch == LunchState.EveningBefore && stb.lunchLabel == "距晚休",
                stb.lunch + " / " + stb.lunchLabel + " " + stb.lunchValue);

            // 老版本把午休时段建成普通日程：内置午休坏掉时拿它顶上
            var withCustom = new Settings();
            withCustom.lark.enabled = false;
            withCustom.calendarEnabled = false;
            withCustom.NormalizeSchedules();
            withCustom.lunchStart = "17:00";
            withCustom.lunchEnd = "17:00";
            withCustom.ScheduleOf("lunch").start = "17:00";
            withCustom.ScheduleOf("lunch").end = "";
            withCustom.schedules.Add(FixedSchedule.Make("c-lunch", "custom", "午休", "11:50", "13:30", "theme"));
            var stc = build(withCustom, nowAt);
            Ok("午休没配好时用午休时段的普通日程顶上",
                stc.lunch == LunchState.Before && stc.lunchLabel == "距午休" && stc.lunchValue == "34m",
                stc.lunch + " / " + stc.lunchLabel + " " + stc.lunchValue);

            // 正常配置下还是「距午休」
            var normal = new Settings();
            normal.lark.enabled = false;
            normal.calendarEnabled = false;
            normal.NormalizeSchedules();
            normal.lunchStart = "11:50";
            normal.lunchEnd = "13:30";
            normal.ScheduleOf("lunch").start = "11:50";
            normal.ScheduleOf("lunch").end = "13:30";
            var stn = build(normal, nowAt);
            Ok("午休正常时显示距午休",
                stn.lunch == LunchState.Before && stn.lunchLabel == "距午休" && stn.lunchValue == "34m",
                stn.lunch + " / " + stn.lunchLabel + " " + stn.lunchValue);
        }

        // ---- 5h. 进度条外发光的颜色跟着条子走（最后半小时条子是彩虹，光也得是） ----
        {
            var gs = new Settings();
            gs.lark.enabled = false;
            gs.calendarEnabled = false;
            gs.ui.edge = "free";
            gs.ui.theme = "lv-kongque";
            gs.ui.cardGlow = "none";
            gs.ui.reducedMotion = true;
            using (var form = new WidgetForm(new SettingsStore(), gs))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Set(form, "snapshot", new AttendanceSnapshot { ok = true, clockIn = DateTime.Today.AddHours(9) });
                Call(form, "Recompute");
                var st2 = Get(form, "status");
                st2.GetType().GetProperty("progress").SetValue(st2, 0.55, null);
                st2.GetType().GetProperty("targetIso").SetValue(
                    st2, DateTime.Now.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ss"), null);
                using (var bmp = new Bitmap(420, 282))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                    int barY = (int)Call(form, "S", 102f);
                    int barH = (int)Call(form, "S", 4f);
                    Color barPx = bmp.GetPixel(200, barY + barH / 2);
                    Color haloPx = bmp.GetPixel(200, Math.Max(0, barY - 4));
                    Color accent = Themes.Get("lv-kongque").accent;
                    Ok("进度条外发光的颜色跟着条子走",
                        Distance(haloPx, barPx) < Distance(haloPx, accent),
                        "光 " + fmt(haloPx) + " / 条 " + fmt(barPx) + " / 主题色 " + fmt(accent));
                }
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        // ---- 5i. 右键胶囊菜单：几何、主题配色、原地长开 ----
        {
            // v3.6.0：菜单从 4 颗加到 6 颗（多了「预览通知」「检查更新」），列高 = 6×40 + 5×8 = 280
            Ok("胶囊几何：6 颗 124×40、间距 8、列高 280、长开 246",
                BubbleMenuForm.PillWDip == 124 && BubbleMenuForm.PillHDip == 40
                && BubbleMenuForm.PillGapDip == 8 && BubbleMenuForm.PillRadiusDip == 20
                && BubbleMenuForm.Labels.Length == 6 && BubbleMenuForm.ColumnHDip == 280
                && BubbleMenuForm.PanelWDip == 246
                && BubbleMenuForm.ExtraHDip(BubbleAction.Theme) == 168
                && BubbleMenuForm.ExtraHDip(BubbleAction.Clock) == 130
                && BubbleMenuForm.ExtraHDip(BubbleAction.Schedule) == 222
                && BubbleMenuForm.ExtraHDip(BubbleAction.Preview) == 208
                && BubbleMenuForm.ExtraHDip(BubbleAction.Update) == 216
                && BubbleMenuForm.ItemHDip(BubbleAction.Theme) == 208
                && BubbleMenuForm.ItemHDip(BubbleAction.Clock) == 170
                && BubbleMenuForm.ItemHDip(BubbleAction.Schedule) == 262
                && BubbleMenuForm.ItemHDip(BubbleAction.Preview) == 248
                && BubbleMenuForm.ItemHDip(BubbleAction.Update) == 256,
                BubbleMenuForm.PillWDip + "x" + BubbleMenuForm.PillHDip + " 列高 " + BubbleMenuForm.ColumnHDip
                + " 长开 168/130/222/208/216（+40 头 = 208/170/262/248/256）");

            var bs = new Settings();
            bs.lark.enabled = false;
            bs.calendarEnabled = false;
            bs.ui.theme = "midnight";
            bs.ui.alwaysOnTop = true;
            var card = new Rectangle(0, 300, 224, 68);
            using (var m = new BubbleMenuForm(bs, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                float sc = (float)Get(m, "scale");
                Func<float, int> s = delegate(float d) { return (int)Math.Round(d * sc); };
                var rects = (Rectangle[])Get(m, "pillRects");
                Ok("六颗胶囊同宽同高、竖排等距",
                    rects.Length == 6 && rects[0].Width == s(124) && rects[0].Height == s(40)
                    && rects[3].Width == s(124) && rects[3].Height == s(40)
                    && rects[1].Top - rects[0].Top == s(48) && rects[5].Top - rects[4].Top == s(48)
                    && rects[5].Bottom <= m.Height,
                    rects.Length + " 颗，第一颗 " + rects[0].Width + "x" + rects[0].Height + "，末颗底边 " + rects[5].Bottom);
                Ok("卡片贴左时胶囊开在右边", (bool)Get(m, "toRight"), "toRight=" + Get(m, "toRight"));

                Set(m, "openedAt", Environment.TickCount - 5000);
                using (var bmp = new Bitmap(m.Width, m.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                        Call(m, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, m.Width, m.Height)));
                    Color center = bmp.GetPixel(rects[0].Left + rects[0].Width / 2, rects[0].Top + rects[0].Height / 2);
                    Color corner = bmp.GetPixel(1, 1);
                    Color round = bmp.GetPixel(rects[0].Left + 1, rects[0].Top + 1);
                    Color bg = Themes.Get("midnight").bgTop;
                    Ok("胶囊底色跟主题走（不是固定黑）",
                        Distance(center, bg) < 50 && center.A > 200 && corner.A < 30 && round.A < 90,
                        "中心 " + fmt(center) + " / 主题底 " + fmt(bg) + " / 角 a=" + corner.A);

                    // 点「修改主题」：原地长开，头还在，下面出 8 个色卡
                    Call(m, "OpenItem", 1);
                    int extra = (int)Call(m, "S", (float)BubbleMenuForm.ExtraHDip(BubbleAction.Theme));
                    Set(m, "animating", false);
                    Set(m, "openExtra", (double)extra);
                    Set(m, "openTo", (double)extra);
                    var item = (Rectangle)Call(m, "ItemRect", 1);
                    using (var g = Graphics.FromImage(bmp))
                        Call(m, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, m.Width, m.Height)));
                    Color head = bmp.GetPixel(item.Left + item.Width / 2, item.Top + s(20));
                    Ok("点开后胶囊头还在、宽度长到 246",
                        item.Width == s(246) && item.Height == s(208) && head.A > 200,
                        item.Width + "x" + item.Height + " 头 a=" + head.A);

                    int chips = 0;
                    for (int y = item.Top + s(50); y < item.Top + s(112); y++)
                        for (int x = item.Left + s(12); x < item.Right - s(12); x++)
                        {
                            Color px = bmp.GetPixel(x, y);
                            if (px.A > 180 && Math.Abs(px.R - px.G) + Math.Abs(px.G - px.B) > 40) chips++;
                        }
                    Ok("长开的卡片里有主题色卡", chips > 30, chips + " 个有色像素");

                    // 主题面板：一屏 4×3 = 12 张 + 底部「新建主题」，滚轮能往下滚
                    int themeHits = 0, itemHits = 0;
                    bool hasNew = false;
                    foreach (object h in (System.Collections.IEnumerable)Get(m, "hits"))
                    {
                        string key = (string)h.GetType().GetField("key").GetValue(h);
                        if (key != null && key.StartsWith("theme:", StringComparison.Ordinal)) themeHits++;
                        if (key == "newtheme") hasNew = true;
                        itemHits++;
                    }
                    Ok("主题面板一屏 12 张色卡 + 新建主题",
                        themeHits == 12 && hasNew, themeHits + " 张 / 新建=" + hasNew + " / 总命中 " + itemHits);

                    var view = (Rectangle)Call(m, "ThemeView", item);
                    int scroll0 = (int)Get(m, "themeScroll");
                    Call(m, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, view.Left + s(6), view.Top + s(20), -120));
                    int scroll1 = (int)Get(m, "themeScroll");
                    Ok("滚轮往下滚一行", scroll1 == scroll0 + s(40), scroll0 + " → " + scroll1 + "（期望 +" + s(40) + "）");

                    for (int i = 0; i < 20; i++)
                        Call(m, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, view.Left + s(6), view.Top + s(20), -120));
                    int scroll2 = (int)Get(m, "themeScroll");
                    int max = (int)Call(m, "ThemeMaxScroll", view);
                    Ok("滚到底就停住", scroll2 == max && max > 0, scroll2 + " / max " + max);

                    themeHits = 0;
                    foreach (object h in (System.Collections.IEnumerable)Get(m, "hits"))
                    {
                        string key = (string)h.GetType().GetField("key").GetValue(h);
                        if (key != null && key.StartsWith("theme:", StringComparison.Ordinal)) themeHits++;
                    }
                    // 39 套 = 9 行整 + 最后一行 3 张，滚到底看到的是 4+4+3 = 11~12 张
                    Ok("滚过之后只留窗口里的那几张", themeHits >= 11 && themeHits <= 12, themeHits + " 张");

                    // v3.6.0：第 5 颗「预览通知」——2 列 × 3 行 6 个 chip，命中区跟着绘制位置走
                    Call(m, "OpenItem", (int)BubbleAction.Preview);
                    int pExtra = (int)Call(m, "S", (float)BubbleMenuForm.ExtraHDip(BubbleAction.Preview));
                    Set(m, "animating", false);
                    Set(m, "openExtra", (double)pExtra);
                    Set(m, "openTo", (double)pExtra);
                    Set(m, "closeExtra", 0.0);
                    Set(m, "closeTo", 0.0);
                    Call(m, "LayoutWindow");
                    var pItem = (Rectangle)Call(m, "ItemRect", (int)BubbleAction.Preview);
                    var pChips = new Rectangle[BubbleMenuForm.PreviewKeys.Length];
                    for (int i = 0; i < pChips.Length; i++)
                        pChips[i] = (Rectangle)Call(m, "PreviewChipRect", pItem, i);
                    var pKeys = new System.Collections.Generic.List<string>();
                    foreach (object h in (System.Collections.IEnumerable)Get(m, "hits"))
                        pKeys.Add((string)h.GetType().GetField("key").GetValue(h));
                    bool pKeyOk = pKeys.Count == 6;
                    for (int i = 0; pKeyOk && i < 6; i++)
                        pKeyOk = pKeys[i] == "preview:" + BubbleMenuForm.PreviewKeys[i];
                    bool pChipOk = pItem.Width == s(246) && pItem.Height == s(40) + pExtra
                        && pChips[0].X == pItem.X + s(12) && pChips[0].Y == pItem.Y + s(62)
                        && pChips[0].Height == s(42)
                        && pChips[1].Y == pChips[0].Y && pChips[1].X == pChips[0].Right + s(8)
                        && pChips[2].X == pChips[0].X && pChips[2].Y == pChips[0].Y + s(50)
                        && pChips[5].Y == pChips[2].Y + s(50) && pChips[5].Bottom <= pItem.Bottom;
                    Ok("预览面板 2 列 × 3 行 6 个 chip，命中区和绘制位置一一对应",
                        pKeyOk && pChipOk,
                        pKeys.Count + " 个命中区 / chip0=" + pChips[0] + " chip5=" + pChips[5]);

                    // v3.6.0：第 6 颗「检查更新」——Widget 推状态，胶囊里的按钮跟着变
                    Call(m, "OpenItem", (int)BubbleAction.Update);
                    int uExtra = (int)Call(m, "S", (float)BubbleMenuForm.ExtraHDip(BubbleAction.Update));
                    Set(m, "animating", false);
                    Set(m, "openExtra", (double)uExtra);
                    Set(m, "openTo", (double)uExtra);
                    Set(m, "closeExtra", 0.0);
                    Set(m, "closeTo", 0.0);
                    Call(m, "LayoutWindow");
                    Func<string[]> uKeys = delegate
                    {
                        var ks = new System.Collections.Generic.List<string>();
                        foreach (object h in (System.Collections.IEnumerable)Get(m, "hits"))
                            ks.Add((string)h.GetType().GetField("key").GetValue(h));
                        return ks.ToArray();
                    };
                    bool idleHasCheck = Array.IndexOf(uKeys(), "update:check") >= 0
                        && Array.IndexOf(uKeys(), "update:page") >= 0;
                    m.SetUpdateState(UpdatePhase.Checking, "", "正在检查", "", 0, false);
                    bool checking = m.CurrentUpdatePhase == UpdatePhase.Checking
                        && Array.IndexOf(uKeys(), "update:check") < 0;
                    m.SetUpdateState(UpdatePhase.Available, "3.6.1", "有新版本", "1. 预览通知\n2. 磨砂玻璃", 0, true);
                    bool avail = m.CurrentUpdatePhase == UpdatePhase.Available
                        && Array.IndexOf(uKeys(), "update:apply") >= 0;
                    m.SetUpdateState(UpdatePhase.Downloading, "3.6.1", "", "", 42, true);
                    bool downloading = m.CurrentUpdatePhase == UpdatePhase.Downloading
                        && Array.IndexOf(uKeys(), "update:check") < 0 && Array.IndexOf(uKeys(), "update:apply") < 0;
                    m.SetUpdateState(UpdatePhase.Ready, "3.6.1", "", "", 100, true);
                    bool ready = m.CurrentUpdatePhase == UpdatePhase.Ready
                        && Array.IndexOf(uKeys(), "update:apply") >= 0;
                    m.SetUpdateState(UpdatePhase.UpToDate, "3.6.0", "已经是最新", "", 0, false);
                    bool upToDate = m.CurrentUpdatePhase == UpdatePhase.UpToDate
                        && Array.IndexOf(uKeys(), "update:check") >= 0;
                    m.SetUpdateState(UpdatePhase.Error, "", "网络不通", "", 0, false);
                    bool errored = m.CurrentUpdatePhase == UpdatePhase.Error
                        && Array.IndexOf(uKeys(), "update:check") >= 0;
                    Ok("检查更新面板：七个状态对应的按钮（检查 / 立即更新 / 链接）都对",
                        idleHasCheck && checking && avail && downloading && ready && upToDate && errored,
                        "idle=" + idleHasCheck + " checking=" + checking + " available=" + avail
                        + " downloading=" + downloading + " ready=" + ready + " upToDate=" + upToDate + " error=" + errored);
                }
                m.CloseMenu();
            }

            // 浅色主题：胶囊也跟着变浅（主题统一）
            var ls = new Settings();
            ls.lark.enabled = false;
            ls.calendarEnabled = false;
            ls.ui.theme = "bai-yudu";
            ls.ui.alwaysOnTop = true;
            using (var m = new BubbleMenuForm(ls, Themes.Get("bai-yudu"), card, true, null, null, null, null, null))
            {
                Set(m, "openedAt", Environment.TickCount - 5000);
                var rects = (Rectangle[])Get(m, "pillRects");
                using (var bmp = new Bitmap(m.Width, m.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                        Call(m, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, m.Width, m.Height)));
                    Color center = bmp.GetPixel(rects[0].Left + rects[0].Width / 2, rects[0].Top + rects[0].Height / 2);
                    Ok("浅色主题下胶囊也是浅色",
                        BubbleMenuForm.Luminance(center) > 0.6 && center.A > 200,
                        fmt(center) + " 亮度 " + BubbleMenuForm.Luminance(center).ToString("0.00"));
                }
                m.CloseMenu();
            }

            // v3.2：气泡改成主题色细边、去掉投影，关闭时先淡出再关窗
            {
                var ds = new Settings();
                ds.lark.enabled = false;
                ds.calendarEnabled = false;
                ds.ui.theme = "midnight";
                ds.ui.alwaysOnTop = true;
                var m = new BubbleMenuForm(ds, Themes.Get("midnight"), card, true, null, null, null, null, null);
                try
                {
                    Set(m, "openedAt", Environment.TickCount - 5000);
                    float sc = (float)Get(m, "scale");
                    Func<float, int> s = delegate(float d) { return (int)Math.Round(d * sc); };
                    var rects = (Rectangle[])Get(m, "pillRects");
                    int cx = rects[0].Left + rects[0].Width / 2;
                    int cy = rects[0].Top + rects[0].Height / 2;
                    Color accent = Themes.Get("midnight").accent;
                    Color edge, center;
                    using (var bmp = new Bitmap(m.Width, m.Height))
                    {
                        using (var g = Graphics.FromImage(bmp))
                            Call(m, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, m.Width, m.Height)));
                        edge = bmp.GetPixel(cx, rects[0].Top);
                        center = bmp.GetPixel(cx, cy);
                        Color under = bmp.GetPixel(cx, Math.Min(m.Height - 1, rects[0].Bottom + s(6)));
                        // 边线是「主题色按透明度压到底色上」的混色：比胶囊内部更偏主题色（午夜蓝 → 更蓝）
                        Ok("气泡有一圈主题色细边",
                            edge.A > 240 && (edge.B - edge.R) >= (center.B - center.R) + 12
                            && Distance(edge, accent) < Distance(center, accent),
                            "边 " + fmt(edge) + " / 内部 " + fmt(center) + " / 主题色 " + fmt(accent));
                        Ok("气泡不再画投影", under.A < 12, "胶囊下方 6 DIP 的像素 a=" + under.A);
                    }

                    // 淡出：退场 1/4 处同一位置的透明度要明显掉下来
                    Set(m, "closingForm", true);
                    Set(m, "exitAt", Environment.TickCount - BubbleMenuForm.ExitMs / 4);
                    int halfA;
                    using (var bmp = new Bitmap(m.Width, m.Height))
                    {
                        using (var g = Graphics.FromImage(bmp))
                            Call(m, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, m.Width, m.Height)));
                        halfA = bmp.GetPixel(cx, cy).A;
                    }
                    Ok("收起时气泡整体淡出", halfA > 20 && halfA < center.A * 0.8,
                        "a " + center.A + " → " + halfA);

                    // 退场走完：StepAnim 自己把窗口关掉
                    Set(m, "exitAt", Environment.TickCount - BubbleMenuForm.ExitMs - 10);
                    Call(m, "StepAnim");
                    Ok("淡出走完才关窗口", m.IsDisposed, "disposed=" + m.IsDisposed);
                }
                finally { if (!m.IsDisposed) m.Dispose(); }
            }

            // 日程提醒常驻时，点小卡要能展开大卡
            var ws = new Settings();
            ws.lark.enabled = false;
            ws.calendarEnabled = false;
            ws.ui.edge = "free";
            using (var form = new WidgetForm(new SettingsStore(), ws))
            {
                Set(form, "collapsed", true);
                Set(form, "reminding", true);
                Set(form, "remindEvent", new ReminderEvent
                {
                    id = "probe-expand",
                    title = "产品评审会",
                    source = "local",
                    start = Reminders.FormatStart(DateTime.Now.AddMinutes(-2)),
                    end = Reminders.FormatStart(DateTime.Now.AddMinutes(28))
                });
                Call(form, "SetCollapsed", false);
                bool animating = (bool)Get(form, "animating");
                int toW = (int)Get(form, "animToW");
                Ok("提醒期间点小卡能展开大卡",
                    animating && toW == (int)Call(form, "S", 280f),
                    "animating=" + animating + " 目标宽 " + toW);
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        // ---- 5i2. v3.6.0 主题背景磨砂玻璃（卡片 / 纸条面板 / 胶囊气泡）----
        {
            // 1) 卡片：bgStyle=glass 时 Frost 画半透明底 + 噪点；solid 时整块不透明
            Func<string, Settings> widgetSettings = delegate(string style)
            {
                var ws = new Settings();
                ws.lark.enabled = false;
                ws.calendarEnabled = false;
                ws.ui.edge = "free";
                ws.ui.theme = "midnight";
                ws.ui.cardGlow = "none";
                ws.ui.reducedMotion = true;
                ws.ui.bgStyle = style;
                return ws;
            };
            int solidSoft = 0, solidHard = 0, glassSoft = 0, glassHard = 0;
            using (var form = new WidgetForm(new SettingsStore(), widgetSettings("solid")))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Call(form, "Recompute");
                using (var bmp = RenderForm(form, 420, 282))
                    AlphaSpread(bmp, new Rectangle(0, 0, 420, 282), out solidSoft, out solidHard);
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
            using (var form = new WidgetForm(new SettingsStore(), widgetSettings("glass")))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Call(form, "Recompute");
                using (var bmp = RenderForm(form, 420, 282))
                    AlphaSpread(bmp, new Rectangle(0, 0, 420, 282), out glassSoft, out glassHard);
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
            Ok("卡片「磨砂玻璃」背景是半透明的（实心背景整块不透明）",
                solidHard > solidSoft * 4 && glassSoft > glassHard * 2,
                "实心 硬/软=" + solidHard + "/" + solidSoft + "，玻璃 硬/软=" + glassHard + "/" + glassSoft);

            // 2) 纸条面板：同一套 Frost，面板底色也跟着变半透明
            var glassCard = new Rectangle(600, 200, 420, 282);
            Func<string, Settings> noteSettings = delegate(string style)
            {
                var ns = new Settings();
                ns.lark.enabled = false;
                ns.calendarEnabled = false;
                ns.note.style = "drawer";
                ns.note.id = "48372";
                ns.ui.theme = "midnight";
                ns.ui.bgStyle = style;
                return ns;
            };
            int nSolidSoft = 0, nSolidHard = 0, nGlassSoft = 0, nGlassHard = 0;
            using (var p = new NotePanelForm(noteSettings("solid"), Themes.Get("midnight"), glassCard, true, null, null, null, null, null))
            using (var bmp = RenderPanel(p))
                AlphaSpread(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), out nSolidSoft, out nSolidHard);
            using (var p = new NotePanelForm(noteSettings("glass"), Themes.Get("midnight"), glassCard, true, null, null, null, null, null))
            using (var bmp = RenderPanel(p))
                AlphaSpread(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), out nGlassSoft, out nGlassHard);
            Ok("纸条面板「磨砂玻璃」背景是半透明的（实心背景整块不透明）",
                nSolidHard > nSolidSoft * 4 && nGlassSoft > nGlassHard * 2,
                "实心 硬/软=" + nSolidHard + "/" + nSolidSoft + "，玻璃 硬/软=" + nGlassHard + "/" + nGlassSoft);

            // 3) 胶囊气泡：glass 时胶囊底色也是半透明
            int bSolidSoft = 0, bSolidHard = 0, bGlassSoft = 0, bGlassHard = 0;
            Func<string, Settings> bubbleSettings = delegate(string style)
            {
                var bset = new Settings();
                bset.lark.enabled = false;
                bset.calendarEnabled = false;
                bset.ui.theme = "midnight";
                bset.ui.alwaysOnTop = true;
                bset.ui.bgStyle = style;
                return bset;
            };
            Func<string, int[]> pillSpread = delegate(string style)
            {
                var bset = bubbleSettings(style);
                int soft = 0, hard = 0;
                using (var m = new BubbleMenuForm(bset, Themes.Get("midnight"), glassCard, true, null, null, null, null, null))
                {
                    Set(m, "openedAt", Environment.TickCount - 5000);
                    var prects = (Rectangle[])Get(m, "pillRects");
                    using (var bmp = RenderForm(m, m.Width, m.Height))
                        AlphaSpread(bmp, prects[0], out soft, out hard);
                    m.CloseMenu();
                }
                return new int[] { soft, hard };
            };
            var bSolid = pillSpread("solid");
            var bGlass = pillSpread("glass");
            bSolidSoft = bSolid[0]; bSolidHard = bSolid[1];
            bGlassSoft = bGlass[0]; bGlassHard = bGlass[1];
            Ok("胶囊气泡「磨砂玻璃」底色半透明（实心时不透明）",
                bSolidHard > bSolidSoft * 4 && bGlassSoft > bGlassHard * 2,
                "实心 硬/软=" + bSolidHard + "/" + bSolidSoft + "，玻璃 硬/软=" + bGlassHard + "/" + bGlassSoft);

            // 4) 展开「检查更新」胶囊会自动查一次；已经在检查时再展开不重复触发
            int checkCalls = 0;
            var us = bubbleSettings("solid");
            using (var um = new BubbleMenuForm(us, Themes.Get("midnight"), glassCard, true, null, null, null, null, null,
                null, null, delegate { checkCalls++; }, null))
            {
                Call(um, "OpenItem", (int)BubbleAction.Update);
                int afterFirst = checkCalls;
                um.SetUpdateState(UpdatePhase.Checking, "", "正在检查", "", 0, false);
                Call(um, "OpenItem", (int)BubbleAction.Update);     // 再点一次 = 收起
                Call(um, "OpenItem", (int)BubbleAction.Update);     // 再展开：正在检查，不重复触发
                Ok("展开「检查更新」胶囊自动查一次，正在检查时不重复触发",
                    afterFirst == 1 && checkCalls == 1, "第一次 " + afterFirst + " / 共 " + checkCalls);
                um.CloseMenu();
            }

            // 5) 「预览通知」chip → onPreview(kind)；Widget 造 id=preview 的临时日程，只放一遍
            string previewKind = null;
            var pvs = bubbleSettings("solid");
            using (var pm = new BubbleMenuForm(pvs, Themes.Get("midnight"), glassCard, true, null, null, null, null, null,
                null, delegate(string k) { previewKind = k; }, null, null))
            {
                Call(pm, "OpenItem", (int)BubbleAction.Preview);
                Call(pm, "RunHit", "preview:local");
                Ok("点预览 chip 把类型交给 Widget（onPreview）", previewKind == "local", "kind=" + previewKind);
            }

            var pv = new Settings();
            pv.lark.enabled = false;
            pv.calendarEnabled = false;
            pv.ui.edge = "free";
            pv.ui.theme = "midnight";
            pv.ui.reminderHoldSeconds = 6;
            pv.Normalize();
            using (var form = new WidgetForm(new SettingsStore(), pv))
            {
                Set(form, "scale", 1.5f);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Call(form, "Recompute");
                Call(form, "PreviewNotification", "off");
                var prevEv = (ReminderEvent)Get(form, "remindEvent");
                DateTime prevUntil = (DateTime)Get(form, "remindUntil");
                double prevSecs = (prevUntil - DateTime.Now).TotalSeconds;
                Ok("预览「下班提醒」造临时日程：id=preview、按最短停留退场（不常驻）",
                    prevEv != null && prevEv.id == "preview" && prevEv.source == "alert"
                    && (bool)Get(form, "reminding") && prevSecs > 2 && prevSecs <= 7,
                    "id=" + (prevEv == null ? "-" : prevEv.id) + " source=" + (prevEv == null ? "-" : prevEv.source)
                    + " 剩余 " + prevSecs.ToString("0.0") + "s");
                Ok("预览只放一遍：不写进用户日程", pv.events.Count == 0, "events=" + pv.events.Count);
                Call(form, "PreviewNotification", "lunch");
                var prevEv2 = (ReminderEvent)Get(form, "remindEvent");
                Ok("预览「午休提醒」换成休息类临时日程",
                    prevEv2 != null && prevEv2.id == "preview" && prevEv2.source == "break",
                    "id=" + (prevEv2 == null ? "-" : prevEv2.id) + " source=" + (prevEv2 == null ? "-" : prevEv2.source));
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        // ---- 5j. 胶囊菜单的点击路由 ----
        {
            var cs = new Settings();
            cs.lark.enabled = false;
            cs.calendarEnabled = false;
            cs.ui.theme = "midnight";
            cs.ui.alwaysOnTop = true;
            var card = new Rectangle(0, 300, 224, 68);

            string pickedTheme = null, addTitle = null, addPlace = null;
            bool? pinTo = null, clockTo = null;
            int addMin = -1, settingsTab = -1;

            Func<BubbleMenuForm> make = delegate
            {
                return new BubbleMenuForm(cs, Themes.Get("midnight"), card, true,
                    delegate(string id) { pickedTheme = id; },
                    delegate(bool on) { pinTo = on; },
                    delegate(bool inClock) { clockTo = inClock; },
                    delegate(string t, string p, int min) { addTitle = t; addPlace = p; addMin = min; },
                    delegate(int tab) { settingsTab = tab; });
            };
            Func<Rectangle, Point> mid = delegate(Rectangle r) { return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2); };
            Func<object, float> scaleOf = delegate(object o) { return (float)Get(o, "scale"); };
            Func<object, Rectangle, int> openAt = delegate(object form, Rectangle pill)
            {
                Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, mid(pill).X, mid(pill).Y, 0));
                return 0;
            };
            Action<object, BubbleAction> settle = delegate(object form, BubbleAction act)
            {
                int extra = (int)Call(form, "S", (float)BubbleMenuForm.ExtraHDip(act));
                Set(form, "animating", false);
                Set(form, "openExtra", (double)extra);
                Set(form, "openTo", (double)extra);
            };

            using (var m = make())
            {
                Set(m, "openedAt", Environment.TickCount - 5000);
                float sc = scaleOf(m);
                Func<float, int> s = delegate(float d) { return (int)Math.Round(d * sc); };
                var rects = (Rectangle[])Get(m, "pillRects");
                openAt(m, rects[1]);
                Ok("点胶囊原地长开", (int)Get(m, "activeIndex") == 1, "activeIndex=" + Get(m, "activeIndex"));
                settle(m, BubbleAction.Theme);
                var item = (Rectangle)Call(m, "ItemRect", 1);
                var swatches = (System.Collections.Generic.List<ThemeDef>)Get(m, "swatches");
                int chipW = s(44);
                int gapX = ((item.Width - s(24)) - chipW * 4) / 3;
                var chip = new Point(item.Left + s(12) + 2 * (chipW + gapX) + chipW / 2, item.Top + s(50) + s(13));
                Call(m, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, chip.X, chip.Y, 0));
                Ok("点色卡立刻换主题",
                    pickedTheme == swatches[2].id && (string)Get(m, "selectedTheme") == swatches[2].id,
                    "点到 " + pickedTheme + " / " + swatches[2].id);
            }

            using (var m = make())
            {
                Set(m, "openedAt", Environment.TickCount - 5000);
                var rects = (Rectangle[])Get(m, "pillRects");
                openAt(m, rects[1]);
                settle(m, BubbleAction.Theme);
                var head2 = (Rectangle)Call(m, "ItemRect", 2);
                openAt(m, head2);
                Ok("切换时旧的收、新的同时长开",
                    (int)Get(m, "closingIndex") == 1 && (int)Get(m, "activeIndex") == 2
                    && (double)Get(m, "closeFrom") > 0 && (double)Get(m, "openTo") > 0,
                    "closing=" + Get(m, "closingIndex") + " active=" + Get(m, "activeIndex"));
            }

            using (var m = make())
            {
                Set(m, "openedAt", Environment.TickCount - 5000);
                var rects = (Rectangle[])Get(m, "pillRects");
                openAt(m, rects[3]);
                Ok("点置顶直接切换（不放大）",
                    pinTo == false && (bool)Get(m, "pinned") == false && (int)Get(m, "activeIndex") == -1,
                    "onPin=" + pinTo + " pinned=" + Get(m, "pinned"));
            }

            using (var m = make())
            {
                Set(m, "openedAt", Environment.TickCount - 5000);
                float sc = scaleOf(m);
                Func<float, int> s = delegate(float d) { return (int)Math.Round(d * sc); };
                var rects = (Rectangle[])Get(m, "pillRects");
                openAt(m, rects[2]);
                settle(m, BubbleAction.Clock);
                var item = (Rectangle)Call(m, "ItemRect", 2);
                var row = new Point(item.Left + s(12) + s(60), item.Top + s(50) + s(20));
                Call(m, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, row.X, row.Y, 0));
                Ok("点上班打卡触发时钟动作", clockTo == true, "clockTo=" + clockTo);
            }

            using (var m = make())
            {
                Set(m, "openedAt", Environment.TickCount - 5000);
                float sc = scaleOf(m);
                Func<float, int> s = delegate(float d) { return (int)Math.Round(d * sc); };
                var rects = (Rectangle[])Get(m, "pillRects");
                openAt(m, rects[0]);
                settle(m, BubbleAction.Schedule);
                var item = (Rectangle)Call(m, "ItemRect", 0);
                var chip = new Point(item.Left + s(12) + s(30), item.Top + s(142) + s(22));
                Call(m, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, chip.X, chip.Y, 0));
                Ok("点 +15 分钟用面板里的标题建日程",
                    addMin == 15 && addTitle == "临时日程" && addPlace == "",
                    "min=" + addMin + " title=" + addTitle + " place=" + addPlace);
            }

            using (var m = make())
            {
                Set(m, "openedAt", Environment.TickCount - 5000);
                float sc = scaleOf(m);
                Func<float, int> s = delegate(float d) { return (int)Math.Round(d * sc); };
                var rects = (Rectangle[])Get(m, "pillRects");
                openAt(m, rects[0]);
                settle(m, BubbleAction.Schedule);
                var item = (Rectangle)Call(m, "ItemRect", 0);
                var save = new Point(item.Left + s(60), item.Top + s(196) + s(17));
                Call(m, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, save.X, save.Y, 0));
                Ok("点保存日程提交标题/地点", addMin == 30 && addTitle == "临时日程", "min=" + addMin);
            }

            using (var m = make())
            {
                Set(m, "openedAt", Environment.TickCount - 5000);
                float sc = scaleOf(m);
                Func<float, int> s = delegate(float d) { return (int)Math.Round(d * sc); };
                var rects = (Rectangle[])Get(m, "pillRects");
                openAt(m, rects[0]);
                settle(m, BubbleAction.Schedule);
                var item = (Rectangle)Call(m, "ItemRect", 0);
                var detail = new Point(item.Left + s(20), item.Top + s(244));
                Call(m, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, detail.X, detail.Y, 0));
                Ok("「详细编辑」落到提醒页", settingsTab == 2, "tab=" + settingsTab);
            }
        }

        // ---- 15. 多份日历订阅 + 晚同步补提醒 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.outlook.calendars.Add(new CalendarSource
            {
                id = "cal-a", name = "工作日历", source = "url",
                url = "https://example.com/work.ics", enabled = true, leadMinutes = 5
            });
            s.outlook.calendars.Add(new CalendarSource
            {
                id = "cal-b", name = "个人日历", source = "url",
                url = "https://example.com/me.ics", enabled = false, leadMinutes = 0
            });
            s.outlook.enabled = true;
            using (var dlg = new SettingsForm(new SettingsStore(), s))
            {
                var list = (CheckedListBox)Get(dlg, "lstCalendars");
                Ok("设置页能列出多份日历", list != null && list.Items.Count == 2 && list.CheckedItems.Count == 1,
                    list == null ? "没有列表" : (list.Items.Count + " 份 / 勾选 " + list.CheckedItems.Count));
                Ok("日历行显示名称和类型",
                    list != null && list.Items[0].ToString().Contains("工作日历") && list.Items[0].ToString().Contains("订阅"),
                    list == null ? "" : list.Items[0].ToString());
                var tabs = (TabControl)Get(dlg, "tabs");
                Ok("多日历后还是七个页签", tabs.TabPages.Count == 7, tabs.TabPages.Count + " 个");
            }
        }

        // 晚于开始时间才同步下来的 Outlook 日程，要补一次提醒（否则整场会都没提醒）
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.reducedMotion = true;
            s.ui.reminderEffect = "none";
            s.ui.reminderSpread = "card";
            s.SetStyleOf(Reminders.KindOutlook, new ReminderStyleDef("card", "circle", "theme"));
            s.Normalize();
            foreach (var f in s.schedules) { f.enabled = false; f.notify = false; }
            var ev = new ReminderEvent();
            ev.id = "ol:late-check";
            ev.source = "outlook";
            ev.title = "迟到的同步";
            ev.start = Reminders.FormatStart(DateTime.Now.AddMinutes(-5));
            ev.end = Reminders.FormatStart(DateTime.Now.AddMinutes(30));
            var cal = new CalendarSource
            {
                id = "cal-late", name = "工作日历", source = "url",
                url = "https://example.com/late.ics", enabled = true
            };
            cal.cached.Add(ev);
            s.outlook.calendars.Add(cal);
            s.outlook.enabled = true;
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Call(form, "CheckReminders");
                var shown = (ReminderEvent)Get(form, "remindEvent");
                Ok("同步晚于开始的日程会补提醒",
                    (bool)Get(form, "reminding") && shown != null && shown.title == "迟到的同步",
                    "reminding=" + Get(form, "reminding") + " event=" + (shown == null ? "null" : shown.title));
            }
        }

        // ---- 16. 工资页 + 工资面板：点击拉长、双击固定 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.salary.monthly = 21000m;
            s.salary.payday = 15;
            s.Normalize();
            using (var dlg = new SettingsForm(new SettingsStore(), s, 5))
            {
                var tabs = (TabControl)Get(dlg, "tabs");
                Ok("工资页就位、纸条是第 7 页", tabs.TabPages.Count == 7 && tabs.TabPages[5].Text == "工资" && tabs.TabPages[6].Text == "纸条",
                    tabs.TabPages.Count + " 页 / 第 6 页=" + tabs.TabPages[5].Text + " / 第 7 页=" + tabs.TabPages[6].Text);
                Ok("工资页能读写月薪和发薪日",
                    ((NumericUpDown)Get(dlg, "numSalary")).Value == 21000m
                    && ((NumericUpDown)Get(dlg, "numPayday")).Value == 15m,
                    Get(dlg, "numSalary") + " / " + Get(dlg, "numPayday"));
                Ok("工资页给出本月应出勤预览",
                    ((Label)Get(dlg, "lblSalaryPreview")).Text.Contains("本月应出勤")
                    && ((Label)Get(dlg, "lblSalaryPreview")).Text.Contains("日薪约"),
                    ((Label)Get(dlg, "lblSalaryPreview")).Text);
            }

            var seed = new CalendarSource
            {
                id = "cal-new", name = "新日历", source = "url", url = "https://example.com/a.ics",
                enabled = true, leadMinutes = 0
            };
            using (var dlg = new CalendarEditForm(seed, false, false))
            {
                ((TextBox)Get(dlg, "tbName")).Text = "项目日历";
                ((TextBox)Get(dlg, "tbAddress")).Text = "https://example.com/project.ics";
                ((NumericUpDown)Get(dlg, "numLead")).Value = 7m;
                Call(dlg, "Confirm");
                var result = dlg.Result;
                Ok("日历编辑弹窗能保存名称/地址/提前量",
                    result != null && result.id == "cal-new" && result.name == "项目日历"
                    && result.url == "https://example.com/project.ics" && result.leadMinutes == 7,
                    result == null ? "null" : result.name + " " + result.url + " " + result.leadMinutes);
            }

            var w = new Settings();
            w.lark.enabled = false;
            w.calendarEnabled = false;
            w.ui.edge = "free";
            w.ui.reducedMotion = true;
            w.ui.reminderEffect = "none";
            w.ui.autoCollapseSeconds = 0;
            w.salary.monthly = 21000m;
            w.salary.payday = 15;
            w.Normalize();
            foreach (var f in w.schedules) { f.enabled = false; f.notify = false; }
            using (var form = new WidgetForm(new SettingsStore(), w))
            {
                Set(form, "scale", 1.5f);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Call(form, "Recompute");
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, form.Width, form.Height)));
                }
                var r = (Rectangle)Get(form, "rectSalary");
                Ok("右上角有工资按钮", r.Width > 0 && r.Right <= form.Width,
                    r.ToString());

                int baseH = form.Height;
                int closedH = (int)Call(form, "get_ExpandedH");
                Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1,
                    r.X + r.Width / 2, r.Y + r.Height / 2, 0));
                Ok("点工资按钮开始拉长动画",
                    (bool)Get(form, "salaryOpen") && (bool)Get(form, "salaryAnimating"),
                    "open=" + Get(form, "salaryOpen") + " anim=" + Get(form, "salaryAnimating"));
                Call(form, "FinishSalaryAnimNow");
                Ok("动画结束后卡片变高",
                    (bool)Get(form, "salaryOpen") && form.Height > baseH + 100,
                    "高度 " + baseH + " -> " + form.Height);
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, form.Width, form.Height)));
                }
                Ok("工资面板能画出来且不再报错", form.Height > baseH, "高度 " + form.Height);

                Ok("工资按钮不再支持双击固定",
                    typeof(WidgetForm).GetField("salaryPinned", F | BindingFlags.DeclaredOnly) == null
                    && typeof(WidgetForm).GetMethod("ToggleSalaryPin", F | BindingFlags.DeclaredOnly) == null
                    && typeof(WidgetForm).GetMethod("SetSalaryPinned", F | BindingFlags.DeclaredOnly) == null
                    && typeof(WidgetForm).GetMethod("OnDoubleClick", F | BindingFlags.DeclaredOnly) == null
                    && typeof(UiSettings).GetProperty("salaryPinned") == null,
                    "固定字段 / 双击处理 / ui.salaryPinned 都应已移除");

                Call(form, "ToggleSalary");
                Ok("点一下开始收起动画",
                    (bool)Get(form, "salaryAnimating") && (bool)Get(form, "salaryOpen"),
                    "anim=" + Get(form, "salaryAnimating") + " open=" + Get(form, "salaryOpen"));
                Call(form, "FinishSalaryAnimNow");
                Ok("收起后高度回到关闭状态",
                    !(bool)Get(form, "salaryOpen") && form.Height == closedH,
                    "open=" + Get(form, "salaryOpen") + " 高度 " + form.Height + "/" + closedH);
            }
        }

        // ---- 17. 字体切换 + 主题预览不压字 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.ui.numberFont = "Courier New";
            s.ui.textFont = "Microsoft YaHei UI";
            s.Normalize();
            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                var big = (Font)Get(form, "fontBig");
                var status = (Font)Get(form, "fontStatus");
                Ok("卡片数字字体跟着设置走", big.FontFamily.Name == "Courier New", big.FontFamily.Name);
                Ok("卡片中文字体跟着设置走", status.FontFamily.Name == "Microsoft YaHei UI", status.FontFamily.Name);
                s.ui.numberFont = "Consolas";
                Call(form, "ApplySettings");
                var big2 = (Font)Get(form, "fontBig");
                Ok("改字体后卡片即时重建", big2.FontFamily.Name == "Consolas", big2.FontFamily.Name);
            }
            UiFont.Configure("", "Consolas");

            using (var sw = new ColorWheelForm.PreviewSwatch())
            {
                sw.Size = new Size(234, 138);
                using (var bmp = new Bitmap(234, 138))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        var mi = typeof(ColorWheelForm.PreviewSwatch).GetMethod("OnPaint", F);
                        mi.Invoke(sw, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 234, 138)) });
                    }
                    int clipped = 0;
                    for (int y = 132; y <= 135; y++)
                        for (int x = 10; x < 224; x++)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (c.R + c.G + c.B > 320) clipped++;
                        }
                    Ok("主题预览底部没有压字", clipped == 0, clipped + " 个亮像素");
                }
            }
        }

        // ---- 18. 小纸条：卡片入口、A/C 面板、设置页 ----
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.edge = "free";
            s.Normalize();
            s.note.AddContact("62015", "阿哲");
            s.note.Append(new NoteMessage("62015", s.note.id, "晚上的评审推迟到 8 点", NoteRelay.Now() - 60), true);
            s.note.Append(new NoteMessage(s.note.id, "62015", "收到，我改完就过去", NoteRelay.Now() - 50), false);

            using (var form = new WidgetForm(new SettingsStore(), s))
            {
                Set(form, "scale", 1.5f);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Call(form, "Recompute");
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, form.Width, form.Height)));
                }
                var noteRect = (Rectangle)Get(form, "rectNote");
                var salaryRect = (Rectangle)Get(form, "rectSalary");
                Ok("卡片右上角有小飞机入口（¥ 左边）",
                    noteRect.Width > 0 && noteRect.Right <= salaryRect.Left && noteRect.Right <= form.Width,
                    "note=" + noteRect + " salary=" + salaryRect);
                Ok("有未读时会记着未读数",
                    s.note.TotalUnread() > 0, "unread=" + s.note.TotalUnread());
                Set(form, "noteFlashAt", Environment.TickCount - 40);
                double rim = (double)Call(form, "NoteRimAlpha");
                Set(form, "noteFlashAt", 0);
                Ok("收到未读后边框闪光会被触发", rim > 0.3, "alpha=" + rim.ToString("0.00"));
            }

            var drawer = new Settings();
            drawer.lark.enabled = false;
            drawer.calendarEnabled = false;
            drawer.note.style = "drawer";
            var card = new Rectangle(600, 200, 420, 282);
            using (var p = new NotePanelForm(drawer, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                float dpiScale = (float)Get(p, "scale");
                int expDrawerW = (int)Math.Round(392f * dpiScale);
                int expCompactH = (int)Math.Round(124f * dpiScale);
                Ok("抽屉式未选对话时收成 392×124 DIP（不再空一大片）", p.Width == expDrawerW && p.Height == expCompactH,
                    p.Width + "x" + p.Height + "（期望 " + expDrawerW + "x" + expCompactH + "，dpi=" + dpiScale + "）");
                Call(p, "SelectContact", "62015", false);
                Ok("点开对话后长回 392×282 DIP",
                    Math.Abs((float)Get(p, "heightTo") - (float)Math.Round(282f * dpiScale)) <= 1,
                    "heightTo=" + Get(p, "heightTo") + " 期望 " + Math.Round(282f * dpiScale));
                Ok("面板贴着卡片一侧显示（左边没空间就开右边）",
                    (p.Right <= card.Left || p.Left >= card.Right) && p.Left >= 0,
                    "panel=" + p.Bounds + " card=" + card);
                Ok("默认在联系人列表状态", Get(p, "state").ToString() == "Chat", "state=" + Get(p, "state"));
                var msgFont = (Font)Get(p, "fMsg");
                var tinyFont = (Font)Get(p, "fTiny");
                Ok("聊天正文字号 ≥ 9pt、次要字 ≥ 7.5pt（150% 下可读）",
                    msgFont.Size >= 9f && tinyFont.Size >= 7.5f,
                    "msg=" + msgFont.Size + "pt tiny=" + tinyFont.Size + "pt");
            }

            // 面板尺寸只跟 DPI 走：卡片再窄（280 DIP）也不该把面板缩回去
            using (var p = new NotePanelForm(drawer, Themes.Get("midnight"), new Rectangle(600, 200, 280, 282), true,
                null, null, null, null, null))
            {
                float dpiScale = (float)Get(p, "scale");
                Ok("面板尺寸和卡片宽度无关（DIP 设计稿：宽 392、未选对话高 124）",
                    p.Width == (int)Math.Round(392f * dpiScale) && p.Height == (int)Math.Round(124f * dpiScale),
                    p.Width + "x" + p.Height);
            }

            // 面板里发送：选中联系人 → 写一句 → 回调拿到 id/文字，之后进历史态
            string sentTo = null, sentText = null;
            drawer.note.AddContact("62015", "阿哲");
            using (var p = new NotePanelForm(drawer, Themes.Get("midnight"), card, true,
                delegate(string to, string text) { sentTo = to; sentText = text; return true; },
                null, null, null, null))
            {
                Call(p, "SelectContact", "62015", false);
                Set(p, "composeText", "收到，我改完就过去");
                int stateAtBefore = (int)Get(p, "stateAt");
                Call(p, "SendCompose");
                Ok("面板发送把 id 和文字交给回调",
                    sentTo == "62015" && sentText == "收到，我改完就过去",
                    sentTo + " / " + sentText);
                Ok("发送后不再整块刷新：停在原来的聊天态、不重置逐行动画",
                    Get(p, "state").ToString() == "Chat" && (int)Get(p, "stateAt") == stateAtBefore,
                    "state=" + Get(p, "state") + " stateAt=" + Get(p, "stateAt") + "（发送前 " + stateAtBefore + "）");
                Ok("发送成功不再显示「已发出」",
                    string.IsNullOrEmpty((string)Get(p, "status")), "status=" + Get(p, "status"));
                Ok("发送后记下进入动画（旧消息上移 / 新消息进入）",
                    (int)Get(p, "sentAt") != 0, "sentAt=" + Get(p, "sentAt"));
            }

            // 编辑窗样式：圆角；改备注编辑窗保持普通输入框样式（用户澄清：要放大/居中的是联系人名字，不是编辑窗）
            var edit = new Settings();
            edit.lark.enabled = false;
            edit.calendarEnabled = false;
            edit.note.style = "columns";
            edit.note.id = "48372";
            edit.note.AddContact("62015", "阿哲");
            edit.note.AddContact("11788", "小满");
            using (var p = new NotePanelForm(edit, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                Call(p, "EditCompose");
                var ed = Get(p, "fieldEditor") as Form;
                Ok("聊天输入编辑窗是圆角（Region 已设，不再是直角矩形）",
                    ed != null && ed.Region != null, ed == null ? "没开" : "region=" + (ed.Region != null));
                bool shapeFits = false;
                if (ed != null && ed.Region != null)
                {
                    using (var gg = Graphics.FromHwnd(IntPtr.Zero))
                    {
                        var rb = ed.Region.GetBounds(gg);
                        shapeFits = Math.Abs(rb.Width - ed.ClientSize.Width) <= 1
                            && Math.Abs(rb.Height - ed.ClientSize.Height) <= 1;
                    }
                }
                Ok("编辑窗圆角 Region 和实际窗口一样大（底边描边不会被裁）", shapeFits,
                    ed == null ? "没开编辑窗" : "region/shapeFits=" + shapeFits + " client=" + ed.ClientSize);
                TextBox tb = FindTextBox(ed);
                Ok("聊天输入编辑窗字体跟面板正文同级（≥9pt）",
                    tb != null && tb.Font.Size >= 9f, tb == null ? "没找到 TextBox" : tb.Font.Size + "pt");
                Call(p, "CloseFieldEditor");

                Call(p, "EditRemark", "11788");
                var ed2 = Get(p, "fieldEditor") as Form;
                TextBox tb2 = FindTextBox(ed2);
                var fieldFont = (Font)Get(p, "fField");
                Ok("双击改备注：编辑窗是普通输入框样式（不放大、不居中）",
                    tb2 != null && tb2.TextAlign != HorizontalAlignment.Center
                    && Math.Abs(tb2.Font.Size - fieldFont.Size) <= 0.2f,
                    tb2 == null ? "没开编辑窗" : "align=" + tb2.TextAlign + " font=" + tb2.Font.Size + "pt / 普通输入 " + fieldFont.Size + "pt");
                var firstCapsule = (Rectangle)Call(p, "ContactRectAt", 0);
                Ok("改备注编辑窗和联系人名字一样大（没有变大）",
                    ed2 != null && Math.Abs(ed2.Width - firstCapsule.Width) <= 2 && Math.Abs(ed2.Height - firstCapsule.Height) <= 2,
                    ed2 == null ? "没开编辑窗" : "editor=" + ed2.Width + "x" + ed2.Height + " capsule=" + firstCapsule.Width + "x" + firstCapsule.Height);
                Call(p, "CloseFieldEditor");
            }

            // 改备注落盘：面板立刻改 + 回调交给 Widget；空备注 = 恢复显示 ID
            string renamedId = null, renamedText = null;
            using (var p = new NotePanelForm(edit, Themes.Get("midnight"), card, true, null, null, null, null, null,
                delegate(string id, string remark) { renamedId = id; renamedText = remark; }))
            {
                Call(p, "ApplyRemark", "11788", "  满哥  ");
                Ok("改备注：去掉首尾空格、面板立刻生效、回调交给 Widget",
                    edit.note.Contact("11788").remark == "满哥" && renamedId == "11788" && renamedText == "满哥",
                    "remark=" + edit.note.Contact("11788").remark + " cb=" + renamedId + "/" + renamedText);
                Call(p, "ApplyRemark", "11788", "一二三四五六七八九十");
                Ok("备注最多 8 个字（超了截断）",
                    edit.note.Contact("11788").remark.Length == NoteSettings.MaxRemark,
                    "len=" + edit.note.Contact("11788").remark.Length);
                Call(p, "ApplyRemark", "11788", "");
                Ok("备注清空后显示回 ID", edit.note.Contact("11788").Display() == "ID 11788",
                    "display=" + edit.note.Contact("11788").Display());
            }

            // 用户澄清的需求 4：联系人**名字本身**居中、字号大一号（不是双击后的编辑窗变大）
            var nameSet = new Settings();
            nameSet.lark.enabled = false;
            nameSet.calendarEnabled = false;
            nameSet.note.style = "drawer";
            nameSet.note.id = "48372";
            nameSet.note.AddContact("62015", "阿哲");
            nameSet.note.AddContact("11788", "小满");
            using (var p = new NotePanelForm(nameSet, Themes.Get("huang-liuli"), card, true, null, null, null, null, null))
            {
                float sc = (float)Get(p, "scale");
                var capsule = (Rectangle)Call(p, "ContactRectAt", 0);
                int left = -1, right = -1;
                using (var bmp = RenderPanel(p))
                {
                    int x0 = capsule.X + (int)Math.Round(4 * sc), x1 = Math.Min(bmp.Width, capsule.Right - (int)Math.Round(4 * sc));
                    int y0 = capsule.Y + (int)Math.Round(6 * sc), y1 = Math.Min(bmp.Height, capsule.Bottom - (int)Math.Round(6 * sc));
                    for (int x = Math.Max(0, x0); x < x1; x++)
                    {
                        int best = 0;
                        for (int y = Math.Max(0, y0); y < y1; y++) best = Math.Max(best, Brightness(bmp.GetPixel(x, y)));
                        if (best >= 120) { if (left < 0) left = x; right = x; }
                    }
                }
                Ok("联系人名字在胶囊里水平居中",
                    left >= 0 && Math.Abs((left + right) / 2.0 - (capsule.X + capsule.Width / 2.0)) <= Math.Max(4, (int)Math.Round(4 * sc)),
                    "text=" + left + ".." + right + " center=" + ((left + right) / 2.0) + " capsuleCenter=" + (capsule.X + capsule.Width / 2.0));
                var nameFont = (Font)Get(p, "fName");
                Ok("联系人名字字号比原来大一号（≥9.3pt）", nameFont.Size >= 9.3f, "name=" + nameFont.Size + "pt");
            }

            // 需求 5 / 6 的像素断言：聊天气泡文字垂直居中、整卡细边框（聊天页 + 添加页都要有）
            var pixSet = new Settings();
            pixSet.lark.enabled = false;
            pixSet.calendarEnabled = false;
            pixSet.note.style = "columns";
            pixSet.note.id = "48372";
            pixSet.note.AddContact("62015", "阿哲");
            pixSet.note.Append(new NoteMessage("62015", "48372", "测试", NoteRelay.Now()), true);
            using (var p = new NotePanelForm(pixSet, Themes.Get("huang-liuli"), card, true, null, null, null, null, null))
            {
                float sc = (float)Get(p, "scale");
                Call(p, "SelectContact", "62015", false);
                // 刚点开对话就要立刻不透明：以前有 180ms 整体淡入，浅色主题下像盖了一层白雾
                using (var bmp = RenderPanel(p))
                {
                    var b = (Rectangle)Call(p, "BodyRect");
                    int maxB = 0;
                    for (int y = Math.Max(0, b.Y); y < Math.Min(bmp.Height, b.Bottom); y++)
                        for (int x = Math.Max(0, b.X); x < Math.Min(bmp.Width, b.Right); x++)
                            maxB = Math.Max(maxB, Brightness(bmp.GetPixel(x, y)));
                    Ok("刚点开对话时内容立刻不透明（没有整体淡入的白雾）", maxB >= 150, "maxBrightness=" + maxB);
                }
                Set(p, "stateAt", Environment.TickCount - 800);   // 跳过状态切换的 180ms 淡入，离屏渲染时才不透明
                // 双栏聊天头部的名字也要能双击改备注（和左栏联系人名字一致）
                bool headerHit = false;
                var bodyHead = (Rectangle)Call(p, "BodyRect");
                foreach (var h in (System.Collections.IEnumerable)Get(p, "hits"))
                {
                    var ht = h.GetType();
                    if (ht.GetField("kind").GetValue(h).ToString() != "Contact") continue;
                    var hr = (Rectangle)ht.GetField("rect").GetValue(h);
                    var hk = (string)ht.GetField("key").GetValue(h);
                    if (hk == "62015" && hr.Y < bodyHead.Y + (int)Call(p, "S", 22f) && hr.Bottom <= bodyHead.Y + (int)Call(p, "S", 22f))
                        headerHit = true;
                }
                Ok("双栏聊天头部的名字也能双击改备注（有命中区）", headerHit, "headerHit=" + headerHit);
                using (var bmp = RenderPanel(p))
                {
                    var edge = bmp.GetPixel(bmp.Width / 2, 1);
                    var inside = bmp.GetPixel(bmp.Width / 2, Math.Min(bmp.Height - 2, (int)Math.Round(6 * sc)));
                    Ok("整卡细边框在聊天页画出来了", Brightness(edge) - Brightness(inside) >= 25,
                        "edge=" + fmt(edge) + " inside=" + fmt(inside));

                    // 双栏聊天第一条（收到的）气泡：x=body.X，y=body.Y+S(22)，高 S(34)
                    var body = (Rectangle)Call(p, "BodyRect");
                    int bubbleTop = body.Y + (int)Call(p, "S", 22f);
                    int bubbleH = (int)Call(p, "S", 34f);
                    int scanR = Math.Min(bmp.Width - 1, body.X + (int)Call(p, "S", 260f));
                    int top = -1, bottom = -1;
                    for (int y = Math.Max(1, bubbleTop); y < Math.Min(bmp.Height - 1, bubbleTop + bubbleH); y++)
                    {
                        int best = 0;
                        for (int x = body.X; x < scanR; x++) best = Math.Max(best, Brightness(bmp.GetPixel(x, y)));
                        if (best >= 120) { if (top < 0) top = y; bottom = y; }
                    }
                    bool centered = top >= 0 && Math.Abs((top + bottom) / 2.0 - (bubbleTop + bubbleH / 2.0)) <= Math.Max(3, (int)Math.Round(3 * sc));
                    Ok("双栏气泡里的文字垂直居中（不是贴在气泡上沿）", centered,
                        "text=" + top + ".." + bottom + " center=" + ((top + bottom) / 2.0) + " bubbleCenter=" + (bubbleTop + bubbleH / 2.0));
                }

                var stateType = typeof(NotePanelForm).GetNestedType("PanelState", BindingFlags.NonPublic);
                Call(p, "SetState", Enum.Parse(stateType, "Add"), false);
                Set(p, "heightNow", (float)Get(p, "heightTo"));
                using (var bmp = RenderPanel(p))
                {
                    var edge = bmp.GetPixel(bmp.Width / 2, 1);
                    var inside = bmp.GetPixel(bmp.Width / 2, Math.Min(bmp.Height - 2, (int)Math.Round(6 * sc)));
                    Ok("整卡细边框在「添加联系人」页也在（以前被整卡重填盖掉）",
                        Brightness(edge) - Brightness(inside) >= 25, "edge=" + fmt(edge) + " inside=" + fmt(inside));
                }
            }

            // 添加联系人：空 ID 点「查在线」/「添加为联系人」要给明确提示（3.3.0 之前点了完全没反应，
            // 而且编辑框是 ShowDialog 模态，打开后别的按钮全都点不动）
            using (var p = new NotePanelForm(drawer, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                Call(p, "ProbeAdd");
                string st = (string)Get(p, "status");
                Ok("空对话码点「查在线」会提示填 5 位字符", st != null && st.Contains("5 位字符"), "status=" + st);
                Call(p, "ConfirmAdd");
                st = (string)Get(p, "status");
                bool opened = (bool)Get(p, "editingField");
                Call(p, "CloseFieldEditor");
                Ok("空对话码点「创建对话」会提示并直接打开输入框",
                    st != null && st.Contains("5 位字符") && opened, "status=" + st + " editor=" + opened);
            }

            // 空输入发消息也要有提示，不要报「没发出去：检查中继连接」
            using (var p = new NotePanelForm(drawer, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                Set(p, "composeText", "");
                Call(p, "SendCompose");
                string st = (string)Get(p, "status");
                Ok("空输入点发送提示「写一句再发」", st != null && st.Contains("写一句"), "status=" + st);
            }

            drawer.note.AddContact("62015", "阿哲");
            drawer.note.AddConversation("项目组#1", "项目群");
            drawer.note.style = "columns";
            using (var p = new NotePanelForm(drawer, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                float sc = (float)Get(p, "scale");
                Ok("双栏式面板 470×298 DIP（按 DPI 缩放）",
                    p.Width == (int)Math.Round(470f * sc) && p.Height == (int)Math.Round(298f * sc),
                    p.Width + "x" + p.Height + "（期望 " + (int)Math.Round(470f * sc) + "x" + (int)Math.Round(298f * sc) + "）");
            }

            using (var dlg = new SettingsForm(new SettingsStore(), drawer, 6))
            {
                var tabs = (TabControl)Get(dlg, "tabs");
                Ok("设置第 7 页是「纸条」", tabs.TabPages.Count == 7 && tabs.TabPages[6].Text == "纸条",
                    tabs.TabPages.Count + " 页 / 第 7 页=" + tabs.TabPages[6].Text);
                Ok("纸条页有样式 / ID / 中继 / 对话控件",
                    Get(dlg, "rbNoteDrawer") != null && Get(dlg, "rbNoteColumns") != null
                    && Get(dlg, "lblNoteId") != null && Get(dlg, "tbNoteRelay") != null
                    && Get(dlg, "lstNoteContacts") != null,
                    "drawer/columns/id/relay/contacts");
                Ok("默认公共中继是 broker.hivemq.com:1883",
                    ((TextBox)Get(dlg, "tbNoteRelay")).Text == "broker.hivemq.com"
                    && ((NumericUpDown)Get(dlg, "numNotePort")).Value == 1883,
                    ((TextBox)Get(dlg, "tbNoteRelay")).Text + ":" + ((NumericUpDown)Get(dlg, "numNotePort")).Value);
                var noteList = (ListBox)Get(dlg, "lstNoteContacts");
                Ok("纸条页能列出对话（个人 ID + 对话码都在）", noteList.Items.Count == 2,
                    noteList.Items.Count + " 条");
                bool roomRow = false;
                foreach (var item in noteList.Items)
                    if (item.ToString().Contains("对话码 " + "项目组#1")) roomRow = true;
                Ok("对话码对话在设置页显示成「对话码 xxxxx」", roomRow, roomRow ? "ok" : "没找到对话码行");
            }

            // ---- 用户反馈 v3.5：① 固定班次工作日显示错（周五上班却显示休息）② 个人昵称 ----
            Ok("工作日界面顺序映射正确（周一→下标1 … 周日→下标0）",
                SettingsForm.WorkdayArrayIndex(0) == 1 && SettingsForm.WorkdayArrayIndex(4) == 5
                && SettingsForm.WorkdayArrayIndex(5) == 6 && SettingsForm.WorkdayArrayIndex(6) == 0,
                "周一=" + SettingsForm.WorkdayArrayIndex(0) + " 周五=" + SettingsForm.WorkdayArrayIndex(4)
                + " 周日=" + SettingsForm.WorkdayArrayIndex(6));

            var wd = new Settings();
            wd.lark.enabled = false;
            wd.calendarEnabled = false;
            wd.mode = "fixed";
            // 周日..周六：只有周一到周五上班（正确写法）
            wd.workdays = new bool[] { false, true, true, true, true, true, false };
            using (var dlg = new SettingsForm(new SettingsStore(), wd, 0))
            {
                var cbs = (CheckBox[])Get(dlg, "cbDays");
                bool uiOk = cbs[0].Checked && cbs[1].Checked && cbs[2].Checked && cbs[3].Checked && cbs[4].Checked
                    && !cbs[5].Checked && !cbs[6].Checked;
                Ok("设置页「工作日」按周一到周五正确勾选（修复前周五不勾、周六勾）", uiOk,
                    "周一..周日=" + cbs[0].Checked + cbs[1].Checked + cbs[2].Checked + cbs[3].Checked
                    + cbs[4].Checked + cbs[5].Checked + cbs[6].Checked);
                Call(dlg, "SetWorkdayPreset", 5);
                Ok("「周一~周五」快捷按钮能用",
                    cbs[0].Checked && cbs[4].Checked && !cbs[5].Checked && !cbs[6].Checked, "ok");
                for (int i = 0; i < 7; i++) cbs[i].Checked = i < 5;
                Call(dlg, "Save", false);
                DateTime fri = DateTime.Today;
                while (fri.DayOfWeek != DayOfWeek.Friday) fri = fri.AddDays(1);
                Ok("勾周一到周五保存后周五是工作日（修复前周五会变成休息日）",
                    wd.workdays[1] && wd.workdays[5] && !wd.workdays[6] && !wd.workdays[0]
                    && Policy.Build(wd, fri).isWorkday,
                    "数组=" + wd.workdays[0] + wd.workdays[1] + wd.workdays[2] + wd.workdays[3]
                    + wd.workdays[4] + wd.workdays[5] + wd.workdays[6] + " / 周五isWorkday=" + Policy.Build(wd, fri).isWorkday);
                var stFri = StatusBuilder.Build(wd, null, fri.Date.AddHours(10), Themes.Get("midnight"));
                Ok("固定班次下周五 10:00 显示「工作中」、预计下班 18:00（不再显示休息日）",
                    stFri.headline != "休息日" && stFri.sub != null && stFri.sub.Contains("18:00"),
                    "headline=" + stFri.headline + " sub=" + stFri.sub);
            }

            // 个人昵称：设置页能填；对话显示「备注 > 对方昵称 > ID」；群聊气泡带昵称
            var nk = new Settings();
            nk.lark.enabled = false;
            nk.calendarEnabled = false;
            nk.note.nickname = "小明";
            nk.note.AddContact("93183", "");
            nk.note.Contact("93183").nick = "阿哲";
            using (var dlg = new SettingsForm(new SettingsStore(), nk, 6))
            {
                var tbNick = Get(dlg, "tbNoteNick") as TextBox;
                Ok("纸条设置页有「昵称」输入框并带出已有昵称", tbNick != null && tbNick.Text == "小明",
                    tbNick == null ? "没有 tbNoteNick" : "text=" + tbNick.Text);
                if (tbNick != null) tbNick.Text = "  老王  ";
                Call(dlg, "Save", false);
                Ok("昵称保存时去首尾空白、最多 12 字",
                    nk.note.nickname == "老王" && NoteSettings.MaxNick == 12, "nickname=" + nk.note.nickname);
            }
            Ok("个人对话没备注时显示对方昵称（自动拿到的）",
                nk.note.Contact("93183").Display() == "阿哲", "display=" + nk.note.Contact("93183").Display());
            nk.note.Contact("93183").remark = "老哲";
            Ok("有本机备注时优先级更高（备注 > 昵称）",
                nk.note.Contact("93183").Display() == "老哲", "display=" + nk.note.Contact("93183").Display());

            // ---- 用户反馈 2026-11-13 ①添加对话 ②边框加粗 ③滚动 + 右键删除 ----

            // 需求 2：卡片边框稍微粗一点
            Ok("小纸条卡片边框加粗到 1.8 DIP（原来 1 DIP）", NotePanelForm.CardBorderDip >= 1.6f,
                NotePanelForm.CardBorderDip + " DIP");

            var bord = new Settings();
            bord.lark.enabled = false;
            bord.calendarEnabled = false;
            bord.note.style = "columns";
            bord.note.id = "48372";
            bord.note.AddContact("62015", "阿哲");
            using (var p = new NotePanelForm(bord, Themes.Get("huang-liuli"), card, true, null, null, null, null, null))
            {
                Call(p, "SelectContact", "62015", false);
                Set(p, "stateAt", Environment.TickCount - 800);
                float sc = (float)Get(p, "scale");
                using (var bmp = RenderPanel(p))
                {
                    int inside = Brightness(bmp.GetPixel(bmp.Width / 2, Math.Min(bmp.Height - 1, (int)Math.Round(18 * sc))));
                    int thick = 0;
                    for (int y = 0; y < Math.Min(10, bmp.Height); y++)
                    {
                        if (Math.Abs(Brightness(bmp.GetPixel(bmp.Width / 2, y)) - inside) >= 18) thick++;
                        else break;
                    }
                    Ok("边框像素厚度 ≥ 3 行（150% 下；原来 2 行）", thick >= 3, "thick=" + thick + "px inside=" + inside);
                }
            }

            // 需求 1：添加对话 —— 任意 5 位字符建「对话码」对话；5 位数字还是个人 1:1
            var conv = new Settings();
            conv.lark.enabled = false;
            conv.calendarEnabled = false;
            conv.note.style = "drawer";
            conv.note.id = "48372";
            string addedCode = null;
            string addedRemark = null;
            using (var p = new NotePanelForm(conv, Themes.Get("midnight"), card, true, null, null, null,
                delegate(string id, string remark) { addedCode = id; addedRemark = remark; return conv.note.AddConversation(id, remark); },
                null, null, null))
            {
                Call(p, "ConfirmAdd");
                string st = (string)Get(p, "status");
                bool opened = (bool)Get(p, "editingField");
                Call(p, "CloseFieldEditor");
                Ok("空对话码点「创建对话」会提示 5 位字符并打开输入框",
                    st != null && st.Contains("5 位字符") && opened, "status=" + st + " editor=" + opened);

                Set(p, "addId", "1234");
                Call(p, "ConfirmAdd");
                st = (string)Get(p, "status");
                Ok("4 位对话码建不了对话", st != null && st.Contains("5 位字符") && conv.note.contacts.Count == 0,
                    "status=" + st + " contacts=" + conv.note.contacts.Count);

                Set(p, "addId", "项目组#1");
                Set(p, "addRemark", "项目群");
                Call(p, "ConfirmAdd");
                Ok("输入任意 5 位字符就建出一个对话码对话",
                    addedCode == "项目组#1" && addedRemark == "项目群"
                    && conv.note.Contact("项目组#1") != null && conv.note.Contact("项目组#1").IsRoom,
                    "code=" + addedCode + " remark=" + addedRemark + " / "
                    + (conv.note.Contact("项目组#1") == null ? "没建成" : "isRoom=" + conv.note.Contact("项目组#1").IsRoom));
                Ok("建完直接进这个对话", Get(p, "state").ToString() == "Chat" && p.SelectedId == "项目组#1",
                    "state=" + Get(p, "state") + " selected=" + p.SelectedId);

                Set(p, "addId", "93183");
                Set(p, "addRemark", "");
                Call(p, "ConfirmAdd");
                Ok("5 位数字还是按个人 ID 建 1:1 对话",
                    conv.note.Contact("93183") != null && !conv.note.Contact("93183").IsRoom,
                    conv.note.Contact("93183") == null ? "没建成" : "isRoom=" + conv.note.Contact("93183").IsRoom);
            }

            // 对话码对话里保留每个人的 ID：气泡带发送者 ID、历史行左边显示 ID
            var roomSet = new Settings();
            roomSet.lark.enabled = false;
            roomSet.calendarEnabled = false;
            roomSet.note.style = "drawer";
            roomSet.note.id = "48372";
            roomSet.note.AddConversation("项目组#1", "项目群");
            roomSet.note.Append(new NoteMessage("93183", "项目组#1", "大家好", NoteRelay.Now()), true);
            using (var p = new NotePanelForm(roomSet, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                Call(p, "SelectContact", "项目组#1", false);
                var inc3 = new NoteMessage("93183", "项目组#1", "大家好", NoteRelay.Now());
                string bubble = (string)Call(p, "BubbleText", inc3, true);
                Ok("对话码对话的气泡带发送者 ID（方便直接按 ID 建 1:1）",
                    bubble == "ID 93183：大家好", "bubble=" + bubble);
                Ok("对话码对话的历史行左边显示发送者 ID", (string)Call(p, "WhoLabel", inc3) == "ID 93183",
                    "who=" + Call(p, "WhoLabel", inc3));
                Ok("自己发的在对话码对话里还是显示「你」",
                    (string)Call(p, "WhoLabel", new NoteMessage("48372", "项目组#1", "收到", NoteRelay.Now())) == "你",
                    "ok");
            }

            // v3.5：群聊里发送者昵称直接展示（消息带昵称 → 昵称；没有 → 退回 ID）
            var nickSet = new Settings();
            nickSet.lark.enabled = false;
            nickSet.calendarEnabled = false;
            nickSet.note.style = "drawer";
            nickSet.note.id = "48372";
            nickSet.note.nickname = "我";
            nickSet.note.AddConversation("项目组#1", "项目群");
            var nickMsg = new NoteMessage("93183", "项目组#1", "大家好", NoteRelay.Now(), "阿哲");
            nickSet.note.Append(nickMsg, true);
            using (var p = new NotePanelForm(nickSet, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                Call(p, "SelectContact", "项目组#1", false);
                Ok("群聊气泡直接显示发送者昵称（不用看 ID）",
                    (string)Call(p, "BubbleText", nickMsg, true) == "阿哲：大家好",
                    "bubble=" + Call(p, "BubbleText", nickMsg, true));
                Ok("群聊历史行左边显示发送者昵称", (string)Call(p, "WhoLabel", nickMsg) == "阿哲",
                    "who=" + Call(p, "WhoLabel", nickMsg));
                var noNick = new NoteMessage("11999", "项目组#1", "hi", NoteRelay.Now());
                Ok("没有昵称的消息退回显示发送者 ID",
                    (string)Call(p, "BubbleText", noNick, true) == "ID 11999：hi",
                    "bubble=" + Call(p, "BubbleText", noNick, true));
            }

            // 需求 3 + v3.6.0：对话多了能滚（抽屉式联系人 > 4 个时铺两排、一屏 8 个）；右键出现删除按钮

            // 3 个联系人：只铺一排，一屏 4 个、面板高还是 124 DIP
            var few = new Settings();
            few.lark.enabled = false;
            few.calendarEnabled = false;
            few.note.style = "drawer";
            few.note.id = "48372";
            for (int i = 0; i < 3; i++) few.note.AddConversation((10000 + i * 1111).ToString(), "对话" + i);
            using (var p = new NotePanelForm(few, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                float sc = (float)Get(p, "scale");
                var drawn3 = (System.Collections.IList)Get(p, "drawnContacts");
                var strip3 = (Rectangle)Call(p, "ContactsRect");
                Ok("抽屉式联系人 ≤ 4 个时只铺一排：一屏 4 个、面板高 124 DIP、对话条带 36 DIP",
                    p.VisibleContactCount == 4 && drawn3.Count == 3
                    && p.Height == (int)Math.Round(124f * sc)
                    && strip3.Height == (int)Math.Round(36f * sc),
                    "visible=" + p.VisibleContactCount + " drawn=" + drawn3.Count + " h=" + p.Height
                    + " strip=" + strip3.Height + "（期望 " + (int)Math.Round(124f * sc) + " / " + (int)Math.Round(36f * sc) + "）");
            }

            // 10 个联系人：铺两排、一屏 8 个；滚一格 / 滚到底 / 进聊天页缩回一排
            var many = new Settings();
            many.lark.enabled = false;
            many.calendarEnabled = false;
            many.note.style = "drawer";
            many.note.id = "48372";
            for (int i = 0; i < 10; i++) many.note.AddConversation((10000 + i * 1111).ToString(), "对话" + i);
            string deletedCb = null;
            using (var p = new NotePanelForm(many, Themes.Get("midnight"), card, true, null, null, null, null, null, null,
                delegate(string id) { deletedCb = id; }))
            {
                float sc = (float)Get(p, "scale");
                Func<int, int> sPx = delegate(int d) { return (int)Math.Round(d * sc); };
                var drawn = (System.Collections.IList)Get(p, "drawnContacts");
                var strip10 = (Rectangle)Call(p, "ContactsRect");
                Ok("抽屉式联系人 > 4 个时铺两排：一屏 8 个、面板高 158 DIP、对话条带 70 DIP",
                    p.VisibleContactCount == 8 && drawn.Count == 8 && p.Height == sPx(158)
                    && strip10.Height == sPx(70),
                    "visible=" + p.VisibleContactCount + " drawn=" + drawn.Count + " h=" + p.Height
                    + " strip=" + strip10.Height + "（期望 " + sPx(158) + " / " + sPx(70) + "）");

                var c0 = (Rectangle)Call(p, "ContactRectAt", 0);
                var c1 = (Rectangle)Call(p, "ContactRectAt", 1);
                var c4 = (Rectangle)Call(p, "ContactRectAt", 4);
                var c7 = (Rectangle)Call(p, "ContactRectAt", 7);
                Ok("两排按 4 列排：第 5 个回到第一列、纵向步进 34 DIP",
                    c4.Left == c0.Left && c4.Top - c0.Top == sPx(34)
                    && c1.Top == c0.Top && c1.Left > c0.Right
                    && c7.Left == c0.Left + 3 * (c1.Left - c0.Left) && c7.Top == c4.Top,
                    "c0=" + c0 + " c4=" + c4 + " c7=" + c7);

                Call(p, "ScrollContacts", 1);
                Ok("滚一格：从第 2 个对话开始显示",
                    p.ContactScroll == 1 && drawn.Count == 8 && ((NoteContact)drawn[0]).id == many.note.contacts[1].id,
                    "scroll=" + p.ContactScroll + " first=" + (drawn.Count > 0 ? ((NoteContact)drawn[0]).id : "-"));
                Call(p, "ScrollContacts", 99);
                Ok("滚到底 clamp 到「最后 8 个」",
                    p.ContactScroll == 2 && drawn.Count == 8
                    && ((NoteContact)drawn[0]).id == many.note.contacts[2].id
                    && ((NoteContact)drawn[7]).id == many.note.contacts[9].id,
                    "scroll=" + p.ContactScroll + " first=" + (drawn.Count > 0 ? ((NoteContact)drawn[0]).id : "-"));
                Call(p, "ScrollContacts", -99);
                Ok("滚回顶部", p.ContactScroll == 0 && ((NoteContact)drawn[0]).id == many.note.contacts[0].id,
                    "scroll=" + p.ContactScroll);

                // 只在对列表页铺两排：进了聊天页缩回一排，别挤掉聊天区
                Call(p, "SelectContact", many.note.contacts[1].id, false);
                Ok("进了聊天页联系人缩回一排（visible 8 → 4）",
                    Get(p, "state").ToString() == "Chat" && p.VisibleContactCount == 4,
                    "state=" + Get(p, "state") + " visible=" + p.VisibleContactCount);

                string victim = many.note.contacts[0].id;
                Call(p, "SelectContact", victim, true);
                var capsule = (Rectangle)Call(p, "ContactRectAt", 0);
                Call(p, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1,
                    capsule.X + capsule.Width / 2, capsule.Y + capsule.Height / 2, 0));
                bool hasDelete = false, hasContact = false;
                foreach (var h in (System.Collections.IEnumerable)Get(p, "hits"))
                {
                    var ht = h.GetType();
                    string kind = ht.GetField("kind").GetValue(h).ToString();
                    if (kind == "Delete") hasDelete = true;
                    if (kind == "Contact") hasContact = true;
                }
                Ok("右键对话后出现「删除」按钮命中区", hasDelete && p.DeleteId == victim && hasContact,
                    "delete=" + hasDelete + " contact=" + hasContact + " deleteId=" + p.DeleteId);
                var delRect = (Rectangle)Call(p, "DeleteButtonRect", capsule);
                Call(p, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1,
                    delRect.X + delRect.Width / 2, delRect.Y + delRect.Height / 2, 0));
                bool threadGone = true;
                foreach (var t in many.note.threads) if (t.contact == victim) threadGone = false;
                Ok("点「删除」真的删掉这个对话（联系人 + 本机线程一起去，并通知 Widget 落盘）",
                    many.note.Contact(victim) == null && threadGone && deletedCb == victim
                    && p.DeleteId == "" && many.note.contacts.Count == 9,
                    "contact=" + (many.note.Contact(victim) == null) + " threadGone=" + threadGone
                    + " cb=" + deletedCb + " contacts=" + many.note.contacts.Count + " deleteId=" + p.DeleteId);
            }

            var manyCols = new Settings();
            manyCols.lark.enabled = false;
            manyCols.calendarEnabled = false;
            manyCols.note.style = "columns";
            manyCols.note.id = "48372";
            for (int i = 0; i < 8; i++) manyCols.note.AddConversation((20000 + i * 1111).ToString(), "对话" + i);
            using (var p = new NotePanelForm(manyCols, Themes.Get("midnight"), card, true, null, null, null, null, null))
            {
                var drawn = (System.Collections.IList)Get(p, "drawnContacts");
                int vis = p.VisibleContactCount;
                Ok("双栏一屏放 5 行对话（给底部「＋ 新建对话」留位），多了能滚",
                    vis == 5 && vis < manyCols.note.contacts.Count,
                    "visible=" + vis + " total=" + manyCols.note.contacts.Count);
                Call(p, "ScrollContacts", 1);
                Ok("双栏也能滚（滚动窗口起点跟着动）", p.ContactScroll == 1, "scroll=" + p.ContactScroll);
                Call(p, "ScrollContacts", 99);
                Ok("双栏滚到底 clamp 到「最后一屏能看到第 8 个对话」",
                    p.ContactScroll == 3 && drawn.Count == 5 && ((NoteContact)drawn[4]).id == manyCols.note.contacts[7].id,
                    "scroll=" + p.ContactScroll + " last=" + (drawn.Count > 0 ? ((NoteContact)drawn[drawn.Count - 1]).id : "-"));
                Call(p, "ScrollContacts", -99);
                bool addHit = false;
                var pill = (Rectangle)Call(p, "ColumnsAddRect");
                foreach (var h in (System.Collections.IEnumerable)Get(p, "hits"))
                {
                    var ht = h.GetType();
                    if (ht.GetField("kind").GetValue(h).ToString() != "AddContact") continue;
                    if ((Rectangle)ht.GetField("rect").GetValue(h) == pill) addHit = true;
                }
                Ok("双栏左栏底部「＋ 新建对话」胶囊能点（命中区和绘制位置一致）", addHit,
                    addHit ? "ok" : "没命中区 pill=" + pill);
            }

            // v3.6.0：卡片页「＋ 新建对话」左边多一颗「昵称」胶囊，双击改本机昵称
            var nick = new Settings();
            nick.lark.enabled = false;
            nick.calendarEnabled = false;
            nick.note.style = "drawer";
            nick.note.id = "48372";
            nick.note.nickname = "阿哲";
            nick.note.AddContact("62015", "小满");
            string nickCb = null;
            using (var p = new NotePanelForm(nick, Themes.Get("midnight"), card, true, null, null, null, null, null, null, null,
                delegate(string n) { nickCb = n; }))
            {
                float sc = (float)Get(p, "scale");
                Func<int, int> s2 = delegate(int d) { return (int)Math.Round(d * sc); };
                var nickRect = (Rectangle)Call(p, "NickRect");
                var header = (Rectangle)Call(p, "HeaderRect");
                var addRect = new Rectangle(header.Right - s2(96), header.Y + s2(2), s2(96), s2(28));
                Ok("昵称胶囊 96×28 DIP，在「＋ 新建对话」左边 8 DIP（不压住按钮）",
                    nickRect.Width == s2(96) && nickRect.Height == s2(28)
                    && nickRect.Right == addRect.Left - s2(8)
                    && Rectangle.Intersect(nickRect, addRect).IsEmpty,
                    "nick=" + nickRect + " add=" + addRect);
                bool nickHit = false;
                foreach (var h in (System.Collections.IEnumerable)Get(p, "hits"))
                {
                    var ht = h.GetType();
                    if (ht.GetField("kind").GetValue(h).ToString() != "Nickname") continue;
                    if ((Rectangle)ht.GetField("rect").GetValue(h) == nickRect) nickHit = true;
                }
                Ok("昵称胶囊有命中区（HitKind.Nickname）", nickHit, nickHit ? "ok" : "没命中区");
                int nx = nickRect.X + nickRect.Width / 2, ny = nickRect.Y + nickRect.Height / 2;
                Call(p, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, nx, ny, 0));
                string nickStatus = (string)Get(p, "status");
                Ok("单击昵称胶囊只提示「双击可以改」",
                    nickStatus != null && nickStatus.Contains("双击"), "status=" + nickStatus);
                Call(p, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, nx, ny, 0));
                bool nickEditing = (bool)Get(p, "editingField");
                var nickEditor = Get(p, "fieldEditor") as Form;
                TextBox nickTb = FindTextBox(nickEditor);
                Ok("双击昵称胶囊打开编辑窗（带出当前昵称，最多 12 字）",
                    nickEditing && nickEditor != null && nickTb != null && nickTb.Text == "阿哲"
                    && nickTb.MaxLength == NoteSettings.MaxNick,
                    nickEditor == null ? "没开编辑窗" : "editing=" + nickEditing + " text=" + (nickTb == null ? "-" : nickTb.Text));
                Call(p, "CloseFieldEditor");
                Call(p, "ApplyNickname", "  老王  ");
                Ok("改昵称去掉首尾空白、面板立刻生效、回调交给 Widget",
                    nick.note.nickname == "老王" && nickCb == "老王",
                    "nickname=" + nick.note.nickname + " cb=" + nickCb);
                Call(p, "ApplyNickname", "一二三四五六七八九十十一十二十三");
                Ok("改昵称最多 12 个字",
                    nick.note.nickname.Length == 12 && nick.note.nickname == "一二三四五六七八九十十一",
                    nick.note.nickname.Length + "/" + nick.note.nickname);
            }
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