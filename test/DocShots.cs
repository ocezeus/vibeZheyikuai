using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

/// <summary>给文档截图：固定日程页（含自建条目 + 自建主题）、提醒页（小卡片展开）、取色对话框。</summary>
static class DocShots
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static object Get(object o, string name) { return o.GetType().GetField(name, F).GetValue(o); }
    static void Set(object o, string name, object v) { o.GetType().GetField(name, F).SetValue(o, v); }
    static object Call(object o, string name, params object[] args)
    {
        var m = o.GetType().GetMethod(name, F);
        return m.Invoke(o, args);
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual");
        Directory.CreateDirectory(dir);

        var store = new SettingsStore();
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.mode = "flex";
        s.lunchStart = "11:50"; s.lunchEnd = "13:30";
        s.eveningEnabled = true; s.eveningStart = "17:50"; s.eveningEnd = "19:00";
        s.Normalize();

        // 自建主题
        var ct = new CustomTheme();
        ct.id = "my-moss"; ct.name = "苔绿"; ct.accent = "#6F9E6A";
        s.customThemes.Add(ct);
        Themes.SetCustom(s.customThemes);

        // 再补两条自建固定日程
        var stand = FixedSchedule.Make("custom-stand", "custom", "站会", "10:30", "", "huang-hupo");
        stand.repeat = "odd"; stand.until = DateTime.Today.AddMonths(3).ToString("yyyy-MM-dd"); stand.endNotify = false;
        var stretch = FixedSchedule.Make("custom-stretch", "custom", "拉伸", "15:30", "15:40", "lv-kongque");
        s.schedules.Insert(1, stand);
        s.schedules.Insert(3, stretch);
        s.Normalize();

        // 一次性日程
        var e1 = new ReminderEvent();
        e1.id = "local:1"; e1.source = "local"; e1.title = "需求评审";
        e1.detail = "确认交互方案和接口人"; e1.place = "12F · 会议室 A";
        e1.start = DateTime.Today.ToString("yyyy-MM-dd") + "T19:30"; e1.end = DateTime.Today.ToString("yyyy-MM-dd") + "T20:00";
        var e2 = new ReminderEvent();
        e2.id = "local:2"; e2.source = "local"; e2.title = "取快递";
        e2.detail = "楼下丰巢，带工卡"; e2.place = "1F 大厅";
        e2.start = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd") + "T18:10";
        e2.end = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd") + "T18:25";
        var e3 = new ReminderEvent();
        e3.id = "local:3"; e3.source = "local"; e3.title = "提交周报";
        e3.detail = ""; e3.place = "";
        e3.start = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd") + "T17:00";
        e3.end = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd") + "T17:30";
        s.events.Add(e1); s.events.Add(e2); s.events.Add(e3);

        var dlg = new SettingsForm(store, s);
        var tabs = (TabControl)Get(dlg, "tabs");
        dlg.CreateControl();
        dlg.Show();
        Application.DoEvents();

        // 固定日程页：选中「站会」
        tabs.SelectedIndex = 1;
        var list = Get(dlg, "listSched");
        Application.DoEvents();
        Set(list, "selected", 1);
        Call(dlg, "SelectSched", 1);
        Application.DoEvents();
        Save(dlg, Path.Combine(dir, "doc-fixed.png"));

        // 提醒页：把第二张小卡片展开
        tabs.SelectedIndex = 2;
        var cards = (Control)Get(dlg, "cardEvents");
        Application.DoEvents();
        Set(cards, "selected", 1);
        Set(cards, "expanded", 1);
        cards.Invalidate();
        Application.DoEvents();
        Save(dlg, Path.Combine(dir, "doc-events.png"));

        // 外观页：自建主题被选中
        tabs.SelectedIndex = 3;
        var picker = (ThemePicker)Get(dlg, "picker");
        picker.SelectedId = "my-moss";
        picker.Invalidate();
        Call(dlg, "UpdateThemeName");
        Application.DoEvents();
        Save(dlg, Path.Combine(dir, "doc-look.png"));

        dlg.Close();

        // 取色对话框
        var cd = new ColorWheelForm(Themes.Get("forest"));
        cd.CreateControl();
        cd.Show();
        Application.DoEvents();
        Save(cd, Path.Combine(dir, "doc-color.png"));
        cd.Close();

        Console.WriteLine("doc shots -> " + dir);
    }

    static void Save(Form f, string path)
    {
        using (var bmp = new Bitmap(f.Width, f.Height))
        {
            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
            bmp.Save(path);
        }
    }
}
