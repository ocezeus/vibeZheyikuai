using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 小字清晰度对比：同主题同尺寸，换字号 / 抗锯齿方式各渲染一张，拼成一张图看。
class Crisp
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }
    static void Static(string n, object v) { typeof(WidgetForm).GetField(n, F).SetValue(null, v); }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        object[][] combos = new object[][]
        {
            new object[] { "A · 现在（9 / 8.5pt，灰阶抗锯齿）", 9f, 9f, 8.5f, 0.28f, 0.40f, false },
            new object[] { "B · 只把字号放大（9.5 / 9pt，灰阶）", 9.5f, 9.5f, 9f, 0.22f, 0.34f, false },
            new object[] { "C · 字号放大 + ClearType", 9.5f, 9.5f, 9f, 0.22f, 0.34f, true },
            new object[] { "D · 字号放大 + ClearType + 更实的次要文字", 9.5f, 9.5f, 9f, 0.16f, 0.28f, true },
        };

        int w = 420, h = 0, pad = 26, labelH = 30;
        Bitmap[] shots = new Bitmap[combos.Length];
        for (int i = 0; i < combos.Length; i++)
        {
            Static("TypeStatusPt", combos[i][1]);
            Static("TypeSmallPt", combos[i][2]);
            Static("TypeTinyPt", combos[i][3]);
            Static("MutedMix", combos[i][4]);
            Static("DimMix", combos[i][5]);
            Static("UseClearType", combos[i][6]);
            shots[i] = Card(out h);
        }

        using (var canvas = new Bitmap(w + pad * 2, pad + (labelH + h + 22) * combos.Length))
        {
            canvas.SetResolution(144, 144);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(233, 236, 242))) g.FillRectangle(bg, 0, 0, canvas.Width, canvas.Height);
                using (var ink = new SolidBrush(Color.FromArgb(28, 34, 48)))
                {
                    int y = pad;
                    for (int i = 0; i < combos.Length; i++)
                    {
                        g.DrawString((string)combos[i][0], UiFont.Make(9.5f, FontStyle.Bold), ink, pad, y);
                        g.DrawImage(shots[i], pad, y + labelH);
                        y += labelH + h + 22;
                    }
                }
            }
            canvas.Save(Path.Combine(dir, "crisp.png"));
        }
        Console.WriteLine("crisp.png " + (w + pad * 2) + "x" + (pad + (labelH + h + 22) * combos.Length) + " 卡片高 " + h);
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
            form.MinimumSize = Size.Empty;
            height = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(420, height);
            Call(form, "Recompute");
            var bmp = new Bitmap(420, height);
            bmp.SetResolution(144, 144);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(233, 236, 242));
                Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, height)));
            }
            return bmp;
        }
    }
}
