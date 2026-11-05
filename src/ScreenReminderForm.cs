using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            string styleId = fixedSched != null && !string.IsNullOrWhiteSpace(fixedSched.styleId)
                ? fixedSched.styleId : ReminderStyles.Follow;
            ReminderStyles.Resolve(styleId, theme, out a, out b, out ink);
            kind = WidgetForm.KindLabel(ev, Reminders.IsMeeting(ev), fixedSched == null ? null : fixedSched.name);
            bar = settings.ui.reminderSpreadStyle == "bar";

            fKind = UiFont.Make(12f * layout, FontStyle.Bold);
            fTitle = UiFont.Make(46f * layout, FontStyle.Bold);
            fCount = UiFont.Make(19f * layout);
            fWhen = UiFont.Make(28f * layout, FontStyle.Bold);
            fPlace = UiFont.Make(16f * layout);
            fDetail = UiFont.Make(14f * layout);
            fHint = UiFont.Make(11.5f * layout);
            FitTitle();

            timer = new Timer();
            timer.Interval = 33;
            timer.Tick += delegate { Tick(); };
            startAt = Environment.TickCount;
            holdMs = Math.Min(6000, Math.Max(3000, settings.ui.reminderHoldSeconds * 1000));
            timer.Start();
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
                if (elapsed < 1000) Invalidate();
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
            if (backdrop != null) g.DrawImageUnscaled(backdrop, 0, 0);
            else { using (var br = new SolidBrush(theme.bgBottom)) g.FillRectangle(br, ClientRectangle); }

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
