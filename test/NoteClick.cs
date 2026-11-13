using System;
using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using WorkCountdown;

// 小纸条「添加联系人 / 查在线 / 发消息 / 连发 / 双击改备注」全流程点击诊断（真实鼠标回归探针）。
// 用法：NoteClick.exe（会话锁屏时真实鼠标到不了窗口，会直接 SKIP，解锁后再跑）
class NoteClick
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static int fails;

    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int index, StringBuilder info, int len, out uint needed);
    const uint LeftDown = 0x0002, LeftUp = 0x0004;
    static bool realMouse;    // 能不能真的移动光标：锁屏 / 无输入桌面时退化成 PostMessage 点按

    /// <summary>会话锁屏 / 没有输入桌面时真实鼠标到不了窗口，探测一下能不能移动光标（能动才用真鼠标）。</summary>
    static bool ProbeRealMouse()
    {
        POINT before, after;
        if (!GetCursorPos(out before)) return false;
        bool ok = SetCursorPos(600, 400);
        if (ok) { System.Threading.Thread.Sleep(60); ok = GetCursorPos(out after) && Math.Abs(after.X - 600) <= 2 && Math.Abs(after.Y - 400) <= 2; }
        SetCursorPos(before.X, before.Y);
        return ok;
    }

    [STAThread]
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { Run(); }
        catch (Exception ex)
        {
            try
            {
                Console.WriteLine("EX " + ex.GetType().FullName);
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
            catch { Console.WriteLine("EX (ToString failed)"); }
            Environment.ExitCode = 99;
        }
    }

    static void Run()
    {
        realMouse = ProbeRealMouse();
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Point savedCursor = Cursor.Position;

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.theme = "ivory";      // 和用户真机主题一致（浅色），浅色下最容易看出白雾/对比度问题
        s.note.style = "drawer";
        s.note.id = "48372";
        var card = new Rectangle(900, 150, 420, 282);
        string sentText = null;
        string addedRemark = null;
        string renamedId = null, renamedRemark = null;
        bool probed = false;

        using (var p = new NotePanelForm(s, Themes.Get("huang-liuli"), card, true,
            delegate(string to, string text) { sentText = text; return true; },
            null,
            delegate(string id, Action<bool> done) { probed = true; done(false); },
            delegate(string id, string remark) { addedRemark = remark; return s.note.AddContact(id, remark); },
            null,
            delegate(string id, string remark) { renamedId = id; renamedRemark = remark; }))
        {
            p.Show();
            p.Activate();
            Pump(500);
            bool foreground = Form.ActiveForm == p || GetForegroundWindow() == p.Handle;
            if (!foreground && realMouse)
            {
                realMouse = false;
                Console.WriteLine("输入：会话锁屏 / 无输入桌面，改用 PostMessage 点按（真机回归请解锁后重跑）");
            }
            else if (realMouse) Console.WriteLine("输入：真实鼠标");
            Console.WriteLine("panel " + p.Bounds + " scale=" + Get(p, "scale")
                + " visible=" + p.Visible + " closing=" + Get(p, "closing")
                + " foreground=" + foreground + " topmost=" + p.TopMost);

            Console.WriteLine("-- 1. 点「＋ 联系人」");
            Click(p, 523, 45);
            Console.WriteLine("   after click: state=" + Get(p, "state") + " closing=" + Get(p, "closing")
                + " active=" + (Form.ActiveForm == p));
            Check("进入添加联系人页面", Get(p, "state").ToString() == "Add", Get(p, "state"));

            Console.WriteLine("-- 2. 点 ID 输入框（以前会弹模态窗，之后所有按钮都点不动）");
            Click(p, 100, 110);
            Pump(300);
            TextBox tb = FindEditorBox(p);
            Check("ID 输入框打开的是非模态编辑窗", tb != null, tb == null ? "没找到编辑窗" : "ok");
            if (tb == null) { p.Close(); Cursor.Position = savedCursor; return; }
            Check("编辑窗打开时面板没有被禁用（能继续点别的按钮）", p.Enabled, "panel.Enabled=" + p.Enabled);

            Console.WriteLine("-- 3. 输入 93183 并点「查在线」");
            tb.Text = "93183";
            Pump(100);
            Check("输入实时同步到面板 addId", (string)Get(p, "addId") == "93183", "addId=" + Get(p, "addId"));
            Click(p, 82, 178);
            Pump(400);
            Check("点「查在线」触发查询并显示结果",
                probed && ((string)Get(p, "status") ?? "").Contains("没在线"), "status=" + Get(p, "status") + " probed=" + probed);
            if (realMouse)
                Check("点按钮时编辑窗已收起", Get(p, "fieldEditor") == null, "fieldEditor=" + Get(p, "fieldEditor"));
            else
                Console.WriteLine("skip 点按钮时编辑窗已收起（没有真实鼠标，失焦收起这条要解锁后再验）");

            Console.WriteLine("-- 4. 填备注（点备注框 → 输入 → 回车）并添加");
            Click(p, 100, 250);
            Pump(300);
            TextBox tb2 = FindEditorBox(p);
            Check("备注输入框也能打开", tb2 != null, tb2 == null ? "没找到编辑窗" : "ok");
            if (tb2 != null) { tb2.Text = "祥源"; tb2.Focus(); SendEnter(tb2); Pump(300); }
            Check("回车提交备注", (string)Get(p, "addRemark") == "祥源", "addRemark=" + Get(p, "addRemark"));
            Click(p, 300, 366);
            Pump(400);
            Check("点「添加为联系人」真的加上了", addedRemark == "祥源" && s.note.contacts.Count == 1,
                "remark=" + addedRemark + " contacts=" + s.note.contacts.Count);
            Check("加完切到和这个联系人的聊天", Get(p, "state").ToString() == "Chat" && (string)Get(p, "selectedId") == "93183",
                "state=" + Get(p, "state") + " selected=" + Get(p, "selectedId"));

            Console.WriteLine("-- 5. 点输入框 → 输入 → 回车发送");
            Click(p, 100, 380);
            Pump(500);
            Console.WriteLine("   after input click: state=" + Get(p, "state") + " editingField=" + Get(p, "editingField")
                + " fieldEditor=" + Get(p, "fieldEditor") + " pendingEdit=" + Get(p, "pendingEdit")
                + " heightNow=" + Get(p, "heightNow") + " bounds=" + p.Bounds);
            DumpForms(p);
            TextBox tb3 = FindEditorBox(p);
            Check("聊天输入框能打开", tb3 != null, tb3 == null ? "没找到编辑窗" : "ok");
            if (tb3 != null)
            {
                int expectY = p.Top + (int)Math.Round((714 - 16 * 1.5 - 38 * 1.5 + 2) * 1.0);
                int realY = tb3.TopLevelControl.Top;
                Check("长高后编辑窗贴在输入框上（不在旧位置）", Math.Abs(realY - expectY) <= 3,
                    "editorY=" + realY + " expect=" + expectY);
                tb3.Text = "第一次测试小纸条"; Pump(100); SendEnter(tb3);
            }
            Pump(400);
            Check("回车真的把消息发出去了", sentText == "第一次测试小纸条", "sentText=" + sentText);
            Check("发完进历史态", Get(p, "state").ToString() == "History", "state=" + Get(p, "state"));
            Check("发完不再提示「已发出」", string.IsNullOrEmpty((string)Get(p, "status")), "status=" + Get(p, "status"));
            TextBox tbKeep = FindEditorBox(p);
            Check("发完编辑窗还开着、文字清空（接着打，不用再点一次输入框）",
                tbKeep != null && tbKeep == tb3 && tbKeep.TextLength == 0,
                tbKeep == null ? "编辑窗被关掉了" : "相同实例=" + (tbKeep == tb3) + " text='" + tbKeep.Text + "'");

            Console.WriteLine("-- 5b. 不点输入框，接着打第二句再回车");
            sentText = null;
            if (tbKeep != null) { tbKeep.Text = "第二句不用再点"; Pump(100); SendEnter(tbKeep); }
            Pump(400);
            Check("连发第二句不用再点输入框", sentText == "第二句不用再点", "sentText=" + sentText);
            Check("第二句发完仍停在原状态（不整块刷新）",
                Get(p, "state").ToString() == "History", "state=" + Get(p, "state"));

            Console.WriteLine("-- 6. 历史态点发送按钮（高度变了以后命中区要跟着移）");
            Rectangle sendHit = Rectangle.Empty;
            foreach (var h in (IEnumerable)Get(p, "hits"))
            {
                var t = h.GetType();
                if (t.GetField("kind").GetValue(h).ToString() == "Send")
                    sendHit = (Rectangle)t.GetField("rect").GetValue(h);
            }
            Check("历史态的发送按钮命中区移到了底部", sendHit.Y > 600, "send=" + sendHit);
            Click(p, 100, 660);          // 底部输入框
            Pump(400);
            TextBox tb4 = FindEditorBox(p);
            Check("历史态还能再点输入框", tb4 != null, tb4 == null ? "没找到编辑窗" : "ok");
            if (tb4 != null) tb4.Text = "第二条";
            sentText = null;
            Click(p, 531, 661);          // 发送按钮（真实鼠标）
            Pump(400);
            Check("点发送按钮能把第二条发出去", sentText == "第二条", "sentText=" + sentText);
            TextBox tbAfterSend = FindEditorBox(p);
            Check("点发送按钮发完也会留一个空输入框（能接着输入）",
                tbAfterSend != null && tbAfterSend.TextLength == 0 && (!realMouse || tbAfterSend != tb4),
                tbAfterSend == null ? "没有编辑窗" : "text='" + tbAfterSend.Text + "'");

            Console.WriteLine("-- 7. 联系人名字居中 / 字号大一号；双击名字改备注用普通编辑窗");
            var fName = (Font)Get(p, "fName");
            Check("联系人名字字号大一号（≥9.3pt）", fName.Size >= 9.3f, "name=" + fName.Size + "pt");
            Rectangle contactHit = Rectangle.Empty;
            foreach (var h in (IEnumerable)Get(p, "hits"))
            {
                var t = h.GetType();
                if (t.GetField("kind").GetValue(h).ToString() == "Contact"
                    && (string)t.GetField("key").GetValue(h) == "93183")
                    contactHit = (Rectangle)t.GetField("rect").GetValue(h);
            }
            DoubleClick(p, 88, 106);
            Pump(300);
            TextBox tbRename = FindEditorBox(p);
            Check("双击联系人名字打开备注编辑窗", tbRename != null, tbRename == null ? "没找到编辑窗" : "ok");
            if (tbRename != null)
            {
                var fieldFont = (Font)Get(p, "fField");
                Check("备注编辑窗是普通输入框样式（不放大、不居中）",
                    tbRename.TextAlign != HorizontalAlignment.Center
                    && Math.Abs(tbRename.Font.Size - fieldFont.Size) <= 0.2f,
                    "align=" + tbRename.TextAlign + " font=" + tbRename.Font.Size + "pt / 普通输入 " + fieldFont.Size + "pt");
                Check("备注编辑窗和联系人名字一样大（没有变大）",
                    contactHit.Width > 0 && Math.Abs(tbRename.TopLevelControl.Width - contactHit.Width) <= 2
                    && Math.Abs(tbRename.TopLevelControl.Height - contactHit.Height) <= 2,
                    "editor=" + tbRename.TopLevelControl.Width + "x" + tbRename.TopLevelControl.Height
                    + " capsule=" + contactHit.Width + "x" + contactHit.Height);
                Check("备注编辑窗带出原来的备注", tbRename.Text == "祥源", "text='" + tbRename.Text + "'");
                tbRename.Text = "老祥"; Pump(100); SendEnter(tbRename);
            }
            Pump(400);
            Check("回车把备注改掉并交给 Widget",
                s.note.Contact("93183") != null && s.note.Contact("93183").remark == "老祥"
                && renamedId == "93183" && renamedRemark == "老祥",
                "remark=" + (s.note.Contact("93183") == null ? "?" : s.note.Contact("93183").remark)
                + " cb=" + renamedId + "/" + renamedRemark);

            p.Close();
        }
        Cursor.Position = savedCursor;
        Console.WriteLine(fails == 0 ? "ALL PASS" : ("FAILED " + fails));
        Environment.ExitCode = fails == 0 ? 0 : 1;
    }

    static void Check(string name, bool ok, object detail)
    {
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  [" + detail + "]");
    }

    static void Pump(int ms)
    {
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
    }

    static void Click(NotePanelForm p, int x, int y)
    {
        if (!realMouse) { PostClick(p, x, y); return; }
        // 用真实鼠标事件（SetCursorPos + mouse_event）：PostMessage 不会激活窗口，
        // 模拟不出「编辑窗失焦收起 → 同一次点击落到面板按钮上」这个真实顺序。
        Point sp = p.PointToScreen(new Point(x, y));
        SetCursorPos(sp.X, sp.Y);
        Pump(90);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        Pump(50);
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
        Pump(320);
    }

    static IntPtr LParam(int x, int y) { return (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF)); }

    /// <summary>锁屏 / 无输入桌面时的退路：直接给面板窗口发鼠标消息（命中区、双击判定照样走面板逻辑）。</summary>
    static void PostClick(NotePanelForm p, int x, int y)
    {
        // 真实鼠标点面板时，打开的编辑窗会先失焦收起；这里手动补这一步，顺序才和真机一致。
        // 顺便把（锁屏下的）光标挪到点击位置，让面板里「光标是否在面板上」的判断和真机一致。
        Point sp = p.PointToScreen(new Point(x, y));
        SetCursorPos(sp.X, sp.Y);
        Pump(30);
        foreach (Form f in Application.OpenForms)
        {
            if (f == p || f.IsDisposed || f.GetType().Name != "NoteField") continue;
            if (f.Bounds.Contains(sp)) return;      // 点在编辑窗自己身上：真实点击也到不了面板
            Call(p, "CloseFieldEditor");
            Pump(80);
            break;
        }
        IntPtr lp = LParam(x, y);
        PostMessage(p.Handle, 0x0200, IntPtr.Zero, lp);      // WM_MOUSEMOVE：记下落在面板里
        Pump(40);
        PostMessage(p.Handle, 0x0201, (IntPtr)1, lp);        // WM_LBUTTONDOWN
        Pump(40);
        PostMessage(p.Handle, 0x0202, IntPtr.Zero, lp);      // WM_LBUTTONUP
        Pump(320);
    }

    static object Call(object o, string name, params object[] a)
    {
        MethodInfo found = null;
        foreach (var mi in o.GetType().GetMethods(F))
        {
            if (mi.Name != name) continue;
            if (mi.GetParameters().Length != a.Length) continue;
            found = mi;
            break;
        }
        if (found == null) throw new Exception("no method " + name + "/" + a.Length);
        return found.Invoke(o, a);
    }

    // 真实双击（两次 down/up 间隔小于系统双击时间）
    static void DoubleClick(NotePanelForm p, int x, int y)
    {
        Point sp = p.PointToScreen(new Point(x, y));
        if (!realMouse)
        {
            SetCursorPos(sp.X, sp.Y);
            IntPtr lp = LParam(x, y);
            PostMessage(p.Handle, 0x0200, IntPtr.Zero, lp);
            Pump(30);
            PostMessage(p.Handle, 0x0201, (IntPtr)1, lp);
            Pump(30);
            PostMessage(p.Handle, 0x0202, IntPtr.Zero, lp);
            Pump(60);
            PostMessage(p.Handle, 0x0201, (IntPtr)1, lp);
            Pump(30);
            PostMessage(p.Handle, 0x0202, IntPtr.Zero, lp);
            Pump(320);
            return;
        }
        SetCursorPos(sp.X, sp.Y);
        Pump(80);
        for (int i = 0; i < 2; i++)
        {
            mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
            Pump(30);
            mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
            Pump(70);
        }
    }

    static void SendEnter(TextBox box)
    {
        IntPtr h = box.Handle;
        PostMessage(h, 0x0100, (IntPtr)0x0D, IntPtr.Zero);   // WM_KEYDOWN VK_RETURN
        PostMessage(h, 0x0101, (IntPtr)0x0D, IntPtr.Zero);   // WM_KEYUP VK_RETURN
    }

    // 面板自己之外、含 TextBox 的那个编辑窗
    static TextBox FindEditorBox(NotePanelForm p)
    {
        foreach (Form f in Application.OpenForms)
        {
            if (f == p || f.IsDisposed) continue;
            TextBox tb = FindTextBox(f);
            if (tb != null) return tb;
        }
        return null;
    }

    static TextBox FindTextBox(Control root)
    {
        foreach (Control c in root.Controls)
        {
            var tb = c as TextBox;
            if (tb != null) return tb;
            var inner = FindTextBox(c);
            if (inner != null) return inner;
        }
        return null;
    }

    static void DumpForms(NotePanelForm p)
    {
        foreach (Form f in Application.OpenForms)
            Console.WriteLine("   form: " + f.GetType().Name + " " + f.Bounds + " visible=" + f.Visible);
        var hits = (IEnumerable)Get(p, "hits");
        if (hits != null) foreach (var hit in hits)
        {
            var t = hit.GetType();
            Console.WriteLine("   hit " + t.GetField("kind").GetValue(hit) + " " + t.GetField("rect").GetValue(hit));
        }
    }

    static object Get(object o, string name)
    {
        var t = o.GetType();
        var fi = t.GetField(name, F);
        if (fi != null) return fi.GetValue(o);
        var pi = t.GetProperty(name, F);
        return pi == null ? null : pi.GetValue(o, null);
    }
}
