using System;
using System.Collections.Generic;
using System.Drawing;

namespace WorkCountdown
{
    /// <summary>进度条的画法。每套皮肤挑一种，Widget 里按它画。</summary>
    public enum SkinProgress
    {
        /// <summary>现在的样子：圆角胶囊 + 外发光 + 流动亮带 + 最后 30 分钟彩色。</summary>
        Default,
        /// <summary>无垠：2 DIP 细线，只留一条实线，不发光、不流动。</summary>
        Hairline,
        /// <summary>云母：5 DIP 胶囊渐变 + 柔光 + 流动亮带。</summary>
        Pill,
        /// <summary>宣纸：3 DIP 笔触，朱砂渐变，两端收圆，不流动。</summary>
        Brush,
        /// <summary>仪表：8 DIP 分刻刻度（24 格），琥珀色，像仪表刻度。</summary>
        Segmented,
        /// <summary>霓虹：4 DIP 品红→青渐变 + 双层辉光 + 亮带。</summary>
        Neon
    }

    /// <summary>
    /// 一套「整体界面主题」（皮肤）。颜色 + 圆角 + 边框 + 质感 + 进度条画法 + 少量装饰。
    /// 默认皮肤不指定颜色（继续用设置里选的 39 套 / 自建主题），其它五套各自带一套固定配色。
    /// 设计稿在 design\theme-packs\（index.html + themes.css 里是同样的令牌）。
    /// </summary>
    public class SkinDef
    {
        public string id;
        public string name;
        public string tagline;
        public bool light;

        // ---- 卡片配色（默认皮肤为 null：用 ui.theme 选出来的 ThemeDef）----
        public ThemeDef colors;
        public Color accent2;               // 进度条渐变第二色（宣纸/霓虹用）
        public Color? glow;                 // 卡片外光颜色（null = accent）

        // ---- 卡片形态 ----
        public int radiusCollapsed = 14;
        public int radiusExpanded = 18;
        public int borderWidth = 1;         // DIP
        public Color? borderColor;          // null = 按主题明暗自动算（默认皮肤原样）
        public double sheenAlpha;           // 顶部高光（0 = 不加）
        public bool glass;                  // 强制磨砂玻璃（云母）
        public bool paperGrain;             // 纸纹颗粒（宣纸）
        public bool screws;                 // 四角螺丝（仪表）
        public bool neonEdge;               // 霓虹渐变描边 + 外辉光
        public SkinProgress progress = SkinProgress.Default;
        public bool numberGlow;             // 大数字发光
        public double numberGlowAlpha = 0.35;
        public Color? numberColor;          // 大数字颜色（null = theme.textMain）
        public string headingFont;          // 设置窗口 / 小纸条标题的字体族（null = 跟界面中文）
        public string defaultThemeId;       // 设置页色卡里高亮哪一套（给皮肤预览用）

        // ---- 设置窗口 / 小纸条 / 右键胶囊的配色 ----
        public Color windowBg;
        public Color windowInk;
        public Color windowMuted;
        public Color windowEdge;
        public Color cardBg;
        public Color cardEdge;
        public Color railBg;
        public Color railActiveBg;
        public Color footerBg;
        public Color accentInk;             // 强调色按钮上的文字色
        public Color panelBg;               // 小纸条 / 胶囊面板底
        public Color panelEdge;
        public Color bubbleIn;
        public Color bubbleInInk;
        public Color bubbleOut;
        public Color bubbleOutInk;
        public int radiusWindow = 18;
        public int radiusCard = 10;
        public int radiusPill = 20;
        public int radiusNote = 18;
        public int radiusBubble = 10;
        public bool hairlineCards;          // 无垠：设置页卡片不填底，只用顶部细线分组
        public bool seal;                   // 宣纸：设置页「外观」右上角印章

        public bool IsDefault { get { return id == Skins.DefaultId; } }
    }

    /// <summary>五套皮肤 + 默认的查表与配色工具。</summary>
    public static class Skins
    {
        public const string DefaultId = "default";

        public static readonly SkinDef[] All = BuildAll();

        public static SkinDef Get(string id)
        {
            id = (id ?? "").Trim();
            foreach (var s in All)
                if (s != null && string.Equals(s.id, id, StringComparison.OrdinalIgnoreCase)) return s;
            return Get(DefaultId);
        }

        /// <summary>设置里当前选中的皮肤（老配置没有 skin → 默认）。</summary>
        public static SkinDef Current(UiSettings ui)
        {
            return Get(ui == null ? null : ui.skin);
        }

        public static bool IsDefault(UiSettings ui)
        {
            return Current(ui).IsDefault;
        }

