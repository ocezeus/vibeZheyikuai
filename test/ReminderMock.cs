using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WorkCountdown;

// 提醒动画改版的效果预览（只出图，不改 src）：
// 从进度条已走段的末端扩散成全屏纯色，然后下方第二块色块和上方标题同时擦出。
// 输出 test\visual\reminder-mock\v1..v3\f*.png，再用 tools\mock-gif.py 拼 GIF。
class ReminderMock
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    const int W = 1280, H = 720;
    const int CardW = 280;
    const int CardX = 940;
    const int BlockTop = 420;

    static int cardY, cardH, tipX, tipY;
    static Bitmap desktop, card;
    static Color A, B, Ink;
    static string kind = "会议提醒", title = "需求对齐会";
    static string when = "18:00–18:45", place = "3F · 会议室 A", detail = "带上排期和接口文档", countdown = "还有 13 分钟";
    static Font fKind, fTitle, fCount, fWhen, fPlace, fDetail, fHint;

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "reminder-mock");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        Prepare();

        Console.WriteLine("A=" + A + " B=" + B + " ink=" + Ink + " progress=" + ((tipX - CardX - 18) / 244.0).ToString("0.00"));
        Console.WriteLine("card=" + CardX + "," + cardY + " " + CardW + "x" + cardH + " tip=" + tipX + "," + tipY);

        for (int v = 1; v <= 3; v++)
        {
            string dir = Path.Combine(root, "v" + v);
            Directory.CreateDirectory(dir);
            for (int t = 0; t <= 3800; t += 50)
            {
                using (var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImageUnscaled(desktop, 0, 0);
                    DrawOverlay(g, v, t);
                    bmp.Save(Path.Combine(dir, "f" + t.ToString("0000") + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            Console.WriteLine("v" + v + " frames done");
        }

        DisposeFonts();
        desktop.Dispose();
        card.Dispose();
    }

    // ---------------- 素材 ----------------

    static void Prepare()
    {
        var store = new SettingsStore();
        var s = store.Load().Settings;
        s.ui.reducedMotion = false;

        DateTime start = DateTime.Today.AddHours(18);
        if (start <= DateTime.Now) start = DateTime.Now.AddMinutes(13);
        when = start.ToString("HH:mm") + "–" + start.AddMinutes(45).ToString("HH:mm");
        countdown = "还有 " + Math.Max(1, (int)Math.Ceiling((start - DateTime.Now).TotalMinutes)) + " 分钟";

        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);
        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            form.MinimumSize = Size.Empty;
            cardH = (int)form.GetType().GetProperty("ExpandedH", F).GetValue(form, null);
            form.Size = new Size(CardW, cardH);
            Call(form, "Recompute");
            var st = Get(form, "status");
            double progress = (double)st.GetType().GetProperty("progress").GetValue(st, null);
            st.GetType().GetProperty("progress").SetValue(st, Math.Max(0.55, Math.Min(0.85, progress)), null);
            progress = (double)st.GetType().GetProperty("progress").GetValue(st, null);

            cardY = 672 - 20 - cardH;
            tipX = CardX + 18 + (int)Math.Round(244 * progress);
            tipY = cardY + 102 + 2;

            card = new Bitmap(CardW, cardH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(card))
            {
                g.Clear(Color.Transparent);
                Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, CardW, cardH)));
            }
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }

        // 会议色 + 跟随当前主题，和 PaintReminder 的取色口径一致
        A = Color.FromArgb(244, 145, 53);
        B = Color.FromArgb(255, 205, 105);
        Ink = Color.FromArgb(52, 30, 15);
        ThemeDef theme = Themes.Get(s.ui.theme);
        ReminderStyles.FitToTheme(theme, ref A, ref B, ref Ink);

        fKind = UiFont.Make(12f, FontStyle.Bold);
        fTitle = UiFont.Make(46f, FontStyle.Bold);
        fCount = UiFont.Make(19f);
        fWhen = UiFont.Make(28f, FontStyle.Bold);
        fPlace = UiFont.Make(16f);
        fDetail = UiFont.Make(14f);
        fHint = UiFont.Make(11.5f);

        desktop = BuildDesktop();
    }

    static void DisposeFonts()
    {
        foreach (var f in new Font[] { fKind, fTitle, fCount, fWhen, fPlace, fDetail, fHint }) if (f != null) f.Dispose();
    }

    static Bitmap BuildDesktop()
    {
        var bmp = new Bitmap(W, H);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H),
                Color.FromArgb(32, 42, 60), Color.FromArgb(11, 15, 23), 90f))
                g.FillRectangle(bg, 0, 0, W, H);

            // 一点壁纸光晕
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-220, -300, 900, 900);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterColor = Color.FromArgb(46, 60, 88);
                    br.SurroundColors = new[] { Color.FromArgb(0, 46, 60, 88) };
                    br.FocusScales = new PointF(0.2f, 0.2f);
                    g.FillPath(br, path);
                }
            }
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(700, 260, 950, 780);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterColor = Color.FromArgb(32, 34, 54);
                    br.SurroundColors = new[] { Color.FromArgb(0, 32, 34, 54) };
                    br.FocusScales = new PointF(0.15f, 0.15f);
                    g.FillPath(br, path);
                }
            }

            // 一个低对比的编辑器窗口，说明这是盖住整个桌面的全屏层
            var win = new Rectangle(96, 74, 828, 500);
            using (var p = RoundRect(win, 14))
            using (var sh = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
            using (var body = new SolidBrush(Color.FromArgb(19, 26, 39)))
            using (var bar = new SolidBrush(Color.FromArgb(27, 36, 52)))
            using (var line = new SolidBrush(Color.FromArgb(35, 46, 66)))
            {
                g.FillPath(sh, RoundRect(new Rectangle(win.X, win.Y + 12, win.Width, win.Height), 14));
                g.FillPath(body, p);
                g.FillRectangle(bar, win.X, win.Y, win.Width, 38);
                g.FillRectangle(body, win.X, win.Y + 38, 150, win.Height - 38);
                for (int i = 0; i < 3; i++)
                    using (var dot = new SolidBrush(Color.FromArgb(90, 120, 150))) g.FillEllipse(dot, win.X + 16 + i * 18, win.Y + 15, 8, 8);
                int y = win.Y + 70;
                for (int i = 0; i < 9; i++)
                {
                    int ww = 180 + (i * 97) % 420;
                    g.FillRectangle(line, win.X + 180, y, ww, 10);
                    y += 30;
                }
            }

            // 任务栏
            using (var tb = new SolidBrush(Color.FromArgb(10, 14, 21))) g.FillRectangle(tb, 0, 672, W, 48);
            using (var sq = new SolidBrush(Color.FromArgb(38, 50, 70)))
            {
                g.FillRectangle(sq, 24, 684, 24, 24);
                for (int i = 0; i < 4; i++) g.FillRectangle(sq, 72 + i * 42, 684, 24, 24);
            }

            // 卡片阴影 + 卡片本体
            using (var sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillPath(sh, RoundRect(new Rectangle(CardX, cardY + 8, CardW, cardH), 16));
            g.DrawImageUnscaled(card, CardX, cardY);
        }
        return bmp;
    }

    // ---------------- 动画 ----------------

    static void DrawOverlay(Graphics g, int variant, int t)
    {
        if (variant == 1) V1(g, t);
        else if (variant == 2) V2(g, t);
        else V3(g, t);
    }

    // v1：从条子末端画圆扩散，标题从上往下擦出、下方色块从下往上擦出（同时）
    static void V1(Graphics g, int t)
    {
        double pFlood = Smooth(Clamp01((t - 100) / 500.0));
        double pReveal = EaseOutCubic(Clamp01((t - 600) / 320.0));
        double pOut = EaseInCubic(Clamp01((t - 3000) / 260.0));
        double pShrink = EaseInCubic(Clamp01((t - 3260) / 400.0));

        int maxR = (int)Math.Ceiling(Math.Sqrt(Math.Max(tipX, W - tipX) * Math.Max(tipX, W - tipX) + Math.Max(tipY, H - tipY) * Math.Max(tipY, H - tipY))) + 40;
        double r = maxR * pFlood * (1 - pShrink);
        if (r <= 0.5) return;

        double reveal = pReveal * (1 - pOut);
        int titleBottom = 260;
        Rectangle titleClip = new Rectangle(0, 0, W, (int)Math.Round(titleBottom * reveal));

        using (var circle = new GraphicsPath())
        {
            circle.AddEllipse((float)(tipX - r), (float)(tipY - r), (float)(r * 2), (float)(r * 2));
            g.SetClip(circle, CombineMode.Replace);
            g.FillRectangle(new SolidBrush(A), 0, 0, W, H);
            DrawClipped(g, titleClip, delegate (Graphics gg) { DrawTitleGroup(gg); });
            // 下方色块整块从屏幕下方拉上来，内容顺序才是「时间 → 地点 → 备注」
            int off = (int)Math.Round((1 - reveal) * (H - BlockTop + 8));
            var saved = g.Save();
            g.TranslateTransform(0, off);
            DrawLowerBlock(g);
            g.Restore(saved);
            g.ResetClip();
        }
    }

    // v2：条子先横向拉满、再纵向撑满；标题和下方色块共用一条从左往右的擦线
    static void V2(Graphics g, int t)
    {
        double pH = Smooth(Clamp01((t - 100) / 280.0));
        double pV = Smooth(Clamp01((t - 360) / 260.0));
        double pReveal = EaseOutCubic(Clamp01((t - 620) / 320.0));
        double pOut = EaseInCubic(Clamp01((t - 3000) / 260.0));
        double pCollapse = EaseInCubic(Clamp01((t - 3260) / 200.0));
        double pCollapseX = EaseInCubic(Clamp01((t - 3440) / 220.0));

        if (t < 100) return;
        int left = (int)Math.Round(tipX - pH * tipX);
        int right = (int)Math.Round(tipX + pH * (W - tipX));
        int top = (int)Math.Round(tipY - 3 - pV * (tipY - 3) + pCollapse * (tipY - 3));
        int bottom = (int)Math.Round(tipY + 3 + pV * (H - tipY - 3) - pCollapse * (H - tipY - 3));
        if (pCollapse > 0.99) { top = tipY - 3; bottom = tipY + 3; }
        left = (int)Math.Round(left + pCollapseX * (tipX - left));
        right = (int)Math.Round(right - pCollapseX * (right - tipX));
        if (right - left < 2 || bottom - top < 2) return;

        double reveal = pReveal * (1 - pOut);
        int wipeX = (int)Math.Round(W * reveal);
        var band = new Rectangle(left, top, right - left, bottom - top);
        g.SetClip(band, CombineMode.Replace);
        using (var br = new SolidBrush(A)) g.FillRectangle(br, band);
        DrawClipped(g, new Rectangle(0, 0, wipeX, H), delegate (Graphics gg) { DrawTitleGroup(gg); });
        DrawClipped(g, new Rectangle(0, BlockTop, wipeX, H - BlockTop), delegate (Graphics gg) { DrawLowerBlock(gg); });
        g.ResetClip();
    }

    // v3：带柔边的径向扩散；标题从下往上、色块从上往下，从中间往两边同时擦出
    static void V3(Graphics g, int t)
    {
        double pFlood = Smooth(Clamp01((t - 100) / 560.0));
        double pReveal = EaseOutCubic(Clamp01((t - 660) / 320.0));
        double pOut = EaseInCubic(Clamp01((t - 3060) / 260.0));
        double pShrink = EaseInCubic(Clamp01((t - 3320) / 400.0));

        int maxR = (int)Math.Ceiling(Math.Sqrt(Math.Max(tipX, W - tipX) * Math.Max(tipX, W - tipX) + Math.Max(tipY, H - tipY) * Math.Max(tipY, H - tipY))) + 40;
        double r = maxR * pFlood * (1 - pShrink);
        if (r <= 1) return;

        double reveal = pReveal * (1 - pOut);
        int titleSpan = 260 - 40;
        Rectangle titleClip = new Rectangle(0, 40 + (int)Math.Round(titleSpan * (1 - reveal)), W, (int)Math.Round(titleSpan * reveal));
        int blockSpan = H - BlockTop;
        Rectangle blockClip = new Rectangle(0, BlockTop, W, (int)Math.Round(blockSpan * reveal));

        using (var path = new GraphicsPath())
        {
            path.AddEllipse((float)(tipX - r), (float)(tipY - r), (float)(r * 2), (float)(r * 2));
            using (var br = new PathGradientBrush(path))
            {
                br.CenterPoint = new PointF(tipX, tipY);
                br.CenterColor = A;
                br.SurroundColors = new[] { Color.FromArgb(0, A) };
                br.FocusScales = new PointF(0.96f, 0.96f);
                g.FillPath(br, path);
            }
            // 文字只落在实心区里
            using (var core = new GraphicsPath())
            {
                core.AddEllipse((float)(tipX - r * 0.955), (float)(tipY - r * 0.955), (float)(r * 1.91), (float)(r * 1.91));
                g.SetClip(core, CombineMode.Replace);
                DrawClipped(g, titleClip, delegate (Graphics gg) { DrawTitleGroup(gg); });
                DrawClipped(g, blockClip, delegate (Graphics gg) { DrawLowerBlock(gg); });
                g.ResetClip();
            }
        }
    }

    static void DrawClipped(Graphics g, Rectangle clip, Action<Graphics> draw)
    {
        if (clip.Width <= 0 || clip.Height <= 0) return;
        var saved = g.Save();
        g.IntersectClip(clip);
        draw(g);
        g.Restore(saved);
    }

    // ---------------- 全屏排版 ----------------

    static void DrawTitleGroup(Graphics g)
    {
        Text(g, kind, fKind, WithAlpha(Ink, 0.62), 74, 56, 600, 32);
        Text(g, title, fTitle, Ink, 68, 88, 1040, 104);
        Text(g, countdown, fCount, WithAlpha(Ink, 0.78), 74, 202, 700, 44);
        DrawClose(g);
    }

    static void DrawLowerBlock(Graphics g)
    {
        using (var br = new SolidBrush(B)) g.FillRectangle(br, 0, BlockTop, W, H - BlockTop);
        using (var pen = new Pen(Color.FromArgb(28, Ink), 1f)) g.DrawLine(pen, 0, BlockTop, W, BlockTop);
        Text(g, when, fWhen, Ink, 72, 454, 700, 64);
        Text(g, place, fPlace, WithAlpha(Ink, 0.92), 74, 524, 700, 40);
        Text(g, detail, fDetail, WithAlpha(Ink, 0.70), 74, 564, 820, 36);
        Text(g, "今天", fHint, WithAlpha(Ink, 0.55), W - 74 - 120, 464, 120, 30, StringAlignment.Far);
        Text(g, "点任意处关闭", fHint, WithAlpha(Ink, 0.45), W - 74 - 180, 652, 180, 30, StringAlignment.Far);
    }

    static void DrawClose(Graphics g)
    {
        float x = W - 74, y = 58, d = 20;
        using (var pen = new Pen(WithAlpha(Ink, 0.62), 1.7f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawLine(pen, x, y, x + d, y + d);
            g.DrawLine(pen, x + d, y, x, y + d);
        }
    }

    // ---------------- 小工具 ----------------

    static void Text(Graphics g, string s, Font f, Color c, float x, float y, float w, float h,
        StringAlignment align = StringAlignment.Near)
    {
        using (var sf = new StringFormat
        {
            Alignment = align,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        })
        using (var br = new SolidBrush(c))
            g.DrawString(s, f, br, new RectangleF(x, y, w, h), sf);
    }

    static Color WithAlpha(Color c, double a)
    {
        return Color.FromArgb((int)Math.Round(255 * Clamp01(a)), c.R, c.G, c.B);
    }

    static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static double Clamp01(double v) { return Math.Max(0, Math.Min(1, v)); }
    static double EaseOutCubic(double t) { t = Clamp01(t); return 1 - Math.Pow(1 - t, 3); }
    static double EaseInCubic(double t) { t = Clamp01(t); return t * t * t; }
    static double Smooth(double t) { t = Clamp01(t); return t * t * (3 - 2 * t); }
}
