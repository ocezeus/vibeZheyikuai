using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 排查「未读左上角每 5 秒快闪」在真机里有没有被调度到：
// 用真实配置（只读）+ 内存里造一条未读，跑 8 秒消息循环，每 60ms 打一次 noteBlinkAt / alpha。
class FlashLiveProbe
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
    static object Field(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static void SetField(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a)
    {
        foreach (var m in o.GetType().GetMethods(F))
            if (m.Name == n && m.GetParameters().Length == (a == null ? 0 : a.Length)) return m.Invoke(o, a);
        throw new MissingMethodException(o.GetType().Name + "." + n);
    }

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
        Application.EnableVisualStyles();

        try
        {
        var store = new SettingsStore();
        var s = store.Load().Settings;
        // 内存里造一条未读（不落盘）
        string first = null;
        foreach (var c in s.note.contacts) { first = c.id; break; }
        if (first == null) { s.note.AddContact("62015", "阿哲"); first = "62015"; }
        s.note.Append(new NoteMessage(first, s.note.id, "未读角闪排查", NoteRelay.Now()), true);
        s.Normalize();

        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        snap.fetchedAt = DateTime.Now;

        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "real-flash");
        Directory.CreateDirectory(dir);

        using (var form = new WidgetForm(store, s))
        {
            SetField(form, "snapshot", snap);
            SetField(form, "scale", 1.5f);
            SetField(form, "collapsed", false);
            form.Show();
            Application.DoEvents();
            int h = form.Height;
            Console.WriteLine("theme=" + s.ui.theme + " unread=" + s.note.TotalUnread() + " size=" + form.Width + "x" + h);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int lastBlink = 0;
            int i = 0;
            while (sw.ElapsedMilliseconds < 8000)
            {
                Application.DoEvents();
                if (i % 4 == 0)
                {
                    int blinkAt = (int)Field(form, "noteBlinkAt");
                    int now = Environment.TickCount;
                    double alpha = WidgetForm.DotPulse(now - blinkAt, WidgetForm.NoteBlinkSpanMs);
                    string mark = "";
                    if (blinkAt != lastBlink) { mark = "  <-- BLINK"; lastBlink = blinkAt; }
                    Console.WriteLine(string.Format("{0,6} ms  noteBlinkAt={1}  alpha={2:0.00}{3}", sw.ElapsedMilliseconds, blinkAt, alpha, mark));
                    // 顺手存一帧左上角
                    using (var bmp = new Bitmap(140, 120))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.FromArgb(18, 20, 26));
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            Call(form, "RenderCard", g);
                        }
                        bmp.Save(Path.Combine(dir, "live-" + i.ToString("D4") + ".png"));
                    }
                }
                Thread.Sleep(15);
                i++;
            }
            form.Close();
        }
        Console.WriteLine("done");
        }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().Name + ": " + ex.Message);
            if (ex.StackTrace != null) Console.WriteLine(ex.StackTrace.Split('\n')[0]);
        }
    }
}
