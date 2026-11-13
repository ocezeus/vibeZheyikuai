using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WorkCountdown;

// 第二版预览：两种样式（圆形柔光 / 进度条拉伸）× 两种范围（全屏 / 仅卡片），都从进度条已走段的末端扩散。
// 只出图，不改 src；输出 test\visual\reminder-mock2\。
class ReminderMock2
{
    static BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n, F).Invoke(o, a); }

    const int W = 1280, H = 720;
    const int CW = 280, CH = 188, SEAM = 64;
    const int CARDX = 940, CARDY = 464;
    const int BLOCKTOP = 420;
    const int CROPX = 840, CROPY = 390, CROPW = 400, CROPH = 300, ZOOM = 2;

    static Bitmap desktop, cardNormal, cardRemind;
    static Color A, B, Ink;
    static string kind = "会议提醒", title = "需求对齐会";
    static string when = "18:00–18:45", place = "3F · 会议室 A", detail = "带上排期和接口文档", countdown = "还有 13 分钟";
    static Font fKind, fTitle, fCount, fWhen, fPlace, fDetail, fHint;
    static int tipX, tipY, cardTipX, cardTipY;

    [STAThread]
    static void Main()
    {
        Environment.SetEnvironmentVariable("WC_NO_SAVE", "1");
        Application.EnableVisualStyles();
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "reminder-mock2");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        Prepare();
        Console.WriteLine("A=" + A + " B=" + B + " ink=" + Ink + " cardTip=" + cardTipX + "," + cardTipY + " tip=" + tipX + "," + tipY);

        RenderFull(root, "full-circle", false);
        RenderFull(root, "full-bar", true);
        RenderCard(root, "card-circle", false);
        RenderCard(root, "card-bar", true);

        DisposeFonts();
        desktop.Dispose(); cardNormal.Dispose(); cardRemind.Dispose();
        Console.WriteLine("done");
    }

    // ---------------- 素材 ----------------

    static void Prepare()
    {
        var store = new SettingsStore();
        var s = store.Load();
        s.ui.reducedMotion = false;

        DateTime start = DateTime.Today.AddHours(18);
        if (start <= DateTime.Now) start = DateTime.Now.AddMinutes(13);
        when = start.ToString("HH:mm") + "–" + start.AddMinutes(45).ToString("HH:mm");
        countdown = "还有 " + Math.Max(1, (int)Math.Ceiling((start - DateTime.Now).TotalMinutes)) + " 分钟";

        A = Color.FromArgb(244, 145, 53); B = Color.FromArgb(255, 205, 105); Ink = Color.FromArgb(52, 30, 15);
        ThemeDef theme = Themes.Get(s.ui.theme);
        ReminderStyles.FitToTheme(theme, ref A, ref B, ref Ink);

        var ev = new ReminderEvent();
        ev.title = title; ev.place = place; ev.detail = detail; ev.source = "lark";
        ev.start = Reminders.FormatStart(start);
        ev.end = Reminders.FormatStart(start.AddMinutes(45));

        var snap = new AttendanceSnapshot();
        snap.ok = true;
        snap.clockIn = DateTime.Today.AddHours(9).AddMinutes(2);

        using (var form = new WidgetForm(store, s))
        {
            Set(form, "scale", 1f);
            Set(form, "snapshot", snap);
            Set(form, "collapsed", false);
            form.MinimumSize = Size.Empty;
            form.Size = new Size(CW, CH);
            Call(form, "Recompute");
            var st = Get(form, "status");
            double progress = (double)st.GetType().GetProperty("progress").GetValue(st, null);
            progress = Math.Max(0.55, Math.Min(0.85, progress));
            st.GetType().GetProperty("progress").SetValue(st, progress, null);

            cardTipX = 18 + (int)Math.Round(244 * progress);
            cardTipY = 102 + 2;
            tipX = CARDX + cardTipX;
            tipY = CARDY + cardTipY;

            cardNormal = PaintCard(form);

            Set(form, "reminding", true);
            Set(form, "remindEvent", ev);
            Set(form, "remindAt", DateTime.Now.AddSeconds(-30));
            cardRemind = PaintCard(form);
            Call(form, "OnFormClosing", new FormClosingEventArgs(CloseReason.None, false));
        }

        fKind = UiFont.Make(12f, FontStyle.Bold);
        fTitle = UiFont.Make(46f, FontStyle.Bold);
        fCount = UiFont.Make(19f);
        fWhen = UiFont.Make(28f, FontStyle.Bold);
        fPlace = UiFont.Make(16f);
        fDetail = UiFont.Make(14f);
        fHint = UiFont.Make(11.5f);

        desktop = BuildDesktop();
        Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "reminder-mock2"));
        cardNormal.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "reminder-mock2", "card-normal.png"));
        cardRemind.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visual", "reminder-mock2", "card-remind.png"));
        if (Environment.GetEnvironmentVariable("WC_MOCK_CARDS") == "1") { Console.WriteLine("cards saved"); return; }
    }

    static Bitmap PaintCard(WidgetForm form)
    {
        var bmp = new Bitmap(CW, CH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, CW, CH)));
        }
        return bmp;
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
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H),
                Color.FromArgb(32, 42, 60), Color.FromArgb(11, 15, 23), 90f))
                g.FillRectangle(bg, 0, 0, W, H);

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

            var win = new Rectangle(96, 74, 828, 500);
            using (var body = new SolidBrush(Color.FromArgb(19, 26, 39)))
            using (var bar = new SolidBrush(Color.FromArgb(27, 36, 52)))
            using (var line = new SolidBrush(Color.FromArgb(35, 46, 66)))
            using (var p = RoundRect(win, 14))
            {
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

            using (var tb = new SolidBrush(Color.FromArgb(10, 14, 21))) g.FillRectangle(tb, 0, 672, W, 48);
            using (var sq = new SolidBrush(Color.FromArgb(38, 50, 70)))
            {
                g.FillRectangle(sq, 24, 684, 24, 24);
                for (int i = 0; i < 4; i++) g.FillRectangle(sq, 72 + i * 42, 684, 24, 24);
            }
        }
        return bmp;
    }

    // ---------------- 输出 ----------------

    static void RenderFull(string root, string name, bool bar)
    {
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        for (int t = 0; t <= 3800; t += 50)
        {
            using (var baseBmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(baseBmp))
                {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImageUnscaled(desktop, 0, 0);
                DrawCard(g, CARDX, CARDY, 1);
                }

                Bitmap result = baseBmp;
            bool blended = false;
            try
            {
                double reveal;
                if (bar)
                {
                    double p1 = Smooth(Clamp01((t - 100) / 280.0));
                    double p2 = Smooth(Clamp01((t - 360) / 260.0));
                    double col = EaseInCubic(Clamp01((t - 3260) / 200.0));
                    double colX = EaseInCubic(Clamp01((t - 3440) / 220.0));
                    int l = (int)Math.Round(tipX - p1 * tipX);
                    int rr = (int)Math.Round(tipX + p1 * (W - tipX));
                    int top = (int)Math.Round(tipY - 3 - p2 * (tipY - 3));
                    int bottom = (int)Math.Round(tipY + 3 + p2 * (H - tipY - 3));
                    if (t > 3260) { l = (int)Math.Round(l + colX * (tipX - l)); rr = (int)Math.Round(rr - colX * (rr - tipX)); }
                    if (col > 0) { top = (int)Math.Round(top + col * (tipY - 3 - top)); bottom = (int)Math.Round(bottom - col * (bottom - (tipY + 3))); }
                    reveal = Reveal(t, 650, 3000, 260);
                    if (t >= 100 && rr - l > 1 && bottom - top > 1)
                    {
                        double fl = l > 0 ? 40 : 0, fr = rr < W ? 40 : 0;
                        double ft = top > 0 ? Math.Min(40, (bottom - top) * 0.25) : 0;
                        double fb = bottom < H ? Math.Min(40, (bottom - top) * 0.25) : 0;
                        result = Blend(baseBmp, A, BandAlpha(l, top, rr, bottom, fl, fr, ft, fb));
                        blended = true;
                    }
                }
                else
                {
                    double p = Smooth(Clamp01((t - 100) / 500.0));
                    double outP = EaseInCubic(Clamp01((t - 3260) / 400.0));
                    double r = MaxR() * p * (1 - outP);
                    reveal = Reveal(t, 600, 3000, 260);
                    if (r > 0.5)
                    {
                        result = Blend(baseBmp, A, CircleAlpha(tipX, tipY, r, Math.Min(80, Math.Max(10, r * 0.07))));
                        blended = true;
                    }
                }

                if (reveal > 0)
                {
                    using (var g2 = Graphics.FromImage(result))
                    {
                        g2.SmoothingMode = SmoothingMode.AntiAlias;
                        g2.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g2.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        if (bar)
                        {
                            int wipeX = (int)Math.Round(W * reveal);
                            DrawClipped(g2, new Rectangle(0, 0, wipeX, H), gg => DrawTitleGroup(gg));
                            DrawClipped(g2, new Rectangle(0, BLOCKTOP, wipeX, H - BLOCKTOP), gg => DrawLowerBlock(gg));
                        }
                        else
                        {
                            int titleH = (int)Math.Round(260 * reveal);
                            DrawClipped(g2, new Rectangle(0, 0, W, titleH), gg => DrawTitleGroup(gg));
                            int off = (int)Math.Round((1 - reveal) * (H - BLOCKTOP + 8));
                            var sv = g2.Save();
                            g2.TranslateTransform(0, off);
                            DrawLowerBlock(g2);
                            g2.Restore(sv);
                        }
                    }
                }
                result.Save(Path.Combine(dir, "f" + t.ToString("0000") + ".png"), ImageFormat.Png);
            }
            finally
            {
                if (blended) result.Dispose();
            }
            }
        }
        Console.WriteLine(name + " done");
    }

    static void RenderCard(string root, string name, bool bar)
    {
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        for (int t = 0; t <= 3500; t += 50)
        {
            using (var frame = CardFrame(t, bar))
            using (var canvas = new Bitmap(CROPW * ZOOM, CROPH * ZOOM))
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(desktop, new Rectangle(0, 0, CROPW * ZOOM, CROPH * ZOOM),
                    new Rectangle(CROPX, CROPY, CROPW, CROPH), GraphicsUnit.Pixel);
                int x = (CARDX - CROPX) * ZOOM, y = (CARDY - CROPY) * ZOOM;
                DrawCardShadow(g, x, y, ZOOM);
                g.DrawImage(frame, new Rectangle(x, y, CW * ZOOM, CH * ZOOM));
                canvas.Save(Path.Combine(dir, "f" + t.ToString("0000") + ".png"), ImageFormat.Png);
            }
        }
        // 停留时的全屏截图，用来看“只在卡片内”的范围
        using (var frame = CardFrame(1500, bar))
        using (var bmp = new Bitmap(W, H))
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawImageUnscaled(desktop, 0, 0);
            DrawCardShadow(g, CARDX, CARDY, 1);
            g.DrawImageUnscaled(frame, CARDX, CARDY);
            bmp.Save(Path.Combine(root, name + "-full.png"), ImageFormat.Png);
        }
        Console.WriteLine(name + " done");
    }

    static Bitmap CardFrame(int t, bool bar)
    {
        double reveal;
        Bitmap frame;
        if (bar)
        {
            double p1 = Smooth(Clamp01((t - 100) / 200.0));
            double p2 = Smooth(Clamp01((t - 280) / 240.0));
            double col = EaseInCubic(Clamp01((t - 3140) / 160.0));
            double colX = EaseInCubic(Clamp01((t - 3280) / 220.0));
            int l = (int)Math.Round(cardTipX - p1 * cardTipX);
            int rr = (int)Math.Round(cardTipX + p1 * (CW - cardTipX));
            int top = (int)Math.Round(cardTipY - 3 - p2 * (cardTipY - 3));
            int bottom = (int)Math.Round(cardTipY + 3 + p2 * (CH - cardTipY - 3));
            if (t > 2900) { l = (int)Math.Round(l + colX * (cardTipX - l)); rr = (int)Math.Round(rr - colX * (rr - cardTipX)); }
            if (col > 0) { top = (int)Math.Round(top + col * (cardTipY - 3 - top)); bottom = (int)Math.Round(bottom - col * (bottom - (cardTipY + 3))); }
            reveal = Reveal(t, 520, 2900, 240);
            frame = (Bitmap)cardNormal.Clone();
            if (t >= 100 && rr - l > 1 && bottom - top > 1)
            {
                double fl = l > 0 ? 10 : 0, fr = rr < CW ? 10 : 0;
                double ft = top > 0 ? Math.Min(10, (bottom - top) * 0.25) : 0;
                double fb = bottom < CH ? Math.Min(10, (bottom - top) * 0.25) : 0;
                frame = Blend(frame, A, BandAlpha(l, top, rr, bottom, fl, fr, ft, fb));
            }
        }
        else
        {
            double p = Smooth(Clamp01((t - 100) / 420.0));
            double outP = EaseInCubic(Clamp01((t - 3140) / 360.0));
            double r = 300 * p * (1 - outP);
            reveal = Reveal(t, 520, 2900, 240);
            frame = (Bitmap)cardNormal.Clone();
            if (r > 0.5) frame = Blend(frame, A, CircleAlpha(cardTipX, cardTipY, r, Math.Min(14, Math.Max(7, r * 0.06))));
        }

        if (reveal > 0)
        {
            using (var g = Graphics.FromImage(frame))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (bar)
                {
                    int wipeX = (int)Math.Round(CW * reveal);
                    DrawClipped(g, new Rectangle(0, 0, wipeX, CH), gg => DrawCardRegions(gg, true, 0));
                }
                else
                {
                    int topH = (int)Math.Round(SEAM * reveal);
                    DrawClipped(g, new Rectangle(0, 0, CW, topH), gg => DrawCardRegions(gg, false, 0));
                    int off = (int)Math.Round((1 - reveal) * (CH - SEAM + 4));
                    DrawClipped(g, new Rectangle(0, 0, CW, CH), gg => DrawCardB(gg, off));
                }
            }
        }
        return frame;
    }

    static void DrawCardRegions(Graphics g, bool full, int off)
    {
        // 上面那块（含标题、关闭按钮）
        g.DrawImage(cardRemind, new Rectangle(0, 0, CW, SEAM), 0, 0, CW, SEAM, GraphicsUnit.Pixel);
        if (full) g.DrawImage(cardRemind, new Rectangle(0, SEAM, CW, CH - SEAM), 0, SEAM, CW, CH - SEAM, GraphicsUnit.Pixel);
    }

    static void DrawCardB(Graphics g, int off)
    {
        g.DrawImage(cardRemind, new Rectangle(0, SEAM + off, CW, CH - SEAM), 0, SEAM, CW, CH - SEAM, GraphicsUnit.Pixel);
    }

    // ---------------- 全屏排版 ----------------

    static void DrawTitleGroup(Graphics g)
    {
        Text(g, kind, fKind, WithAlpha(Ink, 0.62), 74, 56, 600, 32);
        Text(g, title, fTitle, Ink, 68, 88, 1040, 104);
        Text(g, countdown, fCount, WithAlpha(Ink, 0.78), 74, 202, 700, 44);
        float x = W - 74, y = 58, d = 20;
        using (var pen = new Pen(WithAlpha(Ink, 0.62), 1.7f))
        {
            pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
            g.DrawLine(pen, x, y, x + d, y + d);
            g.DrawLine(pen, x + d, y, x, y + d);
        }
    }

    static void DrawLowerBlock(Graphics g)
    {
        using (var br = new SolidBrush(B)) g.FillRectangle(br, 0, BLOCKTOP, W, H - BLOCKTOP);
        using (var pen = new Pen(Color.FromArgb(28, Ink), 1f)) g.DrawLine(pen, 0, BLOCKTOP, W, BLOCKTOP);
        Text(g, when, fWhen, Ink, 72, 454, 700, 64);
        Text(g, place, fPlace, WithAlpha(Ink, 0.92), 74, 524, 700, 40);
        Text(g, detail, fDetail, WithAlpha(Ink, 0.70), 74, 564, 820, 36);
        Text(g, "今天", fHint, WithAlpha(Ink, 0.55), W - 74 - 120, 464, 120, 30, StringAlignment.Far);
        Text(g, "点任意处关闭", fHint, WithAlpha(Ink, 0.45), W - 74 - 180, 652, 180, 30, StringAlignment.Far);
    }

    // ---------------- 卡片绘制 ----------------

    static void DrawCard(Graphics g, int x, int y, int scale)
    {
        DrawCardShadow(g, x, y, scale);
        g.DrawImage(cardNormal, new Rectangle(x, y, CW * scale, CH * scale));
    }

    static void DrawCardShadow(Graphics g, int x, int y, int scale)
    {
        using (var sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            g.FillPath(sh, RoundRect(new Rectangle(x, y + 6 * scale, CW * scale, CH * scale), 16 * scale));
        using (var sh = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
            g.FillPath(sh, RoundRect(new Rectangle(x - 2 * scale, y + 2 * scale, CW * scale + 4 * scale, CH * scale), 16 * scale));
    }

    // ---------------- 柔边扩散 ----------------

    delegate double AlphaAt(int x, int y);

    static Bitmap Blend(Bitmap src, Color color, AlphaAt alpha)
    {
        var dst = (Bitmap)src.Clone();
        int w = dst.Width, h = dst.Height;
        var rect = new Rectangle(0, 0, w, h);
        var bits = dst.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var data = new int[w * h];
        Marshal.Copy(bits.Scan0, data, 0, data.Length);
        int cr = color.R, cg = color.G, cb = color.B;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                double a = alpha(x, y);
                if (a <= 0.004) continue;
                int i = row + x;
                int p = data[i];
                int ba = (p >> 24) & 255;
                if (ba == 0) continue;
                double eff = a * (ba / 255.0);
                int br = (p >> 16) & 255, bg = (p >> 8) & 255, bb = p & 255;
                int rr = (int)(br * (1 - eff) + cr * eff);
                int gg = (int)(bg * (1 - eff) + cg * eff);
                int bl = (int)(bb * (1 - eff) + cb * eff);
                data[i] = (ba << 24) | (rr << 16) | (gg << 8) | bl;
            }
        }
        Marshal.Copy(data, 0, bits.Scan0, data.Length);
        dst.UnlockBits(bits);
        return dst;
    }

    static AlphaAt CircleAlpha(double cx, double cy, double r, double feather)
    {
        return (x, y) =>
        {
            double dx = x - cx, dy = y - cy;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= r - feather) return 1;
            if (d >= r) return 0;
            double u = (r - d) / feather;
            return u * u * (3 - 2 * u);
        };
    }

    static AlphaAt BandAlpha(int left, int top, int right, int bottom,
        double fl, double fr, double ft, double fb)
    {
        return (x, y) =>
        {
            if (x < left || x >= right || y < top || y >= bottom) return 0;
            double a = 1;
            if (fl > 0) a = Math.Min(a, Smooth((x - left) / fl));
            if (fr > 0) a = Math.Min(a, Smooth((right - x) / fr));
            if (ft > 0) a = Math.Min(a, Smooth((y - top) / ft));
            if (fb > 0) a = Math.Min(a, Smooth((bottom - y) / fb));
            return a;
        };
    }

    static double Reveal(int t, int inStart, int exitStart, int exitMs)
    {
        double p = EaseOutCubic(Clamp01((t - inStart) / 320.0));
        double outP = EaseInCubic(Clamp01((t - exitStart) / (double)exitMs));
        return Math.Max(0, p * (1 - outP));
    }

    // ---------------- 小工具 ----------------

    static void DrawClipped(Graphics g, Rectangle clip, Action<Graphics> draw)
    {
        if (clip.Width <= 0 || clip.Height <= 0) return;
        var sv = g.Save();
        g.IntersectClip(clip);
        draw(g);
        g.Restore(sv);
    }

    static void Text(Graphics g, string s, Font f, Color c, float x, float y, float w, float h,
        StringAlignment align = StringAlignment.Near)
    {
        using (var sf = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        using (var br = new SolidBrush(c))
            g.DrawString(s, f, br, new RectangleF(x, y, w, h), sf);
    }

    static Color WithAlpha(Color c, double a) { return Color.FromArgb((int)Math.Round(255 * Clamp01(a)), c.R, c.G, c.B); }

    static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static int MaxR()
    {
        return (int)Math.Ceiling(Math.Sqrt(Math.Max(tipX, W - tipX) * Math.Max(tipX, W - tipX) + Math.Max(tipY, H - tipY) * Math.Max(tipY, H - tipY))) + 40;
    }

    static double Clamp01(double v) { return Math.Max(0, Math.Min(1, v)); }
    static double EaseOutCubic(double t) { t = Clamp01(t); return 1 - Math.Pow(1 - t, 3); }
    static double EaseInCubic(double t) { t = Clamp01(t); return t * t * t; }
    static double Smooth(double t) { t = Clamp01(t); return t * t * (3 - 2 * t); }
}
