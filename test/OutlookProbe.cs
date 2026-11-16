using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 端到端探针：本地 .ics → Widget.RequestOutlook → 提醒池 → 是否到点。
// 用法：OutlookProbe.exe
class OutlookProbe
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static int fail;

    static void Ok(string name, bool ok, string info)
    {
        if (!ok) fail++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + "  => " + info);
    }

    [STAThread]
    static void Main(string[] args)
    {
        try { if (args != null && args.Length > 0 && args[0] == "graph") { GraphLive(); return; } Run(); }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().FullName + " " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 99;
        }
    }

    /// <summary>
    /// 真连一次微软的设备码端点，用假 client ID：预期拿到 invalid_client / unauthorized_client。
    /// 能拿到结构化错误 = 地址、表单格式、TLS、错误解析都是通的。
    /// </summary>
    static void GraphLive()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        OutlookGraph.DeviceCodeInfo info;
        string err;
        bool ok = OutlookGraph.StartDeviceCode("11111111-2222-3333-4444-555555555555", "common", 15000, out info, out err);
        Console.WriteLine("startDeviceCode ok=" + ok + " err=" + err);
        Ok("微软端点可达且返回结构化错误", !ok && err.Length > 0 && (err.IndexOf("invalid_client") >= 0
            || err.IndexOf("unauthorized_client") >= 0 || err.IndexOf("AADSTS") >= 0), err);
        // 空 client id 应该被本地拦住，不发请求
        bool ok2 = OutlookGraph.StartDeviceCode("", "common", 5000, out info, out err);
        Ok("空 client ID 本地拦住", !ok2 && err.IndexOf("client ID") >= 0, err);
        Console.WriteLine();
        Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILURES");
        Environment.Exit(fail == 0 ? 0 : 1);
    }
    static void Run()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();

        // 一个 5 分钟后开始、30 分钟的会；提前 10 分钟 → 现在就该响
        DateTime start = DateTime.Now.AddMinutes(5);
        string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:probe-1\r\n" +
            "SUMMARY:Outlook 探针会\r\n" +
            "DTSTART:" + start.ToString("yyyyMMdd'T'HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
            "DTEND:" + start.AddMinutes(30).ToString("yyyyMMdd'T'HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "\r\n" +
            "LOCATION:3F 云杉\r\n" +
            "END:VEVENT\r\nEND:VCALENDAR\r\n";
        string tmp = Path.Combine(Path.GetTempPath(), "wc-outlook-probe.ics");
        File.WriteAllText(tmp, ics, new UTF8Encoding(false));

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "free";
        s.ui.autoCollapseSeconds = 0;
        s.Normalize();
        s.outlook.enabled = true;
        s.outlook.source = "file";
        s.outlook.path = tmp;
        s.outlook.leadMinutes = 10;
        s.SetStyleOf(Reminders.KindOutlook, new ReminderStyleDef("card", "circle", "lv-kongque"));

        using (var form = new WidgetForm(new SettingsStore(), s))
        {
            form.Show();
            Call(form, "SetCollapsed", true);
            Pump(600);

            Call(form, "RequestOutlook", true);
            Pump(1500);

            var events = (List<ReminderEvent>)Get(form, "outlookEvents");
            Ok("本地 .ics 拉进来 1 条", events != null && events.Count == 1,
                (events == null ? "null" : events.Count.ToString()) + " 条");
            if (events == null || events.Count == 0) { form.Close(); return; }

            var e = events[0];
            Ok("标题/地点", e.title == "Outlook 探针会" && e.place == "3F 云杉", e.title + " · " + e.place);
            Ok("来源是 outlook", e.source == "outlook", e.source);
            Ok("提前量按设置补上", e.leadMinutes == 10, e.leadMinutes.ToString());
            Ok("分类到 Outlook", Reminders.KindOf(e) == Reminders.KindOutlook, Reminders.KindOf(e));

            var merged = new ReminderEngine().Agenda(s, new ReminderScheduleSnapshot(null, events));
            Ok("进提醒池", merged.Count == 1 && merged[0].source == "outlook", merged.Count + " 条");
            Ok("现在就该提醒（提前 10 分钟）", Reminders.IsDue(s, e, DateTime.Now), "start " + e.start);
            Ok("缓存写回了设置", s.outlook.cached != null && s.outlook.cached.Count == 1,
                (s.outlook.cached == null ? "null" : s.outlook.cached.Count.ToString()) + " 条 / " + s.outlook.lastSync);
            Ok("配色按 Outlook 类取", Reminders.ColorIdOf(s, e, null) == "lv-kongque", Reminders.ColorIdOf(s, e, null));

            // 真触发一次提醒：不该抛异常，且提醒里就是这条
            Call(form, "StartReminder", e);
            Pump(400);
            var cur = (ReminderEvent)Get(form, "remindEvent");
            Ok("提醒已经挂上这条", cur != null && cur.id == e.id, cur == null ? "null" : cur.title);

            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
        try { File.Delete(tmp); } catch { }

        Console.WriteLine();
        Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILURES");
        Environment.Exit(fail == 0 ? 0 : 1);
    }

    static void Pump(int ms)
    {
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end) { Application.DoEvents(); Thread.Sleep(10); }
    }

    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a)
    {
        var m = o.GetType().GetMethod(n, F);
        if (m == null) throw new Exception("no method " + n);
        return m.Invoke(o, a);
    }
}
