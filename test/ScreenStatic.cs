using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 只把全屏提醒画出来并停住，不跑提醒时钟：验证分层窗 1:1 是否能显示 4K 画面。
class ScreenStatic
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        var s = new SettingsStore().Load();
        s.ui.reducedMotion = false;
        s.ui.edge = "free";
        s.ui.x = 1800; s.ui.y = 600;
        s.ui.reminderSpread = "screen";
        s.ui.reminderSpreadStyle = "circle";
        var ev = new ReminderEvent();
        ev.title = "需求对齐会"; ev.place = "3F · 会议室 A"; ev.detail = "带上排期和接口文档";
        ev.source = "local";
        ev.start = Reminders.FormatStart(DateTime.Now.AddMinutes(12));
        ev.end = Reminders.FormatStart(DateTime.Now.AddMinutes(40));
        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            form.Show();
            for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(60); }
            Set(form, "remindEvent", ev);
            Set(form, "reminding", true);
            Call(form, "EnterScreenMode");
            Set(form, "remindAt", DateTime.Now.AddMilliseconds(-1500));
            for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(50); }
            var scr = Screen.FromControl(form);
            Console.WriteLine("form=" + form.Bounds + " screen=" + scr.Bounds + " sr=" + Get(form, "screenReminder"));
            var fr = (Bitmap)Get(form, "frame");
            Console.WriteLine("frame=" + (fr == null ? "null" : fr.Size.ToString()));
            Save(scr.Bounds, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "static-screen.png"));
            Call(form, "ExitScreenMode");
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
    }

    static void Save(Rectangle r, string path)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
        IntPtr hb = CreateCompatibleBitmap(screen, r.Width, r.Height);
        IntPtr old = SelectObject(mem, hb);
        BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.Left, r.Top, 0x00CC0020);
        using (var bmp = Image.FromHbitmap(hb)) bmp.Save(path);
        SelectObject(mem, old); DeleteObject(hb); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
    }
}
