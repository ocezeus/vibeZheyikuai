using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 抽屉卡的实机出图：真窗口 + 真卡片 + 真抽屉，截卡片和抽屉那一块。
// 用法：DrawerShot.exe <left|right> [主题id]
// 只为了看效果，不改产品行为；WC_NO_SAVE=1 不会回写用户配置。
class DrawerShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().FullName);
            try { Console.WriteLine(ex.Message); } catch { }
            var inner = ex.InnerException;
            while (inner != null)
            {
                Console.WriteLine("INNER " + inner.GetType().FullName);
                try { Console.WriteLine(inner.Message); } catch { }
                try { Console.WriteLine(inner.StackTrace); } catch { }
                inner = inner.InnerException;
            }
            Environment.ExitCode = 99;
        }
    }

    static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        string side = args != null && args.Length > 0 ? args[0] : "right";
        string themeId = args != null && args.Length > 1 ? args[1] : "ivory";
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = side;
        s.ui.theme = themeId;
        s.ui.alwaysOnTop = true;
        s.ui.reminderSpread = "screen";
        s.ui.reminderSpreadStyle = "tape";
        s.ui.cardGlow = "classic";

        var ev = new ReminderEvent();
        ev.id = "probe-drawer";
        ev.title = "产品评审会 · Q4 排期";
        ev.source = "lark";
        ev.start = DateTime.Now.AddMinutes(10).ToString(Reminders.DateFormat);
        ev.end = DateTime.Now.AddMinutes(40).ToString(Reminders.DateFormat);
        ev.place = "3F · 云杉会议室";

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            form.Show();
            Application.DoEvents();
            Thread.Sleep(300);

            Set(form, "remindEvent", ev);
            Set(form, "reminding", true);
            Set(form, "tapeActive", true);
            Set(form, "remindUntil", DateTime.Now.AddMinutes(30));
            Set(form, "collapsed", true);
            Call(form, "Recompute");
            form.Invalidate();
            Application.DoEvents();
            Thread.Sleep(200);

            Call(form, "ShowDrawer");
            int delay = args != null && args.Length > 2 ? int.Parse(args[2]) : 1200;
            Thread.Sleep(delay);
            Application.DoEvents();

            var card = form.Bounds;
            int need = (int)Call(form, "S", (float)DrawerForm.WidthDip);
            int pad = 24;
            int x0 = side == "left" ? card.Left - pad : card.Left - need - pad;
            var shot = new Rectangle(x0, card.Top - pad,
                card.Width + need + pad * 2, card.Height + pad * 2);
            using (var bmp = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(shot.Left, shot.Top, 0, 0, new Size(shot.Width, shot.Height));
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
                Directory.CreateDirectory(dir);
                string outPath = Path.Combine(dir, "drawer-live-" + side + "-" + themeId + ".png");
                bmp.Save(outPath, ImageFormat.Png);
                Console.WriteLine("ok " + outPath + "  " + shot.Width + "x" + shot.Height);
            }

            var drawer = Get(form, "drawer");
            if (drawer != null)
                Console.WriteLine("drawer visible=" + ((Form)drawer).Visible + " bounds=" + ((Form)drawer).Bounds);
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
        Application.DoEvents();
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
