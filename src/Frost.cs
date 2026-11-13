using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WorkCountdown
{
    /// <summary>
    /// 磨砂玻璃背景（主题背景风格 <c>ui.bgStyle = "glass"</c>）。
    ///
    /// 卡片 / 小纸条面板 / 胶囊气泡都是分层窗（UpdateLayeredWindow，逐像素 alpha），
    /// 这里用「半透明主题底色 + 柔光雾气 + 模糊磨砂颗粒 + 玻璃边缘高光」模拟一块压在桌面上的毛玻璃。
    /// 不做真实背景抓屏模糊：那种做法每次重绘都要 BitBlt + 模糊整屏，动画（秒/厘秒、提醒扩散）会掉帧，
    /// 而且分层窗会把自己也拍进去，形成反馈。
    /// </summary>
    internal static class Frost
    {
        public const string Solid = "solid";
        public const string Glass = "glass";

        /// <summary>底色最高不透明度：再高就不像玻璃了，再低浅色主题上的字会发飘。</summary>
        private const int DarkBaseAlpha = 206;
        private const int LightBaseAlpha = 218;

        private static Bitmap grain;
        private static readonly object grainLock = new object();

        public static bool On(UiSettings ui)
        {
            return ui != null && ui.bgStyle == Glass;
        }

        public static string Normalize(string style)
        {
            return style == Glass ? Glass : Solid;
        }

        public static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        private static int A(double alpha, double factor)
        {
            double v = alpha * factor;
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (int)Math.Round(v);
        }

        /// <summary>
        /// 把一块磨砂玻璃画进 path：半透明底色 → 柔光雾气 → 磨砂颗粒 → 顶部高光 / 底部暗边。
        /// <paramref name="alpha"/> 是整块表面的出现/退场透明度（0..1）；
        /// <paramref name="strength"/> 给胶囊这类小面积用 0.86，卡片用 1。
        /// </summary>
        public static void PaintSurface(Graphics g, GraphicsPath path, Rectangle bounds, ThemeDef theme,
            double alpha, double strength)
        {
            if (theme == null || path == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            if (alpha <= 0.003) return;
            if (strength <= 0) strength = 1;
            double k = Math.Min(1.0, strength);
            bool light = Luminance(theme.bgTop) > 0.56;

            GraphicsState state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1) 半透明主题底色：玻璃的「厚度」都靠这层
            int baseA = (int)Math.Round((light ? LightBaseAlpha : DarkBaseAlpha) * alpha * (0.94 + 0.06 * k));
            using (var br = new LinearGradientBrush(new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height)),
                Color.FromArgb(Math.Min(255, A(baseA, light ? 1.0 : 0.97)), theme.bgTop),
                Color.FromArgb(Math.Min(255, A(baseA, 0.93)), theme.bgBottom), 90f))
                g.FillPath(br, path);

            // 2) 柔光雾气：左上大团白光 + 右下主题色漫反射，玻璃的「朦胧感」来源
            PaintCloud(g, bounds, light ? Color.White : Color.FromArgb(236, 242, 255),
                (light ? 96 : 76) * alpha * k, true);
            PaintCloud(g, bounds, theme.accent, (light ? 30 : 38) * alpha * k, false);

            // 3) 磨砂颗粒（模糊过的噪点，很淡，只提供质感）
            using (var tb = new TextureBrush(GrainTile(), WrapMode.Tile))
            {
                tb.ScaleTransform(g.DpiX / 96f, g.DpiY / 96f);
                g.FillPath(tb, path);
            }

            // 4) 一道斜向反光：玻璃表面被光扫过的高光带
            PaintSheen(g, bounds, light ? 26 : 18, alpha * k);

            // 5) 顶部高光 / 底部暗边：一圈很细的玻璃边缘
            float rim = Math.Max(1f, Math.Min(2.6f, bounds.Height / 110f));
            using (var pen = new Pen(Color.FromArgb(A(light ? 190 : 150, alpha * k), Color.White), rim))
                g.DrawLine(pen, bounds.X + S(bounds, 8), bounds.Y + rim / 2f, bounds.Right - S(bounds, 8), bounds.Y + rim / 2f);
            using (var pen = new Pen(Color.FromArgb(A(light ? 46 : 56, alpha * k), Color.Black), rim))
                g.DrawLine(pen, bounds.X + S(bounds, 8), bounds.Bottom - rim / 2f, bounds.Right - S(bounds, 8), bounds.Bottom - rim / 2f);

            g.Restore(state);
        }

        private static float S(Rectangle bounds, float dip)
        {
            return Math.Max(2f, bounds.Height / 120f * dip);
        }

        /// <summary>一块柔光：PathGradientBrush 中心不透明、边缘透明。</summary>
        private static void PaintCloud(Graphics g, Rectangle bounds, Color color, double alpha, bool topLeft)
        {
            if (alpha <= 0.5) return;
            float w = bounds.Width * (topLeft ? 1.25f : 1.1f);
            float h = bounds.Height * (topLeft ? 1.15f : 1.0f);
            float x = topLeft ? bounds.X - w * 0.30f : bounds.Right - w * 0.60f;
            float y = topLeft ? bounds.Y - h * 0.38f : bounds.Bottom - h * 0.52f;
            var rect = new RectangleF(x, y, w, h);
            var center = new PointF(topLeft ? bounds.X + bounds.Width * 0.18f : bounds.Right - bounds.Width * 0.20f,
                topLeft ? bounds.Y + bounds.Height * 0.12f : bounds.Bottom - bounds.Height * 0.18f);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterPoint = center;
                    br.CenterColor = Color.FromArgb(A(255, alpha / 255.0), color);
                    br.SurroundColors = new[] { Color.FromArgb(0, color) };
                    br.FocusScales = new PointF(0.03f, 0.03f);
                    g.FillPath(br, path);
                }
            }
        }

        /// <summary>斜向反光带：一条 25° 的白色渐变，宽度约卡片对角线的 1/3。</summary>
        private static void PaintSheen(Graphics g, Rectangle bounds, int alpha, double strength)
        {
            if (alpha <= 0) return;
            int w = bounds.Width, h = bounds.Height;
            var state = g.Save();
            g.TranslateTransform(bounds.X + w / 2f, bounds.Y + h / 2f);
            g.RotateTransform(-24f);
            float bandH = Math.Max(10f, h * 0.55f);
            var band = new RectangleF(-w, -bandH / 2f - h * 0.06f, w * 2f, bandH);
            using (var br = new LinearGradientBrush(band,
                Color.FromArgb(0, Color.White), Color.FromArgb(A(alpha, strength), Color.White), 90f))
            {
                ColorBlend blend = new ColorBlend(3);
                blend.Colors = new[] { Color.FromArgb(0, Color.White), Color.FromArgb(A(alpha, strength), Color.White), Color.FromArgb(0, Color.White) };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                br.InterpolationColors = blend;
                g.FillRectangle(br, band);
            }
            g.Restore(state);
        }

        /// <summary>
        /// 磨砂颗粒贴图：先按 2×2 块生成噪点、再做一遍盒式模糊，最后只保留很低的 alpha
        /// （看起来是雾面质感，不是电视雪花）。只生成一次，之后 tile 铺满。
        /// </summary>
        private static Bitmap GrainTile()
        {
            if (grain != null) return grain;
            lock (grainLock)
            {
                if (grain != null) return grain;
                const int n = 128;
                const int cell = 2;
                int cells = n / cell;
                var rnd = new Random(20261113);
                var noise = new double[cells, cells];
                for (int y = 0; y < cells; y++)
                    for (int x = 0; x < cells; x++)
                        noise[x, y] = rnd.NextDouble();

                // 两遍盒式模糊（边界环绕，保证 tile 无缝）
                for (int pass = 0; pass < 2; pass++)
                {
                    var next = new double[cells, cells];
                    for (int y = 0; y < cells; y++)
                    {
                        for (int x = 0; x < cells; x++)
                        {
                            double sum = 0;
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                    sum += noise[((x + dx) + cells) % cells, ((y + dy) + cells) % cells];
                            next[x, y] = sum / 9.0;
                        }
                    }
                    noise = next;
                }

                var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
                var data = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    var bytes = new byte[stride * n];
                    for (int y = 0; y < n; y++)
                    {
                        for (int x = 0; x < n; x++)
                        {
                            // 中间亮度在 5..12 之间：够看出磨砂，又不抢文字
                            int a = 5 + (int)Math.Round(noise[x / cell, y / cell] * 7);
                            int i = y * stride + x * 4;
                            bytes[i] = 255; bytes[i + 1] = 255; bytes[i + 2] = 255;
                            bytes[i + 3] = (byte)a;
                        }
                    }
                    System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                grain = bmp;
                return grain;
            }
        }
    }
}
