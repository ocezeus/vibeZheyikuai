using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WorkCountdown
{
    /// <summary>胶囊菜单里的胶囊，顺序就是竖排顺序。</summary>
    public enum BubbleAction
    {
        Schedule = 0,
        Theme = 1,
        Clock = 2,
        Pin = 3,
        Preview = 4,
        Update = 5
    }

    /// <summary>
    /// 卡片右键的胶囊菜单（第二轮：胶囊自己长成面板）。
    /// 四颗 124×40 的胶囊竖排在卡片外侧；点有选项的那颗，它原地向右向下长成 246 宽的卡片
    /// （头 40 + 内容），下面的胶囊被顶下去，内容晚半拍淡入；再点别的先把旧的收回、再长新的。
    /// 颜色全部取当前主题（底色 / 文字 / 描边 / 强调色），不再用固定深色玻璃。
    /// 分层窗（WS_EX_LAYERED + UpdateLayeredWindow），圆角、半透明都靠像素 alpha；不画投影，
    /// 每颗胶囊和长开的卡片只用一条 1 DIP 的主题色细边勾边。收起时整块菜单淡出 + 往卡片方向缩回。
    /// </summary>
    public class BubbleMenuForm : Form
    {
        // ── 设计稿（96 DPI 的 DIP），运行时统一乘 scale ──
        public const int PillWDip = 124;
        public const int PillHDip = 40;
        public const int PillGapDip = 8;
        public const int PillRadiusDip = 20;
        public const int CardGapDip = 14;
        public const int PanelWDip = 246;
        public const int PanelRadiusDip = 18;
        // 气泡四周留的一点透明边：弹出时胶囊会从卡片方向滑进来（最多 12 DIP），
        // 不加边会被窗口裁掉；不再画投影，所以比原来的 ShadowPadDip(20) 收紧。
        public const int PadDip = 14;
        // 消失动画：整体淡出 + 轻微缩回卡片方向
        public const int ExitMs = 180;

        public const int ThemeExtraDip = 168;
        public const int ClockExtraDip = 130;
        public const int ScheduleExtraDip = 222;
        public const int PreviewExtraDip = 208;
        public const int UpdateExtraDip = 330;

        // 检查更新页底部的「更新记录」：一屏 5 行，多了用滚轮翻
        public const int UpdateLogRowHDip = 22;
        public const int UpdateLogViewHDip = 110;

        // 主题色卡：4 列 × 3 行 = 一屏 12 张，再多用滚轮滚
        public const int ThemeCols = 4;
        public const int ChipWDip = 51;
        public const int ChipHDip = 34;
        public const int ChipGapDip = 6;
        public const int ThemeViewHDip = 114;      // 3×34 + 2×6

        public const int PopStaggerMs = 70;
        public const int PopMs = 400;
        public const int ExtendMs = 360;

        public static readonly string[] Labels = new string[] { "新增日程", "修改主题", "触发上下班", "窗口置顶", "预览通知", "检查更新" };

        /// <summary>「预览通知」里可选的类型（key, 显示名, 副标题）。</summary>
        public static readonly string[] PreviewKeys = new string[] { "fixed", "lunch", "lark", "outlook", "local", "off" };
        public static readonly string[] PreviewNames = new string[] { "上班提醒", "午休提醒", "飞书会议", "Outlook", "普通日程", "下班提醒" };
        public static readonly string[] PreviewSubs = new string[] { "固定日程", "休息时段", "飞书日历", "Outlook", "手动日程", "打卡提醒" };

        private const int GwlExStyle = -20;
        private const int WsExTopMost = 0x00000008;
        private const int WsExLayered = 0x00080000;
        private const int UlwAlpha = 0x2;
        private const byte AcSrcAlpha = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref NativePoint pptDst, ref NativeSize psize, IntPtr hdcSrc, ref NativePoint pptSrc,
            int crKey, ref BlendFunction pblend, int dwFlags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

        private readonly Settings settings;
        private SkinDef skin;              // 整体界面主题（皮肤）：胶囊 / 面板 / 色卡的配色圆角都从这取
        private readonly bool skinDefault; // 默认皮肤：外观必须和 v3.6 完全一致
        private readonly bool glass;       // 磨砂玻璃（默认皮肤的「背景风格」或云母皮肤）
        private ThemeDef theme;
        private readonly Rectangle card;
        private readonly bool topMost;
        private readonly float scale;
        private readonly bool toRight;
        private readonly int winLeft, winWidth, baseTop;
        private readonly Action<string> onTheme;
        private readonly Action<bool> onPin;
        private readonly Action<bool> onClock;
        private readonly Action<string, string, int> onAddEvent;
        private readonly Action<int> onSettings;
        private readonly Func<string, string, string, string> onNewTheme;   // (名字, 底色, 高亮色) → 新主题 id
        private readonly Action<string> onPreview;      // 预览通知：类型 key（fixed / lunch / lark / outlook / local / off）
        private readonly Action onUpdateCheck;          // 检查更新
        private readonly Action onUpdateApply;          // 下载完 → 立即重启更新

        // 更新状态（由 Widget 的后台线程结果推进，气泡只负责画）
        private UpdatePhase updatePhase = UpdatePhase.Idle;
        private string updateLatest = "";
        private string updateDetail = "";
        private string updateNotes = "";
        private int updatePercent;
        private bool updateHasAsset;

        private readonly int pad;
        private readonly int pillW, pillH, gap;
        private readonly int panelW;
        private readonly int columnH;
        private readonly Rectangle[] pillRects;   // 收起时每颗胶囊的位置（头的位置）

        private readonly List<ThemeDef> swatches = new List<ThemeDef>();
        private string selectedTheme;
        private int themeScroll;           // 主题列表滚动量（物理像素）
        private int changelogScroll;       // 更新记录滚动量（物理像素），和主题列表各自独立
        private bool pinned;
        private string eventTitle = "临时日程";
        private string eventPlace = "";

        private int activeIndex = -1;      // 正在长开/已长开的那颗
        private int closingIndex = -1;     // 正在收回的那颗（和新的一起同时动）
        private int openedAt;
        private bool closingForm;
        private bool editingField;
        private int hoverKind;             // 0 无 / 1 胶囊头 / 2 内容项
        private int hoverIndex = -1;
        private readonly List<Hit> hits = new List<Hit>();

        // 展开 / 收回两条进度，同一段时间里各走各的
        private int animAt;
        private double openFrom, openTo, openExtra;
        private double closeFrom, closeTo, closeExtra;
        private bool animating;

        private readonly Font fPill, fTitle, fSub, fChip, fChipSub, fRow, fRowSub, fLink, fField, fFieldLabel, fKbd;
        private Bitmap frame;
        private readonly Timer anim;
        private Action afterClose;         // 淡出结束后再执行的动作（点某一项关闭菜单时用）
        private int exitAt;                // 消失动画的起点（0 = 还没开始退场）

        private enum IconKind { Calendar, Palette, Clock, Pin, Pencil, Arrow, Bell, Refresh }

        private struct Hit
        {
            public Rectangle rect;
            public string key;
            public Hit(Rectangle r, string k) { rect = r; key = k; }
        }

        public BubbleMenuForm(Settings settings, ThemeDef theme, Rectangle card, bool topMost,
            Action<string> onTheme, Action<bool> onPin, Action<bool> onClock,
            Action<string, string, int> onAddEvent, Action<int> onSettings,
            Func<string, string, string, string> onNewTheme = null,
            Action<string> onPreview = null, Action onUpdateCheck = null, Action onUpdateApply = null)
        {
            this.settings = settings;
            this.skin = Skins.Current(settings.ui);
            this.skinDefault = skin.IsDefault;
            this.glass = Skins.UsesGlass(settings.ui);
            // 默认皮肤继续用调用方传进来的主题；其它皮肤用自带配色（Widget 现在传的已经是 ResolveTheme，
            // 这里再兜一次，旧调用点也不会把皮肤配色盖掉）。
            this.theme = skin.colors != null ? skin.colors
                : (theme != null ? theme : Skins.ResolveTheme(settings.ui));
            this.card = card;
            this.topMost = topMost;
            this.onTheme = onTheme;
            this.onPin = onPin;
            this.onClock = onClock;
            this.onAddEvent = onAddEvent;
            this.onSettings = onSettings;
            this.onNewTheme = onNewTheme;
            this.onPreview = onPreview;
            this.onUpdateCheck = onUpdateCheck;
            this.onUpdateApply = onUpdateApply;

            // 皮肤不跟 39 套主题走：色卡高亮显示这套皮肤默认对应的那块（skin.defaultThemeId），
            // 点色卡还是照旧记下选择（切回默认皮肤时才生效）。
            selectedTheme = skin.colors != null
                ? (string.IsNullOrEmpty(skin.defaultThemeId) ? this.theme.id : skin.defaultThemeId)
                : this.theme.id;
            pinned = settings.ui.alwaysOnTop;

            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            if (scale < 0.75f) scale = 0.75f;
            if (scale > 3f) scale = 3f;

            pad = S(PadDip);
            pillW = S(PillWDip);
            pillH = S(PillHDip);
            gap = S(PillGapDip);
            panelW = S(PanelWDip);
            columnH = Labels.Length * pillH + (Labels.Length - 1) * gap;

            Screen scr = Screen.FromRectangle(card);
            Rectangle wa = scr.WorkingArea;
            int need = S(CardGapDip) + panelW;
            toRight = (wa.Right - card.Right >= need) || (card.Left - wa.Left < need);

            winWidth = panelW + pad * 2;
            int left = toRight ? card.Right + S(CardGapDip) - pad : card.Left - S(CardGapDip) - panelW - pad;
            left = Math.Max(wa.Left + 2, Math.Min(left, wa.Right - winWidth - 2));
            winLeft = left;
            baseTop = card.Top + card.Height / 2 - columnH / 2 - pad;
            Bounds = new Rectangle(winLeft, baseTop, winWidth, columnH + pad * 2);
            LayoutWindow();

            pillRects = new Rectangle[Labels.Length];
            for (int i = 0; i < Labels.Length; i++)
                pillRects[i] = new Rectangle(pad, pad + i * (pillH + gap), pillW, pillH);

            // 胶囊文字字体跟皮肤走：宣纸宋体、仪表 Bahnschrift（其它皮肤 / 默认皮肤还是界面中文字体）
            fPill = !skinDefault && !string.IsNullOrWhiteSpace(skin.headingFont)
                ? UiFont.MakeFamily(skin.headingFont, 8.75f, FontStyle.Regular)
                : UiFont.Make(8.75f, FontStyle.Regular);
            fTitle = UiFont.Make(9.75f, FontStyle.Bold);
            fSub = UiFont.Make(7.5f, FontStyle.Regular);
            fChip = UiFont.Make(8.25f, FontStyle.Bold);
            fChipSub = UiFont.Make(6.75f, FontStyle.Regular);
            fRow = UiFont.Make(9f, FontStyle.Regular);
            fRowSub = UiFont.Make(7.125f, FontStyle.Regular);
            fLink = UiFont.Make(8.25f, FontStyle.Regular);
            fField = UiFont.Make(8.625f, FontStyle.Regular);
            fFieldLabel = UiFont.Make(7.125f, FontStyle.Regular);
            fKbd = UiFont.Make(7.5f, FontStyle.Bold);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = false;
            TopMost = topMost;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            BuildSwatches();

            anim = new Timer();
            anim.Interval = 15;
            anim.Tick += delegate { StepAnim(); };
        }

        /// <summary>胶囊列总高（DIP）：6×40 + 5×8 = 280。</summary>
        public static int ColumnHDip { get { return Labels.Length * PillHDip + (Labels.Length - 1) * PillGapDip; } }

        public static int ExtraHDip(BubbleAction a)
        {
            if (a == BubbleAction.Theme) return ThemeExtraDip;
            if (a == BubbleAction.Clock) return ClockExtraDip;
            if (a == BubbleAction.Preview) return PreviewExtraDip;
            if (a == BubbleAction.Update) return UpdateExtraDip;
            return ScheduleExtraDip;
        }

        /// <summary>长开后整颗卡片的高度（头 + 内容）。</summary>
        public static int ItemHDip(BubbleAction a) { return PillHDip + ExtraHDip(a); }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | WsExLayered;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            openedAt = Environment.TickCount;
            anim.Start();
            Invalidate();
        }

        private int S(float dip)
        {
            return (int)Math.Round(dip * scale);
        }

        // ---------------- 颜色 ----------------

        internal static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        internal static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        private static Color Rgba(Color c, int a)
        {
            if (a < 0) a = 0;
            if (a > 255) a = 255;
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        private static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a * c.A / 255)), c.R, c.G, c.B);
        }

        private bool LightTheme { get { return theme != null && Luminance(theme.bgTop) > 0.56; } }

        private Color Ink { get { return theme.textMain; } }
        private Color Muted { get { return theme.textMuted; } }
        private Color Dim { get { return theme.textDim; } }
        private Color Accent { get { return theme.accent; } }
        private Color SurfaceTop { get { return skinDefault ? Mix(theme.bgTop, Color.White, LightTheme ? 0.02 : 0.04) : Mix(skin.panelBg, Color.White, 0.03); } }
        private Color SurfaceBottom { get { return skinDefault ? theme.bgBottom : skin.panelBg; } }
        private Color FieldBg { get { return LightTheme ? Rgba(Color.Black, 12) : Rgba(Color.White, 14); } }
        private Color FieldBorder { get { return LightTheme ? Rgba(Color.Black, 26) : Rgba(Color.White, 22); } }
        private Color HoverTint { get { return LightTheme ? Rgba(Color.Black, 14) : Rgba(Color.White, 10); } }
        private Color OnAccent
        {
            get
            {
                if (!skinDefault) return skin.accentInk;
                return Luminance(Accent) > 0.55 ? Color.FromArgb(23, 17, 17) : Color.White;
            }
        }

        // ---- 皮肤圆角（默认皮肤保持 v3.6 的数值）----
        /// <summary>胶囊 / 长开面板的圆角：收起用 skin.radiusPill；长开时默认皮肤保持 18、皮肤也走 radiusPill。</summary>
        private int PillCorner(bool expanded)
        {
            if (!expanded) return skinDefault ? pillH / 2 : S(skin.radiusPill);
            return skinDefault ? S(PanelRadiusDip) : S(skin.radiusPill);
        }
        private int CardCorner { get { return S(skinDefault ? 9 : skin.radiusCard); } }
        private int ChipCorner { get { return S(skinDefault ? 10 : skin.radiusCard); } }
        private int RowCorner { get { return S(skinDefault ? 11 : skin.radiusCard); } }
        private int FieldCorner { get { return S(skinDefault ? 8 : skin.radiusCard); } }

        // ---------------- 主题色卡 ----------------

        /// <summary>列表里放全部主题（内置 39 套 + 自建），按 4 列排，多了滚动。</summary>
        private void BuildSwatches()
        {
            swatches.Clear();
            foreach (ThemeDef t in Themes.All) swatches.Add(t);
        }

        /// <summary>色卡列表的可视区（相对整颗长开的卡片）。</summary>
        private Rectangle ThemeView(Rectangle item)
        {
            return new Rectangle(item.X + S(12), item.Y + S(46), item.Width - S(24), S(ThemeViewHDip));
        }

        /// <summary>第 i 张色卡的位置（已经扣掉滚动量）。</summary>
        private Rectangle ThemeChip(Rectangle view, int i)
        {
            int col = i % ThemeCols, row = i / ThemeCols;
            int cw = S(ChipWDip);
            int gapX = ThemeCols > 1 ? (view.Width - cw * ThemeCols) / (ThemeCols - 1) : 0;
            int ch = S(ChipHDip), gapY = S(ChipGapDip);
            return new Rectangle(view.X + col * (cw + gapX), view.Y + row * (ch + gapY) - themeScroll, cw, ch);
        }

        private int ThemeMaxScroll(Rectangle view)
        {
            int rows = Math.Max(1, (swatches.Count + ThemeCols - 1) / ThemeCols);
            int content = rows * S(ChipHDip) + Math.Max(0, rows - 1) * S(ChipGapDip);
            return Math.Max(0, content - view.Height);
        }

        /// <summary>打开面板时把当前选中的那套滚进视野（居中）。</summary>
        private void ScrollThemeToSelected()
        {
            int idx = swatches.FindIndex(delegate(ThemeDef t) { return t.id == selectedTheme; });
            if (idx < 0 || activeIndex < 0) return;
            Rectangle view = ThemeView(ItemRectFinal(activeIndex));
            int row = idx / ThemeCols;
            int rowTop = row * (S(ChipHDip) + S(ChipGapDip));
            int max = ThemeMaxScroll(view);
            themeScroll = Math.Max(0, Math.Min(max, rowTop - (view.Height - S(ChipHDip)) / 2));
        }

        /// <summary>色卡上的名字：去掉「经典 · 」这类前缀，自建主题就用原名。</summary>
        private static string ShortThemeName(ThemeDef t)
        {
            if (t == null || string.IsNullOrEmpty(t.name)) return "";
            int i = t.name.IndexOf('·');
            return i >= 0 ? t.name.Substring(i + 1).Trim() : t.name;
        }

        // ---------------- 动画 ----------------

        private static double Clamp01(double t)
        {
            if (t < 0) return 0;
            if (t > 1) return 1;
            return t;
        }

        private static double CssEase(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double lo = 0, hi = 1;
            for (int i = 0; i < 20; i++)
            {
                double mid = (lo + hi) * 0.5;
                if (BezierX(mid) < t) lo = mid; else hi = mid;
            }
            return BezierY((lo + hi) * 0.5);
        }

        private static double BezierX(double u)
        {
            double v = 1 - u;
            return 3 * v * v * u * 0.22 + 3 * v * u * u * 0.28 + u * u * u;
        }

        private static double BezierY(double u)
        {
            double v = 1 - u;
            return 3 * v * v * u * 0.90 + 3 * v * u * u * 1.00 + u * u * u;
        }

        private static double BackOut(double t)
        {
            double c1 = 1.70158;
            double c3 = c1 + 1;
            double u = t - 1;
            return 1 + c3 * u * u * u + c1 * u * u;
        }

        private double PopProgress(int index)
        {
            if (openedAt == 0) return 0;
            int elapsed = Environment.TickCount - openedAt - index * PopStaggerMs;
            if (elapsed <= 0) return 0;
            return Clamp01(elapsed / (double)PopMs);
        }

        private void StepAnim()
        {
            // 退场：整体淡出 / 收拢，走完才真正关窗口
            if (closingForm)
            {
                if (exitAt == 0 || Environment.TickCount - exitAt >= ExitMs) { FinishClose(); return; }
                Invalidate();
                return;
            }
            if (animating)
            {
                double p = Clamp01((Environment.TickCount - animAt) / (double)ExtendMs);
                double e = CssEase(p);
                openExtra = openFrom + (openTo - openFrom) * e;
                closeExtra = closeFrom + (closeTo - closeFrom) * e;
                if (p >= 1)
                {
                    animating = false;
                    openExtra = openTo;
                    closeExtra = closeTo;
                    if (closeTo <= 0.01) { closingIndex = -1; closeFrom = 0; closeExtra = 0; }
                    if (openTo <= 0.01) { activeIndex = -1; openFrom = 0; openExtra = 0; }
                }
            }
            LayoutWindow();
            if (!IsIdle()) Invalidate();
            else
            {
                anim.Stop();
                Invalidate();
            }
        }

        /// <summary>窗口高度同时容纳「正在收」和「正在长」的两块，放不下就整体上移。</summary>
        private void LayoutWindow()
        {
            Screen scr = Screen.FromRectangle(card);
            Rectangle wa = scr.WorkingArea;
            int h = columnH + (int)Math.Round(openExtra + closeExtra) + pad * 2;
            int top = baseTop;
            if (top + h > wa.Bottom - 2) top = wa.Bottom - h - 2;
            if (top < wa.Top + 2) top = wa.Top + 2;
            if (Height != h || Top != top) SetBounds(winLeft, top, winWidth, h);
        }

        private bool IsIdle()
        {
            if (openedAt == 0) return false;
            bool popDone = Environment.TickCount - openedAt > PopStaggerMs * Labels.Length + PopMs + 120;
            return popDone && !animating;
        }

        /// <summary>退场进度：0 = 还在，1 = 完全消失。</summary>
        private double ExitProgress
        {
            get
            {
                if (exitAt == 0) return 0;
                return Clamp01((Environment.TickCount - exitAt) / (double)ExitMs);
            }
        }

        // ---------------- 交互 ----------------

        private void OpenItem(int index)
        {
            int old = activeIndex;
            if (old == index) { CloseItem(); return; }
            closingIndex = old;
            closeFrom = old >= 0 ? openExtra : 0;
            closeTo = 0;
            closeExtra = closeFrom;
            activeIndex = index;
            openFrom = 0;
            openTo = S(ExtraHDip((BubbleAction)index));
            openExtra = 0;
            animAt = Environment.TickCount;
            animating = true;
            hoverKind = 0; hoverIndex = -1;
            if (index == (int)BubbleAction.Theme) ScrollThemeToSelected();
            if (index == (int)BubbleAction.Update) changelogScroll = 0;
            // 更新面板一展开就去查一次（已经查过 / 正在下载就不重复查）
            if (index == (int)BubbleAction.Update && updatePhase == UpdatePhase.Idle && onUpdateCheck != null)
                onUpdateCheck();
            hits.Clear();
            BuildHits();
            anim.Start();
            Invalidate();
        }

        private void CloseItem()
        {
            if (activeIndex < 0) return;
            closingIndex = activeIndex;
            closeFrom = openExtra;
            closeTo = 0;
            closeExtra = closeFrom;
            activeIndex = -1;
            openFrom = openTo = openExtra = 0;
            animAt = Environment.TickCount;
            animating = true;
            hits.Clear();
            anim.Start();
            Invalidate();
        }

        private void ToggleItem(int index)
        {
            if (index == (int)BubbleAction.Pin)
            {
                pinned = !pinned;
                if (onPin != null) onPin(pinned);
                if (!anim.Enabled) anim.Start();
                Invalidate();
                return;
            }
            if (activeIndex == index) { CloseItem(); return; }
            OpenItem(index);
        }

        private double OpenProgress
        {
            get { return openTo <= 0.01 ? 0 : Clamp01(openExtra / openTo); }
        }

        private double CloseProgress
        {
            get { return closeFrom <= 0.01 ? 0 : Clamp01(closeExtra / closeFrom); }
        }

        private Rectangle ItemRect(int index)
        {
            return ItemRectAt(index, false);
        }

        /// <summary>展开完成后的位置：命中区按最终位置算，动画途中点也点得中。</summary>
        private Rectangle ItemRectFinal(int index)
        {
            return ItemRectAt(index, true);
        }

        private Rectangle ItemRectAt(int index, bool final)
        {
            int offset = 0;
            if (!final)
            {
                if (activeIndex >= 0 && index > activeIndex) offset += (int)Math.Round(openExtra);
                if (closingIndex >= 0 && index > closingIndex) offset += (int)Math.Round(closeExtra);
            }
            else if (activeIndex >= 0 && index > activeIndex)
            {
                offset += (int)Math.Round(openTo);
            }

            int own = 0;
            int w = pillW;
            if (!final)
            {
                if (index == activeIndex)
                {
                    own = (int)Math.Round(openExtra);
                    w = pillW + (int)Math.Round((panelW - pillW) * OpenProgress);
                }
                else if (index == closingIndex)
                {
                    own = (int)Math.Round(closeExtra);
                    w = pillW + (int)Math.Round((panelW - pillW) * CloseProgress);
                }
            }
            else if (index == activeIndex)
            {
                own = (int)Math.Round(openTo);
                w = panelW;
            }

            int y = pad + index * (pillH + gap) + offset;
            int x = toRight ? pad : pad + panelW - w;
            return new Rectangle(x, y, w, pillH + own);
        }

        private Rectangle HeadRect(int index)
        {
            Rectangle r = ItemRect(index);
            return new Rectangle(r.X, r.Y, r.Width, pillH);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (closingForm) return;
            int kind = 0, index = -1;
            HitTest(e.Location, out kind, out index);
            if (kind != hoverKind || index != hoverIndex)
            {
                hoverKind = kind; hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverKind != 0) { hoverKind = 0; hoverIndex = -1; Invalidate(); }
        }

        /// <summary>主题列表 / 更新记录上的滚轮：一格滚一行，滚到头就不动。</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (activeIndex == (int)BubbleAction.Theme)
            {
                Rectangle view = ThemeView(ItemRectFinal(activeIndex));
                if (!view.Contains(e.Location)) return;
                int step = Math.Max(S(8), S(ChipHDip + ChipGapDip));
                int max = ThemeMaxScroll(view);
                int next = Math.Max(0, Math.Min(max, themeScroll - e.Delta * step / 120));
                if (next == themeScroll) return;
                themeScroll = next;
                BuildHits();
                Invalidate();
                return;
            }
            if (activeIndex == (int)BubbleAction.Update)
            {
                Rectangle view = ChangelogView(ItemRectFinal(activeIndex));
                if (!view.Contains(e.Location)) return;
                int step = S(UpdateLogRowHDip);
                int max = ChangelogMaxScroll(view);
                int next = Math.Max(0, Math.Min(max, changelogScroll - e.Delta * step / 120));
                if (next == changelogScroll) return;
                changelogScroll = next;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || closingForm) return;

            for (int i = 0; i < hits.Count; i++)
            {
                if (!hits[i].rect.Contains(e.Location)) continue;
                RunHit(hits[i].key);
                return;
            }
            for (int i = 0; i < Labels.Length; i++)
            {
                if (!HeadRect(i).Contains(e.Location)) continue;
                ToggleItem(i);
                return;
            }
            CloseMenu();
        }

        private void HitTest(Point p, out int kind, out int index)
        {
            kind = 0; index = -1;
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].rect.Contains(p)) { kind = 2; index = i; return; }
            for (int i = 0; i < Labels.Length; i++)
                if (HeadRect(i).Contains(p)) { kind = 1; index = i; return; }
        }

        private void RunHit(string key)
        {
            if (key == null) return;
            if (key == "title") { EditField(true); return; }
            if (key == "place") { EditField(false); return; }
            if (key == "save")
            {
                string title = string.IsNullOrEmpty(eventTitle.Trim()) ? "临时日程" : eventTitle.Trim();
                string place = eventPlace.Trim();
                // 先让菜单淡出，关掉之后再建日程（不然新日程的托盘气泡会被菜单压住）
                CloseMenu(delegate { if (onAddEvent != null) onAddEvent(title, place, 30); });
                return;
            }
            if (key == "detail") { CloseMenu(delegate { if (onSettings != null) onSettings(2); }); return; }
            if (key == "newtheme") { OpenNewTheme(); return; }
            if (key == "update:check")
            {
                if (updatePhase != UpdatePhase.Checking && updatePhase != UpdatePhase.Downloading && onUpdateCheck != null)
                    onUpdateCheck();
                return;
            }
            if (key == "update:apply")
            {
                // 先让菜单淡出，再重启更新（新进程要等本进程退出）
                CloseMenu(delegate { if (onUpdateApply != null) onUpdateApply(); });
                return;
            }
            if (key == "update:page")
            {
                CloseMenu(delegate { OpenReleasesPage(); });
                return;
            }
            if (key.StartsWith("preview:", StringComparison.Ordinal))
            {
                string kind = key.Substring(8);
                CloseMenu(delegate { if (onPreview != null) onPreview(kind); });
                return;
            }
            if (key == "clock:1") { CloseMenu(delegate { if (onClock != null) onClock(true); }); return; }
            if (key == "clock:2") { CloseMenu(delegate { if (onClock != null) onClock(false); }); return; }
            if (key.StartsWith("theme:", StringComparison.Ordinal))
            {
                int i;
                if (!int.TryParse(key.Substring(6), out i)) return;
                if (i < 0 || i >= swatches.Count) return;
                ThemeDef t = swatches[i];
                selectedTheme = t.id;
                // 默认皮肤：立刻换色预览；其它皮肤配色固定，只记下选择（切回默认皮肤时生效）
                if (skinDefault) theme = t;
                if (onTheme != null) onTheme(t.id);
                Invalidate();
                return;
            }
            if (key.StartsWith("add:", StringComparison.Ordinal))
            {
                int min;
                if (!int.TryParse(key.Substring(4), out min)) return;
                string title = string.IsNullOrEmpty(eventTitle.Trim()) ? "临时日程" : eventTitle.Trim();
                string place = eventPlace.Trim();
                CloseMenu(delegate { if (onAddEvent != null) onAddEvent(title, place, min); });
                return;
            }
        }

        // ---------------- 标题 / 地点编辑 ----------------

        private void EditField(bool title)
        {
            if (activeIndex != (int)BubbleAction.Schedule) return;
            Rectangle item = ItemRect(activeIndex);
            Rectangle field = title
                ? new Rectangle(item.X + S(12), item.Y + S(48), item.Width - S(24), S(32))
                : new Rectangle(item.X + S(12), item.Y + S(88), item.Width - S(24), S(32));

            editingField = true;
            // WinForms 控件不支持半透明底色：给编辑器算两个不透明色（皮肤用 panelBg 当基色）
            Color baseBg = skinDefault ? theme.bgBottom : skin.panelBg;
            Color solidBg = LightTheme ? Mix(baseBg, Color.White, 0.55) : Mix(baseBg, Color.Black, skinDefault ? 0.25 : 0.18);
            Color solidBorder = LightTheme ? Mix(baseBg, Color.Black, 0.25) : Mix(baseBg, Color.White, skinDefault ? 0.30 : 0.16);
            var ed = new FieldEditor(title ? "标题" : "地点", title ? eventTitle : eventPlace,
                new Rectangle(Left + field.X, Top + field.Y, field.Width, field.Height),
                solidBg, Ink, Accent, solidBorder, scale, topMost);
            ed.FormClosed += delegate
            {
                editingField = false;
                if (!ed.Cancelled)
                {
                    if (title) eventTitle = ed.Value;
                    else eventPlace = ed.Value;
                }
                if (!IsDisposed) { Activate(); Invalidate(); }
            };
            ed.Show(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && !editingField) { CloseMenu(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// 新建主题：直接开设置里那个调色盘（以当前配色为起点），存完立刻切过去。
        /// 走 onNewTheme 回 Widget，由它写设置、存盘、应用；这里只负责刷新自己的列表。
        /// </summary>
        private void OpenNewTheme()
        {
            if (onNewTheme == null) return;
            ThemeDef seed = theme != null ? theme : Skins.ResolveTheme(settings.ui);
            string id = null;
            editingField = true;      // 弹窗期间失焦，别把气泡收掉
            try
            {
                using (var dlg = new ColorWheelForm("新建主题", "我的主题", seed.bgTop, seed.accent))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    id = onNewTheme(dlg.ThemeName, dlg.BgHex, dlg.BarHex);
                }
            }
            finally { editingField = false; }

            if (!string.IsNullOrEmpty(id))
            {
                selectedTheme = id;
                // 默认皮肤立刻应用新主题；其它皮肤配色固定，只把色卡选中环移过去
                if (skinDefault) theme = Themes.Get(id);
                BuildSwatches();
                ScrollThemeToSelected();
                BuildHits();
            }
            if (!IsDisposed) { Activate(); Invalidate(); }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible && !closingForm && !editingField) CloseMenu();
        }

        /// <summary>正在播消失动画（窗口还没真正关掉）。右键这时再按一次要能直接开新的。</summary>
        public bool IsClosing { get { return closingForm; } }

        public void CloseMenu()
        {
            CloseMenu(null);
        }

        /// <summary>
        /// 收起菜单：先播 180ms 的淡出 / 收拢动画（卡片整体缩小一点、往卡片方向退），
        /// 播完再关窗口。给了 after 就在关掉之后执行（点胶囊里的动作时用），
        /// 免得菜单还压在设置窗 / 色盘上面。
        /// </summary>
        public void CloseMenu(Action after)
        {
            if (closingForm)
            {
                if (after != null) afterClose = after;
                return;
            }
            closingForm = true;
            afterClose = after;
            // 没显示过 / 测试里离屏渲染的窗口没有消息循环：直接关，别留个空转的定时器
            if (!Visible || IsDisposed || !IsHandleCreated
                || Environment.GetEnvironmentVariable("WC_NO_EXIT_ANIM") == "1")
            {
                FinishClose();
                return;
            }
            exitAt = Environment.TickCount;
            anim.Start();
            Invalidate();
        }

        /// <summary>退场动画走完（或立刻收起）：关窗口，再把等着执行的动作放出去。</summary>
        private void FinishClose()
        {
            anim.Stop();
            Action act = afterClose;
            afterClose = null;
            if (!IsDisposed) Close();
            if (act != null) act();
        }

        // ---------------- 命中区 ----------------

        private void BuildHits()
        {
            hits.Clear();
            if (activeIndex < 0) return;
            Rectangle item = ItemRectFinal(activeIndex);
            int x = item.X + S(12);
            int w = item.Width - S(24);
            BubbleAction act = (BubbleAction)activeIndex;

            if (act == BubbleAction.Schedule)
            {
                hits.Add(new Hit(new Rectangle(x, item.Y + S(48), w, S(32)), "title"));
                hits.Add(new Hit(new Rectangle(x, item.Y + S(88), w, S(32)), "place"));
                int top = item.Y + S(142);
                int gapX = S(7);
                int chipW = (w - gapX * 2) / 3;
                int[] mins = new int[] { 15, 30, 60 };
                for (int i = 0; i < 3; i++)
                    hits.Add(new Hit(new Rectangle(x + i * (chipW + gapX), top, chipW, S(44)), "add:" + mins[i]));
                hits.Add(new Hit(new Rectangle(x, item.Y + S(196), w, S(34)), "save"));
                hits.Add(new Hit(new Rectangle(x, item.Y + S(238), w, S(22)), "detail"));
            }
            else if (act == BubbleAction.Theme)
            {
                Rectangle view = ThemeView(item);
                for (int i = 0; i < swatches.Count; i++)
                {
                    Rectangle chip = ThemeChip(view, i);
                    if (chip.Bottom <= view.Top || chip.Top >= view.Bottom) continue;
                    Rectangle hit = Rectangle.Intersect(chip, view);
                    if (hit.Width <= 0 || hit.Height <= 0) continue;
                    hits.Add(new Hit(hit, "theme:" + i));
                }
                hits.Add(new Hit(NewThemeRect(item), "newtheme"));
            }
            else if (act == BubbleAction.Clock)
            {
                int top = item.Y + S(50);
                hits.Add(new Hit(new Rectangle(x, top, w, S(40)), "clock:1"));
                hits.Add(new Hit(new Rectangle(x, top + S(48), w, S(40)), "clock:2"));
            }
            else if (act == BubbleAction.Preview)
            {
                for (int i = 0; i < PreviewKeys.Length; i++)
                {
                    Rectangle chip = PreviewChipRect(item, i);
                    if (chip.Width > 0) hits.Add(new Hit(chip, "preview:" + PreviewKeys[i]));
                }
            }
            else if (act == BubbleAction.Update)
            {
                if (updatePhase == UpdatePhase.Available || updatePhase == UpdatePhase.Ready)
                    hits.Add(new Hit(UpdatePrimaryRect(item), updateHasAsset ? "update:apply" : "update:page"));
                else if (updatePhase != UpdatePhase.Downloading && updatePhase != UpdatePhase.Checking)
                    hits.Add(new Hit(UpdatePrimaryRect(item), "update:check"));
                hits.Add(new Hit(UpdateLinkRect(item), "update:page"));
            }
        }

        /// <summary>「＋ 新建主题」那一行（色卡列表下面）。</summary>
        private Rectangle NewThemeRect(Rectangle item)
        {
            return new Rectangle(item.X + S(12), item.Y + S(166), item.Width - S(24), S(34));
        }

        // ---------------- 绘制 ----------------

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            Render();
            if (e.Graphics != null) e.Graphics.DrawImageUnscaled(frame, 0, 0);
            Push();
        }

        private void Render()
        {
            if (frame == null || frame.Width != Width || frame.Height != Height)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }
            frame.SetResolution(96f * scale, 96f * scale);
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                // 不再画投影：气泡只有主题色细边 + 主题底色两层，贴在卡片旁边更干净
                for (int i = 0; i < Labels.Length; i++) DrawItem(g, i);
                DrawHover(g);
            }
        }

        private void DrawHover(Graphics g)
        {
            if (hoverKind != 2 || hoverIndex < 0 || hoverIndex >= hits.Count) return;
            Rectangle r = hits[hoverIndex].rect;
            using (var p = RoundPath(r, CardCorner))
            using (var b = new SolidBrush(HoverTint))
                g.FillPath(b, p);
        }

        private void DrawItem(Graphics g, int index)
        {
            double pop = PopProgress(index);
            if (pop <= 0.001) return;

            Rectangle r = ItemRect(index);
            bool isActive = activeIndex == index;
            bool isClosing = closingIndex == index;
            bool expanded = isActive || isClosing;
            bool hover = hoverKind == 1 && hoverIndex == index;
            bool muted = activeIndex >= 0 && !isActive;
            bool on = index == (int)BubbleAction.Pin && pinned;

            int a = (int)Math.Round(255 * Math.Min(1, pop * 1.8));
            double ease = BackOut(pop);
            float sc = (float)(0.92 + 0.08 * ease);
            int slide = (int)Math.Round(-S(12) * (1 - pop) * (toRight ? 1 : -1));

            // 退场：淡出 + 缩回卡片方向（一个整体动作，比「啪」一下消失柔和）
            double exit = ExitProgress;
            if (exit > 0.001)
            {
                double eased = exit * (2 - exit);      // 二次缓出：开头快一点，尾巴慢慢收干净
                double keep = 1 - eased;
                a = (int)Math.Round(a * keep);
                if (a <= 0) return;
                sc = (float)(sc * (0.94 + 0.06 * keep));
                slide += (int)Math.Round(S(9) * eased * (toRight ? -1 : 1));
            }

            // 退场时以「贴卡片的那条边」为锚点缩小：看起来像整块被卡片吸回去，
            // 而不是原地缩小；弹出的缩放仍以中心为锚点。
            bool leaving = exit > 0.001;
            float anchorX = leaving ? (toRight ? r.Left : r.Right) : r.Left + r.Width / 2f;
            float anchorY = r.Top + r.Height / 2f;

            var state = g.Save();
            g.TranslateTransform(anchorX + slide, anchorY);
            g.ScaleTransform(sc, sc);
            g.TranslateTransform(-anchorX, -anchorY);

            int radius = PillCorner(expanded);
            bool lit = isActive || on || hover;     // 皮肤的高亮态（选中 / 置顶 / 悬停）
            using (var path = RoundPath(r, radius))
            {
                if (glass)
                {
                    Frost.PaintSurface(g, path, r, theme, a / 255.0, 0.86);
                }
                else if (skinDefault)
                {
                    using (var br = new LinearGradientBrush(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)),
                        Alpha(SurfaceTop, a), Alpha(SurfaceBottom, a), 90f))
                        g.FillPath(br, path);
                }
                else
                {
                    // 非默认皮肤：霓虹先画外圈辉光（窗口四周留了 PadDip，露得出来），再填皮肤面板底色。
                    // 底色从 panelBg 稍微提亮一点点到 panelBg，跟设计里的「面板 + 极淡高光」一致。
                    if (skin.neonEdge && lit) PaintNeonGlow(g, path, r, a, isActive || on);
                    using (var br = new LinearGradientBrush(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)),
                        Alpha(SurfaceTop, a), Alpha(SurfaceBottom, a), 90f))
                        g.FillPath(br, path);
                    PaintItemSheen(g, path, r, a);
                }
                if (hover && !isActive)
                    using (var b = new SolidBrush(Alpha(HoverTint, a))) g.FillPath(b, path);
                if (!skinDefault)
                {
                    // 皮肤细边：平时是 panelEdge，选中 / 置顶 / 悬停换成强调色；霓虹用 品红→青 渐变描边
                    if (skin.neonEdge && lit)
                    {
                        using (var grad = new LinearGradientBrush(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)),
                            Alpha(Accent, a), Alpha(skin.accent2, a), 35f))
                        using (var pen = new Pen(grad, Math.Max(1.2f, scale * 1.2f)))
                            g.DrawPath(pen, path);
                    }
                    else
                    {
                        Color skinBorder = isActive ? Alpha(Accent, (int)(a * 0.85))
                            : on ? Alpha(Accent, (int)(a * 0.70))
                            : hover ? Alpha(Accent, (int)(a * 0.58))
                            : Alpha(skin.panelEdge, a);
                        using (var pen = new Pen(skinBorder, Math.Max(1f, scale)))
                            g.DrawPath(pen, path);
                    }
                }
                else
                {
                    // 主题色细边：胶囊 / 长开的卡片统一用强调色，1 DIP 细线，状态只改透明度
                    Color border = isActive ? Alpha(Accent, (int)(a * 0.72))
                        : on ? Alpha(Accent, (int)(a * 0.66))
                        : hover ? Alpha(Accent, (int)(a * 0.58))
                        : Alpha(Accent, (int)(a * 0.46));
                    using (var pen = new Pen(border, Math.Max(1f, scale)))
                        g.DrawPath(pen, path);
                }

                if (expanded)
                {
                    var saved = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    DrawBody(g, index, r, a, isActive);
                    g.Clip = saved;
                }
                DrawHead(g, index, r, a, expanded, on, muted);
            }
            g.Restore(state);
        }

        /// <summary>皮肤胶囊 / 面板的高光：顶部一层很淡的白色渐变（宣纸最明显，无垠几乎看不见）。</summary>
        private void PaintItemSheen(Graphics g, GraphicsPath path, Rectangle r, int a)
        {
            if (skin.sheenAlpha <= 0) return;
            GraphicsState st = g.Save();
            try
            {
                g.SetClip(path, CombineMode.Intersect);
                int bandH = Math.Max(1, (int)Math.Round(r.Height * 0.55));
                var band = new Rectangle(r.X, r.Y, Math.Max(1, r.Width), bandH);
                using (var br = new LinearGradientBrush(band,
                    Alpha(Color.White, (int)(a * Math.Min(0.8, skin.sheenAlpha * 0.8))),
                    Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(br, band);
            }
            finally { g.Restore(st); }
        }

        /// <summary>霓虹外辉光：沿圆角叠几层宽一点的强调色描边（填充前画，内侧会被底色盖住）。</summary>
        private void PaintNeonGlow(Graphics g, GraphicsPath path, Rectangle r, int a, bool strong)
        {
            double k = strong ? 1.0 : 0.6;
            for (int i = 3; i >= 1; i--)
            {
                float wid = Math.Max(2f, S(1.5f + i * 2.2f));
                using (var pen = new Pen(Alpha(Accent, (int)(a * 0.10 * i * k)), wid))
                    g.DrawPath(pen, path);
            }
            for (int i = 2; i >= 1; i--)
            {
                float wid = Math.Max(2f, S(1.5f + i * 2.2f));
                using (var pen = new Pen(Alpha(skin.accent2, (int)(a * 0.06 * i * k)), wid))
                    g.DrawPath(pen, path);
            }
        }

        private void DrawHead(Graphics g, int index, Rectangle r, int a, bool active, bool on, bool muted)
        {
            Color ink = Alpha(Ink, muted ? (int)(a * 0.5) : a);
            Color icon = (on || active) ? Alpha(Accent, a)
                : Alpha(Muted, muted ? (int)(a * 0.55) : (int)(a * 0.9));
            float iconSize = S(18);
            float iconX = r.X + S(12);
            float iconY = r.Y + (pillH - iconSize) / 2f;
            DrawIcon(g, IconOf(index), new RectangleF(iconX, iconY, iconSize, iconSize), icon);

            // 仪表皮肤：选中态在胶囊右端点一颗带辉光的 LED 小灯（设计里「分刻 + 指示灯」的味道）
            bool led = !skinDefault && skin.screws && (active || on);
            if (led) DrawLed(g, r, a, muted ? 0.6f : 1f);

            string label = index == (int)BubbleAction.Pin ? (pinned ? "已置顶" : "窗口置顶") : Labels[index];
            float textX = r.X + S(12) + iconSize + S(9);
            float maxW = r.Width - S(12) - (textX - r.X) - (led ? S(12) : 0);
            DrawTextMid(g, label, fPill, ink, textX, r.Y + pillH / 2f, maxW);

            // 长开后右边补一句「39 套」，让人知道下面还有一整份主题表
            if (active && index == (int)BubbleAction.Theme)
            {
                string cnt = swatches.Count + " 套";
                float cw = Measure(g, cnt, fChipSub);
                DrawTextMid(g, cnt, fChipSub, Alpha(Dim, a), r.Right - S(12) - cw, r.Y + pillH / 2f, cw + S(2));
            }
        }

        /// <summary>仪表皮肤的 LED 小灯：实心点 + 几圈很淡的辉光。</summary>
        private void DrawLed(Graphics g, Rectangle r, int a, float alphaMul)
        {
            float cx = r.Right - S(11);
            float cy = r.Y + pillH / 2f;
            float rr = Math.Max(1.6f, S(2.0f));
            for (int i = 3; i >= 1; i--)
            {
                float gr = rr * (1f + i * 1.5f);
                using (var br = new SolidBrush(Alpha(Accent, (int)(a * 0.10 * i * alphaMul))))
                    g.FillEllipse(br, cx - gr, cy - gr, gr * 2, gr * 2);
            }
            using (var br = new SolidBrush(Alpha(Accent, (int)(a * alphaMul))))
                g.FillEllipse(br, cx - rr, cy - rr, rr * 2, rr * 2);
        }

        private void DrawBody(Graphics g, int index, Rectangle r, int a, bool isActive)
        {
            BubbleAction act = (BubbleAction)index;
            int max = S(ExtraHDip(act));
            double p = max <= 0 ? 1 : Clamp01((isActive ? openExtra : closeExtra) / max);
            int alpha = isActive
                ? (int)Math.Round(a * Clamp01((p - 0.35) / 0.45))
                : (int)Math.Round(a * p);
            if (alpha <= 2) return;
            int x = r.X + S(12);
            int w = r.Width - S(24);

            if (act == BubbleAction.Schedule) DrawScheduleBody(g, r, x, w, alpha);
            else if (act == BubbleAction.Theme) DrawThemeBody(g, r, x, w, alpha);
            else if (act == BubbleAction.Clock) DrawClockBody(g, r, x, w, alpha);
            else if (act == BubbleAction.Preview) DrawPreviewBody(g, r, x, w, alpha);
            else DrawUpdateBody(g, r, x, w, alpha);
        }

        // ---------------- 预览通知 ----------------

        /// <summary>第 i 个通知类型 chip（2 列 × 3 行）。</summary>
        private Rectangle PreviewChipRect(Rectangle item, int i)
        {
            int gapX = S(8), gapY = S(8);
            int cw = (item.Width - S(24) - gapX) / 2;
            int ch = S(42);
            int col = i % 2, row = i / 2;
            return new Rectangle(item.X + S(12) + col * (cw + gapX), item.Y + S(62) + row * (ch + gapY), cw, ch);
        }

        private void DrawPreviewBody(Graphics g, Rectangle r, int x, int w, int a)
        {
            DrawText(g, "选一类通知，立刻在这张卡片上播一遍", fRowSub, Alpha(Dim, a), x, r.Y + S(48));
            for (int i = 0; i < PreviewKeys.Length; i++)
            {
                Rectangle chip = PreviewChipRect(r, i);
                using (var p = RoundPath(chip, ChipCorner))
                {
                    using (var b = new SolidBrush(Alpha(FieldBg, a))) g.FillPath(b, p);
                    using (var pen = new Pen(Alpha(FieldBorder, a), Math.Max(1f, scale))) g.DrawPath(pen, p);
                }
                DrawTextMid(g, PreviewNames[i], fRow, Alpha(Ink, a), chip.X + S(8), chip.Y + S(15), chip.Width - S(16));
                DrawTextMid(g, PreviewSubs[i], fChipSub, Alpha(Dim, a), chip.X + S(8), chip.Y + S(30), chip.Width - S(16));
                DrawIcon(g, PreviewIcon(PreviewKeys[i]),
                    new RectangleF(chip.Right - S(24), chip.Y + S(13), S(14), S(14)), Alpha(Accent, a));
            }
            DrawText(g, "预览只放一遍动画，不改日程、不弹托盘通知。", fRowSub, Alpha(Dim, a), x, r.Y + S(228));
        }

        /// <summary>预览类型的图标：会议类是日历，普通日程是铅笔，提醒类是铃铛。</summary>
        private static IconKind PreviewIcon(string key)
        {
            if (key == "lark" || key == "outlook") return IconKind.Calendar;
            if (key == "local") return IconKind.Pencil;
            return IconKind.Bell;
        }

        // ---------------- 检查更新 ----------------

        /// <summary>「更新记录」列表的可视区（相对整颗长开的卡片）。</summary>
        private Rectangle ChangelogView(Rectangle item)
        {
            return new Rectangle(item.X + S(12), item.Y + S(256), item.Width - S(24), S(UpdateLogViewHDip));
        }

        private int ChangelogMaxScroll(Rectangle view)
        {
            int total = UpdateChangelog.Entries.Count * S(UpdateLogRowHDip);
            return Math.Max(0, total - view.Height);
        }

        private Rectangle UpdatePrimaryRect(Rectangle item)
        {
            return new Rectangle(item.X + S(12), item.Y + S(150), item.Width - S(24), S(36));
        }

        private Rectangle UpdateLinkRect(Rectangle item)
        {
            return new Rectangle(item.X + S(12), item.Y + S(200), item.Width - S(24), S(26));
        }

        private void DrawUpdateBody(Graphics g, Rectangle r, int x, int w, int a)
        {
            // 第一行：当前版本 / 最新版本
            DrawText(g, "当前 v" + AppInfo.Version, fRow, Alpha(Ink, a), x, r.Y + S(52));
            string latest = string.IsNullOrEmpty(updateLatest) ? "" : "最新 v" + updateLatest;
            if (!string.IsNullOrEmpty(latest))
                DrawText(g, latest, fRowSub, Alpha(updatePhase == UpdatePhase.Available || updatePhase == UpdatePhase.Ready
                    ? Accent : Dim, a), x + w - Measure(g, latest, fRowSub), r.Y + S(52));

            // 状态行
            string state;
            Color stateColor = Dim;
            switch (updatePhase)
            {
                case UpdatePhase.Checking:
                    state = "正在检查 GitHub 上的最新版本…";
                    break;
                case UpdatePhase.UpToDate:
                    state = "已经是最新版本";
                    stateColor = theme.success;
                    break;
                case UpdatePhase.Available:
                    state = "有新版本，点下面「立即更新」全量替换";
                    stateColor = Accent;
                    break;
                case UpdatePhase.Downloading:
                    state = "正在下载 " + updatePercent + "%";
                    stateColor = Accent;
                    break;
                case UpdatePhase.Ready:
                    state = "下载完成，点「立即重启」完成更新";
                    stateColor = theme.success;
                    break;
                case UpdatePhase.Error:
                    state = string.IsNullOrEmpty(updateDetail) ? "检查失败" : updateDetail;
                    stateColor = Color.FromArgb(214, 120, 80);
                    break;
                default:
                    state = "点「检查更新」看看有没有新版本";
                    break;
            }
            DrawText(g, Fit(g, state, fRowSub, w), fRowSub, Alpha(stateColor, a), x, r.Y + S(74));

            // 说明 / 进度条
            if (updatePhase == UpdatePhase.Downloading)
            {
                var bar = new Rectangle(x, r.Y + S(100), w, S(8));
                using (var p = RoundPath(bar, S(4)))
                using (var b = new SolidBrush(Alpha(FieldBg, a)))
                    g.FillPath(b, p);
                int fw = (int)Math.Round(bar.Width * (Math.Max(0, Math.Min(100, updatePercent)) / 100.0));
                if (fw > S(4))
                {
                    var fill = new Rectangle(bar.X, bar.Y, fw, bar.Height);
                    using (var p = RoundPath(fill, S(4)))
                    using (var b = new SolidBrush(Alpha(Accent, a)))
                        g.FillPath(b, p);
                }
                DrawText(g, string.IsNullOrEmpty(updateNotes) ? "" : Fit(g, updateNotes, fChipSub, w),
                    fChipSub, Alpha(Dim, a), x, r.Y + S(120));
            }
            else
            {
                string notes = UpdateService.ShortNotes(updateNotes, 46);
                if (!string.IsNullOrEmpty(notes))
                    DrawText(g, Fit(g, notes, fChipSub, w), fChipSub, Alpha(Dim, a), x, r.Y + S(100));
                DrawText(g, "更新方式：下载新版 exe 全量替换，配置和对话都保留。", fChipSub, Alpha(Dim, a), x, r.Y + S(120));
            }

            // 主按钮
            Rectangle btn = UpdatePrimaryRect(r);
            bool busy = updatePhase == UpdatePhase.Checking || updatePhase == UpdatePhase.Downloading;
            bool apply = (updatePhase == UpdatePhase.Available || updatePhase == UpdatePhase.Ready) && updateHasAsset;
            if (busy)
            {
                using (var p = RoundPath(btn, CardCorner))
                using (var b = new SolidBrush(Alpha(FieldBg, a))) g.FillPath(b, p);
                DrawTextMid(g, updatePhase == UpdatePhase.Checking ? "正在检查…" : "正在下载…",
                    fChip, Alpha(Dim, a), btn.X, btn.Y + btn.Height / 2f, btn.Width);
            }
            else
            {
                using (var p = RoundPath(btn, CardCorner))
                using (var b = new SolidBrush(Alpha(apply ? Accent : Mix(Accent, SurfaceBottom, 0.55), a)))
                    g.FillPath(b, p);
                string label = apply
                    ? (updatePhase == UpdatePhase.Ready ? "立即重启并更新" : "立即更新（全量）")
                    : (updatePhase == UpdatePhase.Available || updatePhase == UpdatePhase.Ready) ? "打开发布页下载"
                    : updatePhase == UpdatePhase.UpToDate ? "再检查一次" : "检查更新";
                DrawTextMid(g, label, fChip, Alpha(apply ? OnAccent : Ink, a), btn.X, btn.Y + btn.Height / 2f, btn.Width);
            }
            DrawText(g, "打开发布页 →", fLink, Alpha(Accent, a), x, r.Y + S(213));

            // 更新记录：按大版本从新到旧，一屏 5 行，滚轮看更早；最新一条用强调色
            DrawText(g, "更新记录", fRowSub, Alpha(Dim, a), x, r.Y + S(238));
            Rectangle logView = ChangelogView(r);
            GraphicsState logState = g.Save();
            g.SetClip(logView, CombineMode.Intersect);
            for (int i = 0; i < UpdateChangelog.Entries.Count; i++)
            {
                ChangelogEntry entry = UpdateChangelog.Entries[i];
                int rowTop = logView.Y + i * S(UpdateLogRowHDip) - changelogScroll;
                bool isLatest = i == 0;
                if (i < UpdateChangelog.Entries.Count - 1)
                {
                    using (var pen = new Pen(Alpha(theme.edge, (int)(a * 0.30)), Math.Max(1f, scale * 0.6f)))
                        g.DrawLine(pen, logView.X, rowTop + S(UpdateLogRowHDip) - 1,
                            logView.Right, rowTop + S(UpdateLogRowHDip) - 1);
                }
                float cy = rowTop + S(UpdateLogRowHDip) / 2f;
                DrawTextMid(g, "v" + entry.Version, fChip, Alpha(isLatest ? Accent : Muted, a),
                    logView.X, cy, S(48));
                DrawTextMid(g, entry.Date, fRowSub, Alpha(Dim, a), logView.X + S(50), cy, S(62));
                DrawTextMid(g, entry.Title, fRowSub, Alpha(isLatest ? Accent : Ink, a),
                    logView.X + S(114), cy, logView.Width - S(114));
            }
            g.Restore(logState);
        }

        /// <summary>在浏览器里打开发布页（兜底：拿不到自动下载时用）。</summary>
        private static void OpenReleasesPage()
        {
            try { System.Diagnostics.Process.Start(UpdateService.LatestPageUrl); }
            catch { }
        }

        /// <summary>Widget 的后台线程把更新状态推进来（UI 线程调用）。</summary>
        public void SetUpdateState(UpdatePhase phase, string latestVersion, string detail, string notes, int percent, bool hasAsset)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { SetUpdateState(phase, latestVersion, detail, notes, percent, hasAsset); }); }
                catch { }
                return;
            }
            updatePhase = phase;
            updateLatest = latestVersion ?? "";
            updateDetail = detail ?? "";
            updateNotes = notes ?? "";
            updatePercent = percent;
            updateHasAsset = hasAsset;
            BuildHits();
            Invalidate();
        }

        /// <summary>当前更新状态（Widget 判断要不要重复发起检查）。</summary>
        public UpdatePhase CurrentUpdatePhase { get { return updatePhase; } }

        private void DrawThemeBody(Graphics g, Rectangle r, int x, int w, int a)
        {
            Rectangle view = ThemeView(r);
            GraphicsState state = g.Save();
            g.SetClip(view, CombineMode.Intersect);
            for (int i = 0; i < swatches.Count; i++)
            {
                Rectangle chip = ThemeChip(view, i);
                if (chip.Bottom <= view.Top || chip.Top >= view.Bottom) continue;
                DrawThemeCard(g, chip, swatches[i], a);
            }
            g.Restore(state);

            DrawThemeScrollHint(g, view, a);
            DrawNewThemeRow(g, r, a);
        }

        /// <summary>一张主题色卡：主题底色 + 强调色方块 + 竖排名字（比例照参考图 1.70:1）。</summary>
        private void DrawThemeCard(Graphics g, Rectangle chip, ThemeDef t, int a)
        {
            bool sel = t.id == selectedTheme;
            int tileRadius = skinDefault ? S(8) : S(skin.radiusCard);
            using (var path = RoundPath(chip, tileRadius))
            {
                // 霓虹：选中的色卡先铺一圈辉光（设计里 .tile.on 的 0 0 14px 光）
                if (!skinDefault && skin.neonEdge && sel) PaintThemeGlow(g, path, chip, a);
                using (var br = new LinearGradientBrush(
                    new Rectangle(chip.X, chip.Y, Math.Max(1, chip.Width), Math.Max(1, chip.Height)),
                    Alpha(t.bgTop, a), Alpha(t.bgBottom, a), 90f))
                    g.FillPath(br, path);

                int pad = Math.Max(2, S(2.5f));
                int side = Math.Max(4, chip.Height - pad * 2);
                var block = new Rectangle(chip.X + pad, chip.Y + pad, side, side);
                using (var bp = RoundPath(block, S(5)))
                using (var bb = new SolidBrush(Alpha(t.accent, a)))
                    g.FillPath(bb, bp);

                int textX = block.Right + S(1);
                DrawThemeName(g, t, new Rectangle(textX, chip.Y, Math.Max(1, chip.Right - S(1) - textX), chip.Height), a);

                int edgeA = sel ? a : (int)Math.Round(a * 0.5);
                Color edgeColor = sel ? Accent : (skinDefault ? theme.edge : skin.panelEdge);
                using (var pen = new Pen(Alpha(edgeColor, edgeA), Math.Max(1f, scale * (sel ? 1.7f : 1f))))
                    g.DrawPath(pen, path);
            }
        }

        /// <summary>霓虹色卡的选中辉光（画在底色之前，只留外侧）。</summary>
        private void PaintThemeGlow(Graphics g, GraphicsPath path, Rectangle chip, int a)
        {
            for (int i = 3; i >= 1; i--)
            {
                float wid = Math.Max(1.5f, S(1f + i * 1.6f));
                using (var pen = new Pen(Alpha(Accent, (int)(a * 0.13 * i)), wid))
                    g.DrawPath(pen, path);
            }
        }

        /// <summary>竖排名字：最多 3 个字，在色块右边上下居中。行距按字身（em）算，用 cell 高的话 3 个字会顶出卡片。</summary>
        private void DrawThemeName(Graphics g, ThemeDef t, Rectangle box, int a)
        {
            string name = ShortThemeName(t);
            int n = Math.Min(3, name.Length);
            if (n <= 0) return;
            float em = fChipSub.Size * g.DpiY / 72f;
            float lineH = em * 1.12f;
            float top = box.Y + (box.Height - lineH * n) / 2f;
            Color ink = Alpha(t.textMain, a);
            using (var brush = new SolidBrush(ink))
            {
                for (int i = 0; i < n; i++)
                {
                    string ch = name.Substring(i, 1);
                    float cw = Measure(g, ch, fChipSub);
                    float baseline = top + i * lineH + lineH / 2f + em * 0.34f;
                    g.DrawString(ch, fChipSub, brush, box.X + (box.Width - cw) / 2f,
                        baseline - Ascent(fChipSub, g), StringFormat.GenericTypographic);
                }
            }
        }

        /// <summary>列表右边那根 2px 的滚动条，只在装不下时才画。</summary>
        private void DrawThemeScrollHint(Graphics g, Rectangle view, int a)
        {
            int max = ThemeMaxScroll(view);
            if (max <= 0) return;
            int h = Math.Max(S(18), view.Height * view.Height / (view.Height + max));
            int y = view.Top + (int)Math.Round((view.Height - h) * (themeScroll / (double)max));
            using (var b = new SolidBrush(Alpha(theme.textMuted, (int)Math.Round(a * 0.5))))
                g.FillRectangle(b, view.Right + S(3), y, Math.Max(2, S(2)), h);
        }

        /// <summary>色卡列表下面的「＋ 新建主题」。</summary>
        private void DrawNewThemeRow(Graphics g, Rectangle r, int a)
        {
            Rectangle btn = NewThemeRect(r);
            using (var path = RoundPath(btn, CardCorner))
            {
                using (var b = new SolidBrush(Alpha(Mix(Accent, SurfaceBottom, 0.82), a)))
                    g.FillPath(b, path);
                using (var pen = new Pen(Alpha(Accent, (int)Math.Round(a * 0.32)), Math.Max(1f, scale)))
                    g.DrawPath(pen, path);
            }
            DrawTextMid(g, "＋", fRow, Alpha(Accent, a), btn.X + S(10), btn.Y + btn.Height / 2f, S(14));
            DrawTextMid(g, "新建主题", fRow, Alpha(Accent, a), btn.X + S(27), btn.Y + btn.Height / 2f, btn.Width - S(95));
            string tip = "从当前配色开始";
            float tw = Measure(g, tip, fChipSub);
            DrawTextMid(g, tip, fChipSub, Alpha(Dim, a), btn.Right - S(10) - tw, btn.Y + btn.Height / 2f, tw + S(4));
        }

        private void DrawClockBody(Graphics g, Rectangle r, int x, int w, int a)
        {
            int top = r.Y + S(50);
            for (int i = 0; i < 2; i++)
            {
                var row = new Rectangle(x, top + i * S(48), w, S(40));
                using (var p = RoundPath(row, RowCorner))
                {
                    using (var b = new SolidBrush(Alpha(FieldBg, a))) g.FillPath(b, p);
                    using (var pen = new Pen(Alpha(FieldBorder, a), Math.Max(1f, scale))) g.DrawPath(pen, p);
                }
                DrawIcon(g, IconKind.Clock, new RectangleF(row.X + S(11), row.Y + S(11), S(18), S(18)), Alpha(Accent, a));
                DrawTextMid(g, i == 0 ? "上班打卡" : "下班打卡", fRow, Alpha(Ink, a), row.X + S(38),
                    row.Y + row.Height / 2f, w - S(80));

                var kbd = new Rectangle(row.Right - S(31), row.Y + S(10), S(20), S(20));
                using (var p = RoundPath(kbd, S(6)))
                using (var b = new SolidBrush(Alpha(FieldBg, a))) g.FillPath(b, p);
                DrawTextMid(g, i == 0 ? "1" : "2", fKbd, Alpha(Muted, a), kbd.X, kbd.Y + kbd.Height / 2f, kbd.Width);
            }
            DrawText(g, "没配飞书会话时会提示先去设置里填。", fRowSub, Alpha(Dim, a), x, r.Y + S(156));
        }

        private void DrawScheduleBody(Graphics g, Rectangle r, int x, int w, int a)
        {
            DrawField(g, new Rectangle(x, r.Y + S(48), w, S(32)), IconKind.Pencil, eventTitle,
                string.IsNullOrEmpty(eventTitle) ? "标题" : "", a, false);
            DrawField(g, new Rectangle(x, r.Y + S(88), w, S(32)), IconKind.Pin, eventPlace,
                string.IsNullOrEmpty(eventPlace) ? "添加地点" : "", a, true);

            DrawText(g, "从什么时候开始", fFieldLabel, Alpha(Dim, a), x, r.Y + S(136));

            int top = r.Y + S(142);
            int gapX = S(7);
            int chipW = (w - gapX * 2) / 3;
            string[] caps = new string[] { "+15 分钟", "+30 分钟", "+1 小时" };
            int[] mins = new int[] { 15, 30, 60 };
            DateTime now = DateTime.Now;
            for (int i = 0; i < 3; i++)
            {
                var chip = new Rectangle(x + i * (chipW + gapX), top, chipW, S(44));
                using (var p = RoundPath(chip, ChipCorner))
                {
                    using (var b = new SolidBrush(Alpha(FieldBg, a))) g.FillPath(b, p);
                    using (var pen = new Pen(Alpha(FieldBorder, a), Math.Max(1f, scale))) g.DrawPath(pen, p);
                }
                DrawTextMid(g, caps[i], fChip, Alpha(Ink, a), chip.X + S(4), chip.Y + S(16), chip.Width - S(8));
                DrawTextMid(g, now.AddMinutes(mins[i]).ToString("HH:mm"), fChipSub, Alpha(Dim, a),
                    chip.X + S(4), chip.Y + S(32), chip.Width - S(8));
            }

            var save = new Rectangle(x, r.Y + S(196), w, S(34));
            using (var p = RoundPath(save, CardCorner))
            using (var b = new SolidBrush(Alpha(Accent, a))) g.FillPath(b, p);
            DrawTextMid(g, "保存日程", fChip, Alpha(OnAccent, a), save.X, save.Y + save.Height / 2f, save.Width);

            string more = "详细编辑…";
            DrawText(g, more, fLink, Alpha(Accent, a), x, r.Y + S(254));
            DrawIcon(g, IconKind.Arrow,
                new RectangleF(x + Measure(g, more, fLink) + S(6), r.Y + S(242), S(13), S(13)), Alpha(Accent, a));
        }

        private void DrawField(Graphics g, Rectangle r, IconKind icon, string value, string placeholder, int a, bool dim)
        {
            using (var p = RoundPath(r, FieldCorner))
            {
                using (var b = new SolidBrush(Alpha(FieldBg, a))) g.FillPath(b, p);
                using (var pen = new Pen(Alpha(FieldBorder, a), Math.Max(1f, scale))) g.DrawPath(pen, p);
            }
            DrawIcon(g, icon, new RectangleF(r.X + S(9), r.Y + (r.Height - S(14)) / 2f, S(14), S(14)), Alpha(Accent, a));
            bool empty = string.IsNullOrEmpty(value);
            string text = empty ? placeholder : value;
            DrawTextMid(g, text, fField, Alpha(empty ? Dim : Ink, a), r.X + S(31), r.Y + r.Height / 2f, r.Width - S(40));
        }

        // ---------------- 图标 ----------------

        private static IconKind IconOf(int index)
        {
            if (index == (int)BubbleAction.Schedule) return IconKind.Calendar;
            if (index == (int)BubbleAction.Theme) return IconKind.Palette;
            if (index == (int)BubbleAction.Clock) return IconKind.Clock;
            if (index == (int)BubbleAction.Preview) return IconKind.Bell;
            if (index == (int)BubbleAction.Update) return IconKind.Refresh;
            return IconKind.Pin;
        }

        private void DrawIcon(Graphics g, IconKind kind, RectangleF box, Color color)
        {
            float k = box.Width / 24f;
            var state = g.Save();
            g.TranslateTransform(box.X, box.Y);
            g.ScaleTransform(k, k);
            using (var pen = new Pen(color, Math.Max(1f, 1.7f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case IconKind.Calendar:
                        using (var p = RoundPath(new Rectangle(3, 5, 18, 16), 4)) g.DrawPath(pen, p);
                        g.DrawLine(pen, 8, 3, 8, 7);
                        g.DrawLine(pen, 16, 3, 16, 7);
                        g.DrawLine(pen, 3.5f, 9.5f, 20.5f, 9.5f);
                        g.DrawLine(pen, 12, 13.4f, 12, 18f);
                        g.DrawLine(pen, 9.7f, 15.7f, 14.3f, 15.7f);
                        break;
                    case IconKind.Palette:
                        g.DrawEllipse(pen, 3.4f, 3.4f, 17.2f, 17.2f);
                        using (var b = new SolidBrush(color))
                        {
                            g.FillEllipse(b, 7f, 9.1f, 2.3f, 2.3f);
                            g.FillEllipse(b, 10.9f, 6.6f, 2.3f, 2.3f);
                            g.FillEllipse(b, 14.8f, 9.1f, 2.3f, 2.3f);
                        }
                        break;
                    case IconKind.Clock:
                        g.DrawEllipse(pen, 3.2f, 4.6f, 14.8f, 14.8f);
                        g.DrawLine(pen, 10.6f, 8f, 10.6f, 12.2f);
                        g.DrawLine(pen, 10.6f, 12.2f, 13.3f, 13.8f);
                        g.DrawLine(pen, 18.4f, 13.6f, 21.6f, 13.6f);
                        g.DrawLine(pen, 19.6f, 11.2f, 22f, 13.6f);
                        g.DrawLine(pen, 19.6f, 16f, 22f, 13.6f);
                        break;
                    case IconKind.Pin:
                        g.DrawLine(pen, 4.2f, 5.2f, 19.8f, 5.2f);
                        g.DrawLine(pen, 12f, 19.2f, 12f, 9.4f);
                        g.DrawLine(pen, 7.6f, 13.8f, 12f, 9.4f);
                        g.DrawLine(pen, 16.4f, 13.8f, 12f, 9.4f);
                        break;
                    case IconKind.Pencil:
                        g.DrawLine(pen, 4f, 20f, 8f, 20f);
                        g.DrawLine(pen, 8f, 20f, 18.2f, 9.8f);
                        g.DrawLine(pen, 15.2f, 6.8f, 18.2f, 9.8f);
                        g.DrawLine(pen, 4f, 20f, 5f, 16.8f);
                        g.DrawLine(pen, 5f, 16.8f, 15.2f, 6.8f);
                        break;
                    case IconKind.Arrow:
                        g.DrawLine(pen, 4f, 12f, 19f, 12f);
                        g.DrawLine(pen, 13.5f, 6.8f, 18.7f, 12f);
                        g.DrawLine(pen, 13.5f, 17.2f, 18.7f, 12f);
                        break;
                    case IconKind.Bell:
                        // 铃铛：圆顶 + 两条竖边 + 底座 + 底下的小锤
                        g.DrawArc(pen, 6.2f, 4.2f, 11.6f, 11.6f, 180, 180);
                        g.DrawLine(pen, 6.2f, 10f, 6.2f, 16.4f);
                        g.DrawLine(pen, 17.8f, 10f, 17.8f, 16.4f);
                        g.DrawLine(pen, 4.2f, 16.4f, 19.8f, 16.4f);
                        g.DrawLine(pen, 12f, 3.2f, 12f, 4.4f);
                        g.DrawLine(pen, 12f, 16.4f, 12f, 19.4f);
                        g.DrawLine(pen, 10.8f, 20.6f, 13.2f, 20.6f);
                        break;
                    case IconKind.Refresh:
                        // 循环箭头：一段弧 + 两个箭头
                        g.DrawArc(pen, 3.8f, 4.4f, 16.4f, 16.4f, 40, 285);
                        g.DrawLine(pen, 17.4f, 3.4f, 20.4f, 6.0f);
                        g.DrawLine(pen, 20.4f, 6.0f, 17.2f, 7.6f);
                        break;
                }
            }
            g.Restore(state);
        }

        // ---------------- 文字 ----------------

        private static float Ascent(Font f, Graphics g)
        {
            FontFamily ff = f.FontFamily;
            return ff.GetCellAscent(f.Style) * f.Size * g.DpiY / 72f / ff.GetEmHeight(f.Style);
        }

        private static float Descent(Font f, Graphics g)
        {
            FontFamily ff = f.FontFamily;
            return ff.GetCellDescent(f.Style) * f.Size * g.DpiY / 72f / ff.GetEmHeight(f.Style);
        }

        private static float Measure(Graphics g, string s, Font f)
        {
            return g.MeasureString(s, f, 100000, StringFormat.GenericTypographic).Width;
        }

        private static string Fit(Graphics g, string s, Font f, float maxW)
        {
            if (maxW <= 1) return "";
            if (Measure(g, s, f) <= maxW) return s;
            for (int len = s.Length - 1; len >= 1; len--)
            {
                string cut = s.Substring(0, len) + "…";
                if (Measure(g, cut, f) <= maxW) return cut;
            }
            return "…";
        }

        private static void DrawText(Graphics g, string text, Font f, Color color, float x, float baseline)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var b = new SolidBrush(color))
                g.DrawString(text, f, b, x, baseline - Ascent(f, g), StringFormat.GenericTypographic);
        }

        private static void DrawTextMid(Graphics g, string text, Font f, Color color, float x, float centerY, float maxW)
        {
            if (string.IsNullOrEmpty(text)) return;
            string fit = Fit(g, text, f, maxW);
            float asc = Ascent(f, g), desc = Descent(f, g);
            float baseline = centerY + (asc - desc) / 2f;
            using (var b = new SolidBrush(color))
                g.DrawString(fit, f, b, x, baseline - asc, StringFormat.GenericTypographic);
        }

        // ---------------- 分层窗推送 ----------------

        private void Push()
        {
            if (frame == null || !IsHandleCreated) return;
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = frame.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                oldBitmap = SelectObject(memDc, hBitmap);
                var size = new NativeSize();
                size.cx = frame.Width;
                size.cy = frame.Height;
                var src = new NativePoint();
                var dst = new NativePoint();
                dst.X = Left;
                dst.Y = Top;
                var blend = new BlendFunction();
                blend.BlendOp = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = AcSrcAlpha;
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, UlwAlpha);
            }
            catch { }
            finally
            {
                if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            anim.Stop();
            foreach (var f in new Font[] { fPill, fTitle, fSub, fChip, fChipSub, fRow, fRowSub, fLink, fField, fFieldLabel, fKbd })
                if (f != null) f.Dispose();
            if (frame != null) { frame.Dispose(); frame = null; }
            base.OnFormClosed(e);
        }

        private static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>标题 / 地点的小编辑器：贴在字段上，Enter 提交、Esc 取消。</summary>
        private class FieldEditor : Form
        {
            private readonly TextBox box;
            public bool Cancelled;
            public string Value { get { return box.Text.Trim(); } }

            public FieldEditor(string label, string value, Rectangle screen, Color bg, Color fg,
                Color accent, Color border, float scale, bool topMost)
            {
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = topMost;
                BackColor = border;
                Padding = new Padding(1);
                Bounds = screen;

                var panel = new Panel();
                panel.Dock = DockStyle.Fill;
                panel.BackColor = bg;
                panel.Padding = new Padding((int)(7 * scale), (int)(4 * scale), (int)(7 * scale), 0);
                Controls.Add(panel);

                box = new TextBox();
                box.BorderStyle = BorderStyle.None;
                box.BackColor = bg;
                box.ForeColor = fg;
                box.Font = UiFont.Make(9f, FontStyle.Regular);
                box.Text = string.IsNullOrEmpty(value) ? "" : value;
                box.Dock = DockStyle.Top;
                box.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Close(); }
                    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Cancelled = true; Close(); }
                };
                panel.Controls.Add(box);

                Deactivate += delegate
                {
                    if (!IsDisposed && Visible) Close();
                };
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                box.Focus();
                box.SelectAll();
            }
        }
    }
}
