using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WorkCountdown
{
    /// <summary>
    /// 弧光派生配色：以主题强调色作种子，按固定色相偏移算出的六种光色。
    /// 规则是设计定稿（−45° / 0° / −100° / +42° 与 hot / deep），不是另选一套色板；
    /// 结果只跟 accent 有关，所以按 accent 缓存，绘制时不再算 HSL。
    /// </summary>
    public sealed class ArcPalette
    {
        /// <summary>饱和度下限：暗主题的强调色往往偏灰，抬到 0.68 才看得出光。</summary>
        public const double SatFloor = 0.68;
        /// <summary>卡片底色的明度越过这条线就按浅色主题处理（光色往底色压、整体减淡）。</summary>
        public const double LightLine = 0.58;

        public Color Seed;
        public Color Arc1;      // 种子 −45°：近邻色
        public Color Arc2;      // 种子本身，亮度提到 0.64（不是 accent 的原值）
        public Color Arc3;      // 种子 −100°：环境 / 远景色
        public Color Arc4;      // 种子 +42°：另一侧邻色
        public Color Hot;       // 近白高光
        public Color Deep;      // 深色底光
        /// <summary>种子色相（0..360），色相条与测试断言用。</summary>
        public double Hue;
        /// <summary>抬到下限之后的饱和度。</summary>
        public double Sat;
    }


    /// <summary>一次绘制的输入：卡片物理像素尺寸、展开 / 收起、底色明暗、漂移相位。</summary>
    public struct ArcScene
    {
        public Rectangle Bounds;
        public bool Collapsed;
        public bool Light;
        public Color Ground;
        public double Seconds;

        public static ArcScene Of(Rectangle bounds, bool collapsed, ThemeDef theme, double seconds)
        {
            var scene = new ArcScene();
            scene.Bounds = bounds;
            scene.Collapsed = collapsed;
            scene.Ground = theme == null ? Color.Black : theme.bgBottom;
            scene.Light = theme != null && Frost.Luminance(theme.bgBottom) > 0.56;
            scene.Seconds = seconds;
            return scene;
        }
    }

    /// <summary>
    /// 弧光（Aurora Arc）：可用性判定 + 派生色 + 卡片内绘制。
    /// 只有《默认》皮肤允许开；背景风格（纯色 / 磨砂玻璃）只改变卡片底怎么画，不影响可用性。
    /// </summary>
    public static class ArcGlow
    {
        private static readonly Dictionary<int, ArcPalette> palettes = new Dictionary<int, ArcPalette>();
        private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>漂移用的秒表：三块光幕的周期不同，绘制时各自按秒表相位取值。</summary>
        public static double NowSeconds()
        {
            return clock.Elapsed.TotalSeconds;
        }

        // ---------------- 可用性 ----------------

        /// <summary>这套设置下弧光能不能用：只看皮肤，不看背景风格。</summary>
        public static bool Supported(UiSettings ui)
        {
            return Skins.IsDefault(ui);
        }

        /// <summary>这次绘制要不要画弧光：默认皮肤 + 用户打开开关。</summary>
        public static bool Enabled(UiSettings ui)
        {
            return ui != null && ui.arcGlow && Supported(ui);
        }

        // ---------------- 派生色 ----------------

        /// <summary>按强调色派生出整组光色（纯函数，测试直接调它）。</summary>
        public static ArcPalette Derive(Color seed)
        {
            double h, s, l;
            Themes.ToHsl(seed, out h, out s, out l);
            double sat = Math.Max(s, ArcPalette.SatFloor);
            var p = new ArcPalette();
            p.Seed = seed;
            p.Hue = h;
            p.Sat = sat;
            p.Arc1 = Themes.FromHsl(NormHue(h - 45), sat * 0.78, 0.55);
            p.Arc2 = Themes.FromHsl(NormHue(h), sat, 0.64);
            p.Arc3 = Themes.FromHsl(NormHue(h - 100), sat * 0.92, 0.72);
            p.Arc4 = Themes.FromHsl(NormHue(h + 42), sat * 0.82, 0.64);
            p.Hot = Themes.FromHsl(NormHue(h + 6), 0.28, 0.93);
            p.Deep = Themes.FromHsl(NormHue(h + 12), sat, 0.42);
            return p;
        }

        /// <summary>当前主题那组光色（按 accent 缓存，同一套主题只算一次）。</summary>
        public static ArcPalette For(ThemeDef theme)
        {
            Color seed = theme == null || theme.accent.IsEmpty ? Themes.Get("midnight").accent : theme.accent;
            int key = seed.ToArgb();
            lock (palettes)
            {
                ArcPalette hit;
                if (palettes.TryGetValue(key, out hit)) return hit;
                ArcPalette made = Derive(seed);
                palettes[key] = made;
                return made;
            }
        }

        private static double NormHue(double h)
        {
            h = h % 360;
            if (h < 0) h += 360;
            return h;
        }

        // ---------------- 绘制 ----------------


        // 光幕缓存在 256×192 的小图上，按卡片比例拉伸绘制：既省掉逐帧的模糊，
        // 拉伸本身又带来一层天然柔化。卡片尺寸逐帧变化（展开动画），所以缓存不能按像素尺寸做键。
        private const int CellW = 256;
        private const int CellH = 192;

        private sealed class VeilSet
        {
            public Bitmap A;
            public Bitmap B;
            public Bitmap C;
            public Bitmap Flare;

            public void Dispose()
            {
                if (A != null) A.Dispose();
                if (B != null) B.Dispose();
                if (C != null) C.Dispose();
                if (Flare != null) Flare.Dispose();
                A = B = C = Flare = null;
            }
        }

        private static readonly Dictionary<string, VeilSet> veilSets = new Dictionary<string, VeilSet>();
        private static readonly List<string> veilKeys = new List<string>();

        private sealed class ArcStroke
        {
            /// <summary>起点 + 每段三次贝塞尔的三个控制点（viewBox 坐标）。</summary>
            public float[] Path;
            /// <summary>取值见 ToneOf：0 = 渐变 ia1，1 = 渐变 ia2，8 = 渐变 ia3，2..7 = arc1..deep。</summary>
            public int Tone;
            public float Width;
            public double Alpha;
            public bool Soft;
        }

        private static readonly ArcStroke[] BigArcs = BuildBigArcs();
        private static readonly ArcStroke[] MiniArcs = BuildMiniArcs();

        /// <summary>把整组光画进卡片：宽幕 → 细弧 → 右下角 flare，全部裁在卡片圆角内。</summary>
        public static void Paint(Graphics g, GraphicsPath cardPath, ArcPalette palette, ArcScene scene)
        {
            if (g == null || cardPath == null || palette == null) return;
            Rectangle b = scene.Bounds;
            if (b.Width < 8 || b.Height < 8) return;
            VeilSet set = VeilsFor(palette, scene);
            GraphicsState state = g.Save();
            try
            {
                g.SetClip(cardPath, CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                DrawVeils(g, set, scene);
                DrawArcs(g, palette, scene);
            }
            finally
            {
                g.Restore(state);
            }
        }

        // ---------------- 光幕 ----------------

        private static VeilSet VeilsFor(ArcPalette p, ArcScene scene)
        {
            string key = p.Seed.ToArgb() + (scene.Light ? "/L/" : "/D/") + (scene.Collapsed ? "mini" : "big");
            lock (veilSets)
            {
                VeilSet hit;
                if (veilSets.TryGetValue(key, out hit)) return hit;
                if (veilKeys.Count >= 6)
                {
                    string old = veilKeys[0];
                    veilKeys.RemoveAt(0);
                    VeilSet dead;
                    if (veilSets.TryGetValue(old, out dead))
                    {
                        veilSets.Remove(old);
                        dead.Dispose();
                    }
                }
                VeilSet made = BuildVeils(p, scene);
                veilSets[key] = made;
                veilKeys.Add(key);
                return made;
            }
        }

        private static VeilSet BuildVeils(ArcPalette p, ArcScene scene)
        {
            bool light = scene.Light;
            Color ground = scene.Ground;
            double k = light ? 0.62 : 0.90;
            var set = new VeilSet();
            set.A = SoftLayer(delegate(Graphics g) { PaintVeilA(g, p, light, ground, scene.Collapsed, k); }, 10);
            set.B = SoftLayer(delegate(Graphics g) { PaintVeilB(g, p, light, ground, k); }, 8);
            set.C = SoftLayer(delegate(Graphics g) { PaintVeilC(g, p, light, ground, k); }, 12);
            set.Flare = SoftLayer(delegate(Graphics g) { PaintFlare(g, p, light, ground); }, 10);
            return set;
        }

        /// <summary>
        /// GDI+ 没有高斯模糊：把小图层缩到 1/(radius+1) 再插值放大，来回两遍就是一层很平滑的模糊。
        /// 光幕本来就是低频色块，这个近似眼睛分不出来，比逐像素卷积省得多。
        /// </summary>
        private static void Blur(Bitmap bmp, int radius, int passes)
        {
            if (radius < 1 || passes < 1) return;
            int w = bmp.Width, h = bmp.Height;
            for (int pass = 0; pass < passes; pass++)
            {
                int sw = Math.Max(1, w / (radius + 1));
                int sh = Math.Max(1, h / (radius + 1));
                using (var small = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(bmp, new Rectangle(0, 0, sw, sh));
                    }
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(small, new Rectangle(0, 0, w, h));
                    }
                }
            }
        }

        private static Bitmap SoftLayer(Action<Graphics> paint, int blur)
        {
            var bmp = new Bitmap(CellW, CellH, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                paint(g);
            }
            Blur(bmp, blur, 2);
            return bmp;
        }

        /// <summary>取一档光色，按浅色主题往卡片底色压一压，再套上透明度。</summary>
        private static Color ToneOf(ArcPalette p, int tone, double alpha, bool light, Color ground)
        {
            Color c;
            switch (tone)
            {
                case 3: c = p.Arc2; break;
                case 4: c = p.Arc3; break;
                case 5: c = p.Arc4; break;
                case 6: c = Skins.Mix(p.Hot, p.Arc1, 0.45); break;   // 近白直接叠在深底上会发灰，掺一点近邻色保住青绿
                case 7: c = p.Deep; break;
                default: c = p.Arc1; break;
            }
            // 浅色卡片上直接叠亮色会糊成白雾：先把光色压深，叠上去才是有色淡彩（近似 CSS 的 multiply）
            if (light) c = Skins.Mix(c, Color.Black, 0.30);
            return Skins.Alpha(c, alpha);
        }

        private static void PaintVeilA(Graphics g, ArcPalette p, bool light, Color ground, bool collapsed, double k)
        {
            double left, top, w, h;
            if (collapsed)
            {
                left = -0.30; top = -0.20; w = 1.40; h = 1.20;
            }
            else
            {
                left = -0.38; top = -0.10; w = 1.26; h = 0.70;
            }
            var rect = new RectangleF((float)(left * CellW), (float)(top * CellH), (float)(w * CellW), (float)(h * CellH));
            double o = 0.52 * k;
            var cols = new Color[]
            {
                ToneOf(p, 3, 0, light, ground), ToneOf(p, 3, 0, light, ground),
                ToneOf(p, 3, 0.46 * o, light, ground), ToneOf(p, 2, 0.62 * o, light, ground),
                ToneOf(p, 4, 0.34 * o, light, ground), ToneOf(p, 4, 0, light, ground),
                ToneOf(p, 4, 0, light, ground)
            };
            var pos = new float[] { 0f, 0.06f, 0.32f, 0.52f, 0.69f, 0.88f, 1f };
            PaintBand(g, rect, -16f, cols, pos);
        }

        private static void PaintVeilB(Graphics g, ArcPalette p, bool light, Color ground, double k)
        {
            var rect = new RectangleF((float)(-0.14 * CellW), (float)(0.47 * CellH), (float)(1.10 * CellW), (float)(0.38 * CellH));
            double o = 0.36 * k;
            var cols = new Color[]
            {
                ToneOf(p, 4, 0, light, ground), ToneOf(p, 4, 0, light, ground),
                ToneOf(p, 4, 0.30 * o, light, ground), ToneOf(p, 2, 0.40 * o, light, ground),
                ToneOf(p, 2, 0, light, ground), ToneOf(p, 2, 0, light, ground)
            };
            var pos = new float[] { 0f, 0.12f, 0.38f, 0.60f, 0.86f, 1f };
            PaintBand(g, rect, -20f, cols, pos);
        }

        private static void PaintVeilC(Graphics g, ArcPalette p, bool light, Color ground, double k)
        {
            var rect = new RectangleF((float)(0.44 * CellW), (float)(-0.22 * CellH), (float)(0.70 * CellW), (float)(0.80 * CellH));
            GraphicsState s = g.Save();
            g.TranslateTransform(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
            g.RotateTransform(12f);
            var local = new RectangleF(-rect.Width / 2f, -rect.Height / 2f, rect.Width, rect.Height);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(local);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = ToneOf(p, 5, 0.44 * 0.30 * k, light, ground);
                    brush.SurroundColors = new Color[] { ToneOf(p, 5, 0, light, ground) };
                    brush.FocusScales = new PointF(0.45f, 0.45f);
                    g.FillPath(brush, path);
                }
            }
            g.Restore(s);
        }

        /// <summary>右下角 flare：一小团暖光，卡片外沿溢出后被圆角裁掉。</summary>
        private static void PaintFlare(Graphics g, ArcPalette p, bool light, Color ground)
        {
            var rect = new RectangleF((float)(0.44 * CellW), (float)(0.36 * CellH), (float)(0.72 * CellW), (float)(0.98 * CellH));
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (var brush = new PathGradientBrush(path))
                {
                    float cx = rect.Left + rect.Width * 0.58f;
                    float cy = rect.Top + rect.Height * 0.60f;
                    brush.CenterPoint = new PointF(cx, cy);
                    brush.CenterColor = ToneOf(p, 2, 0.38 * 0.44 * (light ? 0.62 : 1.0), light, ground);
                    brush.SurroundColors = new Color[] { ToneOf(p, 2, 0, light, ground) };
                    brush.FocusScales = new PointF(0.35f, 0.35f);
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>一块斜向的软色幕：绕自己的中心转 a 度，然后按 0..1 的色标填进去。</summary>
        private static void PaintBand(Graphics g, RectangleF rect, float angle, Color[] cols, float[] pos)
        {
            GraphicsState s = g.Save();
            float cx = rect.Left + rect.Width / 2f;
            float cy = rect.Top + rect.Height / 2f;
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            g.TranslateTransform(-cx, -cy);
            var local = new RectangleF(-rect.Width / 2f, -rect.Height / 2f, rect.Width, rect.Height);
            using (var brush = new LinearGradientBrush(
                new PointF(local.Left, local.Top + local.Height / 2f),
                new PointF(local.Right, local.Top + local.Height / 2f), Color.Transparent, Color.Transparent))
            {
                var blend = new ColorBlend();
                blend.Colors = cols;
                blend.Positions = pos;
                brush.InterpolationColors = blend;
                brush.WrapMode = WrapMode.TileFlipXY;
                g.FillRectangle(brush, new RectangleF(cx - rect.Width / 2f, cy - rect.Height / 2f, rect.Width, rect.Height));
            }
            g.Restore(s);
        }

        // ---------------- 细弧 ----------------

        private static void DrawArcs(Graphics g, ArcPalette p, ArcScene scene)
        {
            ArcStroke[] table = scene.Collapsed ? MiniArcs : BigArcs;
            Rectangle b = scene.Bounds;
            float vw = scene.Collapsed ? 360f : 460f;
            float vh = scene.Collapsed ? 110f : 310f;
            float sx = b.Width / vw;
            float sy = b.Height / vh;
            for (int i = 0; i < table.Length; i++)
            {
                ArcStroke stroke = table[i];
                using (var path = BuildArcPath(stroke.Path, b, sx, sy))
                {
                    float width = Math.Max(0.6f, stroke.Width * (sx + sy) * 0.5f);
                    if (stroke.Soft)
                    {
                        // 宽而淡的软光：同一段弧画两遍、各半透明、错开一点，代替 blur
                        double alpha = stroke.Alpha * (scene.Light ? 0.62 : 0.62) * 0.5;
                        for (int d = -1; d <= 1; d += 2)
                        {
                            using (var pen = new Pen(ToneOf(p, stroke.Tone, alpha, scene.Light, scene.Ground), width))
                            {
                                pen.LineJoin = LineJoin.Round;
                                GraphicsState st = g.Save();
                                g.TranslateTransform(0, d * width * 0.4f);
                                g.DrawPath(pen, path);
                                g.Restore(st);
                            }
                        }
                        continue;
                    }
                    DrawStroke(g, path, p, stroke, scene, width, b);
                }
            }
        }

        private static void DrawStroke(Graphics g, GraphicsPath path, ArcPalette p, ArcStroke stroke, ArcScene scene, float width, Rectangle b)
        {
            double alpha = stroke.Alpha * 0.62;
            if (stroke.Width >= 0.8f)
            {
                // 每根细弧下面垫一层宽一点的淡光，弧线才不会像刀刻的直线
                using (var pen = new Pen(ToneOf(p, 2, alpha * 0.12, scene.Light, scene.Ground), width * 2.6f))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }
            if (stroke.Tone == 0 || stroke.Tone == 1 || stroke.Tone == 8)
            {
                using (var brush = GradientBrush(p, stroke.Tone, scene, b))
                using (var pen = new Pen(brush, width))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawPath(pen, path);
                }
                return;
            }
            using (var pen = new Pen(ToneOf(p, stroke.Tone, alpha, scene.Light, scene.Ground), width))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawPath(pen, path);
            }
        }

        /// <summary>
        /// 细弧的渐变：方向取卡片左下 → 右上（和原型 SVG 的 x1=0,y1=1 → x2=1,y2=0 一致）。
        /// 渐变笔刷坐标在整张卡片上，所以同一支笔刷画哪条弧都接得上原型的色序。
        /// </summary>
        private static LinearGradientBrush GradientBrush(ArcPalette p, int tone, ArcScene scene, Rectangle b)
        {
            double k = scene.Light ? 0.50 : 0.62;
            Color[] cols;
            float[] pos;
            if (tone == 1)
            {
                cols = new Color[]
                {
                    ToneOf(p, 3, 0, scene.Light, scene.Ground), ToneOf(p, 3, 0.60 * k, scene.Light, scene.Ground),
                    ToneOf(p, 2, 0.75 * k, scene.Light, scene.Ground), ToneOf(p, 2, 0, scene.Light, scene.Ground)
                };
                pos = new float[] { 0f, 0.5f, 0.8f, 1f };
            }
            else if (tone == 8)
            {
                cols = new Color[]
                {
                    ToneOf(p, 3, 0, scene.Light, scene.Ground), ToneOf(p, 2, 0.55 * k, scene.Light, scene.Ground),
                    ToneOf(p, 6, 0.80 * k, scene.Light, scene.Ground), ToneOf(p, 4, 0, scene.Light, scene.Ground)
                };
                pos = new float[] { 0f, 0.42f, 0.7f, 1f };
            }
            else
            {
                cols = new Color[]
                {
                    ToneOf(p, 3, 0, scene.Light, scene.Ground), ToneOf(p, 2, 0.55 * k, scene.Light, scene.Ground),
                    ToneOf(p, 6, 0.88 * k, scene.Light, scene.Ground), ToneOf(p, 4, 0.55 * k, scene.Light, scene.Ground),
                    ToneOf(p, 4, 0, scene.Light, scene.Ground)
                };
                pos = new float[] { 0f, 0.34f, 0.58f, 0.8f, 1f };
            }
            var brush = new LinearGradientBrush(
                new PointF(b.Left, b.Bottom), new PointF(b.Right, b.Top), Color.Transparent, Color.Transparent);
            var blend = new ColorBlend();
            blend.Colors = cols;
            blend.Positions = pos;
            brush.InterpolationColors = blend;
            brush.WrapMode = WrapMode.TileFlipXY;
            return brush;
        }

        private static GraphicsPath BuildArcPath(float[] pts, Rectangle b, float sx, float sy)
        {
            var path = new GraphicsPath();
            PointF cur = new PointF(b.Left + pts[0] * sx, b.Top + pts[1] * sy);
            int i = 2;
            while (i + 5 < pts.Length)
            {
                PointF c1 = new PointF(b.Left + pts[i] * sx, b.Top + pts[i + 1] * sy);
                PointF c2 = new PointF(b.Left + pts[i + 2] * sx, b.Top + pts[i + 3] * sy);
                PointF end = new PointF(b.Left + pts[i + 4] * sx, b.Top + pts[i + 5] * sy);
                path.AddBezier(cur, c1, c2, end);
                cur = end;
                i += 6;
            }
            return path;
        }

        /// <summary>原型 viewBox 460×310 的五条细弧（两条软光 + 五条锐弧），坐标照原样搬过来。</summary>
        private static ArcStroke[] BuildBigArcs()
        {
            return new ArcStroke[]
            {
                Make(new float[] { 90, 355, 185, 310, 270, 262, 350, 208, 410, 170, 452, 130, 500, 70 }, 2, 18f, 0.20, true),
                Make(new float[] { 150, 380, 235, 335, 315, 290, 390, 245, 440, 214, 480, 178, 520, 125 }, 3, 14f, 0.16, true),
                Make(new float[] { 90, 355, 185, 310, 270, 262, 350, 208, 410, 170, 452, 130, 500, 70 }, 0, 2.1f, 0.95, false),
                Make(new float[] { 150, 380, 235, 335, 315, 290, 390, 245, 440, 214, 480, 178, 520, 125 }, 1, 1.2f, 0.80, false),
                Make(new float[] { 230, 360, 305, 322, 370, 284, 430, 240, 460, 220, 485, 198, 515, 165 }, 0, 0.85f, 0.55, false),
                Make(new float[] { 120, 300, 205, 270, 280, 232, 355, 185 }, 4, 0.6f, 0.45, false),
                Make(new float[] { 330, 280, 390, 240, 440, 205, 500, 150 }, 5, 0.9f, 0.42, false)
            };
        }

        /// <summary>收起态 viewBox 360×110：一条软光 + 两条细弧。</summary>
        private static ArcStroke[] BuildMiniArcs()
        {
            return new ArcStroke[]
            {
                Make(new float[] { 70, 125, 145, 105, 215, 82, 285, 55, 320, 40, 350, 22, 390, 0 }, 2, 9f, 0.20, true),
                Make(new float[] { 70, 125, 145, 105, 215, 82, 285, 55, 320, 40, 350, 22, 390, 0 }, 8, 1.4f, 0.85, false),
                Make(new float[] { 130, 128, 200, 108, 265, 88, 330, 60 }, 3, 0.7f, 0.55, false)
            };
        }

        private static ArcStroke Make(float[] path, int tone, float width, double alpha, bool soft)
        {
            var s = new ArcStroke();
            s.Path = path;
            s.Tone = tone;
            s.Width = width;
            s.Alpha = alpha;
            s.Soft = soft;
            return s;
        }

        // ---------------- 漂移 ----------------

        /// <summary>
        /// 原型用 CSS alternate 关键帧；这里按秒表做同样的来回缓动。
        /// 三块光幕各自 19 / 22 / 28 秒，同一处不会同时到端点，看起来是持续缓慢地流动。
        /// </summary>
        private static double Drift(double seconds, double period, double from, double to)
        {
            if (period <= 0) return to;
            double span = seconds % (period * 2);
            if (span < 0) span += period * 2;
            double u = span < period ? span / period : (period * 2 - span) / period;
            u = 0.5 - 0.5 * Math.Cos(Math.PI * u);
            return from + (to - from) * u;
        }

        private static void DrawVeils(Graphics g, VeilSet set, ArcScene scene)
        {
            Rectangle b = scene.Bounds;
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            double t = scene.Seconds;
            // 原型里的位移是按 460 / 360 宽的卡片给的像素数，这里换算成当前卡片的比例
            double span = scene.Collapsed ? 360.0 : 460.0;
            double scale = b.Width / span;
            DrawLayer(g, set.A, b, cx, cy,
                Drift(t, 22, -16, -12), Drift(t, 22, -14, 16) * scale, Drift(t, 22, 6, -8) * scale,
                Drift(t, 22, 1.02, 1.06));
            DrawLayer(g, set.B, b, cx, cy,
                Drift(t, 28, -20, -16), Drift(t, 28, 10, -18) * scale, Drift(t, 28, 8, -5) * scale, 1.0);
            DrawLayer(g, set.C, b, cx, cy,
                Drift(t, 19, 12, 8), Drift(t, 19, 0, -8) * scale, Drift(t, 19, -4, 8) * scale, 1.0);
            DrawLayer(g, set.Flare, b, cx, cy, 0, 0, 0, 1.0);
        }

        private static void DrawLayer(Graphics g, Bitmap layer, Rectangle b, float cx, float cy,
            double rot, double dx, double dy, double scale)
        {
            if (layer == null) return;
            GraphicsState state = g.Save();
            g.TranslateTransform(cx + (float)dx, cy + (float)dy);
            g.RotateTransform((float)rot);
            g.ScaleTransform((float)scale, (float)scale);
            g.TranslateTransform(-cx, -cy);
            g.DrawImage(layer, b, 0, 0, layer.Width, layer.Height, GraphicsUnit.Pixel);
            g.Restore(state);
        }
    }
}