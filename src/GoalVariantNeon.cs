using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WorkCountdown
{
    /// <summary>
    /// 10 · 霓虹灯牌 Neon Sign（design/card-spread-notify-v4.html 的 .s10）。
    /// 暗砖墙从卡心圆形铺开，双层粉霓虹框通电闪烁，六层粉辉光的标题像灯管一样嗡嗡呼吸；
    /// 青线 + 青字副标题、左上八角星、八颗上飘的尘埃；出框是一大圈粉柔光 + 青/粉两个星。
    ///
    /// 性能：砖墙 / 霓虹框辉光 / 卡内柔光 / 标题与副标题的辉光层 / 出框层这些「只跟尺寸和 scale 有关」
    /// 的东西第一次画时烘成位图缓存起来，之后每帧只是按各自的闪烁系数贴图；每帧真正重画的只有
    /// 尘埃、八角星、青线、以及两行清晰文字。
    /// </summary>
    internal sealed class GoalVariantNeon : IGoalVariant
    {
        public string id { get { return "10"; } }
        public string name { get { return "霓虹灯牌 Neon Sign"; } }
        public bool hasOutside { get { return true; } }

        // ---------------------------------------------------------------- 配色（设计稿）

        private static readonly Color WallCore = GoalPaint.Hex("#4C122F");   // 墙心暖紫
        private static readonly Color WallMid = GoalPaint.Hex("#2C111F");    // 中段墨紫
        private static readonly Color WallEdge = GoalPaint.Hex("#28111E");   // 边缘暗紫
        private static readonly Color Mortar = GoalPaint.Hex("#1F1419");     // 砖面
        private static readonly Color MortarSeam = GoalPaint.Hex("#120A10"); // 水平灰缝
        private static readonly Color MortarJoint = GoalPaint.Hex("#180D15"); // 竖缝（比横缝淡一点）
        private static readonly Color Pink = GoalPaint.Hex("#FF3D9A");       // 霓虹粉
        private static readonly Color PinkSoft = GoalPaint.Hex("#FFD3EA");   // 近白的粉
        private static readonly Color TitleInk = GoalPaint.Hex("#FFF2FA");   // 标题本体
        private static readonly Color Cyan = GoalPaint.Hex("#2EDBEA");       // 霓虹青
        private static readonly Color CyanLight = GoalPaint.Hex("#7FF6FF");  // 亮青（灯管芯）
        private static readonly Color SubInk = GoalPaint.Hex("#BFFAFF");     // 副标题
        private static readonly Color DustInk = GoalPaint.Hex("#FFD2EB");    // 尘埃
        private static readonly Color DustGlow = GoalPaint.Hex("#FF6EB4");   // 尘埃辉光

        private const string TitleText = "今日目标达成";
        private const string SubText = "工作辛苦了";

        // ---------------------------------------------------------------- 时间轴（% × 7s）

        private const double Loop = 7.0;
        private const double FrameDelay = 0.55;
        private const double StarDelay = 0.85;
        private const double RuleDelay = 1.10;
        private const double SubDelay = 1.30;

        /// <summary>灯管文字亮度（.sign 的 s10-flicker）：8% 起亮，16%–80% 恒亮，88% 灭。</summary>
        private static readonly double[] SignKeys = new double[]
        {
            0.00, 0.00, 0.07, 0.00, 0.08, 0.90, 0.09, 0.08, 0.10, 1.00, 0.11, 0.20,
            0.12, 1.00, 0.13, 0.45, 0.14, 1.00, 0.15, 0.70, 0.16, 1.00, 0.17, 1.00,
            0.80, 1.00, 0.82, 0.35, 0.84, 0.95, 0.86, 0.40, 0.88, 0.00, 1.00, 0.00
        };

        /// <summary>中景粉辉光亮度（.glow 的 s10-glow）。</summary>
        private static readonly double[] GlowKeys = new double[]
        {
            0.00, 0.00, 0.08, 0.00, 0.10, 0.35, 0.12, 0.05, 0.14, 0.70, 0.16, 0.18,
            0.18, 0.90, 0.22, 0.72, 0.50, 0.62, 0.78, 0.70, 0.84, 0.20, 0.86, 0.55,
            0.89, 0.00, 1.00, 0.00
        };

        /// <summary>霓虹框亮度（.frame 的 s10-frame）。</summary>
        private static readonly double[] FrameKeys = new double[]
        {
            0.00, 0.00, 0.08, 0.00, 0.10, 0.35, 0.12, 0.90, 0.14, 0.25, 0.16, 0.80,
            0.20, 0.72, 0.80, 0.72, 0.84, 0.20, 0.86, 0.60, 0.88, 0.00, 1.00, 0.00
        };

        /// <summary>青线：7% 起从 0 拉到满宽，15% 到位，88% 灭。</summary>
        private static readonly double[] RuleGrowKeys = new double[]
        {
            0.00, 0.00, 0.07, 0.00, 0.15, 1.00, 1.00, 1.00
        };

        private static readonly double[] RuleAlphaKeys = new double[]
        {
            0.00, 0.00, 0.07, 0.00, 0.15, 1.00, 0.80, 0.90, 0.88, 0.00, 1.00, 0.00
        };

        /// <summary>副标题：8%→16% 淡入并收字距，88% 灭。</summary>
        private static readonly double[] SubAlphaKeys = new double[]
        {
            0.00, 0.00, 0.08, 0.00, 0.16, 1.00, 0.80, 1.00, 0.88, 0.00, 1.00, 0.00
        };

        // 尘埃八颗：位置（卡面百分比）、水平漂移（DIP）、起播延迟（s）
        private static readonly float[] DustX = { 0.16f, 0.28f, 0.72f, 0.84f, 0.22f, 0.68f, 0.44f, 0.56f };
        private static readonly float[] DustY = { 0.30f, 0.18f, 0.24f, 0.44f, 0.72f, 0.78f, 0.84f, 0.12f };
        private static readonly float[] DustDx = { 9f, -6f, 7f, -8f, 6f, -5f, 4f, -4f };
        private static readonly double[] DustDelay = { 0.2, 1.4, 2.6, 3.8, 4.6, 5.4, 1.1, 3.3 };

        // 八角星（s8/s10 共用的十顶点星形，归一化到 0..1）
        private static readonly float[] StarTenPts = new float[]
        {
            0.50f, 0.00f, 0.61f, 0.35f, 0.98f, 0.35f, 0.68f, 0.57f, 0.79f, 0.93f,
            0.50f, 0.70f, 0.21f, 0.93f, 0.32f, 0.57f, 0.02f, 0.35f, 0.39f, 0.35f
        };

        // ---------------------------------------------------------------- 版面（预烘与实时绘制共用）

        /// <summary>一次绘制的全部静态几何（像素）。展开卡和收起卡各一套。</summary>
        private struct Layout
        {
            public float W, H, sc, k, hk;
            public RectangleF frame;
            public float frameRadius;
            public float titleCx, titleY, titleSize, titleTrack;
            public float titleHalfW, titleHalfH;      // 标题辉光烘焙框（半宽 / 半高）
            public float subCx, subY, subSize, subTrack;
            public float subHalfW, subHalfH;
            public float ruleY, ruleW;
            public bool hasRule;
            public float starCx, starCy, starSize;
            public float outPad;                      // 出框层烘焙留白
        }

        private static Layout MakeLayout(GoalScene s)
        {
            var L = new Layout();
            L.W = s.W; L.H = s.H; L.sc = s.scale;
            if (s.collapsed)
            {
                L.k = s.scale;
                L.hk = s.scale;
                L.frame = new RectangleF(5f * L.sc, 5f * L.sc, s.W - 10f * L.sc, s.H - 10f * L.sc);
                L.frameRadius = 8f * L.sc;
                L.titleCx = 122f * L.sc;
                L.titleY = 12.5f * L.sc;
                L.titleSize = 21f;
                L.titleTrack = 0.9f * L.sc;
                L.titleHalfW = 104f * L.sc;
                L.titleHalfH = 46f * L.sc;
                L.subCx = 122f * L.sc;
                L.subY = 40.5f * L.sc;
                L.subSize = 10.5f;
                L.subTrack = 10.5f * 0.38f * L.sc;
                L.subHalfW = 84f * L.sc;
                L.subHalfH = 30f * L.sc;
                L.hasRule = false;
                L.starCx = 20f * L.sc;
                L.starCy = 18f * L.sc;
                L.starSize = 16f * L.sc;
                L.outPad = 70f * L.sc;
            }
            else
            {
                L.k = s.W / 280f;
                L.hk = s.H / 188f;
                L.frame = new RectangleF(8f * L.k, 8f * L.hk, s.W - 16f * L.k, s.H - 16f * L.hk);
                L.frameRadius = 13f * L.k;
                L.titleCx = s.W / 2f;
                L.titleY = 52.5f * L.hk;
                L.titleSize = 34f;
                L.titleTrack = 1.36f * L.sc;
                L.titleHalfW = 152f * L.k;
                L.titleHalfH = 64f * L.hk;
                L.subCx = s.W / 2f;
                L.subY = 123.4f * L.hk;
                L.subSize = 13.5f;
                L.subTrack = 13.5f * 0.38f * L.sc;
                L.subHalfW = 132f * L.k;
                L.subHalfH = 42f * L.hk;
                L.ruleY = 107.9f * L.hk;
                L.ruleW = 98f * L.k;
                L.hasRule = true;
                L.starCx = 37f * L.k;
                L.starCy = 37f * L.hk;
                L.starSize = 26f * L.k;
                L.outPad = 150f * L.sc;
            }
            return L;
        }

        // ---------------------------------------------------------------- 预烘缓存

        /// <summary>只随「卡面尺寸 + scale + DPI」变的一堆图层位图。</summary>
        private sealed class Bake
        {
            public int w, h;
            public float scale, dpiX, dpiY;
            public bool collapsed;
            public Bitmap wall;        // 砖墙（满卡，不透明）
            public Bitmap glow;        // 卡内中景粉辉光
            public Bitmap bloom;       // 标题后面的大范围粉雾（仅展开卡）
            public Bitmap frame;       // 霓虹框的几层辉光 + 实管
            public Bitmap titleGlow;   // 标题六层辉光（无清晰本体）
            public Bitmap subGlow;     // 副标题三层青辉光
            public Bitmap outside;     // 出框层（硬框 + 大柔光 + 两个星 + 散点）
        }

        private Bake bake;

        private Bake EnsureBake(GoalScene s)
        {
            int w = (int)Math.Round(s.W), h = (int)Math.Round(s.H);
            if (bake != null && bake.w == w && bake.h == h && bake.collapsed == s.collapsed
                && Math.Abs(bake.scale - s.scale) < 0.001f
                && Math.Abs(bake.dpiX - s.g.DpiX) < 0.5f && Math.Abs(bake.dpiY - s.g.DpiY) < 0.5f)
                return bake;
            Bake old = bake;
            bake = Build(s);
            DisposeBake(old);
            return bake;
        }

        private static void DisposeBake(Bake b)
        {
            if (b == null) return;
            if (b.wall != null) b.wall.Dispose();
            if (b.glow != null) b.glow.Dispose();
            if (b.bloom != null) b.bloom.Dispose();
            if (b.frame != null) b.frame.Dispose();
            if (b.titleGlow != null) b.titleGlow.Dispose();
            if (b.subGlow != null) b.subGlow.Dispose();
            if (b.outside != null) b.outside.Dispose();
        }

        /// <summary>把一段静态绘制烘进位图：ox/oy 是这段绘制在卡面坐标里的左上角。</summary>
        private delegate void BakeStep(Graphics g, GoalScene s);

        private static Bitmap BakeLayer(GoalScene src, int px, int py, float ox, float oy, BakeStep step)
        {
            int w = Math.Max(1, px), h = Math.Max(1, py);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            try
            {
                bmp.SetResolution(src.g.DpiX, src.g.DpiY);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    g.TranslateTransform(-ox, -oy);
                    var s = new GoalScene();
                    s.g = g;
                    s.W = src.W;
                    s.H = src.H;
                    s.scale = src.scale;
                    s.collapsed = src.collapsed;
                    s.t = src.t;
                    s.In = 1;
                    s.Out = 0;
                    step(g, s);
                }
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
            return bmp;
        }

        private Bake Build(GoalScene s)
        {
            var b = new Bake();
            var L = MakeLayout(s);
            b.w = (int)Math.Round(s.W);
            b.h = (int)Math.Round(s.H);
            b.scale = s.scale;
            b.collapsed = s.collapsed;
            b.dpiX = s.g.DpiX;
            b.dpiY = s.g.DpiY;

            // 1. 砖墙：底色 + 砖缝 + 十字缝 + 压暗
            b.wall = BakeLayer(s, b.w, b.h, 0f, 0f, delegate(Graphics g, GoalScene ls)
            {
                PaintWall(g, ls, 1.0);
            });

            // 2. 卡内中景粉辉光（.glow）
            b.glow = BakeLayer(s, b.w, b.h, 0f, 0f, delegate(Graphics g, GoalScene ls)
            {
                if (s.collapsed)
                    SoftGlow(g, ls.W / 2f, ls.H / 2f, 98f * L.sc, 28f * L.sc, Pink, 0.32, 4);
                else
                    SoftGlow(g, 140f * L.k, 90.2f * L.hk, 130f * L.k, 60f * L.hk, Pink, 0.34, 4);
            });

            // 3. 标题后面的大范围粉雾（只展开卡有）
            if (!s.collapsed)
            {
                b.bloom = BakeLayer(s, b.w, b.h, 0f, 0f, delegate(Graphics g, GoalScene ls)
                {
                    SoftGlow(g, ls.W / 2f, 78f * L.hk, 226f * L.k, 128f * L.hk, Pink, 0.34, 5);
                });
            }

            // 4. 霓虹框
            b.frame = BakeLayer(s, b.w, b.h, 0f, 0f, delegate(Graphics g, GoalScene ls)
            {
                PaintFrame(g, ls, L.frame, L.frameRadius, 1.0);
            });

            // 5. 标题辉光层（六层粉，不带清晰本体）
            int tw = (int)Math.Ceiling(L.titleHalfW * 2f), th = (int)Math.Ceiling(L.titleHalfH * 2f);
            float tox = L.titleCx - L.titleHalfW, toy = L.titleY + L.titleSize * L.sc * 0.55f - L.titleHalfH;
            b.titleGlow = BakeLayer(s, tw, th, tox, toy, delegate(Graphics g, GoalScene ls)
            {
                Font font = ls.Sans(L.titleSize, FontStyle.Bold);
                double alpha = 1.0;
                GoalPaint.TextGlow(g, TitleText, font, L.titleCx, L.titleY, Pink, Pink,
                    alpha * 0.85, L.titleTrack, 13f * L.sc, 2);
                GoalPaint.TextGlow(g, TitleText, font, L.titleCx, L.titleY, PinkSoft, PinkSoft,
                    alpha * 0.50, L.titleTrack, 3f * L.sc, 2);
                GoalPaint.TextGlow(g, TitleText, font, L.titleCx, L.titleY, Color.White, Color.White,
                    alpha * 0.40, L.titleTrack, 1f * L.sc, 1);
            });

            // 6. 副标题辉光层（三层青，按最终字距烘）
            int sw = (int)Math.Ceiling(L.subHalfW * 2f), sh = (int)Math.Ceiling(L.subHalfH * 2f);
            float sox = L.subCx - L.subHalfW, soy = L.subY + L.subSize * L.sc * 0.55f - L.subHalfH;
            b.subGlow = BakeLayer(s, sw, sh, sox, soy, delegate(Graphics g, GoalScene ls)
            {
                Font font = ls.Sans(L.subSize);
                GoalPaint.TextGlow(g, SubText, font, L.subCx, L.subY, Cyan, Cyan,
                    0.60, L.subTrack, 4f * L.sc, 2);
                GoalPaint.TextGlow(g, SubText, font, L.subCx, L.subY, CyanLight, CyanLight,
                    0.55, L.subTrack, 1.5f * L.sc, 2);
            });

            // 7. 出框层按需烘（PaintOutside 第一次调用时再建，避免白等）
            return b;
        }

        /// <summary>出框层整层烘一张（配合 140 DIP 的出框窗口留白）。</summary>
        private static Bitmap BakeOutside(GoalScene s, Layout L)
        {
            int ow = (int)Math.Round(s.W) + (int)Math.Ceiling(L.outPad * 2f);
            int oh = (int)Math.Round(s.H) + (int)Math.Ceiling(L.outPad * 2f);
            return BakeLayer(s, ow, oh, -L.outPad, -L.outPad, delegate(Graphics g, GoalScene ls)
            {
                PaintOutsideArt(g, ls, L, 1.0);
            });
        }

        private static void Blit(Graphics g, Bitmap bmp, float x, float y, double alpha)
        {
            if (bmp == null || alpha <= 0.004) return;
            float w = bmp.Width, h = bmp.Height;
            var dst = new PointF[]
            {
                new PointF(x, y), new PointF(x + w, y), new PointF(x, y + h)
            };
            var src = new RectangleF(0, 0, w, h);
            if (alpha >= 0.998)
            {
                g.DrawImage(bmp, dst, src, GraphicsUnit.Pixel);
                return;
            }
            using (var attrs = new ImageAttributes())
            {
                var cm = new ColorMatrix();
                cm.Matrix33 = (float)Math.Min(1.0, alpha);
                attrs.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(bmp, dst, src, GraphicsUnit.Pixel, attrs);
            }
        }

        // ---------------------------------------------------------------- 时间轴采样

        /// <summary>折线查表 + 线性插值：keys 是 {位置, 值, 位置, 值…}，位置 0..1。</summary>
        private static double Curve(double[] keys, double p)
        {
            int n = keys.Length / 2;
            if (n == 0) return 1;
            if (p <= keys[0]) return keys[1];
            for (int i = 1; i < n; i++)
            {
                double x1 = keys[i * 2];
                if (p > x1) continue;
                double x0 = keys[(i - 1) * 2];
                double y0 = keys[(i - 1) * 2 + 1];
                double y1 = keys[i * 2 + 1];
                double span = x1 - x0;
                if (span <= 0) return y1;
                return y0 + (y1 - y0) * GoalScene.Clamp01((p - x0) / span);
            }
            return keys[keys.Length - 1];
        }

        /// <summary>带延迟的采样：延迟前保持 0%，之后按本段 7s 走。</summary>
        private static double Delayed(double[] keys, double delay, double t)
        {
            if (t < delay) return 0;
            return Curve(keys, (t - delay) / Loop);
        }

        private static double SignLevel(GoalScene s)
        {
            return s.quiet ? 1.0 : Curve(SignKeys, s.t / Loop);
        }

        private static double GlowLevel(GoalScene s)
        {
            return s.quiet ? 0.62 : Curve(GlowKeys, s.t / Loop);
        }

        private static double FrameLevel(GoalScene s)
        {
            return s.quiet ? 0.72 : Delayed(FrameKeys, FrameDelay, s.t);
        }

        /// <summary>标题 3.6s 呼吸（CSS 的 filter:brightness），静态帧取 1.0。</summary>
        private static double Hum(GoalScene s)
        {
            if (s.quiet) return 1.0;
            double p = (s.t % 3.6) / 3.6;
            if (p < 0.48) return 1.0 + 0.12 * GoalScene.Smooth(p / 0.48);
            if (p < 0.52) return 1.12 - 0.16 * GoalScene.Smooth((p - 0.48) / 0.04);
            return 0.96 + 0.04 * GoalScene.Smooth((p - 0.52) / 0.48);
        }

        /// <summary>砖墙不透明度：0.21s 起亮，5.60s–6.16s 淡出。</summary>
        private static double WallAlpha(GoalScene s)
        {
            if (s.quiet) return 1.0;
            double t = s.t;
            if (t < 0.21) return 0;
            if (t < 0.28) return (t - 0.21) / 0.07;
            if (t > 5.60) return Math.Max(0, 1 - (t - 5.60) / 0.56);
            return 1.0;
        }

        /// <summary>砖墙圆形揭示：0→46%（0.91s）→160%（1.40s），返回 0..1 的半径比例。</summary>
        private static double WallRadius(GoalScene s)
        {
            double t = s.t;
            if (t <= 0.21) return 0;
            if (t < 0.91) return 0.2875 * GoalScene.Smooth((t - 0.21) / 0.70);
            if (t < 1.40) return 0.2875 + 0.7125 * GoalScene.Smooth((t - 0.91) / 0.49);
            return 1.0;
        }

        /// <summary>出框层的显隐：8%–16% 淡入、80%–90% 淡出。</summary>
        private static double OutsideLevel(GoalScene s)
        {
            if (s.quiet) return 1.0;
            double t = s.t, a;
            if (t <= 0.56) a = 0;
            else if (t < 1.12) a = GoalScene.Smooth((t - 0.56) / 0.56);
            else a = 1;
            if (t > 5.60) a *= Math.Max(0, 1 - (t - 5.60) / 0.70);
            return a;
        }

        /// <summary>八角星：pop 入场（8% 起 12% 到 1.1 倍），16% 定，88% 灭。</summary>
        private static double StarLevel(GoalScene s, out float scale, out float rot)
        {
            scale = 1f;
            rot = 0f;
            if (s.quiet) return 0.90;
            if (s.t < StarDelay) return 0;
            double p = (s.t - StarDelay) / Loop;
            if (p < 0.08) return 0;
            if (p < 0.12)
            {
                double u = GoalScene.Smooth((p - 0.08) / 0.04);
                scale = (float)(0.40 + 0.70 * u);
                rot = (float)(-20 + 24 * u);
                return u;
            }
            if (p < 0.16)
            {
                double u = GoalScene.Smooth((p - 0.12) / 0.04);
                scale = (float)(1.10 - 0.10 * u);
                rot = (float)(4 - 4 * u);
                return 1.00 - 0.10 * u;
            }
            if (p < 0.80) return 0.90;
            if (p < 0.88)
            {
                double u = GoalScene.Smooth((p - 0.80) / 0.08);
                scale = (float)(1.00 - 0.10 * u);
                return 0.90 * (1 - u);
            }
            scale = 0.90f;
            return 0;
        }

        /// <summary>单颗尘埃：18% 起飘，30% 最亮，80% 飘到 (dx,-18)，88% 灭于 (dx,-26)。</summary>
        private static double Mote(GoalScene s, int i, out float dx, out float dy)
        {
            dx = 0;
            dy = 0;
            if (s.quiet) { dx = DustDx[i] * 0.4f; dy = -7f; return 0.45; }
            double p = (s.t - DustDelay[i]) / Loop;
            if (p < 0.18 || p >= 0.88) return 0;
            double a;
            if (p < 0.30) a = 0.70 * GoalScene.Smooth((p - 0.18) / 0.12);
            else if (p < 0.70) a = 0.70 - 0.30 * ((p - 0.30) / 0.40);
            else if (p < 0.80) a = 0.40 + 0.25 * ((p - 0.70) / 0.10);
            else a = 0.65 * (1 - (p - 0.80) / 0.08);
            double u = p < 0.80 ? (p - 0.18) / 0.62 : 1.0;
            dx = (float)(DustDx[i] * u);
            dy = (float)(p < 0.80 ? -18.0 * u : -18.0 - 8.0 * ((p - 0.80) / 0.08));
            return a;
        }

        // ---------------------------------------------------------------- 卡内

        public void PaintInside(GoalScene s)
        {
            if (s.Out >= 1.0) return;
            Bake b = EnsureBake(s);
            var L = MakeLayout(s);
            Graphics g = s.g;
            double outA = 1.0 - s.Out;

            // ---- 1. 砖墙：从卡心 (140,86) 圆形铺开（只贴一次缓存的墙）
            double wallA = WallAlpha(s) * outA;
            if (wallA > 0.004)
            {
                var state = g.Save();
                if (!s.quiet)
                {
                    double rp = WallRadius(s);
                    if (rp < 0.999)
                    {
                        float cx = L.W / 2f, cy = L.H * 0.46f;
                        float maxR = 427f * s.scale;
                        float r = Math.Max(1f, maxR * (float)GoalScene.Smooth(rp));
                        using (var clip = new GraphicsPath())
                        {
                            clip.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                            g.SetClip(clip, CombineMode.Intersect);
                        }
                    }
                }
                Blit(g, b.wall, 0, 0, wallA);
                g.Restore(state);
            }

            // ---- 2. 卡内中景粉辉光
            Blit(g, b.glow, 0, 0, GlowLevel(s) * outA);

            // ---- 3. 霓虹框
            Blit(g, b.frame, 0, 0, FrameLevel(s) * outA);

            // ---- 4. 左上八角星 (24,24) 26×26
            float starScale, starRot;
            double starA = StarLevel(s, out starScale, out starRot) * outA;
            if (starA > 0.004)
            {
                float size = L.starSize * starScale;
                StarTen(g, L.starCx, L.starCy, size, starRot, starA);
            }

            // ---- 5. 尘埃（在文字之下）
            PaintDust(g, s, L, outA);

            // ---- 6. 灯管文字
            PaintSign(g, s, b, L, outA);
        }

        /// <summary>墙：底色 + 砖缝 + 三层径向压暗（等价 CSS 的 radial-gradient）。只在预烘时调用。</summary>
        private static void PaintWall(Graphics g, GoalScene s, double a)
        {
            float sc = s.scale, W = s.W, H = s.H;
            float cx = W * 0.5f, cy = H * 0.46f;

            // 砖面 + 砖缝（先铺满，径向渐变压在上面）
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(GoalPaint.A(Mortar, a))) g.FillRectangle(b, 0, 0, W, H);

            // 横向砖砌：一行 26 DIP + 2 DIP 灰缝；竖缝每 56 DIP 一条、隔行错开半块（28 DIP）。
            // 墙面一直铺到卡片自己的 18 DIP 圆角边缘（裁剪由调用方负责），不另做内缩裁切
            float rowH = 26f * sc;                                  // 一行砖高（含灰缝）
            float seamH = Math.Max(1f, 2f * sc);                    // 水平灰缝（比竖缝重一点）
            float brickW = 56f * sc;                                // 一块砖宽
            float jointW = Math.Max(1f, 1f * sc);                   // 竖缝
            using (var seam = new SolidBrush(GoalPaint.A(MortarSeam, a)))
            using (var joint = new SolidBrush(GoalPaint.A(MortarJoint, a)))
            {
                int row = 0;
                for (float y = 0; y < H; y += rowH, row++)
                {
                    g.FillRectangle(seam, 0, (float)Math.Floor(y + rowH - seamH), W, seamH);
                    float shift = (row % 2 == 0) ? 0f : brickW * 0.5f;   // 隔行错半块
                    for (float x = shift - brickW; x < W + brickW; x += brickW)
                        g.FillRectangle(joint, (float)Math.Floor(x), y, jointW, Math.Max(1f, rowH - seamH));
                }
            }
            g.SmoothingMode = old;

            // 径向：#4C122F α70 → #2C111F α84（58%）→ #28111E α92。
            // CSS 的 radial-gradient(120% 110%) 里百分比是「半径对盒子尺寸」，所以 rx=1.2×盒宽、ry=1.1×盒高
            var box = new RectangleF(cx - 376f * sc, cy - 231.6f * sc, 752f * sc, 463.2f * sc);
            RadialFill(g, box, new Color[]
            {
                GoalPaint.A(WallCore, 0.70 * a),
                GoalPaint.A(WallMid, 0.84 * a),
                GoalPaint.A(WallEdge, 0.92 * a)
            }, new float[] { 0f, 0.58f, 1f });
        }

        /// <summary>霓虹框：青大光晕 + 粉外晕 + 实管 + 内晕（不用位图模糊）。只在预烘时调用。</summary>
        private static void PaintFrame(Graphics g, GoalScene s, RectangleF rect, float radius, double a)
        {
            float sc = s.scale;
            using (var path = GoalPaint.RoundRect(rect, radius))
            {
                // box-shadow: 0 0 22px rgba(46,219,234,.12)
                StrokeGlow(g, path, Cyan, 1.5f * sc, 15f * sc, 0.07 * a, 3);
                // box-shadow: 0 0 6px rgba(255,61,154,.55)
                StrokeGlow(g, path, Pink, 1.5f * sc, 6f * sc, 0.55 * a, 3);
                // 实管
                using (var pen = new Pen(GoalPaint.A(Pink, 0.62 * a), Math.Max(1f, 1.5f * sc)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
                // box-shadow: inset 0 0 10px rgba(255,61,154,.18)
                var state = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                StrokeGlow(g, path, Pink, 1f * sc, 10f * sc, 0.18 * a, 2);
                g.Restore(state);
            }
        }

        private static void PaintDust(Graphics g, GoalScene s, Layout L, double outA)
        {
            int count = s.collapsed ? 3 : DustX.Length;
            for (int i = 0; i < count; i++)
            {
                float dx, dy;
                double a = Mote(s, i, out dx, out dy) * outA;
                if (a <= 0.004) continue;
                float x = DustX[i] * s.W + dx * L.k;
                float y = DustY[i] * s.H + dy * L.hk;
                GoalPaint.Glow(g, x, y, 4.5f * s.scale, DustGlow, 0.55 * a);
                GoalPaint.Dot(g, x, y, Math.Max(0.7f, 0.7f * s.scale), DustInk, 0.65 * a);
            }
        }

        // ---------------------------------------------------------------- 文字

        /// <summary>
        /// 灯管文字：辉光层全部贴预烘位图，每帧只按闪烁系数贴一次 + 画一遍清晰本体。
        /// </summary>
        private static void PaintSign(Graphics g, GoalScene s, Bake b, Layout L, double outA)
        {
            double signA = SignLevel(s) * outA;
            if (signA <= 0.004) return;
            double hum = Hum(s);
            double lit = Math.Min(1.0, signA * hum);

            // 最外层大范围粉雾（对应 44/70px 两层阴影）
            Blit(g, b.bloom, 0f, 0f, lit);

            // 六层粉辉光（预烘）
            Font titleFont = s.Sans(L.titleSize, FontStyle.Bold);
            Blit(g, b.titleGlow, L.titleCx - L.titleHalfW,
                L.titleY + L.titleSize * L.sc * 0.55f - L.titleHalfH, lit);
            // 清晰本体
            GoalPaint.TextCenter(g, TitleText, titleFont, L.titleCx, L.titleY, TitleInk, signA, L.titleTrack);

            // 青线 98×1（delay 1.1s，7% 起、15% 满宽）——宽度在入场时是动的，实时画
            if (L.hasRule && (!s.quiet || s.t >= RuleDelay))
            {
                double grow = s.quiet ? 1.0 : Delayed(RuleGrowKeys, RuleDelay, s.t);
                double ruleA = (s.quiet ? 0.9 : Delayed(RuleAlphaKeys, RuleDelay, s.t)) * outA * SignLevel(s);
                float rw = L.ruleW * (float)GoalScene.Smooth(grow);
                if (ruleA > 0.004 && rw > 1f)
                    CyanRule(g, L.W / 2f, L.ruleY, rw, s.scale, ruleA);
            }

            // 副标题：13.5px，字距 .58em→.38em（字距是动的，文字实时画；辉光贴预烘）
            double subGrow = s.quiet ? 1.0 : Delayed(SubAlphaKeys, SubDelay, s.t);
            double subA = (s.quiet ? 1.0 : Delayed(SubAlphaKeys, SubDelay, s.t)) * outA * SignLevel(s);
            if (subA > 0.004)
            {
                Font subFont = s.Sans(L.subSize);
                float from = 0.58f, to = 0.38f;
                float subTrack = L.subSize * (from + (to - from) * (float)GoalScene.Smooth(subGrow)) * s.scale;
                Blit(g, b.subGlow, L.subCx - L.subHalfW,
                    L.subY + L.subSize * L.sc * 0.55f - L.subHalfH, subA);
                GoalPaint.TextCenter(g, SubText, subFont, L.subCx, L.subY, SubInk, subA, subTrack);
            }
        }

        /// <summary>青色灯管细线：1px 实线 + 三层青辉光（5/12/22px 的模糊等效）。</summary>
        private static void CyanRule(Graphics g, float cx, float cy, float w, float sc, double a)
        {
            float half = w / 2f;
            GoalPaint.GlowEllipse(g, new RectangleF(cx - half - 11f * sc, cy - 11f * sc, w + 22f * sc, 22f * sc), Cyan, 0.20 * a);
            GoalPaint.GlowEllipse(g, new RectangleF(cx - half - 6f * sc, cy - 6f * sc, w + 12f * sc, 12f * sc), Cyan, 0.34 * a);
            GoalPaint.GlowEllipse(g, new RectangleF(cx - half - 2.5f * sc, cy - 2.5f * sc, w + 5f * sc, 5f * sc), CyanLight, 0.55 * a);
            using (var b = new SolidBrush(GoalPaint.A(CyanLight, 0.95 * a)))
                g.FillRectangle(b, cx - half, cy - Math.Max(0.5f, 0.5f * sc), Math.Max(1f, w), Math.Max(1f, 1f * sc));
        }

        // ---------------------------------------------------------------- 卡外出框层

        public void PaintOutside(GoalScene s)
        {
            Bake b = EnsureBake(s);
            var L = MakeLayout(s);
            double a = OutsideLevel(s) * (1 - s.Out);
            if (a <= 0.004) return;
            if (b.outside == null) b.outside = BakeOutside(s, L);
            Blit(s.g, b.outside, -L.outPad, -L.outPad, a);
        }

        /// <summary>出框层的全部内容（预烘时 a=1，每帧只按 a 贴图）。</summary>
        private static void PaintOutsideArt(Graphics g, GoalScene s, Layout L, double a)
        {
            float k = L.k, hk = L.hk, sc = L.sc;

            if (s.collapsed)
            {
                float W = s.W, H = s.H;
                SoftGlow(g, W / 2f, H / 2f, 132f * sc, 64f * sc, Pink, 0.26 * 0.65 * a, 5);
                using (var path = GoalPaint.RoundRect(new RectangleF(-9f * sc, -9f * sc, W + 18f * sc, H + 18f * sc), 18f * sc))
                {
                    StrokeGlow(g, path, Pink, 1f * sc, 10f * sc, 0.45 * a, 3);
                    using (var pen = new Pen(GoalPaint.A(Pink, 0.55 * a), Math.Max(1f, 1.2f * sc)))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, path);
                    }
                }
                Sparkle4(g, -14f * sc, 14f * sc, 12f * sc, CyanLight, Cyan, a);
                Sparkle4(g, W + 14f * sc, H + 8f * sc, 9f * sc, PinkSoft, Pink, a);
                return;
            }

            // 大范围粉柔光：rx285 / ry190，用多层同心椭圆叠，边缘完全化开
            SoftGlow(g, 140f * k, 94f * hk, 285f * k, 190f * hk, Pink, 0.30 * 0.65 * a, 6);
            SoftGlow(g, 140f * k, 94f * hk, 150f * k, 104f * hk, Pink, 0.10 * 0.65 * a, 4);

            // 圆角硬框 rect(-16,-16,312,220) rx34，1.6px α62% + 两层投影
            using (var path = GoalPaint.RoundRect(
                new RectangleF(-16f * k, -16f * hk, 312f * k, 220f * hk), 34f * k))
            {
                StrokeGlow(g, path, Pink, 1.2f * sc, 18f * sc, 0.50 * a * 0.55, 3);
                StrokeGlow(g, path, Pink, 1.4f * sc, 6f * sc, 0.80 * a, 3);
                using (var pen = new Pen(GoalPaint.A(Pink, 0.62 * a), Math.Max(1f, 1.6f * sc)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }

            // 左下大青星 (-30,22) 高 44（半高 22）
            Sparkle4(g, -30f * k, 22f * hk, 22f * k, CyanLight, Cyan, a);
            // 右下粉星 (326,238) 高 30
            Sparkle4(g, 326f * k, 238f * hk, 15f * k, PinkSoft, Pink, a);

            // 几颗散点
            GoalPaint.GlowDot(g, -58f * k, 200f * hk, 1.8f * k, PinkSoft, 0.70 * a);
            GoalPaint.GlowDot(g, 350f * k, 56f * hk, 1.6f * k, PinkSoft, 0.70 * a);
            GoalPaint.GlowDot(g, 110f * k, -66f * hk, 1.4f * k, PinkSoft, 0.70 * a);
            GoalPaint.GlowDot(g, 390f * k, 180f * hk, 1.3f * k, PinkSoft, 0.70 * a);
        }

        // ---------------------------------------------------------------- 小图形

        /// <summary>
        /// 多层同心椭圆柔光：layers 层半径逐层放大、单层 α 都很低，
        /// 叠起来中心浓、边缘一层层化开，任何半径处都看不出椭圆外沿（外层可远大于卡面）。
        /// peak 是叠加后中心的总强度。
        /// </summary>
        private static void SoftGlow(Graphics g, float cx, float cy, float rx, float ry, Color color,
            double peak, int layers)
        {
            if (rx <= 0.5f || ry <= 0.5f || peak <= 0.004) return;
            if (layers < 1) layers = 1;
            // 每层同浓度，N 层叠加后的中心浓度正好是 peak
            double each = 1.0 - Math.Pow(Math.Max(0.0001, 1.0 - Math.Min(0.98, peak)), 1.0 / layers);
            for (int i = layers; i >= 1; i--)
            {
                float f = i / (float)layers;
                // 半径按 sqrt 分布：内层密、外层疏，衰减更像高斯
                float t = (float)Math.Sqrt(f);
                float sx = Math.Max(1f, rx * t), sy = Math.Max(1f, ry * t);
                GoalPaint.GlowEllipse(g, new RectangleF(cx - sx, cy - sy, sx * 2f, sy * 2f), color, each);
            }
        }

        /// <summary>
        /// 多段径向填充（等价 CSS 的 radial-gradient）。
        /// GDI+ 的 PathGradientBrush 要求色标必须从 0 开始、到 1 结束，
        /// 所以最后一段不足 1 时补一段同色全透明，抹出干净的边缘。
        /// </summary>
        private static void RadialFill(Graphics g, RectangleF box, Color[] stops, float[] positions)
        {
            if (box.Width <= 0 || box.Height <= 0 || stops == null || positions == null) return;
            int n = Math.Min(stops.Length, positions.Length);
            if (n < 2) return;

            bool head = positions[0] > 0.0001f;
            bool tail = positions[n - 1] < 0.9999f;
            var colors = new Color[n + (head ? 1 : 0) + (tail ? 1 : 0)];
            var pos = new float[colors.Length];
            int at = 0;
            if (head) { colors[at] = stops[0]; pos[at] = 0f; at++; }
            for (int i = 0; i < n; i++) { colors[at] = stops[i]; pos[at] = positions[i]; at++; }
            if (tail) { colors[at] = GoalPaint.A(stops[n - 1], 0); pos[at] = 1f; }

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(box);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterPoint = new PointF(box.X + box.Width / 2f, box.Y + box.Height / 2f);
                    var blend = new ColorBlend();
                    blend.Colors = colors;
                    blend.Positions = pos;
                    brush.InterpolationColors = blend;
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>只用「由宽到窄、由淡到浓」的描边叠出灯管外晕（不画实管）。</summary>
        private static void StrokeGlow(Graphics g, GraphicsPath path, Color color, float coreWidth, float blur,
            double alpha, int passes)
        {
            if (alpha <= 0.004) return;
            for (int i = passes; i >= 1; i--)
            {
                float t = i / (float)passes;
                float w = coreWidth + blur * t;
                double al = alpha * (0.30 + 0.30 * (1 - t));
                using (var pen = new Pen(GoalPaint.A(color, al), Math.Max(1f, w)))
                {
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawPath(pen, path);
                }
            }
        }

        /// <summary>十顶点八角星（s8/s10 的 star10），带两层青色 drop-shadow。</summary>
        private static void StarTen(Graphics g, float cx, float cy, float size, float rotDeg, double alpha)
        {
            if (size <= 0.5f || alpha <= 0.004) return;
            var pts = new PointF[StarTenPts.Length / 2];
            double rad = rotDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
            for (int i = 0; i < pts.Length; i++)
            {
                float nx = StarTenPts[i * 2] - 0.5f;
                float ny = StarTenPts[i * 2 + 1] - 0.5f;
                float x = nx * size, y = ny * size;
                pts[i] = new PointF(cx + x * cos - y * sin, cy + x * sin + y * cos);
            }
            GoalPaint.Glow(g, cx, cy, size * 0.34f, Cyan, 0.24 * alpha);
            GoalPaint.Glow(g, cx, cy, size * 0.22f, CyanLight, 0.38 * alpha);
            using (var b = new SolidBrush(GoalPaint.A(CyanLight, 0.96 * alpha))) g.FillPolygon(b, pts);
            using (var b = new SolidBrush(GoalPaint.A(Color.White, 0.55 * alpha)))
                g.FillPolygon(b, ScalePoints(pts, cx, cy, 0.45f));
        }

        /// <summary>四角星（出框层大小星），像 CSS 的 `M0-22 L6-7 L22 0 …` 那种窄腰闪光。</summary>
        private static void Sparkle4(Graphics g, float cx, float cy, float half, Color fill, Color halo, double alpha)
        {
            if (half <= 0.5f || alpha <= 0.004) return;
            const float w = 0.273f;
            var pts = new PointF[]
            {
                new PointF(cx, cy - half),
                new PointF(cx + half * w, cy - half * w),
                new PointF(cx + half, cy),
                new PointF(cx + half * w, cy + half * w),
                new PointF(cx, cy + half),
                new PointF(cx - half * w, cy + half * w),
                new PointF(cx - half, cy),
                new PointF(cx - half * w, cy - half * w)
            };
            GoalPaint.Glow(g, cx, cy, half * 1.55f, halo, 0.45 * alpha);
            GoalPaint.Glow(g, cx, cy, half * 0.75f, fill, 0.55 * alpha);
            using (var b = new SolidBrush(GoalPaint.A(fill, 0.97 * alpha))) g.FillPolygon(b, pts);
            using (var b = new SolidBrush(GoalPaint.A(Color.White, 0.75 * alpha)))
                g.FillPolygon(b, ScalePoints(pts, cx, cy, 0.40f));
        }

        private static PointF[] ScalePoints(PointF[] pts, float cx, float cy, float f)
        {
            var d = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                d[i] = new PointF(cx + (pts[i].X - cx) * f, cy + (pts[i].Y - cy) * f);
            return d;
        }
    }
}
