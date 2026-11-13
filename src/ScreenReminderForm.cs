using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace WorkCountdown
{
    /// <summary>
    /// 全屏提醒：普通无边框窗口（不依赖分层窗），先把桌面截下来当底图，
    /// 再从进度条末端做柔边扩散，最后标题和下方色块同时擦出。
    /// </summary>
    public class ScreenReminderForm : Form
    {
        private readonly Settings settings;
        private readonly ReminderEvent ev;
        private readonly ThemeDef theme;
        private readonly Action onDone;
        private readonly Bitmap backdrop;
        private readonly Timer timer;
        private readonly int startAt;
        private readonly int holdMs;
        private readonly int tipX, tipY;
        private readonly Color a, b, ink;
        private readonly string kind;
        private readonly bool bar;
        private readonly float layout;
        private readonly Font fKind, fCount, fWhen, fPlace, fDetail, fHint;
        private Font fTitle;
        private bool exiting;
        private int exitAt;
        private bool done;

        // ── tape（警戒带 2.4）：整屏宽黄条、上下滚动斜纹、文字反向跑马灯、同步反色呼吸 ──
        private const float TapeHDip = 60f;         // 条高
        private const float TapeRailDip = 8f;       // 上下斜纹带高
        private const float TapeStripeDip = 14f;    // 单条黄纹的垂厚度
        private const float TapePeriodDip = 39.6f;  // 水平周期 = 2 * 14 * √2
        private const float TapeGapDip = 35f;       // 两条内容之间的空隙
        private const float TapeTriDip = 15f;       // 感叹三角
        private const double TapeBreathSec = 2.33;  // 呼吸一轮
        private const double TapeTextSpeed = 70.0;  // 文字速度 DIP/s（向左）
        private const double TapeStripeSpeed = 100.0; // 斜纹速度 DIP/s（向右平移）
        private readonly bool tape;
        private readonly Font fTapeTitle, fTapeKind;
        private readonly float tapeKindW, tapeTitleW, tapePairW;
        private Bitmap tapeMaskTile;

        internal static int TapeBandHeight(float layout) { return (int)Math.Round(TapeHDip * layout); }
        internal static int TapeRailHeight(float layout) { return (int)Math.Round(TapeRailDip * layout); }

        internal Rectangle TapeBandRect()
        {
            int h = TapeBandHeight(layout);
            return new Rectangle(0, (Height - h) / 2, Width, h);
        }

        public ScreenReminderForm(Settings settings, ReminderEvent ev, ThemeDef theme,
            Rectangle screen, Point tipScreen, Action onDone)
        {
            this.settings = settings;
            this.ev = ev;
            this.theme = theme;
            this.onDone = onDone;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = theme.bgBottom;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Bounds = screen;
            TopMost = true;

            backdrop = CaptureBg(screen);
            tipX = tipScreen.X - screen.Left;
            tipY = tipScreen.Y - screen.Top;
            layout = (float)Math.Min(screen.Width / 1280.0, screen.Height / 720.0);
            if (layout < 0.5f) layout = 0.5f;

            var fixedSched = FixedOf(ev);
            string styleId = Reminders.ColorIdOf(settings, ev, fixedSched);
            ReminderStyles.Resolve(styleId, theme, out a, out b, out ink);
            kind = WidgetForm.KindLabel(ev, Reminders.IsMeeting(ev), fixedSched == null ? null : fixedSched.name);
            ReminderStyleDef style = settings.StyleOf(Reminders.KindOf(ev));
            bar = style.shape == "bar";

            fKind = UiFont.Make(12f * layout, FontStyle.Bold);
            fTitle = UiFont.Make(46f * layout, FontStyle.Bold);
            fCount = UiFont.Make(19f * layout);
            fWhen = UiFont.Make(28f * layout, FontStyle.Bold);
            fPlace = UiFont.Make(16f * layout);
            fDetail = UiFont.Make(14f * layout);
            fHint = UiFont.Make(11.5f * layout);
            FitTitle();

            tape = style.shape == "tape";
            if (tape)
            {
                fTapeTitle = TapeFont(16.5f * layout, FontStyle.Bold);  // 22px
                fTapeKind = TapeFont(7.875f * layout, FontStyle.Bold);  // 10.5px

                using (var mb = new Bitmap(1, 1))
                using (var mg = Graphics.FromImage(mb))
                {
                    mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    tapeKindW = MeasureFor(mg, kind, fTapeKind);
                    tapeTitleW = MeasureFor(mg, ev.title ?? "", fTapeTitle);
                }
                tapePairW = (TapeTriDip + 11 + 15) * layout + tapeKindW
                    + (15 + 3 + 15) * layout + tapeTitleW + TapeGapDip * layout;
                if (tapePairW < 80) tapePairW = 80;
                BuildTapeTiles(TapeBandHeight(layout) - TapeRailHeight(layout) * 2);
            }

            timer = new Timer();
            // 警戒带一直在滚：8ms 一帧（配合 timeBeginPeriod(1)，60Hz 屏跑满 60，120Hz 屏能上 120）
            timer.Interval = tape ? 8 : 33;
            timer.Tick += delegate { Tick(); };
            startAt = Environment.TickCount;
            holdMs = Math.Min(6000, Math.Max(3000, settings.ui.reminderHoldSeconds * 1000));
            timer.Start();
        }

        /// <summary>和设计稿（浏览器里的 Microsoft YaHei）保持一致；没有就退回主题字体。</summary>
        private static Font TapeFont(float pt, FontStyle style)
        {
            try { return new Font("Microsoft YaHei", pt, style, GraphicsUnit.Point); }
            catch { return UiFont.Make(pt, style); }
        }

        // ───────────────────────── tape：设计 & 绘制 ─────────────────────────

        private void DrawTape(Graphics g)
        {
            double now = Environment.TickCount - startAt;
            double fade = exiting
                ? 1 - EaseInCubic(Clamp01((Environment.TickCount - exitAt) / 620.0))
                : EaseOutCubic(Clamp01(now / 320.0));
            if (fade <= 0.004) return;

            int h = TapeBandHeight(layout);
            int top = (Height - h) / 2;
            int rail = TapeRailHeight(layout);
            int textTop = top + rail;

            // 呼吸：1 = 实黄底黑字，0 = 全透明黄字
            double b = 0.5 + 0.5 * Math.Cos(2 * Math.PI * (now / 1000.0) / TapeBreathSec);
            b = Clamp01(b);

            using (var br = new SolidBrush(Color.FromArgb(Alpha(b * fade), 245, 196, 0)))
                g.FillRectangle(br, 0, top, Width, h);

            DrawTapeRails(g, top, h, rail, fade);
            DrawTapeText(g, textTop, fade, b);
        }

        /// <summary>
        /// 文字只画一遍：颜色在黑 → 黄之间插值，不再叠两层半透明字（叠层会出重影）。
        /// </summary>
        private void DrawTapeText(Graphics g, int top, double fade, double b)
        {
            if (tapeMaskTile == null || tapePairW <= 1) return;
            double t = 1 - b;
            int cr = (int)Math.Round(11 + (255 - 11) * t);
            int cg = (int)Math.Round(12 + (216 - 12) * t);
            int cb = (int)Math.Round(14 + (77 - 14) * t);
            var color = Color.FromArgb(cr, cg, cb);
            // 影子只在快透明时出现，中间相不参与，避免看起来像双影
            double shadow = Math.Max(0, 0.5 - b) / 0.5 * 0.5 * fade;

            float off = (float)(((Environment.TickCount - startAt) / 1000.0 * TapeTextSpeed * layout) % tapePairW);
            int step = Math.Max(1, (int)Math.Round(tapePairW));
            for (int x = (int)Math.Round(-off); x < Width; x += step)
            {
                if (shadow > 0.01)
                    DrawTinted(g, tapeMaskTile, x + Math.Max(1, (int)Math.Round(layout)), top, Color.FromArgb(11, 12, 14), shadow);
                DrawTinted(g, tapeMaskTile, x, top, color, fade);
            }
        }

        private static void DrawTinted(Graphics g, Bitmap tile, int x, int y, Color c, double a)
        {
            if (a <= 0.004) return;
            var cm = new ColorMatrix(new float[][]
            {
                new float[] { c.R / 255f, 0, 0, 0, 0 },
                new float[] { 0, c.G / 255f, 0, 0, 0 },
                new float[] { 0, 0, c.B / 255f, 0, 0 },
                new float[] { 0, 0, 0, (float)Clamp01(a), 0 },
                new float[] { 0, 0, 0, 0, 1 }
            });
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(tile, new Rectangle(x, y, tile.Width, tile.Height),
                    0, 0, tile.Width, tile.Height, GraphicsUnit.Pixel, ia);
            }
        }

        /// <summary>把「⚠ 类型 · 标题」预渲染成一张白色蒙版，运行时按颜色矩阵染色。</summary>
        private void BuildTapeTiles(int textH)
        {
            if (textH < 8 || tapePairW <= 1) return;
            int w = Math.Max(1, (int)Math.Ceiling(tapePairW));
            tapeMaskTile = new Bitmap(w, textH);
            using (var g = Graphics.FromImage(tapeMaskTile))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                DrawTapePair(g, 0, 0, textH, Color.White, 1.0);
            }
        }

        /// <summary>上下两条警示斜纹：整体水平向右平移，速度由 TapeStripeSpeed 决定。</summary>
        private void DrawTapeRails(Graphics g, int top, int h, int rail, double a)
        {
            float stripe = Math.Max(4f, TapeStripeDip * layout);
            float period = Math.Max(stripe * 2.828f, TapePeriodDip * layout);
            double speed = TapeStripeSpeed * layout;
            float off = (float)(((Environment.TickCount - startAt) / 1000.0 * speed) % period);
            var rects = new[] { new Rectangle(0, top, Width, rail), new Rectangle(0, top + h - rail, Width, rail) };
            foreach (var r in rects)
            {
                using (var br = new SolidBrush(Color.FromArgb(Alpha(a), 11, 12, 14)))
                    g.FillRectangle(br, r);
                var sv = g.Save();
                g.SetClip(r);
                using (var pen = new Pen(Color.FromArgb(Alpha(a), 245, 196, 0), stripe))
                {
                    pen.StartCap = LineCap.Flat;
                    pen.EndCap = LineCap.Flat;
                    float reach = r.Height + stripe;
                    for (float x = -reach - period; x < Width + reach + period; x += period)
                        g.DrawLine(pen, x + off, r.Bottom + stripe, x + off + reach + stripe, r.Top - stripe);
                }
                g.Restore(sv);
            }
        }

        private void DrawTapePair(Graphics g, float x, int top, int h, Color c, double a)
        {
            float tri = Math.Max(8f, TapeTriDip * layout);
            float cy = top + h / 2f;
            using (var br = new SolidBrush(Color.FromArgb(Alpha(a), c)))
            using (var pen = new Pen(Color.FromArgb(Alpha(a), c), Math.Max(1f, 1.6f * layout)))
            {
                pen.LineJoin = LineJoin.Round;
                float tx = x + tri / 2f;
                g.DrawPolygon(pen, new PointF[]
                {
                    new PointF(tx, cy - tri / 2f),
                    new PointF(x + tri, cy + tri / 2f),
                    new PointF(x, cy + tri / 2f)
                });
                g.DrawLine(pen, tx, cy - tri * 0.16f, tx, cy + tri * 0.10f);
                g.FillEllipse(br, tx - 1.2f * layout, cy + tri * 0.20f, 2.4f * layout, 2.4f * layout);

                float kx = x + tri + 11 * layout;
                g.DrawString(kind, fTapeKind, br, kx, cy - fTapeKind.GetHeight(g) / 2f);

                float dx = kx + tapeKindW + 15 * layout;
                g.FillEllipse(br, dx, cy - 1.5f * layout, 3 * layout, 3 * layout);

                float tx2 = dx + 3 * layout + 15 * layout;
                g.DrawString(ev.title ?? "", fTapeTitle, br, tx2, cy - fTapeTitle.GetHeight(g) / 2f);
            }
        }

        private static float MeasureFor(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return g.MeasureString(s, f, 1000000, StringFormat.GenericTypographic).Width;
        }

        private static int Alpha(double a)
        {
            return (int)Math.Round(255 * Clamp01(a));
        }

        private static Bitmap CaptureBg(Rectangle r)
        {
            try
            {
                var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
                return bmp;
            }
            catch { return null; }
        }

        private FixedSchedule FixedOf(ReminderEvent e)
        {
            if (e == null || e.id == null || e.source != "break" || settings.schedules == null) return null;
            string id = e.id;
            if (id.StartsWith("fix:", StringComparison.Ordinal)) id = id.Substring(4);
            int cut = id.IndexOf(":end", StringComparison.Ordinal);
            if (cut >= 0) id = id.Substring(0, cut);
            foreach (var f in settings.schedules)
                if (f != null && f.id == id) return f;
            return null;
        }

        private void FitTitle()
        {
            if (fTitle == null) return;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                float maxW = Width - 2 * (68f * layout);
                while (fTitle.Size > 22f * layout && g.MeasureString(ev.title, fTitle).Width > maxW)
                {
                    float next = fTitle.Size - 2f;
                    fTitle.Dispose();
                    fTitle = UiFont.Make(next, FontStyle.Bold);
                }
            }
        }

        private double T { get { return (Environment.TickCount - startAt) / 1000.0; } }

        private void Tick()
        {
            int elapsed = Environment.TickCount - startAt;
            if (!exiting)
            {
                // 入场 1s，停留「最短停留」，然后自动收回卡片；提醒本身继续常驻
                if (elapsed >= 1000 + holdMs) { BeginExit(); return; }
                // tape 一直在滚：停留期间也得按帧重画
                if (elapsed < 1000 || tape) Invalidate(tape ? TapeBandRect() : ClientRectangle);
                return;
            }
            if (Environment.TickCount - exitAt > 760) { Close(); return; }
            Invalidate();
        }

        public void BeginExit()
        {
            if (exiting) return;
            exiting = true;
            exitAt = Environment.TickCount;
            timer.Start();
            Invalidate();
        }

        public void CloseNow()
        {
            done = true;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            timer.Stop();
            timer.Dispose();
            fKind.Dispose(); fTitle.Dispose(); fCount.Dispose(); fWhen.Dispose();
            fPlace.Dispose(); fDetail.Dispose(); fHint.Dispose();
            if (tape)
            {
                fTapeTitle.Dispose(); fTapeKind.Dispose();
                if (tapeMaskTile != null) tapeMaskTile.Dispose();
            }
            if (backdrop != null) backdrop.Dispose();
            if (!done && onDone != null) onDone();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) BeginExit();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) BeginExit();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (backdrop != null)
            {
                var clip = g.ClipBounds;
                bool partial = tape && clip.Height < Height - 1;
                if (partial)
                {
                    // 只重画长条那一条：整屏 4K 位图每帧全刷太贵，120Hz 撑不住
                    var br = TapeBandRect();
                    g.DrawImage(backdrop, br, br.X, br.Y, br.Width, br.Height, GraphicsUnit.Pixel);
                }
                else g.DrawImageUnscaled(backdrop, 0, 0);
            }
            else { using (var br = new SolidBrush(theme.bgBottom)) g.FillRectangle(br, ClientRectangle); }

            if (tape) { DrawTape(g); return; }

            double t = exiting ? 1000 + 760 + (Environment.TickCount - exitAt) : Math.Min(T * 1000, 1000);
            double reveal = Reveal(t);
            double flood = Flood(t);
            bool full = flood >= 1;
            if (full)
            {
                using (var br = new SolidBrush(a)) g.FillRectangle(br, 0, 0, Width, Height);
            }
            else if (flood > 0)
            {
                if (bar) DrawBar(g);
                else DrawCircle(g);
            }
            if (reveal > 0) DrawContent(g, reveal);
        }

        private double Reveal(double t)
        {
            double start = bar ? 650 : 600;
            double p = EaseOutCubic(Clamp01((t - start) / 320.0));
            double o = exiting ? EaseInCubic(Clamp01((Environment.TickCount - exitAt) / 240.0)) : 0;
            return Math.Max(0, p * (1 - o));
        }

        /// <summary>0=还没开始，1=铺满；退场时从 1 收回去。</summary>
        private double Flood(double t)
        {
            double outP = exiting ? EaseInCubic(Clamp01((Environment.TickCount - exitAt - 260) / 420.0)) : 0;
            if (bar)
            {
                double p1 = Smooth(Clamp01((t - 100) / 280.0));
                double p2 = Smooth(Clamp01((t - 360) / 260.0));
                return Math.Min(p1, p2) * (1 - outP);
            }
            return Smooth(Clamp01((t - 100) / 500.0)) * (1 - outP);
        }

        private void DrawCircle(Graphics g)
        {
            double p = Flood(T * 1000);
            double maxR = 0;
            foreach (var pt in new[] { new Point(0, 0), new Point(Width, 0), new Point(0, Height), new Point(Width, Height) })
                maxR = Math.Max(maxR, Math.Sqrt((pt.X - tipX) * (pt.X - tipX) + (pt.Y - tipY) * (pt.Y - tipY)));
            double r = (maxR + 40 * layout) * p;
            if (r < 2) return;
            double feather = Math.Max(10 * layout, Math.Min(90 * layout, r * 0.07));
            float focus = (float)Math.Max(0.86, Math.Min(0.985, 1 - feather / r));
            using (var path = new GraphicsPath())
            {
                path.AddEllipse((float)(tipX - r), (float)(tipY - r), (float)(r * 2), (float)(r * 2));
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterPoint = new PointF(tipX, tipY);
                    br.CenterColor = a;
                    br.SurroundColors = new[] { Color.FromArgb(0, a) };
                    br.FocusScales = new PointF(focus, focus);
                    g.FillPath(br, path);
                }
            }
        }

        private void DrawBar(Graphics g)
        {
            double t = T * 1000;
            double p1 = Smooth(Clamp01((t - 100) / 280.0));
            double p2 = Smooth(Clamp01((t - 360) / 260.0));
            int left = (int)Math.Round(tipX - p1 * tipX);
            int right = (int)Math.Round(tipX + p1 * (Width - tipX));
            int top = (int)Math.Round(tipY - 3 - p2 * (tipY - 3));
            int bottom = (int)Math.Round(tipY + 3 + p2 * (Height - tipY - 3));
            double outP = exiting ? EaseInCubic(Clamp01((Environment.TickCount - exitAt - 260) / 420.0)) : 0;
            if (outP > 0)
            {
                left = (int)Math.Round(left + outP * (tipX - left));
                right = (int)Math.Round(right - outP * (right - tipX));
                top = (int)Math.Round(top + outP * (tipY - 3 - top));
                bottom = (int)Math.Round(bottom - outP * (bottom - (tipY + 3)));
            }
            if (right - left < 2 || bottom - top < 2) return;
            var band = new Rectangle(left, top, right - left, bottom - top);
            bool horizontal = p2 < 0.5;
            double f = Math.Min(40 * layout, (horizontal ? band.Width : band.Height) / 3.0);
            using (var br = new LinearGradientBrush(band, Color.Transparent, a, horizontal ? 0f : 90f))
            {
                var cb = new ColorBlend(4);
                float u = (float)(f / Math.Max(1.0, horizontal ? band.Width : band.Height));
                cb.Positions = new[] { 0f, u, 1f - u, 1f };
                cb.Colors = new[] { Color.FromArgb(0, a), a, a, Color.FromArgb(0, a) };
                br.InterpolationColors = cb;
                g.FillRectangle(br, band);
            }
        }

        private void DrawContent(Graphics g, double reveal)
        {
            int blockTop = (int)Math.Round(420 * layout);
            if (bar)
            {
                int wipeX = (int)Math.Round(Width * reveal);
                var sv = g.Save();
                g.IntersectClip(new Rectangle(0, 0, wipeX, Height));
                DrawTitle(g);
                DrawBlock(g, 0);
                g.Restore(sv);
            }
            else
            {
                int titleH = (int)Math.Round(260 * layout * reveal);
                if (titleH > 0)
                {
                    var sv = g.Save();
                    g.IntersectClip(new Rectangle(0, 0, Width, titleH));
                    DrawTitle(g);
                    g.Restore(sv);
                }
                int off = (int)Math.Round((1 - reveal) * (Height - blockTop + 8 * layout));
                DrawBlock(g, off);
            }
        }

        private void DrawTitle(Graphics g)
        {
            Txt(g, kind, fKind, WithAlpha(ink, 0.62), 74, 56, 600, 32);
            Txt(g, ev.title, fTitle, ink, 68, 88, Width / layout - 136, 104);
            string count = Reminders.CountdownText(settings, ev, DateTime.Now);
            if (!string.IsNullOrEmpty(count)) Txt(g, count, fCount, WithAlpha(ink, 0.78), 74, 202, 700, 44);
            float x = Width - 74 * layout, y = 58 * layout, d = 20 * layout;
            using (var pen = new Pen(WithAlpha(ink, 0.62), Math.Max(1.6f, 1.7f * layout)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, x, y, x + d, y + d);
                g.DrawLine(pen, x + d, y, x, y + d);
            }
        }

        private void DrawBlock(Graphics g, int off)
        {
            int top = (int)Math.Round(420 * layout) + off;
            using (var br = new SolidBrush(b)) g.FillRectangle(br, 0, top, Width, Height - top);
            using (var pen = new Pen(Color.FromArgb(28, ink), Math.Max(1f, layout))) g.DrawLine(pen, 0, top, Width, top);
            string when = Reminders.TimeRange(ev);
            if (!string.IsNullOrEmpty(when)) Txt(g, when, fWhen, ink, 72, 454, 700, 64);
            if (!string.IsNullOrEmpty(ev.place)) Txt(g, ev.place, fPlace, WithAlpha(ink, 0.92), 74, 524, 700, 40);
            if (!string.IsNullOrEmpty(ev.detail)) Txt(g, ev.detail, fDetail, WithAlpha(ink, 0.70), 74, 564, 820, 36);
            Txt(g, "今天", fHint, WithAlpha(ink, 0.55), Width / layout - 194, 464, 120, 30, StringAlignment.Far);
            Txt(g, "点任意处关闭", fHint, WithAlpha(ink, 0.45), Width / layout - 254, 652, 180, 30, StringAlignment.Far);
        }

        private void Txt(Graphics g, string s, Font f, Color c, float x, float y, float w, float h,
            StringAlignment align = StringAlignment.Near)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (var sf = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var br = new SolidBrush(c))
                g.DrawString(s, f, br, new RectangleF(x * layout, y * layout, w * layout, h * layout), sf);
        }

        private static Color WithAlpha(Color c, double a)
        {
            return Color.FromArgb((int)Math.Round(255 * Math.Max(0, Math.Min(1, a))), c.R, c.G, c.B);
        }

        private static double Clamp01(double v) { return Math.Max(0, Math.Min(1, v)); }
        private static double EaseOutCubic(double t) { t = Clamp01(t); return 1 - Math.Pow(1 - t, 3); }
        private static double EaseInCubic(double t) { t = Clamp01(t); return t * t * t; }
        private static double Smooth(double t) { t = Clamp01(t); return t * t * (3 - 2 * t); }
    }
}
