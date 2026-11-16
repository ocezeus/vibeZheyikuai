using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 真机渲染四种背景光效，检查 1.6× 的强度。
class GlowCard
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);
        var store = new SettingsStore();
        foreach (string mode in new[] { "classic", "corner", "bar", "all" })
        {
            var s = store.Load().Settings;
            s.ui.edge = "free";
            s.ui.reducedMotion = false;
            s.ui.cardGlow = mode;
            using (var form = new WidgetForm(store, s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                var snap = new AttendanceSnapshot();
                snap.ok = true;
                snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
                Set(form, "snapshot", snap);
                Call(form, "Recompute");
                using (var bmp = new Bitmap(420, 282))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                    var frame = (Bitmap)Get(form, "frame");
                    if (frame != null) frame.Save(Path.Combine(dir, "glow-" + mode + ".png"));
                }
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }
        Console.WriteLine("done");
    }

    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
}
