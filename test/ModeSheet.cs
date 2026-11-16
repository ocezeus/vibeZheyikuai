using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 三种「和主题贴合」的做法并排：A 只压暗 / B 再往主题色相靠 / C 主题底色淡染
class ModeSheet
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
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        string[] cols = new string[] { "主题卡片", "会议", "日程（跟随）", "午休（玉茗）", "晚休（桔梗）" };
        string[] rows = new string[] { "A 只压暗", "B 色相靠主题", "C 主题底色淡染" };
        int w = 420, h = 0, pad = 22, labelW = 108, gapX = 14, gapY = 46;
        var shots = new Bitmap[rows.Length + 1, cols.Length];
        for (int c = 0; c < cols.Length; c++) shots[0, c] = Shot(-1, c, out h);
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < cols.Length; c++)
                shots[r + 1, c] = Shot(r, c, out h);

        int gw = pad * 2 + labelW + cols.Length * w + (cols.Length - 1) * gapX;
        int gh = pad * 2 + (rows.Length + 1) * (h + gapY);
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
                    for (int c = 0; c < cols.Length; c++)
                        g.DrawString(cols[c], lbl, ink, pad + labelW + c * (w + gapX), pad);
                    for (int r = 0; r <= rows.Length; r++)
                    {
                        int y = pad + 26 + r * (h + gapY);
                        g.DrawString(r == 0 ? "现在" : rows[r - 1], lbl, ink, pad, y + h / 2 - 8);
                        for (int c = 0; c < cols.Length; c++)
                            g.DrawImage(shots[r, c], pad + labelW + c * (w + gapX), y);
                    }
                }
            }
            canvas.Save(Path.Combine(dir, "modes.png"));
        }
        Console.WriteLine("modes.png " + gw + "x" + gh + " 卡片高 " + h);
    }

    // which: -1 = 普通卡片，0..2 = FitMode
    static Bitmap Shot(int which, int col, out int height)
    {
        var store = new SettingsStore();
        var s = store.Load().Settings;
        s.ui.theme = "my-c6b7a3";   // 固定一套主题，三种做法才好比较
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
            if (which >= 0)
            {
                typeof(ReminderStyles).GetField("FitMode", BindingFlags.Public | BindingFlags.Static).SetValue(null, which);
                Set(form, "reminding", true);
                var ev = new ReminderEvent();
                ev.title = "需求对齐会"; ev.place = "位置 A"; ev.detail = "带上排期";
                ev.start = DateTime.Today.AddHours(11).ToString("yyyy-MM-dd HH:mm:ss");
                ev.end = DateTime.Today.AddHours(12).ToString("yyyy-MM-dd HH:mm:ss");
                if (col == 1) ev.source = "lark";
                else if (col == 2) ev.source = "event";
                else if (col >= 3)
                {
                    ev.source = "break";
                    var fs = s.ScheduleOf(col == 3 ? "lunch" : "evening");
                    if (fs != null) { ev.id = "fix:" + fs.id; ev.title = fs.name; }
                }
                if (col == 0) { Set(form, "reminding", false); }
                Set(form, "remindEvent", ev);
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-1500));
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
