using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 流光淡出探针：把进度条右半段裁出来放大，逐帧看「走到尽头」这一段
// 是不是渐渐变淡，而不是在终点突然消失。
class FlowFade
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        // 一圈 2.6 秒：走到条子的右半段之后，光的透明度开始往下走
        double[] phases = { 0.60, 0.72, 0.80, 0.86, 0.92, 0.96, 0.995 };
        string[] notes =
        {
            "全亮，光带还在右移", "全亮，接近尽头", "开始变淡",
            "更淡", "快看不见了", "只剩一点点", "到尽头：已经淡没（不是突然消失）"
        };

        int cw = 420, ch = 282;
        // 只裁右半段：进度条 60% 时右端在 x=246，留一点余量
        int cropX = 140, cropY = 149, cropW = 260, cropH = 14;
        int zoom = 4;
        int stripH = cropH * zoom;
        int rowH = 22 + stripH + 18;

        int pad = 22;
        int width = pad * 2 + cropW * zoom;
        int height = pad + rowH * phases.Length + 10;

        using (var canvas = new Bitmap(width, height))
        {
            canvas.SetResolution(144, 144);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(232, 235, 241))) g.FillRectangle(bg, 0, 0, width, height);
                using (var ink = new SolidBrush(Color.FromArgb(30, 36, 50)))
                using (var sub = new SolidBrush(Color.FromArgb(110, 120, 138)))
                using (var lbl = UiFont.Make(8.5f, FontStyle.Bold))
                using (var noteFont = UiFont.Make(8f))
                {
                    for (int i = 0; i < phases.Length; i++)
                    {
                        int y = pad + i * rowH;
                        g.DrawString("phase " + phases[i].ToString("0.000"), lbl, ink, pad, y);
                        g.DrawString(notes[i], noteFont, sub, pad + 104, y + 1);

                        using (var card = Card(phases[i], cw, ch))
                        using (var crop = new Bitmap(cropW, cropH))
                        {
                            using (var cg = Graphics.FromImage(crop))
                                cg.DrawImage(card, new Rectangle(0, 0, cropW, cropH),
                                    new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(crop, new Rectangle(pad, y + 18, cropW * zoom, stripH));
                            g.PixelOffsetMode = PixelOffsetMode.Default;
                        }
                    }
                }
            }
            canvas.Save(Path.Combine(dir, "flow-fade.png"));
        }
        Console.WriteLine("flow-fade.png " + width + "x" + height);
    }

    static Bitmap Card(double phase, int w, int h)
    {
        var store = new SettingsStore();
        var s = store.Load().Settings;
        s.ui.reducedMotion = false;
        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            form.MinimumSize = Size.Empty;
            form.Size = new Size(w, h);
            Call(form, "Recompute");
            var st = Get(form, "status");
            st.GetType().GetProperty("progress").SetValue(st, 0.6, null);
            Set(form, "flowPhase", phase);
            var bmp = new Bitmap(w, h);
            bmp.SetResolution(144, 144);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(232, 235, 241));
                Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
            }
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            return bmp;
        }
    }
}
