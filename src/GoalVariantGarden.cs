using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WorkCountdown
{
    /// <summary>
    /// 01 · 花园绽放 Bloom Garden（design/card-spread-notify-v4.html 的 .s1）。
    /// 浅紫柔光从左下角圆形铺开，11 组花叶依次弹出，蝴蝶停在右上；卡内藤蔓往上爬。
    /// 卡外是四段连着的藤（上弧 / 左 / 右 / 下弧），花叶都长在藤上，绕成一圈花环。
    /// </summary>
    internal sealed class GoalVariantGarden : IGoalVariant
    {
        public string id { get { return "01"; } }
        public string name { get { return "花园绽放 Bloom Garden"; } }
        public bool hasOutside { get { return true; } }

        // ------------------------------------------------------------ 配色（.s1）

        private static readonly Color WashIn = GoalPaint.Hex("#FAF0FC");
        private static readonly Color Wash2 = GoalPaint.Hex("#F1DAF5");
        private static readonly Color Wash3 = GoalPaint.Hex("#E5CAEE");
        private static readonly Color WashEdge = GoalPaint.Hex("#D5B3E2");
        private static readonly Color HaloInk = Color.White;

        private static readonly Color Ink = GoalPaint.Hex("#2A1832");
        private static readonly Color SubInk = GoalPaint.Hex("#64406F");
        private static readonly Color RuleA = GoalPaint.Hex("#C79BD7");
        private static readonly Color RuleB = GoalPaint.Hex("#E6B9F0");
        private static readonly Color ShadowInk = GoalPaint.Hex("#5A326E");

        private static readonly Color VineDeep = GoalPaint.Hex("#245A45");
        private static readonly Color Vine = GoalPaint.Hex("#2F6B4F");
        private static readonly Color VineLight = GoalPaint.Hex("#3C7D5D");
        private static readonly Color Coral = GoalPaint.Hex("#F2675B");
        private static readonly Color Cream = GoalPaint.Hex("#FFF9F0");
        private static readonly Color Lilac = GoalPaint.Hex("#A98BD8");
        private static readonly Color Yellow = GoalPaint.Hex("#F6C94C");
        private static readonly Color Berry = GoalPaint.Hex("#E4573D");
        private static readonly Color Seed = GoalPaint.Hex("#FFE9E2");
        private static readonly Color Cocoa = GoalPaint.Hex("#7A4A2A");
        private static readonly Color WingInk = GoalPaint.Hex("#3A2440");

        // ------------------------------------------------------------ 几何常量

        /// <summary>.garden 整体 scale(1.13)，origin 50% 54%。</summary>
        private const float GardenScale = 1.13f;
        private const float GardenCx = 140f;
        private const float GardenCy = 101.5f;
        /// <summary>quiet 时钉在 3.2s（= freeze(3.2)）。</summary>
        private const double StaticT = 3.2;

        private static readonly float[] PetalX = { 36, 88, 196, 224, 62, 178 };
        private static readonly float[] PetalY = { 66, 42, 52, 92, 112, 126 };
        private static readonly float[] PetalR = { 18, -32, 64, -12, 42, -56 };
        private static readonly float[] PetalD = { 1.5f, 1.9f, 2.3f, 2.7f, 3.1f, 3.5f };
        private static readonly Color[] PetalC = { Coral, Cream, Lilac, Yellow, Lilac, Coral };

        // 出框层四段藤（起 + 两段三次贝塞尔；长度 4 表示只有一段）
        private static readonly float[] VineTop = { -58, 36, -10, -10, 50, -44, 112, -48, 180, -52, 264, -18, 326, 30 };
        private static readonly float[] VineLeft = { -28, 38, -56, 88, -58, 146, -38, 204 };
        private static readonly float[] VineRight = { 314, 38, 342, 88, 344, 150, 322, 208 };
        private static readonly float[] VineBottom = { -30, 226, 60, 258, 210, 258, 300, 226 };

        // ------------------------------------------------------------ 入口

        public void PaintInside(GoalScene s)
        {
            if (s.collapsed) { PaintCollapsed(s); return; }

            float k = s.W / 280f;
            float hk = s.H / 188f;
            double t = s.quiet ? StaticT : s.t;
            double fade = s.quiet ? 1.0 : (1.0 - s.Out);
            if (fade <= 0.004) return;

            Wash(s, t, fade);
            Halo(s, k, hk, t, fade);
            PaintGarden(s, k, hk, t, fade);
            PaintPetals(s, k, hk, t, fade);
            PaintText(s, k, hk, t, fade);
            // 出框层压住卡内那一刀（.out 的 z-index 高于 .fx）
            PaintWreath(s, k, hk, t, fade);
        }

        public void PaintOutside(GoalScene s)
        {
            float k = s.W / (s.collapsed ? 224f : 280f);
            float hk = s.H / (s.collapsed ? 68f : 188f);
            double t = s.quiet ? StaticT : s.t;
            double fade = s.quiet ? 1.0 : (1.0 - s.Out);
            if (fade <= 0.004) return;
            if (s.collapsed)
            {
                PaintWreathSmall(s, k, hk, t, fade);
                return;
            }
            PaintWreath(s, k, hk, t, fade);
        }

        // ------------------------------------------------------------ 铺底 / 柔光

        /// <summary>圆形 clip 从左下 (0.18W,0.88H) 扩到满卡，圆内是四段径向渐变。</summary>
        private void Wash(GoalScene s, double t, double fade)
        {
            Graphics g = s.g;
            float W = s.W, H = s.H;

            // clip 半径：0 → 46% → 160%（1% = 70ms）
            double g1 = GoalScene.Smooth(GoalScene.Clamp01((t - 0.21) / 0.63));
            double g2 = GoalScene.Smooth(GoalScene.Clamp01((t - 0.84) / 0.49));
            float proj = (float)(Math.Sqrt(1.2f * W * 1.2f * W + 1.15f * H * 1.15f * H) / Math.Sqrt(2.0));
            float r = proj * (float)(0.46 * g1 + 1.14 * g2);
            if (r < 0.6f) return;

            float cx = W * 0.18f, cy = H * 0.88f;
            GraphicsState st = g.Save();
            if (r < proj * 1.75f)
            {
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(cx - r, cy - r, r * 2f, r * 2f);
                    g.SetClip(clip, CombineMode.Intersect);
                }
            }
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var img = new ImageAttributes())
            {
                var cm = new ColorMatrix();
                cm.Matrix33 = (float)fade;
                img.SetColorMatrix(cm);
                Bitmap bmp = WashBitmap();
                g.DrawImage(bmp, new Rectangle(0, 0, (int)Math.Round(W), (int)Math.Round(H)),
                    0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, img);
            }
            g.Restore(st);
        }

        /// <summary>左上柔光：radial 白 α62% → 0@70%，blur 14。</summary>
        private void Halo(GoalScene s, float k, float hk, double t, double fade)
        {
            double a = GoalScene.Smooth(GoalScene.Clamp01((t - 0.42) / 0.56));
            if (a <= 0.01) return;
            float cx = 115.6f * k, cy = 120.0f * hk;
            float r = 63f * k;
            GoalPaint.GlowEllipse(s.g, new RectangleF(cx - r * 1.22f, cy - r * 1.22f, r * 2.44f, r * 2.44f),
                HaloInk, 0.17 * a * fade);
            GoalPaint.GlowEllipse(s.g, new RectangleF(cx - r, cy - r, r * 2f, r * 2f),
                HaloInk, 0.50 * a * fade);
        }

        // ------------------------------------------------------------ 卡内花园

        private void PaintGarden(GoalScene s, float k, float hk, double t, double fade)
        {
            Graphics g = s.g;
            GraphicsState st;
            double a;

            // 左下大叶 (270,186)
            st = PushBloom(s, 270, 186, 0.30f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004)
            {
                FillPath(g, VineDeep, a, new float[] { 0, 2, -18, -26, -34, -38, -60, -42, -34, -28, -18, -8, -4, 8 });
                FillPath(g, Vine, a, new float[] { 2, 2, -4, -28, 2, -52, 22, -70, 10, -42, 10, -18, 12, 4 });
                FillPath(g, VineLight, a, new float[] { 0, 2, -22, -8, -42, -8, -58, 2, -36, 6, -16, 12, -4, 16 });
            }
            g.Restore(st);

            // 左上 珊瑚花 (35,31)
            st = PushBloom(s, 35, 31, 0.04f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004)
            {
                Stem(g, new float[] { 1, 9, -2, 24, -7, 39, -5, 55 }, 1.7f, Vine, a);
                GoalPaint.Leaf(g, -5, 36, 10.5f, 8f, 200, Vine, a);
                GoalPaint.Leaf(g, -3, 51, 9.5f, 7f, 191, VineLight, a);
                Petals(g, 5, 6.2f, 10f, 11f, 0, Coral, a);
                GoalPaint.Dot(g, 0, 0, 4.6f, Yellow, a);
            }
            g.Restore(st);

            // 顶部 白花 (64,21)
            st = PushBloom(s, 64, 21, 0.17f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004)
            {
                Stem(g, new float[] { 0, 6, 3, 18, -1, 28, -3, 38 }, 1.3f, Vine, a);
                Petals(g, 6, 4.1f, 6.5f, 7f, 0, Cream, a);
                GoalPaint.Dot(g, 0, 0, 3f, Yellow, a);
            }
            g.Restore(st);

            // 左中 小紫花 (17,79)
            st = PushBloom(s, 17, 79, 0.29f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004)
            {
                Stem(g, new float[] { 0, 5, -2, 14, 0, 22, 1, 30 }, 1.2f, Vine, a);
                Petals(g, 5, 3.4f, 5.4f, 6f, 0, Lilac, a);
                GoalPaint.Dot(g, 0, 0, 2.5f, Yellow, a);
            }
            g.Restore(st);

            // 左下 白花 (30,158)
            st = PushBloom(s, 30, 158, 0.22f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004)
            {
                Stem(g, new float[] { 0, 7, 3, 17, 1, 24, 0, 31 }, 1.2f, Vine, a);
                Petals(g, 5, 4.4f, 6.8f, 7f, 0, Cream, a);
                GoalPaint.Dot(g, 0, 0, 3.2f, Yellow, a);
            }
            g.Restore(st);

            // 左下 小黄花 (48,174)
            st = PushBloom(s, 48, 174, 0.36f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004)
            {
                Petals(g, 5, 2.8f, 4.4f, 5f, 0, Yellow, a);
                GoalPaint.Dot(g, 0, 0, 2f, Cocoa, a);
            }
            g.Restore(st);

            // 右下 大黄花 (244,135)
            st = PushBloom(s, 244, 135, 0.12f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004)
            {
                Stem(g, new float[] { 0, 9, -3, 22, -2, 32, -1, 42 }, 1.5f, Vine, a);
                Petals(g, 5, 6f, 9.5f, 10f, 0, Yellow, a);
                GoalPaint.Dot(g, 0, 0, 4.4f, Cocoa, a);
            }
            g.Restore(st);

            // 右上 紫花 (252,47)
            st = PushBloom(s, 252, 47, 0.08f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004)
            {
                Stem(g, new float[] { 0, 9, 2, 22, 0, 32, -2, 42 }, 1.4f, Vine, a);
                GoalPaint.Leaf(g, 0, 27, 8f, 6.5f, 108, VineLight, a);
                Petals(g, 5, 5.2f, 8.2f, 9f, 0, Lilac, a);
                GoalPaint.Dot(g, 0, 0, 3.8f, Yellow, a);
            }
            g.Restore(st);

            // 右上 草莓 (257,17)
            st = PushBloom(s, 257, 17, 0.26f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004) Strawberry(g, a, false);
            g.Restore(st);

            // 左下 草莓 (13,136)
            st = PushBloom(s, 13, 136, 0.40f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004) Strawberry(g, a, true);
            g.Restore(st);

            // 蝴蝶 (174,30)：0.55s 后飞进来，翅膀 1.6s 一拍
            PaintButterfly(s, k, hk, t, fade);

            // 左侧藤蔓 (0,0)：M-6 106 C26 104 48 86 55 58
            st = PushBloom(s, 0, 0, 0.34f, t, k, hk, out a);
            a *= fade;
            if (a > 0.004)
            {
                StrokeBezier(g, new float[] { -6, 106, 26, 104, 48, 86, 55, 58 }, 1.7f, Vine, a);
                FillPath(g, VineLight, a, new float[] { 21, 96, 12, 86, 15, 76, 26, 74, 30, 84, 29, 92, 21, 96 });
                FillPath(g, Vine, a, new float[] { 41, 82, 35, 69, 42, 60, 53, 61, 55, 72, 50, 80, 41, 82 });
                GoalPaint.Dot(g, 55, 58, 3f, Coral, a);
            }
            g.Restore(st);

            // 星点
            Sparkles(s, k, hk, t, fade);
        }

        private void PaintButterfly(GoalScene s, float k, float hk, double t, double fade)
        {
            Graphics g = s.g;
            double inA = GoalScene.Smooth(GoalScene.Clamp01((t - 0.97) / 0.35));
            if (inA <= 0.004) return;
            double p = GoalScene.Clamp01((t - 0.97) / 0.56);
            float pop = 1f + 0.08f * (float)Math.Sin(Math.PI * p);
            float dy = (float)((1 - GoalScene.Smooth(GoalScene.Clamp01((t - 0.97) / 0.35)))) * 8f;
            float flap = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * (t / 1.6)));
            double a = inA * fade;

            GraphicsState st = g.Save();
            g.TranslateTransform(Gx(174, k), Gy(30, hk));
            g.ScaleTransform(GardenScale * k * pop, GardenScale * k * pop);
            g.TranslateTransform(0, dy);
            // 翅膀：左上 / 右上 / 左下 / 右下
            WingEllipse(g, -5.5f, -4f, 7.5f, 5.2f, -26f - 7f * flap, Coral, a);
            WingEllipse(g, 5.5f, -4f, 7.5f, 5.2f, 26f + 7f * flap, Yellow, a);
            WingEllipse(g, -5f, 4.5f, 5.2f, 3.6f, 18f + 5f * flap, Lilac, a);
            WingEllipse(g, 5f, 4.5f, 5.2f, 3.6f, -18f - 5f * flap, Berry, a);
            // 身体
            using (var b = new SolidBrush(GoalPaint.A(WingInk, a)))
                g.FillEllipse(b, -1.05f, -6.4f, 2.1f, 12.8f);
            using (var pen = new Pen(GoalPaint.A(WingInk, a), 0.85f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawBezier(pen, 0, -6, -1.4f, -9, -3.2f, -10.2f, -4.4f, -11.2f);
                g.DrawBezier(pen, 0, -6, 1.4f, -9, 3.2f, -10.2f, 4.4f, -11.2f);
            }
            g.Restore(st);
        }

        private static void WingEllipse(Graphics g, float cx, float cy, float rx, float ry, float rot, Color c, double a)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(rot);
            using (var b = new SolidBrush(GoalPaint.A(c, a))) g.FillEllipse(b, -rx, -ry, rx * 2, ry * 2);
            g.Restore(st);
        }

        /// <summary>5 颗四尖星，容器 1.12–1.54s 淡入，单颗 2.4s 闪。</summary>
        private void Sparkles(GoalScene s, float k, float hk, double t, double fade)
        {
            double cont = GoalScene.Smooth(GoalScene.Clamp01((t - 1.12) / 0.42));
            if (cont <= 0.004) return;
            float[] dx = { 78, 208, 170, 120, 237 };
            float[] dy = { 104, 29, 167, 26, 95 };
            float[] dr = { 5f, 4f, 4.5f, 3.5f, 3.5f };
            float[] dd = { 0f, 0.5f, 0.9f, 1.3f, 0.25f };
            for (int i = 0; i < dx.Length; i++)
            {
                double tw = s.quiet ? 0.62 : 0.5 - 0.5 * Math.Cos(2 * Math.PI * ((t + dd[i]) / 2.4));
                double a = (0.15 + 0.80 * tw) * cont * fade;
                float sc = 0.45f + 0.55f * (float)tw;
                float r = dr[i] * GardenScale * k * sc;
                Spark(s.g, Gx(dx[i], k), Gy(dy[i], hk), r, (float)(45 * tw) - 90f, Color.White, a);
            }
        }

        // ------------------------------------------------------------ 卡内掉的花瓣

        private void PaintPetals(GoalScene s, float k, float hk, double t, double fade)
        {
            Graphics g = s.g;
            for (int i = 0; i < PetalX.Length; i++)
            {
                double d = PetalD[i];
                double p;
                double a;
                if (s.quiet)
                {
                    p = 0.45; a = 0.85;
                }
                else
                {
                    p = GoalScene.Clamp01((t - d - 0.98) / 4.9);
                    a = GoalScene.Smooth(GoalScene.Clamp01((t - d - 0.98) / 0.42));
                    if (p > 0.91) a *= GoalScene.Clamp01((1 - p) / 0.09);
                }
                if (a <= 0.004) continue;
                float tx, ty, rot;
                PetalPose(p, PetalR[i], out tx, out ty, out rot);

                GraphicsState st = g.Save();
                g.TranslateTransform((PetalX[i] + tx) * k, (PetalY[i] + ty) * hk);
                g.RotateTransform(rot);
                g.ScaleTransform(k * 0.95f, k * 0.95f);
                g.TranslateTransform(-3f, -5f);
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(0, 10, 0, 3.5f, 1.2f, 0, 3, 0);
                    path.AddBezier(3, 0, 4.8f, 0, 6, 3.5f, 6, 5.5f);
                    path.AddBezier(6, 5.5f, 6, 8.5f, 3, 10, 0, 10);
                    path.CloseFigure();
                    using (var b = new SolidBrush(GoalPaint.A(PetalC[i], a * fade))) g.FillPath(b, path);
                }
                g.Restore(st);
            }
        }

        private static void PetalPose(double p, float r0, out float tx, out float ty, out float rot)
        {
            // 关键帧：0(0,-14) → .286(6,16) → .686(-4,42) → 1(8,58)；自转 +0/40/90/120
            if (p < 0.286)
            {
                double q = GoalScene.Smooth(p / 0.286);
                tx = (float)(0 + 6 * q); ty = (float)(-14 + 30 * q); rot = (float)(r0 + 40 * q);
            }
            else if (p < 0.686)
            {
                double q = GoalScene.Smooth((p - 0.286) / 0.4);
                tx = (float)(6 + (-10) * q); ty = (float)(16 + 26 * q); rot = (float)(r0 + 40 + 50 * q);
            }
            else
            {
                double q = GoalScene.Smooth((p - 0.686) / 0.314);
                tx = (float)(-4 + 12 * q); ty = (float)(42 + 16 * q); rot = (float)(r0 + 90 + 30 * q);
            }
        }

        // ------------------------------------------------------------ 文字

        private void PaintText(GoalScene s, float k, float hk, double t, double fade)
        {
            DrawTitle(s, 140f * k, 72.5f * hk, 34f, 0.86, 0.055, t, fade);
            double subA = PaintSubtitle(s, 140f * k, 112.7f * hk, 15.5f, 0.18f, t, fade);
            PaintRule(s, 140f * k, 134f * hk, 58f, k, subA, fade);
        }

        /// <summary>逐字上浮的标题（s1-char）。</summary>
        private void DrawTitle(GoalScene s, float cx, float cy, float size, double d0, double step, double t, double fade)
        {
            Graphics g = s.g;
            Font font = s.Sans(size, FontStyle.Bold);
            TextLine line = Line("今日目标达成", font, size * s.scale, 0f);
            float ox = cx - (line.box.X + line.box.Width * 0.5f);
            float oy = cy - (line.box.Y + line.box.Height * 0.5f);
            float midY = line.box.Y + line.box.Height * 0.5f;

            GraphicsState whole = g.Save();
            g.TranslateTransform(ox, oy);

            for (int i = 0; i < line.paths.Length; i++)
            {
                double lt = t - (d0 + i * step) - 0.70;   // 10% 才开始露头
                if (lt <= 0) continue;
                double pA = GoalScene.Smooth(GoalScene.Clamp01(lt / 0.28));
                double pB = GoalScene.Smooth(GoalScene.Clamp01((lt - 0.28) / 0.21));
                double a = GoalScene.Smooth(GoalScene.Clamp01(lt / 0.22)) * fade;
                if (a <= 0.004) continue;

                float dy = (float)(17 + (-19) * pA + 2 * pB);
                float rot = (float)(5 + (-6.4) * pA + 1.4 * pB);
                float sc = (float)(0.96 + 0.04 * pA);
                if (!s.quiet) dy -= 9f * (float)s.Out;

                float tx = line.cx[i] + ox;
                float ty = midY + oy + dy * s.scale;
                GraphicsState st = g.Save();
                g.TranslateTransform(tx, ty);
                g.RotateTransform(rot);
                g.ScaleTransform(sc, sc);
                g.TranslateTransform(-tx, -ty);

                // 阴影：0 1px 0 #FFF 45% + 0 6px 16px #5A326E 12%
                using (var b = new SolidBrush(GoalPaint.A(Color.White, 0.34 * a)))
                {
                    GraphicsState sh = g.Save();
                    g.TranslateTransform(0, 1f * s.scale);
                    g.FillPath(b, line.paths[i]);
                    g.Restore(sh);
                }
                using (var b = new SolidBrush(GoalPaint.A(ShadowInk, 0.055 * a)))
                {
                    GraphicsState sh = g.Save();
                    g.TranslateTransform(0, 5.5f * s.scale);
                    g.FillPath(b, line.paths[i]);
                    g.Restore(sh);
                }
                using (var b = new SolidBrush(GoalPaint.A(Ink, a))) g.FillPath(b, line.paths[i]);
                g.Restore(st);
            }
            g.Restore(whole);
        }

        /// <summary>从下往上揭开、字距 0.18em 的副标题；返回它此刻的强度（渐变短线跟着它）。</summary>
        private double PaintSubtitle(GoalScene s, float cx, float cy, float size, float trackEm, double t, double fade)
        {
            double rev = GoalScene.Smooth(GoalScene.Clamp01((t - 1.92) / 0.84));
            double a = GoalScene.Smooth(GoalScene.Clamp01((t - 1.92) / 0.28)) * fade;
            if (a <= 0.004 || rev <= 0.004) return 0;

            Graphics g = s.g;
            Font font = s.Sans(size);
            float track = size * trackEm * s.scale;
            TextLine line = Line("工作辛苦了", font, size * s.scale, track);
            float ox = cx - (line.box.X + line.box.Width * 0.5f);
            float oy = cy - (line.box.Y + line.box.Height * 0.5f);

            GraphicsState st = g.Save();
            // clip 自下而上：可见部分贴着墨迹底边往上长
            float vis = (float)(line.box.Height * rev);
            g.SetClip(new RectangleF(line.box.X + ox - 4, line.box.Bottom + oy - vis, line.box.Width + 8, vis),
                CombineMode.Intersect);
            using (var b = new SolidBrush(GoalPaint.A(SubInk, a)))
            {
                for (int i = 0; i < line.paths.Length; i++)
                {
                    GraphicsState st2 = g.Save();
                    g.TranslateTransform(ox, oy);
                    g.FillPath(b, line.paths[i]);
                    g.Restore(st2);
                }
            }
            g.Restore(st);
            return a;
        }

        private void PaintRule(GoalScene s, float cx, float y, float widthDip, float k, double a, double fade)
        {
            if (a <= 0.004) return;
            Graphics g = s.g;
            float w = widthDip * k * (0.86f + 0.14f * (float)a);
            float h = Math.Max(1f, 2f * k);
            var box = new RectangleF(cx - w * 0.5f - 1, y, w + 2, h);
            using (var b = new LinearGradientBrush(box, Color.Empty, Color.Empty, 0f))
            {
                var blend = new ColorBlend();
                blend.Colors = new Color[]
                {
                    GoalPaint.A(RuleA, 0), GoalPaint.A(RuleA, a), GoalPaint.A(RuleB, a),
                    GoalPaint.A(RuleA, a), GoalPaint.A(RuleA, 0)
                };
                blend.Positions = new float[] { 0f, 0.22f, 0.5f, 0.78f, 1f };
                b.InterpolationColors = blend;
                g.FillRectangle(b, cx - w * 0.5f, y, w, h);
            }
        }

        // ------------------------------------------------------------ 收起卡 224×68

        private void PaintCollapsed(GoalScene s)
        {
            float k = s.W / 224f;
            float hk = s.H / 68f;
            double t = s.quiet ? StaticT : s.t;
            double fade = s.quiet ? 1.0 : (1.0 - s.Out);
            if (fade <= 0.004) return;
            Graphics g = s.g;

            Wash(s, t, fade);
            GoalPaint.GlowEllipse(g, new RectangleF(-40 * k, -20 * hk, 220 * k, 130 * hk), HaloInk, 0.18 * fade);

            // 左边一朵珊瑚花 + 一片叶（缩到左下）
            GraphicsState st = g.Save();
            double pop = GoalScene.Smooth(GoalScene.Clamp01(t / 0.5));
            float rot = s.quiet ? 0f : (float)(-1.5 + 3.3 * ((0.5 - 0.5 * Math.Cos(2 * Math.PI * (t / 5.2)))));
            g.TranslateTransform(19f * k, 40f * hk);
            g.RotateTransform(rot);
            g.ScaleTransform(k * 0.62f * (float)(0.4 + 0.6 * pop), k * 0.62f * (float)(0.4 + 0.6 * pop));
            double a = pop * fade;
            Stem(g, new float[] { 1, 9, -2, 24, -7, 39, -5, 55 }, 1.9f, Vine, a);
            GoalPaint.Leaf(g, -5, 36, 10.5f, 8f, 200, Vine, a);
            GoalPaint.Leaf(g, -3, 51, 9.5f, 7f, 191, VineLight, a);
            Petals(g, 5, 6.2f, 10f, 11f, 0, Coral, a);
            GoalPaint.Dot(g, 0, 0, 4.6f, Yellow, a);
            g.Restore(st);

            // 一颗星
            double sa = (s.quiet ? 1 : GoalScene.Smooth(GoalScene.Clamp01((t - 0.6) / 0.4))) * fade;
            Spark(g, 206f * k, 17f * hk, 5.5f * k, -90f, Color.White, sa * 0.9);

            // 文字：标题 + 副标题 + 短线
            DrawTitle(s, 128f * k, 25.5f * hk, 19f, 0.30, 0.05, t, fade);
            double subA = PaintSubtitle(s, 128f * k, 44.5f * hk, 10.5f, 0.16f, t, fade);
            PaintRule(s, 128f * k, 55f * hk, 34f, k, subA, fade);
        }

        // ------------------------------------------------------------ 出框花环

        private void PaintWreath(GoalScene s, float k, float hk, double t, double fade)
        {
            double a = (s.quiet ? 1 : GoalScene.Smooth(GoalScene.Clamp01((t - 0.56) / 0.56))) * fade;
            if (a <= 0.004) return;
            Graphics g = s.g;
            GraphicsState st = g.Save();
            g.ScaleTransform(k, hk);

            // ---- 上弧藤
            VineCurve(g, VineTop, 2.4f, Vine, a);
            OutLeaf(g, -42, 18, -24, 1.1f, VineLight, a);
            OutLeaf(g, -10, 0, 18, 0.95f, Vine, a);
            OutLeaf(g, 22, -18, -14, 1.05f, VineLight, a);
            OutLeaf(g, 54, -32, 22, 0.9f, Vine, a);
            OutLeaf(g, 86, -42, -18, 1.1f, VineDeep, a);
            OutLeaf(g, 118, -46, 16, 0.9f, VineLight, a);
            OutLeaf(g, 150, -44, -22, 1.05f, Vine, a);
            OutLeaf(g, 182, -36, 14, 0.95f, VineDeep, a);
            OutLeaf(g, 214, -24, -16, 1.1f, VineLight, a);
            OutLeaf(g, 246, -8, 20, 0.9f, Vine, a);
            OutLeaf(g, 278, 8, -12, 1.05f, VineDeep, a);
            OutLeaf(g, 308, 26, 18, 0.95f, VineLight, a);
            OutFlower(g, -28, -4, 1.45f, Coral, false, a);
            OutFlower(g, 8, 16, 0.8f, Cream, false, a);
            OutFlower(g, -54, 28, 0.9f, Lilac, false, a);
            OutFlower(g, 122, -56, 1.05f, Lilac, false, a);
            OutFlower(g, 152, -40, 0.7f, Yellow, true, a);
            OutFlower(g, 324, 18, 1.25f, Yellow, true, a);
            OutFlower(g, 292, 46, 0.75f, Cream, false, a);
            OutFlower(g, 352, 46, 0.7f, Coral, false, a);

            // ---- 左藤
            VineCurve(g, VineLeft, 2.2f, Vine, a);
            OutLeaf(g, -42, 62, -42, 0.95f, VineLight, a);
            OutLeaf(g, -52, 96, 38, 0.9f, Vine, a);
            OutLeaf(g, -50, 130, -30, 1f, VineDeep, a);
            OutLeaf(g, -44, 162, 34, 0.9f, VineLight, a);
            OutLeaf(g, -36, 190, -24, 0.95f, Vine, a);
            OutFlower(g, -38, 118, 1.05f, Cream, false, a);
            OutFlower(g, -24, 170, 0.8f, Lilac, false, a);

            // ---- 右藤
            VineCurve(g, VineRight, 2.2f, Vine, a);
            OutLeaf(g, 330, 62, 42, 0.95f, VineLight, a);
            OutLeaf(g, 338, 96, -38, 0.9f, Vine, a);
            OutLeaf(g, 336, 130, 30, 1f, VineDeep, a);
            OutLeaf(g, 330, 162, -34, 0.9f, VineLight, a);
            OutLeaf(g, 318, 190, 24, 0.95f, Vine, a);
            OutFlower(g, 334, 118, 1.05f, Yellow, true, a);
            OutFlower(g, 318, 170, 0.8f, Coral, false, a);

            // ---- 下弧藤 + 底部大叶
            VineCurve(g, VineBottom, 2.2f, Vine, a);
            OutLeaf(g, 20, 240, -28, 1.15f, VineDeep, a);
            OutLeaf(g, 58, 248, 22, 0.95f, VineLight, a);
            OutLeaf(g, 96, 252, -18, 1.05f, Vine, a);
            OutLeaf(g, 134, 254, 20, 0.9f, VineDeep, a);
            OutLeaf(g, 172, 252, -16, 1.05f, VineLight, a);
            OutLeaf(g, 208, 248, 18, 0.95f, Vine, a);
            OutLeaf(g, 246, 240, -26, 1.1f, VineDeep, a);

            // 左下 / 右下 两簇大叶
            FillPath(g, VineDeep, a, new float[] { 0, 0, 28, -42, 68, -58, 110, -58, 70, -38, 36, -14, 10, 8 }, -28, 238);
            FillPath(g, VineDeep, a, new float[] { 8, 6, 42, -8, 78, -10, 110, 0, 78, 12, 46, 20, 24, 30 }, -28, 238);
            FillPath(g, VineDeep, a, new float[] { 0, 0, -28, -42, -68, -58, -110, -58, -70, -38, -36, -14, -10, 8 }, 312, 238);
            FillPath(g, VineDeep, a, new float[] { -8, 6, -42, -8, -78, -10, -110, 0, -78, 12, -46, 20, -24, 30 }, 312, 238);

            OutFlower(g, -12, 228, 1.45f, Coral, false, a);
            OutFlower(g, 22, 248, 0.8f, Cream, false, a);
            OutFlower(g, 300, 228, 1.4f, Yellow, true, a);
            OutFlower(g, 270, 250, 0.8f, Cream, false, a);
            OutFlower(g, 336, 250, 0.7f, Lilac, false, a);

            // 草莓点 / 白点
            GoalPaint.Dot(g, -46, 206, 4f, Berry, a);
            GoalPaint.Dot(g, -30, 216, 3f, Berry, a);
            GoalPaint.Dot(g, 348, 210, 4f, Berry, a);
            GoalPaint.Dot(g, 332, 220, 3f, Berry, a);
            GoalPaint.Dot(g, -86, 70, 2f, Color.White, a);
            GoalPaint.Dot(g, 382, 70, 2.2f, Color.White, a);
            GoalPaint.Dot(g, -70, 252, 1.8f, Color.White, a);
            GoalPaint.Dot(g, 360, 270, 2f, Color.White, a);

            g.Restore(st);
        }

        /// <summary>收起卡的花环：一段贴着上边缘的小藤 + 两朵花 + 几片叶，其余不画。</summary>
        private void PaintWreathSmall(GoalScene s, float k, float hk, double t, double fade)
        {
            double a = (s.quiet ? 1 : GoalScene.Smooth(GoalScene.Clamp01((t - 0.56) / 0.56))) * fade;
            if (a <= 0.004) return;
            Graphics g = s.g;
            GraphicsState st = g.Save();
            g.ScaleTransform(k, hk);
            // 横跨卡面上沿的一段藤，两端下探到卡角
            VineCurve(g, new float[] { -10, 16, 24, -14, 80, -24, 136, -22, 186, -18, 216, -2, 238, 16 }, 2f, Vine, a);
            OutLeaf(g, 30, -8, -18, 0.85f, VineLight, a);
            OutLeaf(g, 74, -18, 20, 0.8f, Vine, a);
            OutLeaf(g, 128, -22, -16, 0.9f, VineDeep, a);
            OutLeaf(g, 182, -14, 18, 0.8f, VineLight, a);
            OutLeaf(g, 226, 8, -14, 0.85f, VineDeep, a);
            OutFlower(g, 52, -24, 0.85f, Coral, false, a);
            OutFlower(g, 160, -28, 0.8f, Lilac, false, a);
            OutFlower(g, 240, 20, 0.7f, Lilac, false, a);
            GoalPaint.Dot(g, -18, 34, 1.8f, Color.White, a);
            GoalPaint.Dot(g, 232, 46, 1.6f, Berry, a);
            g.Restore(st);
        }

        // ------------------------------------------------------------ 花叶零件

        /// <summary>花：n 片椭圆绕中心放射 + 花心。</summary>
        private static void Petals(Graphics g, int n, float rx, float ry, float dist, float spin, Color c, double a)
        {
            if (a <= 0.004) return;
            using (var b = new SolidBrush(GoalPaint.A(c, a)))
            {
                for (int i = 0; i < n; i++)
                {
                    GraphicsState st = g.Save();
                    g.RotateTransform(spin + i * (360f / n));
                    g.FillEllipse(b, -rx, -dist - ry, rx * 2f, ry * 2f);
                    g.Restore(st);
                }
            }
        }

        private static void Stem(Graphics g, float[] p, float w, Color c, double a)
        {
            if (a <= 0.004) return;
            using (var pen = new Pen(GoalPaint.A(c, a), w))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawBezier(pen, p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]);
            }
        }

        private static void Strawberry(Graphics g, double a, bool small)
        {
            float s = small ? 0.86f : 1f;
            using (var path = new GraphicsPath())
            {
                path.AddBezier(0, 2, 6 * s, 2, 9 * s, 7, 7 * s, 11);
                path.AddBezier(7 * s, 11, 5 * s, 15, -5 * s, 15, -7 * s, 11);
                path.AddBezier(-7 * s, 11, -9 * s, 7, -6 * s, 2, 0, 2);
                path.CloseFigure();
                using (var b = new SolidBrush(GoalPaint.A(Berry, a))) g.FillPath(b, path);
            }
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new PointF[]
                {
                    new PointF(-5 * s, 3), new PointF(0, -1.2f), new PointF(5 * s, 3), new PointF(0, 5)
                });
                using (var b = new SolidBrush(GoalPaint.A(Vine, a))) g.FillPath(b, path);
            }
            GoalPaint.Dot(g, -2.4f * s, 6, 0.65f, Seed, a);
            GoalPaint.Dot(g, 1.6f * s, 5.4f, 0.65f, Seed, a);
            GoalPaint.Dot(g, 0, 9, 0.65f, Seed, a);
            GoalPaint.Dot(g, 3.4f * s, 8.4f, 0.6f, Seed, a);
        }

        /// <summary>贝塞尔闭合填充；p 前 8 个数是两段贝塞尔（起点 + 6 控制点）。</summary>
        private static void FillPath(Graphics g, Color c, double a, float[] p)
        {
            FillPath(g, c, a, p, 0, 0);
        }

        private static void FillPath(Graphics g, Color c, double a, float[] p, float ox, float oy)
        {
            if (a <= 0.004) return;
            using (var path = new GraphicsPath())
            {
                path.AddBezier(p[0] + ox, p[1] + oy, p[2] + ox, p[3] + oy, p[4] + ox, p[5] + oy, p[6] + ox, p[7] + oy);
                if (p.Length >= 14)
                    path.AddBezier(p[6] + ox, p[7] + oy, p[8] + ox, p[9] + oy, p[10] + ox, p[11] + oy, p[12] + ox, p[13] + oy);
                path.CloseFigure();
                using (var b = new SolidBrush(GoalPaint.A(c, a))) g.FillPath(b, path);
            }
        }

        private static void StrokeBezier(Graphics g, float[] p, float w, Color c, double a)
        {
            if (a <= 0.004) return;
            using (var pen = new Pen(GoalPaint.A(c, a), w))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawBezier(pen, p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]);
            }
        }

        /// <summary>四尖星（菱形星），r 是尖到中心。</summary>
        private static void Spark(Graphics g, float cx, float cy, float r, float rot, Color c, double a)
        {
            if (a <= 0.004 || r <= 0.2f) return;
            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double ang = (rot + i * 45.0) * Math.PI / 180.0;
                float rad = (i % 2 == 0) ? r : r * 0.22f;
                pts[i] = new PointF(cx + (float)(Math.Cos(ang) * rad), cy + (float)(Math.Sin(ang) * rad));
            }
            using (var b = new SolidBrush(GoalPaint.A(c, a))) g.FillPolygon(b, pts);
        }

        // ------------------------------------------------------------ 出框零件

        /// <summary>藤：起 + 1~2 段三次贝塞尔。</summary>
        private static void VineCurve(Graphics g, float[] p, float w, Color c, double a)
        {
            if (a <= 0.004) return;
            using (var path = new GraphicsPath())
            {
                path.AddBezier(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]);
                if (p.Length >= 14)
                    path.AddBezier(p[6], p[7], p[8], p[9], p[10], p[11], p[12], p[13]);
                using (var pen = new Pen(GoalPaint.A(c, a), w))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }
        }

        /// <summary>花环上的叶子（o1leaf / o1leaf2 两种，按 rotate 的正负交替）。</summary>
        private static void OutLeaf(Graphics g, float x, float y, float rot, float sc, Color c, double a)
        {
            if (a <= 0.004) return;
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(rot);
            g.ScaleTransform(sc, sc);
            using (var path = new GraphicsPath())
            {
                path.AddBezier(0, 0, -11, -6, -23, -4, -31, 5);
                path.AddBezier(-31, 5, -19, 12, -6, 8, 0, 0);
                path.CloseFigure();
                using (var b = new SolidBrush(GoalPaint.A(c, a))) g.FillPath(b, path);
            }
            g.Restore(st);
        }

        /// <summary>花环上的花（o1fl：5 瓣 rx6.2 ry10.5，花心 4.2）。</summary>
        private static void OutFlower(Graphics g, float x, float y, float sc, Color c, bool brownCore, double a)
        {
            if (a <= 0.004) return;
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(sc, sc);
            Petals(g, 5, 6.2f, 10.5f, 11f, 0f, c, a);
            GoalPaint.Dot(g, 0, 0, 4.2f, brownCore ? Cocoa : Yellow, a);
            g.Restore(st);
        }

        // ------------------------------------------------------------ 动画 / 变换

        /// <summary>s1-pop：0–5% 隐·scale.32·−28°，9% 过冲 1.14·+4°，12% .97，15% 定。</summary>
        private static void Pop(double lt, out float sc, out float rot, out double a)
        {
            if (lt <= 0.35) { sc = 0.32f; rot = -28f; a = 0; return; }
            if (lt < 0.63)
            {
                double p = GoalScene.Smooth((lt - 0.35) / 0.28);
                sc = (float)(0.32 + 0.82 * p);
                rot = (float)(-28 + 32 * p);
                a = GoalScene.Smooth(GoalScene.Clamp01((lt - 0.35) / 0.2));
                return;
            }
            if (lt < 0.84)
            {
                double p = GoalScene.Smooth((lt - 0.63) / 0.21);
                sc = (float)(1.14 - 0.17 * p);
                rot = (float)(4 - 6 * p);
                a = 1;
                return;
            }
            if (lt < 1.05)
            {
                double p = GoalScene.Smooth((lt - 0.84) / 0.21);
                sc = (float)(0.97 + 0.03 * p);
                rot = (float)(-2 + 2 * p);
                a = 1;
                return;
            }
            sc = 1f; rot = 0f; a = 1;
        }

        /// <summary>把坐标系搬到设计坐标 (dx,dy) 并压上 pop / 摇摆，返回要 Restore 的状态。</summary>
        private GraphicsState PushBloom(GoalScene s, float dx, float dy, float delay, double t,
            float k, float hk, out double alpha)
        {
            float sc, rot;
            Pop(t - delay, out sc, out rot, out alpha);
            Graphics g = s.g;
            GraphicsState st = g.Save();
            g.TranslateTransform(Gx(dx, k), Gy(dy, hk));
            float sway = s.quiet
                ? 0f
                : (float)(-1.5 + 3.3 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * ((t + delay * 1.7) / 5.2))));
            g.RotateTransform(sway + rot);
            float u = GardenScale * k * sc;
            g.ScaleTransform(u, u);
            return st;
        }

        /// <summary>设计坐标 → 卡内坐标（含 1.13 缩放）。</summary>
        private static float Gx(float x, float k) { return (GardenCx + (x - GardenCx) * GardenScale) * k; }
        private static float Gy(float y, float hk) { return (GardenCy + (y - GardenCy) * GardenScale) * hk; }

        // ------------------------------------------------------------ 字形路径

        private sealed class TextLine
        {
            public GraphicsPath[] paths;
            public float[] cx;
            public RectangleF box;
        }

        private static readonly Dictionary<string, TextLine> lineCache = new Dictionary<string, TextLine>();
        private static readonly StringFormat GlyphFmt = CreateGlyphFmt();

        private static StringFormat CreateGlyphFmt()
        {
            var f = StringFormat.GenericTypographic.Clone() as StringFormat;
            f.FormatFlags |= StringFormatFlags.NoWrap;
            f.Alignment = StringAlignment.Near;
            f.LineAlignment = StringAlignment.Near;
            return f;
        }

        /// <summary>
        /// 把整行拆成逐字路径（墨迹包围盒精确），按 em 全角步进 + 字距排开。
        /// 返回的路径都是「设备像素 + 原点在行首」，调用方自己平移。
        /// </summary>
        private static TextLine Line(string text, Font font, float em, float track)
        {
            string key = text + "|" + em.ToString("0.##") + "|" + track.ToString("0.##") + "|"
                + font.FontFamily.Name + "|" + (int)font.Style;
            TextLine line;
            if (lineCache.TryGetValue(key, out line)) return line;

            line = new TextLine();
            line.paths = new GraphicsPath[text.Length];
            line.cx = new float[text.Length];
            float x = 0;
            bool first = true;
            for (int i = 0; i < text.Length; i++)
            {
                var p = new GraphicsPath();
                try
                {
                    p.AddString(text.Substring(i, 1), font.FontFamily, (int)font.Style, em, new PointF(x, 0), GlyphFmt);
                }
                catch
                {
                    try { p.AddString(text.Substring(i, 1), font.FontFamily, 0, em, new PointF(x, 0), GlyphFmt); }
                    catch { }
                }
                line.paths[i] = p;
                RectangleF b = p.GetBounds();
                if (first) { line.box = b; first = false; }
                else if (b.Width > 0 && b.Height > 0) line.box = RectangleF.Union(line.box, b);
                line.cx[i] = x + em * 0.5f;
                x += em + track;
            }
            if (lineCache.Count > 48) lineCache.Clear();
            lineCache[key] = line;
            return line;
        }

        // ------------------------------------------------------------ 铺底位图

        private static Bitmap washBitmap;

        /// <summary>280×188 的径向底图（18% 88% 圆心，120%×115% 半径），四段色标，整卡复用。</summary>
        private static Bitmap WashBitmap()
        {
            if (washBitmap != null) return washBitmap;
            const int w = 280, h = 188;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                var buf = new byte[stride * h];
                float cx = w * 0.18f, cy = h * 0.88f, rx = w * 1.2f, ry = h * 1.15f;
                for (int y = 0; y < h; y++)
                {
                    float ny = (y - cy) / ry;
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        float nx = (x - cx) / rx;
                        double d = Math.Sqrt(nx * nx + ny * ny);
                        Color c;
                        if (d <= 0.26) c = Mix(WashIn, Wash2, d / 0.26);
                        else if (d <= 0.58) c = Mix(Wash2, Wash3, (d - 0.26) / 0.32);
                        else if (d <= 1.0) c = Mix(Wash3, WashEdge, (d - 0.58) / 0.42);
                        else c = WashEdge;
                        int i = row + x * 4;
                        buf[i] = c.B; buf[i + 1] = c.G; buf[i + 2] = c.R; buf[i + 3] = 255;
                    }
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(data); }
            washBitmap = bmp;
            return bmp;
        }

        private static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }
    }
}
