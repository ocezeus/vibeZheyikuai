using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 弧光的离屏出图：三套主题 × 纯色/磨砂玻璃 × 展开/收起，左边关、右边开，
// 卡片压在假「桌面」上，磨砂玻璃的透视和纯色的实底才看得出区别。
// 用法：ArcGlowShot.exe [输出目录]
class ArcGlowShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;

    [STAThread]
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().FullName + " " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 99;
        }
    }

    static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
        Application.EnableVisualStyles();
        string dir = args != null && args.Length > 0 ? args[0]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "arc");
        Directory.CreateDirectory(dir);
        foreach (string theme in new string[] { "midnight", "sunset", "ivory" })
            foreach (string bg in new string[] { "solid", "glass" })
            {
                Shot(dir, theme, bg, false);
                Shot(dir, theme, bg, true);
            }
        SettingsShot(dir, "midnight", "solid", true);
        SettingsShot(dir, "midnight", "glass", false);
        SettingsShot(dir, "ivory", "solid", false);
        Console.WriteLine("ok " + dir);
    }

    static Settings Config(string theme, string bg, bool collapsed)
    {
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.theme = theme;
        s.ui.bgStyle = bg;
        s.ui.edge = collapsed ? "right" : "free";
        s.ui.opacity = 100;
        return s;
    }

    static void Shot(string dir, string theme, string bg, bool collapsed)
    {
        Settings s = Config(theme, bg, collapsed);
        using (var w = new WidgetForm(new SettingsStore(), s))
        {
            Call(w, "Recompute");
            Set(w, "collapsed", collapsed);
            Call(w, "SyncBounds");
            Set(w, "flowPhase", 0.5);
            int cw = w.Width, ch = w.Height;
            Bitmap off = Capture(w, cw, ch);
            s.ui.arcGlow = true;
            Bitmap on = Capture(w, cw, ch);

            string tag = (collapsed ? "mini-" : "card-") + theme + "-" + bg;
            int pad = 20, gap = 24;
            using (var sheet = new Bitmap(cw * 2 + gap + pad * 2, ch + pad * 2 + 26, PixelFormat.Format32bppArgb))
            {
                sheet.SetResolution(144, 144);
                using (var g = Graphics.FromImage(sheet))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    PaintDesk(g, sheet.Width, sheet.Height);
                    using (var font = new Font("Segoe UI", 9f))
                    using (var ink = new SolidBrush(Color.FromArgb(220, 230, 240, 250)))
                    {
                        g.DrawString("弧光 关", font, ink, pad, 4);
                        g.DrawString("弧光 开", font, ink, pad + cw + gap, 4);
                    }
                    g.DrawImage(off, pad, 26);
                    g.DrawImage(on, pad + cw + gap, 26);
                }
                sheet.Save(Path.Combine(dir, tag + ".png"), ImageFormat.Png);
            }

            on.Save(Path.Combine(dir, tag + "-on.png"), ImageFormat.Png);
            using (var crop = on.Clone(new Rectangle(cw / 2, ch / 2, cw - cw / 2, ch - ch / 2), PixelFormat.Format32bppArgb))
                crop.Save(Path.Combine(dir, tag + "-zoom.png"), ImageFormat.Png);

            off.Dispose();
            on.Dispose();
            Console.WriteLine("ok " + tag + "  " + cw + "x" + ch);
            Call(w, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
    }

    static Bitmap Capture(WidgetForm w, int cw, int ch)
    {
        var bmp = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
        bmp.SetResolution(144, 144);
        using (var g = Graphics.FromImage(bmp))
            Call(w, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, cw, ch)));
        return bmp;
    }

    static void SettingsShot(string dir, string theme, string bg, bool on)
    {
        var store = new SettingsStore();
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.theme = theme;
        s.ui.bgStyle = bg;
        s.ui.arcGlow = on;
        using (var dlg = new SettingsForm(store, s))
        {
            dlg.CreateControl();
            dlg.Show();
            var tabs = (TabControl)Get(dlg, "tabs");
            tabs.SelectedIndex = 3;
            Application.DoEvents();
            var host = (Panel)Get(dlg, "contentHost");
            float k;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            host.AutoScrollPosition = new Point(0, (int)Math.Round(96 * k));
            Application.DoEvents();
            System.Threading.Thread.Sleep(150);
            Application.DoEvents();
            using (var bmp = new Bitmap(dlg.Width, dlg.Height))
            {
                dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                string name = "settings-" + theme + "-" + bg + (on ? "-on" : "-off") + ".png";
                bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
                Console.WriteLine("ok " + name + "  " + dlg.Width + "x" + dlg.Height);
            }
            dlg.Close();
        }
    }

    static object Get(object o, string name)
    {
        return o.GetType().GetField(name, F).GetValue(o);
    }

    static void PaintDesk(Graphics g, int w, int h)
    {
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, w, h),
            Color.FromArgb(32, 40, 58), Color.FromArgb(96, 72, 64), 35f))
            g.FillRectangle(br, new Rectangle(0, 0, w, h));
        using (var br = new SolidBrush(Color.FromArgb(120, 64, 132, 220)))
            g.FillEllipse(br, w * 0.52f, h * 0.08f, w * 0.5f, w * 0.5f);
        using (var br = new SolidBrush(Color.FromArgb(90, 232, 176, 96)))
            g.FillEllipse(br, w * 0.04f, h * 0.55f, w * 0.42f, h * 0.42f);
        using (var br = new SolidBrush(Color.FromArgb(70, 120, 220, 170)))
            g.FillEllipse(br, w * 0.30f, h * 0.30f, w * 0.30f, h * 0.30f);
    }

    static void Set(object o, string name, object v)
    {
        o.GetType().GetField(name, F).SetValue(o, v);
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
