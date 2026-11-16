using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 单独验证新的普通全屏提醒窗能不能显示。
class ScreenFormTest
{
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
        var s = new SettingsStore().Load().Settings;
        s.ui.reducedMotion = false;
        s.ui.reminderSpread = "screen";
        s.ui.reminderSpreadStyle = "circle";
        var ev = new ReminderEvent();
        ev.title = "需求对齐会"; ev.place = "3F · 会议室 A"; ev.detail = "带上排期和接口文档";
        ev.source = "local";
        ev.start = Reminders.FormatStart(DateTime.Now.AddMinutes(12));
        ev.end = Reminders.FormatStart(DateTime.Now.AddMinutes(40));
        var scr = Screen.AllScreens[1];
        int tipX = scr.Bounds.Left + 2500, tipY = scr.Bounds.Top + 700;
        using (var f = new ScreenReminderForm(s, ev, Themes.Get(s.ui.theme), scr.Bounds, new Point(tipX, tipY), null))
        {
            f.Show();
            for (int i = 0; i < 25; i++) { Application.DoEvents(); Thread.Sleep(50); }
            Console.WriteLine("form=" + f.Bounds + " visible=" + f.Visible);
            Save(scr.Bounds, System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "screenform.png"));
            f.BeginExit();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(50); }
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
