using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WorkCountdown;

// 皮肤出图探针：小纸条面板 + 右键胶囊菜单，五套皮肤 + 默认（solid / glass）离屏渲染。
// 用法：SkinNoteShot.exe [skin|all] [输出目录]
//   skin = default / default-glass / quiet / mica / paper / instrument / neon（留空 = all）
// 只出图，不动产品行为：WC_NO_SAVE=1 + WC_NO_RELAY=1，设置对象是临时构造的，不读也不写真实配置。
class SkinNoteShot
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
        SetProcessDPIAware();
        Application.EnableVisualStyles();

        string which = args != null && args.Length > 0 ? args[0] : "all";
        string dir = args != null && args.Length > 1 ? args[1]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "skins");
        Directory.CreateDirectory(dir);

        if (which == "all")
            foreach (var id in new string[] { "default", "default-glass", "quiet", "mica", "paper", "instrument", "neon" })
                Shoot(id, dir);
        else if (which == "live" || which.StartsWith("live-", StringComparison.Ordinal))
            Live(which == "live" ? "paper" : which.Substring(5), dir);
        else
            Shoot(which, dir);

        Console.WriteLine("ok " + dir);
    }

    /// <summary>
    /// 实机烟测：真窗口 + UpdateLayeredWindow 推送，截桌面上那一块。
    /// 验证皮肤配色在真实分层窗里也画得出来（离屏探针走的是同一条 Render 路径，但 Push 只有真窗口才走）。
    /// </summary>
    static void Live(string id, string dir)
    {
        var s = MakeSettings(id, "solid");
        Environment.SetEnvironmentVariable("WC_NO_EXIT_ANIM", "1");
        var card = new Rectangle(860, 300, 420, 282);
        using (var p = new NotePanelForm(s, Skins.ResolveTheme(s.ui), card, true, null, null, null, null, null))
        {
            p.Show();
            Pump(650);
            Capture(p.Bounds, Path.Combine(dir, "live-" + id + "-note-contacts.png"));
            Call(p, "SelectContact", "62015", false);
            Pump(650);
            Capture(p.Bounds, Path.Combine(dir, "live-" + id + "-note-chat.png"));
            p.ClosePanel();
            Pump(300);
        }

        using (var m = new BubbleMenuForm(s, Skins.ResolveTheme(s.ui), card, true,
            null, null, null, null, null))
        {
            m.Show();
            Pump(700);
            Capture(m.Bounds, Path.Combine(dir, "live-" + id + "-bubble.png"));
            Call(m, "OpenItem", 1);
            Pump(800);
            Capture(m.Bounds, Path.Combine(dir, "live-" + id + "-bubble-theme.png"));
            m.CloseMenu();
            Pump(300);
        }
        Console.WriteLine("ok live " + id);
    }

    static void Capture(Rectangle r, string path)
    {
        r.Inflate(10, 10);
        r.Intersect(Screen.FromRectangle(r).Bounds);
        using (var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
            bmp.Save(path, ImageFormat.Png);
        }
        Console.WriteLine("  ok " + Path.GetFileName(path) + "  " + r.Width + "x" + r.Height);
    }

    static void Pump(int ms)
    {
        int end = Environment.TickCount + ms;
        while (Environment.TickCount < end)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(10);
        }
    }

    static void Shoot(string id, string dir)
    {
        bool glass = id == "default-glass";
        string skinId = glass ? "default" : id;
        var s = MakeSettings(skinId, glass ? "glass" : "solid");
        string tag = id;

        // 小纸条：模拟旧调用方（Widget 还传 Themes.Get(ui.theme)），验证皮肤在窗体内部覆盖
        using (var p = new NotePanelForm(s, Themes.Get(s.ui.theme), new Rectangle(840, 90, 420, 282), true,
            null, null, null, null, null))
        {
            RenderNote(p, Path.Combine(dir, tag + "-note-contacts.png"), 1.0);

            // 选一个对话看聊天气泡（抽屉式未选对话是收窄的，先落位到聊天态高度）
            Call(p, "SelectContact", "62015", false);
            Set(p, "heightNow", (float)Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 800);
            RenderNote(p, Path.Combine(dir, tag + "-note-chat.png"), 1.0);

            // 历史 8 条
            var stateType = typeof(NotePanelForm).GetNestedType("PanelState", BindingFlags.NonPublic);
            object history = Enum.Parse(stateType, "History");
            Call(p, "SetState", history, true);
            Set(p, "heightNow", (float)Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 1200);
            RenderNote(p, Path.Combine(dir, tag + "-note-history.png"), 1.65);

            // 新建对话页（输入框 / 按钮 / 字段）
            object add = Enum.Parse(stateType, "Add");
            Call(p, "SetState", add, false);
            Set(p, "heightNow", (float)Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 800);
            RenderNote(p, Path.Combine(dir, tag + "-note-add.png"), 1.0);

            // 对话码群聊：对方气泡带发送者昵称
            Call(p, "SelectContact", "项目组#1", false);
            Set(p, "heightNow", (float)Get(p, "heightTo"));
            Set(p, "stateAt", Environment.TickCount - 800);
            RenderNote(p, Path.Combine(dir, tag + "-note-room.png"), 1.0);
        }

        // 右键胶囊：收起 / 修改主题长开 / 新增日程长开
        ShootBubble(s, Path.Combine(dir, tag + "-bubble-collapsed.png"), -1);
        ShootBubble(s, Path.Combine(dir, tag + "-bubble-theme.png"), 1);
        ShootBubble(s, Path.Combine(dir, tag + "-bubble-schedule.png"), 0);
        Console.WriteLine("ok " + tag);
    }

    static Settings MakeSettings(string skinId, string bgStyle)
    {
        var s = new Settings();
        s.lark.enabled = false;
        s.calendarEnabled = false;
        s.ui.skin = skinId;
        s.ui.bgStyle = bgStyle;
        s.ui.theme = "huang-liuli";
        s.note.style = "drawer";
        s.note.id = "48372";
        s.note.nickname = "小明";
        s.note.AddContact("62015", "阿哲");
        s.note.AddContact("11788", "");
        s.note.Contact("11788").nick = "小满";
        s.note.AddContact("90554", "");
        s.note.Contact("90554").nick = "老周";
        s.note.AddContact("38102", "小赵");
        s.note.AddContact("55201", "阿凯");
        s.note.AddContact("71934", "老王");
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
        s.note.Append(new NoteMessage("93183", "项目组#1", "周会材料我放群文件了", now - 700, "阿哲"), true);
        s.note.Append(new NoteMessage("48372", "项目组#1", "收到，我先看下", now - 500), false);
        s.note.Append(new NoteMessage("11788", "项目组#1", "我下午补一页数据", now - 180, "小满"), true);
        return s;
    }

    // ---------------------------------------------------------------- 小纸条

    static void RenderNote(NotePanelForm p, string path, double zoom)
    {
        int w = (int)Get(p, "winWidth");
        int h = (int)(float)Get(p, "heightNow");
        int pad = 54;
        int ow = (int)Math.Round((w + pad * 2) * zoom), oh = (int)Math.Round((h + pad * 2) * zoom);
        using (var bmp = new Bitmap(ow, oh, PixelFormat.Format32bppPArgb))
        {
            bmp.SetResolution(96f * (float)zoom, 96f * (float)zoom);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                PaintDesktop(g, new Rectangle(0, 0, ow, oh));
                g.ScaleTransform((float)zoom, (float)zoom);
                g.TranslateTransform(pad, pad);
                Call(p, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
            }
            bmp.Save(path, ImageFormat.Png);
        }
        Console.WriteLine("  ok " + Path.GetFileName(path) + "  " + ow + "x" + oh);
    }

    // ---------------------------------------------------------------- 右键胶囊

    static void ShootBubble(Settings s, string path, int index)
    {
        var card = new Rectangle(40, 300, 336, 102);
        using (var m = new BubbleMenuForm(s, Themes.Get(s.ui.theme), card, true,
            null, null, null, null, null))
        {
            Set(m, "openedAt", Environment.TickCount - 5000);
            if (index >= 0)
            {
                Call(m, "OpenItem", index);
                int extra = (int)Call(m, "S", (float)BubbleMenuForm.ExtraHDip((BubbleAction)index));
                Set(m, "animating", false);
                Set(m, "openExtra", (double)extra);
                Set(m, "openTo", (double)extra);
                Call(m, "LayoutWindow");
            }
            int pad = 64;
            using (var bmp = new Bitmap(m.Width + pad * 2, m.Height + pad * 2, PixelFormat.Format32bppPArgb))
            {
                bmp.SetResolution(96f, 96f);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    PaintDesktop(g, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    g.TranslateTransform(pad, pad);
                    Call(m, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, m.Width, m.Height)));
                }
                bmp.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("  ok " + Path.GetFileName(path));
            m.CloseMenu();
        }
    }

    /// <summary>桌面上的一块软渐变背景：用来验证磨砂玻璃 / 半透明面板透出的东西。</summary>
    static void PaintDesktop(Graphics g, Rectangle r)
    {
        using (var br = new LinearGradientBrush(r, Color.FromArgb(46, 56, 78), Color.FromArgb(18, 22, 33), 115f))
            g.FillRectangle(br, r);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(r.X - r.Width * 0.15f, r.Y - r.Height * 0.35f, r.Width * 0.85f, r.Height * 1.05f);
            using (var pb = new PathGradientBrush(path))
            {
                pb.CenterColor = Color.FromArgb(70, 96, 160);
                pb.SurroundColors = new[] { Color.FromArgb(0, 30, 40, 60) };
                pb.FocusScales = new PointF(0.05f, 0.05f);
                g.FillPath(pb, path);
            }
        }
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(r.Right - r.Width * 0.72f, r.Bottom - r.Height * 0.85f, r.Width * 0.8f, r.Height * 0.85f);
            using (var pb = new PathGradientBrush(path))
            {
                pb.CenterColor = Color.FromArgb(52, 120, 96);
                pb.SurroundColors = new[] { Color.FromArgb(0, 18, 22, 33) };
                pb.FocusScales = new PointF(0.05f, 0.05f);
                g.FillPath(pb, path);
            }
        }
    }

    // ---------------------------------------------------------------- 反射

    static void Set(object o, string name, object v)
    {
        var f = o.GetType().GetField(name, F);
        if (f == null) throw new Exception("no field " + name);
        f.SetValue(o, v);
    }

    static object Get(object o, string name)
    {
        var f = o.GetType().GetField(name, F);
        if (f == null) throw new Exception("no field " + name);
        return f.GetValue(o);
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
