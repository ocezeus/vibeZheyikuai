using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace WorkCountdown
{
    /// <summary>
    /// 07 · 像素 Pixel（design/card-spread-notify-v4.html 的 s7）。
    /// 8×6 像素块拼底 + 荧光绿扫描线 + 点阵字故障揭幕 + 红心/黄冠/青星出框。
    /// 文字是「低分辨率栅格化 → 阈值 → 裁墨迹 → 邻接描边 → 整数倍放大」烘出来的位图，
    /// 不走字体绘制；所有方块都整数对齐、关抗锯齿，保证边缘是硬像素。
    /// </summary>
    internal sealed class GoalVariantPixel : IGoalVariant
    {
        public string id { get { return "07"; } }
        public string name { get { return "像素 Pixel"; } }
        public bool hasOutside { get { return true; } }

        // ---------------------------------------------------------------- 配色

        private static readonly Color BgTop = GoalPaint.Hex("#0D0A18");
        private static readonly Color BgBottom = GoalPaint.Hex("#1B1032");
        private static readonly Color Green = GoalPaint.Hex("#B6FF3B");
        private static readonly Color Magenta = GoalPaint.Hex("#FF4FA3");
        private static readonly Color Cyan = GoalPaint.Hex("#55E7FF");
        private static readonly Color Yellow = GoalPaint.Hex("#FFD43B");
        private static readonly Color YellowDark = GoalPaint.Hex("#C79A1E");
        private static readonly Color SubInk = GoalPaint.Hex("#EAF6FF");
        private static readonly Color HeartLight = GoalPaint.Hex("#FFD3EA");
        private static readonly Color VignetteInk = GoalPaint.Hex("#05040C");

        // 六种深底色 + 三种强调色（每 23 块插一个）
        private static readonly Color[] BlockPalette = new Color[]
        {
            GoalPaint.Hex("#171026"), GoalPaint.Hex("#1E1430"), GoalPaint.Hex("#26183D"),
            GoalPaint.Hex("#2E1C4A"), GoalPaint.Hex("#130E1F"), GoalPaint.Hex("#342052")
        };
        private static readonly Color[] AccentPalette = new Color[]
        {
            GoalPaint.Hex("#4E2B55"), GoalPaint.Hex("#33501F"), GoalPaint.Hex("#1F4650")
        };

        private readonly Color[] blocks = new Color[48];

        public GoalVariantPixel()
        {
            // 固定序列：同一帧永远同一张块面，不用每帧随机
            uint seed = 0x9E3779B9u;
            for (int i = 0; i < blocks.Length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                int idx = (int)((seed >> 16) % (uint)BlockPalette.Length);
                Color c = BlockPalette[idx];
                if (i % 23 == 22) c = AccentPalette[(i / 23) % AccentPalette.Length];
                blocks[i] = c;
            }
        }

        // ---------------------------------------------------------------- 卡内

        public void PaintInside(GoalScene s)
        {
            Graphics g = s.g;
            if (s.W < 4 || s.H < 4) return;

            GraphicsState saved = g.Save();
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            try
            {
                float W = s.W, H = s.H, k = s.scale;
                double outv = s.Out;
                if (outv >= 0.999) return;

                // 1. 底色：竖向渐变，3%→5% 淡入
                double bgA = (s.quiet ? 1 : GoalScene.Smooth(GoalScene.Clamp01((s.t - 0.21) / 0.14))) * (1 - outv);
                if (bgA > 0.004)
                {
                    using (var b = new LinearGradientBrush(new RectangleF(0, 0, W, H),
                        GoalPaint.A(BgTop, bgA), GoalPaint.A(BgBottom, bgA), 90f))
                        g.FillRectangle(b, 0, 0, W, H);
                }

                // 2. 像素块：8 列 × 6 行（收起 8×2），逐块弹出
                int rows = s.collapsed ? 2 : 6;
                int cols = 8;
                float cw = W / cols, chh = H / rows;
                double now = s.quiet ? 99 : s.t;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int i = r * cols + c;
                        double delay = 0.12 + i * 0.009;
                        double p = now - delay;
                        double a;
                        float sc;
                        if (s.quiet || p >= 0.77) { a = 1; sc = 1f; }
                        else if (p < 0.35) { a = 0; sc = 0.2f; }
                        else if (p < 0.56)
                        {
                            double q = GoalScene.Smooth((p - 0.35) / 0.21);
                            a = 0.95 * q;
                            sc = 0.2f + 0.86f * (float)q;
                        }
                        else
                        {
                            double q = GoalScene.Smooth((p - 0.56) / 0.21);
                            a = 0.95 + 0.05 * q;
                            sc = 1.06f - 0.06f * (float)q;
                        }
                        a *= (1 - outv);
                        sc -= 0.14f * (float)outv;
                        if (a <= 0.004) continue;

                        float x0 = (float)Math.Round(c * cw);
                        float y0 = (float)Math.Round(r * chh);
                        float x1 = (float)Math.Round((c + 1) * cw);
                        float y1 = (float)Math.Round((r + 1) * chh);
                        float mx = (x0 + x1) / 2f, my = (y0 + y1) / 2f;
                        int bx = (int)Math.Round(mx + (x0 - mx) * sc);
                        int by = (int)Math.Round(my + (y0 - my) * sc + 6f * k * (float)outv);
                        int bw = (int)Math.Round((x1 - x0) * sc);
                        int bh = (int)Math.Round((y1 - y0) * sc);
                        if (bw < 1) bw = 1;
                        if (bh < 1) bh = 1;
                        using (var b = new SolidBrush(GoalPaint.A(blocks[i], a)))
                            g.FillRectangle(b, bx, by, bw, bh);
                    }
                }

                // 块缝：设计稿 0.4/0.73 DIP 的暗缝，用 1 DIP 宽的暗线补出来（不是灰缝）
                if (now > 0.5)
                {
                    int sw = Math.Max(1, (int)Math.Round(1f * k));
                    double seamA = 0.30 * (1 - outv);
                    using (var b = new SolidBrush(GoalPaint.A(BgTop, seamA)))
                    {
                        for (int c = 1; c < cols; c++)
                        {
                            int x = (int)Math.Round(c * cw);
                            g.FillRectangle(b, x, 0, sw, H);
                        }
                        for (int r = 1; r < rows; r++)
                        {
                            int y = (int)Math.Round(r * chh);
                            g.FillRectangle(b, 0, y, W, sw);
                        }
                    }
                }

                // 3. 扫描线：22 DIP 高的渐变亮带，2.6s 从 −37.6 扫到 230
                double scanShow = s.quiet ? 1 : GoalScene.Smooth(GoalScene.Clamp01((s.t - 0.56) / 0.42));
                double scanA = scanShow * (1 - outv);
                if (scanA > 0.004)
                {
                    float frac = s.quiet ? (float)((3.2 / 2.6) % 1.0) : (float)((s.t / 2.6) % 1.0);
                    float sy = (float)Math.Round((-37.6f + 230f * frac) * k);
                    float sh = (float)Math.Round(22f * k);
                    if (sh < 1) sh = 1;
                    var box = new RectangleF(0, sy - 1, W, sh + 2);
                    using (var b = new LinearGradientBrush(box, Color.Empty, Color.Empty, 90f))
                    {
                        var blend = new ColorBlend();
                        blend.Colors = new Color[]
                        {
                            GoalPaint.A(Green, 0), GoalPaint.A(Green, 0.13 * scanA),
                            GoalPaint.A(Green, 0.03 * scanA), GoalPaint.A(Green, 0)
                        };
                        blend.Positions = new float[] { 0f, 0.34f, 0.67f, 1f };
                        b.InterpolationColors = blend;
                        g.FillRectangle(b, 0, sy, W, sh);
                    }
                }

                // 4. 暗角（径向 120%×100%，52% 起压到 α55%）
                double vigA = (s.quiet ? 1 : GoalScene.Smooth(GoalScene.Clamp01((s.t - 0.56) / 0.42))) * (1 - outv);
                if (vigA > 0.004) PaintVignette(g, W, H, 0.55 * vigA);

                // 5. 边框 + 四角 L 形 + sprite + 点阵字 + 状态条
                double frameShow = s.quiet ? 1 : GoalScene.Smooth(GoalScene.Clamp01((s.t - 0.56) / 0.42));
                double frameA = frameShow * (1 - outv);
                PaintFrame(s, frameA);
                PaintSprites(s, frameA);
                PaintText(s, (s.quiet ? 1 : GoalScene.Smooth(GoalScene.Clamp01((s.t - 1.05) / 0.21))) * (1 - outv));
                PaintBar(s, frameA);
            }
            finally
            {
                g.Restore(saved);
            }
        }

        private static Bitmap vignetteTex;

        /// <summary>
        /// 暗角：radial-gradient(120% 100% at 50% 50%, transparent 52%, #05040C 100%)。
        /// 预烘一张 128×128 的 alpha 贴图拉伸铺满，避免 PathGradient 在矩形路径上表现不稳。
        /// </summary>
        private static Bitmap VignetteTexture()
        {
            if (vignetteTex != null) return vignetteTex;
            int n = 128;
            var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buf = new byte[stride * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        double dx = (x + 0.5) / n * 2.0 - 1.0;
                        double dy = (y + 0.5) / n * 2.0 - 1.0;
                        // 椭圆半径 120%×100%，所以 x 方向除以 1.2
                        double rr = Math.Sqrt(dx * dx / 1.44 + dy * dy);
                        double t = (rr - 0.52) / 0.48;
                        if (t < 0) t = 0;
                        if (t > 1) t = 1;
                        int a = (int)Math.Round(0.55 * t * 255);
                        int o = y * stride + x * 4;
                        buf[o] = VignetteInk.B;
                        buf[o + 1] = VignetteInk.G;
                        buf[o + 2] = VignetteInk.R;
                        buf[o + 3] = (byte)a;
                    }
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            vignetteTex = bmp;
            return bmp;
        }

        private static void PaintVignette(Graphics g, float W, float H, double alpha)
        {
            if (alpha <= 0.004) return;
            InterpolationMode oldInterp = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            if (alpha >= 0.999)
            {
                g.DrawImage(VignetteTexture(), new Rectangle(0, 0, (int)W, (int)H));
            }
            else
            {
                using (var ia = new ImageAttributes())
                {
                    var cm = new ColorMatrix();
                    cm.Matrix33 = (float)alpha;
                    ia.SetColorMatrix(cm);
                    g.DrawImage(VignetteTexture(), new Rectangle(0, 0, (int)W, (int)H),
                        0, 0, 128, 128, GraphicsUnit.Pixel, ia);
                }
            }
            g.InterpolationMode = oldInterp;
        }

        /// <summary>荧光绿内框（inset 6、线宽 3、α34%）+ 四角 12×12 的 2px L 形（100%）。</summary>
        private void PaintFrame(GoalScene s, double alpha)
        {
            if (alpha <= 0.004) return;
            Graphics g = s.g;
            float W = s.W, H = s.H, k = s.scale;
            float inset = (s.collapsed ? 4f : 6f) * k;
            float lw = (s.collapsed ? 2f : 3f) * k;
            int x0 = (int)Math.Round(inset), y0 = (int)Math.Round(inset);
            int x1 = (int)Math.Round(W - inset), y1 = (int)Math.Round(H - inset);
            int t = Math.Max(1, (int)Math.Round(lw));
            using (var b = new SolidBrush(GoalPaint.A(Green, 0.34 * alpha)))
            {
                g.FillRectangle(b, x0, y0, x1 - x0, t);
                g.FillRectangle(b, x0, y1 - t, x1 - x0, t);
                g.FillRectangle(b, x0, y0 + t, t, Math.Max(1, y1 - y0 - t * 2));
                g.FillRectangle(b, x1 - t, y0 + t, t, Math.Max(1, y1 - y0 - t * 2));
            }

            // 四角 L 形（12×12、2px）
            int cs = Math.Max(2, (int)Math.Round(12f * k));
            int ct = Math.Max(1, (int)Math.Round(2f * k));
            using (var b = new SolidBrush(GoalPaint.A(Green, alpha)))
            {
                g.FillRectangle(b, x0, y0, cs, ct);
                g.FillRectangle(b, x0, y0, ct, cs);
                g.FillRectangle(b, x1 - cs, y0, cs, ct);
                g.FillRectangle(b, x1 - ct, y0, ct, cs);
                g.FillRectangle(b, x0, y1 - ct, cs, ct);
                g.FillRectangle(b, x0, y1 - cs, ct, cs);
                g.FillRectangle(b, x1 - cs, y1 - ct, cs, ct);
                g.FillRectangle(b, x1 - ct, y1 - cs, ct, cs);
            }
        }

        // ---------------------------------------------------------------- sprite

        // 11×11 红心（'o' = 高光）
        private static readonly string[] HeartArt = new string[]
        {
            "           ",
            "  ##   ##  ",
            " ######### ",
            " ######### ",
            " ######### ",
            "  #######  ",
            "  #######  ",
            "   #####   ",
            "    ###    ",
            "     #     ",
            "           "
        };

        // 11×11 青星（四角星）
        private static readonly string[] StarArt = new string[]
        {
            "           ",
            "     #     ",
            "     #     ",
            "    ###    ",
            "    ###    ",
            "###########",
            "###########",
            "    ###    ",
            "     #     ",
            "     #     ",
            "           "
        };

        // 12×12 黄冠（'o' = 底边暗色）
        private static readonly string[] CrownArt = new string[]
        {
            "            ",
            "     ##     ",
            "   ######   ",
            " ########## ",
            " ########## ",
            " ########## ",
            " ########## ",
            " ########## ",
            " oooooooo   ",
            " ########   ",
            "            ",
            "            "
        };

        private void PaintSprites(GoalScene s, double alpha)
        {
            if (alpha <= 0.004) return;
            Graphics g = s.g;
            float k = s.scale;

            // 红心：左上 (14,15) 34×34
            float bob0 = SpriteBob(s, 0.2, 0);
            PaintSprite(g, HeartArt, 14f * k, (15f * k) + bob0, 34f * k / 11f,
                GoalPaint.Hex("#FF4FA3"), HeartLight, alpha, 0);

            // 青星：右下 (226,130) 38×38
            float bob1 = SpriteBob(s, 0.8, 1);
            PaintSprite(g, StarArt, 226f * k, (130f * k) + bob1, 38f * k / 11f,
                Cyan, SubInk, alpha, 0);

            // 黄冠：右上 (227,14) 38×38，画完补三颗彩色像素点
            float bob2 = SpriteBob(s, 0.0, 2);
            float cell = 38f * k / 12f;
            PaintSprite(g, CrownArt, 227f * k, (14f * k) + bob2, cell,
                Yellow, YellowDark, alpha, 1);
            float ccx = 227f * k, ccy = (14f * k) + bob2;
            GoalPaint.Pixel(g, ccx + 3 * cell, ccy + 4 * cell, cell, cell, Magenta, alpha);
            GoalPaint.Pixel(g, ccx + 6 * cell, ccy + 3 * cell, cell, cell, Cyan, alpha);
            GoalPaint.Pixel(g, ccx + 8 * cell, ccy + 4 * cell, cell, cell, Magenta, alpha);
        }

        /// <summary>bob：steps(2,end) 的离散 0 / −3px；quiet 取 50% 相位（sp1/sp2 上移 3）。</summary>
        private static float SpriteBob(GoalScene s, double delay, int index)
        {
            float k = s.scale;
            if (s.quiet) return (index <= 1) ? -3f * k : 0f;
            double local = s.t - delay;
            if (local <= 0) return 0f;
            double p = (local % 2.4) / 2.4;
            return p >= 0.5 ? -3f * k : 0f;
        }

        private static void PaintSprite(Graphics g, string[] rows, float x, float y, float cell,
            Color fill, Color edge, double alpha, int shadow)
        {
            if (alpha <= 0.004) return;
            int c = Math.Max(1, (int)Math.Round(cell));
            if (shadow > 0)
            {
                int off = Math.Max(1, (int)Math.Round(2f * cell / 3f));
                GoalPaint.Bitmap(rows, g, x + off, y + off, c, Color.Black, Color.Black, 0.28 * alpha);
            }
            GoalPaint.Bitmap(rows, g, x, y, c, fill, edge, alpha);
        }

        // ---------------------------------------------------------------- 点阵字

        /// <summary>一张烘焙好的点阵字：bmp 里含 1 圈描边与硬影留白，base* 是不含留白的主层。</summary>
        private sealed class PixelArt
        {
            public Bitmap bmp;
            public int baseW;
            public int baseH;
            public int baseX;
            public int baseY;
        }

        private static readonly Dictionary<string, PixelArt> ArtCache = new Dictionary<string, PixelArt>();

        private string titleKey;
        private string subKey;
        private PixelArt titleArt;
        private PixelArt subArt;

        private void PaintText(GoalScene s, double alpha)
        {
            if (alpha <= 0.004) return;
            Graphics g = s.g;
            float k = s.scale;

            if (s.collapsed)
            {
                PixelArt a14 = GetArt(this, s, "今日目标达成", 14f, 48, Green, Magenta, true, 2f, 1.0, true);
                float cy = 30f * k;
                ArtAt(g, a14, s.W / 2f, cy, alpha, 1f, 0f);
                return;
            }

            PixelArt title = GetArt(this, s, "今日目标达成", 20f, 48, Green, Magenta, false, 3f, 1.0, false);
            PixelArt sub = GetArt(this, s, "工作辛苦了", 17f, 128, SubInk, Cyan, true, 2f, 0.55, false);

            // 标题背后的柔光（0 0 6px #B6FF3B α28%）
            if (alpha > 0.02)
            {
                double glowA = 0.10 * alpha;
                var oldSM = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                GoalPaint.Glow(g, s.W / 2f, 71.5f * k, 118f * k, Green, glowA);
                g.SmoothingMode = oldSM;
            }

            // 标题：6 个离散状态的故障揭幕（clip 自下往上切、X 分段抖动）
            float visible = 1f;
            float xoff = 0f;
            if (!s.quiet)
            {
                double local = s.t - 1.18;
                if (local < 0)
                {
                    visible = 0f;
                }
                else
                {
                    int step = (int)Math.Floor(local / 0.125);
                    if (step > 5) step = 5;
                    float[] fr = new float[] { 0f, 0.58f, 0.34f, 0.82f, 0.92f, 1f };
                    float[] xo = new float[] { -7f, 6f, -4f, 3f, -1f, 0f };
                    visible = fr[step];
                    xoff = xo[step] * k;
                }
            }
            if (visible > 0f)
                ArtAt(g, title, s.W / 2f + xoff, 71.5f * k, alpha, visible, 0f);

            // 副标题：标题硬影底边（71.5 + 22 + 3 ≈ 96.5）到副标题顶边留 8 DIP
            ArtAt(g, sub, s.W / 2f, 123.5f * k, alpha, 1f, 0f);
        }

        /// <summary>
        /// 取缓存的点阵字。key 里带 scale（cell）、文字、阈值与影色/影偏移，参数变了会重烘一份。
        /// 同一实例上重复调用直接命中最后一份，避免每帧查字典。
        /// </summary>
        private static PixelArt GetArt(GoalVariantPixel owner, GoalScene s, string text, float fontPx,
            int threshold, Color fill, Color edge, bool dilate, float shadowDip, double shadowAlpha, bool smallTitle)
        {
            int cell = Math.Max(2, (int)Math.Round(2f * s.scale));
            int shadow = Math.Max(0, (int)Math.Round(shadowDip * s.scale));
            string key = (smallTitle ? "s|" : "b|") + text + "|" + fontPx + "|" + cell + "|" + threshold
                + "|" + shadow + "|" + (int)Math.Round(shadowAlpha * 100) + "|"
                + fill.ToArgb() + "|" + edge.ToArgb() + "|" + (dilate ? 1 : 0);
            if (smallTitle)
            {
                if (key == owner.titleKey && owner.titleArt != null) return owner.titleArt;
                owner.titleKey = key;
                owner.titleArt = GetCachedArt(key, text, fontPx, threshold, cell, fill, edge, dilate, shadow, shadowAlpha);
                return owner.titleArt;
            }
            if (text == "今日目标达成")
            {
                if (key == owner.titleKey && owner.titleArt != null) return owner.titleArt;
                owner.titleKey = key;
                owner.titleArt = GetCachedArt(key, text, fontPx, threshold, cell, fill, edge, dilate, shadow, shadowAlpha);
                return owner.titleArt;
            }
            if (key == owner.subKey && owner.subArt != null) return owner.subArt;
            owner.subKey = key;
            owner.subArt = GetCachedArt(key, text, fontPx, threshold, cell, fill, edge, dilate, shadow, shadowAlpha);
            return owner.subArt;
        }

        private static PixelArt GetCachedArt(string key, string text, float fontPx, int threshold, int cell,
            Color fill, Color edge, bool dilate, int shadow, double shadowAlpha)
        {
            PixelArt art;
            if (ArtCache.TryGetValue(key, out art)) return art;
            art = Bake(text, fontPx, threshold, cell, fill, edge, dilate, shadow, shadowAlpha);
            ArtCache[key] = art;
            return art;
        }

        /// <summary>
        /// 烘焙点阵字：20/17/14px 微软雅黑 700 画到小位图 → alpha 阈值 → 可选取膨胀（四邻 ≥2 补 1px）
        /// → 按墨迹裁边（各边 +1）→ 每个墨迹像素 = cell×cell 方块 → 非墨迹但四邻接墨迹的像素填描边色
        /// → 再叠一份 (shadow,shadow) 的硬影（副标题的影是 α55%，跟设计稿一致）。
        /// </summary>
        private static PixelArt Bake(string text, float fontPx, int threshold, int cell, Color fill, Color edge,
            bool dilate, int shadow, double shadowAlpha)
        {
            int cw = (int)Math.Ceiling(fontPx * text.Length * 1.4f) + 64;
            int ch = (int)Math.Ceiling(fontPx * 4f);
            bool[] on = new bool[cw * ch];
            int minX = cw, minY = ch, maxX = -1, maxY = -1;

            using (var font = MakePixelFont(fontPx))
            using (var low = new Bitmap(cw, ch, PixelFormat.Format32bppArgb))
            {
                using (var lg = Graphics.FromImage(low))
                {
                    lg.Clear(Color.Transparent);
                    lg.SmoothingMode = SmoothingMode.None;
                    lg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    lg.PixelOffsetMode = PixelOffsetMode.Half;
                    using (var sf = StringFormat.GenericTypographic)
                    using (var br = new SolidBrush(Color.White))
                    {
                        sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                        lg.DrawString(text, font, br, new PointF(16f, fontPx * 0.7f), sf);
                    }
                }

                // 阈值：alpha 超过阈值才算墨迹
                BitmapData data = low.LockBits(new Rectangle(0, 0, cw, ch), ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    byte[] buf = new byte[stride * ch];
                    Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                    for (int y = 0; y < ch; y++)
                    {
                        int row = y * stride;
                        for (int x = 0; x < cw; x++)
                        {
                            if (buf[row + x * 4 + 3] > threshold) on[y * cw + x] = true;
                        }
                    }
                }
                finally
                {
                    low.UnlockBits(data);
                }
            }

            // 膨胀：四邻 ≥2 补 1px（副标题加粗）
            if (dilate)
            {
                bool[] copy = (bool[])on.Clone();
                for (int y = 0; y < ch; y++)
                {
                    for (int x = 0; x < cw; x++)
                    {
                        int i = y * cw + x;
                        if (copy[i]) continue;
                        int n = (x > 0 && copy[i - 1] ? 1 : 0)
                              + (x < cw - 1 && copy[i + 1] ? 1 : 0)
                              + (y > 0 && copy[i - cw] ? 1 : 0)
                              + (y < ch - 1 && copy[i + cw] ? 1 : 0);
                        if (n >= 2) on[i] = true;
                    }
                }
            }

            // 按真实墨迹裁边（各边 +1，这 1px 就是描边的位置）
            for (int y = 0; y < ch; y++)
            {
                for (int x = 0; x < cw; x++)
                {
                    if (!on[y * cw + x]) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) return null;

            int x0 = Math.Max(0, minX - 1), y0 = Math.Max(0, minY - 1);
            int bw = maxX - x0 + 2, bh = maxY - y0 + 2;

            var art = new PixelArt();
            art.baseW = bw * cell;
            art.baseH = bh * cell;
            art.baseX = 0;
            art.baseY = 0;
            art.bmp = new Bitmap(art.baseW + shadow, art.baseH + shadow, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(art.bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var bFill = new SolidBrush(fill))
                using (var bEdge = new SolidBrush(edge))
                using (var bShadow = new SolidBrush(GoalPaint.A(edge, shadowAlpha)))
                {
                    for (int y = 0; y < bh; y++)
                    {
                        for (int x = 0; x < bw; x++)
                        {
                            int sx = x0 + x, sy = y0 + y;
                            bool ink = on[sy * cw + sx];
                            bool ring = false;
                            if (!ink)
                            {
                                ring = (sx > 0 && on[sy * cw + sx - 1])
                                    || (sx < cw - 1 && on[sy * cw + sx + 1])
                                    || (sy > 0 && on[(sy - 1) * cw + sx])
                                    || (sy < ch - 1 && on[(sy + 1) * cw + sx]);
                            }
                            int px = x * cell, py = y * cell;
                            if (shadow > 0 && (ink || ring))
                                g.FillRectangle(bShadow, px + shadow, py + shadow, cell, cell);
                            if (ink) g.FillRectangle(bFill, px, py, cell, cell);
                            else if (ring) g.FillRectangle(bEdge, px, py, cell, cell);
                        }
                    }
                }
            }
            return art;
        }

        /// <summary>微软雅黑 700（设计稿字体）；找不到时退回黑体 / 系统无衬线（烘焙只看形状）。</summary>
        private static Font MakePixelFont(float px)
        {
            // 「Microsoft YaHei」在中文系统上会解析成「微软雅黑」，所以不能拿请求名去比对 f.Name
            string[] names = new string[] { "Microsoft YaHei", "微软雅黑", "Microsoft YaHei UI", "SimHei", "Microsoft JhengHei" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    var f = new Font(names[i], px, FontStyle.Bold, GraphicsUnit.Pixel);
                    // 找不到的族会静默落到 Microsoft Sans Serif，用这个名字识别失败
                    if (!string.IsNullOrEmpty(f.Name) && !string.Equals(f.Name, "Microsoft Sans Serif", StringComparison.OrdinalIgnoreCase))
                        return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(FontFamily.GenericSansSerif, px, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        /// <summary>把点阵字按中心放到画面上（x 是主层中心，shadow 留白自动补偿）。</summary>
        private static void ArtAt(Graphics g, PixelArt art, float cx, float cy, double alpha, float visible, float dx)
        {
            if (art == null || alpha <= 0.004 || visible <= 0f) return;
            int x = (int)Math.Round(cx - art.baseW / 2f) - art.baseX + (int)Math.Round(dx);
            int y = (int)Math.Round(cy - art.baseH / 2f) - art.baseY;
            int w = art.bmp.Width, h = art.bmp.Height;
            int sh = (int)Math.Round(h * Math.Min(1f, visible));
            if (sh <= 0) return;
            if (alpha >= 0.999 && sh >= h)
            {
                g.DrawImage(art.bmp, x, y, w, h);
                return;
            }
            using (var ia = new ImageAttributes())
            {
                if (alpha < 0.999)
                {
                    var cm = new ColorMatrix();
                    cm.Matrix33 = (float)alpha;
                    ia.SetColorMatrix(cm);
                }
                g.DrawImage(art.bmp, new Rectangle(x, y, w, sh), 0, 0, w, sh, GraphicsUnit.Pixel, ia);
            }
        }

        // ---------------------------------------------------------------- 状态条

        private static void PaintBar(GoalScene s, double alpha)
        {
            if (alpha <= 0.004) return;
            Graphics g = s.g;
            float k = s.scale;
            int cols = s.collapsed ? 5 : 8;
            int cw = Math.Max(2, (int)Math.Round(9f * k));
            int gap = Math.Max(1, (int)Math.Round(3f * k));
            int chh = Math.Max(2, (int)Math.Round(7f * k));
            int total = cols * cw + (cols - 1) * gap;
            int x = (int)Math.Round((s.W - total) / 2f);
            int bottom = (int)Math.Round((s.collapsed ? 8f : 18f) * k);
            int y = (int)Math.Round(s.H - bottom - chh);

            // 每格背后的辉光（0 0 6px α35%）
            var oldSM = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < cols; i++)
            {
                float gx = x + i * (cw + gap) + cw / 2f;
                float gy = y + chh / 2f;
                Color gc = i == 5 ? Magenta : (i == 6 ? Cyan : Green);
                GoalPaint.Glow(g, gx, gy, cw * 1.15f, gc, 0.18 * alpha);
            }
            g.SmoothingMode = oldSM;

            using (var b = new SolidBrush(GoalPaint.A(Green, alpha)))
            using (var bm = new SolidBrush(GoalPaint.A(Magenta, alpha)))
            using (var bc = new SolidBrush(GoalPaint.A(Cyan, alpha)))
            {
                for (int i = 0; i < cols; i++)
                {
                    SolidBrush brush = b;
                    if (i == 5) brush = bm;
                    else if (i == 6) brush = bc;
                    g.FillRectangle(brush, x + i * (cw + gap), y, cw, chh);
                }
            }
        }

        // ---------------------------------------------------------------- 卡外

        public void PaintOutside(GoalScene s)
        {
            Graphics g = s.g;
            float k = s.scale;
            double show = s.quiet ? 1 : GoalScene.Smooth(GoalScene.Clamp01((s.t - 0.55) / 0.6));
            double a = show * (1 - s.Out);
            if (a <= 0.01) return;

            GraphicsState saved = g.Save();
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            try
            {
                if (s.collapsed) PaintCollapsedOutside(s, a);
                else PaintBigOutside(s, a);
            }
            finally
            {
                g.Restore(saved);
            }
        }

        private void PaintBigOutside(GoalScene s, double a)
        {
            Graphics g = s.g;
            float k = s.scale;

            // 像素框：rect(−14,−14,308,216)，2px α34%
            FillDip(g, -14f, -14f, 308f, 2f, Green, 0.34 * a, k);
            FillDip(g, -14f, 202f, 308f, 2f, Green, 0.34 * a, k);
            FillDip(g, -14f, -14f, 2f, 216f, Green, 0.34 * a, k);
            FillDip(g, 292f, -14f, 2f, 216f, Green, 0.34 * a, k);

            // 出框框四角加粗的 L 块（α80%）
            double ca = 0.80 * a;
            FillDip(g, -14f, -14f, 18f, 6f, Green, ca, k);
            FillDip(g, -14f, -14f, 6f, 18f, Green, ca, k);
            FillDip(g, 276f, -14f, 18f, 6f, Green, ca, k);
            FillDip(g, 288f, -14f, 6f, 18f, Green, ca, k);
            FillDip(g, -14f, 196f, 18f, 6f, Green, ca, k);
            FillDip(g, -14f, 182f, 6f, 18f, Green, ca, k);
            FillDip(g, 276f, 196f, 18f, 6f, Green, ca, k);
            FillDip(g, 288f, 182f, 6f, 18f, Green, ca, k);

            // 像素拖尾
            FillDip(g, -30f, -8f, 10f, 10f, Green, 0.80 * a, k);
            FillDip(g, -44f, 0f, 7f, 7f, Green, 0.80 * a, k);
            FillDip(g, -54f, 12f, 4f, 4f, Green, 0.80 * a, k);
            FillDip(g, 292f, 190f, 14f, 14f, Magenta, 0.80 * a, k);
            FillDip(g, 308f, 206f, 10f, 10f, Magenta, 0.80 * a, k);
            FillDip(g, 320f, 220f, 7f, 7f, Magenta, 0.80 * a, k);
            FillDip(g, -30f, 196f, 9f, 9f, Cyan, 0.80 * a, k);
            FillDip(g, -42f, 208f, 6f, 6f, Cyan, 0.80 * a, k);

            // 大号红心 translate(−54,148) scale(3)
            int hc = Math.Max(1, (int)Math.Round(3f * k));
            GoalPaint.Bitmap(HeartArt, g, -54f * k, 148f * k, hc, Magenta, HeartLight, a);

            // 大号青星 (318,32) scale(3.4)
            int sc = Math.Max(1, (int)Math.Round(3.4f * k));
            GoalPaint.Bitmap(StarArt, g, 318f * k, 32f * k, sc, Cyan, SubInk, a);
            GoalPaint.Pixel(g, 318f * k + 5 * sc, 32f * k + 4 * sc, sc, sc, SubInk, a);
        }

        private void PaintCollapsedOutside(GoalScene s, double a)
        {
            Graphics g = s.g;
            float k = s.scale;

            // 减半：小一圈的像素框 + 两个角块 + 一颗心
            FillDip(g, -10f, -10f, 244f, 2f, Green, 0.30 * a, k);
            FillDip(g, -10f, 76f, 244f, 2f, Green, 0.30 * a, k);
            FillDip(g, -10f, -10f, 2f, 88f, Green, 0.30 * a, k);
            FillDip(g, 234f, -10f, 2f, 88f, Green, 0.30 * a, k);
            FillDip(g, -10f, -10f, 14f, 5f, Green, 0.75 * a, k);
            FillDip(g, -10f, -10f, 5f, 14f, Green, 0.75 * a, k);
            FillDip(g, 220f, 71f, 14f, 5f, Green, 0.75 * a, k);
            FillDip(g, 229f, 62f, 5f, 14f, Green, 0.75 * a, k);

            int hc = Math.Max(1, (int)Math.Round(2f * k));
            GoalPaint.Bitmap(HeartArt, g, -42f * k, 20f * k, hc, Magenta, HeartLight, a);
            GoalPaint.Bitmap(StarArt, g, 238f * k, 34f * k, hc, Cyan, SubInk, a);
        }

        /// <summary>按设计坐标（DIP）填一个整数对齐的方块。</summary>
        private static void FillDip(Graphics g, float x, float y, float w, float h, Color color,
            double alpha, float k)
        {
            if (alpha <= 0.004) return;
            GoalPaint.Pixel(g, x * k, y * k, w * k, h * k, color, alpha);
        }
    }
}
