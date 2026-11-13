using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 警戒带（tape）渲染探针：直接实例化真实的 ScreenReminderForm，画几帧出来看。
// 只出图，不改配置；跑法见文件末尾注释。
class TapeShot
{
    const int W = 1280, H = 720;

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        var s = new Settings();
        s.ui.reminderSpreadStyle = "tape";
        s.ui.reminderHoldSeconds = 20;
        s.ui.theme = "midnight";

        var ev = new ReminderEvent();
        ev.id = "probe";
        ev.title = "产品评审会 · Q4 排期";
        ev.source = "lark";
        ev.start = DateTime.Now.AddMinutes(10).ToString(Reminders.DateFormat);
        ev.end = DateTime.Now.AddMinutes(40).ToString(Reminders.DateFormat);
        ev.place = "3F · 云杉会议室";
        ev.detail = "议题：Q4 排期 / 体验走查";

        var theme = Themes.Get("midnight");
        var screen = new Rectangle(0, 0, W, H);

        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "tape");
        Directory.CreateDirectory(dir);

        var f = new ScreenReminderForm(s, ev, theme, screen, new Point(640, 360), null);
        f.CreateControl();
        var handle = f.Handle;

        int[] waits = new int[] { 260, 900, 2400 };
        int prev = 0;
        foreach (int ms in waits)
        {
            Thread.Sleep(ms - prev);
            prev = ms;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, W, H));
                bmp.Save(Path.Combine(dir, "f" + ms.ToString("0000") + ".png"), ImageFormat.Png);
            }
            Application.DoEvents();
        }
        Console.WriteLine("ok -> " + dir);
        f.CloseNow();
    }
}
