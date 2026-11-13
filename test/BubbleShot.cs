using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 胶囊菜单的实机出图：真窗口 + 真右键菜单，截卡片和菜单那一块。
// 用法：BubbleShot.exe [bubbles|theme|clock|schedule] [主题id]
// 只为了看效果，不改产品行为；WC_NO_SAVE=1 不会回写用户配置。
class BubbleShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

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
        Application.EnableVisualStyles();

        string state = args != null && args.Length > 0 ? args[0] : "bubbles";
        string themeId = args != null && args.Length > 1 ? args[1] : "midnight";
        if (state.StartsWith("offline-", StringComparison.Ordinal))
        {
            Offline(state.Substring(8), themeId);
            return;
        }

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "left";
        s.ui.theme = themeId;
        s.ui.alwaysOnTop = true;
        s.ui.x = 0;
        s.ui.y = 320;
        s.ui.autoCollapseSeconds = 0;
        s.ui.reminderSpread = "screen";
        s.ui.cardGlow = "classic";
        // 探针运行时正好撞上午休/晚休时段：把今天设成休息日，别让真提醒盖住菜单
        s.exceptions.Add(new HolidayEntry { date = DateTime.Today.ToString("yyyy-MM-dd"), kind = "off" });

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            form.Show();
            Application.DoEvents();
            Thread.Sleep(350);
            form.SetBounds(0, 320, form.Width, form.Height);
            Application.DoEvents();

            Call(form, "OpenBubbleMenu");
            Pump(800);

            var menu = Get(form, "bubbleMenu") as Form;
            int index = state == "theme" ? 1 : state == "clock" ? 2 : state == "schedule" ? 0 : -1;
            if (menu != null && index >= 0)
            {
                Call(menu, "OpenItem", index);
                Pump(900);
            }

            var cardRect = form.Bounds;
            Rectangle shot = cardRect;
            if (menu != null && menu.Visible)
            {
                shot = Rectangle.Union(cardRect, menu.Bounds);
                shot.Inflate(20, 20);
            }
            shot.Intersect(Screen.FromRectangle(cardRect).Bounds);

            using (var bmp = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(shot.Left, shot.Top, 0, 0, new Size(shot.Width, shot.Height));
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
                Directory.CreateDirectory(dir);
                string outPath = Path.Combine(dir, "bubble-live-" + state + "-" + themeId + ".png");
                bmp.Save(outPath, ImageFormat.Png);
                Console.WriteLine("ok " + outPath + "  " + shot.Width + "x" + shot.Height);
            }

            if (menu != null) Console.WriteLine("menu " + menu.Bounds + " visible=" + menu.Visible);
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
        Application.DoEvents();
    }

    /// <summary>锁屏 / 无桌面时也能看效果：把菜单按 1:1 画到一张浅色底上，直接存图。</summary>
    static void Offline(string state, string themeId)
    {
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.theme = themeId;
        s.ui.alwaysOnTop = true;
        var card = new Rectangle(40, 300, 336, 102);
        int index = state == "theme" || state == "theme2" ? 1 : state == "clock" ? 2 : state == "schedule" ? 0 : -1;
        using (var m = new BubbleMenuForm(s, Themes.Get(themeId), card, true, null, null, null, null, null))
        {
            Set(m, "openedAt", Environment.TickCount - 5000);
            if (index >= 0)
            {
                Call(m, "OpenItem", index);
                int extra = (int)Call(m, "S", (float)BubbleMenuForm.ExtraHDip((BubbleAction)index));
                Set(m, "animating", false);
                Set(m, "openExtra", (double)extra);
                Set(m, "openTo", (double)extra);
                if (state == "theme2")   // 往下滚两行，看裁剪和滚动条
                {
                    Set(m, "themeScroll", (int)Call(m, "S", 80f));
                    Call(m, "BuildHits");
                }
                Call(m, "LayoutWindow");
            }
            int pad = 70;
            using (var bmp = new Bitmap(m.Width + pad * 2, m.Height + pad * 2, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    using (var br = new LinearGradientBrush(new Rectangle(0, 0, bmp.Width, bmp.Height),
                        Color.FromArgb(242, 245, 250), Color.FromArgb(214, 222, 234), 120f))
                        g.FillRectangle(br, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    g.TranslateTransform(pad, pad);
                    Call(m, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, m.Width, m.Height)));
                }
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
                Directory.CreateDirectory(dir);
                string outPath = Path.Combine(dir, "bubble-offline-" + state + "-" + themeId + ".png");
                bmp.Save(outPath, ImageFormat.Png);
                Console.WriteLine("ok " + outPath + "  " + bmp.Width + "x" + bmp.Height);
            }
            m.CloseMenu();
        }
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
        var m = o.GetType().GetMethod(name, F);
        if (m == null) throw new Exception("no method " + name);
        return m.Invoke(o, a);
    }
}
