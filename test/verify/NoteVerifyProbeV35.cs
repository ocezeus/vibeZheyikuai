using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WorkCountdown;

// v3.5.0 独立验收探针：① 固定班次工作日勾选错位修复；② 个人昵称；③（增量）旧配置工作日迁移。
// 只读 src（编译时一起编进来），只写 test/verify/v35-shots/ 截图 + test/verify/data（迁移测试临时目录，跑完删除）。
// 用法：NoteVerifyProbeV35.exe [输出目录]   默认 <base>\test\verify\v35-shots
class NoteVerifyProbeV35
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static BindingFlags FF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    static int pass, fail;
    static string outDir;
    static readonly List<string> failLines = new List<string>();
    static readonly int mainThread = Thread.CurrentThread.ManagedThreadId;

    [STAThread]
    static int Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 2;
        }
        Console.WriteLine("");
        if (fail == 0) { Console.WriteLine("ALL PASS  (" + pass + " checks)"); return 0; }
        Console.WriteLine("FAILED  " + fail + " of " + (pass + fail));
        foreach (var l in failLines) Console.WriteLine("  " + l);
        return 1;
    }

    static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
        CleanupPortable(AppDomain.CurrentDomain.BaseDirectory);   // 上一次跑崩了留下的 portable.txt / data 清掉
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();

        outDir = (args != null && args.Length > 0) ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "v35-shots");
        Directory.CreateDirectory(outDir);

        Console.WriteLine("== 需求 1：固定班次工作日勾选错位 ==");
        Guard("A1 映射函数", CheckMapping);
        Guard("A2 设置页加载", CheckLoadDays);
        Guard("A3 设置页保存", CheckSaveDays);
        Guard("A4 7 天逐项往返", CheckRoundTripDays);
        Guard("A5 快捷按钮", CheckPresets);
        Guard("A6 Policy 判定", CheckPolicy);

        Console.WriteLine("== 需求 2：个人昵称 ==");
        Guard("B 模型层", CheckNickModel);
        Guard("C 中继端到端", CheckRelayNickEndToEnd);
        Guard("D 设置页/面板 UI", CheckNickUi);
        Guard("E Widget 接线", CheckWidgetNick);

        Console.WriteLine("== 增量：v3.5 旧配置工作日迁移 ==");
        Guard("M1/M2 迁移函数", CheckMigrateFunction);
        Guard("M3/M4/M5 Normalize 迁移/幂等", CheckNormalizeMigration);
        Guard("M6/M7/M8 不误迁移", CheckNoFalseMigration);
        Guard("M9/M10/M11 SettingsStore.Load 落盘", CheckStoreLoadMigration);
    }

    // ---------------------------------------------------------------- 断言框架

    static void Guard(string title, Action a)
    {
        try { a(); }
        catch (Exception ex) { Ok(title + " 未抛异常", false, ex.GetType().Name + ": " + ex.Message); }
    }

    static void Ok(string name, bool cond, string detail)
    {
        if (cond) { pass++; Console.WriteLine("PASS  " + name + "  " + detail); }
        else { fail++; Console.WriteLine("FAIL  " + name + "  " + detail); failLines.Add(name + " | " + detail); }
    }

    static string Out(string name) { return Path.Combine(outDir, name); }

    // ---------------------------------------------------------------- 反射工具

    static MethodInfo FindMethod(Type t, string name, int argc)
    {
        foreach (var mi in t.GetMethods(FF))
            if (mi.Name == name && (argc < 0 || mi.GetParameters().Length == argc)) return mi;
        return null;
    }

    static object Get(object o, string name)
    {
        if (o == null) return null;
        var t = o.GetType();
        var f = t.GetField(name, FF);
        if (f != null) return f.GetValue(o);
        var p = t.GetProperty(name, FF);
        if (p != null) return p.GetValue(o, null);
        return null;
    }

    static void Set(object o, string name, object v)
    {
        var f = o.GetType().GetField(name, FF);
        if (f == null) throw new Exception("no field " + name);
        f.SetValue(o, v);
    }

    static object Call(object o, string name, params object[] a)
    {
        var mi = FindMethod(o.GetType(), name, a.Length);
        if (mi == null) throw new Exception("no method " + name + "/" + a.Length);
        return mi.Invoke(o, a);
    }

    static object TryCall(object o, string name, params object[] a)
    {
        var mi = FindMethod(o.GetType(), name, a.Length);
        return mi == null ? null : mi.Invoke(o, a);
    }

    static object CallStatic(Type t, string name, params object[] a)
    {
        var mi = FindMethod(t, name, a.Length);
        if (mi == null || !mi.IsStatic) throw new Exception("no static method " + name + "/" + a.Length);
        return mi.Invoke(null, a);
    }

    static bool TryInvoke(object o, string name, params object[] a)
    {
        var mi = FindMethod(o.GetType(), name, a.Length);
        if (mi == null) return false;
        mi.Invoke(o, a);
        return true;
    }

    static bool BoolCall(object o, string name, params object[] a)
    {
        var r = Call(o, name, a);
        return r is bool && (bool)r;
    }

    static List<Control> AllControls(Control root)
    {
        var list = new List<Control>();
        Collect(root, list);
        return list;
    }

    static void Collect(Control root, List<Control> into)
    {
        foreach (Control c in root.Controls)
        {
            into.Add(c);
            if (c.Controls.Count > 0) Collect(c, into);
        }
    }

    static Button FindButton(Control root, string text)
    {
        foreach (var c in AllControls(root))
        {
            var b = c as Button;
            if (b != null && b.Text == text) return b;
        }
        return null;
    }

    static void Save(Bitmap bmp, string name)
    {
        bmp.Save(Out(name), ImageFormat.Png);
        Console.WriteLine("      shot -> " + name + "  " + bmp.Width + "x" + bmp.Height);
    }

    static int ColorDist(Color a, Color b)
    {
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    static int CountDiff(Bitmap a, Bitmap b, Rectangle r)
    {
        r.Intersect(new Rectangle(0, 0, Math.Min(a.Width, b.Width), Math.Min(a.Height, b.Height)));
        int n = 0;
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
                if (ColorDist(a.GetPixel(x, y), b.GetPixel(x, y)) > 24) n++;
        return n;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    // ---------------------------------------------------------------- 设置 / 面板

    static Settings FreshSettings()
    {
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.outlook.enabled = false;
        s.ui.edge = "free";
        s.note.id = "46040";
        s.note.style = "drawer";
        s.exceptions.Clear();
        return s;
    }

    static SettingsForm MakeSettingsForm(Settings s)
    {
        return new SettingsForm(new SettingsStore(), s, 0);
    }

    static NotePanelForm MakePanel(Settings s, string themeId, Rectangle card, Action<string> onDelete)
    {
        var theme = Themes.Get(themeId);
        ConstructorInfo best = null;
        foreach (var c in typeof(NotePanelForm).GetConstructors(FF))
            if (best == null || c.GetParameters().Length > best.GetParameters().Length) best = c;
        var ps = best.GetParameters();
        var args = new object[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            Type pt = ps[i].ParameterType;
            if (pt == typeof(Settings)) args[i] = s;
            else if (pt == typeof(ThemeDef)) args[i] = theme;
            else if (pt == typeof(Rectangle)) args[i] = card;
            else if (pt == typeof(bool)) args[i] = true;
            else if (pt == typeof(Action<string>) && i == ps.Length - 1) args[i] = onDelete;
            else args[i] = null;
        }
        return (NotePanelForm)best.Invoke(args);
    }

    static Bitmap RenderPanel(NotePanelForm p)
    {
        int w = (int)Get(p, "winWidth");
        int h = (int)(float)Get(p, "heightNow");
        var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h));
        bmp.SetResolution(144, 144);
        using (var g = Graphics.FromImage(bmp))
            TryCall(p, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height)));
        return bmp;
    }

    // ================================================================ 需求 1

    static void CheckMapping()
    {
        int[] expect = new int[] { 1, 2, 3, 4, 5, 6, 0 };
        var actual = new int[7];
        bool ok = true;
        for (int i = 0; i < 7; i++)
        {
            actual[i] = (int)CallStatic(typeof(SettingsForm), "WorkdayArrayIndex", i);
            if (actual[i] != expect[i]) ok = false;
        }
        Ok("A1 WorkdayArrayIndex(0..6) == 1,2,3,4,5,6,0（UI 周一..周日 → 数组 周日..周六）",
            ok, "actual=" + string.Join(",", Array.ConvertAll(actual, delegate(int x) { return x.ToString(); }))
            + " expect=1,2,3,4,5,6,0");
    }

    static void CheckLoadDays()
    {
        var s = FreshSettings();
        // { 周日, 周一..周五, 周六 } = { false,true,true,true,true,true,false } → 周一~周五上班
        s.workdays = new bool[] { false, true, true, true, true, true, false };
        using (var dlg = MakeSettingsForm(s))
        {
            var cb = (CheckBox[])Get(dlg, "cbDays");
            var states = new bool[7];
            var names = new string[7];
            for (int i = 0; i < 7; i++) { states[i] = cb[i].Checked; names[i] = cb[i].Text; }
            Ok("A2 打开设置：7 个复选框顺序是 周一..周日",
                string.Join(",", names) == "周一,周二,周三,周四,周五,周六,周日", "names=" + string.Join(",", names));
            Ok("A2b workdays={F,T,T,T,T,T,F}（周一~周五上班）→ 复选框勾选 周一..周五",
                string.Join(",", Array.ConvertAll(states, delegate(bool b) { return b ? "1" : "0"; })) == "1,1,1,1,1,0,0",
                "checked=" + JoinBools(states));
        }
    }

    static string JoinBools(bool[] a)
    {
        var p = new List<string>();
        foreach (var b in a) p.Add(b ? "1" : "0");
        return string.Join(",", p.ToArray());
    }

    static void CheckSaveDays()
    {
        var s = FreshSettings();
        using (var dlg = MakeSettingsForm(s))
        {
            var cb = (CheckBox[])Get(dlg, "cbDays");
            for (int i = 0; i < 7; i++) cb[i].Checked = i < 5;   // 周一~周五
            bool okSave = BoolCall(dlg, "Save", false);
            bool[] w = s.workdays;
            Ok("A3 勾选 周一~周五 → Save 后 workdays[0..6] = 0,1,1,1,1,1,0",
                okSave && !w[0] && w[1] && w[2] && w[3] && w[4] && w[5] && !w[6],
                "save=" + okSave + " workdays=" + JoinBools(w));
        }
    }

    static void CheckRoundTripDays()
    {
        // UI 第 i 个（0=周一）单独勾上 → 只有 workdays[(i+1)%7] 为 true
        bool all = true;
        string detail = "";
        for (int i = 0; i < 7; i++)
        {
            var s = FreshSettings();
            using (var dlg = MakeSettingsForm(s))
            {
                var cb = (CheckBox[])Get(dlg, "cbDays");
                for (int j = 0; j < 7; j++) cb[j].Checked = (j == i);
                BoolCall(dlg, "Save", false);
                int idx = (i + 1) % 7;
                bool only = s.workdays[idx];
                for (int k = 0; k < 7; k++) if (k != idx && s.workdays[k]) only = false;
                if (!only) all = false;
                detail += "UI" + i + "->arr" + idx + (only ? "✓ " : "✗ ");
            }
        }
        Ok("A4 7 天逐项往返：UI 第 i 个勾选 → 只写 workdays[(i+1)%7]", all, detail.Trim());
    }

    static void CheckPresets()
    {
        var s = FreshSettings();
        using (var dlg = MakeSettingsForm(s))
        {
            bool btnOk = FindButton(dlg, "周一~周五") != null
                && FindButton(dlg, "周一~周六") != null
                && FindButton(dlg, "每天") != null;
            Ok("A5 设置页有「周一~周五 / 周一~周六 / 每天」快捷按钮", btnOk,
                "btnMonFri=" + (FindButton(dlg, "周一~周五") != null)
                + " btnMonSat=" + (FindButton(dlg, "周一~周六") != null)
                + " btnEvery=" + (FindButton(dlg, "每天") != null));

            var cb = (CheckBox[])Get(dlg, "cbDays");
            TryInvoke(dlg, "SetWorkdayPreset", 5);
            bool ui5 = true;
            for (int i = 0; i < 7; i++) if (cb[i].Checked != (i < 5)) ui5 = false;
            BoolCall(dlg, "Save", false);
            bool arr5 = !s.workdays[0] && s.workdays[1] && s.workdays[2] && s.workdays[3] && s.workdays[4] && s.workdays[5] && !s.workdays[6];
            Ok("A5b 点「周一~周五」→ 复选框 5 个勾、保存后 workdays 周一~周五上班", ui5 && arr5,
                "ui=" + ui5 + " workdays=" + JoinBools(s.workdays));

            TryInvoke(dlg, "SetWorkdayPreset", 6);
            BoolCall(dlg, "Save", false);
            bool arr6 = s.workdays[1] && s.workdays[2] && s.workdays[3] && s.workdays[4] && s.workdays[5] && s.workdays[6] && !s.workdays[0];
            Ok("A5c 点「周一~周六」→ workdays 周一到周六上班、周日休息", arr6, "workdays=" + JoinBools(s.workdays));

            TryInvoke(dlg, "SetWorkdayPreset", 7);
            BoolCall(dlg, "Save", false);
            bool arr7 = true;
            for (int i = 0; i < 7; i++) if (!s.workdays[i]) arr7 = false;
            Ok("A5d 点「每天」→ 7 天都上班", arr7, "workdays=" + JoinBools(s.workdays));
        }
    }

    static void CheckPolicy()
    {
        var s = FreshSettings();
        s.mode = "fixed";
        s.startTime = "09:00";
        s.endTime = "18:00";
        s.exceptions.Clear();
        // 和 UI 相同的操作路径：勾周一~周五 → Save
        using (var dlg = MakeSettingsForm(s))
        {
            var cb = (CheckBox[])Get(dlg, "cbDays");
            for (int i = 0; i < 7; i++) cb[i].Checked = i < 5;
            BoolCall(dlg, "Save", false);
        }
        var fri = new DateTime(2026, 11, 13);   // 周五
        var sat = new DateTime(2026, 11, 14);
        var sun = new DateTime(2026, 11, 15);
        var mon = new DateTime(2026, 11, 16);
        bool friWork = (bool)Get(CallStatic(typeof(Policy), "Build", s, fri), "isWorkday");
        bool monWork = (bool)Get(CallStatic(typeof(Policy), "Build", s, mon), "isWorkday");
        bool satWork = (bool)Get(CallStatic(typeof(Policy), "Build", s, sat), "isWorkday");
        bool sunWork = (bool)Get(CallStatic(typeof(Policy), "Build", s, sun), "isWorkday");
        int friDw = (int)fri.DayOfWeek;
        Ok("A6 Policy.Build：修复后周五=上班、周六/周日=休息（旧代码周五会变休息）",
            friDw == 5 && friWork && monWork && !satWork && !sunWork,
            "2026-11-13 是周" + friDw + "：friday.isWorkday=" + friWork + " monday=" + monWork
            + " saturday=" + satWork + " sunday=" + sunWork + " workdays=" + JoinBools(s.workdays));
    }

    // ================================================================ 需求 2：模型

    static void CheckNickModel()
    {
        // CleanNick / MaxNick / Normalize
        int maxNick = (int)GetStaticConst(typeof(NoteSettings), "MaxNick");
        Ok("B1 NoteSettings.MaxNick == 12", maxNick == 12, "MaxNick=" + maxNick);
        string cnNull = (string)CallStatic(typeof(NoteSettings), "CleanNick", new object[] { null });
        string cnTrim = (string)CallStatic(typeof(NoteSettings), "CleanNick", "  阿哲  ");
        string cnLong = (string)CallStatic(typeof(NoteSettings), "CleanNick", "一二三四五六七八九十甲乙丙");
        Ok("B2 CleanNick：null→\"\"、去首尾空格、超 12 字截断",
            cnNull == "" && cnTrim == "阿哲" && cnLong.Length == 12,
            "clean(null)=[" + (cnNull ?? "<null>") + "] clean(阿哲)=[" + cnTrim + "] len(long)=" + (cnLong == null ? -1 : cnLong.Length));

        var note = new NoteSettings();
        note.nickname = "  阿哲  ";
        note.Normalize();
        Ok("B3 Normalize：nickname 去空格、超长截 12", note.nickname == "阿哲", "nickname=[" + note.nickname + "]");

        // NoteContact.nick / Display 优先级
        var c = new NoteContact();
        c.id = "10001";
        c.remark = "";
        c.nick = "";
        Ok("B4 新联系人 nick 默认空、Display 退回 ID",
            c.nick == "" && c.Display() == "ID 10001", "nick=[" + c.nick + "] display=" + c.Display());
        c.nick = "小满";
        Ok("B5 有昵称、无备注 → Display 显示昵称", c.Display() == "小满", "display=" + c.Display());
        c.remark = "满哥";
        Ok("B6 备注优先于昵称", c.Display() == "满哥", "display=" + c.Display());
        var room = new NoteContact();
        room.id = "abcde";
        room.nick = "";
        Ok("B7 对话码无昵称 → Display「对话 abcde」", room.Display() == "对话 abcde", "display=" + room.Display());

        // SetContactNick
        note = new NoteSettings();
        note.id = "46040";
        note.contacts.Add(c);
        bool set1 = (bool)Call(note, "SetContactNick", "10001", "  新昵称  ");
        bool noChange = (bool)Call(note, "SetContactNick", "10001", "新昵称");
        bool missing = (bool)Call(note, "SetContactNick", "99999", "别人");
        bool longSet = (bool)Call(note, "SetContactNick", "10001", "一二三四五六七八九十甲乙丙");
        string stored = note.Contact("10001").nick;
        Ok("B8 SetContactNick：trim 后写入、没变化返回 false、联系人不存在返回 false、超 12 截断",
            set1 && !noChange && !missing && longSet && notedLength(stored) == 12,
            "set=" + set1 + " noChange=" + noChange + " missing=" + missing
            + " stored=[" + stored + "] len=" + notedLength(stored));

        // NoteMessage.fromNick
        var m = new NoteMessage("11111", "abcde", "你好", NoteRelay.Now(), "小甲");
        Ok("B9 NoteMessage 带发送者昵称 fromNick", m.fromNick == "小甲", "fromNick=" + m.fromNick);
    }

    static int notedLength(string s) { return s == null ? 0 : s.Length; }

    static object GetStaticConst(Type t, string name)
    {
        var f = t.GetField(name, FF);
        if (f != null) return f.GetValue(null);
        var p = t.GetProperty(name, FF);
        return p == null ? null : p.GetValue(null, null);
    }

    // ================================================================ 需求 2：中继端到端

    class Packet { public int type; public int flags; public byte[] body; }

    static Packet ReadPacket(NetworkStream st)
    {
        try
        {
            int first = st.ReadByte();
            if (first < 0) return null;
            int len = 0, mult = 1, b;
            do
            {
                b = st.ReadByte();
                if (b < 0) return null;
                len += (b & 0x7f) * mult;
                mult *= 128;
            } while ((b & 0x80) != 0);
            var body = new byte[len];
            int read = 0;
            while (read < len)
            {
                int n = st.Read(body, read, len - read);
                if (n <= 0) return null;
                read += n;
            }
            return new Packet { type = (first >> 4) & 0x0f, flags = first & 0x0f, body = body };
        }
        catch { return null; }
    }

    static string PublishTopic(int flags, byte[] body, out string payload)
    {
        payload = null;
        try
        {
            int len = (body[0] << 8) | body[1];
            string topic = Encoding.UTF8.GetString(body, 2, len);
            int idx = 2 + len;
            int qos = (flags >> 1) & 3;
            if (qos > 0) idx += 2;
            payload = Encoding.UTF8.GetString(body, idx, body.Length - idx);
            return topic;
        }
        catch { return null; }
    }

    static void CollectSubTopics(byte[] body, List<string> into)
    {
        try
        {
            int i = 2;
            while (i + 2 <= body.Length)
            {
                int len = (body[i] << 8) | body[i + 1];
                i += 2;
                if (i + len > body.Length) break;
                into.Add(Encoding.UTF8.GetString(body, i, len));
                i += len + 1;
            }
        }
        catch { }
    }

    class ProfileRec { public string id, nick; public bool online; public int thread; public long atMs; }

    static void CheckRelayNickEndToEnd()
    {
        var s = FreshSettings();
        s.note.id = "46040";
        s.note.nickname = "阿哲";
        s.note.contacts.Clear();
        s.note.threads.Clear();
        s.note.AddContact("10001", "");
        s.note.AddContact("10002", "");
        s.note.AddConversation("abcde", "");
        s.note.relayHost = "127.0.0.1";

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        s.note.relayPort = port;

        var relay = new NoteRelay(s.note);
        var profiles = new List<ProfileRec>();
        var evt = new ManualResetEvent(false);
        relay.Profile += delegate(string id, string nick, bool online)
        {
            lock (profiles)
            {
                profiles.Add(new ProfileRec
                {
                    id = id, nick = nick, online = online,
                    thread = Thread.CurrentThread.ManagedThreadId,
                    atMs = Environment.TickCount
                });
            }
            evt.Set();
        };

        TcpClient client = null;
        NetworkStream stream = null;
        try
        {
            relay.Start();
            var accept = listener.BeginAcceptTcpClient(null, null);
            if (!accept.AsyncWaitHandle.WaitOne(5000)) throw new Exception("假 broker 等不到 NoteRelay 连接");
            client = listener.EndAcceptTcpClient(accept);
            stream = client.GetStream();
            stream.ReadTimeout = 500;
            var connect = ReadPacket(stream);
            if (connect == null || connect.type != 1) throw new Exception("没收到 CONNECT");
            stream.Write(new byte[] { 0x20, 0x02, 0x00, 0x00 }, 0, 4);   // CONNACK
            long connAt = Environment.TickCount;

            // ---- 收集订阅 + 自己的 retained presence ----
            var subs = new List<string>();
            string ownPresence = null;
            bool ownRetain = false;
            var pubs = new List<string>();
            CollectFor(stream, subs, pubs, delegate(string topic, string payload, int flags)
            {
                if (topic == NoteRelay.PresenceTopic("46040")) { ownPresence = payload; ownRetain = (flags & 1) != 0; return true; }
                return false;
            }, 2500);

            Ok("C1 连上中继后订阅了所有联系人的 presence（个人 ID）+ 房间主题",
                subs.Contains(NoteRelay.PresenceTopic("10001"))
                && subs.Contains(NoteRelay.PresenceTopic("10002"))
                && subs.Contains(NoteRelay.RoomTopic("abcde")),
                "subs=[" + string.Join(",", subs.ToArray()) + "]");
            Ok("C2 CONNACK 后广播自己的 retained presence，payload 带昵称「阿哲」",
                ownPresence != null && ownRetain && ownPresence.Contains("\"nick\":\"阿哲\""),
                "ownPresence=" + (ownPresence ?? "<null>") + " retain=" + ownRetain);

            // ---- 收到对方的 retained presence → Profile 事件（网络线程） ----
            string p1 = NoteRelay.PresenceJson("10001", "小满");
            SendServerPublish(stream, NoteRelay.PresenceTopic("10001"), p1, true);
            bool got1 = evt.WaitOne(3000);
            ProfileRec r1 = null;
            lock (profiles) { if (profiles.Count > 0) r1 = profiles[0]; }
            Ok("C3 收到对方 retained presence → Profile 事件带 (id=10001, nick=小满, online=true)，且在非主线程 raise",
                got1 && r1 != null && r1.id == "10001" && r1.nick == "小满" && r1.online && r1.thread != mainThread,
                got1 ? ("id=" + r1.id + " nick=" + r1.nick + " online=" + r1.online + " thread=" + r1.thread + "(main=" + mainThread + ")") : "3s 内没收到 Profile");

            // ---- 过期 presence → online=false ----
            evt.Reset();
            long staleTs = NoteRelay.Now() - 600;
            string p2 = "{\"v\":1,\"id\":\"10002\",\"ts\":" + staleTs + ",\"nick\":\"老周\"}";
            SendServerPublish(stream, NoteRelay.PresenceTopic("10002"), p2, true);
            bool got2 = evt.WaitOne(3000);
            ProfileRec r2 = null;
            lock (profiles) { if (profiles.Count > 1) r2 = profiles[1]; }
            Ok("C4 过期 presence（600 秒前）→ Profile online=false，昵称仍解出「老周」",
                got2 && r2 != null && r2.id == "10002" && r2.nick == "老周" && !r2.online,
                got2 ? ("id=" + r2.id + " nick=" + r2.nick + " online=" + r2.online) : "3s 内没收到 Profile");

            // ---- RefreshSubscriptions 重发自己的 presence（改昵称立刻广播） ----
            s.note.nickname = "阿哲2";
            var pubs2 = new List<string>();
            string refreshPresence = null;
            relay.RefreshSubscriptions();
            CollectFor(stream, new List<string>(), pubs2, delegate(string topic, string payload, int flags)
            {
                if (topic == NoteRelay.PresenceTopic("46040")) { refreshPresence = payload; return true; }
                return false;
            }, 2500);
            Ok("C5 RefreshSubscriptions 重发自己的 retained presence，带新昵称「阿哲2」",
                refreshPresence != null && refreshPresence.Contains("\"nick\":\"阿哲2\""),
                "presence=" + (refreshPresence ?? "<null>"));

            // ---- SendMessage payload 带昵称 ----
            bool sent = relay.SendMessage("10001", "你好");
            string sentPayload = null, sentTopic = null;
            CollectFor(stream, new List<string>(), new List<string>(), delegate(string topic, string payload, int flags)
            {
                if (topic == NoteRelay.InboxTopic("10001")) { sentTopic = topic; sentPayload = payload; return true; }
                return false;
            }, 3000);
            Ok("C6 发 1:1 消息 payload 带发送者昵称（当前 cfg.nickname=阿哲2）",
                sent && sentTopic == NoteRelay.InboxTopic("10001") && sentPayload != null && sentPayload.Contains("\"nick\":\"阿哲2\""),
                "sent=" + sent + " topic=" + (sentTopic ?? "<null>") + " payload=" + (sentPayload ?? "<null>"));

            // ---- DecodeMessage 解出 nick ----
            string dmPayload = NoteRelay.BuildMessageJson("11111", "abcde", "群消息", NoteRelay.Now(), "小甲");
            NoteIncoming inc = NoteRelay.DecodeMessage(dmPayload);
            Ok("C7 DecodeMessage 解出发送者昵称", inc != null && inc.nick == "小甲" && inc.from == "11111",
                inc == null ? "null" : "from=" + inc.from + " nick=[" + inc.nick + "]");

            // ---- keepalive 重播昵称（15s 定时器，最多等 18s） ----
            bool keepalive = false;
            string kaPayload = null;
            long deadline = Environment.TickCount + 18000;
            while (Environment.TickCount < deadline && !keepalive)
            {
                var p = ReadPacket(stream);
                if (p == null) continue;
                if (p.type == 3)
                {
                    string payload2;
                    string topic = PublishTopic(p.flags, p.body, out payload2);
                    if (topic == NoteRelay.PresenceTopic("46040")
                        && Environment.TickCount - connAt >= 14000
                        && payload2 != null && payload2.Contains("\"nick\":\"阿哲2\""))
                    {
                        keepalive = true; kaPayload = payload2;
                    }
                }
            }
            Ok("C8 keepalive（约 15s）会重播自己的 retained presence 昵称",
                keepalive, "keepalive=" + keepalive + " payload=" + (kaPayload ?? "<null>") + " elapsed>=" + (Environment.TickCount - connAt) + "ms");
        }
        finally
        {
            try { relay.Stop(); } catch { }
            try { relay.Dispose(); } catch { }
            try { if (stream != null) stream.Close(); } catch { }
            try { if (client != null) client.Close(); } catch { }
            try { listener.Stop(); } catch { }
        }
    }

    static void SendServerPublish(NetworkStream st, string topic, string payload, bool retain)
    {
        var pkt = NoteRelay.PublishPacket(topic, payload, retain);
        st.Write(pkt, 0, pkt.Length);
        st.Flush();
    }

    /// <summary>从 broker 侧收包：记录订阅主题 / 发布主题；handler 返回 true 时提前结束。</summary>
    static void CollectFor(NetworkStream st, List<string> subs, List<string> pubs,
        Func<string, string, int, bool> onPublish, int timeoutMs)
    {
        long deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            var p = ReadPacket(st);
            if (p == null) continue;
            if (p.type == 8) { if (subs != null) CollectSubTopics(p.body, subs); continue; }
            if (p.type == 3)
            {
                string payload;
                string topic = PublishTopic(p.flags, p.body, out payload);
                if (topic != null && pubs != null) pubs.Add(topic);
                if (topic != null && onPublish != null && onPublish(topic, payload, p.flags)) return;
            }
        }
    }

    // ================================================================ 需求 2：UI

    static void CheckNickUi()
    {
        // --- 设置页昵称输入框 ---
        var s = FreshSettings();
        s.note.nickname = "阿哲";
        using (var dlg = MakeSettingsForm(s))
        {
            var tb = Get(dlg, "tbNoteNick") as TextBox;
            Ok("D1 设置 → 纸条 有昵称输入框 tbNoteNick，读出现有昵称、最长 12 字",
                tb != null && tb.Text == "阿哲" && tb.MaxLength == 12,
                tb == null ? "没有 tbNoteNick" : "text=[" + tb.Text + "] maxLen=" + tb.MaxLength);
            if (tb != null)
            {
                tb.Text = "  小满  ";
                BoolCall(dlg, "Save", false);
                Ok("D2 设置页保存昵称（去首尾空格）→ s.note.nickname == 小满",
                    s.note.nickname == "小满", "nickname=[" + s.note.nickname + "]");
            }
        }

        // --- 面板头部「我的 ID xxxxx · 昵称」---
        var a = FreshSettings();
        a.note.nickname = "阿哲";
        var b = FreshSettings();
        b.note.nickname = "";
        using (var pa = MakePanel(a, "huang-liuli", new Rectangle(840, 90, 420, 282), null))
        using (var pb = MakePanel(b, "huang-liuli", new Rectangle(840, 90, 420, 282), null))
        {
            using (var ba = RenderPanel(pa))
            using (var bb = RenderPanel(pb))
            {
                Save(ba, "note-header-nick.png");
                Save(bb, "note-header-empty.png");
                var header = new Rectangle(60, 36, 380, 48);   // 头部副标题「我的 ID ... · ...」区域
                int diff = CountDiff(ba, bb, header);
                Ok("D3 面板头部显示昵称：有昵称 / 无昵称时副标题区域像素不同（截图 note-header-*.png）",
                    diff >= 10, "headerDiff=" + diff + "px");
            }
        }

        // --- 群聊气泡 / 历史行显示发送者昵称 ---
        var s2 = FreshSettings();
        s2.note.id = "46040";
        s2.note.nickname = "阿哲";
        s2.note.contacts.Clear();
        s2.note.threads.Clear();
        s2.note.AddConversation("abcde", "");
        s2.note.AddContact("10001", "");
        s2.note.Contact("10001").nick = "小满";
        var c11111 = new NoteContact();
        c11111.id = "11111";
        c11111.nick = "小甲";
        s2.note.contacts.Add(c11111);
        using (var p = MakePanel(s2, "huang-liuli", new Rectangle(900, 120, 470, 298), null))
        {
            TryCall(p, "SelectContact", "abcde", false);
            long now = NoteRelay.Now();
            var m1 = new NoteMessage("11111", "abcde", "你好", now - 30, "小甲");
            var m2 = new NoteMessage("10001", "abcde", "在吗", now - 20);
            var m3 = new NoteMessage("22222", "abcde", "没有昵称的人", now - 10);
            var m4 = new NoteMessage("46040", "abcde", "我自己", now - 5, "阿哲");
            string b1 = (string)Call(p, "BubbleText", m1, true);
            string b2 = (string)Call(p, "BubbleText", m2, true);
            string b3 = (string)Call(p, "BubbleText", m3, true);
            string b4 = (string)Call(p, "BubbleText", m4, true);
            Ok("D4 群聊气泡：消息带昵称→「小甲：…」；无 fromNick 但联系人有昵称→「小满：…」；都没有→「ID 22222：…」；自己发的不加前缀",
                b1 == "小甲：你好" && b2 == "小满：在吗" && b3 == "ID 22222：没有昵称的人" && b4 == "我自己",
                "m1=[" + b1 + "] m2=[" + b2 + "] m3=[" + b3 + "] m4=[" + b4 + "]");

            string w1 = (string)Call(p, "WhoLabel", m1);
            string w2 = (string)Call(p, "WhoLabel", m2);
            string w3 = (string)Call(p, "WhoLabel", m3);
            string w4 = (string)Call(p, "WhoLabel", m4);
            Ok("D5 历史行「谁」：优先 fromNick，其次本机备注/昵称，最后「ID xxxxx」；自己=你",
                w1 == "小甲" && w2 == "小满" && w3 == "ID 22222" && w4 == "你",
                "w1=[" + w1 + "] w2=[" + w2 + "] w3=[" + w3 + "] w4=[" + w4 + "]");

            // 备注优先于昵称
            s2.note.Contact("10001").remark = "满哥";
            string b2r = (string)Call(p, "BubbleText", m2, true);
            string w2r = (string)Call(p, "WhoLabel", m2);
            Ok("D6 群聊里本机备注优先于对方昵称", b2r == "满哥：在吗" && w2r == "满哥",
                "bubble=[" + b2r + "] who=[" + w2r + "]");

            // 渲染一张群聊截图（把消息真的塞进线程，气泡里能看到发送者昵称）
            s2.note.Contact("10001").remark = "";   // 截图用「没有本机备注 → 显示对方昵称」这一档
            s2.note.Append(m1, true);
            s2.note.Append(m2, true);
            s2.note.Append(m3, true);
            s2.note.Append(m4, false);
            TryCall(p, "SelectContact", "abcde", false);
            Set(p, "heightNow", Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 900);
            using (var bmp = RenderPanel(p)) Save(bmp, "note-room-nick.png");
        }

        // --- 个人对话：没备注时列表显示对方昵称 ---
        var s3 = FreshSettings();
        s3.note.id = "46040";
        s3.note.contacts.Clear();
        s3.note.threads.Clear();
        s3.note.AddContact("10001", "");
        s3.note.Contact("10001").nick = "小满";
        Ok("D7 个人对话没备注时显示对方昵称（NoteContact.Display）",
            s3.note.Contact("10001").Display() == "小满", "display=" + s3.note.Contact("10001").Display());
    }

    // ================================================================ 需求 2：Widget 接线

    static void CheckWidgetNick()
    {
        var s = FreshSettings();
        s.note.id = "46040";
        s.note.contacts.Clear();
        s.note.threads.Clear();
        s.note.AddContact("10001", "");
        s.note.AddConversation("abcde", "");
        s.note.Append(new NoteMessage("10001", "46040", "早上好", NoteRelay.Now()), true);

        using (var w = new WidgetForm(new SettingsStore(), s))
        {
            IntPtr h = w.Handle;   // OnNoteProfile / OnNoteMessage 用 BeginInvoke
            var miProfile = FindMethod(typeof(WidgetForm), "OnNoteProfile", 3);
            if (miProfile == null) { Ok("E1 Widget 有 OnNoteProfile 接线", false, "找不到 OnNoteProfile"); return; }

            miProfile.Invoke(w, new object[] { "10001", "小满", true });
            PumpUntil(delegate { return s.note.Contact("10001") != null && s.note.Contact("10001").nick == "小满"; }, 2500);
            Ok("E1 Widget.OnNoteProfile 把对方昵称写进 contact.nick",
                s.note.Contact("10001").nick == "小满", "nick=[" + s.note.Contact("10001").nick + "]");

            int before = s.note.contacts.Count;
            miProfile.Invoke(w, new object[] { "99999", "陌生人", true });
            PumpUntil(delegate { return false; }, 300);
            Ok("E2 收到陌生 ID 的 profile 不会凭空建联系人", s.note.contacts.Count == before,
                "contacts " + before + "->" + s.note.contacts.Count);

            // 消息里带的昵称：1:1
            var miMsg = FindMethod(typeof(WidgetForm), "OnNoteMessage", 1);
            if (miMsg == null) { Ok("E3 Widget.OnNoteMessage 接线", false, "找不到 OnNoteMessage"); return; }
            NoteIncoming in1 = new NoteIncoming();
            in1.from = "10001"; in1.to = "46040"; in1.text = "带昵称的消息"; in1.ts = NoteRelay.Now(); in1.nick = "小满2";
            miMsg.Invoke(w, new object[] { in1 });
            PumpUntil(delegate
            {
                var t = FindThread(s.note, "10001");
                return t != null && t.messages.Count >= 2 && t.messages[t.messages.Count - 1].fromNick == "小满2";
            }, 2500);
            var th = FindThread(s.note, "10001");
            var last = th != null && th.messages.Count > 0 ? th.messages[th.messages.Count - 1] : null;
            Ok("E3 收到消息时用 msg.nick 补昵称（contact.nick + 消息 fromNick）",
                s.note.Contact("10001").nick == "小满2" && last != null && last.fromNick == "小满2",
                "contactNick=[" + s.note.Contact("10001").nick + "] msgFromNick=[" + (last == null ? "-" : last.fromNick) + "]");

            // 群聊消息里的昵称：fromNick 存进线程
            NoteIncoming in2 = new NoteIncoming();
            in2.from = "22222"; in2.to = "abcde"; in2.text = "群里的消息"; in2.ts = NoteRelay.Now(); in2.nick = "乙";
            miMsg.Invoke(w, new object[] { in2 });
            PumpUntil(delegate
            {
                var t = FindThread(s.note, "abcde");
                return t != null && t.messages.Count >= 1 && t.messages[t.messages.Count - 1].fromNick == "乙";
            }, 2500);
            var roomTh = FindThread(s.note, "abcde");
            var roomLast = roomTh != null && roomTh.messages.Count > 0 ? roomTh.messages[roomTh.messages.Count - 1] : null;
            Ok("E4 群聊消息带发送者昵称（fromNick 落进线程）",
                roomLast != null && roomLast.fromNick == "乙" && roomLast.from == "22222",
                "roomLast from=[" + (roomLast == null ? "-" : roomLast.from) + "] fromNick=[" + (roomLast == null ? "-" : roomLast.fromNick) + "]");

            // 接线存在性：ApplySettings → RestartNoteRelay（设置保存后 relay 重启会用新昵称）
            bool hasApply = FindMethod(typeof(WidgetForm), "ApplySettings", 0) != null;
            bool hasRestart = FindMethod(typeof(WidgetForm), "RestartNoteRelay", 0) != null;
            Ok("E5 Widget 有 ApplySettings → RestartNoteRelay 的接线（设置保存后会重建 relay 广播新昵称）",
                hasApply && hasRestart, "ApplySettings=" + hasApply + " RestartNoteRelay=" + hasRestart);
        }
    }

    // ================================================================ 增量：v3.5 旧配置工作日迁移

    static bool ArraysEqual(bool[] a, bool[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static bool[] CallMigrate(bool[] w, int version)
    {
        return (bool[])CallStatic(typeof(SettingsExtensions), "MigrateLegacyWorkdays", w, version);
    }

    static void CheckMigrateFunction()
    {
        bool[] broken = new bool[] { true, true, true, true, true, false, false };
        bool[] brokenCopy = (bool[])broken.Clone();
        bool[] expect = new bool[] { false, true, true, true, true, true, false };
        bool[] fixedArr = CallMigrate(broken, 1);
        Ok("M1 MigrateLegacyWorkdays：version=1 + 旧错位形态 → 右移一位还原成周一~周五（不改入参）",
            ArraysEqual(fixedArr, expect) && ArraysEqual(broken, brokenCopy),
            "in=" + JoinBools(broken) + " out=" + JoinBools(fixedArr) + " expect=" + JoinBools(expect));

        var normal = new bool[] { false, true, true, true, true, true, false };
        var normalOut = CallMigrate(normal, 1);
        var brokenV2 = new bool[] { true, true, true, true, true, false, false };
        var v2Out = CallMigrate(brokenV2, 2);
        var nullOut = CallMigrate(null, 1);
        var len6 = new bool[] { true, true, true, true, true, false };
        var len6Out = CallMigrate(len6, 1);
        Ok("M2 迁移边界：正常配置不动、version>=2 不迁移、null / 长度不对原样返回",
            ReferenceEquals(normalOut, normal) && ReferenceEquals(v2Out, brokenV2)
            && nullOut == null && ReferenceEquals(len6Out, len6),
            "normalSame=" + ReferenceEquals(normalOut, normal) + " v2Same=" + ReferenceEquals(v2Out, brokenV2)
            + " null=" + (nullOut == null) + " len6Same=" + ReferenceEquals(len6Out, len6));
    }

    static void CheckNormalizeMigration()
    {
        // M3：用户真机旧形态「周日 + 周一~周四上班、周五周六休」→ 周一~周五
        var s = new Settings();
        s.version = 1;
        s.workdays = new bool[] { true, true, true, true, true, false, false };
        s.exceptions.Clear();
        s.Normalize();
        bool friWork = (bool)Get(CallStatic(typeof(Policy), "Build", s, new DateTime(2026, 11, 13)), "isWorkday");
        Ok("M3 Normalize 迁移旧错位配置 → workdays=0,1,1,1,1,1,0、version=2、周五=上班",
            ArraysEqual(s.workdays, new bool[] { false, true, true, true, true, true, false })
            && s.version == 2 && friWork,
            "workdays=" + JoinBools(s.workdays) + " version=" + s.version + " friday.isWorkday=" + friWork);

        // M4：旧「周一~周六」形态 → 迁移后仍是周一~周六
        var s2 = new Settings();
        s2.version = 1;
        s2.workdays = new bool[] { true, true, true, true, true, true, false };
        s2.exceptions.Clear();
        s2.Normalize();
        bool sat = (bool)Get(CallStatic(typeof(Policy), "Build", s2, new DateTime(2026, 11, 14)), "isWorkday");
        bool sun = (bool)Get(CallStatic(typeof(Policy), "Build", s2, new DateTime(2026, 11, 15)), "isWorkday");
        Ok("M4 旧「周一~周六」形态迁移后仍周一~周六上班（周六 true、周日 false）",
            ArraysEqual(s2.workdays, new bool[] { false, true, true, true, true, true, true }) && sat && !sun,
            "workdays=" + JoinBools(s2.workdays) + " sat=" + sat + " sun=" + sun);

        // M5：幂等
        var before = (bool[])s.workdays.Clone();
        int v0 = s.version;
        s.Normalize();
        Ok("M5 幂等：迁移后再 Normalize 一次结果不变（version 仍 2）",
            ArraysEqual(before, s.workdays) && s.version == v0,
            "before=" + JoinBools(before) + " after=" + JoinBools(s.workdays) + " version=" + s.version);
    }

    static void CheckNoFalseMigration()
    {
        var def = new Settings();
        def.version = 1;
        def.workdays = new bool[] { false, true, true, true, true, true, false };
        def.Normalize();
        Ok("M6 默认「周一~周五」正常配置不被误迁移",
            ArraysEqual(def.workdays, new bool[] { false, true, true, true, true, true, false }),
            "workdays=" + JoinBools(def.workdays));

        var v2 = new Settings();
        v2.version = 2;
        v2.workdays = new bool[] { true, true, true, true, true, false, false };
        v2.Normalize();
        Ok("M7 已是 version=2 的旧形态不再迁移（只迁一次）",
            ArraysEqual(v2.workdays, new bool[] { true, true, true, true, true, false, false }) && v2.version == 2,
            "workdays=" + JoinBools(v2.workdays) + " version=" + v2.version);

        bool okShapes = true;
        string detail = "";
        var shapes = new List<bool[]>();
        shapes.Add(new bool[] { true, true, true, true, true, true, true });       // 每天
        shapes.Add(new bool[] { false, false, false, false, false, false, false }); // 全休
        shapes.Add(new bool[] { false, false, true, true, true, false, false });
        shapes.Add(new bool[] { true, false, true, true, true, false, false });
        foreach (var sh in shapes)
        {
            var st = new Settings();
            st.version = 1;
            st.workdays = (bool[])sh.Clone();
            st.Normalize();
            if (!ArraysEqual(st.workdays, sh)) { okShapes = false; detail += "MISMATCH "; }
        }
        Ok("M8 其它正常/非常形态不被误迁移（每天、全休、周二~周四、周一+周三~周四）",
            okShapes, "shapes=" + shapes.Count + " " + detail.Trim());
    }

    static void CheckStoreLoadMigration()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string portable = Path.Combine(baseDir, "portable.txt");
        string dataDir = Path.Combine(baseDir, "data");
        string cfgPath = Path.Combine(dataDir, "settings.json");
        string oldSave = Environment.GetEnvironmentVariable("WC_NO_SAVE");
        try
        {
            CleanupPortable(baseDir);
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(portable, "portable", new UTF8Encoding(false));

            var legacy = new Settings();
            legacy.version = 1;
            legacy.workdays = new bool[] { true, true, true, true, true, false, false };
            var ser = new JavaScriptSerializer();
            string json = ser.Serialize(legacy);
            File.WriteAllText(cfgPath, json, new UTF8Encoding(false));

            var store = new SettingsStore();
            bool portableOk = store.Portable
                && string.Equals(store.SettingsPath, cfgPath, StringComparison.OrdinalIgnoreCase);

            // WC_NO_SAVE=1：内存里迁移，但不写盘
            var loaded = store.Load();
            var diskObj = ser.Deserialize<Settings>(File.ReadAllText(cfgPath, Encoding.UTF8));
            int diskVersion = (int)Get(diskObj, "version");
            Ok("M9 SettingsStore.Load（WC_NO_SAVE=1）：内存里迁移、但不写盘",
                portableOk
                && ArraysEqual(loaded.workdays, new bool[] { false, true, true, true, true, true, false })
                && loaded.version == 2 && diskVersion == 1,
                "portable=" + portableOk + " path=" + store.SettingsPath
                + " loaded=" + JoinBools(loaded.workdays) + "/v" + loaded.version + " disk=v" + diskVersion);

            // 允许写盘：Load 迁移后立刻落盘
            Environment.SetEnvironmentVariable("WC_NO_SAVE", null);
            File.WriteAllText(cfgPath, json, new UTF8Encoding(false));
            var loaded2 = store.Load();
            var diskObj2 = ser.Deserialize<Settings>(File.ReadAllText(cfgPath, Encoding.UTF8));
            var diskDays = (bool[])Get(diskObj2, "workdays");
            int diskVer2 = (int)Get(diskObj2, "version");
            Ok("M10 SettingsStore.Load 迁移后立刻落盘：磁盘 version=2、workdays=0,1,1,1,1,1,0",
                loaded2.version == 2 && diskVer2 == 2
                && ArraysEqual(diskDays, new bool[] { false, true, true, true, true, true, false }),
                "memory=v" + loaded2.version + " disk=v" + diskVer2 + " diskWorkdays=" + JoinBools(diskDays));

            // 已是 version=2：Load 不再重复写盘
            DateTime mtime = File.GetLastWriteTimeUtc(cfgPath);
            Thread.Sleep(40);
            var loaded3 = store.Load();
            DateTime mtime2 = File.GetLastWriteTimeUtc(cfgPath);
            Ok("M11 已是 version=2 的配置 Load 不再重复写盘（文件 mtime 不变）",
                mtime == mtime2 && loaded3.version == 2,
                "mtime " + mtime.ToString("HH:mm:ss.fff") + " -> " + mtime2.ToString("HH:mm:ss.fff"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WC_NO_SAVE", oldSave == null ? "1" : oldSave);
            CleanupPortable(baseDir);
        }
    }

    static void CleanupPortable(string baseDir)
    {
        try
        {
            string portable = Path.Combine(baseDir, "portable.txt");
            string dataDir = Path.Combine(baseDir, "data");
            if (File.Exists(portable)) File.Delete(portable);
            if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
        }
        catch { }
    }

    static NoteThread FindThread(NoteSettings note, string id)
    {
        foreach (var t in note.threads) if (t != null && t.contact == id) return t;
        return null;
    }

    static void PumpUntil(Func<bool> cond, int timeoutMs)
    {
        long deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            Application.DoEvents();
            if (cond()) return;
            Thread.Sleep(15);
        }
    }
}
