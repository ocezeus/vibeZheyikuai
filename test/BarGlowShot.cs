using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 把卡片上的进度条 + 外发光放大出图，看「光的颜色」和「条子的颜色」对不对得上。
//   BarGlowShot.exe [主题id] [进度0..1] [hot] [expanded]
class BarGlowShot
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
            while (inner != null) { Console.WriteLine("INNER " + inner.GetType().FullName); try { Console.WriteLine(inner.Message); } catch { } inner = inner.InnerException; }
            Environment.ExitCode = 99;
        }
        Application.DoEvents();
    }

    static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string themeId = args.Length > 0 ? args[0] : "lv-kongque";
        double progress = args.Length > 1 ? double.Parse(args[1]) : 0.55;
        bool hot = args.Length > 2 && args[2] == "hot";
        bool expanded = args.Length > 3 && args[3] == "expanded";

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "free";
        s.ui.theme = themeId;
        s.ui.cardGlow = "classic";
        s.ui.reducedMotion = true;

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            Set(form, "scale", 2f);
            Set(form, "snapshot", new AttendanceSnapshot { ok = true, clockIn = DateTime.Today.AddHours(9) });
            Call(form, "Recompute");
            Set(form, "collapsed", !expanded);
            form.MinimumSize = Size.Empty;
            int w = (int)Call(form, "S", expanded ? 280f : 224f);
            int h = expanded
                ? (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null)
                : (int)Call(form, "S", 68f);
            form.Size = new Size(w, h);

            var st = Get(form, "status");
            st.GetType().GetProperty("progress").SetValue(st, progress, null);
            if (hot)
                st.GetType().GetProperty("targetIso").SetValue(st, DateTime.Now.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ss"), null);
            Set(form, "flowPhase", 0.5);

            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {

                using (var g = Graphics.FromImage(bmp))
                    Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
                string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
                System.IO.Directory.CreateDirectory(dir);
                string tag = themeId + "-" + progress.ToString("0.00") + (hot ? "-hot" : "") + (expanded ? "-big" : "");
                bmp.Save(System.IO.Path.Combine(dir, "bar-" + tag + ".png"), ImageFormat.Png);

                int barY = (int)Call(form, "S", expanded ? 102f : 59f);
                int barH = (int)Call(form, "S", expanded ? 4f : 3f);
                int top = Math.Max(0, barY - (int)Call(form, "S", 10f));
                int height = Math.Min(h - top, barH + (int)Call(form, "S", 20f));
                var crop = new Rectangle(0, top, w, height);
                using (var b2 = new Bitmap(crop.Width * 2, crop.Height * 2))
                {
                    using (var g2 = Graphics.FromImage(b2))
                    {
                        g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g2.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        g2.DrawImage(bmp, new Rectangle(0, 0, b2.Width, b2.Height), crop, GraphicsUnit.Pixel);
                    }
                    b2.Save(System.IO.Path.Combine(dir, "bar-zoom-" + tag + ".png"), ImageFormat.Png);
                }
                Console.WriteLine("ok bar-zoom-" + tag + ".png");
            }
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
