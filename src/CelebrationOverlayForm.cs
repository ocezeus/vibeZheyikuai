using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WorkCountdown
{
    /// <summary>
    /// 目标达成庆祝的「卡外出框层」：跟着卡片走的一块透明分层窗，四边各留
    /// <see cref="PadDip"/> 给月牙 / 行星 / 花环 / 彩屑 / 霓虹柔光。
    /// 整窗 WS_EX_TRANSPARENT + WS_EX_NOACTIVATE：点击全部穿透，不抢焦点。
    /// 卡内部分仍由 WidgetForm 自己画（它按卡片圆角裁切），这里只画外面那一圈。
    /// 叠在卡片之上（设计稿 .out 层 z-index 3），所以贴边的那几笔可以压在卡角上。
    /// </summary>
    internal sealed class CelebrationOverlayForm : Form
    {
        public const int PadDip = 140;
        private const int FrameMs = 33;

        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x00080000;
        private const int WsExTransparent = 0x00000020;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const int UlwAlpha = 0x2;
        private const byte AcSrcAlpha = 0x1;
        private const uint GwHwndPrev = 3;

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
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private readonly WidgetForm owner;
        private readonly float scale;
        private readonly int pad;
        private readonly Timer timer;

        private string variantId;
        private bool quiet;
        private bool collapsed;
        private double t, inP, outP;
        private Rectangle cardRect;
        private Bitmap frame;

        public CelebrationOverlayForm(WidgetForm owner, float scale)
        {
            this.owner = owner;
            this.scale = scale <= 0 ? 1f : scale;
            pad = Math.Max(16, (int)Math.Round(PadDip * this.scale));

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = false;
            TopMost = owner != null && owner.TopMost;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            timer = new Timer();
            timer.Interval = FrameMs;
            timer.Tick += delegate { Step(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WsExToolWindow | WsExLayered | WsExTransparent | WsExNoActivate;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        /// <summary>开场：定好版本与静态/动态，贴到卡片外面并开始跟随。</summary>
        public void Begin(string variantId, bool quiet, bool collapsed, double t, double inP, double outP)
        {
            this.variantId = variantId;
            this.quiet = quiet;
            this.collapsed = collapsed;
            this.t = t;
            this.inP = inP;
            this.outP = outP;
            if (!Visible) Show();
            cardRect = Rectangle.Empty;
            Follow();
            OrderAbove();
            if (quiet) timer.Stop(); else timer.Start();
            Invalidate();
        }

        /// <summary>推进一帧（Widget 的庆祝计时器每帧调一次）。</summary>
        public void Advance(double t, double inP, double outP, bool collapsed)
        {
            this.t = t;
            this.inP = inP;
            this.outP = outP;
            this.collapsed = collapsed;
            if (!Visible) { Begin(variantId, quiet, collapsed, t, inP, outP); return; }
            Follow();
            if (quiet) Push(); else Invalidate();
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
            if (!owner.Visible) { if (Visible) Hide(); return; }
            if (!Visible) Show();
            Follow();
            Invalidate();
        }

        /// <summary>贴到卡片四边各留 pad 的位置。</summary>
        private void Follow()
        {
            if (owner == null || owner.IsDisposed) return;
            Rectangle card = owner.Bounds;
            if (card == cardRect && Visible) return;
            cardRect = card;
            int w = card.Width + pad * 2;
            int h = card.Height + pad * 2;
            Bounds = new Rectangle(card.Left - pad, card.Top - pad, w, h);
            if (TopMost != owner.TopMost) TopMost = owner.TopMost;
            OrderAbove();
        }

        /// <summary>压在卡片上面：把窗口插到「卡片上面那个窗口」的后面。</summary>
        private void OrderAbove()
        {
            if (owner == null || !owner.IsHandleCreated || !IsHandleCreated) return;
            IntPtr above = GetWindow(owner.Handle, GwHwndPrev);
            SetWindowPos(Handle, above, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        private void Render()
        {
            if (Width <= 0 || Height <= 0) return;
            if (frame == null || frame.Width != Width || frame.Height != Height)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }
            frame.SetResolution(96f * scale, 96f * scale);

            IGoalVariant variant = GoalVariants.Get(variantId);
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                if (variant == null) return;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // 透明底上不用 ClearType：灰阶抗锯齿才不留彩边
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.TranslateTransform(pad, pad);
                var scene = new GoalScene();
                scene.g = g;
                scene.W = cardRect.Width;
                scene.H = cardRect.Height;
                scene.scale = scale;
                scene.collapsed = collapsed;
                scene.quiet = quiet;
                scene.outside = true;
                scene.t = t;
                scene.In = inP;
                scene.Out = outP;
                variant.PaintOutside(scene);
            }
        }

        private void Push()
        {
            if (frame == null || !IsHandleCreated || !Visible) return;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (timer != null) timer.Dispose();
                if (frame != null) { frame.Dispose(); frame = null; }
            }
            base.Dispose(disposing);
        }
    }
}
