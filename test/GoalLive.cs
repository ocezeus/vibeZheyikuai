using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 真机预览：把真窗口显示出来、真的播一遍目标达成庆祝，按时间点截屏。
// 比 GoalShot 更接近用户看到的样子（出框层是独立透明窗 + UpdateLayeredWindow，
// 只有真显示出来才验证得到 z 序 / 点击穿透 / 卡外装饰是否真的在屏上）。
//   GoalLive.exe [版本id] [秒数]   默认 05，总时长 8.4s
// 输出：test\visual\live-goal-<id>-<秒>.png
class GoalLive
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        string id = args.Length > 0 ? args[0] : "05";
        double total = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 8.4;

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "free";
        s.ui.theme = "midnight";
        s.ui.goalEffect = id;
        s.ui.autoCollapseSeconds = 0;
        s.ui.alwaysOnTop = true;

        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(120, 120);
            form.MinimumSize = Size.Empty;
            form.Visible = true;
            Application.DoEvents();

            var snap = new AttendanceSnapshot();
            snap.ok = true;
            snap.clockIn = DateTime.Today.AddHours(9);
            Set(form, "snapshot", snap);
            Call(form, "Recompute");
            Set(form, "collapsed", false);
            form.Size = new Size((int)Call(form, "S", 280f),
                (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null));
            form.Location = new Point(120, 120);
            Application.DoEvents();

            // 走真实触发路径：把状态改成已达标，让 Widget 自己排队开播
            var st = Get(form, "status");
            st.GetType().GetProperty("progress").SetValue(st, 1.0, null);
            st.GetType().GetProperty("headline").SetValue(st, "已达标", null);
            Call(form, "MaybeQueueGoal", DateTime.Now);
            Application.DoEvents();
            Console.WriteLine("celebrating=" + Get(form, "goal").GetType().GetProperty("Active").GetValue(Get(form, "goal"), null));

            // 截图范围：卡片 + 出框留白（140 DIP）
            float scale = (float)Get(form, "scale");
            int pad = (int)Math.Round(150 * scale);
            var shotArea = new Rectangle(form.Left - pad, form.Top - pad, form.Width + pad * 2, form.Height + pad * 2);
            var scr = Screen.FromControl(form);
            shotArea.Intersect(scr.Bounds);

            DateTime start = DateTime.Now;
            double[] marks = new double[] { 0.6, 1.5, 3.2, 6.3, 7.4 };
            int next = 0;
            while ((DateTime.Now - start).TotalSeconds < total && next < marks.Length)
            {
                double t = (DateTime.Now - start).TotalSeconds;
                Application.DoEvents();
                System.Threading.Thread.Sleep(15);
                if (t >= marks[next])
                {
                    using (var bmp = new Bitmap(shotArea.Width, shotArea.Height, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                            g.CopyFromScreen(shotArea.Left, shotArea.Top, 0, 0, shotArea.Size);
                        string path = Path.Combine(dir, "live-goal-" + id + "-" + t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                        bmp.Save(path, ImageFormat.Png);
                        Console.WriteLine("shot " + path);
                    }
                    next++;
                }
            }

            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
        Application.DoEvents();
    }

    static void Set(object o, string name, object v)
    {
        var f = o.GetType().GetField(name, F);
        if (f == null) throw new Exception("no field " + name);
        f.SetValue(o, v);
    }

    static object Get(object o, string name)
    {
        var f = o.GetType().GetField(name, F);
        if (f == null) throw new Exception("no field " + name);
        return f.GetValue(o);
    }

    static object Call(object o, string name, params object[] a)
    {
        foreach (var m in o.GetType().GetMethods(F))
        {
            if (m.Name != name || m.GetParameters().Length != a.Length) continue;
            return m.Invoke(o, a);
        }
        throw new Exception("no method " + name + "/" + a.Length);
    }
}
