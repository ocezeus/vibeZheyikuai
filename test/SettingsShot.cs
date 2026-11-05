using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 固定日程页的截图：多放几条，验证列表能滚动、新建的不会被顶掉。
class SettingsShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }

    [STAThread]
    static void Main()
    {
        // 探针只读配置，绝不回写用户自己的设置
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        var store = new SettingsStore();
        var s = store.Load();
        s.schedules.Add(FixedSchedule.Make("demo-standup", "custom", "每日站会", "10:30", "10:45", "zi-jiegeng"));
        s.schedules.Add(FixedSchedule.Make("demo-stretch", "custom", "拉伸一下", "15:30", "", "theme"));
        s.schedules.Add(FixedSchedule.Make("demo-weekly", "custom", "周会", "16:00", "17:00", "lan-yuming"));

        using (var dlg = new SettingsForm(store, s))
        {
            dlg.CreateControl();
            dlg.Show();
            var tabs = (TabControl)Get(dlg, "tabs");
            tabs.SelectedIndex = 1;
            Application.DoEvents();
            System.Threading.Thread.Sleep(150);
            Application.DoEvents();

            Save(dlg, Path.Combine(dir, "sched-top.png"));

            // 点「新建一条」，列表应该滚到新加的那条上
            Call(dlg, "AddSched");
            Application.DoEvents();
            System.Threading.Thread.Sleep(120);
            Application.DoEvents();
            Save(dlg, Path.Combine(dir, "sched-added.png"));

            var list = (ScheduleList)Get(dlg, "listSched");
            Console.WriteLine("条目 " + list.Items.Count + " 条 / 可滚 " + list.MaxScroll + "px / 当前 " + list.Scroll);
            Console.WriteLine("最后一条：" + list.Items[list.Items.Count - 1].name + "，选中 " + list.SelectedIndex);
            dlg.Close();
        }
    }

    static void Save(Form dlg, string path)
    {
        using (var bmp = new Bitmap(dlg.Width, dlg.Height))
        {
            dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(path);
        }
    }
}
