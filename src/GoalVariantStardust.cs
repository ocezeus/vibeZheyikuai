using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace WorkCountdown
{
    /// <summary>
    /// 05 · 星屑 Stardust（design/card-spread-notify-v4.html 的 s5）。
    /// 深靛星空从右下角铺满卡面，金屑从进度条末端爆开；金箔字放大聚焦，月牙/行星/星轨出框。
    /// </summary>
    internal sealed class GoalVariantStardust : IGoalVariant
    {
        public string id { get { return "05"; } }
        public string name { get { return "星屑 Stardust"; } }
        public bool hasOutside { get { return true; } }

        // 设计稿配色
        private static readonly Color VeilIn = GoalPaint.Hex("#211A4A");
        private static readonly Color VeilMid = GoalPaint.Hex("#141031");
        private static readonly Color VeilOut = GoalPaint.Hex("#090819");
        private static readonly Color VeilEdge = GoalPaint.Hex("#05040E");
        private static readonly Color NebulaPurple = GoalPaint.Hex("#8A56FF");
        private static readonly Color NebulaPink = GoalPaint.Hex("#FF5EB4");
        private static readonly Color Gold = GoalPaint.Hex("#E8C77B");
        private static readonly Color GoldLight = GoalPaint.Hex("#FFE7B0");
        private static readonly Color GoldPale = GoalPaint.Hex("#FFF1CE");
        private static readonly Color SubInk = GoalPaint.Hex("#F2E2B8");
        private static readonly Color Planet = GoalPaint.Hex("#C99B50");

        // 星场：固定种子，同一帧永远同一张星座
        private sealed class Star { public float x, y, r, phase, speed, alpha; }

        private readonly Star[] stars;
        private readonly float[] burstAngle;
        private readonly float[] burstDist;
        private readonly float[] burstSize;
        private readonly float[] burstDelay;
        private readonly Color[] burstColor;

        public GoalVariantStardust()
        {
            var rnd = new Lcg(5);
            stars = new Star[54];
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i] = new Star();
                stars[i].x = rnd.NextFloat() * 280f;
                stars[i].y = rnd.NextFloat() * 188f;
                stars[i].r = 0.7f + rnd.NextFloat() * 1.0f;
                stars[i].phase = rnd.NextFloat() * 6.283f;
                stars[i].speed = 2.2f + rnd.NextFloat() * 3.4f;
                stars[i].alpha = 0.35f + rnd.NextFloat() * 0.55f;
            }

            burstAngle = new float[44];
            burstDist = new float[44];
            burstSize = new float[44];
            burstDelay = new float[44];
            burstColor = new Color[44];
            for (int i = 0; i < burstAngle.Length; i++)
            {
                // 主要朝左上 / 上方散开，少数往右下补一点
                burstAngle[i] = -170f + rnd.NextFloat() * 210f;
                burstDist[i] = 40f + rnd.NextFloat() * 190f;
                burstSize[i] = 0.8f + rnd.NextFloat() * 1.6f;
                burstDelay[i] = 0.35f + rnd.NextFloat() * 0.5f;
                burstColor[i] = i % 5 == 0 ? GoldPale : (i % 3 == 0 ? GoalPaint.Hex("#FFD98A") : Gold);
            }
        }

        // ---------------------------------------------------------------- 卡内

        public void PaintInside(GoalScene s)
        {
            Graphics g = s.g;
            float W = s.W, H = s.H;
            float k = W / 280f;                 // 收起卡按同一套设计坐标缩一点
            float hk = H / 188f;

            // 1. 星空底：从右下角（进度条末端）圆形铺开。设计稿里铺底是「不透明 + 圆形 clip」，
            //    不是整体 alpha 渐变（4% 就 opacity:1），所以这里只在退场时整体淡出。
            double veilIn = s.Ease(0.15, 0.85);
            double veilAlpha = s.quiet ? 1 : (1 - s.Out);
            if (veilAlpha < 0.01) return;

            float cx = 224f * k, cy = 164f * hk;
            var saved = g.Save();
            if (!s.quiet && veilIn < 1)
            {
                float maxR = (float)Math.Sqrt(W * W + H * H) * 1.2f;
                float r = Math.Max(1f, maxR * (float)GoalScene.Smooth(veilIn));
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                    g.SetClip(clip, CombineMode.Intersect);
                }
            }

            EnsureVeil((int)Math.Round(W), (int)Math.Round(H), s.scale);
            GoalPaint.Layer(g, veil, 0, 0, veilAlpha);

            // 2. 星云：紫（左上）+ 粉（右下），设计稿 blur(20px) → 预烘好的径向柔光小图，
            //    每帧只贴两下（PathGradientBrush 每帧重算会吃掉十几毫秒）
            double neb = (s.quiet ? 1 : s.Ease(0.3, 0.7)) * (1 - s.Out) * (1 - s.Out);
            if (neb > 0.01)
            {
                EnsureNebula(k, hk, s.scale);
                float drift = (float)(s.quiet ? 0 : Math.Sin(s.t * 0.63) * 6);
                GoalPaint.Layer(g, nebPurple, -45 * k + drift, -22 * hk - drift * 0.6f, neb);
                GoalPaint.Layer(g, nebPink, 80 * k - drift, 42 * hk + drift * 0.7f, neb);
            }

            // 3. 星场
            double starA = (s.quiet ? 1 : s.Ease(0.35, 0.5)) * (1 - s.Out);
            if (starA > 0.01)
            {
                for (int i = 0; i < stars.Length; i++)
                {
                    Star st = stars[i];
                    double tw = s.quiet ? 0.62 : 0.12 + 0.73 * (0.5 - 0.5 * Math.Cos(s.t * st.speed + st.phase));
                    GoalPaint.SoftDot(g, st.x * k, st.y * hk, st.r * k, GoldPale, st.alpha * tw * starA);
                }
            }

            // 4. 金屑爆开：从 (224,164) 沿角度飞出，边飞边淡
            double burstVisible = (1 - s.Out) * (s.quiet ? 1 : s.Ease(0.35, 0.2));
            if (burstVisible > 0.01)
            {
                for (int i = 0; i < burstAngle.Length; i++)
                {
                    double local = s.t - burstDelay[i];
                    if (!s.quiet && local <= 0) continue;
                    double p = s.quiet ? 0.34 : GoalScene.Clamp01(local / 5.6);
                    double eased = 1 - Math.Pow(1 - p, 2.4);
                    double a = p < 0.14 ? GoalScene.Clamp01((p - 0.02) / 0.12) : Math.Max(0, 1 - (p - 0.14) / 0.86);
                    double ang = (burstAngle[i] + (p > 0.7 ? 8 : 0)) * Math.PI / 180.0;
                    float dist = (float)(burstDist[i] * eased * k);
                    float px = cx + (float)(Math.Cos(ang) * dist);
                    float py = cy + (float)(Math.Sin(ang) * dist);
                    float size = burstSize[i] * k * (float)(1 - 0.45 * p);
                    GoalPaint.SoftDot(g, px, py, size, burstColor[i], a * burstVisible);
                }
            }

            // 5. 轨道：两层虚线椭圆 + 一颗小星
            double orbitA = (s.quiet ? 1 : s.Ease(0.5, 0.6)) * (1 - s.Out);
            if (orbitA > 0.01)
            {
                float rot = s.quiet ? 0 : (float)(s.t / 26.0 * 360.0);
                GoalPaint.DashedEllipse(g, W / 2f, H / 2f, 112f * k, 50f * k, Gold, Math.Max(1f, 0.9f * s.scale), 2f * s.scale, 5f * s.scale, 0.24 * orbitA, -8 + rot);
                GoalPaint.DashedEllipse(g, W / 2f, H / 2f, 78f * k, 34f * hk, Gold, Math.Max(1f, 0.7f * s.scale), 1.5f * s.scale, 6f * s.scale, 0.17 * orbitA, 7 - rot * 0.6f);
                double orbitDot = (s.quiet ? 0.5 : s.t / 26.0);
                float ox = W / 2f + (float)(Math.Cos((orbitDot * 6.283) - 1.2) * 112 * k);
                float oy = H / 2f + (float)(Math.Sin((orbitDot * 6.283) - 1.2) * 50 * k);
                GoalPaint.SoftDot(g, ox, oy, 2.2f * k, GoldLight, 0.9 * orbitA);
            }

            // 6. 星座连线（设计稿 0.9s 描出来）+ 两颗四角星
            double constA = (s.quiet ? 1 : s.Ease(0.55, 0.45)) * (1 - s.Out);
            if (constA > 0.01)
            {
                var pts = new PointF[]
                {
                    new PointF(28 * k, 48 * hk), new PointF(58 * k, 30 * hk),
                    new PointF(86 * k, 44 * hk), new PointF(108 * k, 26 * hk)
                };
                DrawConstellation(g, pts, 0.38 * constA, s.scale);
                GoalPaint.Sparkle(g, 250 * k, 124 * hk, 11 * k, GoldPale, 0.9 * constA);
                GoalPaint.Sparkle(g, 64 * k, 166 * hk, 7 * k, GoldLight, 0.7 * constA);
            }
            g.Restore(saved);

            // 7. 文字：金箔字 + 细线 + 字距很开的副标题
            PaintText(s, W, H, k, hk);
        }

        // 预烘缓存：星云两块柔光 + 整张星空底（只随卡面尺寸/scale 变，每帧只贴图）
        private Bitmap nebPurple, nebPink;
        private string nebKey;
        private Bitmap veil;
        private string veilKey;

        /// <summary>星云的两块柔光预烘成小图（只随卡面尺寸/scale 变），每帧只贴图。</summary>
        private void EnsureNebula(float k, float hk, float scale)
        {
            string key = (int)Math.Round(230 * k) + "x" + (int)Math.Round(160 * hk) + "|" + scale.ToString("0.##");
            if (nebKey == key && nebPurple != null && nebPink != null) return;
            if (nebPurple != null) { nebPurple.Dispose(); nebPurple = null; }
            if (nebPink != null) { nebPink.Dispose(); nebPink = null; }
            nebPurple = GoalPaint.BakeGlow((int)Math.Round(230 * k), (int)Math.Round(160 * hk), scale, NebulaPurple, 0.30);
            nebPink = GoalPaint.BakeGlow((int)Math.Round(250 * k), (int)Math.Round(180 * hk), scale, NebulaPink, 0.22);
            nebKey = key;
        }

        /// <summary>整张星空底（底 + 三层径向渐变）预烘成一张卡面大小的图，每帧按 veilAlpha 贴一次。</summary>
        private void EnsureVeil(int w, int h, float scale)
        {
            string key = Math.Max(1, w) + "x" + Math.Max(1, h) + "|" + scale.ToString("0.##");
            if (veilKey == key && veil != null) return;
            if (veil != null) { veil.Dispose(); veil = null; }
            veil = new Bitmap(Math.Max(1, w), Math.Max(1, h), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            veil.SetResolution(96f * scale, 96f * scale);
            using (var g = Graphics.FromImage(veil))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                BgFill(g, veil.Width, veil.Height);
            }
            veilKey = key;
        }

        /// <summary>深靛底：三段径向渐变（80%,82% 处最亮）。用三层椭圆叠出设计稿的 radial-gradient。</summary>
        private void BgFill(Graphics g, float W, float H)
        {
            using (var b = new SolidBrush(VeilEdge)) g.FillRectangle(b, 0, 0, W, H);
            float cx = W * 0.80f, cy = H * 0.82f;
            float rx = W * 1.35f, ry = H * 1.45f;
            // 36% 处 #141031，再往外 #090819：用两层椭圆近似
            GoalPaint.GlowEllipse(g, new RectangleF(cx - rx, cy - ry, rx * 2, ry * 2), VeilOut, 0.92);
            rx *= 0.62f; ry *= 0.62f;
            GoalPaint.GlowEllipse(g, new RectangleF(cx - rx, cy - ry, rx * 2, ry * 2), VeilMid, 0.95);
            rx *= 0.55f; ry *= 0.55f;
            GoalPaint.GlowEllipse(g, new RectangleF(cx - rx, cy - ry, rx * 2, ry * 2), VeilIn, 0.85);
        }

        private static void DrawConstellation(Graphics g, PointF[] pts, double alpha, float scale)
        {
            if (alpha <= 0.01) return;
            var pen = new Pen(GoalPaint.A(GoldLight, alpha), Math.Max(1f, 0.8f * scale));
            pen.DashStyle = DashStyle.Custom;
            pen.DashPattern = new float[] { 2f, 4f };
            g.DrawLines(pen, pts);
            pen.Dispose();
            float[] radii = new float[] { 1.7f, 2.1f, 1.5f, 1.9f };
            for (int i = 0; i < pts.Length; i++)
                GoalPaint.SoftDot(g, pts[i].X, pts[i].Y, radii[i] * scale, GoldLight, 0.85 * alpha);
        }

        private void PaintText(GoalScene s, float W, float H, float k, float hk)
        {
            Graphics g = s.g;
            if (s.collapsed)
            {
                // 紧凑版：只留一行金箔字 + 一颗星
                float size = 19f;
                var font = s.Serif(size, FontStyle.Bold);
                double a = (s.quiet ? 1 : s.Ease(0.25, 0.6)) * (1 - s.Out);
                float step = 1.6f * s.scale;
                GoalPaint.TextGradient(g, "今日目标达成", font, W * 0.44f, H / 2f - size * 0.62f,
                    new Color[] { GoalPaint.Hex("#FFE8B2"), GoalPaint.Hex("#D2A250"), GoalPaint.Hex("#FFF4D4"), GoalPaint.Hex("#FFE7B0") },
                    a, step, 104f);
                double pa = (s.quiet ? 1 : s.Ease(0.5, 0.5)) * (1 - s.Out);
                GoalPaint.Sparkle(g, W - 16f * s.scale, H * 0.42f, 7f * s.scale, GoldPale, 0.9 * pa);
                GoalPaint.Glow(g, W - 16f * s.scale, H * 0.42f, 18f * s.scale, Gold, 0.25 * pa);
                return;
            }

            // 展开版：34px 宋体金箔字，10px 下一条金线，13.5px 副标题
            float titleSize = 34f;
            var titleFont = s.Serif(titleSize, FontStyle.Bold);
            var subFont = s.Serif(13.5f);
            float titleW = GoalPaint.TextWidth(g, "今日目标达成", titleFont, 1.7f * s.scale);

            // 文字块整体垂直居中：标题 + 10 间距 + 1 线 + 10 间距 + 副标题
            float subH = 13.5f * s.scale * 1.25f;
            float blockH = titleSize * s.scale * 1.02f + 10 * s.scale + 1 * s.scale + 10 * s.scale + subH;
            float top = (H - blockH) / 2f + 2 * s.scale;

            double inA = s.quiet ? 1 : GoalScene.Smooth(s.Ease(0.55, 0.55));
            double textA = inA * (1 - s.Out);
            float spread = (float)(1 - inA) * 4.5f * s.scale;   // 模糊聚焦 ≈ 轻微散开 + 低透明度
            float dy = (float)(1 - inA) * 4 * s.scale + (float)s.Out * -5 * s.scale;

            // 入场时先铺一层散开的淡金，压住“从模糊里浮出来”的感觉
            if (!s.quiet && inA < 0.98)
            {
                GoalPaint.TextGradient(g, "今日目标达成", titleFont, W / 2f + spread, top + dy - spread * 0.4f,
                    new Color[] { GoldPale, Gold, GoldPale }, textA * 0.55, 1.7f * s.scale, 104f);
            }
            GoalPaint.TextGradient(g, "今日目标达成", titleFont, W / 2f, top + dy,
                new Color[] { GoalPaint.Hex("#FFE8B2"), GoalPaint.Hex("#D2A250"), GoalPaint.Hex("#FFF4D4"), GoalPaint.Hex("#C08B3E"), GoalPaint.Hex("#FFE7B0") },
                textA, 1.7f * s.scale, 104f);

            // 金线：从 0 长到 34
            double ruleA = (s.quiet ? 1 : s.Ease(1.45, 0.35)) * (1 - s.Out);
            if (ruleA > 0.01)
            {
                float rw = 34f * s.scale * (float)(s.quiet ? 1 : GoalScene.Smooth(s.Ease(1.45, 0.45)));
                float ry = top + titleSize * s.scale * 1.02f + 10 * s.scale + dy * 0.4f;
                using (var b = new LinearGradientBrush(new RectangleF(W / 2f - rw / 2f - 1, ry - 1, rw + 2, 3),
                    GoalPaint.A(Gold, 0), GoalPaint.A(Gold, 0), 0f))
                {
                    var blend = new ColorBlend();
                    blend.Colors = new Color[] { GoalPaint.A(Gold, 0), GoalPaint.A(Gold, 0.8 * ruleA), GoalPaint.A(Gold, 0) };
                    blend.Positions = new float[] { 0f, 0.5f, 1f };
                    b.InterpolationColors = blend;
                    g.FillRectangle(b, W / 2f - rw / 2f, ry, Math.Max(1, rw), Math.Max(1, 1f * s.scale));
                }
            }

            // 副标题：字距从 .58em 收紧到 .36em
            double subA = (s.quiet ? 1 : GoalScene.Smooth(s.Ease(1.6, 0.5))) * (1 - s.Out);
            if (subA > 0.01)
            {
                float tracking = (13.5f * (s.quiet ? 0.36f : (0.58f - 0.22f * (float)GoalScene.Smooth(s.Ease(1.6, 0.5))))) * s.scale;
                float sy = top + titleSize * s.scale * 1.02f + 21 * s.scale + dy;
                GoalPaint.Glow(g, W / 2f, sy + subH * 0.45f, 62 * s.scale, Gold, 0.16 * subA);
                // 小字号细笔画的覆盖率低，在深色底上会显得发灰：错开半像素再压一遍
                GoalPaint.TextCenter(g, "工作辛苦了", subFont, W / 2f + 0.45f * s.scale, sy, SubInk, subA, tracking);
                GoalPaint.TextCenter(g, "工作辛苦了", subFont, W / 2f, sy, SubInk, subA, tracking);
            }
        }

        // ---------------------------------------------------------------- 卡外

        public void PaintOutside(GoalScene s)
        {
            Graphics g = s.g;
            float k = s.W / 280f, hk = s.H / 188f;
            double a = (s.quiet ? 1 : GoalScene.Smooth(s.Ease(0.35, 0.75))) * (1 - s.Out);
            if (a <= 0.01) return;

            if (s.collapsed)
            {
                // 紧凑版：右边一个月牙 + 两个小星，其余装饰不画
                float mx = s.W + 34 * k, my = s.H * 0.5f;
                GoalPaint.Glow(g, mx, my, 40 * k, Gold, 0.20 * a);
                GoalPaint.Crescent(g, mx, my, 20 * k, 7 * k, -4 * k, 16 * k, GoldLight, 0.95 * a);
                GoalPaint.Ring(g, mx, my, 26 * k, GoldLight, Math.Max(1f, 0.8f * s.scale), 0.14 * a);
                GoalPaint.Sparkle(g, -14 * k, s.H * 0.28f, 6 * k, GoldPale, 0.7 * a);
                GoalPaint.Sparkle(g, s.W * 0.86f, -12 * k, 5 * k, GoldPale, 0.6 * a);
                return;
            }

            double sway = s.quiet ? 0 : Math.Sin(s.t * 0.7) * 3;
            double floatY = s.quiet ? 0 : Math.Sin(s.t * 0.9 + 1.1) * 4;

            // 大号星轨
            GoalPaint.DashedEllipse(g, 140 * k, 94 * hk, 205 * k, 124 * hk, Gold,
                Math.Max(1f, 1f * s.scale), 2f * s.scale, 7f * s.scale, 0.13 * a, -8 + (float)sway * 0.4f);

            // 右上月牙（330,10）r44，挖掉 (348,0) r38
            float moonX = 330 * k, moonY = 10 * hk + (float)floatY;
            GoalPaint.Glow(g, moonX, moonY, 62 * k, GoldLight, 0.13 * a);
            GoalPaint.Crescent(g, moonX, moonY, 44 * k, 18 * k, -10 * k, 38 * k, GoldLight, 0.98 * a);
            GoalPaint.Ring(g, moonX, moonY, 60 * k, GoldLight, Math.Max(1f, 0.9f * s.scale), 0.12 * a);

            // 左下带环行星（-30,230）r22
            float px = -30 * k, py = 230 * hk - (float)floatY * 0.6f;
            GoalPaint.Glow(g, px, py, 56 * k, Planet, 0.22 * a);
            GoalPaint.Dot(g, px, py, 22 * k, Planet, 0.98 * a);
            GoalPaint.Dot(g, px - 6 * k, py - 7 * k, 7 * k, GoldLight, 0.50 * a);
            var state = g.Save();
            g.TranslateTransform(px, py);
            g.RotateTransform(-16f);
            using (var pen = new Pen(GoalPaint.A(Gold, 0.40 * a), Math.Max(1.5f, 2f * s.scale)))
                g.DrawEllipse(pen, -38 * k, -10 * k, 76 * k, 20 * k);
            GoalPaint.Dot(g, px + 30 * k, py - 10 * k, 3f * k, GoldLight, 0.85 * a);
            g.Restore(state);

            // 左下角外的星座
            DrawConstellation(g, new PointF[]
            {
                new PointF(-70 * k, 36 * hk), new PointF(-22 * k, 12 * hk),
                new PointF(26 * k, 30 * hk), new PointF(64 * k, 2 * hk)
            }, 0.35 * a, s.scale);

            // 四角星 + 零星小点
            GoalPaint.Sparkle(g, -56 * k, 76 * hk, 11 * k, GoldPale, 0.85 * a);
            GoalPaint.Sparkle(g, 356 * k, 150 * hk, 9 * k, GoldLight, 0.80 * a);
            GoalPaint.Sparkle(g, 120 * k, -56 * hk, 8 * k, GoldPale, 0.70 * a);
            GoalPaint.Sparkle(g, 300 * k, 270 * hk, 7 * k, GoldLight, 0.70 * a);
            GoalPaint.Dot(g, 370 * k, 220 * hk, 1.6f * k, GoldLight, 0.55 * a);
            GoalPaint.Dot(g, 20 * k, -60 * hk, 1.4f * k, GoldLight, 0.55 * a);
            GoalPaint.Dot(g, 390 * k, 60 * hk, 1.2f * k, GoldLight, 0.55 * a);
            GoalPaint.Dot(g, -60 * k, 180 * hk, 1.4f * k, GoldLight, 0.55 * a);
        }

        /// <summary>固定种子的小随机数（星场、金屑用；不同帧必须一样）。</summary>
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
