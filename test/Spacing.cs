using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 行距对照：同一个卡片，只改 RowGapDip，并排放出来挑。
class Spacing
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static void SetStatic(string n, object v) { typeof(WidgetForm).GetField(n, F).SetValue(null, v); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        int[] gaps = new int[] { 3, 4, 5, 6 };
        string[] names = new string[] { "现在 · 3 DIP", "4 DIP", "5 DIP", "6 DIP" };

        int w = 420, pad = 24, labelW = 0, gapX = 20;
        var shots = new Bitmap[gaps.Length];
        int[] heights = new int[gaps.Length];
        for (int i = 0; i < gaps.Length; i++)
        {
            SetStatic("RowGapDip", gaps[i]);
            shots[i] = Card(out heights[i]);
        }
        SetStatic("RowGapDip", 3);

        int maxH = 0;
        foreach (int h in heights) maxH = Math.Max(maxH, h);
        int cw = pad * 2 + (w + gapX) * gaps.Length - gapX;
        int ch = pad + 34 + maxH + 44;
        using (var canvas = new Bitmap(cw, ch))
        {
            canvas.SetResolution(144, 144);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(232, 235, 241))) g.FillRectangle(bg, 0, 0, cw, ch);
                using (var ink = new SolidBrush(Color.FromArgb(30, 36, 50)))
                using (var sub = new SolidBrush(Color.FromArgb(120, 128, 145)))
                {
                    for (int i = 0; i < gaps.Length; i++)
                    {
                        int x = pad + i * (w + gapX);
                        g.DrawString(names[i], UiFont.Make(10.5f, FontStyle.Bold), ink, x, pad - 20);
                        g.DrawImage(shots[i], x, pad + 22);
                        g.DrawString("卡片高 " + heights[i] + "px", UiFont.Make(8.5f), sub, x, pad + 22 + heights[i] + 8);
                    }
                }
            }
            canvas.Save(Path.Combine(dir, "spacing.png"));
        }
        Console.WriteLine("spacing.png " + cw + "x" + ch + " 高度 " + string.Join("/", Array.ConvertAll(heights, v => v.ToString())));
    }

    static Bitmap Card(out int height)
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
