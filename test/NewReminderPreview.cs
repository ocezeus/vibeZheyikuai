using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 新提醒动画的真机渲染探针：卡片内两种样式 + 全屏两种样式，只出图。
class NewReminderPreview
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
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "new-reminder");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        var store = new SettingsStore();
        foreach (string style in new string[] { "circle", "bar" })
        {
            var s = store.Load();
            s.ui.reducedMotion = false;
            s.ui.reminderSpread = "card";
            s.ui.reminderSpreadStyle = style;
            var ev = Event();
            using (var form = new WidgetForm(store, s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Set(form, "snapshot", Snapshot());
                Call(form, "Recompute");
                Call(form, "StartReminder", ev);
                foreach (int t in new int[] { 0, 150, 300, 450, 550, 700, 900, 1200 })
                {
                    Set(form, "remindAt", DateTime.Now.AddMilliseconds(-t));
                    using (var bmp = new Bitmap(420, 282, PixelFormat.Format32bppArgb))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.FromArgb(232, 235, 241));
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 282)));
                    }
                    var frame = (Bitmap)Get(form, "frame");
                    if (frame != null) frame.Save(Path.Combine(root, "card-" + style + "-" + t.ToString("0000") + ".png"));
                }
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }

        foreach (string style in new string[] { "circle", "bar" })
        {
            var s = store.Load();
            s.ui.reducedMotion = false;
            s.ui.reminderSpread = "screen";
            s.ui.reminderSpreadStyle = style;
            var ev = Event();
            using (var form = new WidgetForm(store, s))
            {
                Set(form, "scale", 1.5f);
                Set(form, "collapsed", false);
                form.MinimumSize = Size.Empty;
                form.Size = new Size(420, 282);
                Set(form, "snapshot", Snapshot());
                Call(form, "Recompute");
                Call(form, "OnPaint", new PaintEventArgs(Graphics.FromImage(new Bitmap(1, 1)), new Rectangle(0, 0, 1, 1)));
                Call(form, "StartReminder", ev);
                int rw = (int)Get(form, "screenRenderW");
                int rh = (int)Get(form, "screenRenderH");
                foreach (int t in new int[] { 300, 600, 900, 1500 })
                {
                    Set(form, "remindAt", DateTime.Now.AddMilliseconds(-t));
                    using (var bmp = new Bitmap(rw, rh, PixelFormat.Format32bppArgb))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, rw, rh)));
                    }
                    var frame = (Bitmap)Get(form, "frame");
                    if (frame != null) frame.Save(Path.Combine(root, "screen-" + style + "-" + t.ToString("0000") + ".png"));
                }
                Call(form, "ExitScreenMode");
                Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
            }
        }
        Console.WriteLine("done");
    }

    static ReminderEvent Event()
    {
        var ev = new ReminderEvent();
        ev.title = "需求对齐会";
        ev.place = "3F · 会议室 A";
        ev.detail = "带上排期和接口文档";
        ev.source = "local";
        ev.start = Reminders.FormatStart(DateTime.Now.AddMinutes(12));
        ev.end = Reminders.FormatStart(DateTime.Now.AddMinutes(40));
        return ev;
    }

    static AttendanceSnapshot Snapshot()
    {
        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        return snap;
    }
}
