using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace WorkCountdown
{
    /// <summary>
    /// 06 · 丝带礼物 Ribbon Gift（design/card-spread-notify-v4.html 的 s6，规格见 GOAL-SPEC-v4.md）。
    /// 奶油纸上下对开 → 珊瑚缎带从左抽出 → 吊牌落下轻摆；卡外左上大蝴蝶结、右下金星蜡封、彩屑。
    /// 时间轴按设计稿 7s 循环取绝对秒（1% = 70ms），末尾统一乘 (1 - s.Out) 收场。
    /// </summary>
    internal sealed class GoalVariantRibbon : IGoalVariant
    {
        public string id { get { return "06"; } }
        public string name { get { return "丝带礼物 Ribbon Gift"; } }
        public bool hasOutside { get { return true; } }

        // ---------------- 设计稿配色 ----------------
        private static readonly Color PaperTop = GoalPaint.Hex("#FFFBF2");
        private static readonly Color PaperBottom = GoalPaint.Hex("#FBF2E1");
        private static readonly Color PaperStripe = GoalPaint.Hex("#A07846");
        private static readonly Color PaperShade = GoalPaint.Hex("#785428");
        private static readonly Color BandTop = GoalPaint.Hex("#ED6A50");
        private static readonly Color BandMid = GoalPaint.Hex("#D84F37");
        private static readonly Color BandBottom = GoalPaint.Hex("#C2432F");
        private static readonly Color BandShadow = GoalPaint.Hex("#782C1E");
        private static readonly Color TagTop = GoalPaint.Hex("#FFFDF7");
        private static readonly Color TagBottom = GoalPaint.Hex("#FFF7E8");
        private static readonly Color TagEdge = GoalPaint.Hex("#B08850");
        private static readonly Color TagShadow = GoalPaint.Hex("#5A3C1E");
        private static readonly Color HoleFill = GoalPaint.Hex("#F4E9D6");
        private static readonly Color TitleInk = GoalPaint.Hex("#26372D");
        private static readonly Color RuleGold = GoalPaint.Hex("#C9A227");
        private static readonly Color SubInk = GoalPaint.Hex("#7A6A50");
        private static readonly Color BowFill = GoalPaint.Hex("#E4573D");
        private static readonly Color BowLine = GoalPaint.Hex("#B93B2C");
        private static readonly Color TailFill = GoalPaint.Hex("#D84F37");
        private static readonly Color KnotFill = GoalPaint.Hex("#C2432F");
        private static readonly Color KnotLine = GoalPaint.Hex("#A53125");
        private static readonly Color WaxHi = GoalPaint.Hex("#F6D98A");
        private static readonly Color WaxMid = GoalPaint.Hex("#C99A2E");
        private static readonly Color WaxLow = GoalPaint.Hex("#946C18");
        private static readonly Color WaxRing = GoalPaint.Hex("#FFF5C8");
        private static readonly Color WaxStar = GoalPaint.Hex("#FFF6D8");

        // 彩屑 6 色（设计稿 --c 的取值集合）
        private static readonly Color[] Confetti =
        {
            GoalPaint.Hex("#E4573D"), GoalPaint.Hex("#C9A227"), GoalPaint.Hex("#2F6B4F"),
            GoalPaint.Hex("#F2A7B8"), GoalPaint.Hex("#EAD9B8"), GoalPaint.Hex("#B93B2C")
        };

        // 卡内 30 片彩屑的随机参数（固定种子，任何一帧都一致）
        private sealed class Bit { public float x, w, h, rot, dur, delay; public Color color; }
        private readonly Bit[] bits = new Bit[30];

        public GoalVariantRibbon()
        {
            var rnd = new Lcg(6);
            for (int i = 0; i < bits.Length; i++)
            {
                var b = new Bit();
                b.x = rnd.NextFloat() * 280f;
                b.w = 4f + rnd.NextFloat() * 4f;
                b.h = 6f + rnd.NextFloat() * 6f;
                b.rot = rnd.NextFloat() * 360f;
                b.dur = 3.6f + rnd.NextFloat() * 2.4f;
                b.delay = rnd.NextFloat() * 4f;
                b.color = Confetti[(int)(rnd.NextFloat() * 5.999f)];
                bits[i] = b;
            }
        }

        // ================================================================ 卡内

        public void PaintInside(GoalScene s)
        {
            if (s.collapsed) { PaintCollapsed(s); return; }

            float u = s.scale, W = s.W, H = s.H;
            double p = s.quiet ? 3.2 : s.t;
            double fade = s.quiet ? 1.0 : Math.Max(0, 1 - s.Out);
            if (fade <= 0.004) return;

            // 1. 纸面：上下两条矩形向中线外扩（3% 起，17% 全开 = 0.21–1.19s）
            double open = s.quiet ? 1.0 : GoalScene.Smooth(GoalScene.Clamp01((p - 0.21) / 0.98));
            double paperA = s.quiet ? 1.0 : GoalScene.Smooth(GoalScene.Clamp01((p - 0.26) / 0.16)) * fade;

            var saved = s.g.Save();
            if (open < 0.999)
            {
                float mid = 94f * u;
                float half = mid * (float)open;
                using (var clip = new GraphicsPath())
                {
                    if (half > 0.4f)
                    {
                        clip.AddRectangle(new RectangleF(0, mid - half, W, half));
                        clip.AddRectangle(new RectangleF(0, mid, W, half));
                    }
                    s.g.SetClip(clip, CombineMode.Intersect);
                }
            }
            if (paperA > 0.004)
            {
                PaperFill(s.g, W, H, paperA);
                PaperStrips(s.g, u, W, H, paperA);
                PaperCorners(s.g, W, H, paperA);
            }
            s.g.Restore(saved);

            // 2. 缎带：从左端（x=-12）横向抽开（3%→12% = 0.21–0.84s）
            double bandP = s.quiet ? 1.0 : GoalScene.Smooth(GoalScene.Clamp01((p - 0.21) / 0.63));
            if (bandP > 0.004 && fade > 0.004) Band(s.g, u, bandP, fade);

            // 3. 彩屑（容器 10%→17% 显，30 片各自下落）
            double confA = (s.quiet ? 1.0 : GoalScene.Smooth(GoalScene.Clamp01((p - 0.70) / 0.49))) * fade;
            if (confA > 0.004) ConfettiInside(s.g, u, p, confA);

            // 4. 吊牌（delay .55s：0.97 隐 → 1.60 定 → 1.88 收）
            double tagA;
            float theta, dy, sc;
            TagPose(p, s.quiet, out tagA, out theta, out dy, out sc);
            dy -= (float)(8.0 * s.Out);        // 退场时吊牌再往上一小段（设计稿 88% 的 translateY(-8)）
            double swing = 0;
            if (!s.quiet && p > 1.0)
            {
                double ph = ((p - 1.0) / 5.0) % 1.0;
                swing = -0.8 + 1.6 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * ph));
            }
            Tag(s.g, s, theta, dy, sc, (float)swing, tagA * fade);

            // 5. 蝴蝶结压在卡面上的那一半（画在吊牌之上；卡外面那半在 PaintOutside 里同形画一遍，
            //    真实窗口里出框层本来就盖在卡面之上，离屏探针也靠这一笔把结拼完整）
            double bowA = (s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(0.56, 0.56))) * fade;
            DrawBow(s.g, u, bowA, 0.92f, -2f * u, -8f * u);
        }

        // ---------------------------------------------------------------- 纸

        private static void PaperFill(Graphics g, float w, float h, double alpha)
        {
            var rect = new RectangleF(0, 0, Math.Max(1f, w), Math.Max(1f, h));
            using (var b = new LinearGradientBrush(rect, GoalPaint.A(PaperTop, alpha), GoalPaint.A(PaperBottom, alpha), 90f))
                g.FillRectangle(b, rect);
        }

        /// <summary>1px 竖纹，周期 6 DIP（repeating-linear-gradient 90deg）。</summary>
        private static void PaperStrips(Graphics g, float u, float w, float h, double alpha)
        {
            float step = 6f * u;
            if (step < 2f) step = 2f;
            using (var pen = new Pen(GoalPaint.A(PaperStripe, 0.0385 * alpha), Math.Max(1f, u)))
            {
                for (float x = step * 0.5f; x < w; x += step)
                    g.DrawLine(pen, x, 0, x, h);
            }
        }

        /// <summary>四角压暗：radial-gradient(100% 100% at 50% 40%)，中心 50% 内透明。</summary>
        private static void PaperCorners(Graphics g, float w, float h, double alpha)
        {
            var box = new RectangleF(w * 0.5f - w, h * 0.4f - h, w * 2f, h * 2f);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(box);
                using (var b = new PathGradientBrush(path))
                {
                    b.CenterPoint = new PointF(w * 0.5f, h * 0.4f);
                    b.CenterColor = GoalPaint.A(PaperShade, 0);
                    b.SurroundColors = new Color[] { GoalPaint.A(PaperShade, 0.15 * alpha) };
                    try { b.SetSigmaBellShape(0.4f, 1f); } catch { }
                    g.FillPath(b, path);
                }
            }
        }

        // ---------------------------------------------------------------- 缎带

        private static void Band(Graphics g, float u, double q, double alpha)
        {
            float x = -12f * u;
            float y = 98f * u;
            float h = 26f * u;
            float w = 304f * u * (float)q;
            if (w <= 0.5f) return;

            // 投影 0 3px 8px #782C1E 16%
            using (var b = new LinearGradientBrush(
                new RectangleF(x, y + 2f * u, w, 11f * u),
                GoalPaint.A(BandShadow, 0.16 * alpha), GoalPaint.A(BandShadow, 0), 90f))
                g.FillRectangle(b, x, y + 2f * u, w, 11f * u);

            // 竖向三段渐变
            using (var b = new LinearGradientBrush(new RectangleF(x, y, w, h), Color.Empty, Color.Empty, 90f))
            {
                var blend = new ColorBlend(3);
                blend.Colors = new Color[]
                {
                    GoalPaint.A(BandTop, alpha), GoalPaint.A(BandMid, alpha), GoalPaint.A(BandBottom, alpha)
                };
                blend.Positions = new float[] { 0f, 0.46f, 1f };
                b.InterpolationColors = blend;
                g.FillRectangle(b, x, y, w, h);
            }

            // 顶部 4px 高光条（左亮右暗）
            using (var b = new LinearGradientBrush(
                new RectangleF(x, y + 3f * u, w, 4f * u),
                GoalPaint.A(Color.White, 0.224 * alpha), GoalPaint.A(Color.White, 0.032 * alpha), 0f))
                g.FillRectangle(b, x, y + 3f * u, w, 4f * u);
        }

        // ---------------------------------------------------------------- 彩屑

        private void ConfettiInside(Graphics g, float u, double p, double alpha)
        {
            for (int i = 0; i < bits.Length; i++)
            {
                Bit b = bits[i];
                double prog = ((p + b.delay) / b.dur) % 1.0;

                // 0% 透明 → 8% .95 → 76% .85 → 100% 0
                double pa;
                if (prog < 0.08) pa = 0.95 * (prog / 0.08);
                else if (prog < 0.76) pa = 0.95 - 0.10 * ((prog - 0.08) / 0.68);
                else pa = 0.85 * (1 - (prog - 0.76) / 0.24);

                float y = (-10f + 210f * (float)prog) * u;
                float angle = b.rot + 220f * (float)prog;
                DrawBit(g, b.x * u, y, b.w * u, b.h * u, angle, b.color, pa * alpha);
            }
        }

        // ---------------------------------------------------------------- 吊牌

        /// <summary>吊牌姿态：入场 scale/旋转/位移，退场只乘 alpha。</summary>
        private static void TagPose(double p, bool quiet, out double alpha, out float theta, out float dy, out float sc)
        {
            if (quiet || p >= 1.87) { alpha = 1; theta = -2.4f; dy = 0f; sc = 1f; return; }
            if (p <= 0.97) { alpha = 0; theta = -12f; dy = -18f; sc = 0.9f; return; }
            if (p < 1.60)
            {
                double e = GoalScene.Smooth(GoalScene.Clamp01((p - 0.97) / 0.63));
                alpha = e;
                theta = (float)(-12 + 8 * e);
                dy = (float)(-18 + 20 * e);
                sc = (float)(0.9 + 0.11 * e);
                return;
            }
            double e2 = GoalScene.Smooth(GoalScene.Clamp01((p - 1.60) / 0.27));
            alpha = 1;
            theta = (float)(-4 + 1.6 * e2);
            dy = (float)(2 - 2 * e2);
            sc = (float)(1.01 - 0.01 * e2);
        }

        private static void Tag(Graphics g, GoalScene s, float theta, float dy, float sc, float swing, double alpha)
        {
            if (alpha <= 0.004) return;
            float u = s.scale;
            float cx = 140f * u, cy = 94f * u;
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(theta);
            g.ScaleTransform(sc, sc);
            g.TranslateTransform(0, dy * u);
            g.TranslateTransform(-cx, -cy);

            var tag = new RectangleF(20f * u, 24f * u, 240f * u, 140f * u);

            // 投影 0 10px 24px rgba(90,60,30,.16)：三层近似
            using (var b = new SolidBrush(GoalPaint.A(TagShadow, 0.045 * alpha)))
            {
                for (int i = 3; i >= 1; i--)
                {
                    float grow = i * 2.2f * u;
                    var sh = new RectangleF(tag.X - grow * 0.5f, tag.Y + 8f * u + grow * 0.35f,
                        tag.Width + grow, tag.Height + grow);
                    using (var path = GoalPaint.RoundRect(sh, 5f * u + grow * 0.4f))
                        g.FillPath(b, path);
                }
            }

            // 牌面
            using (var path = GoalPaint.RoundRect(tag, 5f * u))
            {
                using (var b = new LinearGradientBrush(tag, GoalPaint.A(TagTop, alpha), GoalPaint.A(TagBottom, alpha), 90f))
                    g.FillPath(b, path);
                using (var b = new SolidBrush(GoalPaint.A(Color.White, 0.5 * alpha)))
                    g.FillRectangle(b, tag.X + 5f * u, tag.Y + 1.2f * u, tag.Width - 10f * u, Math.Max(1f, u));
                using (var pen = new Pen(GoalPaint.A(TagEdge, 0.28 * alpha), Math.Max(1f, u)))
                    g.DrawPath(pen, path);
            }

            // 内虚线框 inset 12/8/8（落回 (28,36)–(252,156)）
            using (var pen = new Pen(GoalPaint.A(TagEdge, 0.30 * alpha), Math.Max(1f, u)))
            {
                pen.DashStyle = DashStyle.Custom;
                pen.DashPattern = new float[] { 3f, 3f };
                using (var path = GoalPaint.RoundRect(new RectangleF(28f * u, 36f * u, 224f * u, 120f * u), 3f * u))
                    g.DrawPath(pen, path);
            }

            // 吊孔 9×9 @ top 9
            float hx = 140f * u, hy = 37.5f * u, hr = 4.5f * u;
            using (var b = new SolidBrush(GoalPaint.A(HoleFill, alpha)))
                g.FillEllipse(b, hx - hr, hy - hr, hr * 2, hr * 2);
            using (var pen = new Pen(GoalPaint.A(TagShadow, 0.20 * alpha), Math.Max(1f, u)))
                g.DrawEllipse(pen, hx - hr, hy - hr, hr * 2, hr * 2);
            using (var pen = new Pen(GoalPaint.A(TagShadow, 0.30 * alpha), Math.Max(1f, u)))
                g.DrawArc(pen, hx - hr * 0.78f, hy - hr * 0.92f, hr * 1.56f, hr * 1.7f, 200, 140);
            using (var pen = new Pen(GoalPaint.A(Color.White, 0.55 * alpha), Math.Max(1f, u)))
                g.DrawArc(pen, hx - hr * 0.72f, hy - hr * 0.66f, hr * 1.44f, hr * 1.3f, 30, 110);

            // 内容：内层再叠一次 ±0.8° 摆动（文字按吊牌局部坐标画，不重排）
            var inner = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(swing);
            g.TranslateTransform(-cx, -cy);
            TagText(g, s, alpha);
            g.Restore(inner);

            g.Restore(state);
        }

        private static void TagText(Graphics g, GoalScene s, double alpha)
        {
            float u = s.scale;
            float cx = 140f * u;

            // 标题 30px 宋体 600 / 字距 .05em / #26372D + 白影 0 1px
            var title = s.Serif(30f, FontStyle.Bold);
            float titleY = 63.9f * u;
            GoalPaint.TextCenter(g, "今日目标达成", title, cx, titleY + 1f * u, Color.White, 0.70 * alpha, 1.5f * u);
            GoalPaint.TextCenter(g, "今日目标达成", title, cx, titleY, TitleInk, alpha, 1.5f * u);

            // 金色分隔线 52×1
            float ry = 101.6f * u, rw = 52f * u;
            if (alpha > 0.004)
            {
                using (var b = new LinearGradientBrush(
                    new RectangleF(cx - rw * 0.5f - u, ry - u, rw + 2f * u, 3f * u), Color.Empty, Color.Empty, 0f))
                {
                    var blend = new ColorBlend(3);
                    blend.Colors = new Color[]
                    {
                        GoalPaint.A(RuleGold, 0), GoalPaint.A(RuleGold, 0.95 * alpha), GoalPaint.A(RuleGold, 0)
                    };
                    blend.Positions = new float[] { 0f, 0.5f, 1f };
                    b.InterpolationColors = blend;
                    g.FillRectangle(b, cx - rw * 0.5f, ry, rw, Math.Max(1f, 1f * u));
                }
            }

            // 副标题 14px 楷体 / 字距 .2em / #7A6A50
            var sub = s.Kai(14f);
            GoalPaint.TextCenter(g, "工作辛苦了", sub, cx, 111.4f * u, SubInk, alpha, 2.8f * u);
        }

        // ================================================================ 卡外

        public void PaintOutside(GoalScene s)
        {
            float u = s.scale;
            double fade = s.quiet ? 1.0 : Math.Max(0, 1 - s.Out);
            double show = s.quiet ? 1.0 : GoalScene.Smooth(s.Ease(0.56, 0.56));
            double a = show * fade;
            if (a <= 0.004) return;

            if (s.collapsed)
            {
                // 收起卡：卡外只留一个小蝴蝶结 + 两片彩屑
                DrawBow(s.g, u, a, 0.40f, -28f * u, -6f * u);
                DrawBit(s.g, -22f * u, 51f * u, 5f * u, 8f * u, 24f, Confetti[0], 0.85 * a);
                DrawBit(s.g, s.W + 12f * u, 26f * u, 4f * u, 7f * u, -18f, Confetti[1], 0.8 * a);
                return;
            }

            // 大蝴蝶结 + 两条飘带（组 transform translate(-2,-8) scale(.92)）。
            // 卡面以内那半已经由 PaintInside 画在卡层上，这里把卡面挖掉，避免两层叠出更深的边。
            var st = s.g.Save();
            using (var card = GoalPaint.RoundRect(new RectangleF(0, 0, s.W, s.H), 18f * u))
                s.g.SetClip(card, CombineMode.Exclude);
            DrawBow(s.g, u, a, 0.92f, -2f * u, -8f * u);
            s.g.Restore(st);

            // 卡外彩屑（设计稿出框 SVG 的固定位置）
            DrawBit(s.g, -58f * u, 34f * u, 8f * u, 12f * u, 24f, Confetti[0], 0.95 * a);
            DrawBit(s.g, -36f * u, 105f * u, 7f * u, 10f * u, -16f, Confetti[0], 0.95 * a);
            DrawBit(s.g, -49f * u, 182f * u, 9f * u, 12f * u, 38f, Confetti[0], 0.95 * a);
            DrawBit(s.g, -34f * u, 59f * u, 7f * u, 11f * u, 32f, Confetti[2], 0.95 * a);
            DrawBit(s.g, -50f * u, 238f * u, 8f * u, 12f * u, 12f, Confetti[3], 0.95 * a);
            DrawBit(s.g, 321f * u, 65f * u, 7f * u, 11f * u, 18f, Confetti[1], 0.95 * a);
            DrawBit(s.g, 344f * u, 122f * u, 8f * u, 12f * u, -24f, Confetti[1], 0.95 * a);
            DrawBit(s.g, 333f * u, 255f * u, 7f * u, 10f * u, 40f, Confetti[1], 0.95 * a);
            DrawBit(s.g, 349f * u, 195f * u, 7f * u, 11f * u, -20f, Confetti[2], 0.95 * a);
            DrawBit(s.g, 346f * u, 25f * u, 8f * u, 11f * u, -28f, Confetti[3], 0.95 * a);
            DrawBit(s.g, 372f * u, 148f * u, 6f * u, 10f * u, 8f, Confetti[4], 0.9 * a);
            DrawBit(s.g, -68f * u, 78f * u, 6f * u, 9f * u, -34f, Confetti[4], 0.9 * a);

            // 右下金星蜡封 (306,206) r20
            DrawWax(s.g, u, 306f * u, 206f * u, 20f * u, a);
        }

        // ---------------------------------------------------------------- 蝴蝶结

        private static void DrawBow(Graphics g, float u, double alpha, float f, float ox, float oy)
        {
            if (alpha <= 0.004) return;
            var state = g.Save();
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(f * u, f * u);   // 设计稿坐标（DIP）× 组缩放 × 卡片缩放
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                // 两片环
                using (var path = new GraphicsPath())
                {
                    BowLoop(path, 1f);
                    BowLoop(path, -1f);
                    using (var b = new SolidBrush(GoalPaint.A(BowFill, alpha))) g.FillPath(b, path);
                    using (var pen = new Pen(GoalPaint.A(BowLine, alpha), 1.5f))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, path);
                    }
                }
                // 两条飘带
                using (var path = new GraphicsPath())
                {
                    BowTail(path, 1f);
                    BowTail(path, -1f);
                    using (var b = new SolidBrush(GoalPaint.A(TailFill, alpha))) g.FillPath(b, path);
                    using (var pen = new Pen(GoalPaint.A(BowLine, alpha), 1.2f))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, path);
                    }
                }
                // 结
                using (var path = GoalPaint.RoundRect(new RectangleF(-13f, 0f, 26f, 20f), 5f))
                {
                    using (var b = new SolidBrush(GoalPaint.A(KnotFill, alpha))) g.FillPath(b, path);
                    using (var pen = new Pen(GoalPaint.A(KnotLine, alpha), 1.2f)) g.DrawPath(pen, path);
                }
                // 结上的高光弧
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(new PointF(-10f, 3f), new PointF(-4f, -4f), new PointF(4f, -4f), new PointF(10f, 3f));
                    using (var pen = new Pen(GoalPaint.A(Color.White, 0.30 * alpha), 2f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawPath(pen, path);
                    }
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        private static void BowLoop(GraphicsPath p, float sign)
        {
            p.StartFigure();
            p.AddBezier(new PointF(-8f * sign, 8f), new PointF(-48f * sign, -20f),
                new PointF(-82f * sign, -10f), new PointF(-70f * sign, 18f));
            p.AddBezier(new PointF(-70f * sign, 18f), new PointF(-62f * sign, 38f),
                new PointF(-28f * sign, 28f), new PointF(-6f * sign, 12f));
            p.CloseFigure();
        }

        private static void BowTail(GraphicsPath p, float sign)
        {
            p.StartFigure();
            p.AddBezier(new PointF(-8f * sign, 10f), new PointF(-34f * sign, 38f),
                new PointF(-44f * sign, 60f), new PointF(-40f * sign, 78f));
            p.AddLine(new PointF(-40f * sign, 78f), new PointF(-24f * sign, 70f));
            p.AddLine(new PointF(-24f * sign, 70f), new PointF(-18f * sign, 88f));
            p.AddLine(new PointF(-18f * sign, 88f), new PointF(2f * sign, 62f));
            p.CloseFigure();
        }

        // ---------------------------------------------------------------- 蜡封

        private static void DrawWax(Graphics g, float u, float cx, float cy, float r, double alpha)
        {
            if (r <= 0 || alpha <= 0.004) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                using (var b = new PathGradientBrush(path))
                {
                    b.CenterPoint = new PointF(cx - 0.28f * r, cy - 0.40f * r);
                    var blend = new ColorBlend(3);
                    blend.Colors = new Color[]
                    {
                        GoalPaint.A(WaxHi, alpha), GoalPaint.A(WaxMid, alpha), GoalPaint.A(WaxLow, alpha)
                    };
                    blend.Positions = new float[] { 0f, 0.66f, 1f };
                    b.InterpolationColors = blend;
                    g.FillPath(b, path);
                }
            }
            using (var pen = new Pen(GoalPaint.A(WaxRing, 0.45 * alpha), Math.Max(1f, u)))
                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);

            // 五角星（设计稿 SVG 的 10 点路径）
            float k = r / 20f;
            var star = new PointF[]
            {
                new PointF(0, -10), new PointF(2.8f, -3), new PointF(10, -2.4f), new PointF(4.6f, 2.2f),
                new PointF(6.2f, 9.4f), new PointF(0, 5.6f), new PointF(-6.2f, 9.4f), new PointF(-4.6f, 2.2f),
                new PointF(-10, -2.4f), new PointF(-2.8f, -3)
            };
            for (int i = 0; i < star.Length; i++)
                star[i] = new PointF(cx + star[i].X * k, cy + star[i].Y * k);
            using (var b = new SolidBrush(GoalPaint.A(WaxStar, alpha))) g.FillPolygon(b, star);
        }

        // ---------------------------------------------------------------- 彩屑片

        private static void DrawBit(Graphics g, float cx, float cy, float w, float h, float angle,
            Color color, double alpha)
        {
            if (alpha <= 0.004) return;
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            using (var b = new SolidBrush(GoalPaint.A(color, alpha)))
            using (var p = GoalPaint.RoundRect(new RectangleF(-w * 0.5f, -h * 0.5f, w, h), 1f))
                g.FillPath(b, p);
            g.Restore(state);
        }

        // ================================================================ 收起卡

        /// <summary>收起卡 224×68：纸面 + 一段缩窄缎带 + 右下 14×14 蜡封 + 标题，单独排版。</summary>
        private void PaintCollapsed(GoalScene s)
        {
            float u = s.scale, W = s.W, H = s.H;
            double p = s.quiet ? 3.2 : s.t;
            double fade = s.quiet ? 1.0 : Math.Max(0, 1 - s.Out);
            if (fade <= 0.004) return;

            double paperA = (s.quiet ? 1.0 : GoalScene.Smooth(GoalScene.Clamp01((p - 0.16) / 0.34))) * fade;
            if (paperA <= 0.004) return;

            PaperFill(s.g, W, H, paperA);
            PaperStrips(s.g, u, W, H, paperA);
            PaperCorners(s.g, W, H, paperA);

            // 缩窄缎带：贴底一段，左端抽出
            double bandP = s.quiet ? 1.0 : GoalScene.Smooth(GoalScene.Clamp01((p - 0.18) / 0.42));
            float bx = -8f * u, by = 46f * u, bh = 13f * u;
            float bw = (W + 16f * u) * (float)bandP;
            if (bw > 0.5f)
            {
                using (var b = new LinearGradientBrush(
                    new RectangleF(bx, by + 1.5f * u, bw, 6f * u),
                    GoalPaint.A(BandShadow, 0.16 * paperA), GoalPaint.A(BandShadow, 0), 90f))
                    s.g.FillRectangle(b, bx, by + 1.5f * u, bw, 6f * u);
                using (var b = new LinearGradientBrush(new RectangleF(bx, by, bw, bh), Color.Empty, Color.Empty, 90f))
                {
                    var blend = new ColorBlend(3);
                    blend.Colors = new Color[]
                    {
                        GoalPaint.A(BandTop, paperA), GoalPaint.A(BandMid, paperA), GoalPaint.A(BandBottom, paperA)
                    };
                    blend.Positions = new float[] { 0f, 0.46f, 1f };
                    b.InterpolationColors = blend;
                    s.g.FillRectangle(b, bx, by, bw, bh);
                }
                using (var b = new LinearGradientBrush(
                    new RectangleF(bx, by + 2f * u, bw, 2.5f * u),
                    GoalPaint.A(Color.White, 0.224 * paperA), GoalPaint.A(Color.White, 0.032 * paperA), 0f))
                    s.g.FillRectangle(b, bx, by + 2f * u, bw, 2.5f * u);
            }

            // 右下 14×14 蜡封（右/下各留 8）
            DrawWax(s.g, u, W - 15f * u, H - 15f * u, 7f * u, paperA);

            // 标题 20px 宋体 + 副标题 11px 楷体（左对齐，压在纸面上）
            var title = s.Serif(20f, FontStyle.Bold);
            float tx = 16f * u;
            TextLeft(s.g, "今日目标达成", title, tx, 5.4f * u, Color.White, 0.70 * paperA, 1f * u);
            TextLeft(s.g, "今日目标达成", title, tx, 4.4f * u, TitleInk, paperA, 1f * u);

            var sub = s.Kai(11f);
            TextLeft(s.g, "工作辛苦了", sub, tx, 26.6f * u, SubInk, paperA, 2.2f * u);
        }

        private static void TextLeft(Graphics g, string text, Font font, float x, float y, Color color,
            double alpha, float spacing)
        {
            if (alpha <= 0.004) return;
            using (var b = new SolidBrush(GoalPaint.A(color, alpha)))
                GoalPaint.Chars(g, text, font, x, y, spacing, delegate { return (Brush)b; });
        }

        // ================================================================ 小工具

        /// <summary>固定种子的小随机数（彩屑排布；不同帧必须一样）。</summary>
        private sealed class Lcg
        {
            private uint state;
            public Lcg(uint seed) { state = seed * 2654435761u + 12345u; }
            public float NextFloat()
            {
                state = state * 1664525u + 1013904223u;
                return ((state >> 8) & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
