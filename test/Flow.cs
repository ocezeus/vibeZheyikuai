using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 提醒卡动画的时间轴拼图：入场 → 覆盖 → 退场，一帧一帧看衔接
class Flow
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v)
    {
        var f = o.GetType().GetField(n, F);
        if (f == null) throw new Exception("no field " + n);
        f.SetValue(o, v);
    }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        // 时间点：入场、覆盖、退场
        double[] inT = new double[] { 60, 150, 260, 400, 620, 1100 };
        double[] outT = new double[] { 100, 330, 700 };
        int w = 420, h = 0;

        var frames = new Bitmap[inT.Length + outT.Length];
        for (int i = 0; i < inT.Length; i++) frames[i] = Frame(inT[i], false, out h);
        for (int i = 0; i < outT.Length; i++) frames[inT.Length + i] = Frame(outT[i], true, out h);

        int cols = 4, pad = 20, gapX = 14, gapY = 34;
        int rows = (frames.Length + cols - 1) / cols;
        int gw = pad * 2 + cols * w + (cols - 1) * gapX;
        int gh = pad * 2 + rows * (h + gapY);
        using (var canvas = new Bitmap(gw, gh))
        {
            canvas.SetResolution(144, 144);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(232, 235, 241))) g.FillRectangle(bg, 0, 0, gw, gh);
                using (var ink = new SolidBrush(Color.FromArgb(30, 36, 50)))
                using (var sub = new SolidBrush(Color.FromArgb(110, 120, 138)))
                using (var lbl = UiFont.Make(9f, FontStyle.Bold))
                using (var lb2 = UiFont.Make(8f))
                {
                    for (int i = 0; i < frames.Length; i++)
                    {
                        int col = i % cols, row = i / cols;
                        int x = pad + col * (w + gapX);
                        int y = pad + row * (h + gapY);
                        string tag = i < inT.Length ? "+" + inT[i].ToString("0") + "ms" : "退场 +" + outT[i - inT.Length].ToString("0") + "ms";
                        g.DrawString(tag, lbl, ink, x, y - 2);
                        g.DrawString(i < inT.Length ? "入场" : "退场", lb2, sub, x + 62, y + 1);
                        g.DrawImage(frames[i], x, y + 18);
                    }
                }
            }
            canvas.Save(Path.Combine(dir, "flow.png"));
        }
        Console.WriteLine("flow.png " + gw + "x" + gh + " 卡片高 " + h);
    }

    static Bitmap Frame(double t, bool exiting, out int height)
    {
        var store = new SettingsStore();
        var s = store.Load();
        s.ui.reducedMotion = false;
        s.ui.reminderEffect = "glow";
        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            Set(form, "reminding", true);
            var ev = new ReminderEvent();
            ev.title = "需求对齐会";
            ev.place = "位置 A";
            ev.detail = "带上排期";
            ev.source = "meeting";
            Set(form, "remindEvent", ev);
            form.MinimumSize = Size.Empty;
            height = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(420, height);
            if (exiting)
            {
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-3600));
                Set(form, "remindExiting", true);
                Set(form, "remindExitAt", DateTime.Now.AddMilliseconds(-t));
            }
            else
            {
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-t));
            }
            Call(form, "Recompute");
            var bmp = new Bitmap(420, height);
            bmp.SetResolution(144, 144);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(232, 235, 241));
                Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, height)));
            }
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            return bmp;
        }
    }
}
