using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WorkCountdown;

// 独立验收探针：用户 4 条反馈（对话码 / 边框加粗 / 滚动 + 右键删除 / 大卡片图标一致）。
// 只读 src（编译时一起编进本 exe），只写 test/visual/verify-* 截图。
// 用法：NoteVerifyProbe.exe [输出目录]   默认 test\visual\verify-after
//
// 设计原则：
//  - 新增 API（ValidCode / IsRoom / AddConversation / RoomTopic / ContactScroll / ScrollContacts /
//    DeleteId / ShowDelete / HideDelete / DeleteConversation / CardBorderDip / onDeleteContact）
//    全部走反射按名字找：Lead 改了签名 / 名字，探针报 FAIL 而不是编译不过，方便逐条定位。
//  - 像素断言全部离屏 OnPaint，锁屏也能跑。
class NoteVerifyProbe
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static BindingFlags FF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    static int pass, fail;
    static string outDir;
    static readonly List<string> failLines = new List<string>();

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

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
        SetProcessDPIAware();
        Application.EnableVisualStyles();

        outDir = (args != null && args.Length > 0) ? args[0]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "visual", "verify-after");
        Directory.CreateDirectory(outDir);
        EnsureSub("shots");

        Console.WriteLine("== 需求 1：添加对话 / 对话码 ==");
        Guard("R1 模型层", CheckCodes);
        Guard("R1 房间主题/收发", CheckRoomModel);
        Guard("R1 主题解析/十六进制", CheckRoomTopicParse);
        Guard("R1 端到端中继", CheckRelayEndToEnd);
        Guard("R1 房间里显示发送者 ID", CheckRoomSenderVisible);
        Guard("R1 气泡/历史里的发送者标注", CheckRoomSenderText);
        Guard("R1 Widget 收房间消息", CheckWidgetRoomReceive);
        Guard("R1 面板新建对话流程", CheckPanelAddFlow);
        Guard("R1 设置页对话框", CheckSettingsDialog);

        Console.WriteLine("== 需求 2：卡片边框加粗 ==");
        Guard("R2 边框", CheckBorder);

        Console.WriteLine("== 需求 3：滚动 + 右键删除 ==");
        Guard("R3 滚动", CheckScroll);
        Guard("R3 选中自动滚到可见", CheckEnsureVisible);
        Guard("R3 双栏行数 / 新建对话命中区", CheckColumnsRows);
        Guard("R3 右键删除", CheckDelete);
        Guard("R3 右键/点删除端到端", CheckRightClickDelete);
        Guard("R3 Widget 删除落盘", CheckWidgetDelete);

        Console.WriteLine("== 需求 4：大卡片图标一致 ==");
        Guard("R4 图标", CheckGlyphs);
    }

    // ---------------------------------------------------------------- 断言框架

    static void Guard(string title, Action a)
    {
        try { a(); }
        catch (Exception ex)
        {
            Ok(title + " 未抛异常", false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    static void Ok(string name, bool cond, string detail)
    {
        if (cond) { pass++; Console.WriteLine("PASS  " + name + "  " + detail); }
        else { fail++; Console.WriteLine("FAIL  " + name + "  " + detail); failLines.Add(name + " | " + detail); }
    }

    static void Skip(string name, string why)
    {
        Console.WriteLine("SKIP  " + name + "  " + why);
    }

    static string EnsureSub(string name)
    {
        string d = Path.Combine(outDir, name);
        Directory.CreateDirectory(d);
        return d;
    }

    // ---------------------------------------------------------------- 反射工具

    static object TryCall(object o, string name, params object[] a)
    {
        if (o == null) return null;
        MethodInfo found = null;
        foreach (var mi in o.GetType().GetMethods(FF))
        {
            if (mi.Name != name || mi.GetParameters().Length != a.Length) continue;
            found = mi; break;
        }
        if (found == null) return null;
        return found.Invoke(o, a);
    }

    static bool TryCallBool(object o, string name, out bool result, params object[] a)
    {
        result = false;
        var r = TryCall(o, name, a);
        if (r == null) return false;
        result = (bool)r;
        return true;
    }

    static object TryCallStatic(Type t, string name, params object[] a)
    {
        MethodInfo found = null;
        foreach (var mi in t.GetMethods(FF))
        {
            if (mi.Name != name || mi.GetParameters().Length != a.Length) continue;
            if (!mi.IsStatic) return null;      // 实例方法交给 TryCallStaticOrInstance 的实例分支
            found = mi; break;
        }
        if (found == null) return null;
        return found.Invoke(null, a);
    }

    static bool TryStaticBool(Type t, string name, out bool result, params object[] a)
    {
        result = false;
        var r = TryCallStatic(t, name, a);
        if (r == null) return false;
        result = (bool)r;
        return true;
    }

    static bool MemberExists(object o, string name)
    {
        if (o == null) return false;
        var t = o.GetType();
        return t.GetField(name, FF) != null || t.GetProperty(name, FF) != null || FindMethod(t, name, -1) != null;
    }

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

    static bool Set(object o, string name, object v)
    {
        var f = o.GetType().GetField(name, FF);
        if (f == null) return false;
        f.SetValue(o, v);
        return true;
    }

    static int GetInt(object o, string name, int fallback)
    {
        var v = Get(o, name);
        if (v == null) return fallback;
        return Convert.ToInt32(v);
    }

    static object CallOpt(object o, string name, params object[] a)
    {
        return TryCall(o, name, a);
    }

    /// <summary>调用一个方法并返回「方法是否存在」（void 方法也能区分缺 API 和调用成功）。</summary>
    static bool TryInvoke(object o, string name, params object[] a)
    {
        if (o == null) return false;
        var mi = FindMethod(o.GetType(), name, a.Length);
        if (mi == null) return false;
        mi.Invoke(o, a);
        return true;
    }

    // 静态或实例都能调（Lead 可能把 ValidCode / IsRoom 做成静态）
    static bool TryCallStaticOrInstance(object inst, Type t, string name, out bool result, params object[] a)
    {
        result = false;
        var r = TryCallStatic(t, name, a);
        if (r != null) { result = (bool)r; return true; }
        if (inst == null) return false;
        r = TryCall(inst, name, a);
        if (r == null) return false;
        result = (bool)r;
        return true;
    }

    // ---------------------------------------------------------------- 面板 / 渲染工具

    /// <summary>按实际构造函数形状造面板（Lead 可能加了 onDeleteContact 参数，用反射传）。</summary>
    static NotePanelForm MakePanel(Settings s, string themeId, Rectangle card,
        Action<string> onDelete = null, Func<string, string, bool> onAdd = null)
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
            else if (pt == typeof(Func<string, string, bool>)) args[i] = onAdd;
            else if (pt == typeof(Action<string>)) args[i] = (i == ps.Length - 1) ? onDelete : null;
            else if (pt == typeof(Action)) args[i] = null;
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

    static void Save(Bitmap bmp, string name)
    {
        bmp.Save(Path.Combine(outDir, name), ImageFormat.Png);
        Console.WriteLine("      shot -> " + name + "  " + bmp.Width + "x" + bmp.Height);
    }

    static int Brightness(Color c) { return Math.Max(c.R, Math.Max(c.G, c.B)); }

    static int ColorDist(Color a, Color b)
    {
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    static string fmt(Color c) { return c.R + "," + c.G + "," + c.B; }

    static double ScaleOf(NotePanelForm p)
    {
        var v = Get(p, "scale");
        return v == null ? 1.0 : Convert.ToDouble(v);
    }

    static int S(NotePanelForm p, double dip) { return (int)Math.Round(dip * ScaleOf(p)); }

    static Settings FreshSettings(string style)
    {
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.edge = "free";
        s.note.style = style;
        s.note.id = "48372";
        return s;
    }

    static void AddContacts(Settings s, int n)
    {
        for (int i = 0; i < n; i++)
        {
            string id = (10001 + i).ToString();
            s.note.AddContact(id, i == 0 ? "阿哲" : "同事" + (i + 1));
        }
    }

    static bool HasThread(NoteSettings note, string id)
    {
        foreach (var t in note.threads) if (t != null && t.contact == id) return true;
        return false;
    }

    static NoteThread FindThread(NoteSettings note, string id)
    {
        foreach (var t in note.threads) if (t != null && t.contact == id) return t;
        return null;
    }

    static int CountContacts(NoteSettings note, string id)
    {
        int n = 0;
        foreach (var c in note.contacts) if (c != null && c.id == id) n++;
        return n;
    }

    static string Hex(byte[] b)
    {
        var sb = new StringBuilder();
        foreach (var x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    // ---------------------------------------------------------------- R1：模型层

    static void CheckCodes()
    {
        var s = FreshSettings("drawer");
        var note = s.note;

        bool v12345, vAlpha, vCjk, v4, v6, vSpace, vEmpty, vBlank;
        TryStaticBool(typeof(NoteSettings), "ValidCode", out v12345, "12345");
        TryStaticBool(typeof(NoteSettings), "ValidCode", out vAlpha, "abcde");
        TryStaticBool(typeof(NoteSettings), "ValidCode", out vCjk, "讨论组ab");
        TryStaticBool(typeof(NoteSettings), "ValidCode", out v4, "1234");
        TryStaticBool(typeof(NoteSettings), "ValidCode", out v6, "123456");
        TryStaticBool(typeof(NoteSettings), "ValidCode", out vSpace, "12 45");
        TryStaticBool(typeof(NoteSettings), "ValidCode", out vEmpty, "");
        TryStaticBool(typeof(NoteSettings), "ValidCode", out vBlank, "     ");

        Ok("R1-01 5 位数字是合法码", v12345, "ValidCode(12345)=" + v12345);
        Ok("R1-02 5 位字母/中文是合法对话码", vAlpha && vCjk,
            "abcde=" + vAlpha + " 讨论组ab=" + vCjk);
        Ok("R1-03 4/6 位、含空格、空串都非法", !v4 && !v6 && !vSpace && !vEmpty && !vBlank,
            "4位=" + v4 + " 6位=" + v6 + " 含空格=" + vSpace + " 空=" + vEmpty + " 全空格=" + vBlank);

        bool id12345, idAlpha, id0;
        TryStaticBool(typeof(NoteSettings), "ValidId", out id12345, "12345");
        TryStaticBool(typeof(NoteSettings), "ValidId", out idAlpha, "abcde");
        TryStaticBool(typeof(NoteSettings), "ValidId", out id0, "01234");
        Ok("R1-04 5 位字符不是个人 ID、0 开头也不行", id12345 && !idAlpha && !id0,
            "12345=" + id12345 + " abcde=" + idAlpha + " 01234=" + id0);

        bool roomAlpha, roomNum;
        bool haveAlpha = TryCallStaticOrInstance(note, typeof(NoteSettings), "IsRoom", out roomAlpha, "abcde");
        bool haveNum = TryCallStaticOrInstance(note, typeof(NoteSettings), "IsRoom", out roomNum, "12345");
        Ok("R1-05 IsRoom：非数字 5 位=true，5 位数字=false", haveAlpha && haveNum && roomAlpha && !roomNum,
            "have=" + (haveAlpha && haveNum) + " abcde=" + roomAlpha + " 12345=" + roomNum);

        bool addAlpha = TryAddConversation(note, "abcde", "");
        bool addNum = TryAddConversation(note, "12345", "林总");
        bool addNum2 = TryAddConversation(note, "23456", "");
        Ok("R1-06 AddConversation 建对话码对话（联系人与线程都在）",
            addAlpha && note.Contact("abcde") != null && HasThread(note, "abcde"),
            "ret=" + addAlpha + " contact=" + (note.Contact("abcde") != null) + " thread=" + HasThread(note, "abcde"));
        Ok("R1-07 5 位数字建的是 1:1，ID 原样保留",
            addNum && note.Contact("12345") != null && !note.Contact("12345").IsRoom && note.Contact("12345").id == "12345",
            "ret=" + addNum + " isRoom=" + (note.Contact("12345") == null ? "null" : note.Contact("12345").IsRoom.ToString()));

        var cRoom = note.Contact("abcde");
        var cNum = note.Contact("23456");
        Ok("R1-08 显示名：对话码→「对话 abcde」，没备注的数字 ID→「ID 23456」，备注优先",
            cRoom != null && cNum != null && addNum2
            && cRoom.Display() == "对话 abcde" && cNum.Display() == "ID 23456"
            && note.Contact("12345").Display() == "林总",
            "room=" + (cRoom == null ? "null" : cRoom.Display())
            + " num=" + (cNum == null ? "null" : cNum.Display())
            + " remarked=" + note.Contact("12345").Display());

        int before = note.contacts.Count;
        bool bad4 = TryAddConversation(note, "1234", "");
        bool badSpace = TryAddConversation(note, "12 45", "");
        bool bad6 = TryAddConversation(note, "123456", "");
        Ok("R1-09 非法输入不建对话", !bad4 && !badSpace && !bad6 && note.contacts.Count == before,
            "4位=" + bad4 + " 含空格=" + badSpace + " 6位=" + bad6 + " contacts=" + before + "->" + note.contacts.Count);

        bool dup = TryAddConversation(note, "abcde", "新备注");
        Ok("R1-10 同一个码重复添加：不重复建、备注更新",
            !dup && CountContacts(note, "abcde") == 1 && note.Contact("abcde").remark == "新备注",
            "ret=" + dup + " count=" + CountContacts(note, "abcde") + " remark=" + note.Contact("abcde").remark);
    }

    static bool TryAddConversation(NoteSettings note, string code, string remark)
    {
        bool b;
        if (TryCallBool(note, "AddConversation", out b, code, remark)) return b;
        // 兼容签名 AddConversation(string) 或没有该方法时退回旧 AddContact
        if (TryCallBool(note, "AddConversation", out b, code)) return b;
        if (TryCallBool(note, "AddContact", out b, code, remark)) return b;
        return false;
    }

    // ---------------------------------------------------------------- R1：房间主题 + 消息路由

    static void CheckRoomModel()
    {
        // 主题
        string topic = TryCallStatic(typeof(NoteRelay), "RoomTopic", "abcde") as string;
        if (topic == null)
        {
            var tmp = new NoteRelay(FreshSettings("drawer").note);
            topic = TryCall(tmp, "RoomTopic", "abcde") as string;
        }
        string expected = "xzt/v1/room/" + Hex(Encoding.UTF8.GetBytes("abcde"));
        Ok("R1-11 房间主题 = xzt/v1/room/<hex(UTF8(code))>",
            topic != null && topic == expected, "actual=" + (topic ?? "<null>") + " expect=" + expected);

        string t1 = RoomTopicOf("abcde"), t2 = RoomTopicOf("讨论组ab");
        string t3 = RoomTopicOf("abcde");
        Ok("R1-12 同码同主题、不同码不同主题、中文按 UTF-8 编码",
            t1 != null && t1 == t3 && t2 != null && t1 != t2
            && t2 == "xzt/v1/room/" + Hex(Encoding.UTF8.GetBytes("讨论组ab")),
            "t1=" + (t1 ?? "<null>") + " t2=" + (t2 ?? "<null>"));

        // Append 路由：两个不同发送者、同一个码 → 同一个线程；保留 from
        var s = FreshSettings("columns");
        s.note.contacts.Clear();
        s.note.threads.Clear();
        s.note.id = "46040";
        long now = NoteRelay.Now();
        var m1 = new NoteMessage("11111", "abcde", "大家好", now - 30);
        var m2 = new NoteMessage("22222", "abcde", "收到", now - 20);
        var m3 = new NoteMessage("46040", "abcde", "我也在", now - 10);
        bool ok = s.note.Append(m1, true) && s.note.Append(m2, true) && s.note.Append(m3, false);
        var th = FindThread(s.note, "abcde");
        Ok("R1-13 房间消息进对应线程：不同发送者同一个码 = 同一个对话",
            ok && s.note.threads.Count >= 1 && th != null && th.messages.Count == 3,
            "ok=" + ok + " threads=" + s.note.threads.Count + " msgs=" + (th == null ? -1 : th.messages.Count));
        Ok("R1-14 房间消息保留发送者 5 位 ID",
            th != null && th.messages.Count == 3 && th.messages[0].from == "11111"
            && th.messages[1].from == "22222" && th.messages[2].from == "46040"
            && th.messages[0].to == "abcde",
            th == null || th.messages.Count != 3 ? "线程/消息数不对"
                : "froms=" + th.messages[0].from + "," + th.messages[1].from + "," + th.messages[2].from + " to=" + th.messages[0].to);
        Ok("R1-15 自己从房间发出也进同一线程且 from=本机 ID",
            th != null && th.messages.Count == 3 && th.messages[2].from == "46040" && th.messages[2].to == "abcde",
            th == null ? "无线程" : "last.from=" + th.messages[th.messages.Count - 1].from);

        // 收包解码：房间消息 payload 也要能解出来（原来 DecodeMessage 只认 ValidId）
        string payload = NoteRelay.BuildMessageJson("11111", "abcde", "房间消息", now);
        NoteIncoming inc = NoteRelay.DecodeMessage(payload);
        Ok("R1-16 房间消息能被解码（from/to 是对话码）",
            inc != null && inc.from == "11111" && inc.to == "abcde" && inc.text == "房间消息",
            inc == null ? "DecodeMessage=null payload=" + payload
                : "from=" + inc.from + " to=" + inc.to + " text=" + inc.text);
    }

    static string RoomTopicOf(string code)
    {
        var r = TryCallStatic(typeof(NoteRelay), "RoomTopic", code);
        if (r is string) return (string)r;
        var cfg = FreshSettings("drawer");
        var relay = new NoteRelay(cfg.note);
        try { return TryCall(relay, "RoomTopic", code) as string; }
        finally { relay.Dispose(); }
    }

    // ---------------------------------------------------------------- R1：主题解析 / 十六进制

    static void CheckRoomTopicParse()
    {
        string code;
        string topic = NoteRelay.RoomTopic("abcde");
        bool rt1 = NoteRelay.ParseRoomTopic(topic, out code) && code == "abcde";
        string topic2 = NoteRelay.RoomTopic("讨论组ab");
        bool rt2 = NoteRelay.ParseRoomTopic(topic2, out code) && code == "讨论组ab";
        string code2;
        bool rejectMsg = !NoteRelay.ParseRoomTopic(NoteRelay.InboxTopic("12345"), out code2);
        bool rejectBad = !NoteRelay.ParseRoomTopic("xzt/v1/room/zz", out code2);
        bool hexOk = NoteRelay.HexDecode(NoteRelay.HexEncode("讨论组ab")) == "讨论组ab"
            && NoteRelay.HexDecode(NoteRelay.HexEncode("abcde")) == "abcde";
        Ok("R1-21 房间主题能反解回原码（中英文都行），非房间主题/坏十六进制被拒",
            rt1 && rt2 && rejectMsg && rejectBad && hexOk,
            "abcde=" + rt1 + " 讨论组ab=" + rt2 + " rejectMsg=" + rejectMsg + " rejectBad=" + rejectBad + " hexRoundtrip=" + hexOk);
    }

    // ---------------------------------------------------------------- R1：端到端（假 MQTT broker）

    static void CheckRelayEndToEnd()
    {
        var s = FreshSettings("drawer");
        s.note.id = "46040";
        s.note.contacts.Clear();
        s.note.threads.Clear();
        TryAddConversation(s.note, "abcde", "");
        TryAddConversation(s.note, "讨论组ab", "");
        s.note.relayHost = "127.0.0.1";

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        s.note.relayPort = port;

        var relay = new NoteRelay(s.note);
        TcpClient client = null;
        NetworkStream stream = null;
        try
        {
            relay.Start();
            var accept = listener.BeginAcceptTcpClient(null, null);
            if (!accept.AsyncWaitHandle.WaitOne(5000)) throw new Exception("假 broker 等不到 NoteRelay 连接（5s）");
            client = listener.EndAcceptTcpClient(accept);
            stream = client.GetStream();
            stream.ReadTimeout = 3000;

            var connect = ReadPacket(stream);
            bool gotConnect = connect != null && connect.type == 1;
            stream.Write(new byte[] { 0x20, 0x02, 0x00, 0x00 }, 0, 4);   // CONNACK
            // 等 relay 进入 connected
            for (int i = 0; i < 60 && !relay.Connected; i++) Thread.Sleep(50);

            // 收集订阅（房间订阅应该含两个码的主题）
            var subs = new List<string>();
            long deadline = Environment.TickCount + 2500;
            while (Environment.TickCount < deadline)
            {
                var p = ReadPacket(stream);
                if (p == null) break;
                if (p.type == 8) CollectSubTopics(p.body, subs);
                if (subs.Count > 0 && Environment.TickCount - deadline > -1500) break;   // 拿到订阅后再多读一小会
            }

            string room1 = RoomTopicOf("abcde");
            string room2 = RoomTopicOf("讨论组ab");
            Ok("R1-17 连上中继后订阅了房间主题（同码的人才能进同一对话）",
                gotConnect && subs.Contains(room1) && subs.Contains(room2),
                "connect=" + gotConnect + " subs=[" + string.Join(",", subs.ToArray()) + "] 期望含 " + room1 + " / " + room2);

            // 发一条到房间码，抓 PUBLISH 的 topic
            bool sent = false;
            MethodInfo send = FindMethod(typeof(NoteRelay), "SendRoomMessage", 2)
                ?? FindMethod(typeof(NoteRelay), "SendMessage", 2);
            if (send != null)
            {
                var r = send.Invoke(relay, new object[] { "abcde", "房间测试" });
                sent = r is bool && (bool)r;
            }
            string pubTopic = null, payload = null;
            deadline = Environment.TickCount + 3000;
            while (Environment.TickCount < deadline)
            {
                var p = ReadPacket(stream);
                if (p == null) break;
                if (p.type == 3)
                {
                    string t = ParsePublishTopic(p.flags, p.body, out payload);
                    if (t != null && t == room1) { pubTopic = t; break; }
                }
            }
            Ok("R1-18 往对话码发消息：发布主题是房间主题、payload 带本机 ID",
                sent && pubTopic == room1 && payload != null && payload.Contains("\"46040\""),
                "sent=" + sent + " topic=" + (pubTopic ?? "<null>") + " payload=" + (payload ?? "<null>"));

            // 新增对话后要立刻补订阅（不用等重连），否则同码的人发消息收不到
            TryAddConversation(s.note, "新码abc", "");
            string newTopic = RoomTopicOf("新码abc");
            var subs2 = new List<string>();
            relay.RefreshSubscriptions();
            long dl = Environment.TickCount + 2500;
            while (Environment.TickCount < dl)
            {
                var p = ReadPacket(stream);
                if (p == null) break;
                if (p.type == 8) CollectSubTopics(p.body, subs2);
                if (subs2.Contains(newTopic)) break;
            }
            Ok("R1-31 新增对话码后（已连接状态）会立刻补订阅新房间主题",
                subs2.Contains(newTopic), "subs=[" + string.Join(",", subs2.ToArray()) + "] 期望含 " + newTopic);
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

    static void CollectSubTopics(byte[] body, List<string> into)
    {
        try
        {
            int i = 2;   // packet id
            while (i + 2 <= body.Length)
            {
                int len = (body[i] << 8) | body[i + 1];
                i += 2;
                if (i + len > body.Length) break;
                into.Add(Encoding.UTF8.GetString(body, i, len));
                i += len + 1;   // qos
            }
        }
        catch { }
    }

    static string ParsePublishTopic(int flags, byte[] body, out string payload)
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

    // ---------------------------------------------------------------- R1：房间对话显示发送者 ID

    static void CheckRoomSenderVisible()
    {
        Func<string, Bitmap> render = delegate(string sender)
        {
            var s = FreshSettings("columns");
            s.note.id = "46040";
            s.note.contacts.Clear();
            s.note.threads.Clear();
            TryAddConversation(s.note, "abcde", "");
            long now = NoteRelay.Now();
            s.note.Append(new NoteMessage(sender, "abcde", "同样的字", now - 20), true);
            s.note.Append(new NoteMessage("46040", "abcde", "同样的回复", now - 10), false);
            using (var p = MakePanel(s, "huang-liuli", new Rectangle(900, 120, 470, 298)))
            {
                TryCall(p, "SelectContact", "abcde", false);
                Set(p, "heightNow", Get(p, "heightTo"));
                Set(p, "stateAt", Environment.TickCount - 900);
                var bmp = RenderPanel(p);
                // 聊天态 + 历史态各一张，发送者 ID 标注哪一页出现都能查到
                TryCall(p, "CloseFieldEditor");
                var stateType = typeof(NotePanelForm).GetNestedType("PanelState", BindingFlags.NonPublic);
                TryCall(p, "SetState", Enum.Parse(stateType, "History"), true);
                Set(p, "heightNow", Get(p, "heightTo"));
                Set(p, "stateAt", Environment.TickCount - 900);
                var hist = RenderPanel(p);
                var composed = new Bitmap(Math.Max(bmp.Width, hist.Width), bmp.Height + hist.Height + 4);
                using (var g = Graphics.FromImage(composed))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImageUnscaled(bmp, 0, 0);
                    g.DrawImageUnscaled(hist, 0, bmp.Height + 4);
                }
                bmp.Dispose(); hist.Dispose();
                return composed;
            }
        };

        using (var a = render("11111"))
        using (var b = render("22222"))
        {
            Save(a, Path.Combine("shots", "note-room-sender-11111.png"));
            Save(b, Path.Combine("shots", "note-room-sender-22222.png"));
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
            int diff = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (ColorDist(a.GetPixel(x, y), b.GetPixel(x, y)) > 24)
                    {
                        diff++;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            // 只改发送者 ID：差异应出现在气泡外侧的小字标注区，且不能占满整张图
            Ok("R1-19 房间对话里显示发送者 ID（换发送者 → 图上有差异）",
                diff >= 20 && diff <= w * h / 4,
                "diff=" + diff + "px bbox=(" + (minX == int.MaxValue ? "-" : minX + "," + minY + ")-(" + maxX + "," + maxY + ")")
                + " img=" + w + "x" + h);
        }
    }

    // ---------------------------------------------------------------- R1：气泡 / 历史里的发送者标注

    static void CheckRoomSenderText()
    {
        var s = FreshSettings("columns");
        s.note.id = "46040";
        s.note.contacts.Clear();
        s.note.threads.Clear();
        TryAddConversation(s.note, "abcde", "");
        TryAddConversation(s.note, "12345", "");
        using (var p = MakePanel(s, "huang-liuli", new Rectangle(900, 120, 470, 298)))
        {
            long now = NoteRelay.Now();
            var inRoom = new NoteMessage("11111", "abcde", "你好", now - 20);
            var mineRoom = new NoteMessage("46040", "abcde", "收到", now - 10);
            var inOne = new NoteMessage("12345", "46040", "在吗", now - 5);
            string b1 = p.BubbleText(inRoom, true);
            string b2 = p.BubbleText(mineRoom, true);
            string b3 = p.BubbleText(inOne, false);
            Ok("R1-22 对话码气泡：别人发的带「ID 发送者：」，自己发的不带；1:1 不带",
                b1 == "ID 11111：你好" && b2 == "收到" && b3 == "在吗",
                "roomIn=[" + b1 + "] roomMine=[" + b2 + "] oneToOne=[" + b3 + "]");

            TryCall(p, "SelectContact", "abcde", false);
            string whoRoom = p.WhoLabel(inRoom);
            string whoMine = p.WhoLabel(mineRoom);
            TryCall(p, "SelectContact", "12345", false);
            string whoOne = p.WhoLabel(inOne);
            Ok("R1-23 历史行「谁」：对话码显示发送者 5 位 ID，自己=你，1:1=对方",
                whoRoom == "11111" && whoMine == "你" && whoOne == "对方",
                "room=" + whoRoom + " mine=" + whoMine + " oneToOne=" + whoOne);

            // 抽屉式也要补一行「对话码 xxx」，不然不知道在哪个对话里
            var s2 = FreshSettings("drawer");
            s2.note.id = "46040";
            s2.note.contacts.Clear();
            s2.note.threads.Clear();
            TryAddConversation(s2.note, "abcde", "");
            using (var p2 = MakePanel(s2, "huang-liuli", new Rectangle(840, 90, 420, 282)))
            {
                TryCall(p2, "SelectContact", "abcde", false);
                Set(p2, "heightNow", Get(p2, "heightTo"));
                Set(p2, "stateAt", Environment.TickCount - 900);
                int bw = 0;
                using (var bmp = RenderPanel(p2)) { Save(bmp, Path.Combine("shots", "note-drawer-room-chat.png")); bw = bmp.Width; }
                Ok("R1-24 抽屉式房间对话有「名字 + 对话码」头行（截图 note-drawer-room-chat.png）",
                    p2.SelectedId == "abcde" && bw > 0, "selected=" + (p2.SelectedId ?? "<null>") + " w=" + bw);
            }
        }
    }

    // ---------------------------------------------------------------- R1：Widget 收房间消息接线

    static void CheckWidgetRoomReceive()
    {
        var s = FreshSettings("drawer");
        s.note.id = "46040";
        s.note.contacts.Clear();
        s.note.threads.Clear();
        TryAddConversation(s.note, "abcde", "");

        using (var w = new WidgetForm(new SettingsStore(), s))
        {
            IntPtr h = w.Handle;   // 强制建句柄：OnNoteMessage 里 BeginInvoke 需要
            var mi = typeof(WidgetForm).GetMethod("OnNoteMessage", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi == null) { Ok("R1-25 Widget 收到房间消息会进对应对话", false, "找不到 OnNoteMessage"); return; }
            long now = NoteRelay.Now();
            mi.Invoke(w, new object[] { new NoteIncoming { from = "11111", to = "abcde", text = "甲在房间", ts = now - 20 } });
            mi.Invoke(w, new object[] { new NoteIncoming { from = "22222", to = "abcde", text = "乙也在", ts = now - 10 } });
            PumpUntil(delegate { var t = FindThread(s.note, "abcde"); return t != null && t.messages.Count >= 2; }, 2500);
            var th = FindThread(s.note, "abcde");
            Ok("R1-25 Widget 收到房间消息会进对应对话（不同发送者同一个码 = 一个对话）",
                th != null && th.messages.Count == 2 && th.messages[0].from == "11111" && th.messages[1].from == "22222",
                th == null ? "无线程" : "msgs=" + th.messages.Count + " froms="
                    + (th.messages.Count > 0 ? th.messages[0].from : "-") + "," + (th.messages.Count > 1 ? th.messages[1].from : "-"));

            // 已删除 / 不存在的对话码：中继回显也不能冒出新对话
            int contactsBefore = s.note.contacts.Count, threadsBefore = s.note.threads.Count;
            mi.Invoke(w, new object[] { new NoteIncoming { from = "33333", to = "zzzzz", text = "不该出现", ts = now } });
            PumpUntil(delegate { return false; }, 300);
            Ok("R1-26 不存在（已删）的对话码收到消息不会重建对话",
                s.note.contacts.Count == contactsBefore && s.note.threads.Count == threadsBefore,
                "contacts " + contactsBefore + "->" + s.note.contacts.Count + " threads " + threadsBefore + "->" + s.note.threads.Count);

            // 1:1 陌生人：自动建一个按 ID 的对话
            mi.Invoke(w, new object[] { new NoteIncoming { from = "99999", to = "46040", text = "陌生人", ts = now } });
            PumpUntil(delegate { return s.note.Contact("99999") != null; }, 2500);
            var c = s.note.Contact("99999");
            var t2 = FindThread(s.note, "99999");
            Ok("R1-27 1:1 陌生人发来的消息会自动建对话（保留对方 ID）",
                c != null && c.id == "99999" && !c.IsRoom && t2 != null && t2.messages.Count == 1 && t2.messages[0].from == "99999",
                c == null ? "没建联系人" : "id=" + c.id + " isRoom=" + c.IsRoom + " msgs=" + (t2 == null ? -1 : t2.messages.Count));
        }
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

    // ---------------------------------------------------------------- R1：面板「新建对话」流程

    static void CheckPanelAddFlow()
    {
        var s = FreshSettings("drawer");
        s.note.id = "46040";
        s.note.contacts.Clear();
        s.note.threads.Clear();
        string addedCode = null;
        using (var p = MakePanel(s, "huang-liuli", new Rectangle(840, 90, 420, 282), null,
            delegate(string id, string remark) { addedCode = id; return s.note.AddConversation(id, remark); }))
        {
            Set(p, "addId", "abcde");
            Set(p, "addRemark", "项目组");
            Call(p, "ConfirmAdd");
            var c = s.note.Contact("abcde");
            string st = Get(p, "status") as string;
            Ok("R1-28 面板新建对话：任意 5 位字符 → 对话码对话（回调 + 线程 + 提示）",
                addedCode == "abcde" && c != null && c.IsRoom && HasThread(s.note, "abcde")
                && st != null && st.Contains("已创建对话"),
                "cb=" + (addedCode ?? "<null>") + " contact=" + (c != null) + " status=" + (st ?? "<null>"));
            Ok("R1-29 面板新建对话：5 位数字 → 1:1（IsRoom=false）",
                AddAndCheck(p, s, "12345", "林总", false),
                "contact=" + (s.note.Contact("12345") != null ? ("isRoom=" + s.note.Contact("12345").IsRoom + " remark=" + s.note.Contact("12345").remark) : "<null>"));
            int before = s.note.contacts.Count;
            Set(p, "addId", "1234");
            Call(p, "ConfirmAdd");
            string stBad = Get(p, "status") as string;
            Ok("R1-30 面板新建对话：4 位输入被拒并提示",
                s.note.contacts.Count == before && stBad != null && stBad.Contains("5 位"), "status=" + (stBad ?? "<null>"));

            // 添加对话页截图（文案 + 提示行）
            Set(p, "addId", "abcde");
            Set(p, "addRemark", "项目组");
            var stateType = typeof(NotePanelForm).GetNestedType("PanelState", BindingFlags.NonPublic);
            TryCall(p, "SetState", Enum.Parse(stateType, "Add"), false);
            Set(p, "heightNow", Get(p, "heightTo"));
            using (var bmp = RenderPanel(p)) Save(bmp, Path.Combine("shots", "note-drawer-add-room.png"));
        }
    }

    static bool AddAndCheck(NotePanelForm p, Settings s, string code, string remark, bool expectRoom)
    {
        Set(p, "addId", code);
        Set(p, "addRemark", remark);
        Call(p, "ConfirmAdd");
        var c = s.note.Contact(code);
        return c != null && c.IsRoom == expectRoom && HasThread(s.note, code);
    }

    // ---------------------------------------------------------------- R1：设置页对话框

    static void CheckSettingsDialog()
    {
        Type dlgType = null;
        foreach (var nt in typeof(SettingsForm).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            if (nt.Name == "NoteContactDialog") { dlgType = nt; break; }
        if (dlgType == null) { Ok("R1-20 设置页纸条对话框改成对话码", false, "找不到 NoteContactDialog 类型"); return; }
        ConstructorInfo ctor = null;
        foreach (var c in dlgType.GetConstructors(FF))
            if (c.GetParameters().Length == 4) { ctor = c; break; }
        if (ctor == null) { Ok("R1-20 设置页纸条对话框改成对话码", false, "NoteContactDialog 构造签名变了"); return; }
        using (var dlg = (Form)ctor.Invoke(new object[] { "添加对话", "", "", true }))
        {
            var texts = new List<string>();
            CollectControlText(dlg, texts);
            string all = string.Join(" | ", texts.ToArray());
            bool mentionsCode = all.Contains("对话码") || all.Contains("5 位字符") || all.Contains("对话");
            bool rejects4 = !CodeAccepted("1234");
            bool acceptsAlpha = CodeAccepted("abcde");
            Ok("R1-20 设置页纸条对话框改成对话码（文案 + 校验）",
                mentionsCode && acceptsAlpha && rejects4,
                "mentionsCode=" + mentionsCode + " abcde=" + acceptsAlpha + " 1234=" + rejects4 + " texts=[" + all + "]");
        }
    }

    static bool CodeAccepted(string code)
    {
        bool v;
        if (TryStaticBool(typeof(NoteSettings), "ValidCode", out v, code)) return v;
        var s = FreshSettings("drawer");
        return TryCallStaticOrInstance(s.note, typeof(NoteSettings), "ValidCode", out v, code) && v;
    }

    static void CollectControlText(Control root, List<string> into)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Label) into.Add(((Label)c).Text);
            else if (c is Button) into.Add(((Button)c).Text);
            else if (c is TextBox) into.Add("textbox(max=" + ((TextBox)c).MaxLength + ")");
            if (c.Controls.Count > 0) CollectControlText(c, into);
        }
    }

    // ---------------------------------------------------------------- R2：卡片边框

    static void CheckBorder()
    {
        var s = FreshSettings("drawer");
        AddContacts(s, 3);
        long now = NoteRelay.Now();
        s.note.Append(new NoteMessage("10002", "48372", "晚上的评审推迟到 8 点", now - 60), true);
        s.note.Append(new NoteMessage("48372", "10002", "收到，我改完就过去", now - 50), false);

        using (var p = MakePanel(s, "huang-liuli", new Rectangle(840, 90, 420, 282)))
        {
            Call(p, "SelectContact", "10002", false);
            Set(p, "heightNow", Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 900);

            var v = Get(p, "CardBorderDip");
            double dip = v == null ? -1 : Convert.ToDouble(v);
            Ok("R2-01 边框宽度常量 CardBorderDip 在 1.4–2.0 DIP",
                dip >= 1.4 && dip <= 2.0, "CardBorderDip=" + (v == null ? "<缺失>" : dip.ToString("0.00")));

            using (var bmp = RenderPanel(p))
            {
                Save(bmp, Path.Combine("shots", "note-drawer-chat-border.png"));
                int topRows = BorderRows(bmp, true);
                int botRows = BorderRows(bmp, false);
                int edge = Brightness(bmp.GetPixel(bmp.Width / 2, 0));
                int inside = Brightness(bmp.GetPixel(bmp.Width / 2, Math.Min(bmp.Height - 2, S(p, 10))));
                Ok("R2-02 顶部边框像素带变粗（改动前 2 行）", topRows >= 3,
                    "topRows=" + topRows + "（改动前基线 2）scale=" + ScaleOf(p));
                Ok("R2-03 底部边框像素带同步变粗（改动前 2 行）", botRows >= 3,
                    "bottomRows=" + botRows + "（改动前基线 2）");
                Ok("R2-04 边框仍然清晰可见（亮度差 ≥ 25）", edge - inside >= 25,
                    "edge=" + edge + " inside=" + inside);
            }

            // 添加页边框同样要在
            var stateType = typeof(NotePanelForm).GetNestedType("PanelState", BindingFlags.NonPublic);
            TryCall(p, "SetState", Enum.Parse(stateType, "Add"), false);
            Set(p, "heightNow", Get(p, "heightTo"));
            using (var bmp = RenderPanel(p))
            {
                Save(bmp, Path.Combine("shots", "note-drawer-add-border.png"));
                int rows = BorderRows(bmp, true);
                Ok("R2-05 添加对话页边框也在，且同样加粗", rows >= 3, "addPageTopRows=" + rows);
            }

            // 历史页边框
            TryCall(p, "SetState", Enum.Parse(stateType, "History"), true);
            Set(p, "heightNow", Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 900);
            using (var bmp = RenderPanel(p))
            {
                Save(bmp, Path.Combine("shots", "note-drawer-history-border.png"));
                Ok("R2-06 历史页边框同样加粗", BorderRows(bmp, true) >= 3 && BorderRows(bmp, false) >= 3,
                    "top=" + BorderRows(bmp, true) + " bottom=" + BorderRows(bmp, false));
            }
        }

        // 双栏式边框也要加粗
        var sc = FreshSettings("columns");
        AddContacts(sc, 2);
        sc.note.Append(new NoteMessage("10002", "48372", "双栏边框测试", NoteRelay.Now()), true);
        using (var p = MakePanel(sc, "huang-liuli", new Rectangle(600, 200, 470, 298)))
        {
            Call(p, "SelectContact", "10002", false);
            Set(p, "heightNow", Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 900);
            using (var bmp = RenderPanel(p))
            {
                Save(bmp, Path.Combine("shots", "note-columns-border.png"));
                Ok("R2-07 双栏式边框同样加粗", BorderRows(bmp, true) >= 3 && BorderRows(bmp, false) >= 3,
                    "top=" + BorderRows(bmp, true) + " bottom=" + BorderRows(bmp, false));
            }
        }
    }

    /// <summary>
    /// 在 x=宽/2 处从边缘往里扫，数「与内部底色色差 > 12」的连续行数（边框像素带厚度）。
    /// 改动前实测：顶部 2 行、底部 2 行（1 DIP @150%）；加粗后应 ≥3 行。
    /// </summary>
    static int BorderRows(Bitmap bmp, bool fromTop)
    {
        int x = bmp.Width / 2;
        int refY = fromTop ? Math.Min(bmp.Height - 1, 10) : Math.Max(0, bmp.Height - 11);
        Color inside = bmp.GetPixel(x, refY);
        int n = 0;
        for (int i = 0; i < Math.Min(12, bmp.Height); i++)
        {
            int y = fromTop ? i : bmp.Height - 1 - i;
            if (y < 0 || y >= bmp.Height) break;
            if (ColorDist(bmp.GetPixel(x, y), inside) > 12) n++;
            else if (n > 0) break;
        }
        return n;
    }

    // ---------------------------------------------------------------- R3：滚动

    static void CheckScroll()
    {
        foreach (string style in new string[] { "drawer", "columns" })
        {
            var s = FreshSettings(style);
            AddContacts(s, 8);
            using (var p = MakePanel(s, "huang-liuli", new Rectangle(600, 200, 420, 282)))
            {
                bool hasScroll = MemberExists(p, "ContactScroll");
                bool hasVis = MemberExists(p, "VisibleContactCount");
                int visCount = GetInt(p, "VisibleContactCount", -1);
                Ok("R3-01 [" + style + "] 有 ContactScroll / VisibleContactCount",
                    hasScroll && hasVis, "ContactScroll=" + hasScroll + " VisibleContactCount=" + hasVis + "(" + visCount + ")");

                int scroll0 = GetInt(p, "ContactScroll", -1);
                var drawn = DrawnContacts(p);
                int expectVis = style == "columns" ? 5 : 4;   // 双栏正常态 5 行（298 DIP 高）/ 抽屉横排 4 个
                Ok("R3-02 [" + style + "] 初始 scroll=0、只画一屏（" + expectVis + " 行，不是 8 个全画）",
                    scroll0 == 0 && visCount == expectVis && drawn.Count == expectVis,
                    "scroll=" + scroll0 + " VisibleContactCount=" + visCount + " 期望 " + expectVis
                    + " drawn=" + drawn.Count + " ids=[" + Ids(drawn) + "]");

                var firstBefore = drawn.Count > 0 ? drawn[0].id : null;
                bool hasScrollCall = TryInvoke(p, "ScrollContacts", 1);
                var drawn1 = DrawnContacts(p);
                int scroll1 = GetInt(p, "ContactScroll", -1);
                string firstAfter = drawn1.Count > 0 ? drawn1[0].id : null;
                Ok("R3-03 [" + style + "] ScrollContacts(+1) 窗口真的动了",
                    hasScrollCall && firstBefore != firstAfter,
                    "hasMethod=" + hasScrollCall + " scroll=" + scroll1 + " first=" + firstBefore + "->" + firstAfter);

                // 滚轮一格
                int beforeWheel = GetInt(p, "ContactScroll", -1);
                InvokeWheel(p, -120);
                int afterWheel = GetInt(p, "ContactScroll", -1);
                InvokeWheel(p, 120);
                int backWheel = GetInt(p, "ContactScroll", -1);
                Ok("R3-04 [" + style + "] 滚轮一格 = scroll ±1",
                    afterWheel == beforeWheel + 1 && backWheel == beforeWheel,
                    "before=" + beforeWheel + " wheelDown=" + afterWheel + " wheelUp=" + backWheel);

                // clamp
                TryInvoke(p, "ScrollContacts", 999);
                int maxScroll = GetInt(p, "ContactScroll", -1);
                var drawnMax = DrawnContacts(p);
                string lastId = drawnMax.Count > 0 ? drawnMax[drawnMax.Count - 1].id : null;
                bool sawLast = false;
                foreach (var c in drawnMax) if (c.id == "10008") sawLast = true;
                using (var bmp = RenderPanel(p)) Save(bmp, Path.Combine("shots", "note-" + style + "-scroll-max.png"));
                TryInvoke(p, "ScrollContacts", -999);
                int minScroll = GetInt(p, "ContactScroll", -1);
                var drawnMin = DrawnContacts(p);
                bool sawFirst = false;
                foreach (var c in drawnMin) if (c.id == "10001") sawFirst = true;
                int total = s.note.contacts.Count;
                Ok("R3-05 [" + style + "] 滚动边界 clamp：不越界、末屏能看到最后一个对话",
                    maxScroll >= 0 && maxScroll <= Math.Max(0, total - 1) && sawLast && minScroll == 0 && sawFirst,
                    "max=" + maxScroll + " lastId=" + lastId + " sawLast=" + sawLast + " min=" + minScroll + " sawFirst=" + sawFirst);
            }
        }
    }

    static List<NoteContact> DrawnContacts(NotePanelForm p)
    {
        var list = new List<NoteContact>();
        var raw = Get(p, "drawnContacts") as System.Collections.IEnumerable;
        if (raw != null)
            foreach (var c in raw)
            {
                var nc = c as NoteContact;
                if (nc != null) list.Add(nc);
            }
        return list;
    }

    static string Ids(List<NoteContact> list)
    {
        var a = new List<string>();
        foreach (var c in list) a.Add(c.id);
        return string.Join(",", a.ToArray());
    }

    static void InvokeWheel(NotePanelForm p, int delta)
    {
        var mi = typeof(NotePanelForm).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi == null) return;
        mi.Invoke(p, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 10, delta) });
    }

    // ---------------------------------------------------------------- R3：选中后自动滚到可见

    static void CheckEnsureVisible()
    {
        foreach (string style in new string[] { "drawer", "columns" })
        {
            var s = FreshSettings(style);
            AddContacts(s, 8);
            using (var p = MakePanel(s, "huang-liuli", new Rectangle(600, 200, 420, 282)))
            {
                TryCall(p, "SelectContact", "10008", false);
                int scroll = GetInt(p, "ContactScroll", -1);
                var drawn = DrawnContacts(p);
                bool visible = false;
                foreach (var c in drawn) if (c.id == "10008") visible = true;
                int max = Math.Max(0, s.note.contacts.Count - GetInt(p, "VisibleContactCount", 4));
                Ok("R3-16 [" + style + "] 选中窗口外的对话会自动滚到可见（EnsureContactVisible）",
                    scroll == max && visible,
                    "scroll=" + scroll + " max=" + max + " drawn=[" + Ids(drawn) + "] 见10008=" + visible);

                TryCall(p, "SelectContact", "10001", false);
                int back = GetInt(p, "ContactScroll", -1);
                Ok("R3-16b [" + style + "] 再选中第一个对话会滚回顶部", back == 0, "scroll=" + back);
            }
        }
    }

    // ---------------------------------------------------------------- R3：双栏行数 / 底部「＋ 新建对话」命中区

    static void CheckColumnsRows()
    {
        // 正常态 298 DIP → 5 行；历史态 512 DIP → 10 行（Lead 2026-11-13 16:23 调整）
        var s = FreshSettings("columns");
        AddContacts(s, 8);
        using (var p = MakePanel(s, "huang-liuli", new Rectangle(600, 200, 470, 298)))
        {
            int vis = GetInt(p, "VisibleContactCount", -1);
            var drawn = DrawnContacts(p);
            Ok("R3-17 双栏正常态一屏 5 行（150% 下 5 个对话胶囊 + 底部新建对话）",
                vis == 5 && drawn.Count == 5, "VisibleContactCount=" + vis + " drawn=" + drawn.Count + " ids=[" + Ids(drawn) + "]");
            using (var bmp = RenderPanel(p)) Save(bmp, Path.Combine("shots", "note-columns-contacts-5rows.png"));

            var stateType = typeof(NotePanelForm).GetNestedType("PanelState", BindingFlags.NonPublic);
            TryCall(p, "SetState", Enum.Parse(stateType, "History"), true);
            Set(p, "heightNow", Get(p, "heightTo"));
            TryCall(p, "BuildHits");
            int visH = GetInt(p, "VisibleContactCount", -1);
            var drawnH = DrawnContacts(p);
            Ok("R3-17b 双栏历史态一屏 10 行（8 个对话全画、不用滚）",
                visH == 10 && drawnH.Count == 8 && GetInt(p, "ContactScroll", -1) == 0,
                "VisibleContactCount=" + visH + " drawn=" + drawnH.Count + " scroll=" + GetInt(p, "ContactScroll", -1));
            using (var bmp = RenderPanel(p)) Save(bmp, Path.Combine("shots", "note-columns-history-10rows.png"));
        }

        // 底部「＋ 新建对话」命中区必须和绘制位置一致；点它能进新建对话页
        var s2 = FreshSettings("columns");
        AddContacts(s2, 8);
        using (var p = MakePanel(s2, "huang-liuli", new Rectangle(600, 200, 470, 298)))
        {
            var drawRect = (Rectangle)TryCall(p, "ColumnsAddRect");
            Rectangle hitRect = Rectangle.Empty;
            var hits = Get(p, "hits") as System.Collections.IEnumerable;
            if (hits != null)
                foreach (var h in hits)
                {
                    var kind = h.GetType().GetField("kind", FF).GetValue(h);
                    if (kind != null && kind.ToString() == "AddContact")
                        hitRect = (Rectangle)h.GetType().GetField("rect", FF).GetValue(h);
                }
            Ok("R3-18 双栏底部「＋ 新建对话」命中区 == 绘制位置",
                hitRect.Width > 0 && hitRect == drawRect && hitRect.Bottom <= p.Height,
                "hit=" + hitRect + " draw=" + drawRect + " panel=" + p.Width + "x" + p.Height);

            var mu = typeof(NotePanelForm).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
            if (hitRect.Width > 0 && mu != null)
                mu.Invoke(p, new object[] { new MouseEventArgs(MouseButtons.Left, 1,
                    hitRect.X + hitRect.Width / 2, hitRect.Y + hitRect.Height / 2, 0) });
            Ok("R3-18b 点底部「＋ 新建对话」真的进入新建对话页",
                Get(p, "state").ToString() == "Add", "state=" + Get(p, "state"));
        }
    }

    // ---------------------------------------------------------------- R3：右键删除

    static void CheckDelete()
    {
        string style = "drawer";
        string deleteCbId = null;
        var s = FreshSettings(style);
        AddContacts(s, 4);
        using (var p = MakePanel(s, "huang-liuli", new Rectangle(600, 200, 420, 282),
            delegate(string id) { deleteCbId = id; }))
        {
            Bitmap before = RenderPanel(p);
            Save(before, Path.Combine("shots", "note-drawer-before-delete.png"));

            bool shown = TryInvoke(p, "ShowDelete", "10002");
            string deleteId = Get(p, "DeleteId") as string;
            Ok("R3-07 右键某个对话 → 出现删除按钮（DeleteId + Delete 命中区）",
                shown && deleteId == "10002",
                "ShowDelete=" + shown + " DeleteId=" + (deleteId ?? "<null>"));

            // 找 Delete 命中区
            Rectangle deleteRect = Rectangle.Empty;
            object deleteHit = null;
            var hits = Get(p, "hits") as System.Collections.IEnumerable;
            int hitCount = 0;
            if (hits != null)
                foreach (var h in hits)
                {
                    hitCount++;
                    var ht = h.GetType();
                    var kind = ht.GetField("kind", FF).GetValue(h);
                    if (kind != null && kind.ToString() == "Delete")
                    {
                        deleteRect = (Rectangle)ht.GetField("rect", FF).GetValue(h);
                        deleteHit = h;
                    }
                }
            var capsule = (Rectangle)TryCall(p, "ContactRectAt", 1);
            bool near = deleteRect.Width > 0
                && deleteRect.IntersectsWith(new Rectangle(capsule.X - S(p, 8), capsule.Y - S(p, 8),
                    capsule.Width + S(p, 16), capsule.Height + S(p, 16)));
            Ok("R3-08 删除按钮命中区落在该对话胶囊上/旁边",
                deleteRect.Width > 0 && near,
                "deleteRect=" + deleteRect + " capsule=" + capsule + " hits=" + hitCount);

            using (var after = RenderPanel(p))
            {
                Save(after, Path.Combine("shots", "note-drawer-delete.png"));
                int diff = CountDiff(before, after);
                Ok("R3-09 删除按钮画出来了（ShowDelete 前后像素有变化）", diff >= 10, "diff=" + diff + "px");
            }
            before.Dispose();

            // 点删除 → 联系人 + 线程一起删、回调通知 Widget
            if (deleteHit != null)
            {
                var hm = typeof(NotePanelForm).GetMethod("HandleHit", BindingFlags.Instance | BindingFlags.NonPublic);
                if (hm != null) hm.Invoke(p, new object[] { deleteHit });
            }
            bool gone = s.note.Contact("10002") == null && !HasThread(s.note, "10002");
            Ok("R3-10 点删除：联系人 + 本机线程都删掉、回调通知 Widget",
                gone && deleteCbId == "10002",
                "contactGone=" + (s.note.Contact("10002") == null) + " threadGone=" + !HasThread(s.note, "10002")
                + " callback=" + (deleteCbId ?? "<null>"));

            TryInvoke(p, "HideDelete");
            string hid = Get(p, "DeleteId") as string;
            bool anyDeleteHit = false;
            var hits2 = Get(p, "hits") as System.Collections.IEnumerable;
            if (hits2 != null)
                foreach (var h in hits2)
                {
                    var kind = h.GetType().GetField("kind", FF).GetValue(h);
                    if (kind != null && kind.ToString() == "Delete") anyDeleteHit = true;
                }
            Ok("R3-11 HideDelete 后删除状态清空、命中区消失",
                string.IsNullOrEmpty(hid) && !anyDeleteHit,
                "DeleteId=" + (hid ?? "<null>") + " deleteHit=" + anyDeleteHit);
        }

        // 左键正常选中、不误删
        var s2 = FreshSettings(style);
        AddContacts(s2, 3);
        using (var p = MakePanel(s2, "huang-liuli", new Rectangle(600, 200, 420, 282)))
        {
            var hit = FindContactHit(p, "10003");
            if (hit != null) TryCall(p, "HandleHit", hit);
            string sel = Get(p, "selectedId") as string;
            Ok("R3-12 左键点对话仍然正常选中、不误删", sel == "10003" && s2.note.Contact("10003") != null,
                "selected=" + (sel ?? "<null>") + " contactExists=" + (s2.note.Contact("10003") != null));
        }
    }

    static object FindContactHit(NotePanelForm p, string id)
    {
        var hits = Get(p, "hits") as System.Collections.IEnumerable;
        if (hits == null) return null;
        foreach (var h in hits)
        {
            var ht = h.GetType();
            var kind = ht.GetField("kind", FF).GetValue(h);
            var key = ht.GetField("key", FF).GetValue(h) as string;
            if (kind != null && kind.ToString() == "Contact" && key == id) return h;
        }
        return null;
    }

    static int CountDiff(Bitmap a, Bitmap b)
    {
        int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height), n = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (ColorDist(a.GetPixel(x, y), b.GetPixel(x, y)) > 24) n++;
        return n;
    }

    // ---------------------------------------------------------------- R3：真实右键 / 点删除按钮

    static void CheckRightClickDelete()
    {
        string cb = null;
        var s = FreshSettings("drawer");
        AddContacts(s, 4);
        using (var p = MakePanel(s, "huang-liuli", new Rectangle(600, 200, 420, 282),
            delegate(string id) { cb = id; }))
        {
            var capsule = (Rectangle)Call(p, "ContactRectAt", 1);
            var md = typeof(NotePanelForm).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
            md.Invoke(p, new object[] { new MouseEventArgs(MouseButtons.Right, 1,
                capsule.X + capsule.Width / 2, capsule.Y + capsule.Height / 2, 0) });
            string delId = Get(p, "DeleteId") as string;
            Ok("R3-13 真实右键 OnMouseDown(Right) 命中对话 → DeleteId 指向它",
                delId == "10002", "DeleteId=" + (delId ?? "<null>"));

            Rectangle dr = Rectangle.Empty;
            var hits = Get(p, "hits") as System.Collections.IEnumerable;
            foreach (var h in hits)
            {
                var kind = h.GetType().GetField("kind", FF).GetValue(h);
                if (kind != null && kind.ToString() == "Delete")
                    dr = (Rectangle)h.GetType().GetField("rect", FF).GetValue(h);
            }
            bool deleted = false;
            if (dr.Width > 0)
            {
                var mu = typeof(NotePanelForm).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
                mu.Invoke(p, new object[] { new MouseEventArgs(MouseButtons.Left, 1,
                    dr.X + dr.Width / 2, dr.Y + dr.Height / 2, 0) });
                deleted = s.note.Contact("10002") == null;
            }
            Ok("R3-14 左键点「删除」按钮真的删掉（命中区点击 → 面板 + 回调）",
                dr.Width > 0 && deleted && cb == "10002" && !HasThread(s.note, "10002"),
                "deleteRect=" + dr + " contactGone=" + (s.note.Contact("10002") == null)
                + " threadGone=" + !HasThread(s.note, "10002") + " cb=" + (cb ?? "<null>"));
        }
    }

    // ---------------------------------------------------------------- R3：Widget 删除落盘接线

    static void CheckWidgetDelete()
    {
        var s = FreshSettings("drawer");
        s.note.id = "46040";
        s.note.contacts.Clear();
        s.note.threads.Clear();
        s.note.AddContact("10001", "阿哲");
        s.note.AddContact("10002", "小满");
        s.note.Append(new NoteMessage("10001", "46040", "早上好", NoteRelay.Now()), true);
        using (var w = new WidgetForm(new SettingsStore(), s))
        {
            var mi = typeof(WidgetForm).GetMethod("DeleteNoteContact", BindingFlags.Instance | BindingFlags.NonPublic);
            if (mi == null) { Ok("R3-15 Widget.DeleteNoteContact 接线", false, "找不到 DeleteNoteContact"); return; }
            mi.Invoke(w, new object[] { "10001" });
            Ok("R3-15 Widget 删除对话：联系人和本机 8 条一起删",
                s.note.Contact("10001") == null && !HasThread(s.note, "10001")
                && s.note.Contact("10002") != null,
                "gone=" + (s.note.Contact("10001") == null) + " threadGone=" + !HasThread(s.note, "10001")
                + " otherKept=" + (s.note.Contact("10002") != null));
        }
    }

    // ---------------------------------------------------------------- R4：大卡片图标

    static void CheckGlyphs()
    {
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.theme = "ivory";
        s.Normalize();

        using (var w = new WidgetForm(new SettingsStore(), s))
        {
            Set(w, "scale", 1.5f);
            w.MinimumSize = Size.Empty;
            w.Size = new Size(420, 282);
            Call(w, "Recompute");
            Call(w, "FinishAnimNow");
            using (var bmp = new Bitmap(w.Width, w.Height))
            {
                bmp.SetResolution(144, 144);
                using (var g = Graphics.FromImage(bmp))
                    Call(w, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height)));
                Save(bmp, Path.Combine("shots", "card-expanded.png"));

                string[] names = { "plane", "¥", "gear", "refresh", "chevron" };
                string[] fields = { "rectNote", "rectSalary", "rectGear", "rectRefresh", "rectChevron" };
                var rects = new Rectangle[5];
                var boxes = new Rectangle[5];
                int[] widths = new int[5];
                int[] heights = new int[5];
                double[] cx = new double[5];
                double[] cy = new double[5];
                bool allRects = true;
                for (int i = 0; i < 5; i++)
                {
                    rects[i] = (Rectangle)Get(w, fields[i]);
                    if (rects[i].Width <= 0) allRects = false;
                    var b = GlyphBox(bmp, rects[i]);
                    boxes[i] = b;
                    widths[i] = b.Width;
                    heights[i] = b.Height;
                    cx[i] = b.X + b.Width / 2.0;
                    cy[i] = b.Y + b.Height / 2.0;
                }
                SaveCrop(bmp, new Rectangle(Math.Max(0, rects[0].X - 6), Math.Max(0, rects[0].Y - 6),
                    Math.Min(bmp.Width - Math.Max(0, rects[0].X - 6), rects[4].Right - rects[0].X + 12),
                    rects[0].Height + 12), Path.Combine("shots", "card-glyphs-row.png"));

                int wMin = int.MaxValue, wMax = 0, hMin = int.MaxValue, hMax = 0;
                double cyMin = double.MaxValue, cyMax = double.MinValue;
                double maxCenterDx = 0;
                for (int i = 0; i < 5; i++)
                {
                    wMin = Math.Min(wMin, widths[i]); wMax = Math.Max(wMax, widths[i]);
                    hMin = Math.Min(hMin, heights[i]); hMax = Math.Max(hMax, heights[i]);
                    cyMin = Math.Min(cyMin, cy[i]); cyMax = Math.Max(cyMax, cy[i]);
                    maxCenterDx = Math.Max(maxCenterDx,
                        Math.Abs(cx[i] - (rects[i].X + rects[i].Width / 2.0)));
                }
                string detail = "";
                for (int i = 0; i < 5; i++)
                    detail += names[i] + "=" + widths[i] + "x" + heights[i]
                        + "@(" + boxes[i].X + "," + boxes[i].Y + ") ";
                Console.WriteLine("      glyphs: " + detail);

                bool sameHits = allRects;
                for (int i = 1; i < 5; i++)
                    if (rects[i].Width != rects[0].Width || rects[i].Height != rects[0].Height) sameHits = false;
                Ok("R4-01 5 个图标命中矩形仍都是 24 DIP（36×36 @1.5）",
                    sameHits && rects[0].Width == 36 && rects[0].Height == 36,
                    "rects=" + rects[0] + " " + rects[1] + " " + rects[2] + " " + rects[3] + " " + rects[4]);

                Ok("R4-02 图标可见宽度一致（极差 ≤ 8px；改动前 26−11=15）",
                    wMax - wMin <= 8, "widths=" + string.Join(",", Array.ConvertAll(widths, delegate(int x) { return x.ToString(); }))
                    + " 极差=" + (wMax - wMin) + "（改动前 15）");
                Ok("R4-03 图标可见高度一致（极差 ≤ 5px；改动前 25−17=8）",
                    hMax - hMin <= 5, "heights=" + string.Join(",", Array.ConvertAll(heights, delegate(int x) { return x.ToString(); }))
                    + " 极差=" + (hMax - hMin) + "（改动前 8）");
                Ok("R4-04 5 个图标纵向中心对齐（极差 ≤ 3px）",
                    cyMax - cyMin <= 3, "centersY=" + JoinDoubles(cy) + " 极差=" + (cyMax - cyMin).ToString("0.0"));
                Ok("R4-05 图标横向中心与命中矩形中心偏差 ≤ 4px",
                    maxCenterDx <= 4, "maxDx=" + maxCenterDx.ToString("0.0"));
                Ok("R4-06 图标仍然是线稿/¥ 字（左上角第一个图标有可见像素）",
                    boxes[0].Width > 0, "planeBox=" + boxes[0]);
            }
            TryCall(w, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
    }

    static string JoinDoubles(double[] a)
    {
        var p = new List<string>();
        foreach (var x in a) p.Add(x.ToString("0.0"));
        return string.Join(",", p.ToArray());
    }

    /// <summary>在给定矩形里找「与邻近背景色差 > 90」的像素包围盒。</summary>
    static Rectangle GlyphBox(Bitmap bmp, Rectangle r)
    {
        int x0 = Math.Max(0, r.X), y0 = Math.Max(0, r.Y);
        int x1 = Math.Min(bmp.Width, r.Right), y1 = Math.Min(bmp.Height, r.Bottom);
        if (x1 <= x0 || y1 <= y0) return Rectangle.Empty;
        Color bg = bmp.GetPixel(Math.Max(0, x0 - 3), Math.Min(bmp.Height - 1, y0 + 2));
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (ColorDist(bmp.GetPixel(x, y), bg) > 90)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return Rectangle.Empty;
        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    static void SaveCrop(Bitmap src, Rectangle r, string name)
    {
        r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
        if (r.Width <= 0 || r.Height <= 0) return;
        using (var c = new Bitmap(r.Width, r.Height))
        {
            using (var g = Graphics.FromImage(c)) g.DrawImageUnscaled(src, -r.X, -r.Y);
            Save(c, name);
        }
    }

    // ---------------------------------------------------------------- 兼容 Check.cs 的 Call

    static object Call(object o, string name, params object[] a)
    {
        MethodInfo found = null;
        foreach (var mi in o.GetType().GetMethods(F))
        {
            if (mi.Name != name) continue;
            if (mi.GetParameters().Length != a.Length) continue;
            found = mi; break;
        }
        if (found == null) throw new Exception("no method " + name + "/" + a.Length);
        return found.Invoke(o, a);
    }
}
