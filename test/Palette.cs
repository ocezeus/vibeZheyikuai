using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 提醒卡配色对照：普通卡片 / 会议 / 日程 / 午休（自选配色）/ 晚休（自选配色）
class Palette
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
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        string[] tags = new string[] { "普通卡片", "会议（橙）", "日程（跟随主题）", "午休（玉茗）", "晚休（桔梗）" };
        var bmps = new Bitmap[tags.Length];
        int h = 0;
        bmps[0] = Shot(null, null, 0, out h);
        bmps[1] = Shot("lark", null, 1500, out h);
        bmps[2] = Shot("event", null, 1500, out h);
        bmps[3] = Shot("break", "lunch", 1500, out h);
        bmps[4] = Shot("break", "evening", 1500, out h);
        // 文档用单张：README 里逐类展示
        string[] files = new string[] { "plain", "meeting", "event", "lunch", "evening" };
        for (int i = 0; i < files.Length; i++)
            bmps[i].Save(Path.Combine(dir, "kind-" + files[i] + ".png"));

        int w = 420, pad = 20, gap = 16;
        int gw = pad * 2 + tags.Length * w + (tags.Length - 1) * gap;
        int gh = pad * 2 + h + 26;
        using (var canvas = new Bitmap(gw, gh))
        {
            canvas.SetResolution(144, 144);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(232, 235, 241))) g.FillRectangle(bg, 0, 0, gw, gh);
                using (var ink = new SolidBrush(Color.FromArgb(30, 36, 50)))
                using (var lbl = UiFont.Make(9.5f, FontStyle.Bold))
                {
                    for (int i = 0; i < tags.Length; i++)
                    {
                        int x = pad + i * (w + gap);
                        g.DrawString(tags[i], lbl, ink, x, pad);
                        g.DrawImage(bmps[i], x, pad + 22);
                    }
                }
            }
            canvas.Save(Path.Combine(dir, "palette.png"));
        }
        Console.WriteLine("palette.png " + gw + "x" + gh + " 卡片高 " + h);
    }

    static Bitmap Shot(string source, string role, double t, out int height)
    {
        var store = new SettingsStore();
        var s = store.Load();
        s.ui.reducedMotion = true;
        s.ui.reminderEffect = "none";
        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            form.MinimumSize = Size.Empty;
            height = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(420, height);
            if (source != null)
            {
                Set(form, "reminding", true);
                var ev = new ReminderEvent();
                ev.title = source == "lark" ? "需求对齐会" : (role == "lunch" ? "该吃午饭了" : role == "evening" ? "该吃晚饭了" : "周会");
                ev.place = "位置 A";
                ev.detail = "带上排期";
                ev.source = source;
                if (role != null)
                {
                    var fs = s.ScheduleOf(role);
                    if (fs != null) { ev.id = "fix:" + fs.id; ev.title = fs.name; }
                }
                ev.start = DateTime.Today.AddHours(11).ToString("yyyy-MM-dd HH:mm:ss");
                ev.end = DateTime.Today.AddHours(12).ToString("yyyy-MM-dd HH:mm:ss");
                Set(form, "remindEvent", ev);
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-t));
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
