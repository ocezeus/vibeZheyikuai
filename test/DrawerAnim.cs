using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 抽屉滑出曲线采样：每 60ms 打印一次进度，确认时长和 cubic-bezier 形状和 HTML 一致。
class DrawerAnim
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        var s = new Settings();
        s.ui.reducedMotion = false;
        var ev = new ReminderEvent();
        ev.title = "产品评审会 · Q4 排期";
        ev.place = "3F · 云杉会议室";
        ev.source = "lark";
        ev.start = DateTime.Now.AddMinutes(10).ToString(Reminders.DateFormat);
        ev.end = DateTime.Now.AddMinutes(40).ToString(Reminders.DateFormat);

        using (var d = new DrawerForm(s, ev, new Rectangle(700, 300, 336, 102), true, 1.5f, false, null))
        {
            d.Show();
            Application.DoEvents();
            d.BeginSlide();
            int t0 = Environment.TickCount;
            int prev = -1;
            while (Environment.TickCount - t0 < 1600)
            {
                Application.DoEvents();
                Thread.Sleep(20);
                int ms = Environment.TickCount - t0;
                if (ms / 60 == prev) continue;
                prev = ms / 60;
                double p = (double)Get(d, "p");
                Console.WriteLine(string.Format("t={0,4}ms  p={1:0.000}  width={2}", ms, p, d.Width));
                if (p >= 0.999) break;
            }
            d.CloseNow();
        }
        Application.DoEvents();
    }

    static object Get(object o, string name)
    {
        return o.GetType().GetField(name, F).GetValue(o);
    }
}
