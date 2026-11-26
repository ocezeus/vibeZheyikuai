using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WorkCountdown;

// 小纸条出图：A 抽屉式 / C 极简双栏的四个状态 + 卡片贴边闪光。
// 用法：NoteShot.exe [drawer|columns] [输出目录]
class NoteShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

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
        Environment.SetEnvironmentVariable("WC_NO_RELAY", "1");
        // 和真机一致：150% 下出 1.5 倍的图（以前进程不感知 DPI，出的是 96 DPI 的设计尺寸）
        SetProcessDPIAware();
        Application.EnableVisualStyles();

        string style = args != null && args.Length > 0 ? args[0] : "drawer";
        string themeId = args != null && args.Length > 2 ? args[2] : "huang-liuli";
        string dir = args != null && args.Length > 1 ? args[1]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "note");
        Directory.CreateDirectory(dir);

        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.theme = themeId;
        s.note.style = style;
        s.note.id = "48372";
        s.note.nickname = "小明";              // v3.5：本机昵称（对方加了直接看到）
        s.note.AddContact("62015", "阿哲");     // 有本机备注：显示备注
        s.note.AddContact("11788", "");         // 没备注：显示对方自己的昵称
        s.note.Contact("11788").nick = "小满";
        s.note.AddContact("90554", "");
        s.note.Contact("90554").nick = "老周";
        s.note.AddContact("38102", "小赵");
        s.note.AddContact("55201", "阿凯");
        s.note.AddContact("71934", "老王");
        // 对话码对话：任意 5 位字符，谁输同一个码谁就进这个对话
        s.note.AddConversation("项目组#1", "项目群");
        var th = s.note.Thread("62015");
        th.unread = 2;
        long now = NoteRelay.Now();
        s.note.Append(new NoteMessage("48372", "62015", "早，今天的评审几点？", now - 3600), false);
        s.note.Append(new NoteMessage("62015", "48372", "下午 4 点，会议室 B", now - 3500), true);
        s.note.Append(new NoteMessage("48372", "62015", "接口那版发你邮箱了", now - 3000), false);
        s.note.Append(new NoteMessage("62015", "48372", "收到，中午看", now - 2900), true);
        s.note.Append(new NoteMessage("48372", "62015", "评审材料加了两页压测", now - 900), false);
        s.note.Append(new NoteMessage("62015", "48372", "好，辛苦", now - 800), true);
        s.note.Append(new NoteMessage("48372", "62015", "压测刚跑完，马上发你", now - 120), false);
        s.note.Append(new NoteMessage("62015", "48372", "晚上的评审推迟到 8 点", now - 60), true);
        // 群聊消息带发送者昵称（v3.5）：气泡 / 历史行直接显示「阿哲：…」
        s.note.Append(new NoteMessage("93183", "项目组#1", "周会材料我放群文件了", now - 700, "阿哲"), true);
        s.note.Append(new NoteMessage("48372", "项目组#1", "收到，我先看下", now - 500), false);
        s.note.Append(new NoteMessage("11788", "项目组#1", "我下午补一页数据", now - 180, "小满"), true);
        // 「大字报」公共房间：所有人都能看到（Normalize 会自动加进来并排最前面）
        s.note.Append(new NoteMessage("93183", NoteSettings.BulletinCode, "周五下午三点全员会，别迟到", now - 400, "阿哲"), true);
        s.note.Append(new NoteMessage("11788", NoteSettings.BulletinCode, "收到", now - 200, "小满"), true);
        s.Normalize();

        var card = new Rectangle(840, 90, 420, 282);
        using (var p = new NotePanelForm(s, Themes.Get(themeId), card, true, null, null, null, null, null))
        {
            Render(p, Path.Combine(dir, "note-" + style + "-contacts.png"));

            // v5.2：对话全部铺开，不需要滚；右键直接在列表上弹「删除」按钮
            Call(p, "ShowDelete", s.note.contacts[1].id);
            Render(p, Path.Combine(dir, "note-" + style + "-delete.png"));
            Call(p, "HideDelete");

            Call(p, "SelectContact", "62015", false);
            Set(p, "heightNow", (float)Get(p, "heightTo"));   // 抽屉式未选对话是收窄的，选完要等长高（离屏没有定时器，直接落位）
            Set(p, "stateAt", Environment.TickCount - 800);
            Render(p, Path.Combine(dir, "note-" + style + "-chat.png"));
            var stateType = typeof(NotePanelForm).GetNestedType("PanelState", BindingFlags.NonPublic);
            object history = Enum.Parse(stateType, "History");
            Call(p, "SetState", history, true);
            Set(p, "heightNow", (float)Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 1200);
            Render(p, Path.Combine(dir, "note-" + style + "-history.png"));
            object add = Enum.Parse(stateType, "Add");
            Call(p, "SetState", add, false);
            Set(p, "heightNow", (float)Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 800);
            Render(p, Path.Combine(dir, "note-" + style + "-add.png"));

            // 对话码对话：气泡里带发送者 5 位 ID
            Call(p, "SelectContact", "项目组#1", false);
            Set(p, "heightNow", (float)Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 800);
            Render(p, Path.Combine(dir, "note-" + style + "-room.png"));

            // v5.2 公共房间「大字报」：所有人默认都有、人人都能发
            Call(p, "SelectContact", NoteSettings.BulletinCode, false);
            Set(p, "heightNow", (float)Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 800);
            Render(p, Path.Combine(dir, "note-" + style + "-bulletin.png"));
        }

        // 卡片状态点高亮闪烁（离屏渲染 Widget）
        using (var w = new WidgetForm(new SettingsStore(), s))
        {
            Set(w, "scale", 1.5f);
            w.MinimumSize = Size.Empty;
            w.Size = new Size(420, 282);
            Call(w, "Recompute");
            Call(w, "FinishAnimNow");
            Set(w, "noteFlashAt", Environment.TickCount - 300);
            using (var bmp = new Bitmap(w.Width, w.Height))
            {
                bmp.SetResolution(144, 144);
                using (var g = Graphics.FromImage(bmp))
                    Call(w, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w.Width, w.Height)));
                bmp.Save(Path.Combine(dir, "note-widget-dot.png"), ImageFormat.Png);
            }
            Set(w, "noteFlashAt", 0);
            Call(w, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }
        Console.WriteLine("ok " + dir);
    }

    static void Render(NotePanelForm p, string path)
    {
        int w = (int)Get(p, "winWidth");
        int h = (int)(float)Get(p, "heightNow");
        using (var bmp = new Bitmap(w, h))
        {
            bmp.SetResolution(144, 144);
            using (var g = Graphics.FromImage(bmp))
                Call(p, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
            bmp.Save(path, ImageFormat.Png);
        }
        Console.WriteLine("ok " + path + "  " + w + "x" + h);
    }

    static void Set(object o, string name, object v)
    {
        o.GetType().GetField(name, F).SetValue(o, v);
    }

    static object Get(object o, string name)
    {
        return o.GetType().GetField(name, F).GetValue(o);
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
}
