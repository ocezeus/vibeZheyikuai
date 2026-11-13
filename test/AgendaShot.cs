using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 设置窗「日程」页的离屏出图：真窗口 + DrawToBitmap。
// 用法：AgendaShot.exe [tabIndex] [主题id]
class AgendaShot
{
    [STAThread]
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().FullName);
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 99;
        }
    }

    static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        int tab = args != null && args.Length > 0 ? int.Parse(args[0]) : 4;
        string themeId = args != null && args.Length > 1 ? args[1] : "midnight";

        var s = new Settings();
        s.ui.theme = themeId;
        s.Normalize();
        s.outlook.calendars.Clear();
        s.outlook.calendars.Add(new CalendarSource
        {
            id = "cal-work", name = "工作日历", source = "url", enabled = true, leadMinutes = 10,
            url = "https://outlook.office365.com/owa/calendar/abc/calendar.ics",
            lastSync = "2026-11-10 19:40"
        });
        s.outlook.calendars.Add(new CalendarSource
        {
            id = "cal-me", name = "个人日历", source = "url", enabled = true, leadMinutes = 0,
            url = "https://outlook.office365.com/owa/calendar/me/calendar.ics"
        });
        s.outlook.enabled = true;
        s.outlook.lastSync = "2026-11-10 19:40";
        s.outlook.cached = new List<ReminderEvent>();
        s.salary.monthly = 28000m;
        s.salary.payday = 15;
        s.note.style = tab == 6 ? "columns" : "drawer";
        s.note.AddContact("62015", "阿哲");
        s.note.AddContact("11788", "小满");
        s.note.AddContact("90554", "老周");
        s.note.Thread("62015").messages.Add(new NoteMessage("62015", s.note.id, "晚上的评审推迟到 8 点", NoteRelay.Now() - 600));
        s.SetStyleOf(Reminders.KindOutlook, new ReminderStyleDef("screen", "tape", "lv-kongque"));
        s.SetStyleOf(Reminders.KindLark, new ReminderStyleDef("screen", "bar", "lan-yuming"));

        using (var f = new SettingsForm(new SettingsStore(), s, tab))
        {
            f.Show();
            Pump(500);
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "settings-tab" + tab + "-" + themeId + ".png");
                bmp.Save(p, ImageFormat.Png);
                Console.WriteLine("ok " + p + "  " + f.Width + "x" + f.Height);
            }
            f.Close();
        }
        Application.DoEvents();
    }

    static void Pump(int ms)
    {
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end) { Application.DoEvents(); Thread.Sleep(10); }
    }
}
