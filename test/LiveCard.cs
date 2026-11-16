using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 用你真实的配置（主题、午休/晚休、班次）渲染一张当前卡片，确认改完的样子。
class LiveCard
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
        var store = new SettingsStore();
        var s = store.Load().Settings;
        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        snap.fetchedAt = DateTime.Now;

        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            form.MinimumSize = Size.Empty;
            int h = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(420, h);
            Call(form, "Recompute");

            using (var card = Shot(form, 420, h))
            using (var canvas = new Bitmap(420 + 56, h + 102 + 110))
            {
                canvas.SetResolution(144, 144);
                using (var g = Graphics.FromImage(canvas))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    using (var bg = new SolidBrush(Color.FromArgb(233, 236, 242))) g.FillRectangle(bg, 0, 0, canvas.Width, canvas.Height);
                    g.DrawImage(card, 28, 28);

                    Set(form, "collapsed", true);
                    form.Size = new Size(336, (int)Call(form, "S", 68f));
                    Call(form, "Recompute");
                    using (var pill = Shot(form, form.Width, form.Height)) g.DrawImage(pill, 28, 28 + h + 34);
                    using (var muted = new SolidBrush(Color.FromArgb(110, 118, 134)))
                        g.DrawString("收起态", UiFont.Make(9f, FontStyle.Regular), muted, 28 + 348, 28 + h + 34 + 40);
                }
                canvas.Save(Path.Combine(dir, "live-card.png"));
            }
            Console.WriteLine("theme=" + (s.ui.theme ?? "-") + " 字体=" + UiFont.Family + " 卡片高=" + h);
        }
    }

    static Bitmap Shot(WidgetForm form, int w, int h)
    {
        var bmp = new Bitmap(w, h);
        bmp.SetResolution(144, 144);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(233, 236, 242));
            Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
        }
        return bmp;
    }
}
