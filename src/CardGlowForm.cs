using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WorkCountdown
{
    /// <summary>
    /// 卡片背面的流光（对齐 design/card-glow-3.html 第 3 列「呼吸 + 扫边」）。
    /// 两层：一层跟主题强调色走的呼吸柔光（HTML 的 blur(22px) .halo + 卡片 box-shadow 0 0 22px 45%），
    /// 一层是沿圆角边框跑的亮光（HTML 把旋转 conic-gradient 裁在卡片轮廓上，只露出贴边的一圈）。
    /// 独立分层窗，垫在卡片下面，四边各留 PadDip 给光晕；整窗 WS_EX_TRANSPARENT，点击全部穿透。
    /// </summary>
    public class CardGlowForm : Form
    {
        public const int PadDip = 56;

        private const int SweepMs = 5500;
        private const int BreatheMs = 4200;
        private const int FrameMs = 33;

        // 扫边 = conic-gradient(from 0deg, transparent 0 72%, accent 82%, #fff 85%, accent 88%, transparent 96%)
        private const double SweepFrom = 0.72;
        private const double SweepHotIn = 0.82;
        private const double SweepWhite = 0.85;
        private const double SweepHotOut = 0.88;
        private const double SweepTo = 0.96;
        private const double SweepPeak = 0.90;
        private const double SweepBandDip = 3.0;     // 亮边总宽：卡片本体盖住里半边

        // 柔光 = .halo（inset -12 / radius 24 / blur(22px) / opacity .34–.62）+ box-shadow 0 0 22px 45%
        private const double HaloOutsetDip = 12;
        private const double HaloRadiusDip = 24;
        private const double HaloSigmaDip = 18;
        private const double HaloMin = 0.34;
        private const double HaloMax = 0.62;
        private const double EdgeSigmaDip = 10;
        private const double EdgeAlpha = 0.45;

        // 柔光图按 1/2 尺寸模糊再放大：3 次盒模糊 ≈ 一次高斯
        private const int BlurDiv = 2;
        private const int BlurPasses = 3;
        // 扫边贴图按 1/2 尺寸逐像素算色：conic 只跟角度有关，分辨率够用，放大后反而更顺
        private const int ConicDiv = 2;

        private const int GwlExStyle = -20;
        private const int WsExTopMost = 0x00000008;
        private const int WsExLayered = 0x00080000;
        private const int WsExTransparent = 0x00000020;
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

        /// <summary>测试探针用：把时钟钉在 FixedClock 上，同一帧可以反复出图。</summary>
        internal static bool Frozen = false;
        internal static int FixedClock = 0;
        /// <summary>测试探针用：把柔光透明度钉死（&lt; 0 表示按呼吸曲线走）。</summary>
        internal static double HaloAlpha = -1;

        private readonly WidgetForm owner;
        private readonly float scale;
        private readonly int pad;
        private readonly Timer timer;
        private ThemeDef theme;
        private Rectangle cardRect;
        private Bitmap frame;
        private Bitmap baseFrame;
        private string baseKey;
        private int baseBucket = -1;
        private Bitmap haloWide;
        private Bitmap haloEdge;
        private string haloKey;
        private Bitmap conic;
        private int startAt;
        private bool light;
        /// <summary>皮肤指定的卡外光颜色（null = 用主题 accent，也就是默认皮肤的行为）。</summary>
        private Color? glowOverride;

        public CardGlowForm(WidgetForm owner, ThemeDef theme, float scale)
        {
            this.owner = owner;
            this.theme = theme;
            this.scale = scale <= 0 ? 1f : scale;
            pad = Math.Max(8, Ipx(PadDip));
            light = theme != null && Luminance(theme.bgTop) > 0.56;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = false;
            TopMost = owner != null && owner.TopMost;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            timer = new Timer();
            timer.Interval = FrameMs;
            timer.Tick += delegate { Step(); };
            startAt = Now;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | WsExLayered | WsExTransparent | WsExNoActivate;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private static int Now { get { return Frozen ? FixedClock : Environment.TickCount; } }

        private int Ipx(double dip) { return (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero); }

        public void SetTheme(ThemeDef t)
        {
            if (t == null) return;
            theme = t;
            light = Luminance(t.bgTop) > 0.56;
            // 颜色烘在位图里，换主题必须重做
            haloKey = null;
            baseKey = null;
            baseBucket = -1;
            DisposeConic();
            Invalidate();
        }

        /// <summary>
        /// 卡外光的颜色：颜色烘在位图里，变了才重做。传进来的就是 theme.accent。
        /// </summary>
        public void SetGlow(Color? c)
        {
            bool same = glowOverride.HasValue == c.HasValue
                && (!c.HasValue || glowOverride.Value.ToArgb() == c.Value.ToArgb());
            if (same) return;
            glowOverride = c;
            haloKey = null;
            baseKey = null;
            baseBucket = -1;
            DisposeConic();
            Invalidate();
        }

        /// <summary>开始跟随卡片；窗口和位置由 Step 每帧自己对齐。</summary>
        public void Begin()
        {
            if (!Visible) Show();
            OrderBelow();
            timer.Start();
            startAt = Now;
        }

        public void CloseNow()
        {
            timer.Stop();
            if (!IsDisposed) Close();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Render();
            if (e.Graphics != null && frame != null) e.Graphics.DrawImageUnscaled(frame, 0, 0);
            Push();
        }

        private void Step()
        {
            if (owner == null || owner.IsDisposed) { CloseNow(); return; }
            if (!owner.Visible)
            {
                if (Visible) Hide();
                return;
            }
            if (!Visible) Show();
            Follow();
            Invalidate();
        }

        /// <summary>把窗口贴到卡片四边各留 pad 的位置，并压到卡片下面。</summary>
        private void Follow()
        {
            Rectangle card = owner.Bounds;
            if (card == cardRect && Visible) return;
            cardRect = card;
            int w = card.Width + pad * 2;
            int h = card.Height + pad * 2;
            if (Width != w || Height != h) { haloKey = null; }
            Bounds = new Rectangle(card.Left - pad, card.Top - pad, w, h);
            if (TopMost != owner.TopMost) TopMost = owner.TopMost;
            OrderBelow();
        }

        /// <summary>压到卡片下面：两张都在同一 z 序带时，卡片盖住光晕的内半边。</summary>
        private void OrderBelow()
        {
            if (owner == null || !owner.IsHandleCreated || !IsHandleCreated) return;
            SetWindowPos(Handle, owner.Handle, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        private static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        private Color Accent
        {
            get
            {
                if (glowOverride.HasValue) return glowOverride.Value;
                return theme == null ? Color.White : theme.accent;
            }
        }

        /// <summary>浅色主题把光压暗一档，不然白底上会糊成一片。</summary>
        private double Strength { get { return light ? 0.55 : 1.0; } }

        /// <summary>卡片圆角跟着卡片本体走（收起 14 / 展开 18 DIP）。</summary>
        private int CardRadius
        {
            get
            {
                int r = owner != null && !owner.IsDisposed ? owner.CardRadiusPx : Ipx(14);
                return Math.Max(2, r);
            }
        }

        // ---------------- 绘制 ----------------

        private void Render()
        {
            if (Width <= 0 || Height <= 0) return;
            if (frame == null || frame.Width != Width || frame.Height != Height)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
                if (baseFrame != null) { baseFrame.Dispose(); baseFrame = null; }
            }
            frame.SetResolution(96f * scale, 96f * scale);

            double t = (Now - startAt) / 1000.0;
            double breath = HaloAlpha >= 0
                ? HaloAlpha
                : HaloMin + (HaloMax - HaloMin) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t / (BreatheMs / 1000.0)));

            // 柔光一秒钟只变二十来次：按呼吸档位烘一张底图，每帧只贴一次再画扫边
            int bucket = (int)Math.Round(breath * 63.0);
            string key = cardRect.Width + "x" + cardRect.Height + "|" + Accent.ToArgb() + "|"
                + (light ? "L" : "D") + "|" + (HaloAlpha >= 0 ? "f" : "-");
            if (baseFrame == null || baseKey != key || baseBucket != bucket) BuildBase(key, bucket, breath);
            if (baseFrame == null) return;

            using (var g = Graphics.FromImage(frame))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(baseFrame, 0, 0);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                DrawSweep(g, t);
            }
        }

        private void BuildBase(string key, int bucket, double breath)
        {
            if (baseFrame == null || baseFrame.Width != Width || baseFrame.Height != Height)
            {
                if (baseFrame != null) baseFrame.Dispose();
                baseFrame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }
            using (var g = Graphics.FromImage(baseFrame))
            {
                g.Clear(Color.Transparent);
                DrawHalo(g, breath);
            }
            baseKey = key;
            baseBucket = bucket;
        }

        /// <summary>呼吸柔光：光晕图一次生成好，按呼吸曲线改透明度铺上去。</summary>
        private void DrawHalo(Graphics g, double breath)
        {
            if (cardRect.Width <= 0 || cardRect.Height <= 0) return;
            string key = cardRect.Width + "x" + cardRect.Height + "|" + Accent.ToArgb() + "|" + (light ? "L" : "D");
            if (haloWide == null || haloKey != key) { BuildHalos(); haloKey = key; }
            if (haloWide == null) return;

            DrawLayer(g, haloWide, breath * Strength);
            DrawLayer(g, haloEdge, EdgeAlpha * Strength);
        }

        private void DrawLayer(Graphics g, Bitmap layer, double alpha)
        {
            if (layer == null || alpha <= 0.002) return;
            InterpolationMode old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix();
                cm.Matrix33 = (float)Math.Min(1.0, alpha);
                ia.SetColorMatrix(cm);
                g.DrawImage(layer, new Rectangle(0, 0, Width, Height), 0, 0, layer.Width, layer.Height, GraphicsUnit.Pixel, ia);
            }
            g.InterpolationMode = old;
        }

        private void BuildHalos()
        {
            if (haloWide != null) { haloWide.Dispose(); haloWide = null; }
            if (haloEdge != null) { haloEdge.Dispose(); haloEdge = null; }
            if (cardRect.Width <= 0 || cardRect.Height <= 0) return;

            int outset = Math.Max(2, Ipx(HaloOutsetDip));
            var wide = new Rectangle(pad - outset, pad - outset, cardRect.Width + outset * 2, cardRect.Height + outset * 2);
            haloWide = BuildGlow(wide, Ipx(HaloRadiusDip), HaloSigmaDip);

            var card = new Rectangle(pad, pad, cardRect.Width, cardRect.Height);
            haloEdge = BuildGlow(card, CardRadius, EdgeSigmaDip);
        }

        /// <summary>把一块圆角矩形按高斯模糊（3 次盒模糊）做成一张整窗大小的柔光图。</summary>
        private Bitmap BuildGlow(Rectangle rect, int radius, double sigmaDip)
        {
            int sw = Math.Max(8, Width / BlurDiv);
            int sh = Math.Max(8, Height / BlurDiv);
            byte[] mask;
            using (var small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    var r = new Rectangle(
                        (int)Math.Round(rect.X / (double)BlurDiv), (int)Math.Round(rect.Y / (double)BlurDiv),
                        Math.Max(4, (int)Math.Round(rect.Width / (double)BlurDiv)),
                        Math.Max(4, (int)Math.Round(rect.Height / (double)BlurDiv)));
                    int rad = Math.Max(2, (int)Math.Round(radius / (double)BlurDiv));
                    using (var path = RoundPath(r, rad))
                    using (var brush = new SolidBrush(Color.White))
                        g.FillPath(brush, path);
                }
                mask = ReadAlpha(small);
            }

            // 3 次盒模糊的方差 = r(r+1)，反解出半径
            double sigma = Math.Max(0.7, sigmaDip * scale / BlurDiv);
            int box = Math.Max(1, (int)Math.Round((-1.0 + Math.Sqrt(1.0 + 4.0 * sigma * sigma)) / 2.0));
            for (int i = 0; i < BlurPasses; i++) BoxBlur(mask, sw, sh, box);

            Color accent = Accent;
            var src = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb);
            BitmapData data = src.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var buf = new byte[data.Stride * sh];
                for (int y = 0; y < sh; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < sw; x++)
                    {
                        int a = mask[y * sw + x];
                        if (a == 0) continue;
                        int o = row + x * 4;
                        // 预乘 alpha：BGRA
                        buf[o + 0] = (byte)(accent.B * a / 255);
                        buf[o + 1] = (byte)(accent.G * a / 255);
                        buf[o + 2] = (byte)(accent.R * a / 255);
                        buf[o + 3] = (byte)a;
                    }
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally { src.UnlockBits(data); }

            var full = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(full))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, Width, Height), 0, 0, sw, sh, GraphicsUnit.Pixel);
            }
            src.Dispose();
            return full;
        }

        private static byte[] ReadAlpha(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var bytes = new byte[w * h];
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var line = new byte[data.Stride];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy((IntPtr)(data.Scan0.ToInt64() + (long)y * data.Stride), line, 0, data.Stride);
                    for (int x = 0; x < w; x++) bytes[y * w + x] = line[x * 4 + 3];
                }
            }
            finally { bmp.UnlockBits(data); }
            return bytes;
        }

        private static int ClampIndex(int v, int max) { return v < 0 ? 0 : (v > max ? max : v); }

        /// <summary>单通道盒模糊（横一遍、竖一遍），边缘按端点复制。</summary>
        private static void BoxBlur(byte[] src, int w, int h, int r)
        {
            if (r <= 0) return;
            var tmp = new byte[src.Length];
            int win = 2 * r + 1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                int sum = 0;
                for (int i = -r; i <= r; i++) sum += src[row + ClampIndex(i, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = (byte)(sum / win);
                    sum -= src[row + ClampIndex(x - r, w - 1)];
                    sum += src[row + ClampIndex(x + r + 1, w - 1)];
                }
            }
            for (int x = 0; x < w; x++)
            {
                int sum = 0;
                for (int i = -r; i <= r; i++) sum += tmp[ClampIndex(i, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    src[y * w + x] = (byte)(sum / win);
                    sum -= tmp[ClampIndex(y - r, h - 1) * w + x];
                    sum += tmp[ClampIndex(y + r + 1, h - 1) * w + x];
                }
            }
        }

        /// <summary>沿圆角边框跑的一小段亮光：conic-gradient 按角度取色，整张图跟时间一起转。</summary>
        private void DrawSweep(Graphics g, double t)
        {
            if (cardRect.Width <= 0 || cardRect.Height <= 0) return;
            int band = SweepBand;
            // 贴图是正方形、按对角线留了余量：只要还盖得住当前轮廓就别重做
            // （展开动画里卡片是一帧一帧长出来的，每帧重烘一张太费）
            double need = Math.Sqrt((double)cardRect.Width * cardRect.Width + (double)cardRect.Height * cardRect.Height) + band;
            if (conic == null || need > conic.Width * 2) RebuildConic();
            if (conic == null) return;

            var card = new Rectangle(pad, pad, cardRect.Width, cardRect.Height);
            using (var ring = RingPath(card, CardRadius, band))
            {
                GraphicsState state = g.Save();
                g.SetClip(ring, CombineMode.Replace);
                double phase = (t % (SweepMs / 1000.0)) / (SweepMs / 1000.0);
                g.TranslateTransform(card.X + card.Width / 2f, card.Y + card.Height / 2f);
                g.RotateTransform((float)(phase * 360.0));
                InterpolationMode old = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(conic, new Rectangle(-conic.Width, -conic.Height, conic.Width * 2, conic.Height * 2),
                    0, 0, conic.Width, conic.Height, GraphicsUnit.Pixel);
                g.InterpolationMode = old;
                g.Restore(state);
            }
        }

        private int SweepBand { get { return Math.Max(4, Ipx(SweepBandDip)); } }

        private void DisposeConic()
        {
            if (conic != null) { conic.Dispose(); conic = null; }
        }

        /// <summary>把 conic-gradient 逐角烘成一张位图（比卡片对角线大一圈，缩到 1/2 存）。</summary>
        private void RebuildConic()
        {
            DisposeConic();
            int w = cardRect.Width, h = cardRect.Height;
            if (w <= 0 || h <= 0) return;
            int band = SweepBand;
            int size = (int)Math.Ceiling(Math.Sqrt((double)w * w + (double)h * h)) + band * 4 + 8;
            if (size > 1400) size = 1400;
            int sw = Math.Max(16, size / ConicDiv);

            var bmp = new Bitmap(sw, sw, PixelFormat.Format32bppPArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, sw, sw), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var buf = new byte[data.Stride * sw];
                double c = sw / 2.0;
                double inv = 1.0 / (2 * Math.PI);
                for (int y = 0; y < sw; y++)
                {
                    double dy = (y + 0.5) - c;
                    int row = y * data.Stride;
                    for (int x = 0; x < sw; x++)
                    {
                        double u = Math.Atan2((x + 0.5) - c, -dy) * inv;
                        if (u < 0) u += 1.0;
                        if (u < SweepFrom || u > SweepTo) continue;
                        Color col = SweepColor(u);
                        if (col.A == 0) continue;
                        int o = row + x * 4;
                        buf[o + 0] = (byte)(col.B * col.A / 255);
                        buf[o + 1] = (byte)(col.G * col.A / 255);
                        buf[o + 2] = (byte)(col.R * col.A / 255);
                        buf[o + 3] = col.A;
                    }
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(data); }

            conic = bmp;
        }

        /// <summary>角度（0=正上方，顺时针 0..1）→ conic-gradient 的颜色。</summary>
        private Color SweepColor(double u)
        {
            Color accent = Accent;
            double a;
            Color c;
            if (u < SweepHotIn)
            {
                double k = (u - SweepFrom) / (SweepHotIn - SweepFrom);
                a = SweepPeak * k;
                c = accent;
            }
            else if (u < SweepWhite)
            {
                double k = (u - SweepHotIn) / (SweepWhite - SweepHotIn);
                a = SweepPeak + (1.0 - SweepPeak) * k;
                c = Mix(accent, Color.White, k);
            }
            else if (u < SweepHotOut)
            {
                double k = (u - SweepWhite) / (SweepHotOut - SweepWhite);
                a = 1.0 - (1.0 - SweepPeak) * k;
                c = Mix(Color.White, accent, k);
            }
            else
            {
                double k = (u - SweepHotOut) / (SweepTo - SweepHotOut);
                a = SweepPeak * (1.0 - k);
                c = accent;
            }
            a *= Strength;
            if (a <= 0.004) return Color.Transparent;
            return Color.FromArgb((int)Math.Round(255 * Math.Min(1.0, a)), c);
        }

        /// <summary>卡片轮廓内外各扩 band/2 的圆环：conic 图只在这一圈里露出。</summary>
        private static GraphicsPath RingPath(Rectangle card, int radius, int band)
        {
            int half = Math.Max(1, band / 2);
            var outer = card; outer.Inflate(half, half);
            var inner = card; inner.Inflate(-half, -half);
            var path = new GraphicsPath(FillMode.Alternate);
            using (var a = RoundPath(outer, Math.Min(radius + half, Math.Min(outer.Width, outer.Height) / 2)))
                path.AddPath(a, false);
            if (inner.Width > 2 && inner.Height > 2)
                using (var b = RoundPath(inner, Math.Max(1, Math.Min(radius - half, Math.Min(inner.Width, inner.Height) / 2))))
                    path.AddPath(b, false);
            return path;
        }

        private static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            int max = Math.Min(r.Width, r.Height) / 2;
            if (radius > max) radius = max;
            if (radius < 1) radius = 1;
            int d = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void Push()
        {
            if (frame == null || !IsHandleCreated) return;
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = frame.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                oldBitmap = SelectObject(memDc, hBitmap);
                var size = new NativeSize();
                size.cx = frame.Width;
                size.cy = frame.Height;
                var src = new NativePoint();
                var dst = new NativePoint();
                dst.X = Left;
                dst.Y = Top;
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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            if (haloWide != null) { haloWide.Dispose(); haloWide = null; }
            if (haloEdge != null) { haloEdge.Dispose(); haloEdge = null; }
            DisposeConic();
            if (baseFrame != null) { baseFrame.Dispose(); baseFrame = null; }
            if (frame != null) { frame.Dispose(); frame = null; }
            base.OnFormClosed(e);
        }
    }
}
