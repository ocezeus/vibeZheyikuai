using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 检查提醒卡右边缘那几个像素到底是什么颜色
class EdgeProbe
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [STAThread]
    static void Main()
    {
        var store = new SettingsStore();
        var s = store.Load().Settings;
        s.ui.reminderEffect = "glow";
        s.ui.reducedMotion = false;
        var snap = new AttendanceSnapshot(); snap.ok = true; snap.clockIn = DateTime.Today.AddHours(9);
        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            Set(form, "reminding", true);
            var ev = new ReminderEvent(); ev.title = "需求对齐会"; ev.place = "位置 A"; ev.source = "meeting";
            Set(form, "remindEvent", ev);
            form.MinimumSize = Size.Empty;
            int h = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(420, h);
            Set(form, "remindAt", DateTime.Now.AddMilliseconds(-350));
            Call(form, "Recompute");
            Console.WriteLine("form.Size=" + form.Size + " ClientSize=" + form.ClientSize + " Width=" + form.Width);
            using (var bmp = new Bitmap(420, h))
            {
                bmp.SetResolution(144, 144);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(232, 235, 241));
                    Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, h)));
                }
                for (int x = 400; x < 420; x++)
                {
                    Color c = bmp.GetPixel(x, 100);
                    Console.WriteLine(x + ": " + c.R + "," + c.G + "," + c.B + " a=" + c.A);
                }
            }
        }
    }
}
