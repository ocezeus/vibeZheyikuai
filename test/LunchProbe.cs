using System;
using System.Windows.Forms;
using WorkCountdown;

// 用真实配置算一遍卡片第二行，确认午休/晚休倒计时回来了（只读，不保存）。
class LunchProbe
{
    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        var store = new SettingsStore();
        var s = store.Load();
        s.NormalizeSchedules();
        var theme = Themes.Get(s.ui.theme);
        var now = DateTime.Now;
        var st = StatusBuilder.Build(s, null, now, theme);
        Console.WriteLine("now=" + now.ToString("HH:mm"));
        Console.WriteLine("lunchStart=" + s.lunchStart + " lunchEnd=" + s.lunchEnd +
            "  evening=" + s.eveningStart + "-" + s.eveningEnd + " enabled=" + s.eveningEnabled);
        Console.WriteLine("showLunch=" + s.ui.showLunch);
        Console.WriteLine("second row: " + st.lunch + " | " + st.lunchLabel + " " + st.lunchValue +
            " | counting=" + st.lunchCounting);
        foreach (var f in s.schedules)
            Console.WriteLine("  sched " + f.id + " role=" + f.role + " name=" + f.name +
                " " + f.start + "-" + f.end + " enabled=" + f.enabled);
    }
}
