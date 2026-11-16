using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 生成 README 顶部那张动图的逐帧图：入场 → 停留 → 退场，每 50ms 一张
class GifFrames
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
        // 探针只读配置，绝不回写用户自己的设置
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "demo");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        int index = 0;
        // 入场 0-700ms，停留 700-1500ms（取两帧），退场 0-800ms
        for (double t = 0; t <= 700; t += 50) Save(dir, index++, Frame(t + 20, -1));
        Save(dir, index++, Frame(1100, -1));
        Save(dir, index++, Frame(1500, -1));
        for (double t = 0; t <= 800; t += 50) Save(dir, index++, Frame(-1, t + 20));
        Console.WriteLine("demo frames: " + index);
    }

    static void Save(string dir, int i, Bitmap bmp)
    {
        using (bmp) bmp.Save(Path.Combine(dir, "demo-" + i.ToString("000") + ".png"));
    }

    static Bitmap Frame(double inMs, double exitMs)
    {
        bool exiting = exitMs >= 0;
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
            Set(form, "reminding", true);
            var ev = new ReminderEvent();
            ev.title = "需求对齐会";
            ev.place = "位置 A";
            ev.detail = "带上排期";
            ev.source = "meeting";
            Set(form, "remindEvent", ev);
            form.MinimumSize = Size.Empty;
            int height = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(420, height);
            if (exiting)
            {
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-3600));
                Set(form, "remindExiting", true);
                Set(form, "remindExitAt", DateTime.Now.AddMilliseconds(-exitMs));
            }
            else
            {
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-inMs));
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
