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
    /// <summary>
    /// 小纸条面板（分层窗）：A 抽屉式 / C 极简双栏两种布局，设置里切换。
    /// 状态：对话列表 → 聊天（最后两条 + 输入框）→ 历史（8 条）→ 新建对话。
    /// 对话：5 位数字 = 和某个人的 1:1；任意其它 5 位字符 = 对话码（同码的人进同一个对话）。
    /// 所有动作带过渡：从卡片侧拉开 / 高度长高 / 气泡上浮 / 历史逐行 / 闪光由 Widget 画在卡片上。
    /// </summary>
    public class NotePanelForm : Form
    {
        public const int DrawerWDip = 392;
        public const int DrawerHDip = 282;
        public const int DrawerContactsHDip = 124;   // 抽屉式「还没选对话」时只留一条：标题 + 对话 + 一句提示，别空一大片
        public const int DrawerHistoryHDip = 476;
        public const int ColumnsWDip = 470;
        public const int ColumnsHDip = 298;
        public const int ColumnsHistoryHDip = 512;
        public const int RailWDip = 150;
        public const int PadDip = 16;
        public const int CloseMs = 180;
        /// <summary>整卡外描边宽度（设计 DIP）。原来是 1 DIP，用户说「稍微粗一点」→ 1.8 DIP。</summary>
        public const float CardBorderDip = 1.8f;

        private const int WmMouseWheel = 0x020A;
        private const int DrawerVisibleContacts = 4;    // 抽屉式一排正好 4 个胶囊
        private const int DrawerRowStepDip = 34;        // 抽屉式第二排的纵向步进（胶囊 30 + 间距 4）
        private const int ContactRowStrideDip = 40;     // 双栏竖排一行：胶囊 34 + 间距 6

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

        private enum PanelState { Contacts, Chat, History, Add }
        private enum HitKind { None, Contact, Input, AddContact, Close, IdField, RemarkField, Probe, ConfirmAdd, Send, Delete, Nickname }

        private class Hit
        {
            public Rectangle rect;
            public HitKind kind;
            public string key;
            public Hit(Rectangle r, HitKind k, string key) { rect = r; kind = k; this.key = key; }
        }

        private readonly Settings settings;
        private readonly NoteSettings note;
        private readonly SkinDef skin;           // 整体界面主题（皮肤）：颜色 / 圆角 / 质感都从这取
        private readonly bool skinDefault;       // 默认皮肤：外观必须和 v3.6 完全一致
        private readonly bool glass;             // 磨砂玻璃底座（默认皮肤的「背景风格」或云母皮肤）
        private readonly ThemeDef theme;
        private readonly Rectangle card;
        private readonly bool topMost;
        private readonly bool columns;
        private readonly float scale;
        private readonly float u;                 // 设计像素 → 实际像素（设计稿是 96 DPI 的 DIP，1 设计 px = 1 DIP）
        private readonly bool toRight;
        private readonly int winWidth;
        private readonly int winLeft;
        private readonly int baseTop;
        private readonly Rectangle workArea;
        private readonly Func<string, string, bool> onSend;
        private readonly Action<string> onRead;
        private readonly Action<string, Action<bool>> onProbe;
        private readonly Func<string, string, bool> onAddContact;
        private readonly Action onOpenSettings;
        private readonly Action<string, string> onRenameContact;   // 双击对话名字改备注（可空）
        private readonly Action<string> onDeleteContact;           // 右键删除对话（可空；为空时面板自己删）
        private readonly Action<string> onSetNickname;             // 卡片页改本机昵称（可空；为空时面板自己存）

        private PanelState state = PanelState.Contacts;
        private string selectedId;
        private int hoverContact = -1;
        private bool closing;
        private int openedAt;
        private int stateAt;
        private int closeAt;
        private float heightNow;
        private float heightFrom;
        private float heightTo;
        private int heightAnimAt;
        private bool heightAnimating;
        private bool editingField;
        private NoteField fieldEditor;            // 贴在输入框上的小编辑窗（非模态）
        private Action pendingEdit;               // 面板长高动画结束后再开编辑窗，避免编辑窗停在旧位置
        private string addId = "";
        private string addRemark = "";
        private bool? addOnline;
        private bool addProbing;
        private string composeText = "";
        private string status = "";
        private int statusAt;
        private int sentAt;                       // 刚发出去的时间：让新消息「上移 / 进入」只动那两行，不整块重画
        private int sentPrevCount;                // 发之前会话里有几条（决定旧消息要不要从底部滑到上面）
        private string lastClickContact;          // 双击对话名字改备注：记上一次点的是谁
        private int lastClickAt;
        private int lastNickClickAt;              // 双击「昵称」胶囊：记上一次点的时间
        private bool lastMouseInside;             // 拿不到真实光标时（锁屏 / 无输入桌面）判断点击落点用
        private int contactScroll;                // 对话列表滚动窗口起点（抽屉横排 / 双栏竖排共用）
        private int historyScroll;                // 历史页往上翻了几条（0 = 最新一屏）
        private int historySettleMs = 420;        // 这次重画动画至少跑多久（历史页 760ms，其它 420ms）
        private string deleteId = "";             // 右键选中的对话：该对话上显示「删除」按钮
        private NoteWheelFilter wheelFilter;      // 面板开着时收滚轮（别的窗口拿到焦点也能滚）

        private readonly List<Hit> hits = new List<Hit>();
        private readonly List<NoteContact> drawnContacts = new List<NoteContact>();
        private readonly Font fTitle, fHeading, fSub, fContact, fContactSub, fMsg, fMsgSub, fTiny, fField, fButton, fGlyph, fName;
        private Bitmap frame;
        private readonly Timer anim;
        private int animTick;

        public NotePanelForm(Settings settings, ThemeDef theme, Rectangle card, bool topMost,
            Func<string, string, bool> onSend, Action<string> onRead,
            Action<string, Action<bool>> onProbe, Func<string, string, bool> onAddContact,
            Action onOpenSettings, Action<string, string> onRenameContact = null,
            Action<string> onDeleteContact = null, Action<string> onSetNickname = null)
        {
            this.settings = settings;
            this.note = settings.note;
            this.skin = Skins.Current(settings.ui);
            this.skinDefault = skin.IsDefault;
            this.glass = Skins.UsesGlass(settings.ui);
            // 默认皮肤继续用调用方传进来的那套主题；其它皮肤用自带配色（Widget 现在传的已经是 ResolveTheme，
            // 这里再兜一次，旧调用点也不会把皮肤配色盖掉）。
            this.theme = skin.colors != null ? skin.colors
                : (theme != null ? theme : Skins.ResolveTheme(settings.ui));
            this.card = card;
            this.topMost = topMost;
            this.onSend = onSend;
            this.onRead = onRead;
            this.onProbe = onProbe;
            this.onAddContact = onAddContact;
            this.onOpenSettings = onOpenSettings;
            this.onRenameContact = onRenameContact;
            this.onDeleteContact = onDeleteContact;
            this.onSetNickname = onSetNickname;
            this.columns = string.Equals(note.style, "columns", StringComparison.OrdinalIgnoreCase);

            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            if (scale < 0.75f) scale = 0.75f;
            if (scale > 3f) scale = 3f;
            // 设计稿（design/note/note.html）的 392×282 / 470×298 是 96 DPI 的 DIP：面板尺寸和字号
            // 都按 DPI 缩放，和卡片（280 DIP 宽）用同一坐标系。不能在按卡片宽度等比缩一次——
            // 之前 u = card.Width / 420f 把设计稿当成「卡片 420 宽」再缩到 280，字号只剩 6.25pt，150% 下看不清。
            u = scale;

            winWidth = S(columns ? ColumnsWDip : DrawerWDip);
            int panelH = S(columns ? ColumnsHDip : (state == PanelState.Contacts ? DrawerContactsPanelHDip : DrawerHDip));
            Screen scr = Screen.FromRectangle(card);
            Rectangle wa = scr.WorkingArea;
            workArea = wa;
            int roomRight = wa.Right - card.Right;
            int roomLeft = card.Left - wa.Left;
            bool fitsRight = roomRight >= winWidth + S(14);
            bool fitsLeft = roomLeft >= winWidth + S(14);
            // 优先放右边；右边放不下再看左边；两边都放不下（面板变宽后小屏上会出现）就选空间大的一侧，
            // 再由下面的 clamp 拉回屏内，至少不会整块跑到屏幕外。
            bool right = fitsRight || (!fitsLeft && roomRight >= roomLeft);
            toRight = right;
            int left = right ? card.Right + S(14) : card.Left - S(14) - winWidth;
            left = Math.Max(wa.Left + 2, Math.Min(left, wa.Right - winWidth - 2));
            int top = card.Top;
            top = Math.Max(wa.Top + 2, Math.Min(top, wa.Bottom - panelH - 2));
            winLeft = left;
            baseTop = top;
            heightNow = panelH;
            heightFrom = heightTo = panelH;
            Bounds = new Rectangle(winLeft, baseTop, winWidth, (int)heightNow);
            BuildHits();

            fTitle = F(14f, FontStyle.Bold);
            // 皮肤标题字体：宣纸宋体、仪表 Bahnschrift；默认皮肤 / 没配字体的皮肤跟界面中文字体走
            fHeading = !skinDefault && !string.IsNullOrWhiteSpace(skin.headingFont)
                ? UiFont.MakeFamily(skin.headingFont, Pt(14f), FontStyle.Bold)
                : F(14f, FontStyle.Bold);
            fSub = F(10.5f, FontStyle.Regular);
            fContact = F(11.5f, FontStyle.Regular);
            fName = F(12.5f, FontStyle.Regular);      // 联系人名字：比胶囊里的辅助字大一号、居中画
            fContactSub = F(10f, FontStyle.Regular);
            fMsg = F(12.5f, FontStyle.Regular);
            fMsgSub = F(9.5f, FontStyle.Regular);
            fTiny = F(10f, FontStyle.Regular);
            fField = F(12.5f, FontStyle.Regular);
            fButton = F(12.5f, FontStyle.Bold);
            fGlyph = FN(15f, FontStyle.Bold);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = false;
            TopMost = topMost;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            anim = new Timer();
            anim.Interval = 15;
            anim.Tick += delegate { StepAnim(); };
        }

        public string SelectedId { get { return selectedId; } }
        public bool IsClosing { get { return closing; } }
        /// <summary>右键选中的、正在显示「删除」按钮的对话（空 = 没有）。</summary>
        public string DeleteId { get { return deleteId; } }
        /// <summary>对话列表滚动窗口的起点（0 = 最前面）。</summary>
        public int ContactScroll { get { return contactScroll; } }
        public int HistoryScroll { get { return historyScroll; } }


        /// <summary>当前能同时显示多少个对话（抽屉 4 / 8 个，双栏按高度算）。</summary>
        public int VisibleContactCount { get { return columns ? ColumnsVisibleCount() : DrawerVisibleContacts * DrawerRows; } }

        /// <summary>
        /// 抽屉式联系人显示几排：只在「对话列表页」（还没选对话）且联系人多于一排时铺两排，
        /// 一屏能看 8 个；进了聊天 / 历史页还是只留一排，别挤掉聊天区。
        /// </summary>
        private int DrawerRows
        {
            get
            {
                if (columns) return 1;
                if (state != PanelState.Contacts) return 1;
                return note.contacts.Count > DrawerVisibleContacts ? 2 : 1;
            }
        }

        /// <summary>抽屉式对话列表「条带」高度（DIP）：一排 36，两排 36 + 34。</summary>
        private int DrawerContactsStripHDip { get { return 36 + (DrawerRows - 1) * DrawerRowStepDip; } }

        /// <summary>抽屉式未选对话时整块面板的高度（DIP）：一排 124（标题 + 对话 + 一句提示），两排 +34。</summary>
        private int DrawerContactsPanelHDip { get { return DrawerContactsHDip + (DrawerRows - 1) * DrawerRowStepDip; } }

        /// <summary>设计像素 → 实际像素：按卡片宽度等比，和卡片字号比例一致。</summary>
        private int S(float px) { return (int)Math.Round(px * u); }

        private float Pt(float px) { return Math.Max(4f, px * u * 72f / (96f * scale)); }

        private Font F(float px, FontStyle st) { return UiFont.Make(Pt(px), st); }

        private Font FN(float px, FontStyle st) { return UiFont.MakeNumber(Pt(px), st); }

        // ---------------------------------------------------------------- 颜色

        private static double Luminance(Color c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; }
        private static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }
        private static Color Alpha(Color c, int a)
        {
            if (a < 0) a = 0; if (a > 255) a = 255;
            return Color.FromArgb(a * c.A / 255, c.R, c.G, c.B);
        }
        private bool Light { get { return theme != null && Luminance(theme.bgTop) > 0.56; } }
        private Color Ink { get { return theme.textMain; } }
        private Color Muted { get { return Mix(theme.textMain, theme.bgBottom, 0.18f); } }
        private Color Dim { get { return Mix(theme.textMain, theme.bgBottom, 0.34f); } }
        private Color Accent { get { return theme.accent; } }
        private Color SurfaceTop { get { return Mix(theme.bgTop, Color.White, Light ? 0.02 : 0.04); } }
        private Color SurfaceBottom { get { return theme.bgBottom; } }
        private Color FieldBg
        {
            get
            {
                if (skinDefault) return Light ? Color.FromArgb(12, 0, 0, 0) : Color.FromArgb(14, 255, 255, 255);
                return Light ? Mix(skin.panelBg, Color.White, 0.55) : Mix(skin.panelBg, Color.Black, 0.22);
            }
        }
        private Color FieldBorder
        {
            get
            {
                if (skinDefault) return Light ? Color.FromArgb(26, 0, 0, 0) : Color.FromArgb(22, 255, 255, 255);
                return Light ? Mix(skin.panelBg, skin.windowInk, 0.18) : Mix(skin.panelBg, Color.White, 0.14);
            }
        }
        private Color OnAccent
        {
            get
            {
                if (!skinDefault) return skin.accentInk;
                return Luminance(Accent) > 0.55 ? Color.FromArgb(23, 17, 17) : Color.White;
            }
        }

        // ---- 皮肤圆角（默认皮肤全部保持 v3.6 的数值）----
        private int NoteRadius { get { return S(skin.radiusNote); } }                                   // 默认 18
        private int BubbleRadius { get { return S(skinDefault ? 11 : skin.radiusBubble); } }            // 默认 11
        private int InputRadius { get { return S(skinDefault ? 12 : skin.radiusBubble); } }             // 默认 12
        private int FieldRadius { get { return S(skinDefault ? 10 : skin.radiusCard); } }               // 默认 10
        private int ButtonRadius { get { return S(skinDefault ? 9 : skin.radiusCard); } }               // 默认 9
        private int ContactRadius(bool vertical)
        {
            int half = S(vertical ? 34 : 30) / 2;
            return skinDefault ? S(vertical ? 9 : 15) : Math.Min(S(skin.radiusPill), half);
        }
        private int PillRadius(Rectangle r)
        {
            int half = r.Height / 2;
            return skinDefault ? half : Math.Min(S(skin.radiusPill), half);
        }

        // ---------------------------------------------------------------- 打开 / 关闭

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x00000080 | WsExLayered; return cp; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            openedAt = Environment.TickCount;
            stateAt = openedAt;
            MarkSelectedRead();
            // 面板开着的过程中收滚轮：鼠标滚轮消息只发给焦点窗口，面板不一定拿到焦点，
            // 所以挂消息过滤器，只要光标在面板上就由面板处理（对话多了能滚动查看）。
            if (wheelFilter == null)
            {
                wheelFilter = new NoteWheelFilter(this);
                Application.AddMessageFilter(wheelFilter);
            }
            anim.Start();
            Invalidate();
        }

        public void ClosePanel()
        {
            if (closing) return;
            closing = true;
            pendingEdit = null;
            CloseFieldEditor();
            closeAt = Environment.TickCount;
            anim.Start();
            Invalidate();
        }

        public void RefreshData()
        {
            if (IsDisposed) return;
            // 对话数量跨过「一排 / 两排」的分界（新对话来了 / 删掉了）时，列表页高度跟着变
            if (state == PanelState.Contacts && !heightAnimating)
            {
                int target = S(columns ? ColumnsHDip : DrawerContactsPanelHDip);
                if (Math.Abs(target - heightTo) > 1) SetState(PanelState.Contacts, false);
            }
            if (historyScroll > HistoryMaxScroll()) historyScroll = HistoryMaxScroll();
            BuildHits();
            Invalidate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible && !closing && !editingField) ClosePanel();
        }

        private void FinishClose()
        {
            anim.Stop();
            pendingEdit = null;
            if (!IsDisposed) Close();
        }

        private void StepAnim()
        {
            if (closing)
            {
                if (Environment.TickCount - closeAt >= CloseMs) { FinishClose(); return; }
                Invalidate();
                return;
            }
            if (heightAnimating)
            {
                if (Environment.TickCount - heightAnimAt >= 320)
                {
                    heightNow = heightTo;
                    heightAnimating = false;
                }
                else
                {
                    double p = Clamp01((Environment.TickCount - heightAnimAt) / 320.0);
                    double e = Ease(p);
                    heightNow = (float)(heightFrom + (heightTo - heightFrom) * e);
                }
                SetBounds(winLeft, TopForHeight(heightNow), winWidth, (int)heightNow);
                BuildHits();      // 高度在变：输入框 / 发送按钮的命中区要跟着走，否则长高后点不到
                if (!heightAnimating) RunPendingEdit();
            }
            animTick++;
            Invalidate();
            // 历史页 8 行按 45ms 一行错峰淡入，最后一行要 ~535ms 才全不透明；
            // 以前 420ms 就停 anim，最后一行常停在半透明，看起来像「最新一条不显示」。
            int settle = historySettleMs;
            int lastAct = Math.Max(stateAt, sentAt);
            if (!closing && Environment.TickCount - lastAct > settle && !heightAnimating) anim.Stop();
        }

        /// <summary>
        /// 刚发出一条消息时的进入动画进度（0→1，240ms）。只有「新消息进场 / 旧消息上移」用这个，
        /// 其余行不再重新淡入——发完消息不该整块刷新。
        /// </summary>
        private double EnterProgress()
        {
            if (sentAt == 0) return 1;
            return Clamp01((Environment.TickCount - sentAt) / 240.0);
        }

        /// <summary>长高动画结束后再开编辑窗（点输入框会先长高到历史态）。</summary>
        private void RunPendingEdit()
        {
            Action a = pendingEdit;
            pendingEdit = null;
            if (a != null && !IsDisposed && !closing) a();
        }

        private static double Clamp01(double t) { return t < 0 ? 0 : t > 1 ? 1 : t; }
        private static double Ease(double t) { return 1 - Math.Pow(1 - t, 3); }

        /// <summary>长高到历史态时，如果会顶出屏幕底边就把窗口整体上移（历史 476 DIP 比聊天态高 1.7 倍）。</summary>
        private int TopForHeight(float h)
        {
            int t = Math.Min(baseTop, workArea.Bottom - (int)h - 2);
            return Math.Max(workArea.Top + 2, t);
        }

        private void SetState(PanelState s, bool historyHeight)
        {
            state = s;
            historyScroll = 0;
            historySettleMs = s == PanelState.History ? 760 : 420;
            stateAt = Environment.TickCount;
            sentAt = 0;
            sentPrevCount = 0;
            int normalH = S(columns ? ColumnsHDip : (state == PanelState.Contacts ? DrawerContactsPanelHDip : DrawerHDip));
            int historyH = S(columns ? ColumnsHistoryHDip : DrawerHistoryHDip);
            float target = historyHeight ? historyH : normalH;
            if (Math.Abs(target - heightNow) > 1)
            {
                heightFrom = heightNow;
                heightTo = target;
                heightAnimAt = Environment.TickCount;
                heightAnimating = true;
            }
            BuildHits();
            anim.Start();
            Invalidate();
        }

        private void MarkSelectedRead()
        {
            if (string.IsNullOrEmpty(selectedId)) return;
            var t = note.Thread(selectedId);
            bool had = t.unread > 0;
            t.unread = 0;
            if (had && onRead != null) onRead(selectedId);
        }

        // ---------------------------------------------------------------- 布局

        private int BodyTop() { return S(columns ? 52 : 100); }
        private Rectangle HeaderRect() { return new Rectangle(S(PadDip), S(14), winWidth - S(PadDip) * 2, S(34)); }
        private Rectangle ContactsRect()
        {
            if (!columns) return new Rectangle(S(PadDip), S(56), winWidth - S(PadDip) * 2, S(DrawerContactsStripHDip));
            return new Rectangle(0, S(52), S(RailWDip), (int)heightNow - S(52));
        }
        private int InputH { get { return S(38); } }
        private Rectangle InputRect()
        {
            int x = columns ? S(RailWDip + PadDip) : S(PadDip);
            int w = columns ? winWidth - S(RailWDip) - S(PadDip) * 2 : winWidth - S(PadDip) * 2;
            return new Rectangle(x, (int)heightNow - S(PadDip) - InputH, w, InputH);
        }
        private Rectangle BodyRect()
        {
            int x = columns ? S(RailWDip + PadDip) : S(PadDip);
            int w = columns ? winWidth - S(RailWDip) - S(PadDip) * 2 : winWidth - S(PadDip) * 2;
            int top = BodyTop();
            int bottom = InputRect().Top - S(8);
            return new Rectangle(x, top, w, Math.Max(10, bottom - top));
        }

        private Rectangle ContactRectAt(int index)
        {
            if (!columns)
            {
                int x = S(PadDip);
                int y = ContactsRect().Y;
                int per = (winWidth - S(PadDip) * 2 - S(8)) / DrawerVisibleContacts;
                int row = index / DrawerVisibleContacts;
                int col = index % DrawerVisibleContacts;
                return new Rectangle(x + col * (per + S(2)), y + row * S(DrawerRowStepDip), per - S(2), S(30));
            }
            int rowH = S(34);
            return new Rectangle(S(10), ContactsRect().Y + S(8) + index * S(ContactRowStrideDip), S(RailWDip) - S(20), rowH);
        }

        /// <summary>双栏式左栏底部「＋ 新建对话」胶囊。对话多了就钉在栏底，少了就跟在最后一行下面。</summary>
        private Rectangle ColumnsAddRect()
        {
            var area = ContactsRect();
            int y = area.Y + S(8) + drawnContacts.Count * S(ContactRowStrideDip);
            int maxY = area.Bottom - S(38);
            if (y > maxY) y = maxY;
            return new Rectangle(S(10), y, S(RailWDip) - S(20), S(30));
        }

        /// <summary>右键出现的「删除」按钮：画在对话胶囊右半边，命中区就是它。</summary>
        private Rectangle DeleteButtonRect(Rectangle capsule)
        {
            int w = Math.Max(S(26), capsule.Width / 2);
            int h = Math.Max(S(16), capsule.Height - S(6));
            return new Rectangle(capsule.Right - w - S(4), capsule.Y + (capsule.Height - h) / 2, w, h);
        }

        /// <summary>
        /// 双栏式左栏一屏能放几行对话：底部要留「＋ 新建对话」胶囊（30 高 + 8 边距）和最后一行之间 6 的间距。
        /// 正常高度（298 DIP，栏高 246）算出来是 5 行；历史态（512）能放 10 行。
        /// </summary>
        private int ColumnsVisibleCount()
        {
            int railH = ContactsRect().Height;
            return Math.Max(1, (railH - S(86)) / S(ContactRowStrideDip) + 1);
        }

        /// <summary>对话列表滚动窗口最多能滚到哪（滚动窗口起点上限）。</summary>
        private int MaxContactScroll()
        {
            int vis = VisibleContactCount;
            return Math.Max(0, note.contacts.Count - vis);
        }

        /// <summary>把某个对话滚进可见窗口（选中 / 新建对话时用）。</summary>
        private void EnsureContactVisible(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            int idx = -1;
            for (int i = 0; i < note.contacts.Count; i++)
                if (note.contacts[i] != null && note.contacts[i].id == id) { idx = i; break; }
            if (idx < 0) return;
            int vis = VisibleContactCount;
            if (idx < contactScroll) contactScroll = idx;
            else if (idx >= contactScroll + vis) contactScroll = idx - vis + 1;
            ClampContactScroll();
        }

        private void ClampContactScroll()
        {
            if (contactScroll < 0) contactScroll = 0;
            int max = MaxContactScroll();
            if (contactScroll > max) contactScroll = max;
        }

        /// <summary>滚轮滚对话列表：delta &gt; 0 往后看。到头上就不动。</summary>
        public void ScrollContacts(int delta)
        {
            if (delta == 0) return;
            int before = contactScroll;
            contactScroll += delta;
            ClampContactScroll();
            if (before == contactScroll) return;
            deleteId = "";          // 删除按钮跟着那一行走，滚掉就对不上了，先收掉
            hoverContact = -1;
            BuildHits();
            Invalidate();
        }

        /// <summary>历史页还能往上翻几条（0 = 一屏装得下）。</summary>
        /// <summary>历史页第一行的上边（窗口像素坐标）——和绘制逻辑保持一致，验收用。</summary>
        internal int HistoryRowsTopPx()
        {
            int top = BodyRect().Y;
            if (columns) top += S(22);
            else if (!string.IsNullOrEmpty(selectedId) && note.IsRoom(selectedId) && state != PanelState.History) top += S(22);
            return top + S(16);
        }

        /// <summary>历史页最后一行的底边（窗口像素坐标）：断言它不超过 BodyRect 底边，否则会被输入框裁掉。</summary>
        internal int HistoryRowsBottomPx()
        {
            int rowH2 = columns ? S(34) : S(36);
            return HistoryRowsTopPx() + NoteSettings.HistoryRows * rowH2;
        }

        /// <summary>正文区（不含输入框）的底边，窗口像素坐标。</summary>
        internal int BodyBottomPx() { return BodyRect().Bottom; }

        /// <summary>聊天态最近两条气泡的底边（窗口像素坐标）；房间对话带名字行，验证不会被输入框裁掉。</summary>
        internal int ChatBubblesBottomPx()
        {
            int top = BodyRect().Y;
            if (columns) top += S(22);
            else if (!string.IsNullOrEmpty(selectedId) && note.IsRoom(selectedId)) top += S(22);
            return top + 2 * (S(34) + S(7));
        }

        internal int HistoryMaxScroll()
        {
            if (string.IsNullOrEmpty(selectedId) || note == null) return 0;
            var t = note.Thread(selectedId);
            int n = t == null ? 0 : t.messages.Count;
            return Math.Max(0, n - NoteSettings.HistoryRows);
        }

        /// <summary>历史页翻一页（delta &gt; 0 = 看更早的）。到头上就不动，返回是否真的动了。</summary>
        public bool ScrollHistory(int delta)
        {
            if (delta == 0) return false;
            int before = historyScroll;
            historyScroll += delta;
            int max = HistoryMaxScroll();
            if (historyScroll < 0) historyScroll = 0;
            if (historyScroll > max) historyScroll = max;
            if (before == historyScroll) return false;
            Invalidate();
            return true;
        }

        /// <summary>右键某个对话：在该对话上显示「删除」按钮。</summary>
        public void ShowDelete(string id)
        {
            if (string.IsNullOrEmpty(id) || note.Contact(id) == null) return;
            deleteId = id;
            BuildHits();
            Invalidate();
        }

        public void HideDelete()
        {
            if (string.IsNullOrEmpty(deleteId)) return;
            deleteId = "";
            BuildHits();
            Invalidate();
        }

        /// <summary>删除对话（连带本机这段 8 条消息）。面板立刻生效，再交给 Widget 落盘。</summary>
        public void DeleteConversation(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var c = note.Contact(id);
            string name = c == null ? id : c.Display();
            note.RemoveContact(id);
            if (onDeleteContact != null) onDeleteContact(id);
            if (selectedId == id)
            {
                selectedId = null;
                deleteId = "";
                SetState(PanelState.Contacts, false);
            }
            deleteId = "";
            ClampContactScroll();
            status = "已删除对话 " + name;
            statusAt = Environment.TickCount;
            BuildHits();
            Invalidate();
        }

        private string ContactAt(Point p)
        {
            for (int i = 0; i < drawnContacts.Count; i++)
                if (ContactRectAt(i).Contains(p)) return drawnContacts[i].id;
            return null;
        }

        /// <summary>滚轮（消息过滤器里调用）：光标在面板上才处理，返回 true 表示这条消息吃掉了。</summary>
        internal bool HandleWheelMessage(IntPtr wParam, IntPtr lParam)
        {
            if (closing || IsDisposed || !Visible) return false;
            if (editingField || state == PanelState.Add) return false;
            long lp = lParam.ToInt64();
            int x = (short)(lp & 0xffff);
            int y = (short)((lp >> 16) & 0xffff);
            if (!Bounds.Contains(new Point(x, y))) return false;
            int delta = (short)(((wParam.ToInt64() >> 16) & 0xffff));
            if (delta == 0) return false;
            // 历史页：先在历史记录里往上翻（能看到更早的），翻到头再退回对话列表滚动
            if (state == PanelState.History && HistoryMaxScroll() > 0)
            {
                if (ScrollHistory(delta > 0 ? 1 : -1)) return true;
            }
            if (note.contacts.Count <= VisibleContactCount) return false;
            ScrollContacts(delta > 0 ? -1 : 1);
            return true;
        }

        private sealed class NoteWheelFilter : IMessageFilter
        {
            private readonly NotePanelForm owner;
            public NoteWheelFilter(NotePanelForm owner) { this.owner = owner; }
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WmMouseWheel) return false;
                try { return owner != null && !owner.IsDisposed && owner.HandleWheelMessage(m.WParam, m.LParam); }
                catch { return false; }
            }
        }

        private void BuildHits()
        {
            hits.Clear();
            if (state == PanelState.Add)
            {
                var sheet = new Rectangle(0, 0, winWidth, (int)heightNow);
                hits.Add(new Hit(new Rectangle(winWidth - S(52), S(14), S(30), S(30)), HitKind.Close, "close"));
                hits.Add(new Hit(new Rectangle(S(16), S(56), winWidth - S(32), S(40)), HitKind.IdField, "id"));
                hits.Add(new Hit(new Rectangle(S(16), S(104), S(78), S(30)), HitKind.Probe, "probe"));
                hits.Add(new Hit(new Rectangle(S(16), S(150), winWidth - S(32), S(40)), HitKind.RemarkField, "remark"));
                hits.Add(new Hit(new Rectangle(S(16), (int)heightNow - S(58), winWidth - S(32), S(40)), HitKind.ConfirmAdd, "confirm"));
                return;
            }
            HitKind addKind = HitKind.AddContact;
            hits.Add(new Hit(new Rectangle(winWidth - S(118), S(14), S(102), S(30)), addKind, "add"));
            // 卡片页「＋ 新建对话」左边的昵称胶囊：双击改本机昵称
            hits.Add(new Hit(NickRect(), HitKind.Nickname, "nicki"));
            drawnContacts.Clear();
            ClampContactScroll();
            int vis = VisibleContactCount;
            int idx = 0;
            for (int i = contactScroll; i < note.contacts.Count && idx < vis; i++)
            {
                var c = note.contacts[i];
                if (c == null) continue;
                drawnContacts.Add(c);
                hits.Add(new Hit(ContactRectAt(idx), HitKind.Contact, c.id));
                idx++;
            }
            // 双栏左栏底部的「＋ 新建对话」胶囊也要能点（位置跟着可见行数走，必须在 drawnContacts 填好后算）
            if (columns) hits.Add(new Hit(ColumnsAddRect(), addKind, "add"));
            // 右键选中的对话：先放「删除」命中区，比整条胶囊优先命中；不在可见窗口里就收掉
            if (!string.IsNullOrEmpty(deleteId))
            {
                int di = -1;
                for (int i = 0; i < drawnContacts.Count; i++)
                    if (drawnContacts[i].id == deleteId) { di = i; break; }
                if (di < 0) deleteId = "";
                else hits.Insert(0, new Hit(DeleteButtonRect(ContactRectAt(di)), HitKind.Delete, deleteId));
            }
            if (state == PanelState.Chat || state == PanelState.History)
            {
                // 双栏聊天头部的名字也能双击改备注（和左栏联系人名字一致）
                if (columns && !string.IsNullOrEmpty(selectedId))
                {
                    var headBody = BodyRect();
                    hits.Add(new Hit(new Rectangle(headBody.X - S(2), headBody.Y - S(2),
                        headBody.Width, S(24)), HitKind.Contact, selectedId));
                }
                var input = InputRect();
                hits.Add(new Hit(new Rectangle(input.X, input.Y, input.Width - S(40), input.Height), HitKind.Input, "input"));
                hits.Add(new Hit(new Rectangle(input.Right - S(38), input.Y + S(4), S(32), S(30)), HitKind.Send, "send"));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (closing) return;
            if (e.Button != MouseButtons.Right) return;
            // 右键对话 → 在这条对话上显示「删除」按钮；右键别处 → 收掉已有按钮
            if (state == PanelState.Add) { HideDelete(); return; }
            string id = ContactAt(e.Location);
            if (!string.IsNullOrEmpty(id)) ShowDelete(id);
            else HideDelete();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // 正常情况走消息过滤器（别的窗口拿焦点也能滚）；这里是焦点本来就在面板上的兜底
            if (closing || editingField || state == PanelState.Add) return;
            if (state == PanelState.History && HistoryMaxScroll() > 0)
            {
                if (ScrollHistory(e.Delta > 0 ? 1 : -1)) return;
            }
            if (note.contacts.Count <= VisibleContactCount) return;
            ScrollContacts(e.Delta > 0 ? -1 : 1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            lastMouseInside = ClientRectangle.Contains(e.Location);
            if (closing) return;
            int hover = -1;
            if (state != PanelState.Add)
            {
                for (int i = 0; i < drawnContacts.Count; i++)
                    if (ContactRectAt(i).Contains(e.Location)) { hover = i; break; }
            }
            if (hover != hoverContact)
            {
                hoverContact = hover;
                if (hover >= 0 && state == PanelState.Contacts)
                {
                    SelectContact(drawnContacts[hover].id, false);
                }
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (closing || e.Button != MouseButtons.Left) return;
            for (int i = 0; i < hits.Count; i++)
            {
                if (!hits[i].rect.Contains(e.Location)) continue;
                HandleHit(hits[i]);
                return;
            }
            // 点空白也回对话列表；右键出来的删除按钮收掉
            if (!string.IsNullOrEmpty(deleteId)) HideDelete();
            if (state == PanelState.Chat && !InputRect().Contains(e.Location))
                SelectContact(selectedId, true);
        }

        private void HandleHit(Hit hit)
        {
            switch (hit.kind)
            {
                case HitKind.AddContact:
                    addId = ""; addRemark = ""; addOnline = null; addProbing = false;
                    SetState(PanelState.Add, false);
                    return;
                case HitKind.Close:
                    SetState(PanelState.Contacts, false);
                    return;
                case HitKind.Contact:
                    // 双击同一个对话名字 = 改备注；单击还是正常选中会话
                    if (hit.key != null && hit.key == lastClickContact
                        && Environment.TickCount - lastClickAt <= SystemInformation.DoubleClickTime)
                    {
                        lastClickContact = null;
                        lastClickAt = 0;
                        EditRemark(hit.key);
                        return;
                    }
                    HideDelete();
                    lastClickContact = hit.key;
                    lastClickAt = Environment.TickCount;
                    SelectContact(hit.key, false);
                    return;
                case HitKind.Delete:
                    DeleteConversation(hit.key);
                    return;
                case HitKind.Input:
                    EditCompose();      // 长高和开编辑窗都交给 EditCompose：先长高、动画完再开窗，位置才对得上
                    return;
                case HitKind.Send:
                    SendCompose();
                    return;
                case HitKind.IdField:
                    EditAddField(true);
                    return;
                case HitKind.RemarkField:
                    EditAddField(false);
                    return;
                case HitKind.Probe:
                    ProbeAdd();
                    return;
                case HitKind.ConfirmAdd:
                    ConfirmAdd();
                    return;
                case HitKind.Nickname:
                    // 双击昵称胶囊才进编辑；单击先给一句提示，避免手滑就弹输入框
                    if (Environment.TickCount - lastNickClickAt <= SystemInformation.DoubleClickTime)
                    {
                        lastNickClickAt = 0;
                        EditNickname();
                        return;
                    }
                    lastNickClickAt = Environment.TickCount;
                    status = "双击「昵称」可以改本机昵称";
                    statusAt = Environment.TickCount;
                    Invalidate();
                    return;
            }
        }

        private void SelectContact(string id, bool keepState)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!string.Equals(selectedId, id, StringComparison.Ordinal)) composeText = "";   // 换人别把上一人的草稿带过去
            selectedId = id;
            sentAt = 0;
            sentPrevCount = 0;
            deleteId = "";
            EnsureContactVisible(id);   // 新建 / 别人发来的对话可能在列表外面，选中后滚到看得见
            BuildHits();
            if (!keepState) SetState(PanelState.Chat, false);
            var t = note.Thread(id);
            if (t.unread > 0)
            {
                t.unread = 0;
                if (onRead != null) onRead(id);
            }
            anim.Start();
            Invalidate();
        }

        // ---------------------------------------------------------------- 改备注（双击名字）

        /// <summary>双击联系人名字：在名字原位开一个小编辑窗改备注（普通编辑窗样式，不放大、不居中）。</summary>
        private void EditRemark(string id)
        {
            NoteContact c = note.Contact(id);
            if (c == null) return;
            if (fieldEditor != null && !fieldEditor.IsDisposed) CloseFieldEditor();
            int idx = -1;
            for (int i = 0; i < drawnContacts.Count; i++)
                if (drawnContacts[i].id == id) { idx = i; break; }
            Rectangle r = idx >= 0
                ? ContactRectAt(idx)
                : new Rectangle(S(PadDip), S(56), winWidth - S(PadDip) * 2, S(36));
            var screen = new Rectangle(Left + r.X, Top + r.Y, r.Width, r.Height);
            string pending = c.remark ?? "";
            OpenField(screen, c.remark ?? "", NoteSettings.MaxRemark,
                delegate(string v) { pending = v; },
                delegate { ApplyRemark(id, pending); },
                false, 12.5f, true);
        }

        /// <summary>改备注：面板先改（显示立刻变），再交给 Widget 落盘。空备注 = 恢复显示 ID。</summary>
        private void ApplyRemark(string id, string remark)
        {
            NoteContact c = note.Contact(id);
            if (c == null) return;
            remark = (remark ?? "").Trim();
            if (remark.Length > NoteSettings.MaxRemark) remark = remark.Substring(0, NoteSettings.MaxRemark);
            c.remark = remark;
            if (onRenameContact != null) onRenameContact(id, remark);
            status = string.IsNullOrEmpty(remark) ? "备注已清空" : "备注：" + remark;
            statusAt = Environment.TickCount;
            BuildHits();
            Invalidate();
        }

        // ---------------------------------------------------------------- 改昵称（双击昵称胶囊）

        /// <summary>双击卡片页的「昵称」胶囊：在原位开小编辑窗改本机昵称（最多 12 个字）。</summary>
        private void EditNickname()
        {
            if (fieldEditor != null && !fieldEditor.IsDisposed) CloseFieldEditor();
            Rectangle r = NickRect();
            var screen = new Rectangle(Left + r.X, Top + r.Y, r.Width, r.Height);
            string pending = note.nickname ?? "";
            OpenField(screen, note.nickname ?? "", NoteSettings.MaxNick,
                delegate(string v) { pending = v; },
                delegate { ApplyNickname(pending); },
                false, 11.5f, true);
        }

        /// <summary>改昵称：面板先改（头部立刻变），再交给 Widget 落盘 + 重启中继广播。</summary>
        private void ApplyNickname(string nick)
        {
            nick = NoteSettings.CleanNick(nick);
            note.nickname = nick;
            if (onSetNickname != null) onSetNickname(nick);
            status = string.IsNullOrEmpty(nick) ? "昵称已清空，别人会看到 ID" : "昵称：" + nick;
            statusAt = Environment.TickCount;
            BuildHits();
            Invalidate();
        }

        // ---------------------------------------------------------------- 聊天输入框

        private void EditCompose()
        {
            if (fieldEditor != null && !fieldEditor.IsDisposed) { fieldEditor.Activate(); return; }
            // 点输入框会先把面板长高到历史态，长高以后再开编辑窗，否则编辑窗停在旧位置、和输入框对不上
            if (state == PanelState.Chat)
            {
                SetState(PanelState.History, true);
                pendingEdit = OpenComposeEditor;
                if (!heightAnimating) RunPendingEdit();
                return;
            }
            OpenComposeEditor();
        }

        private void OpenComposeEditor()
        {
            if (fieldEditor != null || IsDisposed) return;
            var input = InputRect();
            OpenField(new Rectangle(Left + input.X + S(2), Top + input.Y + S(2), input.Width - S(44), input.Height - S(4)),
                composeText, NoteSettings.MaxText,
                delegate(string v) { composeText = v; },
                null, false, 12.5f, false,
                delegate { return SendCompose(); });
        }

        /// <summary>
        /// 小编辑窗（非模态）：输入实时同步，Enter 收起并提交，Esc 还原，点到别处也收起。
        /// keepOpenSubmit 非空时（聊天输入框）：回车发送成功后清空文字、保持焦点，可以接着打下一句，不用再点一次。
        /// </summary>
        private void OpenField(Rectangle screen, string value, int maxLen, Action<string> onText, Action onSubmit,
            bool centered = false, float fontPx = 12.5f, bool commitOnBlur = false, Func<bool> keepOpenSubmit = null)
        {
            editingField = true;
            var ed = new NoteField(value, screen, FieldSolid(true), Ink, Accent, scale, topMost, centered, Pt(fontPx),
                skinDefault ? 11 : skin.radiusBubble);
            if (maxLen > 0) ed.MaxLength = maxLen;
            fieldEditor = ed;
            ed.ValueChanged = onText;
            ed.Submitted = onSubmit;
            ed.CommitOnBlur = commitOnBlur;
            ed.SubmitKeepsOpen = keepOpenSubmit;
            ed.FormClosed += editor_FormClosed;
            ed.Show(this);
        }

        private void CloseFieldEditor()
        {
            NoteField ed = fieldEditor;
            fieldEditor = null;
            if (ed != null && !ed.IsDisposed) ed.Close();
        }

        private void editor_FormClosed(object sender, FormClosedEventArgs e)
        {
            NoteField ed = sender as NoteField;
            if (fieldEditor == sender) fieldEditor = null;
            editingField = false;
            if (IsDisposed || closing) return;
            if (ed != null && ed.ClosedByKey)
            {
                Activate();
                BuildHits();
                Invalidate();
                if (!ed.Cancelled && ed.Submitted != null) ed.Submitted();
                else if (ed.Cancelled && state == PanelState.History) SetState(PanelState.Chat, false);   // Esc 取消输入 → 缩回聊天态
                return;
            }
            // 点到别处收起：看鼠标现在落在哪。落在面板上说明是点面板上的按钮（面板会被这次点击激活），
            // 不能把面板关掉；落在别处（点别的程序 / 点卡片）就跟着面板一起收。
            BuildHits();
            Invalidate();
            // 改备注的编辑窗：点到别处也把这次修改落下（Esc 取消除外）
            if (ed != null && !ed.Cancelled && ed.CommitOnBlur && ed.Submitted != null) ed.Submitted();
            if (!CursorOnPanel()) ClosePanel();
        }

        /// <summary>光标是不是落在面板上。正常用真实光标；拿不到光标（锁屏 / 无输入桌面 / 远程）时用最后一次面板内的坐标。</summary>
        private bool CursorOnPanel()
        {
            Point c = Cursor.Position;
            if (c.X != 0 || c.Y != 0) return Bounds.Contains(c);
            return lastMouseInside;
        }

        private bool SendCompose()
        {
            string text = (composeText ?? "").Trim();
            if (text.Length == 0)
            {
                status = "写一句再发";
                statusAt = Environment.TickCount;
                Invalidate();
                return false;
            }
            if (text.Length > NoteSettings.MaxText) text = text.Substring(0, NoteSettings.MaxText);
            int prevCount = 0;
            NoteThread th = string.IsNullOrEmpty(selectedId) ? null : note.Thread(selectedId);
            if (th != null) prevCount = th.messages.Count;
            bool ok = false;
            if (onSend != null) ok = onSend(selectedId, text);
            if (ok)
            {
                // 不 SetState、不改高度：会话原有的消息整体上移一位，新这条淡入进来，
                // 其余行不再重新淡入（以前发一条会整块刷新、还闪一下「已发出」）。
                composeText = "";
                status = "";
                statusAt = Environment.TickCount;
                sentPrevCount = prevCount;
                historyScroll = 0;   // 发完自动回到最新一屏
                sentAt = Environment.TickCount;
                if (fieldEditor != null && !fieldEditor.IsDisposed) fieldEditor.ClearText();
                anim.Start();
                BuildHits();
                Invalidate();
                EnsureComposeEditor();
                return true;
            }
            composeText = text;
            status = "没发出去：检查中继连接或对方 ID";
            statusAt = Environment.TickCount;
            Invalidate();
            return false;
        }

        /// <summary>发完消息输入框继续可用：回车路径编辑窗本来就留着；点发送按钮那条路径被点掉的，这里补开。</summary>
        private void EnsureComposeEditor()
        {
            if (IsDisposed || closing) return;
            if (state != PanelState.History) return;     // 只有真正在打字的长高态才自动开窗
            if (fieldEditor != null && !fieldEditor.IsDisposed) { fieldEditor.Activate(); return; }
            if (heightAnimating) { pendingEdit = OpenComposeEditor; return; }
            OpenComposeEditor();
        }

        private void EditAddField(bool idField)
        {
            if (fieldEditor != null && !fieldEditor.IsDisposed) { fieldEditor.Activate(); return; }
            var rect = idField
                ? new Rectangle(Left + S(16), Top + S(56), winWidth - S(32), S(40))
                : new Rectangle(Left + S(16), Top + S(150), winWidth - S(32), S(40));
            if (idField)
                OpenField(rect, addId, 5,
                    delegate(string v) { addId = v; addOnline = null; },
                    delegate { if (NoteSettings.ValidId(addId)) ProbeAdd(); });
            else
                OpenField(rect, addRemark, NoteSettings.MaxRemark,
                    delegate(string v) { addRemark = v; }, null);
        }

        private void ProbeAdd()
        {
            string code = NoteSettings.NormalizeCode(addId);
            addId = code;
            if (!NoteSettings.ValidCode(code))
            {
                status = "先填 5 位字符（对方 ID 或对话码）";
                statusAt = Environment.TickCount;
                Invalidate();
                return;
            }
            if (!NoteSettings.ValidId(code))
            {
                // 对话码没有「在线」这种状态：对方输入同一个码就自然进同一个对话
                addProbing = false;
                addOnline = null;
                status = "对话码 ✓ 对方输入同一个码就能进这个对话";
                statusAt = Environment.TickCount;
                Invalidate();
                return;
            }
            if (onProbe == null)
            {
                addOnline = false;
                status = "中继没连上，去 设置 → 纸条 检查";
                statusAt = Environment.TickCount;
                Invalidate();
                return;
            }
            addProbing = true;
            addOnline = null;
            status = "";
            Invalidate();
            onProbe(code, delegate(bool online)
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        addProbing = false;
                        addOnline = online;
                        status = online ? "● 在线，可以加" : "○ 没在线，也能先加";
                        statusAt = Environment.TickCount;
                        Invalidate();
                    });
                }
                catch { }
            });
        }

        private void ConfirmAdd()
        {
            string code = NoteSettings.NormalizeCode(addId);
            string remark = (addRemark ?? "").Trim();
            if (!NoteSettings.ValidCode(code))
            {
                status = "要 5 位字符：5 位数字 = 和这个人聊，其它 = 对话码";
                statusAt = Environment.TickCount;
                Invalidate();
                if (code.Length == 0) EditAddField(true);   // 空的时候直接把编辑窗打开，省得用户找不到
                return;
            }
            addId = code;
            bool room = !NoteSettings.ValidId(code);
            bool added = onAddContact != null && onAddContact(code, remark);
            selectedId = code;
            CloseFieldEditor();
            SetState(PanelState.Chat, false);
            if (!added) status = "这个对话已经在列表里了";
            else if (room) status = "已创建对话 " + code + "（同码的人都会进来）";
            else status = "已添加 " + (string.IsNullOrEmpty(remark) ? code : remark);
            statusAt = Environment.TickCount;
            BuildHits();
            Invalidate();
        }

        private Color FieldSolid(bool background)
        {
            if (!skinDefault)
                return Light ? (background ? Mix(skin.panelBg, Color.White, 0.6) : Mix(skin.panelBg, Color.Black, 0.24))
                    : (background ? Mix(skin.panelBg, Color.Black, 0.18) : Mix(skin.panelBg, Color.White, 0.24));
            return Light ? (background ? Mix(theme.bgBottom, Color.White, 0.6) : Mix(theme.bgBottom, Color.Black, 0.24))
                : (background ? Mix(theme.bgBottom, Color.Black, 0.22) : Mix(theme.bgBottom, Color.White, 0.28));
        }

        // ---------------------------------------------------------------- 绘制

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
            int w = Math.Max(1, winWidth);
            int h = Math.Max(1, (int)heightNow);
            if (frame == null || frame.Width != w || frame.Height != h)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            }
            frame.SetResolution(96f * scale, 96f * scale);
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                double open = Clamp01((Environment.TickCount - openedAt) / 300.0);
                double close = closing ? Clamp01((Environment.TickCount - closeAt) / (double)CloseMs) : 0;
                double alpha = open * (1 - close);
                if (alpha <= 0.01) return;
                int slide = (int)Math.Round(S(14) * (1 - Ease(open)));
                if (closing) slide += (int)Math.Round(S(10) * close * (toRight ? 1 : -1));

                var saved = g.Save();
                g.TranslateTransform(slide, 0);
                using (var path = RoundPath(new Rectangle(0, 0, w, h), NoteRadius))
                    PaintPanelBackdrop(g, path, new Rectangle(0, 0, w, Math.Max(1, h)), alpha);

                DrawHeader(g, alpha);
                if (state == PanelState.Add) DrawAddSheet(g, alpha);
                else
                {
                    DrawContacts(g, alpha);
                    DrawBody(g, alpha);
                    DrawInput(g, alpha);
                }
                // 整张卡片的细边框：主题色沿圆角走一圈。必须在所有内容之后画——
                // 「新建对话」页的 DrawAddSheet 会把整卡渐变重填一遍，先画就被盖掉了。
                // 笔迹往里缩半笔宽，以前直接画在窗口边上，一半被裁掉，只剩不到 1px，几乎看不见。
                // 用户反馈「边框稍微粗一点」：从 1 DIP 调到 CardBorderDip（1.8 DIP）。
                // 皮肤（非默认）：描边色改走 skin.panelEdge，霓虹用 品红→青 渐变描边 + 内侧辉光。
                float bw = Math.Max(2f, S(CardBorderDip));
                int bi = (int)Math.Max(1, Math.Round(bw / 2.0));
                var border = new Rectangle(bi, bi, Math.Max(2, w - bi * 2), Math.Max(2, h - bi * 2));
                using (var path = RoundPath(border, Math.Max(2, NoteRadius - bi)))
                    PaintPanelBorder(g, path, border, bw, alpha);
                g.Restore(saved);
            }
        }

        /// <summary>
        /// 面板底：玻璃皮肤（云母）走 Frost；默认皮肤还是 v3.6 的主题渐变；其它皮肤用 skin.panelBg，
        /// 再叠一层皮肤高光；宣纸额外铺一层纸纹颗粒（复用 Frost 的噪点 tile）。
        /// </summary>
        private void PaintPanelBackdrop(Graphics g, GraphicsPath path, Rectangle r, double a)
        {
            if (glass)
            {
                Frost.PaintSurface(g, path, r, theme, a, 1);
                return;
            }
            if (skinDefault)
            {
                using (var br = new LinearGradientBrush(r, Alpha(SurfaceTop, (int)(255 * a)),
                    Alpha(SurfaceBottom, (int)(255 * a)), 90f))
                    g.FillPath(br, path);
                return;
            }
            using (var br = new LinearGradientBrush(r, Alpha(Mix(skin.panelBg, Color.White, 0.03), (int)(255 * a)),
                Alpha(skin.panelBg, (int)(255 * a)), 90f))
                g.FillPath(br, path);
            PaintPanelSheen(g, path, r, a);
            if (skin.paperGrain)
                Frost.PaintPaperGrain(g, path, r, Mix(skin.panelBg, theme.textMain, 0.35), 2.6 * a);
            if (skin.neonEdge) PaintInnerGlow(g, path, a);
        }

        /// <summary>皮肤高光：顶部一层白色渐变（宣纸最明显、无垠几乎看不见）。</summary>
        private void PaintPanelSheen(Graphics g, GraphicsPath path, Rectangle r, double a)
        {
            if (skin.sheenAlpha <= 0) return;
            GraphicsState st = g.Save();
            try
            {
                g.SetClip(path, CombineMode.Intersect);
                int h = Math.Max(1, (int)Math.Round(r.Height * 0.42));
                var band = new Rectangle(r.X, r.Y, Math.Max(1, r.Width), h);
                using (var br = new LinearGradientBrush(band,
                    Alpha(Color.White, (int)(255 * a * Math.Min(1.0, skin.sheenAlpha))),
                    Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(br, band);
            }
            finally { g.Restore(st); }
        }

        /// <summary>霓虹内侧辉光：宽笔沿圆角走一圈再按 path 裁掉外侧，只剩贴边的一圈光。</summary>
        private void PaintInnerGlow(Graphics g, GraphicsPath path, double a)
        {
            GraphicsState st = g.Save();
            try
            {
                g.SetClip(path, CombineMode.Intersect);
                using (var pen = new Pen(Alpha(Accent, (int)(255 * a * 0.20)), Math.Max(2f, S(9))))
                    g.DrawPath(pen, path);
                using (var pen = new Pen(Alpha(skin.accent2, (int)(255 * a * 0.14)), Math.Max(1.5f, S(4))))
                    g.DrawPath(pen, path);
            }
            finally { g.Restore(st); }
        }

        /// <summary>非默认皮肤整卡描边；霓虹用 品红→青 渐变描边 + 内侧细亮边。</summary>
        private void PaintPanelBorder(Graphics g, GraphicsPath path, Rectangle r, float bw, double a)
        {
            if (skinDefault)
            {
                using (var pen = new Pen(Alpha(Accent, (int)(255 * a * 0.62)), bw))
                    g.DrawPath(pen, path);
                return;
            }
            if (skin.neonEdge)
            {
                using (var grad = new LinearGradientBrush(r, Alpha(Accent, (int)(255 * a)),
                    Alpha(skin.accent2, (int)(255 * a)), 35f))
                using (var pen = new Pen(grad, bw))
                    g.DrawPath(pen, path);
                using (var pen = new Pen(Alpha(skin.accent2, (int)(255 * a * 0.30)), Math.Max(1f, bw * 0.45f)))
                    g.DrawPath(pen, path);
                return;
            }
            using (var pen = new Pen(Alpha(skin.panelEdge, (int)(255 * a * 0.95)), bw))
                g.DrawPath(pen, path);
        }

        private void DrawHeader(Graphics g, double a)
        {
            var r = HeaderRect();
            DrawPlane(g, new RectangleF(r.X, r.Y + S(3), S(24), S(26)), Alpha(Accent, (int)(255 * a)));
            // 标题字体跟皮肤走（宣纸宋体、仪表 Bahnschrift）；默认皮肤还是原来的加粗中文字体
            DrawText(g, "小纸条", fHeading, Alpha(Ink, (int)(255 * a)), r.X + S(32), r.Y + S(16));
            Rectangle nick = NickRect();
            // 副标题：有昵称就显示「我的 ID · 昵称」，没有还是「我的 ID · 这台电脑」
            string sub = "我的 ID " + note.id;
            if (!string.IsNullOrEmpty(note.nickname)) sub += " · " + note.nickname;
            double subW = Math.Max(S(40), nick.X - S(8) - (r.X + S(32)));
            DrawText(g, Fit(g, sub, fSub, (float)subW), fSub, Alpha(Dim, (int)(255 * a)), r.X + S(32), r.Y + S(28));
            int unread = note.TotalUnread();
            if (unread > 0 && state != PanelState.Add)
            {
                string txt = unread + " 条未读";
                DrawText(g, txt, fTiny, Alpha(Dim, (int)(255 * a)),
                    nick.X - S(8) - Measure(g, txt, fTiny), r.Y + S(16));
            }
            if (state != PanelState.Add)
            {
                // 「新建对话」旁边跟一颗昵称胶囊：双击就能改本机昵称（不用进设置页）
                bool set = !string.IsNullOrEmpty(note.nickname);
                Color nickBg = skinDefault
                    ? Alpha(Ink, (int)(255 * a * (set ? 0.10 : 0.05)))
                    : set ? Alpha(Light ? Mix(skin.panelBg, Color.White, 0.55) : Mix(skin.panelBg, Color.White, 0.06), (int)(255 * a))
                          : Color.FromArgb(0);
                DrawPill(g, nick, set ? "昵称 " + note.nickname : "＋ 昵称",
                    Alpha(set ? Ink : Muted, (int)(255 * a)), nickBg, a, !set);
                // 「＋ 新建对话」：默认皮肤是强调色淡底 + 强调色字；皮肤按设计改成实心强调色胶囊
                // （宣纸朱砂 / 仪表琥珀 / 霓虹品红），配 skin.accentInk 的文字色
                bool solid = !skinDefault;
                DrawPill(g, new Rectangle(r.Right - S(96), r.Y + S(2), S(96), S(28)), "＋ 新建对话",
                    Alpha(solid ? OnAccent : Accent, (int)(255 * a)),
                    Alpha(Accent, (int)(255 * a * (solid ? 1.0 : 0.16))), a, false, solid);
            }
        }

        /// <summary>头部「昵称」胶囊的位置（就在「＋ 新建对话」左边）。</summary>
        private Rectangle NickRect()
        {
            var r = HeaderRect();
            return new Rectangle(r.Right - S(96) - S(8) - S(96), r.Y + S(2), S(96), S(28));
        }

        private void DrawContacts(Graphics g, double a)
        {
            var area = ContactsRect();
            if (!columns)
            {
                for (int i = 0; i < drawnContacts.Count; i++)
                {
                    var c = drawnContacts[i];
                    var r = ContactRectAt(i);
                    bool on = c.id == selectedId;
                    bool hover = i == hoverContact;
                    DrawCapsule(g, r, c, on || hover, a, false);
                }
                if (note.contacts.Count == 0)
                    DrawText(g, "还没有对话，点右上角「＋ 新建对话」建一个", fContactSub, Alpha(Dim, (int)(255 * a)),
                        area.X, area.Y + S(12));
                DrawContactScrollHint(g, a);
                return;
            }
            for (int i = 0; i < drawnContacts.Count; i++)
                DrawCapsule(g, ContactRectAt(i), drawnContacts[i], drawnContacts[i].id == selectedId || i == hoverContact, a, true);
            DrawPill(g, ColumnsAddRect(), "＋ 新建对话",
                Alpha(skinDefault ? Accent : OnAccent, (int)(255 * a)),
                Alpha(Accent, (int)(255 * a * (skinDefault ? 0.10 : 1.0))), a, true, !skinDefault);
            DrawContactScrollHint(g, a);
        }

        /// <summary>
        /// 对话多了的滚动提示：双栏在左栏右侧画一条细的轨道 + 滑块；抽屉式在横排右边画三个点。
        /// 只在「装不下」时出现，不占额外布局。
        /// </summary>
        private void DrawContactScrollHint(Graphics g, double a)
        {
            int total = note.contacts.Count;
            int vis = VisibleContactCount;
            if (total <= vis || vis <= 0) return;
            var area = ContactsRect();
            int max = Math.Max(1, total - vis);
            double prog = Math.Max(0, Math.Min(1, contactScroll / (double)max));
            if (columns)
            {
                int x = area.Right - S(4);
                int top = area.Y + S(8);
                int h = Math.Max(S(24), area.Height - S(8) - S(44));
                int thumb = Math.Max(S(18), (int)Math.Round(h * (double)vis / total));
                int ty = top + (int)Math.Round((h - thumb) * prog);
                using (var pen = new Pen(Alpha(Ink, (int)(255 * a * 0.12)), Math.Max(1f, S(2))))
                    g.DrawLine(pen, x, top, x, top + h);
                using (var pen = new Pen(Alpha(Accent, (int)(255 * a * 0.55)), Math.Max(1f, S(2))))
                    g.DrawLine(pen, x, ty, x, ty + thumb);
            }
            else
            {
                int cx = winWidth - S(9);
                int cy = area.Y + area.Height / 2;
                for (int i = 0; i < 3; i++)
                {
                    bool on = Math.Abs(prog * 2 - i) < 0.5;
                    int rr = Math.Max(1, S(on ? 2.2f : 1.7f));
                    using (var br = new SolidBrush(Alpha(on ? Accent : Ink, (int)(255 * a * (on ? 0.75 : 0.20)))))
                        g.FillEllipse(br, cx - rr, cy + (i - 1) * S(9) - rr, rr * 2, rr * 2);
                }
            }
        }

        /// <summary>右键出现的「删除」按钮：画在对话胶囊右半边。</summary>
        private void DrawDeleteButton(Graphics g, Rectangle r, double a)
        {
            using (var path = RoundPath(r, S(8)))
            using (var br = new SolidBrush(Alpha(Color.FromArgb(214, 84, 84), (int)(255 * a))))
                g.FillPath(br, path);
            DrawTextMid(g, "删除", fContactSub, Alpha(Color.White, (int)(255 * a)), r);
        }

        private void DrawCapsule(Graphics g, Rectangle r, NoteContact c, bool on, double a, bool vertical)
        {
            var t = note.Thread(c.id);
            Color fill, edge;
            if (skinDefault)
            {
                // 浅色主题别用半透明白：白底上等于没有，看着像盖了一层白纱；改用极淡的墨色
                fill = on ? Mix(theme.bgTop, Accent, 0.16)
                    : (Light ? Mix(theme.bgBottom, theme.textMain, 0.05) : Color.FromArgb(20, 255, 255, 255));
                edge = on ? Accent : theme.edge;
            }
            else
            {
                // 皮肤：底的混合基色是 skin.panelBg（不是主题的 bgTop），选中态用强调色淡染
                fill = on ? Mix(skin.panelBg, Accent, 0.20)
                    : (Light ? Mix(skin.panelBg, theme.textMain, 0.06) : Mix(skin.panelBg, Color.White, 0.07));
                edge = on ? Accent : skin.panelEdge;
            }
            using (var path = RoundPath(r, ContactRadius(vertical)))
            {
                using (var br = new SolidBrush(Alpha(fill, (int)(255 * a))))
                    g.FillPath(br, path);
                using (var pen = new Pen(Alpha(edge, (int)(255 * a * (on ? 0.8 : 0.9))), Math.Max(1f, scale)))
                    g.DrawPath(pen, path);
            }
            var t2 = note.Thread(c.id);
            if (vertical)
            {
                // 左栏竖排：名字在胶囊里居中（右侧给未读点 / 时间留位，长名字不会被压住）
                int reserve = t2.unread > 0 ? S(30) : (t2.messages.Count > 0 ? S(56) : S(10));
                DrawTextCenter(g, c.Display(), fName, Alpha(Ink, (int)(255 * a)),
                    new Rectangle(r.X + S(6), r.Y, r.Width - reserve - S(12), r.Height));
                if (t2.unread > 0)
                    DrawBadge(g, r.Right - S(22), r.Y + r.Height / 2 - S(7), t2.unread, a);
                else if (t2.messages.Count > 0)
                {
                    string hm = Hm(t2.messages[t2.messages.Count - 1].ts);
                    DrawText(g, hm, fContactSub, Alpha(Dim, (int)(255 * a)),
                        r.Right - S(8) - Measure(g, hm, fContactSub), r.Y + r.Height / 2 + S(2));
                }
            }
            else
            {
                // 抽屉式横排：名字居中，未读点在右边单独占位
                int badge = t2.unread > 0 ? S(20) : 0;
                DrawTextCenter(g, c.Display(), fName, Alpha(Ink, (int)(255 * a)),
                    new Rectangle(r.X + S(4), r.Y, r.Width - S(8) - badge, r.Height));
                if (badge > 0) DrawBadge(g, r.Right - S(20), r.Y + r.Height / 2 - S(7), t2.unread, a);
            }
            // 右键选中的这条：右边盖上「删除」按钮
            if (!string.IsNullOrEmpty(deleteId) && deleteId == c.id)
                DrawDeleteButton(g, DeleteButtonRect(r), a);
        }

        private void DrawBadge(Graphics g, int x, int y, int count, double a)
        {
            var r = new Rectangle(x, y, S(15), S(14));
            using (var path = RoundPath(r, S(7)))
            using (var br = new SolidBrush(Alpha(Accent, (int)(255 * a))))
                g.FillPath(br, path);
            DrawText(g, count > 9 ? "9+" : count.ToString(), fTiny, Alpha(OnAccent, (int)(255 * a)), r.X + S(4), r.Y + S(7));
        }

        private void DrawBody(Graphics g, double a)
        {
            var body = BodyRect();
            if (state == PanelState.Contacts)
            {
                // 抽屉式「还没选对话」时面板收成一条，body 已经很矮：提示固定画在对话下面一行
                // （对话铺了两排时提示跟着下移，别压住第二排胶囊）
                float hintY = columns ? body.Y + body.Height / 2 - S(8) : S(104) + (DrawerRows - 1) * S(DrawerRowStepDip);
                DrawText(g, "点一个对话，看最后一条小纸条", fMsgSub, Alpha(Dim, (int)(255 * a)),
                    body.X, hintY);
                return;
            }
            if (string.IsNullOrEmpty(selectedId)) return;
            // 进对话不再整体淡入：以前刚点开时整块内容从浅色底上淡出来，像盖了一层白雾
            double st = state == PanelState.Chat ? 1.0
                : (stateAt > 0 ? Clamp01((Environment.TickCount - stateAt) / 180.0) : 1);
            double enter = EnterProgress();
            int at = (int)(255 * a * st);
            var thread = note.Thread(selectedId);
            bool room = note.IsRoom(selectedId);
            int top = body.Y;
            if (columns)
            {
                var c = note.Contact(selectedId);
                string name = Fit(g, c == null ? selectedId : c.Display(), fName, body.Width - S(112));
                DrawText(g, name, fName, Alpha(Ink, at), body.X, body.Y + S(7));
                // ID / 对话码跟着名字走：备注最多 8 个字，以前固定 +92 会被长名字压住
                bool named = c != null && !string.IsNullOrWhiteSpace(c.remark);
                string sub = !room ? "ID " + selectedId
                    : (named ? "对话码 " + selectedId : "同码的人都在这里");
                DrawText(g, sub, fTiny, Alpha(Dim, at),
                    body.X + Measure(g, name, fName) + S(10), body.Y + S(7));
                top += S(22);
            }
            else if (room && state != PanelState.History)
            {
                // 抽屉式没有名字行：对话码对话补一行「名字 + 对话码」，不然不知道自己在哪个对话里
                var rc = note.Contact(selectedId);
                string nm = rc == null ? "对话 " + selectedId : rc.Display();
                DrawText(g, Fit(g, nm, fName, body.Width - S(120)), fName, Alpha(Ink, at), body.X, body.Y + S(7));
                DrawText(g, "对话码 " + selectedId, fTiny, Alpha(Dim, at),
                    body.X + Measure(g, Fit(g, nm, fName, body.Width - S(120)), fName) + S(10), body.Y + S(7));
                top += S(22);
            }
            if (state == PanelState.Chat)
            {
                var last = LastTwo(thread);
                if (last.Count == 0)
                {
                    DrawText(g, "还没聊过，写第一句吧", fMsgSub, Alpha(Dim, (int)(255 * a)), body.X, top + S(14));
                    return;
                }
                int rowH = S(34) + S(7);
                for (int i = 0; i < last.Count; i++)
                {
                    int y = top + i * rowH;
                    int ia = at;
                    if (enter < 1)
                    {
                        if (i == last.Count - 1)
                        {
                            // 新消息：从下面浮上来
                            y += (int)Math.Round(S(12) * (1 - Ease(enter)));
                            ia = (int)(255 * a * st * enter);
                        }
                        else if (sentPrevCount >= 2 && i == last.Count - 2)
                        {
                            // 原来压在最底下的那条：往上挪一格，让出新消息的位置
                            y += (int)Math.Round(rowH * (1 - Ease(enter)));
                        }
                    }
                    DrawBubble(g, body, last[i], y, S(34), ia, room);
                }
                return;
            }
            // 历史：一屏 8 条；本地最多留 NoteSettings.MaxMessages（16）条，更早的用滚轮往上翻。
            // 固定行高 → 新消息从底部进来、老消息整体上移一位，不会整块重排。
            int rowsShow = NoteSettings.HistoryRows;
            int count = thread.messages.Count;
            int maxScroll = Math.Max(0, count - rowsShow);
            if (historyScroll > maxScroll) historyScroll = maxScroll;
            if (historyScroll < 0) historyScroll = 0;
            int endIdx = count - historyScroll;
            int startIdx = Math.Max(0, endIdx - rowsShow);
            string histPrefix = "";
            if (room && !columns)
            {
                var rc2 = note.Contact(selectedId);
                string nm2 = rc2 == null ? "对话 " + selectedId : rc2.Display();
                histPrefix = Fit(g, nm2 + " · 对话码 " + selectedId + " · ", fTiny, Math.Max(S(60), body.Width * 0.62f));
            }
            string histTitle;
            if (count <= rowsShow) histTitle = histPrefix + "最近 " + count + " 条";
            else if (historyScroll == 0) histTitle = histPrefix + "最近 " + NoteSettings.MaxMessages + " 条 · 滚轮看更早";
            else histTitle = histPrefix + "往上翻了 " + historyScroll + " 条 · 滚轮继续 / 翻到底看最新";
            DrawText(g, Fit(g, histTitle, fTiny, body.Width - S(4)), fTiny, Alpha(Dim, (int)(255 * a)), body.X, top);
            int rowTop = top + S(16);
            int rowH2 = columns ? S(34) : S(36);
            int lastIdx = count - 1;
            for (int i = startIdx; i < endIdx; i++)
            {
                double rowA = Clamp01(((Environment.TickCount - stateAt) - (i - startIdx) * 45.0) / 220.0);
                int ra = (int)(255 * a * rowA);
                if (ra <= 0) continue;
                int y = rowTop + (i - startIdx) * rowH2;
                if (enter < 1 && i == lastIdx)
                {
                    y += (int)Math.Round(S(12) * (1 - Ease(enter)));
                    ra = (int)(ra * enter);
                }
                DrawHistoryRow(g, body, thread.messages[i], y, rowH2, ra);
            }
            if (maxScroll > 0)
            {
                int shown = Math.Max(1, endIdx - startIdx);
                int trackH = Math.Max(S(20), shown * rowH2 - S(6));
                int barH = Math.Max(S(14), trackH * rowsShow / Math.Max(1, count));
                int barY = rowTop + (int)((trackH - barH) * (maxScroll - historyScroll) / (double)Math.Max(1, maxScroll));
                int bx = body.Right - S(3);
                using (var tb = new SolidBrush(Alpha(Ink, (int)(55 * a))))
                    g.FillRectangle(tb, new Rectangle(bx, rowTop, S(3), trackH));
                using (var bb = new SolidBrush(Alpha(Accent, (int)(210 * a))))
                    g.FillRectangle(bb, new Rectangle(bx, barY, S(3), barH));
            }
        }

        private List<NoteMessage> LastTwo(NoteThread t)
        {
            var list = new List<NoteMessage>();
            int n = t.messages.Count;
            if (n > 0) list.Add(t.messages[n - 1]);
            if (n > 1) list.Insert(0, t.messages[n - 2]);
            return list;
        }

        /// <summary>
        /// 聊天气泡：文字在气泡里垂直居中，时间放到气泡外面（自己发的时间在左、收到的在右）。
        /// 以前文字画在 y+12（气泡 34 高偏上）、时间塞在气泡右下角，长文字还会和时间撞一起，
        /// 极简双栏看着就是「气泡和文字错位」。
        /// </summary>
        /// <summary>
        /// 气泡里显示的文字：对话码对话里，别人发来的消息前面带发送者名字
        /// —— 有昵称用昵称，没有昵称退回 5 位 ID；每个人的 ID 都保留着，看到就能直接按 ID 建一对一对话。
        /// </summary>
        internal string BubbleText(NoteMessage m, bool room)
        {
            if (m == null) return "";
            string raw = m.text ?? "";
            if (room && !m.IsMine(note.id)) return SenderName(m) + "：" + raw;
            return raw;
        }

        /// <summary>历史行左边的「谁」：个人对话是你 / 对方；对话码对话显示发送者昵称（没有昵称就用 5 位 ID）。</summary>
        internal string WhoLabel(NoteMessage m)
        {
            if (m == null) return "";
            if (m.IsMine(note.id)) return "你";
            return note.IsRoom(selectedId) ? SenderName(m) : "对方";
        }

        /// <summary>对话码对话里一条消息的发送者怎么称呼：优先消息里带的昵称，其次本机给这个 ID 的备注 / 昵称，最后 ID。</summary>
        internal string SenderName(NoteMessage m)
        {
            if (m == null) return "";
            if (!string.IsNullOrEmpty(m.fromNick)) return m.fromNick;
            var c = note.Contact(m.from);
            if (c != null && !string.IsNullOrWhiteSpace(c.remark)) return c.remark;
            if (c != null && !string.IsNullOrWhiteSpace(c.nick)) return c.nick;
            return "ID " + m.from;
        }

        private int DrawBubble(Graphics g, Rectangle body, NoteMessage m, int y, int h, int a, bool room = false)
        {
            bool mine = m.IsMine(note.id);
            int gap = S(9);
            int timeW = (int)Math.Ceiling(Measure(g, "00:00", fMsgSub));
            int maxBubble = Math.Max(S(48), body.Width - timeW - gap);
            int padX = S(12);
            string text = Fit(g, BubbleText(m, room), fMsg, Math.Max(S(24), maxBubble - padX * 2));
            float tw = Measure(g, text, fMsg);
            int w = (int)Math.Min(maxBubble, tw + padX * 2);
            int x = mine ? body.Right - w : body.X;
            var r = new Rectangle(x, y, w, h);
            Color bubbleFill, bubbleInk;
            if (skinDefault)
            {
                // 收到的气泡：浅色主题用淡墨色，不用半透明白（白底上像一层白纱/白遮罩）
                bubbleFill = mine ? Mix(Accent, theme.bgBottom, 0.78)
                    : (Light ? Mix(theme.bgBottom, theme.textMain, 0.07) : Color.FromArgb(18, 255, 255, 255));
                bubbleInk = mine ? Ink : Muted;
            }
            else
            {
                // 皮肤：收到 / 发出直接取 skin.bubbleIn/bubbleOut 和配套文字色
                bubbleFill = mine ? skin.bubbleOut : skin.bubbleIn;
                bubbleInk = mine ? skin.bubbleOutInk : skin.bubbleInInk;
            }
            using (var path = RoundPath(r, BubbleRadius))
            {
                using (var br = new SolidBrush(Alpha(bubbleFill, a)))
                    g.FillPath(br, path);
                if (!skinDefault)
                {
                    // 皮肤气泡带一圈细边：收消息用 panelEdge（霓虹换青色描边），
                    // 仪表 / 霓虹这种半透明强调色发出的消息再补一圈强调色边
                    Color edge = Color.Empty;
                    int ea = 0;
                    if (!mine)
                    {
                        edge = skin.neonEdge ? skin.accent2 : skin.panelEdge;
                        ea = skin.neonEdge ? (int)(a * 0.30) : (int)(a * 0.90);
                    }
                    else if (skin.neonEdge || skin.screws)
                    {
                        edge = Accent;
                        ea = (int)(a * 0.45);
                    }
                    if (edge.A > 0 && ea > 0)
                        using (var pen = new Pen(Alpha(edge, ea), Math.Max(1f, scale)))
                            g.DrawPath(pen, path);
                }
            }
            // DrawText 的 centerY 是按字体 cell 高度算的，中文 / 数字字形在 cell 里偏上，
            // 直接放在几何中心会显得「字偏上」；这里补一个视觉居中偏移（按字号比例）。
            float cyText = r.Y + r.Height / 2f + S(4);
            DrawText(g, text, fMsg, Alpha(bubbleInk, a), r.X + padX, cyText);
            string hm = Hm(m.ts);
            float hw = Measure(g, hm, fMsgSub);
            float hx = mine ? r.X - gap - hw : r.Right + gap;
            DrawText(g, hm, fMsgSub, Alpha(Dim, a), hx, r.Y + r.Height / 2f + S(3));
            return h;
        }

        private void DrawHistoryRow(Graphics g, Rectangle body, NoteMessage m, int y, int rowH, int a)
        {
            bool mine = m.IsMine(note.id);
            bool room = note.IsRoom(selectedId);
            if (!columns)
            {
                if (y + rowH - S(6) > body.Bottom) return;
                int cy = y + rowH / 2;
                // 对话码对话里显示发送者 ID（5 位数字），个人对话还是「你 / 对方」
                DrawText(g, WhoLabel(m), fMsgSub, Alpha(mine ? Accent : Muted, a), body.X, cy);
                DrawText(g, Fit(g, m.text ?? "", fMsg, body.Width - S(120)), fMsg, Alpha(Ink, a), body.X + S(34), cy);
                string hm = Hm(m.ts);
                DrawText(g, hm, fMsgSub, Alpha(Dim, a), body.Right - S(4) - Measure(g, hm, fMsgSub), cy);
                using (var pen = new Pen(Alpha(Ink, a / 12), 1f))
                    g.DrawLine(pen, body.X, y + rowH - S(2), body.Right, y + rowH - S(2));
                return;
            }
            // C：气泡历史（和聊天气泡同一套：文字居中、时间在气泡外侧）
            DrawBubble(g, body, m, y + S(3), S(32), a, room);
        }

        private void DrawInput(Graphics g, double a)
        {
            if (state != PanelState.Chat && state != PanelState.History) return;
            var r = InputRect();
            using (var path = RoundPath(r, InputRadius))
            {
                using (var br = new SolidBrush(Alpha(FieldBg, (int)(255 * a))))
                    g.FillPath(br, path);
                using (var pen = new Pen(Alpha(FieldBorder, (int)(255 * a)), Math.Max(1f, scale)))
                    g.DrawPath(pen, path);
            }
            DrawText(g, string.IsNullOrEmpty(composeText) ? "写一句，回车发送…" : composeText, fField,
                Alpha(string.IsNullOrEmpty(composeText) ? Dim : Ink, (int)(255 * a)), r.X + S(12), r.Y + r.Height / 2);
            var send = new Rectangle(r.Right - S(34), r.Y + S(5), S(28), S(28));
            using (var path = RoundPath(send, ButtonRadius))
            {
                // 霓虹发出的按钮带一圈辉光（设计里的 sendGlow），其它皮肤保持干净的实心块
                if (!skinDefault && skin.neonEdge)
                    for (int i = 2; i >= 1; i--)
                        using (var pen = new Pen(Alpha(Accent, (int)(255 * a * 0.12 * i)), Math.Max(2f, S(2 + i * 2))))
                            g.DrawPath(pen, path);
                using (var br = new SolidBrush(Alpha(Accent, (int)(255 * a))))
                    g.FillPath(br, path);
            }
            DrawPlane(g, new RectangleF(send.X + S(7), send.Y + S(7), S(14), S(14)), Alpha(OnAccent, (int)(255 * a)));
            if (!string.IsNullOrEmpty(status) && Environment.TickCount - statusAt < 6000)
                DrawText(g, status, fTiny, Alpha(Color.FromArgb(239, 138, 96), (int)(255 * a)),
                    r.X + S(2), r.Y - S(14));
        }

        private void DrawAddSheet(Graphics g, double a)
        {
            int w = winWidth;
            int h = (int)heightNow;
            using (var path = RoundPath(new Rectangle(0, 0, w, h), NoteRadius))
                PaintPanelBackdrop(g, path, new Rectangle(0, 0, w, Math.Max(1, h)), a);
            DrawText(g, "＋ 新建对话", fHeading, Alpha(Ink, (int)(255 * a)), S(16), S(28));
            Button(g, new Rectangle(w - S(46), S(14), S(30), S(30)), "×", false, a);

            DrawText(g, "对方的 5 位 ID / 5 位对话码", fTiny, Alpha(Dim, (int)(255 * a)), S(16), S(48));
            FieldBox(g, new Rectangle(S(16), S(56), w - S(32), S(40)), string.IsNullOrEmpty(addId) ? "5 位数字或任意 5 位字符" : addId,
                Alpha(addId.Length == 0 ? Dim : Accent, (int)(255 * a)), false, a);
            Button(g, new Rectangle(S(16), S(104), S(78), S(30)), "查在线", false, a);
            string found = addProbing ? "查询中…" : addOnline == null ? "" : addOnline == true ? "● 在线" : "○ 未在线";
            bool showStatus = !string.IsNullOrEmpty(status) && Environment.TickCount - statusAt < 6000;
            if (showStatus)
            {
                // 反馈统一显示在「查在线」右边这一行，不再挤在底部提示行里
                DrawText(g, Fit(g, status, fContactSub, w - S(104) - S(18)), fContactSub,
                    Alpha(Color.FromArgb(214, 120, 80), (int)(255 * a)), S(104), S(119));
            }
            else if (!string.IsNullOrEmpty(found))
            {
                DrawText(g, found, fContactSub, Alpha(addOnline == true ? Color.FromArgb(110, 209, 161) : Muted, (int)(255 * a)),
                    S(104), S(119));
            }

            DrawText(g, "名字（最多 8 个字，可空）", fTiny, Alpha(Dim, (int)(255 * a)), S(16), S(142));
            FieldBox(g, new Rectangle(S(16), S(150), w - S(32), S(40)), string.IsNullOrEmpty(addRemark) ? "比如：老李 / 项目组" : addRemark,
                Alpha(string.IsNullOrEmpty(addRemark) ? Dim : Ink, (int)(255 * a)), false, a);

            Button(g, new Rectangle(S(16), h - S(58), w - S(32), S(40)), "创建对话", true, a);
            DrawText(g, "5 位数字 = 和这个人单独聊；其它 5 位字符 = 对话码",
                fTiny, Alpha(Dim, (int)(255 * a)), S(16), h - S(84));
            DrawText(g, "别人输入同一个码，就进同一个对话。",
                fTiny, Alpha(Dim, (int)(255 * a)), S(16), h - S(70));
        }

        private void FieldBox(Graphics g, Rectangle r, string text, Color color, bool mono, double a)
        {
            using (var path = RoundPath(r, FieldRadius))
            {
                using (var br = new SolidBrush(Alpha(FieldBg, (int)(255 * a)))) g.FillPath(br, path);
                using (var pen = new Pen(Alpha(FieldBorder, (int)(255 * a)), Math.Max(1f, scale))) g.DrawPath(pen, path);
            }
            Font f = mono ? FontMono() : fField;
            DrawText(g, text, f, color, r.X + S(12), r.Y + r.Height / 2);
        }

        private Font FontMono()
        {
            return fGlyph;
        }

        private void Button(Graphics g, Rectangle r, string text, bool primary, double a)
        {
            using (var path = RoundPath(r, ButtonRadius))
            {
                var fill = primary ? Accent
                    : (Light ? Mix(theme.bgBottom, theme.textMain, 0.05) : Color.FromArgb(16, 255, 255, 255));
                using (var br = new SolidBrush(Alpha(fill, (int)(255 * a))))
                    g.FillPath(br, path);
                if (!primary)
                    using (var pen = new Pen(Alpha(FieldBorder, (int)(255 * a)), Math.Max(1f, scale))) g.DrawPath(pen, path);
            }
            DrawTextMid(g, text, fButton, Alpha(primary ? OnAccent : Ink, (int)(255 * a)), r);
        }

        private void DrawPill(Graphics g, Rectangle r, string text, Color ink, Color bg, double a, bool dashed, bool solid = false)
        {
            Color border;
            if (skinDefault) border = Alpha(Accent, (int)(255 * a * 0.55));
            else if (solid) border = Alpha(OnAccent, (int)(255 * a * 0.16));
            else if (dashed) border = Alpha(Accent, (int)(255 * a * 0.5));
            else border = Alpha(skin.panelEdge, (int)(255 * a * 0.9));
            using (var path = RoundPath(r, PillRadius(r)))
            {
                if (bg.A > 0)
                    using (var br = new SolidBrush(bg)) g.FillPath(br, path);
                using (var pen = new Pen(border, Math.Max(1f, scale)))
                {
                    if (dashed) pen.DashStyle = DashStyle.Dash;
                    g.DrawPath(pen, path);
                }
            }
            DrawTextMid(g, text, fContact, ink, r);
        }

        // ---------------------------------------------------------------- 文字 / 形状

        private static float Ascent(Font f, Graphics g)
        {
            var ff = f.FontFamily;
            return ff.GetCellAscent(f.Style) * f.Size * g.DpiY / 72f / ff.GetEmHeight(f.Style);
        }

        private static float Measure(Graphics g, string s, Font f)
        {
            return g.MeasureString(s ?? "", f, 100000, StringFormat.GenericTypographic).Width;
        }

        private static string Fit(Graphics g, string s, Font f, float maxW)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (Measure(g, s, f) <= maxW) return s;
            string cut = s;
            while (cut.Length > 1 && Measure(g, cut + "…", f) > maxW) cut = cut.Substring(0, cut.Length - 1);
            return cut + "…";
        }

        private static void DrawText(Graphics g, string text, Font f, Color color, float x, float centerY)
        {
            if (string.IsNullOrEmpty(text)) return;
            float asc = Ascent(f, g);
            using (var b = new SolidBrush(color))
                g.DrawString(text, f, b, x, centerY - asc, StringFormat.GenericTypographic);
        }

        private static void DrawTextMid(Graphics g, string text, Font f, Color color, Rectangle r)
        {
            if (string.IsNullOrEmpty(text)) return;
            string fit = Fit(g, text, f, r.Width - 8);
            float w = Measure(g, fit, f);
            float asc = Ascent(f, g);
            using (var b = new SolidBrush(color))
                g.DrawString(fit, f, b, r.X + (r.Width - w) / 2f, r.Y + r.Height / 2f - asc, StringFormat.GenericTypographic);
        }

        /// <summary>水平 + 垂直居中画一行字（联系人名字用；补 S(2) 视觉居中偏移，中文不会显得贴着上沿）。</summary>
        private void DrawTextCenter(Graphics g, string text, Font f, Color color, Rectangle r)
        {
            if (string.IsNullOrEmpty(text) || r.Width <= 0) return;
            string fit = Fit(g, text, f, r.Width);
            float w = Measure(g, fit, f);
            DrawText(g, fit, f, color, r.X + (r.Width - w) / 2f, r.Y + r.Height / 2f + S(2));
        }

        private void DrawPlane(Graphics g, RectangleF box, Color color)
        {
            using (var pen = new Pen(color, Math.Max(1f, 1.7f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                float k = box.Width / 24f;
                var pts = new PointF[]
                {
                    new PointF(box.X + 21 * k, box.Y + 3 * k),
                    new PointF(box.X + 3.6f * k, box.Y + 10.3f * k),
                    new PointF(box.X + 9.7f * k, box.Y + 13.7f * k),
                    new PointF(box.X + 21 * k, box.Y + 3 * k)
                };
                g.DrawLines(pen, pts);
                g.DrawLine(pen, box.X + 9.7f * k, box.Y + 13.7f * k, box.X + 11.4f * k, box.Y + 19.4f * k);
            }
        }

        private static string Hm(long ts)
        {
            var t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ts).ToLocalTime();
            return t.ToString("HH:mm");
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

        // ---------------------------------------------------------------- 分层窗

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
            if (wheelFilter != null)
            {
                Application.RemoveMessageFilter(wheelFilter);
                wheelFilter = null;
            }
            foreach (var f in new Font[] { fTitle, fHeading, fSub, fContact, fContactSub, fMsg, fMsgSub, fTiny, fField, fButton, fGlyph, fName })
                if (f != null) f.Dispose();
            if (frame != null) { frame.Dispose(); frame = null; }
            base.OnFormClosed(e);
        }

        /// <summary>
        /// 贴在输入框上的小编辑窗。非模态（ShowDialog 会禁掉父面板，导致「点了输入框以后别的按钮全点不动」），
        /// 输入实时回传给面板；Enter 收起并提交，Esc 还原原值，点到别处也收起。
        /// 圆角 + 文字可居中 + 字号可调：以前是直角矩形、9pt 左对齐，和面板里的圆角输入框对不上。
        /// </summary>
        private class NoteField : Form
        {
            private const int WmGetMinMaxInfo = 0x0024;
            [StructLayout(LayoutKind.Sequential)]
            private struct Point2 { public int x, y; }
            [StructLayout(LayoutKind.Sequential)]
            private struct MinMaxInfo
            {
                public Point2 ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
            }

            private readonly TextBox box;
            private readonly string original;
            private readonly Color bg, accent;
            private readonly float scale;
            private readonly int radiusDip;      // 编辑窗圆角（DIP）：跟皮肤的输入框圆角走
            private int radius;
            private Region shape;
            public Action<string> ValueChanged;   // 每次输入回调（面板里实时更新 composeText / addId / addRemark）
            public Action Submitted;              // Enter 收起后执行（发消息 / 查在线 / 改备注）
            public Func<bool> SubmitKeepsOpen;    // 非空：回车不收起；返回 true 表示已发出，清空后接着输入
            public bool CommitOnBlur;             // 点到别处收起时也提交（改备注用）
            public bool ClosedByKey;              // 由 Enter / Esc 主动收起
            public bool Cancelled;

            public int MaxLength { set { box.MaxLength = value; } }

            public NoteField(string value, Rectangle screen, Color bg, Color fg,
                Color accent, float scale, bool topMost, bool centered, float fontPt, int radiusDip = 11)
            {
                original = value ?? "";
                this.bg = bg;
                this.accent = accent;
                this.scale = scale;
                this.radiusDip = radiusDip;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = topMost;
                BackColor = bg;
                Bounds = screen;
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

                radius = Math.Max(2, Math.Min(Sd(this.radiusDip), Math.Min(Width, Height) / 2));
                RebuildShape();

                box = new TextBox();
                box.BorderStyle = BorderStyle.None;
                box.BackColor = bg;
                box.ForeColor = fg;
                box.Font = UiFont.Make(fontPt, FontStyle.Regular);
                box.Text = original;
                if (centered) box.TextAlign = HorizontalAlignment.Center;
                box.TextChanged += delegate { if (ValueChanged != null) ValueChanged(box.Text); };
                box.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        if (SubmitKeepsOpen != null)
                        {
                            // 聊天输入框：发出去不清场，清空文字接着打下一句（不用再点一次输入框）
                            if (SubmitKeepsOpen())
                            {
                                if (box.TextLength > 0) box.Text = "";
                                box.SelectionStart = 0;
                            }
                            return;
                        }
                        ClosedByKey = true;
                        Close();
                    }
                    else if (e.KeyCode == Keys.Escape)
                    {
                        e.SuppressKeyPress = true;
                        Cancelled = true;
                        ClosedByKey = true;
                        if (box.Text != original) box.Text = original;   // 触发 TextChanged，把面板里的值也还原
                        Close();
                    }
                };
                Controls.Add(box);
                LayoutBox();
                Deactivate += delegate { if (!IsDisposed && Visible) Close(); };
            }

            /// <summary>
            /// 允许窗口比系统最小跟踪尺寸更小：改备注的编辑窗要正好盖在联系人名字上（抽屉式胶囊只有 86×30 设计 px），
            /// 默认会被系统最小窗宽 / 窗高钳大，看起来像「输入框变大了」。
            /// </summary>
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmGetMinMaxInfo)
                {
                    try
                    {
                        var mmi = (MinMaxInfo)Marshal.PtrToStructure(m.LParam, typeof(MinMaxInfo));
                        mmi.ptMinTrackSize.x = 1;
                        mmi.ptMinTrackSize.y = 1;
                        Marshal.StructureToPtr(mmi, m.LParam, false);
                        return;
                    }
                    catch { }
                }
                base.WndProc(ref m);
            }

            private int Sd(float px) { return (int)Math.Round(px * scale); }

            /// <summary>
            /// 按窗口实际客户区重建圆角 Region。Show 之后必须再做一次：无边框窗口有系统最小窗高
            /// （150% 下 56px），申请的 51 高会被钳上去，Region 若还按旧尺寸建，底边会被裁掉、描边缺一条。
            /// </summary>
            private void RebuildShape()
            {
                int w = Math.Max(2, ClientSize.Width), h = Math.Max(2, ClientSize.Height);
                radius = Math.Max(2, Math.Min(Sd(radiusDip), Math.Min(w, h) / 2));
                Region old = shape;
                using (var path = RoundPath(new Rectangle(0, 0, w, h), radius))
                    shape = new Region(path);
                Region = shape;
                if (old != null) old.Dispose();
            }

            /// <summary>发送成功后清空输入框（点发送按钮那条路径编辑窗还开着，也要清成占位提示）。</summary>
            public void ClearText()
            {
                if (box.TextLength > 0) box.Text = "";
                box.SelectionStart = 0;
            }

            /// <summary>文本框垂直居中（单行 TextBox 的高度由字体决定，不能直接 Dock 顶到上边）。</summary>
            private void LayoutBox()
            {
                int pen = Math.Max(1, (int)Math.Round(scale));
                int inset = Math.Max(pen + 2, Sd(10));
                int h = box.PreferredHeight;
                if (h <= 0) h = Math.Max(8, Sd(18));
                h = Math.Min(h, Math.Max(6, ClientSize.Height - 4));
                box.SetBounds(inset, (ClientSize.Height - h) / 2, Math.Max(8, ClientSize.Width - inset * 2), h);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(bg)) g.FillRectangle(br, ClientRectangle);
                float penW = Math.Max(1f, scale);
                int inset = (int)Math.Max(1, Math.Round(penW / 2.0));
                using (var path = RoundPath(new Rectangle(inset, inset,
                    Math.Max(2, ClientSize.Width - inset * 2), Math.Max(2, ClientSize.Height - inset * 2)),
                    Math.Max(2, radius - inset)))
                using (var pen = new Pen(accent, penW))
                    g.DrawPath(pen, path);
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                RebuildShape();       // 实际高度可能被系统最小窗高钳过，按真实客户区重画圆角
                LayoutBox();
                box.Focus();
                box.SelectAll();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (shape == null) return;
                RebuildShape();
                LayoutBox();
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                if (shape != null) { Region = null; shape.Dispose(); shape = null; }
                base.OnFormClosed(e);
            }
        }
    }
}
