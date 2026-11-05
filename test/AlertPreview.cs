using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 渲染「该下班啦」常驻提醒的几个关键时间点，用来做预览图 / GIF
class AlertPreview
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object o, string name, object value) { o.GetType().GetField(name, F).SetValue(o, value); }
    static object Call(object o, string name, params object[] args) { return o.GetType().GetMethod(name, F).Invoke(o, args); }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        int[] moments = { 0, 100, 250, 450, 700, 1200 };
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "free";
        s.ui.theme = "midnight";

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            Set(form, "scale", 1.5f);
            Set(form, "collapsed", false);
            form.MinimumSize = Size.Empty;
            form.Size = new Size(420, 273);
            Call(form, "Recompute");

            var e = new ReminderEvent();
            e.id = "alert:offwork";
            e.source = "alert";
            e.title = "该下班啦";
            e.detail = "在飞书发「2」打卡后自动消失";
            e.start = Reminders.FormatStart(new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 20, 4, 0));
            Set(form, "remindEvent", e);
            Set(form, "reminding", true);

            foreach (int t in moments)
            {
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-t));
                using (var bmp = new Bitmap(420, 273))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 273)));
                    bmp.Save(Path.Combine(dir, "alert-t" + t.ToString("D4") + ".png"));
                }
            }

            // 收起成横条的样子
            Set(form, "collapsed", true);
            form.Size = new Size(336, 102);
            Call(form, "Recompute");
            Set(form, "remindAt", DateTime.Now.AddMilliseconds(-1200));
            using (var bmp = new Bitmap(336, 102))
            {
                bmp.SetResolution(144, 144);
                using (var g = Graphics.FromImage(bmp))
                    Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 336, 102)));
                bmp.Save(Path.Combine(dir, "alert-collapsed.png"));
            }

            // 每套主题各画一张（下拉里就是这些）
            Set(form, "collapsed", false);
            form.Size = new Size(420, 273);
            Call(form, "Recompute");
            foreach (var id in ReminderStyles.Ids)
            {
                s.ui.offReminderStyle = id;
                Set(form, "remindAt", DateTime.Now.AddMilliseconds(-3000));
                using (var bmp = new Bitmap(420, 273))
                {
                    bmp.SetResolution(144, 144);
                    using (var g = Graphics.FromImage(bmp))
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 420, 273)));
                    bmp.Save(Path.Combine(dir, "style-" + id + ".png"));
                }
            }

            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
        Console.WriteLine("frames -> " + dir);
    }
}
