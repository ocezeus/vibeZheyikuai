using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 最后 60 分钟：分:秒.厘秒；同时看展开时各行错峰浮上来的过程
class Soon
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [STAThread]
    static void Main()
    {
        // 探针只读配置，绝不回写用户自己的设置
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        // 1) 三段剩余时间
        string[] labels = new string[] { "还剩 45 分钟", "还剩 12 分钟", "还剩 90 秒" };
        double[] mins = new double[] { 45, 12, 1.5 };
        var shots = new Bitmap[labels.Length];
        int h = 0;
        for (int i = 0; i < labels.Length; i++) shots[i] = Card(mins[i], -1, out h);
        Tile(dir, "soon.png", labels, shots, h, 3);

        // 2) 展开错峰：同一个卡片，改「已经过去多久」
        string[] stage = new string[] { "0ms", "70ms", "150ms", "240ms", "340ms", "460ms", "620ms" };
        int[] ms = new int[] { 0, 70, 150, 240, 340, 460, 620 };
        var frames = new Bitmap[stage.Length];
        for (int i = 0; i < stage.Length; i++) frames[i] = Card(90, ms[i], out h);
        Tile(dir, "expand.png", stage, frames, h, 4);

        Console.WriteLine("soon.png / expand.png 卡片高 " + h);
    }

    static void Tile(string dir, string name, string[] labels, Bitmap[] shots, int h, int perRow)
    {
        int w = 420, pad = 20, gap = 14;
        int cols = Math.Min(perRow, shots.Length);
        int rows = (shots.Length + cols - 1) / cols;
        int gw = pad * 2 + cols * w + (cols - 1) * gap;
        int gh = pad * 2 + rows * (h + 26 + gap) - gap;
        using (var canvas = new Bitmap(gw, gh))
        {
            canvas.SetResolution(144, 144);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(232, 235, 241))) g.FillRectangle(bg, 0, 0, gw, gh);
                using (var ink = new SolidBrush(Color.FromArgb(30, 36, 50)))
                using (var lbl = UiFont.Make(9.5f, FontStyle.Bold))
                {
                    for (int i = 0; i < shots.Length; i++)
                    {
                        int cx = pad + (i % cols) * (w + gap);
                        int cy = pad + (i / cols) * (h + 26 + gap);
                        g.DrawString(labels[i], lbl, ink, cx, cy);
                        g.DrawImage(shots[i], cx, cy + 22);
                    }
                }
            }
            canvas.Save(Path.Combine(dir, name));
        }
    }

    static Bitmap Card(double minutesToOff, int staggerMs, out int height)
    {
        var store = new SettingsStore();
        var s = store.Load();
        s.ui.reducedMotion = false;
        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(28);
        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            form.MinimumSize = Size.Empty;
            height = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(420, height);
            Call(form, "Recompute");
            if (minutesToOff > 0)
            {
                object st0 = form.GetType().GetField("status", F).GetValue(form);
                var target = DateTime.Now.AddMinutes(minutesToOff);
                st0.GetType().GetProperty("targetIso").SetValue(st0, target.ToString("s"), null);
                st0.GetType().GetProperty("big").SetValue(st0, Policy.Clock(target - DateTime.Now), null);
            }
            if (staggerMs >= 0)
            {
                Set(form, "expandAnim", true);
                Set(form, "expandStart", Environment.TickCount - staggerMs);
            }
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
