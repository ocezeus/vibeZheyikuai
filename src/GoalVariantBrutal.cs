using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace WorkCountdown
{
    /// <summary>
    /// 08 · 新粗野 Neo-Brutalist（design/card-spread-notify-v4.html 的 .s8）。
    /// 高饱和黄底 + 硬边黑 + 红蓝几何：黄底自下而上 clip 揭示、粗黑标题逐字砸落、
    /// 红底黑边副标题条、45° 斜纹带、出框红圆/红条/黄环/黄点/蓝点。
    /// 画面全硬边（SmoothingMode.None、矩形取整），只有文字走抗锯齿。
    /// </summary>
    internal sealed class GoalVariantBrutal : IGoalVariant
    {
        public string id { get { return "08"; } }
        public string name { get { return "新粗野 Neo-Brutalist"; } }
        public bool hasOutside { get { return true; } }

        // ---------------------------------------------------------------- 配色

        private static readonly Color Yellow = GoalPaint.Hex("#FFD43B");   // 黄底 / 斜纹黄条 / 黄环黄点
        private static readonly Color Ink = GoalPaint.Hex("#111111");      // 硬边黑：边框 / 字 / 点阵
        private static readonly Color Blue = GoalPaint.Hex("#2F5BFF");     // 蓝半圆 / 蓝点
        private static readonly Color Red = GoalPaint.Hex("#FF4B3E");      // 红星 / 副标题条 / 出框红圆红条
        private static readonly Color SubInk = GoalPaint.Hex("#FFF7E8");   // 副标题字

        // ---------------------------------------------------------------- 设计常量（DIP，卡面 280×188）

        private const float TitleSize = 39f;        // 标题字号（不压缩字号，只压字形）
        private const float TitleTrack = -0.03f;    // 字距 −.03em
        private const float TitleSqueeze = 0.86f;   // 没有 Bahnschrift Condensed：用变换矩阵横压
        private const float TitleWeight = 0.055f;   // 用描边加粗到 900 的观感（em 比例）
        private const float ShadowInk = 6f;         // 黑硬影 (+6,+6)
        private const float ShadowWhite = 2f;       // 白硬影 (+2,+2)

        private const float SubSize = 14.5f;        // 副标题字号
        private const float SubTrack = 0.14f;       // 字距 .14em
        private const float BarPadX = 11f;          // 内距 11
        private const float BarPadY = 4f;           // 内距 4
        private const float BarBorder = 2f;         // 2px 黑边
        private const float BarLine = 1.2f;         // 行高（em 倍数）

        private const float BandH = 28f;            // 斜纹带高
        private const float BandStripe = 8f;        // 斜纹 8px 黑 / 8px 黄
        private const float FrameInset = 6f;        // 内框内缩
        private const float FrameW = 3f;            // 内框线宽
        private const float FrameR = 13f;           // 内框圆角

        private const float CollapsedBandH = 14f;   // 收起卡斜纹带高
        private const float CollapsedFrameInset = 4f;
        private const float CollapsedFrameW = 2f;

        // ---------------------------------------------------------------- 时间轴（秒，卡面 7s 循环里的绝对秒）

        private const double BgT0 = 0.21, BgDur = 0.49;      // 黄底自下而上揭示 3%→10%
        private const double ShowT0 = 0.49, ShowDur = 0.35;  // s8-show 7%→12%
        private const double BandT0 = 0.28, BandDur = 0.56;  // 斜纹带 scaleX 4%→12%
        private const double TextT0 = 0.91, TextDur = 0.21;  // 文字层 13%→16%
        private const double CharT0 = 0.90, CharStep = 0.045, CharDur = 0.40;
        private const double SubT0 = 1.34, SubDur = 0.38;
        private const double MarkT0 = 1.50, MarkDur = 0.32;
        private const double DomeT0 = 1.18, StarT0 = 1.30, ShapeDur = 0.42;

        // 震屏：13%–22%（0.91–1.54s）里的 5 个离散位移，查表，不连续抖动
        private static readonly float[] ShakeXs = new float[] { 0f, -2f, 2f, -1f, 0f };
        private static readonly float[] ShakeYs = new float[] { 0f, 1f, -1f, 0f, 0f };
        private const double ShakeT0 = 1.26, ShakeStep = 0.07;

        // 十角星 polygon（.s8 .star 的 clip-path，单位：自身盒子的 0..1）
        private static readonly float[] StarPts = new float[]
        {
            0.50f, 0.00f, 0.61f, 0.35f, 0.98f, 0.35f, 0.68f, 0.57f, 0.79f, 0.93f,
            0.50f, 0.70f, 0.21f, 0.93f, 0.32f, 0.57f, 0.02f, 0.35f, 0.39f, 0.35f
        };

        // ---------------------------------------------------------------- 卡内

        public void PaintInside(GoalScene s)
        {
            Graphics g = s.g;
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            double fade = s.quiet ? 1.0 : (1.0 - s.Out);
            if (fade <= 0.002) { g.Restore(st); return; }

            if (s.collapsed) PaintCollapsed(s, fade);
            else PaintExpanded(s, fade);

            g.Restore(st);
        }

        private void PaintExpanded(GoalScene s, double fade)
        {
            Graphics g = s.g;
            float k = s.scale;

            // 1. 黄底 + 点阵（自下而上 clip）
            PaintBackground(s, fade);

            // 2. 蓝半圆 / 红星（s8-show 7%→12%，各自 1.18s / 1.30s 弹入）
            double shapesA = (s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(ShowT0, ShowDur))) * fade;
            if (shapesA > 0.004)
            {
                double dp = s.quiet ? 1.0 : s.Ease(DomeT0, ShapeDur);
                if (dp > 0)
                {
                    float ds = (float)PopScale(dp);
                    PaintDome(g, 14f * k, s.H - 28f * k - 58f * k, 58f * k,
                        -18f + PopTilt(dp), ds, shapesA);
                }
                double sp = s.quiet ? 1.0 : s.Ease(StarT0, ShapeDur);
                if (sp > 0)
                {
                    float ss = (float)PopScale(sp);
                    PaintStar(g, s.W - 28f * k - 27f * k, s.H - 16f * k - 27f * k, 27f * k,
                        12f + PopTilt(sp), ss, shapesA);
                }
            }

            // 3. 斜纹带（压住星的下缘，和设计稿 z-index 一致）
            PaintBand(s, s.H - BandH * k, BandH * k, fade);

            // 4. 文字层（含整层震屏）
            PaintTitleBlock(s, fade);

            // 5. 对勾方框
            PaintMark(s, fade);

            // 6. 最上层黑框
            PaintFrame(s, fade, FrameInset * k, FrameW * k, FrameR * k);
        }

        /// <summary>黄底：clip 自下而上升起，上面叠 7px 网格的 1px 黑点（α13%）。</summary>
        private static void PaintBackground(GoalScene s, double fade)
        {
            Graphics g = s.g;
            float k = s.scale;
            double p = s.quiet ? 1.0 : s.Ease(BgT0, BgDur);
            if (p <= 0.001) return;
            double reveal = 1.0 - GoalScene.Smooth(p);
            float top = (float)Math.Round(s.H * reveal);
            if (top >= s.H) return;

            // opacity：3% 之前 0、5% 到 1
            double a = fade * (s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(BgT0, 0.14)));
            if (a <= 0.004) return;

            GraphicsState st = g.Save();
            g.SetClip(new RectangleF(0, top, s.W, s.H - top), CombineMode.Intersect);
            using (var b = new SolidBrush(GoalPaint.A(Yellow, a))) g.FillRectangle(b, 0, 0, s.W, s.H);

            float pitch = 7f * k;
            float dot = Math.Max(1f, (float)Math.Round(1f * k));
            using (var b = new SolidBrush(GoalPaint.A(Ink, 0.13 * a)))
            {
                for (float y = 0; y < s.H; y += pitch)
                {
                    int iy = (int)Math.Round(y);
                    for (float x = 0; x < s.W; x += pitch)
                        g.FillRectangle(b, (float)Math.Round(x), iy, dot, dot);
                }
            }
            g.Restore(st);
        }

        /// <summary>蓝半圆：58×58，圆角 50% 50% 0 0（上半圆 + 下方直边），2px 黑边，−18°。</summary>
        private static void PaintDome(Graphics g, float x, float y, float size, float rot, float scale, double alpha)
        {
            if (scale <= 0.001 || alpha <= 0.004) return;
            float cx = x + size / 2f, cy = y + size / 2f;
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(rot);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-size / 2f, -size / 2f);

            float r = size / 2f;
            float border = 2f * size / 58f;          // 2 DIP 的黑边（随尺寸换算）
            using (var path = new GraphicsPath())
            {
                path.AddArc(0, 0, size, size, 180, 90);   // 左上角（r = 半宽）
                path.AddArc(0, 0, size, size, 270, 90);   // 右上角
                path.AddLine(size, r, size, size);
                path.AddLine(size, size, 0, size);
                path.CloseFigure();
                using (var b = new SolidBrush(GoalPaint.A(Ink, alpha))) g.FillPath(b, path);
            }
            // 内缩 border 的蓝色面：保持硬边，黑边只剩外侧一圈
            float isz = size - border * 2f;
            float ir = isz / 2f;
            using (var path = new GraphicsPath())
            {
                path.AddArc(border, border, isz, isz, 180, 90);
                path.AddArc(border, border, isz, isz, 270, 90);
                path.AddLine(border + isz, border + ir, border + isz, size);
                path.AddLine(border + isz, size, border, size);
                path.CloseFigure();
                using (var b = new SolidBrush(GoalPaint.A(Blue, alpha))) g.FillPath(b, path);
            }
            g.Restore(st);
        }

        /// <summary>红十角星：polygon 取自设计稿 clip-path，无描边。</summary>
        private static void PaintStar(Graphics g, float cx, float cy, float half, float rot, float scale, double alpha)
        {
            if (half <= 0 || scale <= 0.001 || alpha <= 0.004) return;
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(rot);
            g.ScaleTransform(scale, scale);
            var pts = new PointF[StarPts.Length / 2];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new PointF((StarPts[i * 2] - 0.5f) * half * 2f, (StarPts[i * 2 + 1] - 0.5f) * half * 2f);
            using (var b = new SolidBrush(GoalPaint.A(Red, alpha))) g.FillPolygon(b, pts);
            g.Restore(st);
        }

        /// <summary>底部斜纹带：左锚 scaleX 0→1，45° 黑黄 8/8，上边 2px 黑。</summary>
        private static void PaintBand(GoalScene s, float y, float h, double alpha)
        {
            if (alpha <= 0.004 || h <= 0) return;
            Graphics g = s.g;
            float k = s.scale;
            double p = s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(BandT0, BandDur));
            float w = s.W * (float)p;
            if (w < 1f) return;

            GraphicsState st = g.Save();
            g.SetClip(new RectangleF(0, y, w, h), CombineMode.Intersect);
            using (var b = new SolidBrush(GoalPaint.A(Yellow, alpha))) g.FillRectangle(b, 0, y, s.W, h);
            PaintStripes(g, new RectangleF(0, y, s.W, h), BandStripe * k, BandStripe * k, alpha);
            using (var b = new SolidBrush(GoalPaint.A(Ink, alpha))) g.FillRectangle(b, 0, y, s.W, 2f * k);
            g.Restore(st);
        }

        /// <summary>
        /// 45° 斜纹：设计稿是 repeating-linear-gradient(45deg,#111 0 8px,#FFD43B 8px 16px)。
        /// 黑条 8px、周期 16px 都按「垂直于条纹」量，所以旋转坐标系里沿 x 的步进就是 8+8。
        /// </summary>
        private static void PaintStripes(Graphics g, RectangleF r, float width, float gap, double alpha)
        {
            if (width <= 0f || alpha <= 0.004f) return;
            GraphicsState st = g.Save();
            using (var clip = new GraphicsPath())
            {
                clip.AddRectangle(r);
                g.SetClip(clip, CombineMode.Intersect);
            }
            g.RotateTransform(45f);
            float step = width + gap;
            float span = (float)Math.Sqrt(r.Width * r.Width + r.Height * r.Height) * 1.4f;
            using (var pen = new Pen(GoalPaint.A(Ink, alpha), width))
            {
                pen.LineJoin = LineJoin.Miter;
                for (float x = -span; x < span; x += step)
                    g.DrawLine(pen, x, -span, x, span);
            }
            g.Restore(st);
        }

        /// <summary>黑内框：inset 6、3px、圆角 13（描边向内画，占 6..9）。</summary>
        private static void PaintFrame(GoalScene s, double fade, float inset, float width, float radius)
        {
            Graphics g = s.g;
            double a = (s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(ShowT0, ShowDur))) * fade;
            if (a <= 0.004) return;
            // Pen 以路径为中心，所以路径要再往里挪半个线宽，边框才落在 inset..inset+width
            float edge = inset + width / 2f;
            var r = new RectangleF(edge, edge, s.W - edge * 2f, s.H - edge * 2f);
            if (r.Width <= 2f || r.Height <= 2f) return;
            using (var path = GoalPaint.RoundRect(r, Math.Max(1f, radius - width / 2f)))
            using (var pen = new Pen(GoalPaint.A(Ink, a), width))
                g.DrawPath(pen, path);
        }

        /// <summary>文字层：标题逐字砸落 + 红底黑边副标题条，整层吃震屏位移。</summary>
        private static void PaintTitleBlock(GoalScene s, double fade)
        {
            double ta = (s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(TextT0, TextDur))) * fade;
            if (ta <= 0.004) return;
            float k = s.scale;
            float dx = 0f, dy = 0f;
            if (!s.quiet) ShakeAt(s.t, out dx, out dy);

            // 标题墨迹中心：卡面中线往上 24（188 卡高时 = 70），副标题条中心往下 23.5（=117.5）
            PaintTitle(s, "今日目标达成", TitleSize, s.CX, s.H / 2f - 24f * k,
                TitleSqueeze, ShadowInk * k, ShadowWhite * k, dx * k, dy * k, ta, true);
            PaintSubBar(s, s.H / 2f + 23.5f * k, fade, dx * k, dy * k);
        }

        /// <summary>
        /// 画一行硬影粗字：每个字三遍——先 (+6,+6) 黑 α92%，再 (+2,+2) 白，最后本体。
        /// 字形用 GraphicsPath 取墨迹（横向按墨迹居中、纵向保留同一基线），所以逐字
        /// scale/rotate 都绕自己的中心，横压 0.86 只走变换矩阵、不动字号。
        /// </summary>
        private static void PaintTitle(GoalScene s, string text, float size, float cx, float cy, float squeeze,
            float shInk, float shWhite, float shakeX, float shakeY, double alpha, bool animate)
        {
            Graphics g = s.g;
            Font font = s.Sans(size, FontStyle.Bold);
            float em = font.Size * g.DpiY / 72f;      // 该字体真实渲染的 em（像素）
            float space = TitleTrack * em;
            float penW = TitleWeight * 100f;           // 字形路径的单位是「em = 100」
            float inkCenter = InkCenterY(font, text, em);   // 整行墨迹中心相对基线的偏移（像素）

            int n = text.Length;
            var adv = new float[n];
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                adv[i] = GoalPaint.CharWidth(g, text[i], font);
                total += adv[i];
                if (i < n - 1) total += space;
            }

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cursor = -total / 2f;
            for (int i = 0; i < n; i++)
            {
                float glyphCx = cx + (cursor + adv[i] / 2f) * squeeze + shakeX;
                cursor += adv[i] + space;

                double p = 1.0;
                if (animate && !s.quiet) p = s.Ease(CharT0 + i * CharStep, CharDur);
                if (p <= 0.0) continue;

                double e = BackOut(p);
                float sc = 1.25f + (1f - 1.25f) * (float)e;      // 1.25 → 1
                float rot = -4f + 4f * (float)e;                 // −4° → 0
                float dy = -18f * s.scale * (float)(1.0 - e);    // −18 → 0
                double charA = alpha * GoalScene.Clamp01(e);
                GraphicsPath glyph = Glyph(font, text[i]);

                // 三遍：黑硬影(+6,+6) → 白硬影(+2,+2) → 本体；偏移在屏幕坐标里给，不吃横压
                for (int layer = 0; layer < 3; layer++)
                {
                    float ox = layer == 0 ? shInk : (layer == 1 ? shWhite : 0f);
                    float oy = layer == 0 ? shInk : (layer == 1 ? shWhite : 0f);
                    GraphicsState cs = g.Save();
                    g.TranslateTransform(glyphCx + ox, cy + dy + shakeY + oy);
                    g.RotateTransform(rot);
                    g.ScaleTransform(sc * squeeze * em / 100f, sc * em / 100f);
                    g.TranslateTransform(0f, -inkCenter * 100f / em);
                    if (layer == 0)
                        FillGlyph(g, glyph, Ink, charA * 0.92, penW);
                    else if (layer == 1)
                        FillGlyph(g, glyph, Color.White, charA, penW);
                    else
                        FillGlyph(g, glyph, Ink, charA, penW);
                    g.Restore(cs);
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>副标题条：#FF4B3E 底 + 2px #111 边，−1.6°，scaleX 0→1（绕中心）。</summary>
        private static void PaintSubBar(GoalScene s, float cy, double fade, float shakeX, float shakeY)
        {
            Graphics g = s.g;
            float k = s.scale;
            Font font = s.Sans(SubSize, FontStyle.Bold);
            float em = font.Size * g.DpiY / 72f;
            float space = SubTrack * em;
            string text = "工作辛苦了";
            int n = text.Length;
            var adv = new float[n];
            float textW = 0f;
            for (int i = 0; i < n; i++)
            {
                adv[i] = GoalPaint.CharWidth(g, text[i], font);
                textW += adv[i];
                if (i < n - 1) textW += space;
            }
            float barW = textW + (BarPadX * 2f + BarBorder * 2f) * k;
            float barH = em * BarLine + (BarPadY * 2f + BarBorder * 2f) * k;
            float inkCenter = InkCenterY(font, text, em);

            double p = s.quiet ? 1.0 : s.Ease(SubT0, SubDur);
            double e = BackOut(p);
            if (e <= 0.001) return;
            float sx = (float)e;
            double a = fade * GoalScene.Clamp01(p * 2.0);

            GraphicsState st = g.Save();
            g.TranslateTransform(s.CX + shakeX, cy + shakeY);
            g.RotateTransform(-1.6f);
            g.ScaleTransform(sx, 1f);

            float hw = barW / 2f, hh = barH / 2f;
            HardRect(g, -hw, -hh, barW, barH, Ink, a);
            HardRect(g, -hw + BarBorder * k, -hh + BarBorder * k,
                barW - BarBorder * 2f * k, barH - BarBorder * 2f * k, Red, a);

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cursor = -textW / 2f;
            for (int i = 0; i < n; i++)
            {
                GraphicsState cs = g.Save();
                g.TranslateTransform(cursor + adv[i] / 2f, -inkCenter);
                g.ScaleTransform(em / 100f, em / 100f);
                FillGlyph(g, Glyph(font, text[i]), SubInk, a, 0f);
                g.Restore(cs);
                cursor += adv[i] + space;
            }
            g.SmoothingMode = old;
            g.Restore(st);
        }

        /// <summary>对勾方框：30×30、左 20 顶 16、8°、黄底 2px 黑边，1.50s 弹入。</summary>
        private static void PaintMark(GoalScene s, double fade)
        {
            Graphics g = s.g;
            float k = s.scale;
            double p = s.quiet ? 1.0 : s.Ease(MarkT0, MarkDur);
            if (p <= 0.0) return;
            double e = BackOut(p);
            if (e <= 0.001) return;

            GraphicsState st = g.Save();
            g.TranslateTransform((20f + 15f) * k, (16f + 15f) * k);
            g.RotateTransform(8f);
            g.ScaleTransform((float)e, (float)e);
            float half = 15f * k;
            HardRect(g, -half, -half, 30f * k, 30f * k, Ink, fade);
            HardRect(g, -half + 2f * k, -half + 2f * k, 26f * k, 26f * k, Yellow, fade);

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(GoalPaint.A(Ink, fade), 2.6f * k))
            {
                pen.LineJoin = LineJoin.Miter;
                pen.StartCap = LineCap.Flat;
                pen.EndCap = LineCap.Flat;
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(-6.5f * k, -0.5f * k),
                    new PointF(-1.5f * k, 5.5f * k),
                    new PointF(7.5f * k, -6.5f * k)
                });
            }
            g.SmoothingMode = old;
            g.Restore(st);
        }

        // ---------------------------------------------------------------- 收起卡（224×68）

        private void PaintCollapsed(GoalScene s, double fade)
        {
            Graphics g = s.g;
            float k = s.scale;
            float W = s.W, H = s.H;

            PaintBackground(s, fade);

            // 红星缩到 20×20，贴在斜纹带上沿
            double shapesA = (s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(0.55, 0.35))) * fade;
            if (shapesA > 0.004)
            {
                double sp = s.quiet ? 1.0 : s.Ease(DomeT0, ShapeDur);
                if (sp > 0)
                    PaintStar(g, W - 8f * k - 10f * k, H - CollapsedBandH * k - 10f * k - 2f * k,
                        10f * k, 12f, (float)PopScale(sp), shapesA);
            }

            PaintBand(s, H - CollapsedBandH * k, CollapsedBandH * k, fade);

            // 标题 23 DIP 窄体 900（硬影按字号等比缩）
            double ta = (s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(0.5, 0.35))) * fade;
            float cy = H / 2f - 4f * k;
            PaintTitle(s, "今日目标达成", 23f, s.CX, cy, TitleSqueeze,
                3.5f * k, 1.2f * k, 0f, 0f, ta, true);

            // 副标题只留一个 8px 红点
            double dotA = (s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(0.8, 0.3))) * fade;
            if (dotA > 0.004) HardDisc(g, s.CX, H / 2f + 13f * k, 4f * k, Red, Ink, 0f, dotA);

            PaintFrame(s, fade, CollapsedFrameInset * k, CollapsedFrameW * k, 10f * k);
        }

        // ---------------------------------------------------------------- 卡外

        public void PaintOutside(GoalScene s)
        {
            Graphics g = s.g;
            double a = s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(0.56, 0.56)) * (1.0 - s.Out);
            if (a <= 0.004) return;

            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            float k = s.scale;
            // 收起卡：出框坐标按卡面比例压一点，让 5 个几何还贴着卡
            float kx = s.collapsed ? s.W / (280f * k) : 1f;
            float ky = s.collapsed ? s.H / (188f * k) : 1f;
            float kr = (kx + ky) * 0.5f;

            // 红圆 (342,−2) r50，4px 黑边
            HardDisc(g, 342f * k * kx, -2f * k * ky, 50f * k * kr, Red, Ink, 4f * k * kr, a);

            // 红条 (−100,150,130,16)，绕中心 (−35,158) 转 −17°，3px 黑边
            float barK = s.collapsed ? 0.8f : 1f;
            float bx = -35f * k * kx, by = 158f * k * ky;
            GraphicsState bs = g.Save();
            g.TranslateTransform(bx, by);
            g.RotateTransform(-17f);
            g.ScaleTransform(barK, barK);
            HardRect(g, -65f * k, -8f * k, 130f * k, 16f * k, Ink, a);
            HardRect(g, -62f * k, -5f * k, 124f * k, 10f * k, Red, a);
            g.Restore(bs);

            // 黄环 (336,250) r30 + 5px
            using (var pen = new Pen(GoalPaint.A(Yellow, a), 5f * k * kr))
                g.DrawEllipse(pen, 336f * k * kx - 30f * k * kr, 250f * k * ky - 30f * k * kr,
                    60f * k * kr, 60f * k * kr);

            // 黄点 (−70,30) r10 + 3px；蓝点 (382,120) r14 + 3px
            HardDisc(g, -70f * k * kx, 30f * k * ky, 10f * k * kr, Yellow, Ink, 3f * k * kr, a);
            HardDisc(g, 382f * k * kx, 120f * k * ky, 14f * k * kr, Blue, Ink, 3f * k * kr, a);

            g.Restore(st);
        }

        // ---------------------------------------------------------------- 小工具

        /// <summary>硬边矩形（取整）。</summary>
        private static void HardRect(Graphics g, float x, float y, float w, float h, Color color, double alpha)
        {
            if (w <= 0f || h <= 0f || alpha <= 0.004) return;
            int px = (int)Math.Round(x), py = (int)Math.Round(y);
            int pw = Math.Max(1, (int)Math.Round(w)), ph = Math.Max(1, (int)Math.Round(h));
            using (var b = new SolidBrush(GoalPaint.A(color, alpha))) g.FillRectangle(b, px, py, pw, ph);
        }

        /// <summary>硬边圆点：外圈描边色 + 内圈填充色（都取整）。</summary>
        private static void HardDisc(Graphics g, float cx, float cy, float r, Color fill, Color stroke,
            float strokeW, double alpha)
        {
            if (r <= 0.5f || alpha <= 0.004) return;
            if (strokeW > 0f)
                using (var b = new SolidBrush(GoalPaint.A(stroke, alpha)))
                    g.FillEllipse(b, cx - r, cy - r, r * 2f, r * 2f);
            float ri = Math.Max(0.5f, r - strokeW);
            using (var b = new SolidBrush(GoalPaint.A(fill, alpha)))
                g.FillEllipse(b, cx - ri, cy - ri, ri * 2f, ri * 2f);
        }

        /// <summary>填充字形；penW &gt; 0 时再描一圈把笔画加粗到 900 的观感。</summary>
        private static void FillGlyph(Graphics g, GraphicsPath path, Color color, double alpha, float penW)
        {
            if (alpha <= 0.004 || path == null) return;
            using (var b = new SolidBrush(GoalPaint.A(color, alpha))) g.FillPath(b, path);
            if (penW > 0f)
            {
                using (var p = new Pen(GoalPaint.A(color, alpha), penW))
                {
                    p.LineJoin = LineJoin.Miter;
                    p.StartCap = LineCap.Flat;
                    p.EndCap = LineCap.Flat;
                    g.DrawPath(p, path);
                }
            }
        }

        private static readonly Dictionary<string, GraphicsPath> glyphCache =
            new Dictionary<string, GraphicsPath>();

        /// <summary>
        /// 取字的墨迹路径（em = 100 单位，横向已平移到墨迹中心，纵向保留同一基线原点），
        /// 按 字体族 + 字重 + 字 缓存（与字号无关：路径本身按 em = 100 生成）。
        /// </summary>
        private static GraphicsPath Glyph(Font font, char ch)
        {
            string key = font.FontFamily.Name + "|" + ((int)font.Style).ToString() + "|" + ((int)ch).ToString();
            GraphicsPath path;
            if (glyphCache.TryGetValue(key, out path) && path != null) return path;

            path = null;
            FontStyle style = font.Style;
            for (int attempt = 0; attempt < 2 && path == null; attempt++)
            {
                try
                {
                    var p = new GraphicsPath();
                    p.AddString(ch.ToString(), font.FontFamily, (int)style, 100f, PointF.Empty,
                        StringFormat.GenericTypographic);
                    RectangleF b = p.GetBounds();
                    if (b.Width <= 0f || b.Height <= 0f) { p.Dispose(); break; }
                    using (var m = new Matrix())
                    {
                        m.Translate(-(b.X + b.Width / 2f), 0f);
                        p.Transform(m);
                    }
                    path = p;
                }
                catch
                {
                    style = FontStyle.Regular;
                }
            }
            if (path == null) path = new GraphicsPath();
            glyphCache[key] = path;
            return path;
        }

        /// <summary>整行墨迹中心相对字形原点的纵向偏移（像素），用来把一行字在光学上居中。</summary>
        private static float InkCenterY(Font font, string text, float em)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < text.Length; i++)
            {
                RectangleF b = Glyph(font, text[i]).GetBounds();
                if (b.Height <= 0f) continue;
                if (b.Y < lo) lo = b.Y;
                if (b.Bottom > hi) hi = b.Bottom;
            }
            if (lo >= hi) return 0f;
            return (lo + hi) * 0.5f * em / 100f;
        }

        /// <summary>0→1 的过冲缓动（回弹约 10%），对应 cubic-bezier(.2,1.5~1.6,.3,1)。</summary>
        private static double BackOut(double p)
        {
            p = GoalScene.Clamp01(p);
            double c1 = 1.70158;
            double c3 = c1 + 1.0;
            double q = p - 1.0;
            return 1.0 + c3 * q * q * q + c1 * q * q;
        }

        /// <summary>s8-pop：0 → 1.12（70%）→ 1.0。</summary>
        private static double PopScale(double p)
        {
            p = GoalScene.Clamp01(p);
            if (p < 0.7)
            {
                double u = p / 0.7;
                return 1.12 * (1.0 - Math.Pow(1.0 - u, 3.0));
            }
            double v = GoalScene.Smooth((p - 0.7) / 0.3);
            return 1.12 - 0.12 * v;
        }

        /// <summary>s8-pop 的 +4° 角度过冲（70% 时 +4°，100% 归位）。</summary>
        private static float PopTilt(double p)
        {
            p = GoalScene.Clamp01(p);
            if (p < 0.7) return 4f * (float)(p / 0.7);
            return 4f * (float)((1.0 - p) / 0.3);
        }

        /// <summary>震屏查表：1.26–1.54s 的 5 个离散位移。</summary>
        private static void ShakeAt(double t, out float dx, out float dy)
        {
            dx = 0f;
            dy = 0f;
            if (t < ShakeT0) return;
            int idx = (int)((t - ShakeT0) / ShakeStep);
            if (idx < 0) idx = 0;
            if (idx > ShakeXs.Length - 1) idx = ShakeXs.Length - 1;
            dx = ShakeXs[idx];
            dy = ShakeYs[idx];
        }
    }
}
