using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 皮肤卡片离屏渲染探针：把六套「整体界面主题」的展开卡 / 收起横条 / 工资面板各渲一张，
// 输出到 test/visual/skins/，用于肉眼核对（1 CSS px = 1 DIP，卡片 280×188 / 224×68）。
// 只读用户配置，绝不回写（WC_NO_SAVE=1）；皮肤 id 只在内存里改。
class SkinCardShot
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }
    static object Prop(object o, string n) { return o.GetType().GetProperty(n, F).GetValue(o, null); }
    static object Field(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }

    const int Pad = 20;   // 画布留白，方便看圆角和外圈
    /// <summary>状态文案里带倒计时秒数：钉死一个时间，同一份代码重复跑出来的图能逐像素对比。</summary>
    static readonly DateTime FixedNow = new DateTime(2026, 11, 16, 13, 30, 0);

    [STAThread]
    static void Main(string[] args)
    {
        // 可选参数：args[0] 覆盖默认皮肤用的 39 套主题 id，args[1] 覆盖背景风格（solid / glass）
        string themeArg = args.Length > 0 ? args[0] : null;
        string bgArg = args.Length > 1 ? args[1] : null;
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        var store = new SettingsStore();
        var s = store.Load().Settings;

        // 探针要稳定：关掉动效抑制、固定流光相位；工资面板用一份假数据（只在本进程内存里）
        s.ui.reducedMotion = false;
        s.ui.cardGlow = "classic";
        if (bgArg != null) s.ui.bgStyle = bgArg;
        if (s.salary == null) s.salary = new SalarySettings();
        s.salary.monthly = 12345.678m;

        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        snap.fetchedAt = DateTime.Now;

        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "skins");
        Directory.CreateDirectory(dir);

        var ids = new[] { "default", "quiet", "mica", "paper", "instrument", "neon" };
        var expanded = new Bitmap[ids.Length];
        var collapsedPills = new Bitmap[ids.Length];
        var names = new string[ids.Length];

        using (var form = new WidgetForm(store, s))
        {
            Set(form, "snapshot", snap);
            Set(form, "flowPhase", 0.42);

            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                SkinDef skin = Skins.Get(id);
                s.ui.skin = id;
                s.ui.theme = themeArg ?? skin.defaultThemeId ?? s.ui.theme;
                names[i] = skin.name + "  " + id;

                Set(form, "scale", 1f);
                form.MinimumSize = Size.Empty;
                Set(form, "collapsed", false);
                Set(form, "salaryOpen", false);
                int h = (int)Prop(form, "ExpandedH");
                form.Size = new Size(280, h);
                Call(form, "Recompute");
                Freeze(form, s, snap);
                var painted = (ThemeDef)Field(form, "theme");
                string wantTheme = skin.colors != null ? skin.colors.id : s.ui.theme;
                if (painted == null || painted.id != wantTheme)
                    Console.WriteLine("  !! theme mismatch " + id + " want=" + wantTheme + " got=" + (painted == null ? "null" : painted.id));
                expanded[i] = Shot(form, 280, h, BackOf(skin));
                expanded[i].Save(Path.Combine(dir, id + "-expanded-1x.png"));

                Set(form, "collapsed", true);
                form.Size = new Size(224, 68);
                Call(form, "Recompute");
                Freeze(form, s, snap);
                collapsedPills[i] = Shot(form, 224, 68, BackOf(skin));
                collapsedPills[i].Save(Path.Combine(dir, id + "-collapsed-1x.png"));

                // 工资面板：卡片整体拉长 116 DIP
                Set(form, "collapsed", false);
                Set(form, "salaryOpen", true);
                form.Size = new Size(280, h + 116);
                Call(form, "Recompute");
                Freeze(form, s, snap);
                using (var salary = Shot(form, 280, h + 116, BackOf(skin)))
                    salary.Save(Path.Combine(dir, id + "-salary-1x.png"));

                // 1.5 倍：检查高分屏下的笔画、螺丝、发光
                Set(form, "scale", 1.5f);
                Set(form, "salaryOpen", false);
                Set(form, "collapsed", false);
                int h15 = (int)Prop(form, "ExpandedH");
                form.Size = new Size(420, h15);
                Call(form, "Recompute");
                Freeze(form, s, snap);
                using (var big = Shot(form, 420, h15, BackOf(skin)))
                    big.Save(Path.Combine(dir, id + "-expanded-150.png"));
                Set(form, "scale", 1f);

                // 提醒层：减少动态效果时是整张提醒卡（确定性最好），检查皮肤的圆角裁剪和提醒配色
                bool reduced = s.ui.reducedMotion;
                s.ui.reducedMotion = true;
                Set(form, "collapsed", false);
                form.Size = new Size(280, h);
                Call(form, "Recompute");
                Freeze(form, s, snap);
                Set(form, "remindEvent", ProbeEvent());
                Set(form, "reminding", true);
                using (var reminder = Shot(form, 280, h, BackOf(skin)))
                    reminder.Save(Path.Combine(dir, id + "-reminder-1x.png"));
                Set(form, "reminding", false);
                s.ui.reducedMotion = reduced;

                Console.WriteLine(id + ": expanded h=" + h);
            }

            // 六套展开卡一排、六条收起横条一排，各出一张联络图（1x = 设计尺寸）
            using (var sheet = new Bitmap(Pad + ids.Length * (280 + Pad), 46 + 188 + Pad))
                Compose(sheet, expanded, names, 280, 188, "six skins · expanded 280x188 · 1px = 1 DIP",
                    Path.Combine(dir, "sheet-expanded.png"));
            using (var sheet = new Bitmap(Pad + ids.Length * (224 + Pad), 46 + 68 + Pad))
                Compose(sheet, collapsedPills, names, 224, 68, "six skins · collapsed 224x68",
                    Path.Combine(dir, "sheet-collapsed.png"));
            foreach (var b in expanded) if (b != null) b.Dispose();
            foreach (var b in collapsedPills) if (b != null) b.Dispose();
        }
        Console.WriteLine("out=" + dir);
    }

    static void Compose(Bitmap sheet, Bitmap[] cards, string[] names, int cardW, int cardH, string title, string path)
    {
        sheet.SetResolution(96, 96);
        using (var g = Graphics.FromImage(sheet))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, sheet.Width, sheet.Height),
                Color.FromArgb(22, 24, 30), Color.FromArgb(38, 40, 48), 90f))
                g.FillRectangle(bg, 0, 0, sheet.Width, sheet.Height);
            using (var font = UiFont.Make(9.5f, FontStyle.Bold))
            using (var small = UiFont.Make(8.5f, FontStyle.Regular))
            using (var ink = new SolidBrush(Color.FromArgb(228, 232, 240)))
            using (var dim = new SolidBrush(Color.FromArgb(140, 148, 164)))
            {
                g.DrawString(title, small, dim, Pad, 6);
                for (int i = 0; i < names.Length; i++)
                {
                    int x = Pad + i * (cardW + Pad);
                    int y = 46;
                    g.DrawString(names[i], font, ink, x, y - 20);
                    if (cards[i] != null) g.DrawImageUnscaled(cards[i], x, y);
                }
            }
        }
        sheet.Save(path);
    }

    /// <summary>提醒层用的假日程（只在内存里）。</summary>
    static ReminderEvent ProbeEvent()
    {
        return new ReminderEvent
        {
            id = "probe-reminder",
            title = "需求对齐会",
            start = FixedNow.ToString("yyyy-MM-dd HH:mm"),
            end = FixedNow.AddMinutes(60).ToString("yyyy-MM-dd HH:mm"),
            place = "会议室 A",
            detail = "过一遍这周的主题皮肤改动",
            source = "lark"
        };
    }

    /// <summary>浅色皮肤放在浅灰底上、深色皮肤放深底上，看圆角边缘和描边是否干净。</summary>
    static Color BackOf(SkinDef skin)
    {
        return skin.light ? Color.FromArgb(232, 229, 220) : Color.FromArgb(18, 20, 26);
    }

    /// <summary>
    /// 直接调 RenderCard 画到 96 DPI 的画布上（不经过 OnPaint）：
    /// OnPaint 里的 frame 是按屏幕 DPI 建的，DrawImage 到 96 DPI 画布会按 96/144 缩一道，
    /// 探针只要看 1 DIP = 1 像素的设计尺寸，所以绕开那一层。
    /// </summary>
    static Bitmap Shot(WidgetForm form, int w, int h, Color bg)
    {
        var bmp = new Bitmap(w, h);
        bmp.SetResolution(96, 96);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Call(form, "RenderCard", g);
        }
        return bmp;
    }

    /// <summary>Recompute 走的是 DateTime.Now；出图前把 status 换成固定时间的，图才是可复现的。</summary>
    static void Freeze(WidgetForm form, Settings s, AttendanceSnapshot snap)
    {
        var theme = (ThemeDef)Field(form, "theme");
        Set(form, "status", StatusBuilder.Build(s, snap, FixedNow, theme));
    }
}
