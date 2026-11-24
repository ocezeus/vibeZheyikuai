using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WorkCountdown
{
    // ============================================================================
    // 庆祝画面用的 GDI+ 小工具箱：颜色、柔光、星/花/月/叶、像素块、渐变字。
    // 所有尺寸都吃「物理像素」，由各版本用 GoalScene.S(dip) 换算；alpha 一律 0..1。
    // ============================================================================

    internal static class GoalPaint
    {
        // ---------------- 颜色 ----------------

        /// <summary>"#RRGGBB" 或 "#AARRGGBB"。</summary>
        public static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.White;
            string s = hex.Trim();
            if (s[0] == '#') s = s.Substring(1);
            try
            {
                if (s.Length == 8)
                    return Color.FromArgb(
                        Convert.ToInt32(s.Substring(0, 2), 16), Convert.ToInt32(s.Substring(2, 2), 16),
                        Convert.ToInt32(s.Substring(4, 2), 16), Convert.ToInt32(s.Substring(6, 2), 16));
                if (s.Length == 6)
                    return Color.FromArgb(255,
                        Convert.ToInt32(s.Substring(0, 2), 16), Convert.ToInt32(s.Substring(2, 2), 16),
                        Convert.ToInt32(s.Substring(4, 2), 16));
            }
            catch { }
            return Color.White;
        }

        public static Color A(Color c, double alpha)
        {
            return Color.FromArgb((int)Math.Round(Math.Max(0, Math.Min(1, alpha)) * 255), c.R, c.G, c.B);
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // ---------------- 底 ----------------

        /// <summary>整卡竖向渐变底（不透明）。</summary>
        public static void Backdrop(Graphics g, float w, float h, Color top, Color bottom)
        {
            using (var b = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(1, w), Math.Max(1, h)), top, bottom, 90f))
                g.FillRectangle(b, 0, 0, w, h);
        }

        /// <summary>径向柔光球：中心 alpha，边缘透明。</summary>
        public static void Glow(Graphics g, float cx, float cy, float r, Color color, double alpha)
        {
            if (r <= 0 || alpha <= 0.004) return;
            var box = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(box);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterPoint = new PointF(cx, cy);
                    brush.CenterColor = A(color, alpha);
                    brush.SurroundColors = new Color[] { A(color, 0) };
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>椭圆柔光（星云那种压扁的光斑）。</summary>
        public static void GlowEllipse(Graphics g, RectangleF box, Color color, double alpha)
        {
            if (box.Width <= 0 || box.Height <= 0 || alpha <= 0.004) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(box);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterPoint = new PointF(box.X + box.Width / 2f, box.Y + box.Height / 2f);
                    brush.CenterColor = A(color, alpha);
                    brush.SurroundColors = new Color[] { A(color, 0) };
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>把一张预烘的图层按 alpha 贴到 (x,y)：每帧只做一次 DrawImage，别重建渐变。</summary>
        public static void Layer(Graphics g, Bitmap layer, float x, float y, double alpha)
        {
            if (layer == null || alpha <= 0.004) return;
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix();
                cm.Matrix33 = (float)Math.Min(1, alpha);
                ia.SetColorMatrix(cm);
                g.DrawImage(layer, new Rectangle((int)Math.Round(x), (int)Math.Round(y), layer.Width, layer.Height),
                    0, 0, layer.Width, layer.Height, GraphicsUnit.Pixel, ia);
            }
        }

        /// <summary>预烘一张「径向柔光球」小图（后面用 Layer 贴，避免每帧 PathGradientBrush）。</summary>
        public static Bitmap BakeGlow(int w, int h, float scale, Color color, double alpha)
        {
            var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            bmp.SetResolution(96f * scale, 96f * scale);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                GlowEllipse(g, new RectangleF(0, 0, bmp.Width, bmp.Height), color, alpha);
            }
            return bmp;
        }

        // ---------------- 点 / 线 / 环 ----------------

        public static void Dot(Graphics g, float cx, float cy, float r, Color color, double alpha)
        {
            if (r <= 0 || alpha <= 0.004) return;
            using (var b = new SolidBrush(A(color, alpha))) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        }

        /// <summary>带柔光的点（星屑 / 霓虹小灯）。</summary>
        public static void GlowDot(Graphics g, float cx, float cy, float r, Color color, double alpha)
        {
            Glow(g, cx, cy, r * 4.2f, color, alpha * 0.34);
            Dot(g, cx, cy, r, color, alpha);
        }

        /// <summary>
        /// 便宜版柔光点：三层同心圆代替 PathGradientBrush。每帧几十上百颗星屑时用它，
        /// 单点开销约 1/10，肉眼几乎看不出差别（星点本来就小）。
        /// </summary>
        public static void SoftDot(Graphics g, float cx, float cy, float r, Color color, double alpha)
        {
            if (r <= 0 || alpha <= 0.004) return;
            Dot(g, cx, cy, r * 2.3f, color, alpha * 0.10);
            Dot(g, cx, cy, r * 1.55f, color, alpha * 0.20);
            Dot(g, cx, cy, r, color, alpha);
        }

        public static void Ring(Graphics g, float cx, float cy, float r, Color color, float width, double alpha)
        {
            if (r <= 0 || width <= 0 || alpha <= 0.004) return;
            using (var pen = new Pen(A(color, alpha), width))
                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        }

        /// <summary>虚线椭圆（轨道 / 星轨），可整体旋转。</summary>
        public static void DashedEllipse(Graphics g, float cx, float cy, float rx, float ry, Color color,
            float width, float dash, float gap, double alpha, float rotateDeg)
        {
            if (rx <= 0 || ry <= 0 || alpha <= 0.004) return;
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(rotateDeg);
            using (var pen = new Pen(A(color, alpha), width))
            {
                pen.DashStyle = DashStyle.Custom;
                pen.DashPattern = new float[] { Math.Max(0.05f, dash / width), Math.Max(0.05f, gap / width) };
                g.DrawEllipse(pen, -rx, -ry, rx * 2, ry * 2);
            }
            g.Restore(state);
        }

        /// <summary>折线星座。</summary>
        public static void Polyline(Graphics g, PointF[] points, Color color, float width, double alpha)
        {
            if (points == null || points.Length < 2 || alpha <= 0.004) return;
            using (var pen = new Pen(A(color, alpha), width))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLines(pen, points);
            }
        }

        /// <summary>带辉光的折线（霓虹灯管）：宽而淡打底，再叠细亮线。</summary>
        public static void GlowPolyline(Graphics g, PointF[] points, Color color, float width, double alpha, float glow)
        {
            if (points == null || points.Length < 2) return;
            for (int i = 3; i >= 1; i--)
                Polyline(g, points, color, width * (1 + glow * i * 0.55f), alpha * 0.16 / i * 2.4);
            Polyline(g, points, color, width, alpha);
        }

        /// <summary>带辉光的路径描边：pen 由调用方给颜色与线宽。</summary>
        public static void GlowPath(Graphics g, GraphicsPath path, Color color, float width, double alpha, int passes = 3)
        {
            if (path == null) return;
            for (int i = passes; i >= 1; i--)
            {
                using (var pen = new Pen(A(color, alpha * (0.10 + 0.06 * (passes - i))), width * (1 + i * 1.5f)))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawPath(pen, path);
                }
            }
            using (var pen = new Pen(A(color, alpha), width))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawPath(pen, path);
            }
        }

        // ---------------- 星 ----------------

        /// <summary>四角星（细长的闪光）。r 是尖到中心的距离。</summary>
        public static void Sparkle(Graphics g, float cx, float cy, float r, Color color, double alpha, float waist = 0.13f)
        {
            if (r <= 0 || alpha <= 0.004) return;
            float k = r * waist;
            using (var path = new GraphicsPath())
            {
                path.AddClosedCurve(new PointF[]
                {
                    new PointF(cx, cy - r), new PointF(cx + k, cy - k), new PointF(cx + r, cy),
                    new PointF(cx + k, cy + k), new PointF(cx, cy + r), new PointF(cx - k, cy + k),
                    new PointF(cx - r, cy), new PointF(cx - k, cy - k)
                }, 0.12f);
                using (var b = new SolidBrush(A(color, alpha))) g.FillPath(b, path);
            }
        }

        /// <summary>五角星。</summary>
        public static void Star(Graphics g, float cx, float cy, float r, Color color, double alpha, float rotateDeg = -90)
        {
            if (r <= 0 || alpha <= 0.004) return;
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double ang = (rotateDeg + i * 36) * Math.PI / 180.0;
                double rad = (i % 2 == 0) ? r : r * 0.42;
                pts[i] = new PointF(cx + (float)(Math.Cos(ang) * rad), cy + (float)(Math.Sin(ang) * rad));
            }
            using (var b = new SolidBrush(A(color, alpha))) g.FillPolygon(b, pts);
        }

        // ---------------- 花 / 叶 ----------------

        /// <summary>
        /// 一朵花：petals 片花瓣绕中心排一圈，每片是沿半径的椭圆。
        /// petalLen 从中心到花瓣尖，petalWid 花瓣最宽处。
        /// </summary>
        public static void Flower(Graphics g, float cx, float cy, int petals, float petalLen, float petalWid,
            float rotateDeg, Color petal, Color core, float coreR, double alpha)
        {
            if (petals <= 0 || petalLen <= 0) return;
            using (var b = new SolidBrush(A(petal, alpha)))
            {
                for (int i = 0; i < petals; i++)
                {
                    var state = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(rotateDeg + i * (360f / petals));
                    g.FillEllipse(b, -petalWid / 2f, -petalLen, petalWid, petalLen);
                    g.Restore(state);
                }
            }
            if (coreR > 0) Dot(g, cx, cy, coreR, core, alpha);
        }

        /// <summary>叶子：从 (x,y) 出发，朝 angle 方向长 len，最宽 wid。</summary>
        public static void Leaf(Graphics g, float x, float y, float len, float wid, float angle, Color color, double alpha)
        {
            if (len <= 0 || alpha <= 0.004) return;
            var state = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(angle);
            using (var path = new GraphicsPath())
            {
                path.AddBezier(new PointF(0, 0), new PointF(wid * 0.7f, -len * 0.28f), new PointF(wid * 0.5f, -len * 0.78f), new PointF(0, -len));
                path.AddBezier(new PointF(0, -len), new PointF(-wid * 0.5f, -len * 0.78f), new PointF(-wid * 0.7f, -len * 0.28f), new PointF(0, 0));
                using (var b = new SolidBrush(A(color, alpha))) g.FillPath(b, path);
            }
            g.Restore(state);
        }

        /// <summary>月牙：大圆挖掉一个偏移的小圆。</summary>
        public static void Crescent(Graphics g, float cx, float cy, float r, float cutDx, float cutDy, float cutR,
            Color color, double alpha)
        {
            if (r <= 0 || alpha <= 0.004) return;
            using (var outer = new GraphicsPath())
            {
                outer.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                using (var region = new Region(outer))
                {
                    using (var inner = new GraphicsPath())
                    {
                        inner.AddEllipse(cx + cutDx - cutR, cy + cutDy - cutR, cutR * 2, cutR * 2);
                        region.Exclude(inner);
                        using (var b = new SolidBrush(A(color, alpha))) g.FillRegion(b, region);
                    }
                }
            }
        }

        /// <summary>爱心（07 像素版也用它，只是画成方块）。</summary>
        public static void Heart(Graphics g, float cx, float cy, float size, Color color, double alpha)
        {
            if (size <= 0 || alpha <= 0.004) return;
            using (var path = new GraphicsPath())
            {
                float s = size;
                path.AddBezier(cx, cy + s * 0.75f, cx - s * 1.15f, cy - s * 0.1f, cx - s * 0.55f, cy - s * 0.95f, cx, cy - s * 0.38f);
                path.AddBezier(cx, cy - s * 0.38f, cx + s * 0.55f, cy - s * 0.95f, cx + s * 1.15f, cy - s * 0.1f, cx, cy + s * 0.75f);
                using (var b = new SolidBrush(A(color, alpha))) g.FillPath(b, path);
            }
        }

        // ---------------- 像素 / 方块 ----------------

        /// <summary>像素块：整数对齐、关抗锯齿，保证边缘是硬的。</summary>
        public static void Pixel(Graphics g, float x, float y, float w, float h, Color color, double alpha)
        {
            if (w <= 0 || h <= 0 || alpha <= 0.004) return;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            int px = (int)Math.Round(x), py = (int)Math.Round(y);
            int pw = Math.Max(1, (int)Math.Round(w)), ph = Math.Max(1, (int)Math.Round(h));
            using (var b = new SolidBrush(A(color, alpha))) g.FillRectangle(b, px, py, pw, ph);
            g.SmoothingMode = old;
        }

        /// <summary>由一个字符网格画出点阵字（07 用）：rows 里 '#' = 填充，'o' = 描边色。</summary>
        public static void Bitmap(string[] rows, Graphics g, float x, float y, float cell, Color fill, Color edge,
            double alpha, float gap = 0)
        {
            if (rows == null) return;
            for (int r = 0; r < rows.Length; r++)
            {
                string line = rows[r];
                for (int c = 0; c < line.Length; c++)
                {
                    char ch = line[c];
                    if (ch == ' ') continue;
                    Color color = ch == '#' ? fill : (ch == 'o' ? edge : fill);
                    Pixel(g, x + c * (cell + gap), y + r * (cell + gap), cell, cell, color, alpha);
                }
            }
        }

        // ---------------- 文本 ----------------

        private static readonly StringFormat Typo = CreateTypo();
        private static StringFormat CreateTypo()
        {
            var sf = StringFormat.GenericTypographic.Clone() as StringFormat;
            sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            sf.Alignment = StringAlignment.Near;
            sf.LineAlignment = StringAlignment.Near;
            return sf;
        }

        private static readonly Dictionary<string, float> measureCache = new Dictionary<string, float>();

        private static string FontKey(Font f)
        {
            return f.Name + "|" + f.SizeInPoints.ToString("0.##") + "|" + (int)f.Style;
        }

        /// <summary>单个字符的步进宽度（像素）。</summary>
        public static float CharWidth(Graphics g, char ch, Font font)
        {
            string key = FontKey(font) + "|" + ((int)ch);
            float w;
            if (measureCache.TryGetValue(key, out w)) return w;
            try { w = g.MeasureString(ch.ToString(), font, PointF.Empty, Typo).Width; }
            catch { w = font.Size; }
            if (measureCache.Count > 2000) measureCache.Clear();
            measureCache[key] = w;
            return w;
        }

        /// <summary>整行的步进宽度（含字距）。</summary>
        public static float TextWidth(Graphics g, string text, Font font, float spacing)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            float w = 0;
            for (int i = 0; i < text.Length; i++)
            {
                w += CharWidth(g, text[i], font);
                if (i < text.Length - 1) w += spacing;
            }
            return w;
        }

        /// <summary>
        /// 逐字画一行（字距、逐字位移都能控）。x 是行首，y 是行顶。
        /// brushAt: 第 i 个字用什么刷子；dyAt: 第 i 个字的竖向偏移（像素）。
        /// </summary>
        public static void Chars(Graphics g, string text, Font font, float x, float y, float spacing,
            Func<int, Brush> brushAt, Func<int, float> dyAt = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            float cx = x;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                float adv = CharWidth(g, ch, font);
                if (ch != ' ')
                {
                    Brush brush = brushAt == null ? Brushes.White : brushAt(i);
                    float dy = dyAt == null ? 0 : dyAt(i);
                    // 刷子归调用方所有：这里只画，不释放
                    if (brush != null) g.DrawString(ch.ToString(), font, brush, new PointF(cx, y + dy), Typo);
                }
                cx += adv + spacing;
            }
        }

        /// <summary>居中画一行纯色字。</summary>
        public static void TextCenter(Graphics g, string text, Font font, float cx, float y, Color color,
            double alpha, float spacing = 0)
        {
            if (string.IsNullOrEmpty(text) || alpha <= 0.004) return;
            float w = TextWidth(g, text, font, spacing);
            using (var b = new SolidBrush(A(color, alpha)))
                Chars(g, text, font, cx - w / 2f, y, spacing, delegate { return (Brush)b; });
        }

        /// <summary>居中画一行渐变字（金箔字 / 粉青字）。</summary>
        public static void TextGradient(Graphics g, string text, Font font, float cx, float y, Color[] stops,
            double alpha, float spacing = 0, float angleDeg = 0f, float dy = 0f)
        {
            if (string.IsNullOrEmpty(text) || alpha <= 0.004) return;
            float w = TextWidth(g, text, font, spacing);
            var box = new RectangleF(cx - w / 2f, y + dy - font.Size, Math.Max(1, w), font.Size * 2.4f);
            using (var brush = new LinearGradientBrush(box, Color.Empty, Color.Empty, angleDeg))
            {
                var blend = new ColorBlend();
                blend.Colors = new Color[stops.Length];
                blend.Positions = new float[stops.Length];
                for (int i = 0; i < stops.Length; i++)
                {
                    blend.Colors[i] = A(stops[i], alpha);
                    blend.Positions[i] = stops.Length == 1 ? 0f : i / (float)(stops.Length - 1);
                }
                brush.InterpolationColors = blend;
                Chars(g, text, font, cx - w / 2f, y, spacing, delegate { return (Brush)brush; });
            }
        }

        /// <summary>
        /// 带辉光的字：先把同一行字往四边偏移画几层淡色（假模糊），再压一行实字。
        /// 霓虹（10）和星屑标题用。
        /// </summary>
        public static void TextGlow(Graphics g, string text, Font font, float cx, float y, Color ink, Color glow,
            double alpha, float spacing = 0, float radius = 3f, int passes = 2)
        {
            if (string.IsNullOrEmpty(text) || alpha <= 0.004) return;
            float w = TextWidth(g, text, font, spacing);
            float x = cx - w / 2f;
            float[] dx = new float[] { -1, 0, 1, 0, -0.7f, 0.7f, -0.7f, 0.7f };
            float[] dyy = new float[] { 0, -1, 0, 1, -0.7f, -0.7f, 0.7f, 0.7f };
            for (int p = passes; p >= 1; p--)
            {
                using (var b = new SolidBrush(A(glow, alpha * (0.16 / p))))
                {
                    for (int i = 0; i < dx.Length; i++)
                    {
                        float ox = dx[i] * radius * p, oy = dyy[i] * radius * p;
                        Chars(g, text, font, x + ox, y + oy, spacing, delegate { return (Brush)b; });
                    }
                }
            }
            using (var b = new SolidBrush(A(ink, alpha)))
                Chars(g, text, font, x, y, spacing, delegate { return (Brush)b; });
        }

        /// <summary>把一段文字画进矩形（左/中/右由 align 决定），返回实际高度。</summary>
        public static float TextBlock(Graphics g, string text, Font font, RectangleF rect, Color color, double alpha,
            StringAlignment align, StringAlignment lineAlign = StringAlignment.Center, float spacing = 0)
        {
            if (string.IsNullOrEmpty(text) || alpha <= 0.004) return 0;
            using (var sf = new StringFormat())
            using (var b = new SolidBrush(A(color, alpha)))
            {
                sf.Alignment = align;
                sf.LineAlignment = lineAlign;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, font, b, rect, sf);
            }
            return rect.Height;
        }

        // ---------------- 其它 ----------------

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>斜条纹（08 的警示条）。</summary>
        public static void Stripes(Graphics g, RectangleF r, Color color, float width, float gap, double alpha, float angleDeg = 45)
        {
            if (r.Width <= 0 || r.Height <= 0 || alpha <= 0.004) return;
            var state = g.Save();
            using (var clip = RoundRect(r, 0))
            {
                g.SetClip(clip, CombineMode.Intersect);
                g.RotateTransform(angleDeg);
                using (var pen = new Pen(A(color, alpha), width))
                {
                    float span = (float)Math.Sqrt(r.Width * r.Width + r.Height * r.Height) * 1.2f;
                    for (float x = -span; x < span; x += width + gap)
                        g.DrawLine(pen, x, -span, x, span);
                }
            }
            g.Restore(state);
        }

        /// <summary>一排金色小点（05 的进度终点装饰、06 的缝线）。</summary>
        public static void Dots(Graphics g, float x0, float y0, float x1, float y1, int count, float r, Color color, double alpha)
        {
            for (int i = 0; i < count; i++)
            {
                float p = count <= 1 ? 0 : i / (float)(count - 1);
                Dot(g, x0 + (x1 - x0) * p, y0 + (y1 - y0) * p, r, color, alpha);
            }
        }
    }
}
