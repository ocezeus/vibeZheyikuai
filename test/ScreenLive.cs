using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using WorkCountdown;

// 真机全屏提醒：把探针窗口放到副屏，起一条提醒，截屏；再点掉，确认缩回卡片。
class ScreenLive
{
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L,T,R,B; }
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(root);

        var store = new SettingsStore();
        var s = store.Load();
        s.ui.reducedMotion = false;
        s.ui.edge = "free";
        s.ui.x = 1800;
        s.ui.y = 600;
        s.ui.reminderSpread = "screen";
        s.ui.reminderSpreadStyle = "circle";

        var ev = new ReminderEvent();
        ev.title = "需求对齐会";
        ev.place = "3F · 会议室 A";
        ev.detail = "带上排期和接口文档";
        ev.source = "local";
        ev.start = Reminders.FormatStart(DateTime.Now.AddMinutes(12));
        ev.end = Reminders.FormatStart(DateTime.Now.AddMinutes(40));

        using (var form = new WidgetForm(store, s))
        {
            form.Show();
            Application.DoEvents();
            Thread.Sleep(300);
            Application.DoEvents();

            Call(form, "StartReminder", ev);
            for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(60); }

            var scr = Screen.FromControl(form);
            RECT wr; GetWindowRect(form.Handle, out wr);
            Console.WriteLine("screen=" + scr.Bounds + " form=" + form.Bounds + " screenReminder=" + Get(form, "screenReminder")
                + " visible=" + IsWindowVisible(form.Handle) + " ex=" + GetWindowLong(form.Handle, -20).ToString("X")
                + " rect=" + wr.L + "," + wr.T + "," + wr.R + "," + wr.B
                + " ulw=" + Get(form, "lastUlwOk") + "/" + Get(form, "lastUlwErr"));
            var fr = (Bitmap)Get(form, "frame");
            if (fr != null) { fr.Save(Path.Combine(root, "live-screen-frame.png")); Console.WriteLine("frame=" + fr.Size); }
            SaveScreen(scr.Bounds, Path.Combine(root, "live-screen-hold.png"));
            int si = 0;
            foreach (var sc in Screen.AllScreens) { SaveScreen(sc.Bounds, Path.Combine(root, "live-all-" + (si++) + ".png")); Console.WriteLine("captured " + sc.Bounds); }

            Call(form, "DismissReminder");
            for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(60); }
            Console.WriteLine("after: form=" + form.Bounds + " screenReminder=" + Get(form, "screenReminder")
                + " reminding=" + Get(form, "reminding"));
            SaveScreen(scr.Bounds, Path.Combine(root, "live-screen-after.png"));

            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
    }

    static void SaveScreen(Rectangle r, string path)
    {
        using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
            bmp.Save(path);
        }
    }
}
