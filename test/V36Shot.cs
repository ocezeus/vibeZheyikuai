using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// v3.6.0 新增视觉的离屏出图：
//   1. 磨砂玻璃背景（深色 / 浅色主题的卡片，正面压在一块「桌面」背景上看透视）
//   2. 小纸条抽屉页：昵称胶囊 + 联系人多到两排
//   3. 右键气泡：6 颗胶囊、预览通知面板、检查更新的三种状态
// 用法：V36Shot.exe [输出目录]
class V36Shot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().FullName);
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 99;
        }
    }

    static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        if (args != null && args.Length > 0 && args[0] == "update-live")
        {
            UpdateLive();
            return;
        }
        string dir = args != null && args.Length > 0 ? args[0]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "v36");
        Directory.CreateDirectory(dir);

        GlassCards(dir);
        NotePages(dir);
        BubbleMenus(dir);
        SettingsPage(dir);
        PreviewFrames(dir);
        UpdateProbe(dir);
        TestUpdater();
        Console.WriteLine("ok " + dir);
    }

    // ---------------- 7. 更新脚本：真跑一遍「锁着 → 放开 → 覆盖」 ----------------

    static void TestUpdater()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "wc-updater-test");
        try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        Directory.CreateDirectory(tmp);
        string dst = Path.Combine(tmp, "app.exe");
        string src = Path.Combine(tmp, "new.exe");
        File.WriteAllText(dst, "OLD");
        File.WriteAllText(src, "NEW");
        string text = UpdateService.BuildUpdaterScript(dst, src)
            .Replace("start \"\" \"%DST%\"", "rem no-start")
            .Replace("pause >nul", "rem no-pause")
            .Replace("del \"%~f0\" >nul 2>nul", "rem keep-script");
        string script = Path.Combine(tmp, "apply.cmd");
        File.WriteAllText(script, text, new UTF8Encoding(false));

        // 先占住目标文件 2.5 秒（模拟旧进程还没退干净），脚本应该重试到锁放开再覆盖成功
        var th = new Thread(delegate()
        {
            try
            {
                using (var fs = new FileStream(dst, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Thread.Sleep(2500);
            }
            catch { }
        });
        th.IsBackground = true;
        th.Start();
        var p = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + script + "\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        p.WaitForExit(40000);
        th.Join(60000);
        string now = File.ReadAllText(dst);
        bool backup = File.Exists(dst + ".bak") && File.ReadAllText(dst + ".bak") == "OLD";
        Console.WriteLine("更新脚本: 覆盖=" + (now == "NEW") + " 备份=" + backup + " exit=" + p.ExitCode);
        try { Directory.Delete(tmp, true); } catch { }
    }

    // ---------------- 8. 真机链路：右键气泡「检查更新」从展开到拿到结果 ----------------

    static void UpdateLive()
    {
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.theme = "graphite";
        s.ui.edge = "free";
        s.ui.x = 60;
        s.ui.y = 60;
        s.ui.autoCollapseSeconds = 0;
        s.exceptions.Add(new HolidayEntry { date = DateTime.Today.ToString("yyyy-MM-dd"), kind = "off" });
        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            form.Show();
            Pump(300);
            Call(form, "OpenBubbleMenu");
            Pump(600);
            var menu = Get(form, "bubbleMenu") as BubbleMenuForm;
            Console.WriteLine("bubble menu: " + (menu == null ? "null" : "visible=" + menu.Visible));
            if (menu == null) return;
            Call(menu, "OpenItem", (int)BubbleAction.Update);
            Pump(400);
            // 展开更新胶囊会自动触发检查（Widget 后台线程 → BeginInvoke 推状态）
            UpdatePhase phase = UpdatePhase.Idle;
            int waited = 0;
            while (waited < 20000)
            {
                phase = menu.CurrentUpdatePhase;
                if (phase != UpdatePhase.Idle && phase != UpdatePhase.Checking) break;
                Pump(250);
                waited += 250;
            }
            Console.WriteLine("update phase=" + phase + " latest=" + (phase == UpdatePhase.UpToDate ? "== 当前版本" : "?")
                + " waitedMs=" + waited);
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
        Application.DoEvents();
    }

    static void Pump(int ms)
    {
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    // ---------------- 5. 预览通知：真的走一遍 StartReminder，抓提醒中的卡片 ----------------

    static void PreviewFrames(string dir)
    {
        string[] kinds = new string[] { "fixed", "lunch", "lark", "outlook", "local", "off" };
        foreach (string kind in kinds)
        {
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.theme = "graphite";
            s.ui.edge = "free";
            s.ui.opacity = 100;
            // 四类通知都走「只卡片」，别在探针里弹全屏窗
            s.ui.styleFixed = new ReminderStyleDef("card", "circle", ReminderStyles.Follow);
            s.ui.styleLark = new ReminderStyleDef("card", "circle", ReminderStyles.Follow);
            s.ui.styleLocal = new ReminderStyleDef("card", "circle", ReminderStyles.Follow);
            s.ui.styleOutlook = new ReminderStyleDef("card", "circle", ReminderStyles.Follow);
            using (var w = new WidgetForm(new SettingsStore(), s))
            {
                Set(w, "scale", 1.5f);
                w.MinimumSize = Size.Empty;
                w.Size = new Size(420, 282);
                Call(w, "Recompute");
                Call(w, "FinishAnimNow");
                Set(w, "collapsed", false);
                Call(w, "PreviewNotification", kind);
                // 停在入场动画中段，和手动预览时看到的一致
                Set(w, "remindAt", DateTime.Now.AddMilliseconds(-700));
                object ev = Get(w, "remindEvent");
                Console.WriteLine("preview " + kind + " -> event=" + (ev == null ? "null" : ev.GetType().Name)
                    + " reminding=" + Get(w, "reminding") + " until=" + Get(w, "remindUntil"));
                using (var bmp = new Bitmap(w.Width, w.Height, PixelFormat.Format32bppArgb))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(w, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w.Width, w.Height)));
                    bmp.Save(Path.Combine(dir, "preview-" + kind + ".png"), ImageFormat.Png);
                }
                Call(w, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }
    }

    // ---------------- 6. 更新检查：真的打一次 GitHub API ----------------

    static void UpdateProbe(string dir)
    {
        try
        {
            Console.WriteLine("更新检查: 当前版本 v" + AppInfo.Version);
            UpdateRelease rel = UpdateService.Check();
            Console.WriteLine("更新检查: 远端 tag=" + rel.tag + " version=" + rel.version
                + " asset=" + (rel.HasAsset ? rel.assetName : "(无)")
                + " notes=" + UpdateService.ShortNotes(rel.notes, 60));
            if (rel.HasAsset)
            {
                // 真下载一遍 Release 里的 exe 资产，验证「自动更新」拿得到完整文件
                string path = UpdateService.Download(rel, delegate(long done, long total)
                {
                    if (total > 0 && done >= total)
                        Console.WriteLine("下载完成: " + UpdateService.HumanSize(done));
                });
                string message;
                bool ok = UpdateService.VerifyDownloaded(path, rel, out message);
                var vi = FileVersionInfo.GetVersionInfo(path);
                Console.WriteLine("下载校验: ok=" + ok + " 文件=" + UpdateService.HumanSize(new FileInfo(path).Length)
                    + " FileVersion=" + vi.FileVersion + (string.IsNullOrEmpty(message) ? "" : " / " + message));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("更新检查失败（发布前 releases 为空是正常的）: " + ex.Message);
        }
        Console.WriteLine("IsNewer 3.6.1>3.6.0 = " + UpdateService.IsNewer("3.6.1", "3.6.0")
            + " / 3.6.0>3.6.0 = " + UpdateService.IsNewer("3.6.0", "3.6.0")
            + " / v3.5.9>3.6.0 = " + UpdateService.IsNewer("v3.5.9", "3.6.0")
            + " / 3.6>3.6.0 = " + UpdateService.IsNewer("3.6", "3.6.0")
            + " / tag(v3.7.0)=" + UpdateService.VersionFromTag("v3.7.0")
            + " / human(1234567)=" + UpdateService.HumanSize(1234567));
    }

    // ---------------- 4. 设置页「外观」：新的背景风格一栏 ----------------

    static void SettingsPage(string dir)
    {
        var store = new SettingsStore();
        var s = store.Load();
        s.ui.theme = "ivory";
        s.ui.bgStyle = "glass";
        using (var dlg = new SettingsForm(store, s))
        {
            dlg.CreateControl();
            dlg.Show();
            var tabs = (TabControl)Get(dlg, "tabs");
            tabs.SelectedIndex = 3;
            Application.DoEvents();
            System.Threading.Thread.Sleep(200);
            Application.DoEvents();
            using (var bmp = new Bitmap(dlg.Width, dlg.Height))
            {
                dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(Path.Combine(dir, "settings-appearance.png"), ImageFormat.Png);
                Console.WriteLine("ok settings-appearance.png  " + dlg.Width + "x" + dlg.Height);
            }
            dlg.Close();
        }
    }

    // ---------------- 1. 磨砂玻璃卡片 ----------------

    static void GlassCards(string dir)
    {
        foreach (var pair in new string[][] { new[] { "graphite", "dark" }, new[] { "ivory", "light" } })
        {
            string themeId = pair[0], tag = pair[1];
            foreach (string style in new[] { "solid", "glass" })
            {
                var s = new Settings();
                s.lark.enabled = false;
                s.calendarEnabled = false;
                s.ui.theme = themeId;
                s.ui.bgStyle = style;
                s.ui.edge = "free";
                s.ui.opacity = 100;
                s.events.Add(new ReminderEvent
                {
                    id = "local:shot",
                    source = "local",
                    title = "产品部周会",
                    start = Reminders.FormatStart(DateTime.Now.AddMinutes(-30)),
                    end = Reminders.FormatStart(DateTime.Now.AddMinutes(30)),
                    place = "3F 会议室"
                });
                using (var w = new WidgetForm(new SettingsStore(), s))
                {
                    Set(w, "scale", 1.5f);
                    w.MinimumSize = Size.Empty;
                    w.Size = new Size(420, 282);
                    Call(w, "Recompute");
                    Call(w, "FinishAnimNow");
                    Set(w, "collapsed", false);
                    using (var bmp = new Bitmap(w.Width + 160, w.Height + 160, PixelFormat.Format32bppArgb))
                    {
                        bmp.SetResolution(144, 144);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            PaintDesk(g, bmp.Width, bmp.Height);
                            using (var frame = new Bitmap(w.Width, w.Height, PixelFormat.Format32bppArgb))
                            {
                                frame.SetResolution(144, 144);
                                using (var fg = Graphics.FromImage(frame))
                                    Call(w, "OnPaint", new PaintEventArgs(fg, new Rectangle(0, 0, w.Width, w.Height)));
                                g.DrawImage(frame, 80, 80);
                            }
                        }
                        bmp.Save(Path.Combine(dir, "card-" + tag + "-" + style + ".png"), ImageFormat.Png);
                        Console.WriteLine("ok card-" + tag + "-" + style + ".png");
                    }
                    Call(w, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
                }
            }
        }
    }

    /// <summary>假桌面：斜向渐变色块 + 几个圆，用来观察磨砂玻璃透出的背景。</summary>
    static void PaintDesk(Graphics g, int w, int h)
    {
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, w, h),
            Color.FromArgb(32, 40, 58), Color.FromArgb(96, 72, 64), 35f))
            g.FillRectangle(br, new Rectangle(0, 0, w, h));
        using (var br = new SolidBrush(Color.FromArgb(120, 64, 132, 220)))
            g.FillEllipse(br, w * 0.52f, h * 0.08f, w * 0.5f, w * 0.5f);
        using (var br = new SolidBrush(Color.FromArgb(90, 232, 176, 96)))
            g.FillEllipse(br, w * 0.04f, h * 0.55f, w * 0.42f, w * 0.42f);
        using (var br = new SolidBrush(Color.FromArgb(70, 120, 220, 170)))
            g.FillEllipse(br, w * 0.30f, h * 0.30f, w * 0.30f, w * 0.30f);
    }

    // ---------------- 2. 小纸条抽屉页 ----------------

    static void NotePages(string dir)
    {
        foreach (var pair in new string[][] { new[] { "ivory", "light" }, new[] { "huang-liuli", "dark" } })
        {
            string themeId = pair[0], tag = pair[1];
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.theme = themeId;
            s.note.style = "drawer";
            s.note.id = "48372";
            s.note.nickname = "小明";
            s.note.AddContact("62015", "阿哲");
            s.note.AddContact("11788", "");
            s.note.Contact("11788").nick = "小满";
            s.note.AddContact("90554", "");
            s.note.Contact("90554").nick = "老周";
            s.note.AddContact("38102", "小赵");
            s.note.AddContact("55201", "阿凯");
            s.note.AddContact("71934", "老王");
            s.note.AddConversation("项目组#1", "项目群");
            var th = s.note.Thread("62015");
            th.unread = 2;
            long now = NoteRelay.Now();
            s.note.Append(new NoteMessage("62015", "48372", "下午 4 点，会议室 B", now - 3500), true);
            s.note.Append(new NoteMessage("48372", "62015", "收到，中午看", now - 2900), false);

            var card = new Rectangle(840, 90, 420, 282);
            using (var p = new NotePanelForm(s, Themes.Get(themeId), card, true, null, null, null, null, null))
            {
                RenderPanel(p, Path.Combine(dir, "note-drawer-two-rows-" + tag + ".png"));
                s.ui.bgStyle = "glass"; Call(p, "RefreshData");
                RenderPanel(p, Path.Combine(dir, "note-drawer-glass-" + tag + ".png")); s.ui.bgStyle = "solid"; Call(p, "RefreshData");

                // 昵称清空：胶囊显示「＋ 昵称」（虚线）
                s.note.nickname = "";
                Call(p, "RefreshData");
                RenderPanel(p, Path.Combine(dir, "note-drawer-nick-empty-" + tag + ".png"));
                s.note.nickname = "小明";

                // 只剩 3 个对话：回到一排、面板收窄
                while (s.note.contacts.Count > 3) s.note.RemoveContact(s.note.contacts[s.note.contacts.Count - 1].id);
                Call(p, "RefreshData");
                Set(p, "heightNow", (float)Get(p, "heightTo"));
                RenderPanel(p, Path.Combine(dir, "note-drawer-one-row-" + tag + ".png"));
            }
        }
    }

    static void RenderPanel(NotePanelForm p, string path)
    {
        int w = (int)Get(p, "winWidth");
        int h = (int)(float)Get(p, "heightNow");
        using (var bmp = new Bitmap(w, h))
        {
            bmp.SetResolution(144, 144);
            using (var g = Graphics.FromImage(bmp))
                Call(p, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("ok " + Path.GetFileName(path) + "  " + w + "x" + h);
        }
    }

    // ---------------- 3. 右键气泡 ----------------

    static void BubbleMenus(string dir)
    {
        foreach (var pair in new string[][] { new[] { "ivory", "light" }, new[] { "graphite", "dark" } })
        {
            string themeId = pair[0], tag = pair[1];
            var s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.ui.theme = themeId;
            s.ui.bgStyle = "glass";
            s.ui.alwaysOnTop = true;
            var card = new Rectangle(40, 300, 336, 102);

            // 收起：6 颗胶囊
            using (var m = new BubbleMenuForm(s, Themes.Get(themeId), card, true, null, null, null, null, null,
                null, null, null, null))
            {
                Set(m, "openedAt", Environment.TickCount - 6000);
                ShotMenu(m, Path.Combine(dir, "bubble-six-" + tag + ".png"));
            }

            // 预览通知面板
            using (var m = new BubbleMenuForm(s, Themes.Get(themeId), card, true, null, null, null, null, null,
                null, null, null, null))
            {
                OpenBubbleItem(m, (int)BubbleAction.Preview);
                ShotMenu(m, Path.Combine(dir, "bubble-preview-" + tag + ".png"));
            }

            // 检查更新：有新版本
            using (var m = new BubbleMenuForm(s, Themes.Get(themeId), card, true, null, null, null, null, null,
                null, null, null, null))
            {
                OpenBubbleItem(m, (int)BubbleAction.Update);
                Call(m, "SetUpdateState", UpdatePhase.Available, "3.6.1", "",
                    "· 主题背景新增磨砂玻璃\n· 小纸条两排联系人\n· 检查更新（全量替换）", 0, true);
                ShotMenu(m, Path.Combine(dir, "bubble-update-available-" + tag + ".png"));
            }

            // 检查更新：下载中
            using (var m = new BubbleMenuForm(s, Themes.Get(themeId), card, true, null, null, null, null, null,
                null, null, null, null))
            {
                OpenBubbleItem(m, (int)BubbleAction.Update);
                Call(m, "SetUpdateState", UpdatePhase.Downloading, "3.6.1", "", "", 46, true);
                ShotMenu(m, Path.Combine(dir, "bubble-update-downloading-" + tag + ".png"));
            }

            // 检查更新：已是最新
            using (var m = new BubbleMenuForm(s, Themes.Get(themeId), card, true, null, null, null, null, null,
                null, null, null, null))
            {
                OpenBubbleItem(m, (int)BubbleAction.Update);
                Call(m, "SetUpdateState", UpdatePhase.UpToDate, "3.6.0", "", "", 0, false);
                ShotMenu(m, Path.Combine(dir, "bubble-update-latest-" + tag + ".png"));
            }
        }
    }

    static void OpenBubbleItem(BubbleMenuForm m, int index)
    {
        Set(m, "openedAt", Environment.TickCount - 6000);
        Call(m, "OpenItem", index);
        int extra = (int)Call(m, "S", (float)BubbleMenuForm.ExtraHDip((BubbleAction)index));
        Set(m, "animating", false);
        Set(m, "openExtra", (double)extra);
        Set(m, "openTo", (double)extra);
        MethodInfo build = m.GetType().GetMethod("BuildHits", F);
        if (build != null) build.Invoke(m, null);
        Call(m, "LayoutWindow");
    }

    static void ShotMenu(BubbleMenuForm m, string path)
    {
        int pad = 60;
        using (var bmp = new Bitmap(m.Width + pad * 2, m.Height + pad * 2, PixelFormat.Format32bppArgb))
        {
            bmp.SetResolution(144, 144);
            using (var g = Graphics.FromImage(bmp))
            {
                PaintDesk(g, bmp.Width, bmp.Height);
                g.TranslateTransform(pad, pad);
                Call(m, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, m.Width, m.Height)));
            }
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("ok " + Path.GetFileName(path) + "  " + m.Width + "x" + m.Height);
        }
    }

    // ---------------- 反射小工具 ----------------

    static void Set(object o, string name, object v)
    {
        o.GetType().GetField(name, F).SetValue(o, v);
    }

    static object Get(object o, string name)
    {
        return o.GetType().GetField(name, F).GetValue(o);
    }

    static object Call(object o, string name, params object[] a)
    {
        MethodInfo found = null;
        foreach (var mi in o.GetType().GetMethods(F))
        {
            if (mi.Name != name) continue;
            if (mi.GetParameters().Length != a.Length) continue;
            found = mi;
            break;
        }
        if (found == null) throw new Exception("no method " + name + "/" + a.Length);
        return found.Invoke(o, a);
    }
}
