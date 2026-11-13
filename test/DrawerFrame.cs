using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 把抽屉卡按 1.0 缩放出成 440×68 的帧，和 HTML 预览同尺寸对比用。
// 同时打印实际用的字体族和两行的高度，方便对齐排版。
class DrawerFrame
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        Console.WriteLine("font family = " + UiFont.Family + "  boldFace=" + UiFont.HasBoldFace);

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;

        var ev = new ReminderEvent();
        ev.title = "产品评审会 · Q4 排期";
        ev.place = "3F · 云杉会议室";
        ev.source = "lark";
        ev.start = Reminders.FormatStart(DateTime.Now.AddMinutes(10));
        ev.end = Reminders.FormatStart(DateTime.Now.AddMinutes(40));

        var card = new Rectangle(800, 200, 224, 68);
        using (var d = new DrawerForm(s, ev, card, true, 1f, false, null))
        {
            var bmp = (Bitmap)Call(d, "BuildFrame", DrawerForm.WidthDip);
            Console.WriteLine("frame dpi=" + bmp.HorizontalResolution + " size=" + bmp.Width + "x" + bmp.Height);
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
            Directory.CreateDirectory(dir);
            string outPath = Path.Combine(dir, "drawer-frame-1x.png");
            bmp.Save(outPath, ImageFormat.Png);
            Console.WriteLine("ok " + outPath + " " + bmp.Width + "x" + bmp.Height);
        }
        Application.DoEvents();
    }

    static object Call(object o, string name, params object[] a)
    {
        var m = o.GetType().GetMethod(name, F);
        if (m == null) throw new Exception("no method " + name);
        return m.Invoke(o, a);
    }

    static object Get(object o, string name)
    {
        return o.GetType().GetField(name, F).GetValue(o);
    }
}