        /// <summary>
        /// 当前应该用的配色：默认皮肤看设置里的 39 套主题；其它皮肤用自带的那一套。
        /// 提醒配色 / 固定日程「跟随当前主题」也会顺着这里走，所以提醒卡自动跟着皮肤变。
        /// </summary>
        public static ThemeDef ResolveTheme(UiSettings ui)
        {
            var skin = Current(ui);
            if (skin.colors != null) return skin.colors;
            return Themes.Get(ui == null ? null : ui.theme);
        }

        /// <summary>要不要按磨砂玻璃画：云母强制玻璃；默认皮肤看「背景风格」设置。</summary>
        public static bool UsesGlass(UiSettings ui)
        {
            var skin = Current(ui);
            if (skin.glass) return true;
            if (!skin.IsDefault) return false;
            return ui != null && ui.bgStyle == "glass";
        }

        /// <summary>卡片描边色：皮肤指定了就用指定色，否则按主题明暗自动算（和 v3.6 一致）。</summary>
        public static Color CardBorderColor(SkinDef skin, ThemeDef theme)
        {
            if (skin.borderColor.HasValue) return skin.borderColor.Value;
            return theme.edge;
        }

        public static Color GlowColor(SkinDef skin, ThemeDef theme)
        {
            return skin.glow.HasValue ? skin.glow.Value : theme.accent;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, double a)
        {
            a = Math.Max(0, Math.Min(1, a));
            return Color.FromArgb((int)Math.Round(255 * a), c.R, c.G, c.B);
        }

