using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 警戒带 → 抽屉 的状态机探针：真跑一遍 StartReminder，按时间点打印状态。
// 会在屏幕上真的放一次警戒带（约 4~8 秒），只在需要人工确认时跑。
class DrawerFlow
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "right";
        s.ui.theme = "ivory";
        s.ui.alwaysOnTop = true;
        s.ui.reminderSpread = "screen";
        s.ui.reminderSpreadStyle = "tape";
        s.ui.reminderHoldSeconds = 3;

        var ev = new ReminderEvent();
        ev.id = "probe-flow";
        ev.title = "产品评审会 · Q4 排期";
        ev.source = "lark";
        ev.start = DateTime.Now.AddMinutes(1).ToString(Reminders.DateFormat);
        ev.end = DateTime.Now.AddMinutes(31).ToString(Reminders.DateFormat);
        ev.place = "3F · 云杉会议室";

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            form.Show();
            Application.DoEvents();
            Thread.Sleep(300);
            Console.WriteLine("start collapsed=" + Get(form, "collapsed") + " size=" + form.Size);

            Call(form, "StartReminder", ev);
            int t0 = Environment.TickCount;
            int[] marks = new int[] { 60, 200, 400, 1200, 3000, 5000, 6500, 8000 };
            int prev = 0;
            foreach (int ms in marks)
            {
                Thread.Sleep(ms - prev);
                prev = ms;
                Application.DoEvents();
                Console.WriteLine(string.Format("t={0,5}ms collapsed={1} anim={2} band={3} drawer={4} phase={5} width={6}",
                    Environment.TickCount - t0,
                    Get(form, "collapsed"), Get(form, "animating"),
                    Get(form, "screenForm") != null,
                    Get(form, "drawer") != null,
                    Get(form, "tapePhase"), form.Width));
            }

            var drawer = Get(form, "drawer");
            if (drawer != null)
            {
                Console.WriteLine("drawer bounds=" + ((Form)drawer).Bounds);
            }
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
        Application.DoEvents();
    }

    static object Get(object o, string name)
    {
        return o.GetType().GetField(name, F).GetValue(o);
    }

    static object Call(object o, string name, params object[] a)
    {
        var m = o.GetType().GetMethod(name, F);
        if (m == null) throw new Exception("no method " + name);
        return m.Invoke(o, a);
    }
}
