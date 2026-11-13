using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WorkCountdown
{
    /// <summary>
    /// 警戒带之后，从卡片「贴边反向」拉出来的抽屉卡（方案 A）。
    /// 实心工业黄 + 同色系三阶文字：标题暖黑、时间地点深褐、剩余时间深橙；68 DIP 高、外角 14。
    /// 和主卡片一样是分层窗（WS_EX_LAYERED + UpdateLayeredWindow），圆角靠像素 alpha 收，
    /// 不用 Region —— Region 剪出来的圆角是硬锯齿。
    /// 窗口有一部分压在卡片底下（OverlapDip），由主卡片盖住，接缝才是一条直线。
    /// </summary>
    public class DrawerForm : Form
    {
        // ── 设计稿（96 DPI 的 DIP），运行时统一乘 scale ──
        public const int WidthDip = 440;      // 含压在卡片底下的 24
        public const int HeightDip = 68;      // 和收起态小组件同高
        public const int OverlapDip = 24;     // 压在卡片底下、被卡片盖住的那段
        public const int GapDip = 12;         // 文字离卡片边
        public const int FarPadDip = 22;      // 文字离外端
        public const int RadiusDip = 14;
        public const int SlideMs = 550;       // 拉出（用户定的：展开 400 / 收起 400 / 抽屉 550）
        public const int CloseMs = 240;       // 收回
        public const float TitlePt = 12.75f;  // 17px
        public const float MetaPt = 9.75f;    // 13px
        public const float LineGapDip = 7f;   // 两行之间的行距

        // 方案 A 的三个色阶：都落在暖色系里，不用纯黑、不用半透明灰
        public static readonly Color TapeColor = Color.FromArgb(245, 196, 0);   // #F5C400
        public static readonly Color InkColor  = Color.FromArgb(0x33, 0x26, 0x0A); // #33260A 标题
        public static readonly Color Ink2Color = Color.FromArgb(0x5C, 0x44, 0x00); // #5C4400 时间/地点
        public static readonly Color Ink3Color = Color.FromArgb(0x8A, 0x3D, 0x00); // #8A3D00 剩余时间

        private const int GwlExStyle = -20;
        private const int WsExTopMost = 0x00000008;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const int UlwAlpha = 0x2;
        private const byte AcSrcAlpha = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc,
            int crKey, ref BlendFunction pblend, int dwFlags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private readonly Settings settings;
        private readonly ReminderEvent ev;
        private readonly float scale;
        private readonly bool instant;
        private readonly bool topMost;
        private readonly string title;
        private readonly string place;
        private readonly Action onDismiss;
        private Rectangle card;
        private bool toRight;

        private Font fTitle, fMeta, fMetaBold;
        private Bitmap frame;
        private readonly Timer animTimer;
        private int animStart;
        private double p;                 // 0 = 完全收在卡片底下，1 = 完全拉出
        private double pFrom, pTo;
        private int animMs;
        private bool closing;
        private bool done;
        private string lastCount;

        public DrawerForm(Settings settings, ReminderEvent ev, Rectangle card, bool toRight,
            float scale, bool topMost, Action onDismiss)
        {
            this.settings = settings;
            this.ev = ev;
            this.card = card;
            this.toRight = toRight;
            this.scale = scale <= 0 ? 1f : scale;
            this.topMost = topMost;
            this.onDismiss = onDismiss;
            instant = settings != null && settings.ui.reducedMotion;

            title = ev == null ? "" : (ev.title ?? "");
            place = ev == null ? "" : (ev.place ?? "");

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = TapeColor;
            DoubleBuffered = false;      // 分层窗不能开双缓冲，否则 OnPaint 不被调用、永远不出帧
            TopMost = topMost;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            // 和 HTML 预览同一个字族：苹方/雅黑混排会把标题画「轻」、副行也变宽，和设计稿对不上
            fTitle = DesignFont(TitlePt, FontStyle.Bold);
            fMeta = DesignFont(MetaPt, FontStyle.Regular);
            fMetaBold = DesignFont(MetaPt, FontStyle.Bold);

            animTimer = new Timer();
            animTimer.Interval = 15;
            animTimer.Tick += delegate { StepAnim(); };
            lastCount = Reminders.CountdownText(settings, ev, DateTime.Now);

            Bounds = AnchorBounds(S(WidthDip));
        }

        /// <summary>窗口压在卡片边上：往右拉时右移，往左拉时往左长。</summary>
        private Rectangle AnchorBounds(int widthPx)
        {
            int h = S(HeightDip);
            int x = toRight ? card.Right - S(OverlapDip) : card.Left + S(OverlapDip) - widthPx;
            return new Rectangle(x, card.Top, Math.Max(1, widthPx), h);
        }

        /// <summary>卡片位置/方向变了（换边、换屏）时重新贴一次。</summary>
        public void SetAnchor(Rectangle cardRect, bool drawerToRight)
        {
            card = cardRect;
            toRight = drawerToRight;
            if (p > 0) RenderAndPush();
        }

        private int S(float dip)
        {
            return (int)Math.Round(dip * scale);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | WsExLayered | WsExNoActivate;  // toolwindow + 分层 + 不抢焦点
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 画面全部走 UpdateLayeredWindow，这里只保证第一帧有东西
            if (frame != null && e.Graphics != null) e.Graphics.DrawImageUnscaled(frame, 0, 0);
        }

        /// <summary>拉出。instant（减少动效）时直接到位。</summary>
        public void BeginSlide()
        {
            p = 0;
            RenderAndPush();
            if (instant) { p = 1; RenderAndPush(); return; }
            StartAnim(0, 1, SlideMs, false);
        }

        /// <summary>点一下收回：先滑回去再关窗。</summary>
        public void BeginClose()
        {
            if (done) return;
            done = true;
            if (onDismiss != null) onDismiss();
            if (instant || !IsHandleCreated) { CloseNow(); return; }
            StartAnim(p, 0, CloseMs, true);
        }

        /// <summary>立刻关掉（提醒结束、卡片被点、设置改了）。</summary>
        public void CloseNow()
        {
            done = true;
            animTimer.Stop();
            if (!IsDisposed) Close();
        }

        private void StartAnim(double from, double to, int ms, bool close)
        {
            pFrom = from; pTo = to; animMs = Math.Max(1, ms); closing = close;
            animStart = Environment.TickCount;
            animTimer.Start();
        }

        private void StepAnim()
        {
            double t = Math.Min(1.0, (Environment.TickCount - animStart) / (double)animMs);
            // 和 HTML 预览同一条曲线：cubic-bezier(.22,.9,.28,1) —— 先窜出去，再慢慢落定
            double e = CssEase(t);
            p = pFrom + (pTo - pFrom) * e;
            RenderAndPush();
            if (t >= 1)
            {
                animTimer.Stop();
                if (closing) Close();
            }
        }

        /// <summary>CSS 的 cubic-bezier(.22, .9, .28, 1)：用二分求 x 对应的 y。</summary>
        private static double CssEase(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double lo = 0, hi = 1;
            for (int i = 0; i < 22; i++)
            {
                double mid = (lo + hi) * 0.5;
                if (BezierX(mid) < t) lo = mid; else hi = mid;
            }
            return BezierY((lo + hi) * 0.5);
        }

        private static double BezierX(double u)
        {
            double v = 1 - u;
            return 3 * v * v * u * 0.22 + 3 * v * u * u * 0.28 + u * u * u;
        }

        private static double BezierY(double u)
        {
            double v = 1 - u;
            return 3 * v * v * u * 0.90 + 3 * v * u * u * 1.00 + u * u * u;
        }

        /// <summary>抽屉卡用设计稿的字族（微软雅黑），和警戒带一致；没有就退回 UI 字体。</summary>
        private static Font DesignFont(float pt, FontStyle style)
        {
            try { return new Font("Microsoft YaHei", pt, style, GraphicsUnit.Point); }
            catch { return UiFont.Make(pt, style); }
        }

        /// <summary>剩余时间每分钟变一次（还有 10 分钟 → 正在进行），由主窗口的 tick 叫。</summary>
        public void RefreshText()
        {
            if (p <= 0) return;
            string now = Reminders.CountdownText(settings, ev, DateTime.Now);
            if (now == lastCount) return;
            lastCount = now;
            RenderAndPush();
        }

        private void RenderAndPush()
        {
            int w = (int)Math.Round(S(WidthDip) * p);
            if (w < 1) w = 1;
            BuildFrame(w);
            Push();
        }

        /// <summary>按当前宽度画一帧：外角圆角、黄底、两行文字；宽度不够时文字被硬裁（不提前露）。</summary>
        internal Bitmap BuildFrame(int widthPx)
        {
            int hpx = Math.Max(1, S(HeightDip));
            if (widthPx < 1) widthPx = 1;
            if (frame == null || frame.Width != widthPx || frame.Height != hpx)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(widthPx, hpx, PixelFormat.Format32bppArgb);
            }
            // 位图按屏幕 DPI 设分辨率：字号用 pt，换算成像素才和 DIP 几何对得上
            frame.SetResolution(96f * scale, 96f * scale);
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                if (widthPx < 3) return frame;      // 还没拉出来：整帧透明
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var path = RoundPath(new Rectangle(0, 0, widthPx, hpx), S(RadiusDip)))
                {
                    using (var brush = new SolidBrush(TapeColor)) g.FillPath(brush, path);
                    var saved = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    // 顶部 1px 高光 + 底部一层很淡的暗边（和预览里的 inset 对齐）
                    using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, scale)))
                        g.DrawLine(pen, 0, Math.Max(0.5f, scale * 0.5f), widthPx, Math.Max(0.5f, scale * 0.5f));
                    using (var pen = new Pen(Color.FromArgb(34, 92, 60, 0), Math.Max(1f, scale)))
                        g.DrawLine(pen, 0, hpx - Math.Max(0.5f, scale * 0.5f), widthPx, hpx - Math.Max(0.5f, scale * 0.5f));
                    DrawText(g, widthPx, hpx);
                    g.Clip = saved;
                }
            }
            return frame;
        }

        private void DrawText(Graphics g, int wpx, int hpx)
        {
            int x = toRight ? S(OverlapDip + GapDip) : S(FarPadDip);
            int maxW = S(WidthDip - OverlapDip - GapDip - FarPadDip);
            if (maxW < 8) maxW = 8;

            float titleH = fTitle.GetHeight(g);
            float metaH = fMeta.GetHeight(g);
            float gap = S(LineGapDip);
            float top = (hpx - (titleH + gap + metaH)) / 2f;

            // 用「基线 + 上升高度」定位，而不是矩形 + LineAlignment：
            // GDI+ 的矩形排版在 CJK 上会按行高裁切，差 0.04px 就整行不画（踩过）。
            DrawTitle(g, x, top + Ascent(fTitle, g), maxW);
            DrawMeta(g, x, top + titleH + gap + Ascent(fMeta, g), maxW);
        }

        /// <summary>从行顶到基线的距离（像素）。</summary>
        private static float Ascent(Font f, Graphics g)
        {
            var ff = f.FontFamily;
            return ff.GetCellAscent(f.Style) * f.Size * g.DpiY / 72f / ff.GetEmHeight(f.Style);
        }

        private void DrawTitle(Graphics g, float x, float baseline, int maxW)
        {
            if (string.IsNullOrEmpty(title)) return;
            string text = FitText(g, title, fTitle, maxW);
            using (var br = new SolidBrush(InkColor))
                g.DrawString(text, fTitle, br, x, baseline - Ascent(fTitle, g), StringFormat.GenericTypographic);
        }

        /// <summary>一行放不下就砍字加省略号（不依赖 GDI+ 的 Trimming，那条路对 CJK 不稳）。</summary>
        private static string FitText(Graphics g, string s, Font f, int maxW)
        {
            if (g.MeasureString(s, f, 100000, StringFormat.GenericTypographic).Width <= maxW) return s;
            for (int len = s.Length - 1; len >= 1; len--)
            {
                string cut = s.Substring(0, len) + "…";
                if (g.MeasureString(cut, f, 100000, StringFormat.GenericTypographic).Width <= maxW) return cut;
            }
            return "…";
        }

        /// <summary>第二行：时间段 · 地点 · 剩余时间。整行共用一条基线，字族不同也对得齐。</summary>
        private void DrawMeta(Graphics g, float x, float baseline, int maxW)
        {
            var segs = new List<MetaSeg>();
            string when = Reminders.TimeRange(ev);
            string count = Reminders.CountdownText(settings, ev, DateTime.Now);
            if (!string.IsNullOrEmpty(when)) segs.Add(MetaSeg.Text(when, fMeta, Ink2Color));
            if (!string.IsNullOrEmpty(place)) { segs.Add(MetaSeg.Dot(fMeta)); segs.Add(MetaSeg.Text(place, fMeta, Ink2Color)); }
            if (!string.IsNullOrEmpty(count)) { segs.Add(MetaSeg.Dot(fMeta)); segs.Add(MetaSeg.Text(count, fMetaBold, Ink3Color, true)); }
            segs = Fit(segs, g, maxW);

            float cx = x;
            float gap = S(9);
            foreach (var seg in segs)
            {
                if (seg.text.Length == 0) continue;
                using (var br = new SolidBrush(seg.color))
                    g.DrawString(seg.text, seg.font, br, cx, baseline - Ascent(seg.font, g), StringFormat.GenericTypographic);
                cx += g.MeasureString(seg.text, seg.font, 100000, StringFormat.GenericTypographic).Width + gap;
            }
        }

        /// <summary>整行装不下时，从后往前把普通文字段砍成省略号；加粗的「剩余时间」最后才动。</summary>
        private List<MetaSeg> Fit(List<MetaSeg> segs, Graphics g, int maxW)
        {
            float gap = S(9);
            if (MeasureSegs(g, segs, gap) <= maxW) return segs;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int idx = segs.Count - 1; idx >= 0; idx--)
                {
                    if (segs[idx].kind != MetaKind.Text) continue;
                    if (pass == 0 && segs[idx].bold) continue;      // 第一轮先砍普通段
                    if (pass == 1 && !segs[idx].bold) continue;     // 第二轮才轮到加粗段
                    string s = segs[idx].text;
                    for (int len = s.Length - 1; len >= 1; len--)
                    {
                        var trial = new List<MetaSeg>(segs);
                        trial[idx] = MetaSeg.Text(s.Substring(0, len) + "…", segs[idx].font, segs[idx].color, segs[idx].bold);
                        if (MeasureSegs(g, trial, gap) <= maxW) return trial;
                    }
                }
            }
            return segs;
        }

        private static float MeasureSegs(Graphics g, List<MetaSeg> segs, float gap)
        {
            float total = 0;
            for (int i = 0; i < segs.Count; i++)
                total += g.MeasureString(segs[i].text, segs[i].font, 100000, StringFormat.GenericTypographic).Width + gap;
            return total;
        }

        private enum MetaKind { Text, Dot }

        private struct MetaSeg
        {
            public readonly string text;
            public readonly Font font;
            public readonly Color color;
            public readonly bool bold;
            public readonly MetaKind kind;
            private MetaSeg(string t, Font f, Color c, bool b, MetaKind k)
            { text = t; font = f; color = c; bold = b; kind = k; }
            public static MetaSeg Text(string t, Font f, Color c, bool bold = false) { return new MetaSeg(t, f, c, bold, MetaKind.Text); }
            public static MetaSeg Dot(Font f) { return new MetaSeg("·", f, Color.FromArgb(115, Ink2Color), false, MetaKind.Dot); }
        }

        private static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>把整帧按像素 alpha 推到屏幕，位置跟着 current width 走。</summary>
        private void Push()
        {
            if (frame == null || !IsHandleCreated) return;
            int w = frame.Width, h = frame.Height;
            int x = toRight ? card.Right - S(OverlapDip) : card.Left + S(OverlapDip) - w;
            int y = card.Top;
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = frame.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                oldBitmap = SelectObject(memDc, hBitmap);
                var size = new NativeSize();
                size.cx = w; size.cy = h;
                var src = new NativePoint();
                var dst = new NativePoint();
                dst.X = x; dst.Y = y;
                var blend = new BlendFunction();
                blend.BlendOp = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = AcSrcAlpha;
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, UlwAlpha);
            }
            catch { }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>把抽屉压到卡片下面：两张都是置顶时，夹在同一条 z 序带里，卡片在上。</summary>
        public void OrderBelow(IntPtr cardHandle)
        {
            if (!IsHandleCreated || cardHandle == IntPtr.Zero) return;
            SetWindowPos(Handle, cardHandle, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) BeginClose();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            animTimer.Stop();
            if (fTitle != null) fTitle.Dispose();
            if (fMeta != null) fMeta.Dispose();
            if (fMetaBold != null) fMetaBold.Dispose();
            if (frame != null) { frame.Dispose(); frame = null; }
            base.OnFormClosed(e);
        }
    }
}
