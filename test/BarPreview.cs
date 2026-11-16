using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 进度条：普通状态 + 最后 30 分钟彩色态，再放大看发光
class BarPreview
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [STAThread]
    static void Main()
    {
        // 探针只读配置，绝不回写用户自己的设置
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        int w = 420;
        var cards = new Bitmap[3];
        int h = 0;
        cards[0] = Card(0.62, 180, out h);
        cards[1] = Card(0.90, 22, out h);
        cards[2] = Card(0.97, 5, out h);

        int pad = 24, gapY = 26;
        int cw = w + pad * 2;
        int ch = pad + (h + 34) * 3 + 40;
        using (var canvas = new Bitmap(cw, ch))
        {
            canvas.SetResolution(144, 144);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(232, 235, 241))) g.FillRectangle(bg, 0, 0, cw, ch);
                using (var ink = new SolidBrush(Color.FromArgb(30, 36, 50)))
                {
                    string[] caps = new string[] { "还剩 3 小时（62%）：走过的一段发光", "还剩 22 分钟：整条变彩色", "还剩 5 分钟：彩色 + 更亮的光" };
                    for (int i = 0; i < 3; i++)
                    {
                        int y = pad + i * (h + 34);
                        g.DrawString(caps[i], UiFont.Make(9.5f, FontStyle.Bold), ink, pad, y);
                        g.DrawImage(cards[i], pad, y + 22);
                    }
                }
            }
            canvas.Save(Path.Combine(dir, "bar.png"));
        }
        Console.WriteLine("bar.png " + cw + "x" + ch + " 卡片高 " + h);
    }

    static Bitmap Card(double progress, double left, out int height)
    {
        var store = new SettingsStore();
        var s = store.Load().Settings;
        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            Set(form, "reminding", false);
            form.MinimumSize = Size.Empty;
            height = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(420, height);
            Call(form, "Recompute");
            object status = Get(form, "status");
            status.GetType().GetProperty("progress").SetValue(status, progress, null);
            status.GetType().GetProperty("targetIso").SetValue(status,
                DateTime.Now.AddMinutes(left).ToString("s"), null);
            var bmp = new Bitmap(420, height);
            Console.WriteLine("  left=" + left + " targetIso=" + status.GetType().GetProperty("targetIso").GetValue(status, null) + " minutes=" + ((double)Call(form, "MinutesToOffWork")).ToString("0.0"));
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
