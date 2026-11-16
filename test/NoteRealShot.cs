using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 用用户真实配置（只读）把小纸条面板渲染出来，排查「最新一条聊天不显示」。
// 用法：NoteRealShot.exe <联系人 id> [chat|history]
class NoteRealShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a)
    {
        foreach (var m in o.GetType().GetMethods(F))
            if (m.Name == n && m.GetParameters().Length == (a == null ? 0 : a.Length)) return m.Invoke(o, a);
        throw new MissingMethodException(o.GetType().Name + "." + n);
    }

    [STAThread]
    static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
        Application.EnableVisualStyles();
        string want = args != null && args.Length > 0 ? args[0] : "MIKFU";
        string state = args != null && args.Length > 1 ? args[1] : "chat";

        try
        {
        Settings s;
        string id = want;
        bool synth = (args != null && args.Length > 2 && (args[2] == "synth" || args[2] == "synthroom"));
        bool synthRoom = (args != null && args.Length > 2 && args[2] == "synthroom");
        if (synth)
        {
            s = new Settings();
            s.lark.enabled = false;
            s.calendarEnabled = false;
            s.note.id = "48372";
            if (synthRoom)
            {
                s.note.AddConversation("MIKFU", "压测群");
                for (int i = 0; i < 16; i++)
                {
                    string from = (i % 2 == 0) ? "93183" : "10825";
                    s.note.Append(new NoteMessage(from, "MIKFU", "群消息" + i + " 压测滚动", 1731400000 + i * 60,
                        (i % 2 == 0) ? "阿哲" : "小满"), true);
                }
                id = "MIKFU";
            }
            else
            {
                s.note.AddContact("11111", "压测群");
                for (int i = 0; i < 16; i++)
                {
                    bool inc = i % 2 == 0;
                    s.note.Append(new NoteMessage(inc ? "11111" : "48372", inc ? "48372" : "11111", "消息" + i + " 压测滚动", 1731400000 + i * 60), inc);
                }
                id = "11111";
            }
        }
        else
        {
            var store = new SettingsStore();
            s = store.Load().Settings;
        }
        var note = s.note;
        if (note.Contact(id) == null)
        {
            foreach (var c in note.contacts) { id = c.id; break; }
        }
        var th = note.Thread(id);
        Console.WriteLine("contact=" + id + " room=" + note.IsRoom(id) + " messages=" + th.messages.Count + " style=" + note.style + " scaleDPI? ");

        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "real-note");
        Directory.CreateDirectory(dir);

        int w = 392, h = 282;
        using (var p = new NotePanelForm(s, Skins.ResolveTheme(s.ui), new Rectangle(0, 0, 420, 282), true,
                   null, null, null, null, null))
        {

            Set(p, "selectedId", id);
            var st = Enum.Parse(p.GetType().GetNestedType("PanelState", BindingFlags.NonPublic), "Chat");
            Call(p, "SetState", st, false);
            // 高度动画直接落到位（别等 320ms）
            Set(p, "heightAnimating", false);
            Set(p, "heightNow", (float)h);
            Set(p, "heightTo", (float)h);
            p.SetBounds(0, 0, w, h);
            Call(p, "BuildHits");

            Shot(p, w, h, Path.Combine(dir, "chat-" + id + ".png"));

            // 历史态（点输入框长高后的样子，一屏 8 条）
            var stH = Enum.Parse(p.GetType().GetNestedType("PanelState", BindingFlags.NonPublic), "History");
            Call(p, "SetState", stH, true);
            System.Threading.Thread.Sleep(700);   // 等 8 行错峰淡入跑完（真机是 anim 定时器，探针靠 TickCount）
            Set(p, "heightAnimating", false);
            Set(p, "heightNow", (float)476);
            Set(p, "heightTo", (float)476);
            p.SetBounds(0, 0, w, 476);
            Call(p, "BuildHits");
            Shot(p, w, 476, Path.Combine(dir, "history-" + id + ".png"));
            // 往上翻到最老的 8 条（滚动条应该到最上面）
            Call(p, "ScrollHistory", 99);
            System.Threading.Thread.Sleep(80);
            Call(p, "Invalidate");
            Shot(p, w, 476, Path.Combine(dir, "history-oldest-" + id + ".png"));
            Console.WriteLine("historyScroll=" + Get(p, "historyScroll") + " max=" + Call(p, "HistoryMaxScroll"));
            Call(p, "ScrollHistory", -99);
            Call(p, "Invalidate");
            System.Threading.Thread.Sleep(60);
            Shot(p, w, 476, Path.Combine(dir, "history-latest-" + id + ".png"));

            // 再渲染一次「聊天」态，模拟刚发出一条（最新一条）
            Call(p, "SetState", st, false);
            Set(p, "heightAnimating", false);
            Set(p, "heightNow", (float)h);
            Set(p, "heightTo", (float)h);
            p.SetBounds(0, 0, w, h);
            var last = th.messages.Count > 0 ? th.messages[th.messages.Count - 1] : null;
            if (last != null) Console.WriteLine("last message: ts=" + last.ts + " text=" + last.text);
            Call(p, "BuildHits");
            Shot(p, w, h, Path.Combine(dir, "chat-after-" + id + ".png"));
        }
        Console.WriteLine("-> " + dir);
        }
        catch (Exception ex)
        {
            Console.WriteLine("EX " + ex.GetType().Name + ": " + ex.Message);
            var m = ex.TargetSite;
            if (m != null) Console.WriteLine("at " + (m.DeclaringType == null ? "?" : m.DeclaringType.Name) + "." + m.Name);
            if (ex.InnerException != null) Console.WriteLine("inner " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
        }
    }

    static void Shot(Form f, int w, int h, string path)
    {
        using (var bmp = new Bitmap(w, h))
        {
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(30, 34, 42));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                Call(f, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
            }
            bmp.Save(path);
        }
    }
}
