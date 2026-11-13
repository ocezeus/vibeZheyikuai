using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 警戒带的「干净」预览：用合成壁纸替换截屏底图，再调用真实 ScreenReminderForm 绘制。
// 只是为了出图，不改产品行为。
class TapeShotClean
{
    const int W = 1280, H = 720;
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        var s = new Settings();
        s.ui.reminderSpreadStyle = "tape";
        s.ui.reminderHoldSeconds = 20;

        var ev = new ReminderEvent();
        ev.id = "probe";
        ev.title = "产品评审会 · Q4 排期";
        ev.source = "lark";
        ev.start = DateTime.Now.AddMinutes(10).ToString(Reminders.DateFormat);
        ev.end = DateTime.Now.AddMinutes(40).ToString(Reminders.DateFormat);
        ev.place = "3F · 云杉会议室";
        ev.detail = "议题：Q4 排期 / 体验走查";

        var theme = Themes.Get("midnight");
        var screen = new Rectangle(0, 0, W, H);

        var f = new ScreenReminderForm(s, ev, theme, screen, new Point(640, 360), null);
        f.CreateControl();
        var h = f.Handle;

        var bg = MakeDesktop();
        Set(f, "backdrop", bg);
        int th = ScreenReminderForm.TapeBandHeight(1f);


        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "tape-clean");
        Directory.CreateDirectory(dir);

        int prev = 0;
        foreach (int ms in new int[] { 583, 2330 })
        {
            Thread.Sleep(ms - prev);
            prev = ms;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, W, H));
                bmp.Save(Path.Combine(dir, "f" + ms.ToString("0000") + ".png"), ImageFormat.Png);
            }
            Application.DoEvents();
        }
        Console.WriteLine("ok -> " + dir);
        f.CloseNow();
    }

    static void Set(object o, string name, object v)
    {
        o.GetType().GetField(name, F).SetValue(o, v);
    }

    static Bitmap MakeDesktop()
    {
        var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, W, H),
                Color.FromArgb(15, 20, 28), Color.FromArgb(9, 13, 19), 160f))
                g.FillRectangle(br, 0, 0, W, H);
            using (var br = new SolidBrush(Color.FromArgb(26, 44, 66)))
                g.FillEllipse(br, -120, -140, 620, 460);
            using (var br = new SolidBrush(Color.FromArgb(34, 26, 44)))
                g.FillEllipse(br, 880, -160, 560, 420);
            using (var br = new SolidBrush(Color.FromArgb(18, 36, 34)))
                g.FillEllipse(br, 700, 560, 700, 420);

            Window(g, 70, 92, 520, 330, false);
            Window(g, 790, 150, 420, 300, true);
            using (var br = new SolidBrush(Color.FromArgb(200, 8, 11, 16)))
                g.FillRectangle(br, 0, H - 44, W, 44);
        }
        return bmp;
    }

    static void Window(Graphics g, int x, int y, int w, int h, bool dark)
    {
        var r = new Rectangle(x, y, w, h);
        using (var p = Round(r, 10))
        using (var br = new SolidBrush(dark ? Color.FromArgb(235, 16, 22, 31) : Color.FromArgb(235, 237, 241, 248)))
            g.FillPath(br, p);
        using (var br = new SolidBrush(dark ? Color.FromArgb(255, 30, 38, 50) : Color.FromArgb(255, 226, 231, 240)))
            g.FillRectangle(br, x, y, w, 26);
        using (var br = new SolidBrush(Color.FromArgb(255, 226, 99, 92)))
            g.FillEllipse(br, x + 12, y + 9, 8, 8);
        using (var br = new SolidBrush(Color.FromArgb(255, 229, 178, 76)))
            g.FillEllipse(br, x + 27, y + 9, 8, 8);
        using (var br = new SolidBrush(Color.FromArgb(255, 89, 195, 106)))
            g.FillEllipse(br, x + 42, y + 9, 8, 8);
        var line = dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(30, 22, 30, 44);
        var line2 = dark ? Color.FromArgb(40, 120, 180, 255) : Color.FromArgb(46, 70, 120, 200);
        int ly = y + 60;
        int[] widths = new int[] { 300, 210, 340, 160, 260, 300 };
        for (int i = 0; i < widths.Length && ly < y + h - 20; i++)
        {
            using (var br = new SolidBrush(i % 2 == 0 ? line : line2))
                g.FillRectangle(br, x + 22, ly, widths[i], 8);
            ly += 22;
        }
    }

    static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