        public static Color Hex(string hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && hex.Length == 7 && hex[0] == '#')
                    return Color.FromArgb(
                        Convert.ToInt32(hex.Substring(1, 2), 16),
                        Convert.ToInt32(hex.Substring(3, 2), 16),
                        Convert.ToInt32(hex.Substring(5, 2), 16));
            }
            catch { }
            return Color.Magenta;
        }

        // ==================================================================
        // 六套定义。颜色照 design\theme-packs\themes.css 抄，单位 DIP。
        // ==================================================================
        private static SkinDef[] BuildAll()
        {
            var list = new List<SkinDef>();

            // ---------------- 默认：现在的样子（39 套主题 + 背景风格）----------------
            list.Add(new SkinDef
            {
                id = DefaultId,
                name = "默认",
                tagline = "现在的样子：39 套主题 + 可选磨砂玻璃",
                light = false,
                colors = null,
                radiusCollapsed = 14,
                radiusExpanded = 18,
                radiusWindow = 12,
                radiusCard = 10,
                radiusPill = 20,
                radiusNote = 18,
                radiusBubble = 10,
                progress = SkinProgress.Default,
                windowBg = Color.FromArgb(246, 248, 251),
                windowInk = Color.FromArgb(38, 46, 64),
                windowMuted = Color.FromArgb(120, 128, 146),
                windowEdge = Color.FromArgb(226, 231, 239),
                cardBg = Color.White,
                cardEdge = Color.FromArgb(230, 234, 242),
                railBg = Color.FromArgb(238, 241, 246),
                railActiveBg = Color.White,
                footerBg = Color.White,
                accentInk = Color.White,
                panelBg = Color.FromArgb(20, 24, 33),
                panelEdge = Color.FromArgb(44, 52, 68),
                bubbleIn = Color.FromArgb(38, 46, 64),
                bubbleInInk = Color.White,
                bubbleOut = Color.FromArgb(64, 124, 245),
                bubbleOutInk = Color.White,
                defaultThemeId = "midnight",
                headingFont = null
            });

            // ---------------- 01 无垠 Quiet：近黑 + 1px 细线 + 等宽数字 ----------------
            list.Add(new SkinDef
            {
                id = "quiet",
                name = "无垠",
                tagline = "近黑底 + 极细线 + 等宽数字，几乎不存在",
                light = false,
                colors = MakeTheme("skin-quiet", "无垠",
                    "#12151A", "#171B21", "#96A9FF", "#CFD6E4", "#46D39A",
                    "#E9ECF1", "#929AA7", "#666E7B", Color.FromArgb(19, 255, 255, 255)),
                accent2 = Hex("#CFD6E4"),
                radiusCollapsed = 10,
                radiusExpanded = 10,
                borderWidth = 1,
                borderColor = Color.FromArgb(19, 255, 255, 255),
                sheenAlpha = 0.045,
                progress = SkinProgress.Hairline,
                numberColor = Hex("#F1F3F7"),
                numberGlow = false,
                radiusWindow = 14,
                radiusCard = 3,
                radiusPill = 8,
                radiusNote = 14,
                radiusBubble = 8,
                hairlineCards = true,
                windowBg = Hex("#101318"),
                windowInk = Hex("#E9ECF1"),
                windowMuted = Hex("#929AA7"),
                windowEdge = Hex("#242A33"),
                cardBg = Hex("#12151A"),
                cardEdge = Hex("#232932"),
                railBg = Hex("#0C0F13"),
                railActiveBg = Color.FromArgb(255, 30, 35, 43),
                footerBg = Hex("#0D1015"),
                accentInk = Hex("#0A0C12"),
                panelBg = Hex("#101318"),
                panelEdge = Color.FromArgb(255, 34, 40, 49),
                bubbleIn = Hex("#171B21"),
                bubbleInInk = Hex("#E9ECF1"),
                bubbleOut = Color.FromArgb(255, 38, 45, 58),
                bubbleOutInk = Hex("#E9ECF1"),
                defaultThemeId = "graphite"
            });

            // ---------------- 02 云母 Mica：磨砂玻璃 ----------------
            list.Add(new SkinDef
            {
                id = "mica",
                name = "云母",
                tagline = "半透明磨砂玻璃，桌面从卡片后面透进来",
                light = false,
                colors = MakeTheme("skin-mica", "云母",
                    "#161D2C", "#101521", "#6EA8FE", "#8CC8FF", "#46D39A",
                    "#F3F7FF", "#AAB8D0", "#7E8DA6", Color.FromArgb(41, 255, 255, 255)),
                accent2 = Hex("#8CC8FF"),
                glow = Hex("#6EA8FE"),
                radiusCollapsed = 18,
                radiusExpanded = 18,
                borderColor = Color.FromArgb(41, 255, 255, 255),
                sheenAlpha = 0.15,
                glass = true,
                progress = SkinProgress.Pill,
                numberColor = Hex("#FBFDFF"),
                numberGlow = true,
                numberGlowAlpha = 0.26,
                radiusWindow = 20,
                radiusCard = 14,
                radiusPill = 17,
                radiusNote = 20,
                radiusBubble = 13,
                windowBg = Hex("#121826"),
                windowInk = Hex("#F3F7FF"),
                windowMuted = Hex("#93A3BC"),
                windowEdge = Hex("#2B3A52"),
                cardBg = Color.FromArgb(255, 26, 33, 49),
                cardEdge = Color.FromArgb(255, 46, 60, 82),
                railBg = Hex("#0F1420"),
                railActiveBg = Color.FromArgb(255, 31, 41, 60),
                footerBg = Hex("#101623"),
                accentInk = Hex("#06101F"),
                panelBg = Color.FromArgb(255, 20, 27, 42),
                panelEdge = Color.FromArgb(255, 44, 58, 80),
                bubbleIn = Color.FromArgb(255, 34, 44, 62),
                bubbleInInk = Hex("#F3F7FF"),
                bubbleOut = Hex("#4E8CFF"),
                bubbleOutInk = Hex("#06101F"),
                defaultThemeId = "midnight"
            });

            // ---------------- 03 宣纸 Ink：浅色 + 宋体 + 朱砂 ----------------
            list.Add(new SkinDef
            {
                id = "paper",
                name = "宣纸",
                tagline = "宣纸底、墨色字、朱砂一点，白天最舒服",
                light = true,
                colors = MakeTheme("skin-paper", "宣纸",
                    "#FDFAF2", "#F6EFE0", "#B23A2C", "#C4523F", "#2E9E6B",
                    "#2F2A24", "#7C6F5D", "#A4967F", Hex("#E0D5BF")),
                accent2 = Hex("#C4523F"),
                radiusCollapsed = 5,
                radiusExpanded = 5,
                borderColor = Hex("#E0D5BF"),
                sheenAlpha = 0.55,
                paperGrain = true,
                progress = SkinProgress.Brush,
                numberColor = Hex("#2C2721"),
                numberGlow = false,
                headingFont = "Noto Serif SC",
                seal = true,
                radiusWindow = 8,
                radiusCard = 7,
                radiusPill = 13,
                radiusNote = 8,
                radiusBubble = 10,
                windowBg = Hex("#FAF5EA"),
                windowInk = Hex("#2F2A24"),
                windowMuted = Hex("#7C6F5D"),
                windowEdge = Hex("#E2D7C2"),
                cardBg = Color.FromArgb(255, 255, 253, 247),
                cardEdge = Hex("#E7DDC9"),
                railBg = Hex("#F2EBDB"),
                railActiveBg = Color.FromArgb(255, 255, 251, 240),
                footerBg = Hex("#FFFDF7"),
                accentInk = Hex("#FFF9F0"),
                panelBg = Hex("#FBF6EA"),
                panelEdge = Hex("#E2D7C2"),
                bubbleIn = Hex("#F2E9D6"),
                bubbleInInk = Hex("#2F2A24"),
                bubbleOut = Hex("#B23A2C"),
                bubbleOutInk = Hex("#FFF6EC"),
                defaultThemeId = "ivory"
            });

            // ---------------- 04 仪表 Instrument：金属 + 琥珀数码 + 分刻 ----------------
            list.Add(new SkinDef
            {
                id = "instrument",
                name = "仪表",
                tagline = "金属壳 + 琥珀数码 + 分刻刻度，像一台倒计时装置",
                light = false,
                colors = MakeTheme("skin-instrument", "仪表",
                    "#1E1F1A", "#232420", "#FFB02E", "#FFC24A", "#8FD3A5",
                    "#EDE7D6", "#ACA48E", "#7A735E", Hex("#3E3F35")),
                accent2 = Hex("#FFC24A"),
                glow = Hex("#FFB02E"),
                radiusCollapsed = 6,
                radiusExpanded = 6,
                borderColor = Hex("#3E3F35"),
                sheenAlpha = 0.07,
                screws = true,
                progress = SkinProgress.Segmented,
                numberColor = Hex("#FFC24A"),
                numberGlow = true,
                numberGlowAlpha = 0.45,
                headingFont = "Bahnschrift",
                radiusWindow = 10,
                radiusCard = 6,
                radiusPill = 4,
                radiusNote = 10,
                radiusBubble = 6,
                windowBg = Hex("#1A1B17"),
                windowInk = Hex("#EDE7D6"),
                windowMuted = Hex("#9C947E"),
                windowEdge = Hex("#3A3B30"),
                cardBg = Hex("#20211B"),
                cardEdge = Hex("#34352B"),
                railBg = Hex("#161713"),
                railActiveBg = Color.FromArgb(255, 42, 42, 32),
                footerBg = Hex("#161713"),
                accentInk = Hex("#241A05"),
                panelBg = Hex("#1A1B17"),
                panelEdge = Hex("#3A3B30"),
                bubbleIn = Hex("#23241E"),
                bubbleInInk = Hex("#EDE7D6"),
                bubbleOut = Color.FromArgb(255, 58, 46, 18),
                bubbleOutInk = Hex("#FFD98A"),
                defaultThemeId = "sunset"
            });

            // ---------------- 05 霓虹 Neon：品红 × 青 ----------------
            list.Add(new SkinDef
            {
                id = "neon",
                name = "霓虹",
                tagline = "深空底上的双色霓虹，网格 + 辉光",
                light = false,
                colors = MakeTheme("skin-neon", "霓虹",
                    "#0D0A1A", "#130E28", "#FF3CAC", "#38E8FF", "#8FD3A5",
                    "#F5EEFF", "#ADA0CC", "#7C6F9B", Color.FromArgb(97, 255, 90, 200)),
                accent2 = Hex("#38E8FF"),
                glow = Hex("#FF3CAC"),
                radiusCollapsed = 14,
                radiusExpanded = 14,
                borderColor = Color.FromArgb(97, 255, 90, 200),
                sheenAlpha = 0.10,
                neonEdge = true,
                progress = SkinProgress.Neon,
                numberColor = Hex("#FFFFFF"),
                numberGlow = true,
                numberGlowAlpha = 0.50,
                radiusWindow = 16,
                radiusCard = 12,
                radiusPill = 12,
                radiusNote = 16,
                radiusBubble = 12,
                windowBg = Hex("#0E0A1C"),
                windowInk = Hex("#F5EEFF"),
                windowMuted = Hex("#A395C2"),
                windowEdge = Hex("#3A2A55"),
                cardBg = Hex("#151026"),
                cardEdge = Hex("#3A2A55"),
                railBg = Hex("#0B0716"),
                railActiveBg = Color.FromArgb(255, 38, 20, 58),
                footerBg = Hex("#0B0716"),
                accentInk = Hex("#1A0512"),
                panelBg = Hex("#130E24"),
                panelEdge = Hex("#3A2A55"),
                bubbleIn = Color.FromArgb(255, 30, 20, 50),
                bubbleInInk = Hex("#F5EEFF"),
                bubbleOut = Color.FromArgb(255, 60, 20, 80),
                bubbleOutInk = Hex("#FFE6F6"),
                defaultThemeId = "zi-fengxin"
            });

            return list.ToArray();
        }

        /// <summary>按设计稿的色值直接拼一套 ThemeDef（不走 39 套的推导规则，颜色更准）。</summary>
        private static ThemeDef MakeTheme(string id, string name,
            string bgTop, string bgBottom, string accent, string accent2, string success,
            string textMain, string textMuted, string textDim, Color edge)
        {
            var t = new ThemeDef();
            t.id = id;
            t.name = name;
            t.bgTop = Hex(bgTop);
            t.bgBottom = Hex(bgBottom);
            t.accent = Hex(accent);
            t.success = Hex(success);
            t.track = Mix(Hex(bgBottom), Color.White, 0.10);
            t.textMain = Hex(textMain);
            t.textMuted = Hex(textMuted);
            t.textDim = Hex(textDim);
            t.edge = edge;
            return t;
        }
    }
}
