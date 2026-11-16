using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WorkCountdown
{
    public class WidgetForm : Form, ISyncConfigSink, ISettingsApplyTarget
    {
        // 96 DPI 下的设计尺寸，实际按 scale 放大
        private const int DesignExpandedW = 280;
        private const int DesignCollapsedW = 224;
        private const int DesignCollapsedH = 68;

        // 卡片内竖直节奏：一行一行往下排，行与行之间只用这一个行距，卡片高度按内容算出来
        private const int CardTopDip = 12;      // 第一行离卡片顶部
        private const int CardBottomDip = 12;   // 最后一行离卡片底部
        private const int HeadHDip = 20;        // 状态行
        private const int LineDip = 18;         // 普通文字行
        private static int BigDip = 34;          // 大号倒计时：按字形高度校准过，和别的行看起来一样宽
        private const int BarDip = 4;           // 进度条
        private const int FootDip = 16;         // 最后一行小字
        /// <summary>行距（DIP）。改这一个值，整张卡片就等距重排。</summary>
        internal static int RowGapDip = 6;   // 行距（DIP）：每两行之间都走这个值，改这一个数整张卡片就等距重排

        private readonly SettingsStore store;
        private Settings settings;
        private readonly LarkClient lark;
        private readonly SyncEngine sync;
        private readonly SettingsApplyCoordinator apply;
        private readonly SettingsCommitCoordinator commit;

        private CardStatus status;
        private ThemeDef theme;
        private float scale = 1f;

        /// <summary>
        /// 当前整体界面主题（皮肤）。默认皮肤继续走原有画法（39 套主题 + 背景风格），
        /// 其它五套按 SkinDef 的字段画圆角 / 描边 / 高光 / 进度条 / 发光 / 装饰。
        /// 查表只有 6 项，直接算，免得设置改完没刷新。
        /// </summary>
        private SkinDef Skin { get { return Skins.Current(settings.ui); } }

        private DateTime lastDay = DateTime.Today;

        /// <summary>考勤展示快照：只由同步模块接受结果时赋值，窗口不再自己拉取或合并。</summary>
        private AttendanceSnapshot snapshot;

        private bool lastFetchFailed
        {
            get
            {
                AttendanceSnapshot state = snapshot;
                return state != null && !state.ok;
            }
        }

        private bool collapsed;
        private readonly System.Windows.Forms.Timer tick;
        private readonly System.Windows.Forms.Timer poll;
        private readonly System.Windows.Forms.Timer autoCollapse;
        private readonly System.Windows.Forms.Timer animT;
        private readonly System.Windows.Forms.Timer calPoll;
        private readonly System.Windows.Forms.Timer olPoll;
        private readonly System.Windows.Forms.Timer remindT;
        private readonly System.Windows.Forms.Timer flowT;
        private readonly System.Windows.Forms.Timer topT;

        // 分层窗口：整帧画在 32 位位图上，圆角由像素 alpha 收边（Region 剪出来的是硬锯齿边）
        private Bitmap frame;
        private byte frameAlpha = 240;

        // 日程提醒：日程快照（飞书 / Outlook 各来源的合并、去重、过滤）由 SyncEngine 统一拥有
        private readonly ReminderEngine engine = new ReminderEngine();
        private bool reminding;
        private DateTime remindAt;
        private ReminderEvent remindEvent;
        private const int RemindInMs = 1000;    // 两块色块先后扫入（A 0-300ms，B 260-660ms）
        private int RemindHoldMs { get { return Math.Max(3, Math.Min(20, settings.ui.reminderHoldSeconds)) * 1000; } } // // 完全覆盖
        private const int RemindOutMs = 960;   // 两块色块扫出

        // 常驻「该下班啦」：到点没打卡就一直在，每 5 分钟重播一次入场动画
        private const int OffAlertAnimMs = 1100;   // 只有这段需要 30fps，其余时间静止不耗 CPU
        private DateTime remindUntil = DateTime.MaxValue;        // 常驻到什么时候（时段结束）
        private bool remindExiting;
        private DateTime remindExitAt;
        private RectangleF rectDismiss;                          // 提醒卡片右上角的关闭按钮
        // A = 第一块（整卡），B = 第二块（压在底部一半），B 稍晚到、稍晚走
        private const double RemindAMs = 300;
        private const double RemindBDelayMs = 260;
        private const double RemindBMs = 380;
        private const double RemindOutAMs = 300;
        private const double RemindOutBDelayMs = 150;
        private const double RemindOutBMs = 360;
        private Font fontRemindBig;
        private Font fontRemindSmall;
        private Font fontRemindTitle;
        private Font fontRemindMeta;
        private Font fontRemindTime;
        private Font fontCollapsedText;

        // 全屏提醒：同一扇分层窗临时铺满整块屏，按 1/DPI 的分辨率渲染再交给 ULW 放大
        private bool screenReminder;
        private Rectangle screenRestoreBounds;
        private bool screenRestoreCollapsed;
        private Rectangle screenBounds;
        private int screenW, screenH, screenRenderW, screenRenderH;
        private double screenLayout;
        private int screenTipX, screenTipY;
        private byte screenAlphaBefore;
        private Bitmap screenCardUnder;
        private ScreenReminderForm screenForm;
        private Font screenKindFont, screenTitleFont, screenCountFont, screenWhenFont,
            screenPlaceFont, screenDetailFont, screenHintFont;

        // 警戒带之后的抽屉卡（方案 A：实心黄 + 同色系三阶文字）
        private DrawerForm drawer;
        private bool drawerDismissed;      // 用户点掉了抽屉，这一条提醒不再自动弹
        private int tapePhase;             // 0 空闲 / 1 等卡片展开后放全屏 / 2 等警戒带消失后开抽屉
        private int tapeAt;                // 上面两个阶段的触发时刻（Environment.TickCount）
        private bool tapeActive;           // 本次提醒走警戒带：卡片不做覆盖
        private readonly System.Windows.Forms.Timer tapeT;

        private bool animating;
        private int animFromW, animFromH, animFromX, animToW, animToH, animToX;
        private int animStart;
        private int animDuration = AnimMs;
        private const int AnimMs = 150;
        // 警戒带那一路按 HTML 预览的节奏：展开 0.9s、收回 0.7s（普通点开/收起仍是 150ms）
        private const int TapeExpandMs = 400;
        private const int TapeCollapseMs = 400;
        // 展开后的错峰：主视觉先出，其余行每行相隔 55ms，每行 190ms 淡入
        // 进度条流光跑一个来回的时间
        private const int FlowPeriodMs = 2600;
        // 流光在一圈两端的淡入 / 淡出占比：走到尽头要慢慢变淡，不能「啪」一下消失
        private const double FlowFadeIn = 0.18;
        private const double FlowFadeOut = 0.30;
        // 探针用：>= 0 时固定流光相位（真实运行永远是 -1，按 TickCount 自己走）
        private double flowPhase = -1;
        // 进度条整体透明度：展开动画里要和时间一起淡入
        private double barAlpha = 1;
        private const int StaggerGapMs = 55;
        private const int StaggerTotalMs = 6 * StaggerGapMs + 200;
        private bool expandAnim;
        private int expandStart;

        private int downAt;
        private bool dragging;
        private bool moved;
        private Point dragOrigin;
        private Point formOrigin;

        private readonly ContextMenuStrip menu;
        private BubbleMenuForm bubbleMenu;
        private NotePanelForm notePanel;
        private NoteRelay noteRelay;
        private int noteFlashAt;
        private int noteBlinkAt;                  // 未读左上角快闪：这一轮两次快闪的起点（0 = 没在闪）
        private int noteBlinkLast;                // 上一次快闪的起点（每 5 秒一次）
        private CardGlowForm cardGlow;
        private NotifyIcon tray;

        // 检查更新（气泡菜单里的「检查更新」胶囊）：状态留在 Widget 上，
        // 菜单关掉再打开还能看到「已下载 / 有新版本」；网络和下载都在后台线程。
        private UpdateRelease updateRelease;
        private string updateExePath;
        private bool updateBusy;
        private UpdatePhase updatePhase = UpdatePhase.Idle;
        private string updateDetail = "";
        private int updatePercent;
        private readonly ToolTip reminderTip = new ToolTip { InitialDelay = 800, ReshowDelay = 200, AutoPopDelay = 20000, ShowAlways = true };

        private Font fontBig;
        private Font fontBigText;
        private Font fontStatus;
        private Font fontTiny;
        private Font fontSmallBold;
        private Font fontSmall;
        private Font fontCollapsedNum;
        private Font fontCollapsedLabel;

        private readonly StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private readonly StringFormat sfNear = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        private readonly StringFormat sfFar = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        private Rectangle rectGear, rectRefresh, rectChevron, rectSalary, rectNote;
        // 工资面板：单击 ¥ 展开 / 收起（v3.2 起不再支持双击固定）
        private bool salaryOpen;
        // 工资面板展开 / 收起的按行浮现动画
        private bool salaryAnimating;
        private bool salaryAnimOpen;
        private int salaryAnimStart;
        private int salaryAnimDuration;
        private int salaryAnimFromH;
        private int salaryAnimToH;
        private const int SalaryPanelDip = 116;
        private Font fontSalary;
        private Font fontGlyphSalary;

        public WidgetForm(SettingsStore store, Settings settings)
            : this(store, settings, null)
        {
        }

        /// <summary>测试 / 宿主可以注入同步模块；生产构造走上面那个重载，使用真实 adapter。</summary>
        internal WidgetForm(SettingsStore store, Settings settings, SyncEngine sync)
        {
            this.store = store;
            this.settings = settings;
            this.lark = new LarkClient(store);
            settings.NormalizeSchedules();      // 老配置 / 直接 new 出来的 Settings 也要有固定日程
            settings.NormalizeCustomThemes();
            if (settings.note == null) settings.note = new NoteSettings();
            settings.note.Normalize();
            this.sync = sync != null ? sync : new SyncEngine(
                new ThreadPoolSyncScheduler(), new ControlSyncDispatcher(this),
                new LarkAttendanceReader(this.lark), new LiveCalendarSourceReader(this.lark),
                delegate { return DateTime.Now; });
            this.sync.ConfigSink = this;
            this.sync.Published += OnSyncPublished;
            this.apply = new SettingsApplyCoordinator(this);
            this.commit = new SettingsCommitCoordinator(store, this.apply);
            this.theme = Skins.ResolveTheme(settings.ui);

            using (var screen = Graphics.FromHwnd(IntPtr.Zero))
            {
                scale = screen.DpiX / 96f;
            }
            if (scale < 0.75f) scale = 0.75f;
            if (scale > 3f) scale = 3f;

            // 分层窗口：画面是我们自己渲染到 frame 位图再 UpdateLayeredWindow 推上去的，
            // WinForms 这条 WM_PAINT 只负责「该出一帧了」。**千万不要开双缓冲**
            // （OptimizedDoubleBuffer / DoubleBuffered = true）：在 WS_EX_LAYERED 窗口上
            // WinForms 会因此根本不调用 OnPaint，于是永远没有第一帧——
            // 窗口全透明、连点击都穿透过去，看起来就是「看不见、点不动」。
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Text = "上班工具箱";
            MinimumSize = new Size(S(DesignCollapsedW), S(DesignCollapsedH));

            // 工资面板每次启动都从收起状态开始（v3.2 起不再持久化展开 / 固定状态）
            salaryOpen = false;

            menu = BuildMenu();
            // 卡片右键走自己的胶囊菜单（BuildMenu 只留给托盘）；不挂 ContextMenuStrip，
            // 否则系统菜单会先弹出来，把右键抢走。

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 1000;
            tick.Tick += delegate
            {
                if (lastDay != DateTime.Today)
                {
                    lastDay = DateTime.Today;
                    sync.ResetAttendance();
                    snapshot = null;
                    RequestRefresh(false);
                    RequestCalendar(false);
                }
                Recompute();
                CheckSticky();
                AdvanceReminders(DateTime.Now);
                if (drawer != null) drawer.RefreshText();
                MaybeBlinkUnread();
            };

            poll = new System.Windows.Forms.Timer();
            poll.Tick += delegate { RequestRefresh(false); };

            autoCollapse = new System.Windows.Forms.Timer();
            autoCollapse.Tick += delegate { autoCollapse.Stop(); MaybeAutoCollapse(); };

            animT = new System.Windows.Forms.Timer();
            animT.Interval = 15;
            animT.Tick += delegate { StepAnim(); };

            calPoll = new System.Windows.Forms.Timer();
            calPoll.Interval = Math.Max(3, settings.calendarPollMinutes) * 60 * 1000;
            calPoll.Tick += delegate { RequestCalendar(false); };

            // Outlook / Exchange：.ics 订阅，和飞书日程同一个轮询间隔
            olPoll = new System.Windows.Forms.Timer();
            olPoll.Interval = Math.Max(3, settings.calendarPollMinutes) * 60 * 1000;
            olPoll.Tick += delegate { RequestOutlook(false); };

            remindT = new System.Windows.Forms.Timer();
            remindT.Interval = 16;

            // 进度条上的流光要一直走：只在窗口可见、没在放提醒动画时按 18fps 重画。
            // 工资面板开着时也靠这一路刷新，后三位小数才会每秒高速跳动；关了动效也保留。
            flowT = new System.Windows.Forms.Timer();
            flowT.Interval = 55;
            flowT.Tick += delegate
            {
                if (!Visible) return;
                if (reminding || animating || expandAnim) return;
                bool salaryTick = salaryOpen;
                if (settings.ui.reducedMotion || SystemInformation.HighContrast)
                {
                    if (salaryTick || NoteRimActive()) Invalidate();
                    return;
                }
                // 流动亮带只有默认 / 云母 / 霓虹这三套有；别的皮肤静止时不用 18fps 空转
                if (!BarAnimated && !salaryTick && !NoteRimActive()) return;
                Invalidate();
            };
            remindT.Tick += delegate { StepReminder(); };

            // 置顶看门狗：Windows 有时会把 WS_EX_TOPMOST 摘掉（换显示器 / Explorer 重启 /
            // 别的顶层窗口抢 z 序），每 2 秒看一眼，掉了就补回来。已经是置顶时不动 z 序，
            // 免得把设置窗、右键菜单压下去。
            topT = new System.Windows.Forms.Timer();
            topT.Interval = 2000;
            topT.Tick += delegate { KeepTopMost(); };

            // 警戒带流程的看门狗：等卡片展开、等警戒带消失 0.3s 后拉抽屉
            tapeT = new System.Windows.Forms.Timer();
            tapeT.Interval = 50;
            tapeT.Tick += delegate { StepTapeFlow(); };

            apply.Apply(settings);
            BackColor = theme.bgBottom;
            collapsed = settings.ui.edge != "free";
            Size = collapsed ? new Size(S(DesignCollapsedW), S(DesignCollapsedH)) : new Size(S(DesignExpandedW), ExpandedH);
            PlaceInitial();
            ApplyRegion();
        }

        /// <summary>把 96 DPI 的设计坐标换算成当前屏幕的物理像素。</summary>
        private int S(float value)
        {
            return (int)Math.Round(value * scale);
        }

        /// <summary>全屏提醒的 1280×720 设计坐标 → 渲染位图坐标。</summary>
        private int SX(float value)
        {
            return (int)Math.Round(value * screenLayout);
        }

        private int SY(float value)
        {
            return (int)Math.Round(value * screenLayout);
        }

        /// <summary>是否显示第二段时间（午休倒计时）。</summary>
        private bool ShowLunchRow
        {
            get { return settings.ui.showLunch && status != null && status.lunch != LunchState.None; }
        }

        // 小字排版的几个旋钮：字号、次要文字的灰度。调这几个值就能整张卡片跟着变。
        internal static float TypeStatusPt = 9.5f;
        internal static float TypeSmallPt = 9.5f;
        internal static float TypeTinyPt = 9f;
        internal static float MutedMix = 0.18f;
        internal static float DimMix = 0.30f;
        // 分层窗口里整帧是 32 位带 alpha 的位图：ClearType 会在字形边上留下彩色描边（看着像锯齿），
        // 灰阶抗锯齿 + 整数像素定位才是干净的做法。
        internal static bool UseClearType = false;

        private static int CardHeightDip(bool withLunch)
        {
            int rows = HeadHDip + LineDip + BigDip + BarDip + LineDip + FootDip + (withLunch ? LineDip : 0);
            int gaps = withLunch ? 6 : 5;
            return CardTopDip + rows + RowGapDip * gaps + CardBottomDip;
        }

        private int ExpandedH
        {
            get
            {
                int h = S(CardHeightDip(ShowLunchRow));
                if (salaryOpen) h += S(SalaryPanelDip);
                return h;
            }
        }

        /// <summary>卡片圆角（DIP）：默认皮肤收起 14 / 展开 18，其它皮肤按 SkinDef。</summary>
        private int CardRadiusDip
        {
            get { return Math.Max(1, collapsed ? Skin.radiusCollapsed : Skin.radiusExpanded); }
        }

        /// <summary>卡片圆角的物理像素，和 RenderCard 画的那条路径一致（CardGlowForm 也读它）。</summary>
        internal int CardRadiusPx
        {
            get { return S(CardRadiusDip); }
        }

        /// <summary>验收 / 宿主读取同步快照的接口；窗口不再暴露私有 fetching 字段。</summary>
        internal SyncSnapshot CurrentSync
        {
            get { return sync.Snapshot(); }
        }

        /// <summary>按当前设置重建所有卡片字体（换中文字体 / 数字字体后调用）。</summary>
        private void CreateFonts()
        {
            DisposeCoreFonts();
            fontBig = UiFont.MakeNumber(28f, FontStyle.Bold);
            fontBigText = UiFont.Make(17f, FontStyle.Bold);
            fontStatus = UiFont.Make(TypeStatusPt, FontStyle.Regular);
            fontTiny = UiFont.Make(TypeTinyPt, FontStyle.Regular);
            fontSmall = UiFont.Make(TypeSmallPt, FontStyle.Regular);
            fontSmallBold = UiFont.Make(TypeSmallPt, FontStyle.Bold);
            fontCollapsedNum = UiFont.MakeNumber(17f, FontStyle.Bold);
            fontCollapsedLabel = UiFont.Make(TypeSmallPt, FontStyle.Regular);
            fontCollapsedText = UiFont.Make(13f, FontStyle.Bold);
            fontRemindBig = UiFont.Make(17f, FontStyle.Bold);
            fontRemindSmall = UiFont.Make(10f, FontStyle.Regular);
            fontRemindTitle = UiFont.Make(17f, FontStyle.Bold);
            fontRemindMeta = UiFont.Make(10f, FontStyle.Regular);
            fontRemindTime = UiFont.Make(12f, FontStyle.Bold);
            fontSalary = UiFont.MakeNumber(22f, FontStyle.Bold);
            // 卡片右上角那颗 ¥：用常规字重。字号只决定轮廓精度，DrawGlyph 会把字形墨迹缩放到约 13.4 DIP 高，
            // 粗体在这一档会显得比旁边 1.5 DIP 的线条图标重。
            fontGlyphSalary = UiFont.MakeNumber(13f, FontStyle.Regular);
        }

        private void DisposeCoreFonts()
        {
            foreach (var font in new Font[] { fontBig, fontBigText, fontStatus, fontTiny, fontSmall,
                fontCollapsedNum, fontCollapsedLabel, fontCollapsedText, fontRemindBig, fontRemindSmall,
                fontRemindTitle, fontRemindMeta, fontRemindTime, fontSalary, fontGlyphSalary })
            {
                if (font != null && !ReferenceEquals(font, SystemFonts.DefaultFont)) font.Dispose();
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | WsExLayered; // WS_EX_TOOLWINDOW + 分层窗口
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Recompute();
            RequestRefresh(false);
            RequestCalendar(false);
            RequestOutlook(false);
            tick.Start();
            flowT.Start();
            topT.Start();
            KeepTopMost();
            if (!settings.ui.reducedMotion && !SystemInformation.HighContrast && cardGlow == null)
            {
                cardGlow = new CardGlowForm(this, theme, scale);
                cardGlow.SetGlow(Skins.GlowColor(Skin, theme));
                cardGlow.Begin();
            }
            // 开机也是展开的：同样按「N 秒没动就收起」的规则走，别一直杵在那儿
            ResetAutoCollapse();
            // 第一帧主动推一次：分层窗口不会因为窗口出现就自动有东西
            BeginInvoke((MethodInvoker)delegate { Invalidate(); if (IsHandleCreated && Visible) PushFrame(); });
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // 从托盘 / 最小化恢复时重新压到最上层，别让它停在别人后面
            if (Visible) KeepTopMost();
        }

        /// <summary>
        /// 置顶看门狗：设置里开着「窗口置顶」时，保证窗口带 WS_EX_TOPMOST。
        /// 已经是置顶就什么都不做——不能每 2 秒把 z 序抬一次，否则会盖住自己的
        /// 设置窗 / 右键菜单，也会和别人的置顶窗口互相打架。
        /// </summary>
        internal void KeepTopMost()
        {
            if (settings == null) return;
            bool want = settings.ui.alwaysOnTop || screenReminder;
            if (TopMost != want) TopMost = want;     // 还没建句柄时 WinForms 会记着，建句柄时应用
            if (!want || !IsHandleCreated) return;
            if (IsWindowTopMost()) return;
            SetWindowPos(Handle, HwndTopMost, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);
        }

        /// <summary>窗口现在是不是置顶（读实际的扩展样式，不信 Form.TopMost 字段）。</summary>
        internal bool IsWindowTopMost()
        {
            return IsHandleCreated && (GetWindowLong(Handle, GwlExStyle) & WsExTopMost) != 0;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // 换显示器 / 改缩放后，有的显卡驱动会把窗口的置顶样式弄丢：补一次
            if (m.Msg == 0x007E && Visible) KeepTopMost();   // WM_DISPLAYCHANGE
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CloseBubbleMenu();
            if (cardGlow != null) { cardGlow.CloseNow(); cardGlow = null; }
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            tick.Stop(); poll.Stop(); autoCollapse.Stop(); animT.Stop(); flowT.Stop(); topT.Stop(); calPoll.Stop(); olPoll.Stop(); remindT.Stop(); tapeT.Stop();
            sync.Dispose();
            CloseDrawer();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reminderTip.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                foreach (var timer in new System.Windows.Forms.Timer[] { tick, poll, autoCollapse, animT, calPoll, olPoll, remindT, flowT, topT, tapeT })
                    if (timer != null) timer.Dispose();
                foreach (var font in new Font[] { fontBig, fontBigText, fontStatus, fontTiny, fontSmall,
                    fontCollapsedNum, fontCollapsedLabel, fontCollapsedText, fontRemindBig, fontRemindSmall,
                    fontRemindTitle, fontRemindMeta, fontRemindTime, fontSalary, fontGlyphSalary }) if (font != null) font.Dispose();
                sfCenter.Dispose(); sfNear.Dispose(); sfFar.Dispose();
                DisposeScreenFonts();
                if (screenForm != null) { screenForm.CloseNow(); screenForm = null; }
                CloseDrawer();
                if (screenCardUnder != null) { screenCardUnder.Dispose(); screenCardUnder = null; }
                if (frame != null) { frame.Dispose(); frame = null; }
                CloseBubbleMenu();
                CloseNotePanel();
                if (noteRelay != null) { noteRelay.Dispose(); noteRelay = null; }
                if (cardGlow != null) { cardGlow.CloseNow(); cardGlow = null; }
                if (sync != null) sync.Dispose();
                if (menu != null) menu.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------------- 状态 ----------------

        private void Recompute()
        {
            theme = Skins.ResolveTheme(settings.ui);
            // 窗口底色必须和卡片一致：圆角抗锯齿边缘会与底色混合，浅灰底会出现一圈白边
            if (BackColor != theme.bgBottom) BackColor = theme.bgBottom;
            DateTime now = DateTime.Now;
            status = StatusBuilder.Build(settings, AttendanceSource.Resolve(settings, snapshot, now), now, theme);
            if (cardGlow != null)
            {
                cardGlow.SetTheme(theme);
                // 卡外光颜色按皮肤走；默认皮肤 Skins.GlowColor 就是 theme.accent，画法不变
                cardGlow.SetGlow(Skins.GlowColor(Skin, theme));
            }
            UpdateTickRate();
            // 工资面板开着时，午休行出现 / 消失会改变展开高度：每秒顺手校正一次
            if (salaryOpen && !screenReminder && !collapsed && !animating && !salaryAnimating && Height != ExpandedH)
            {
                Size = new Size(S(DesignExpandedW), ExpandedH);
                ApplyRegion();
            }
            Invalidate();
        }

        private void RequestRefresh(bool manual)
        {
            if (sync.RequestAttendance(manual) != null) Invalidate();
        }

        private static string DescribeSnapshot(AttendanceSnapshot s)
        {
            if (s.clockOut.HasValue) return "今日 " + s.clockIn.GetValueOrDefault().ToString("HH:mm") + " → " + s.clockOut.Value.ToString("HH:mm") + " 已下班";
            if (s.clockIn.HasValue) return "今日 " + s.clockIn.Value.ToString("HH:mm") + " 上班打卡";
            return "今日暂无打卡记录";
        }

        // ---------------- 日程提醒 ----------------

        private void RequestCalendar(bool manual)
        {
            if (sync.RequestLarkCalendar(manual) != null) Invalidate();
        }

        private ReminderScheduleSnapshot ScheduleSnapshot()
        {
            SyncSnapshot state = sync.Snapshot();
            return new ReminderScheduleSnapshot(state.LarkEvents, state.OutlookEvents);
        }

        private string NextEventText()
        {
            var next = engine.NextEvent(settings, ScheduleSnapshot(), DateTime.Now);
            if (next == null) return "没有待提醒的日程";
            DateTime start = Reminders.ParseStart(next);
            return start.ToString("HH:mm") + " " + next.title;
        }

        private void RequestOutlook(bool manual)
        {
            if (sync.RequestOutlook(manual) != null) Invalidate();
        }

        /// <summary>
        /// 同步模块接受结果后的界面应用：只读快照做日志与手动提示；
        /// 日程合并、来源过滤、在途去重与过期判断都不在窗口里。
        /// </summary>
        private void OnSyncPublished(SyncPublication publication)
        {
            if (publication == null || IsDisposed || Disposing) return;
            SyncSnapshot state = publication.Snapshot == null ? sync.Snapshot() : publication.Snapshot;
            if (publication.Scope == SyncScopes.Attendance)
            {
                AttendanceSnapshot attendance = state.Attendance;
                snapshot = attendance;
                if (attendance != null && !attendance.ok)
                    store.Log("飞书读取失败: " + (attendance.error ?? ""));
                Recompute();
                if (publication.Manual)
                {
                    ShowBalloon(attendance != null && attendance.ok
                        ? "已同步：" + DescribeSnapshot(attendance)
                        : "同步失败：" + LarkClient.Shorten(attendance == null ? "没有返回考勤记录" : attendance.error, 90));
                }
            }
            else if (publication.Scope == SyncScopes.LarkCalendar)
            {
                if (publication.ErrorChanged && !string.IsNullOrEmpty(state.LarkError))
                    store.Log("飞书日程读取失败: " + state.LarkError);
                if (publication.Manual)
                {
                    if (!string.IsNullOrEmpty(state.LarkError))
                        ShowBalloon("日程同步失败：" + LarkClient.Shorten(state.LarkError, 90));
                    else if (state.LarkEvents.Count == 0)
                        ShowBalloon("飞书日程：这两天没有日程");
                    else
                        ShowBalloon("飞书日程：" + state.LarkEvents.Count + " 个 · " + NextEventText());
                }
                Invalidate();
            }
            else if (publication.Scope == SyncScopes.Outlook)
            {
                if (publication.ErrorChanged && !string.IsNullOrEmpty(state.OutlookError))
                    store.Log("Outlook 日程读取失败: " + state.OutlookError);
                if (publication.Manual)
                {
                    if (!string.IsNullOrEmpty(state.OutlookError) && state.OutlookEvents.Count == 0)
                        ShowBalloon("Outlook 同步失败：" + LarkClient.Shorten(state.OutlookError, 90));
                    else if (!string.IsNullOrEmpty(state.OutlookError))
                        ShowBalloon("Outlook 日程：" + state.OutlookEvents.Count + " 个 · 部分日历失败：" +
                            LarkClient.Shorten(state.OutlookError, 50));
                    else
                        ShowBalloon("Outlook 日程：" + state.OutlookEvents.Count + " 个 · " + NextEventText());
                }
                Invalidate();
            }
        }

        /// <summary>
        /// 状态拥有者在这里应用同步模块接受的配置更新：每份日历的缓存 / 同步时间 / 错误、
        /// 汇总缓存与 Graph 轮换后的受保护令牌，然后按原约定保存。
        /// </summary>
        public void ApplySyncConfigUpdate(SyncConfigUpdate update)
        {
            if (update == null || settings == null || settings.outlook == null) return;
            if (update.Calendars != null)
            {
                foreach (var item in update.Calendars)
                {
                    if (item == null) continue;
                    CalendarSource source = FindCalendarSource(item.SourceId);
                    if (source == null) continue;
                    if (item.Succeeded)
                    {
                        source.cached = item.Events == null
                            ? new List<ReminderEvent>() : new List<ReminderEvent>(item.Events);
                        source.lastSync = item.LastSync ?? "";
                    }
                    source.lastError = item.LastError ?? "";
                }
            }
            settings.outlook.cached = update.OutlookCached == null
                ? new List<ReminderEvent>() : new List<ReminderEvent>(update.OutlookCached);
            settings.outlook.lastSync = update.OutlookLastSync ?? "";
            settings.outlook.lastError = update.OutlookLastError ?? "";
            if (update.RefreshToken != null)
            {
                settings.outlook.refreshToken = update.RefreshToken;
                settings.outlook.tokenAt = update.TokenAt ?? "";
            }
            SaveSettingsQuietly("同步结果回写");
        }

        /// <summary>
        /// 后台 / 轮询 / 悬浮窗快捷操作的保存策略：失败只记日志，绝不弹窗，避免每次轮询打扰。
        /// 需要交互反馈的保存（设置确认、导入、导出）在自己的调用点检查结果。
        /// </summary>
        internal SettingsSaveResult SaveSettingsQuietly(string reason)
        {
            var result = store.Save(settings);
            if (result.IsFailed)
                store.Log("保存设置失败（" + reason + "）：" + result.Message);
            return result;
        }

        private CalendarSource FindCalendarSource(string id)
        {
            if (settings.outlook == null || settings.outlook.calendars == null) return null;
            foreach (var source in settings.outlook.calendars)
                if (source != null && source.id == id) return source;
            return null;
        }

        /// <summary>
        /// 每秒推进一次提醒决策：常驻下班提醒、固定日程、普通日程与补提醒都在
        /// ReminderEngine 里；窗口只把决定应用成动画 / 全屏 / 抽屉 / 托盘气泡。
        /// </summary>
        internal bool AdvanceReminders(DateTime now)
        {
            var signals = new ReminderSignals();
            signals.OffWorkDue = status != null && status.offAlert;
            signals.OffWorkTarget = status == null ? null : status.targetIso;
            signals.Showing = reminding;
            var outcome = engine.Advance(settings, ScheduleSnapshot(), now, signals);
            foreach (var d in outcome.Decisions) ApplyReminderDecision(d, now);
            return outcome.Started;
        }

        /// <summary>把一条提醒决定落到窗口的展示状态上（决定本身不带动画标志）。</summary>
        private void ApplyReminderDecision(ReminderDecision d, DateTime now)
        {
            if (d == null) return;
            if (d.Reason == ReminderReasons.OffWorkStopped)
            {
                if (remindEvent != null && remindEvent.source == "alert")
                {
                    remindT.Stop();
                    reminding = false;
                    remindEvent = null;
                    ResetAutoCollapse();
                    Invalidate();
                    store.Log("下班打卡已同步: 收起「该下班啦」");
                }
                return;
            }
            if (d.Event == null) return;
            if (d.Reason == ReminderReasons.OffWorkRepeat)
            {
                // 原地重播入场：色块不动，文字重淡入 + 扫光
                remindEvent = d.Event;
                remindAt = now;
                reminding = true;
                autoCollapse.Stop();
                remindT.Interval = 33;
                remindT.Start();
                Invalidate();
                store.Log("该下班啦: 重新提醒 @ " + now.ToString("HH:mm"));
                return;
            }
            if (d.Reason == ReminderReasons.OffWorkStart || d.Reason == ReminderReasons.OffWorkResume)
            {
                StartOffAlert(d.Event);
                return;
            }
            if (d.Reason == ReminderReasons.Late)
                store.Log("补提醒（错过开始时间）: " + d.Event.title + " @" + Reminders.ParseStart(d.Event).ToString("yyyy-MM-dd HH:mm"));
            StartReminder(d.Event);
        }

        /// <summary>右键「忽略今天的下班提醒」：今天不再弹，第二天自动恢复。</summary>
        private void MuteOffAlert()
        {
            engine.MuteOffWork(DateTime.Now);
            if (remindEvent != null && remindEvent.source == "alert")
            {
                remindT.Stop();
                reminding = false;
                remindEvent = null;
                ResetAutoCollapse();
                Invalidate();
            }
            store.Log("手动忽略今天的下班提醒");
        }

        private void StartOffAlert(ReminderEvent e)
        {
            if (animating) { animT.Stop(); animating = false; SetBounds(animToX, Top, animToW, animToH); collapsed = animToW == S(DesignCollapsedW); ApplyRegion(); }
            remindEvent = e;
            remindUntil = DateTime.MaxValue;      // 直到打卡
            remindExiting = false;
            remindAt = DateTime.Now;
            reminding = true;
            autoCollapse.Stop();
            reminderTip.SetToolTip(this, "常驻提醒\r\n该下班啦\r\n打卡后自动消失");
            AccessibleDescription = "注意，该下班啦";
            remindT.Interval = 33; remindT.Start();
            Invalidate();
            store.Log("该下班啦: 开始常驻提醒 @" + DateTime.Now.ToString("HH:mm"));
            ShowBalloon("该下班啦\r\n还没同步到下班打卡，每 5 分钟提醒一次");
        }

        private void StartReminder(ReminderEvent e)
        {
            CloseBubbleMenu();
            if (screenReminder) ExitScreenMode();
            if (animating) { animT.Stop(); animating = false; SetBounds(animToX, Top, animToW, animToH); collapsed = animToW == S(DesignCollapsedW); ApplyRegion(); }
            remindEvent = e;
            remindUntil = engine.HoldUntil(settings, e, DateTime.Now);
            remindExiting = false;
            AccessibleDescription = e.title + " · " + Reminders.TimeRange(e) + " · " + e.place + " · " + e.detail +
                "（常驻提醒，右上角可关闭）";
            reminderTip.SetToolTip(this, "最近提醒\r\n" + e.title + "\r\n" + Reminders.TimeRange(e) + " · " + e.place + "\r\n" + e.detail);
            remindAt = DateTime.Now;
            reminding = true;
            autoCollapse.Stop();
            remindT.Interval = (settings.ui.reducedMotion || SystemInformation.HighContrast) ? 100 : 33; remindT.Start();
            // 警戒带：先把小卡展开成大卡，等展开动画走完再把全屏条推上来；
            // 这一轮里卡片不做覆盖（只显示普通卡片），信息交给警戒带和抽屉
            bool tape = UseScreenReminder(e) && settings.StyleOf(Reminders.KindOf(e)).shape == "tape";
            tapeActive = tape;
            CloseDrawer();
            drawerDismissed = false;
            if (tape)
            {
                if (collapsed) StartAnim(false, TapeExpandMs);
                if (animating)
                {
                    tapePhase = 1;
                    tapeAt = Environment.TickCount + animDuration + 80;
                    tapeT.Start();
                }
                else ShowScreenReminder(e);
            }
            else if (UseScreenReminder(e)) ShowScreenReminder(e);
            Invalidate();
            store.Log("日程提醒: " + e.title + " @" + Reminders.ParseStart(e).ToString("yyyy-MM-dd HH:mm"));
            if (e.id != "preview")      // 预览通知不弹托盘气泡，免得以为真来了个日程
                ShowBalloon("日程提醒 · " + e.title + "\r\n" + Reminders.ParseStart(e).ToString("HH:mm") +
                    " 开始，" + Reminders.CountdownText(settings, e, DateTime.Now));
        }

        /// <summary>提醒动画的虚拟时钟：入场 → 冻住常驻 → 退场继续走。</summary>
        private double ReminderClock()
        {
            if (remindEvent != null && remindEvent.source == "alert") return (DateTime.Now - remindAt).TotalMilliseconds;
            if (remindExiting) return RemindInMs + RemindHoldMs + (DateTime.Now - remindExitAt).TotalMilliseconds;
            return Math.Min((DateTime.Now - remindAt).TotalMilliseconds, RemindInMs + RemindHoldMs);
        }

        /// <summary>开始退场（两块色块先后扫出），放完就回到普通卡片。</summary>
        private void BeginExitReminder()
        {
            if (remindEvent == null) return;
            if (screenForm != null) { screenForm.BeginExit(); return; }
            remindExiting = true;
            remindExitAt = DateTime.Now;
            remindT.Interval = 33;
            remindT.Start();
            Invalidate();
        }

        private void EndReminder()
        {
            remindT.Stop();
            if (screenForm != null) { screenForm.CloseNow(); screenForm = null; }
            CloseDrawer();
            tapeT.Stop();
            tapePhase = 0;
            tapeActive = false;
            reminding = false;
            remindExiting = false;
            remindEvent = null;
            if (screenReminder) ExitScreenMode();
            ResetAutoCollapse();
            Invalidate();
        }

        private bool UseScreenReminder(ReminderEvent e)
        {
            return settings.StyleOf(Reminders.KindOf(e)).spread == "screen"
                && !settings.ui.reducedMotion && !SystemInformation.HighContrast
                && e != null && e.source != "alert";
        }

        private void ShowScreenReminder(ReminderEvent e)
        {
            if (screenForm != null) { screenForm.CloseNow(); screenForm = null; }
            var scr = Screen.FromControl(this);
            int pad = S(18);
            int barY = collapsed ? S(59) : S(102);
            int barH = collapsed ? S(3) : S(4);
            double progress = status == null ? 0 : Math.Max(0, Math.Min(1, status.progress));
            int tipX = Left + pad + (int)Math.Round((Width - pad * 2) * progress);
            int tipY = Top + barY + barH / 2;
            screenForm = new ScreenReminderForm(settings, e, theme, scr.Bounds,
                new Point(tipX, tipY), OnScreenDone);
            screenForm.Show();
            // 全屏窗自己跑入场动画；卡片留在后面，等它关掉再恢复
            remindT.Stop();
        }

        private void OnScreenDone()
        {
            // 全屏动画放完回到卡片提醒；提醒本身继续常驻
            screenForm = null;
            Invalidate();
            if (!tapeActive || remindEvent == null) return;
            // 警戒带放完：卡片收回小卡，等它收完再拉抽屉（HTML 预览里也是先收后拉）
            bool collapsing = !collapsed;
            if (collapsing) StartAnim(true, TapeCollapseMs);
            if (drawerDismissed) return;
            tapePhase = 2;
            tapeAt = Environment.TickCount + (collapsing ? TapeCollapseMs + 300 : 300);
            tapeT.Start();
        }

        /// <summary>警戒带流程的两段等待：先等卡片展开，再等警戒带消失 0.3s。</summary>
        private void StepTapeFlow()
        {
            int now = Environment.TickCount;
            if (tapePhase == 1)
            {
                if (!reminding || remindEvent == null) { tapePhase = 0; tapeT.Stop(); return; }
                if (now < tapeAt) return;
                tapePhase = 0;
                tapeT.Stop();
                if (remindEvent != null && UseScreenReminder(remindEvent)) ShowScreenReminder(remindEvent);
                return;
            }
            if (tapePhase == 2)
            {
                if (now < tapeAt) return;
                tapePhase = 0;
                tapeT.Stop();
                ShowDrawer();
            }
        }

        /// <summary>从卡片贴边反向拉出抽屉卡；贴左往右拉，贴右往左拉。</summary>
        private void ShowDrawer()
        {
            if (!tapeActive || remindEvent == null || drawerDismissed) return;
            if (screenForm != null) return;
            CloseDrawer();
            bool toRight = DrawerToRight();
            var d = new DrawerForm(settings, remindEvent, Bounds, toRight, scale,
                IsWindowTopMost(), delegate { drawerDismissed = true; });
            d.FormClosed += delegate { if (drawer == d) drawer = null; };
            drawer = d;
            d.Show();
            if (d.IsHandleCreated) d.OrderBelow(Handle);
            d.BeginSlide();
            store.Log("抽屉卡: " + remindEvent.title + " " + (toRight ? "向右拉" : "向左拉"));
        }

        /// <summary>抽屉往哪边拉：贴左的往右，贴右的往左；自由位置按卡片在屏幕哪半边决定，出屏就翻边。</summary>
        private bool DrawerToRight()
        {
            Screen scr = Screen.FromRectangle(Bounds);
            Rectangle wa = scr.WorkingArea;
            int need = S(DrawerForm.WidthDip - DrawerForm.OverlapDip);
            if (settings.ui.edge == "left") return true;
            if (settings.ui.edge == "right") return false;
            bool cardOnRight = Bounds.Left + Bounds.Width / 2 >= wa.Left + wa.Width / 2;
            bool toRight = !cardOnRight;
            if (toRight && Bounds.Right - S(DrawerForm.OverlapDip) + need > wa.Right) toRight = false;
            if (!toRight && Bounds.Left + S(DrawerForm.OverlapDip) - need < wa.Left) toRight = true;
            return toRight;
        }

        /// <summary>关掉抽屉卡（提醒结束、卡片被动过、设置改了）。</summary>
        private void CloseDrawer()
        {
            if (tapePhase == 2) tapePhase = 0;
            if (drawer != null)
            {
                drawer.CloseNow();
                drawer = null;
            }
        }

        /// <summary>把窗口临时铺满小组件所在的那块屏，退场后原样缩回卡片。</summary>
        private void EnterScreenMode()
        {
            if (screenReminder) return;
            screenRestoreBounds = Bounds;
            screenRestoreCollapsed = collapsed;
            var scr = Screen.FromControl(this);
            screenBounds = scr.Bounds;
            screenW = Math.Max(320, screenBounds.Width);
            screenH = Math.Max(240, screenBounds.Height);
            screenRenderW = screenW;
            screenRenderH = screenH;
            screenLayout = Math.Min(screenRenderW / 1280.0, screenRenderH / 720.0);

            int pad = S(18);
            int barY = collapsed ? S(59) : S(102);
            int barH = collapsed ? S(3) : S(4);
            double progress = status == null ? 0 : Math.Max(0, Math.Min(1, status.progress));
            double tipLocalX = pad + (Width - pad * 2) * progress;
            double tipLocalY = barY + barH / 2.0;
            screenTipX = (int)Math.Round(Left + tipLocalX - screenBounds.Left);
            screenTipY = (int)Math.Round(Top + tipLocalY - screenBounds.Top);

            screenCardUnder = frame == null ? null : (Bitmap)frame.Clone();
            CreateScreenFonts();
            screenAlphaBefore = frameAlpha;
            frameAlpha = 255;
            screenReminder = true;
            MinimumSize = Size.Empty;
            SetBounds(screenBounds.Left, screenBounds.Top, screenW, screenH);
            TopMost = true;
            KeepTopMost();
            if (frame != null) { frame.Dispose(); frame = null; }
            Invalidate();
        }

        private Bitmap CaptureScreen(Rectangle r)
        {
            try
            {
                var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
                return bmp;
            }
            catch { return null; }
        }

        private void ExitScreenMode()
        {
            if (!screenReminder) return;
            screenReminder = false;
            DisposeScreenFonts();
            if (screenCardUnder != null) { screenCardUnder.Dispose(); screenCardUnder = null; }
            frameAlpha = screenAlphaBefore;
            MinimumSize = new Size(S(DesignCollapsedW), S(DesignCollapsedH));
            SetBounds(screenRestoreBounds.X, screenRestoreBounds.Y, screenRestoreBounds.Width, screenRestoreBounds.Height);
            collapsed = screenRestoreCollapsed;
            if (frame != null) { frame.Dispose(); frame = null; }
            TopMost = settings.ui.alwaysOnTop;
            KeepTopMost();
            Invalidate();
        }

        private void CreateScreenFonts()
        {
            DisposeScreenFonts();
            float l = (float)screenLayout;
            screenKindFont = UiFont.Make(12f * l, FontStyle.Bold);
            screenTitleFont = UiFont.Make(46f * l, FontStyle.Bold);
            screenCountFont = UiFont.Make(19f * l);
            screenWhenFont = UiFont.Make(28f * l, FontStyle.Bold);
            screenPlaceFont = UiFont.Make(16f * l);
            screenDetailFont = UiFont.Make(14f * l);
            screenHintFont = UiFont.Make(11.5f * l);

            // 标题太长就缩一点，别把右侧的关闭按钮顶掉
            if (remindEvent != null && screenTitleFont != null)
            {
                float maxW = screenRenderW - 2 * (68f * l);
                using (var probe = new Bitmap(1, 1))
                using (var pg = Graphics.FromImage(probe))
                {
                    while (screenTitleFont.Size > 22f * l && pg.MeasureString(remindEvent.title, screenTitleFont).Width > maxW)
                    {
                        float next = screenTitleFont.Size - 2f;
                        screenTitleFont.Dispose();
                        screenTitleFont = UiFont.Make(next, FontStyle.Bold);
                    }
                }
            }
        }

        private void DisposeScreenFonts()
        {
            foreach (var f in new Font[] { screenKindFont, screenTitleFont, screenCountFont,
                screenWhenFont, screenPlaceFont, screenDetailFont, screenHintFont })
                if (f != null) f.Dispose();
            screenKindFont = screenTitleFont = screenCountFont = screenWhenFont =
                screenPlaceFont = screenDetailFont = screenHintFont = null;
        }

        /// <summary>最后 60 分钟显示厘秒，这时才把刷新提到 20Hz；其余时间仍旧一秒一跳。</summary>
        private void UpdateTickRate()
        {
            int ms = 1000;
            if (status != null && status.bigIsClock && status.big != null && status.big.IndexOf('.') >= 0)
                ms = settings.ui.reducedMotion ? 200 : 50;
            if (tick.Interval != ms) tick.Interval = ms;
        }

        /// <summary>每次 tick：常驻的提醒一旦过了自己的时段，就退场。</summary>
        private void CheckSticky()
        {
            if (!reminding || remindEvent == null || remindExiting) return;
            // 预览通知：不管哪一类（含下班提醒那种常驻样式），到点就退场
            if (remindEvent.id == "preview")
            {
                if (DateTime.Now >= remindUntil) BeginExitReminder();
                return;
            }
            if (remindEvent.source == "alert") return;      // 下班提醒由 AdvanceReminders / 常驻状态机管
            if (DateTime.Now < remindUntil) return;
            store.Log("提醒时段结束: 收起「" + remindEvent.title + "」");
            BeginExitReminder();
        }

        /// <summary>手动关闭当前提醒（卡片右上角的 × 或右键菜单）。</summary>
        private void DismissReminder()
        {
            if (!reminding || remindEvent == null) return;
            var e = remindEvent;
            engine.Dismiss(e, DateTime.Now);
            store.Log("手动关闭提醒: " + e.title);
            drawerDismissed = true;
            CloseDrawer();
            if (screenForm != null)
            {
                // 全屏时手动关闭：直接结束提醒，不要让卡片再留着
                EndReminder();
                return;
            }
            if (e.source == "alert")
            {
                // 下班提醒的 × = 忽略今天（状态在 ReminderEngine 里）
                EndReminder();
                return;
            }
            BeginExitReminder();
        }

        private void StepReminder()
        {
            if (remindEvent == null) { remindT.Stop(); return; }
            // 预览通知：入场 → 停一会 → 退场，全走普通提醒的时钟（时长 = 最短停留）
            if (remindEvent.id == "preview")
            {
                if (remindExiting && (DateTime.Now - remindExitAt).TotalMilliseconds > RemindOutMs) { EndReminder(); return; }
                if (!remindExiting && DateTime.Now >= remindUntil) BeginExitReminder();
                Invalidate();
                return;
            }
            if (remindEvent.source == "alert")
            {
                // 常驻：入场动画放完就停表，静态显示，等下一次 5 分钟重播
                if ((DateTime.Now - remindAt).TotalMilliseconds > OffAlertAnimMs)
                {
                    remindT.Stop();
                    Invalidate();
                    return;
                }
                Invalidate();
                return;
            }
            if (remindExiting)
            {
                if ((DateTime.Now - remindExitAt).TotalMilliseconds > RemindOutMs) { EndReminder(); return; }
                Invalidate();
                return;
            }
            // 入场放完就停表：常驻期间靠每秒的 tick 重绘（倒计时文字跟着走），不占 CPU
            if ((DateTime.Now - remindAt).TotalMilliseconds >= RemindInMs)
            {
                remindT.Stop();
                Invalidate();
                return;
            }
            Invalidate();
        }

        private static double EaseOutCubic(double t)
        {
            double u = 1 - Math.Max(0, Math.Min(1, t));
            return 1 - u * u * u;
        }

        private static double EaseInCubic(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t * t * t;
        }

        private static double Smooth(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t * t * (3 - 2 * t);
        }

        private static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        private static Color MixColor(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // Text and background have independent timelines. No wipe ever clips a glyph.
        /// <summary>提醒卡：整块色块淡入，文字稍后上浮淡入；退场整层淡出，露出下面的普通卡片。</summary>
        private void PaintReminder(Graphics g, double t, bool quiet, double bandAlpha, double contentAlpha)
        {
            if (remindEvent == null) return;
            bool alert = remindEvent.source == "alert";
            if (alert) { PaintOffAlert(g, t, quiet, bandAlpha); return; }
            bool meeting = Reminders.IsMeeting(remindEvent);
            var fixedSched = FixedOf(remindEvent);
            // 固定日程用自己的配色；会议、日程等一律跟随当前主题。
            Color a, b;
            Color styleInk;
            ReminderColors(out a, out b, out styleInk);

            double exit = t - RemindInMs - RemindHoldMs;
            int seam = collapsed ? S(37) : S(64);
            // 两张卡叠上来：上面的色块先从上往下落定，下面的色块再从下往上顶上来；
            // 退场反过来——下面先抽走，上面再走。quiet 时直接到位。
            double pa, pb;
            if (quiet)
            {
                pa = 1; pb = 1;
            }
            else
            {
                pa = Clamp01(Math.Min(EaseOutCubic(Clamp01(t / 300.0)), 1 - EaseInCubic(Clamp01((exit - 330) / 280.0))));
                pb = Clamp01(Math.Min(EaseOutCubic(Clamp01((t - 210) / 300.0)), 1 - EaseInCubic(Clamp01((exit - 130) / 280.0))));
            }
            if (pa > 0)
            {
                int offA = (int)Math.Round((1 - pa) * -seam);
                using (var brush = new SolidBrush(a)) g.FillRectangle(brush, 0, offA, Width, seam);
            }
            if (pb > 0)
            {
                int offB = (int)Math.Round((1 - pb) * (Height - seam));
                using (var brush = new SolidBrush(b)) g.FillRectangle(brush, 0, seam + offB, Width, Height - seam);
            }

            // 文字比色块晚一点出现，并且从下往上浮 7 DIP，衔接才连得上
            double p = contentAlpha;
            if (p <= 0) { DrawDismissButton(g, styleInk, 0); return; }
            var saved = g.Save();
            g.TranslateTransform(0, (float)((1 - EaseOutCubic(p)) * S(7)));
            DrawReminderContent(g, meeting, fixedSched != null ? fixedSched.name : null, styleInk, p);
            g.Restore(saved);
            if (!quiet) DrawDismissButton(g, styleInk, p);
            else DrawDismissButton(g, styleInk, 1);
        }

        /// <summary>提醒常驻时右上角的关闭按钮（×）：点一下这条提醒就收掉。</summary>
        private void DrawDismissButton(Graphics g, Color ink, double alpha)
        {
            int size = S(26);
            float x = Width - S(12) - size;
            float y = S(9);
            rectDismiss = new RectangleF(x, y, size, size);
            float pad = S(8);
            using (var pen = new Pen(Color.FromArgb((int)(132 * Clamp01(alpha)), ink), Math.Max(1.4f, S(1.7f))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, x + pad, y + pad, x + size - pad, y + size - pad);
                g.DrawLine(pen, x + size - pad, y + pad, x + pad, y + size - pad);
            }
        }

        /// <summary>提醒的两层色：固定日程按自己的配色；会议、日程等跟随当前主题。</summary>
        private void ReminderColors(out Color a, out Color b, out Color ink)
        {
            if (theme == null) theme = Skins.ResolveTheme(settings.ui);
            // 固定日程按自己选的配色（旧规则），其余三类按「该类日程」的配色设置
            string styleId = Reminders.ColorIdOf(settings, remindEvent, FixedOf(remindEvent));
            ReminderStyles.Resolve(styleId, theme, out a, out b, out ink);
        }

        /// <summary>提醒卡片第一行的类型名（会议/日程/固定日程名/注意）。</summary>
        internal static string KindLabel(ReminderEvent e, bool meeting, string fixedName)
        {
            if (e != null && e.source == "alert") return "注意";
            if (e != null && e.source == "break")
                return (string.IsNullOrWhiteSpace(fixedName) ? "固定日程" : fixedName) + "提醒";
            if (e != null && e.source == "outlook") return "Outlook 日程";
            return meeting ? "会议提醒" : "日程提醒";
        }

        /// <summary>这条提醒是不是某个固定日程发的；不是就返回 null。</summary>
        private FixedSchedule FixedOf(ReminderEvent e)
        {
            if (e == null || e.id == null || e.source != "break") return null;
            string id = e.id;
            if (id.StartsWith("fix:", StringComparison.Ordinal)) id = id.Substring(4);
            int cut = id.IndexOf(":end", StringComparison.Ordinal);
            if (cut >= 0) id = id.Substring(0, cut);
            if (settings.schedules == null) return null;
            foreach (var f in settings.schedules)
                if (f != null && f.id == id) return f;
            return null;
        }

        /// <summary>
        /// 常驻下班提醒：上下两块深浅分明的清爽绿（不是警示红），
        /// 每 5 分钟重播一次「文字淡出→淡入 + 一道扫光」。
        /// </summary>
        private void PaintOffAlert(Graphics g, double t, bool quiet, double bandAlpha)
        {
            Color a, b, ink;
            var offSched = settings.ScheduleOf("offwork");
            string offStyle = offSched != null && !string.IsNullOrWhiteSpace(offSched.styleId)
                ? offSched.styleId : settings.ui.offReminderStyle;
            ReminderStyles.ResolveNoFit(offStyle, theme, out a, out b, out ink);
            int seam = collapsed ? S(37) : S(64);
            // 上下两块深浅分明的绿：卡面上的字（浅色墨）在两块上都够清楚，也不看主题脸色
            int alphaA = (int)Math.Round(255 * Clamp01(bandAlpha));
            if (alphaA > 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(alphaA, a))) g.FillRectangle(brush, 0, 0, Width, seam);
                using (var brush = new SolidBrush(Color.FromArgb(alphaA, b))) g.FillRectangle(brush, 0, seam, Width, Height - seam);
            }
            // 文字：先淡出（150ms）再淡入，让「重播」看得出来但不会闪屏
            double alpha = quiet ? 1 : (t < 150 ? 1 - t / 150 : Clamp01((t - 150) / 300));
            if (alpha > 0) DrawReminderContent(g, false, "alert", ink, alpha);
            if (!quiet) DrawDismissButton(g, ink, 1);
        }

        private static double Clamp01(double v) { return Math.Max(0, Math.Min(1, v)); }

        private void Line(Graphics g, string text, Font font, Color color, RectangleF rect, double alpha, bool far)
        {
            string clean = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            using (var format = new StringFormat { Alignment = far ? StringAlignment.Far : StringAlignment.Near,
                LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap })
            using (var brush = new SolidBrush(Color.FromArgb((int)(255 * Clamp01(alpha)), color)))
                g.DrawString(clean, font, brush, rect, format);
        }

        private void DrawReminderContent(Graphics g, bool meeting, string fixedName, Color ink, double alpha)
        {
            int pad = S(collapsed ? 14 : 18);
            float w = Width - pad * 2;
            float wTop = w - S(28);       // 右上角给关闭按钮留位置
            bool brk = remindEvent.source == "break";
            bool alert = remindEvent.source == "alert";
            string kind = KindLabel(remindEvent, meeting, fixedName);
            string when = alert
                ? "下班时间 " + Reminders.ParseStart(remindEvent).ToString("HH:mm", CultureInfo.InvariantCulture)
                : Reminders.TimeRange(remindEvent);
            string remaining = alert ? "" : (brk ? BreakTail(remindEvent) : Reminders.CountdownText(settings, remindEvent, DateTime.Now));
            if (collapsed)
            {
                Line(g, remindEvent.title, fontCollapsedText, ink, new RectangleF(pad,S(9),wTop,S(25)),alpha,false);
                string meta = alert ? when : when + " · " + remaining;
                Line(g, meta, fontCollapsedLabel, ink, new RectangleF(pad,S(40),wTop,S(20)),alpha,false);
                return;
            }
            // 行距压紧后仍然逐行分开：类型 12 / 标题 30（分界线 64）/ 下面的信息按剩余高度均分。
            Line(g, kind, fontStatus, ink, new RectangleF(pad,S(12),wTop,S(18)),alpha,false);
            Line(g, remindEvent.title, fontRemindTitle, ink, new RectangleF(pad,S(30),wTop,S(32)),alpha,false);

            var rows = new List<object[]>();
            rows.Add(new object[] { when, fontRemindTime, 22f });
            if (!string.IsNullOrWhiteSpace(remindEvent.place))
                rows.Add(new object[] { remindEvent.place, fontRemindMeta, 18f });
            if (!string.IsNullOrWhiteSpace(remindEvent.detail))
                rows.Add(new object[] { remindEvent.detail, fontSmall, 16f });
            if (!string.IsNullOrWhiteSpace(remaining))
                rows.Add(new object[] { remaining, fontStatus, 20f });

            float top = S(66), bottom = Height - S(10);
            float total = 0;
            foreach (var r in rows) total += S((float)r[2]);
            float gap = (bottom - top - total) / Math.Max(1, rows.Count - 1);
            // 卡片矮的时候宁可少一行备注，也不要挤成一坨。
            if (gap < S(2) && rows.Count > 2 && (string)rows[2][0] == remindEvent.detail)
            {
                rows.RemoveAt(2);
                total = 0;
                foreach (var r in rows) total += S((float)r[2]);
                gap = (bottom - top - total) / Math.Max(1, rows.Count - 1);
            }
            if (gap > S(16)) gap = S(16);
            if (gap < S(2)) gap = S(2);
            // 行少的时候整体居中，不要都堆在上面、下面留一大块空
            float blockH = total + gap * (rows.Count - 1);
            float y = top + Math.Max(0, (bottom - top - blockH) / 2f);
            foreach (var r in rows)
            {
                float h = S((float)r[2]);
                Line(g, (string)r[0], (Font)r[1], ink, new RectangleF(pad, y, w, h), alpha, false);
                y += h + gap;
            }
        }

        /// <summary>休息类提醒：按结束时间倒数（「还剩 40 分钟」/「已结束」）。</summary>
        private static string BreakTail(ReminderEvent e)
        {
            DateTime end = Reminders.ParseEnd(e);
            if (end == DateTime.MinValue) return "正在休息";
            TimeSpan left = end - DateTime.Now;
            if (left.TotalMinutes <= 0) return "已结束";
            if (left.TotalMinutes < 60) return "还剩 " + (int)Math.Ceiling(left.TotalMinutes) + " 分钟";
            return "还剩 " + Policy.Hm((int)Math.Round(left.TotalMinutes));

        }

        /// <summary>按宽度把字号缩到放得下，返回的可能是一个新 Font（用完要释放）。</summary>
        private Font FitFont(Graphics g, string text, Font font, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return font;
            SizeF m = g.MeasureString(text, font);
            if (m.Width <= maxWidth || m.Width <= 0) return font;
            return new Font(font.FontFamily, Math.Max(8f, font.Size * maxWidth / m.Width), font.Style);
        }

        private static string FitWidth(Graphics g, string text, Font font, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (g.MeasureString(text, font).Width <= maxWidth) return text;
            string cut = text;
            while (cut.Length > 1 && g.MeasureString(cut + "…", font).Width > maxWidth)
                cut = cut.Substring(0, cut.Length - 1);
            return cut + "…";
        }

        private void ShowBalloon(string text)
        {
            try
            {
                if (tray == null) return;
                tray.BalloonTipTitle = "上班工具箱";
                tray.BalloonTipText = text;
                tray.ShowBalloonTip(2500);
            }
            catch { }
        }

        /// <summary>
        /// UI 冒烟入口：按当前 settings 重新走一次变化计划。生产调用点直接
        /// apply.Apply(下一份配置)，这里只是让界面探针能重放「应用当前配置」。
        /// </summary>
        internal void ApplySettings()
        {
            apply.Apply(settings);
        }

        private static bool RelayDisabledByEnvironment()
        {
            return Environment.GetEnvironmentVariable("WC_NO_RELAY") == "1"
                || Environment.GetEnvironmentVariable("WC_NO_SAVE") == "1";
        }

        SettingsApplyState ISettingsApplyTarget.CaptureState()
        {
            var state = new SettingsApplyState();
            state.AttendancePollRunning = poll != null && poll.Enabled;
            state.CalendarPollRunning = calPoll != null && calPoll.Enabled;
            state.OutlookPollRunning = olPoll != null && olPoll.Enabled;
            state.NoteRelayRunning = noteRelay != null || RelayDisabledByEnvironment();
            state.SyncFingerprint = sync.ConfigFingerprint();
            return state;
        }

        void ISettingsApplyTarget.AdoptSettings(Settings next)
        {
            settings = next;
        }

        void ISettingsApplyTarget.ConfigureSync(Settings next)
        {
            sync.Configure(next);
            snapshot = sync.Snapshot().Attendance;
        }

        void ISettingsApplyTarget.ApplyAttendancePoll(Settings next)
        {
            poll.Interval = Math.Max(1, next.lark.pollMinutes) * 60 * 1000;
            poll.Stop();
            if (next.lark.enabled) poll.Start();
        }

        void ISettingsApplyTarget.ApplyCalendarPoll(Settings next)
        {
            calPoll.Interval = Math.Max(3, next.calendarPollMinutes) * 60 * 1000;
            calPoll.Stop();
            if (next.calendarEnabled && next.lark.enabled) calPoll.Start();
        }

        void ISettingsApplyTarget.ApplyOutlookPoll(Settings next)
        {
            olPoll.Interval = Math.Max(3, next.calendarPollMinutes) * 60 * 1000;
            olPoll.Stop();
            if (next.outlook != null && next.outlook.HasEnabled) olPoll.Start();
        }

        void ISettingsApplyTarget.RestartNoteRelay(Settings next)
        {
            RestartNoteRelay();
        }

        void ISettingsApplyTarget.RefreshNoteRelay(Settings next)
        {
            if (noteRelay != null) noteRelay.RefreshSubscriptions();
        }

        void ISettingsApplyTarget.ApplyFonts(Settings next)
        {
            UiFont.Configure(next.ui.textFont, next.ui.numberFont);
            CreateFonts();
        }

        void ISettingsApplyTarget.ApplyDisplayResources(Settings next)
        {
            theme = Skins.ResolveTheme(next.ui);
            if (BackColor != theme.bgBottom) BackColor = theme.bgBottom;
            frameAlpha = (byte)Math.Round(255 * Math.Max(0.3, Math.Min(1.0, next.ui.opacity / 100.0)));
            RefreshTrayIcon();
            Invalidate();
        }

        void ISettingsApplyTarget.ApplyTopMost(Settings next)
        {
            KeepTopMost();
        }

        void ISettingsApplyTarget.ApplyNotePanel(Settings next, bool relayout)
        {
            if (relayout)
            {
                CloseNotePanel();
                return;
            }
            if (notePanel != null) notePanel.RefreshFromSettings(next, theme);
        }

        void ISettingsApplyTarget.ApplySalaryState(Settings next)
        {
            if (salaryAnimating) FinishSalaryAnimNow();
            if (next.salary == null || next.salary.monthly <= 0m) salaryOpen = false;
        }

        void ISettingsApplyTarget.AfterApply(Settings next)
        {
            if (drawer != null) drawer.RebindSettings(next);
            Recompute();
            if (!collapsed && !animating && Height != ExpandedH)
            {
                Size = new Size(S(DesignExpandedW), ExpandedH);
                ApplyRegion();
            }
            Invalidate();
        }

        /// <summary>托盘图标跟随当前主题；只有显示资源变化时由 applier 调用。</summary>
        private void RefreshTrayIcon()
        {
            if (tray == null) return;
            try
            {
                Icon old = tray.Icon;
                tray.Icon = MakeTrayIcon();
                if (old != null) old.Dispose();
            }
            catch { }
        }

        // ---------------- 位置与尺寸 ----------------

        private Screen CurrentScreen()
        {
            return Screen.FromPoint(new Point(Math.Max(0, Left) + S(20), Math.Max(0, Top) + S(20)));
        }

        private void PlaceInitial()
        {
            Screen scr = Screen.PrimaryScreen;
            if (settings.ui.x >= 0 && IsPointVisible(settings.ui.x, settings.ui.y))
            {
                scr = Screen.FromPoint(new Point(settings.ui.x, settings.ui.y));
            }
            int y = settings.ui.y;
            if (y < -5000 || y > 20000) y = S(260);
            int width = collapsed ? S(DesignCollapsedW) : S(DesignExpandedW);
            int x;
            if (settings.ui.edge == "left") x = scr.WorkingArea.Left;
            else if (settings.ui.edge == "free") x = IsPointVisible(settings.ui.x, settings.ui.y) ? settings.ui.x : scr.WorkingArea.Right - width - S(12);
            else x = scr.WorkingArea.Right - width;
            y = Math.Max(scr.WorkingArea.Top, Math.Min(scr.WorkingArea.Bottom - Height, y));
            Location = new Point(x, y);
            // 存下来的位置可能落在屏幕外（换过显示器/缩放改过）：那就挪回主屏右上角，别让卡片「打开看不见」
            if (!IsRectVisible(new Rectangle(x, y, width, Height)))
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - S(12), Math.Max(wa.Top + S(40), wa.Top + (wa.Height - Height) / 3));
            }
        }

        private static bool IsRectVisible(Rectangle r)
        {
            foreach (Screen s in Screen.AllScreens)
                if (s.WorkingArea.IntersectsWith(r)) return true;
            return false;
        }

        private static bool IsPointVisible(int x, int y)
        {
            if (x < 0) return false;
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.WorkingArea.Contains(new Point(x, y + 10))) return true;
            }
            return false;
        }

        private int DockedX(int width)
        {
            Screen scr = CurrentScreen();
            if (settings.ui.edge == "left") return scr.WorkingArea.Left;
            return scr.WorkingArea.Right - width;
        }

        private void ApplyRegion()
        {
            // 分层窗口用像素 alpha 收圆角，不再设置 Region（Region 的边缘是没有抗锯齿的硬锯齿）
        }

        private static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d <= 1) d = 2;
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRegion();
        }

        private void StartAnim(bool collapse)
        {
            StartAnim(collapse, AnimMs);
        }

        /// <summary>带时长的展开/收起：警戒带那一路按 HTML 预览的节奏走（展开 0.9s、收回 0.7s）。</summary>
        private void StartAnim(bool collapse, int duration)
        {
            CloseDrawer();                    // 卡片要变尺寸了，抽屉先收掉
            if (salaryAnimating) FinishSalaryAnimNow();
            if (animating) FinishAnimNow();   // 动画还没走完也照样响应，先把它收尾
            animating = true;
            animDuration = Math.Max(60, duration);
            animFromW = Width; animFromH = Height; animFromX = Left;
            animToW = collapse ? S(DesignCollapsedW) : S(DesignExpandedW);
            animToH = collapse ? S(DesignCollapsedH) : ExpandedH;
            animToX = settings.ui.edge == "free" ? Left : DockedX(animToW);
            store.Log((collapse ? "收起" : "展开") + "动画: " + Width + "x" + Height + " -> " + animToW + "x" + animToH);
            animStart = Environment.TickCount;
            // 展开时错峰从这一刻就算起：窗口一边长高，时间、进度条和其余行一边各就各位
            expandAnim = !collapse;
            expandStart = animStart;
            animT.Start();
        }

        private void StepAnim()
        {
            // 工资面板的展开 / 收起动画
            if (salaryAnimating && !animating) { StepSalaryAnim(); return; }
            // 看门狗：动画超时还没结束就直接收尾，免得一直卡在「点击没反应」的状态
            if (animating && Environment.TickCount - animStart > animDuration + 1200)
            {
                FinishAnimNow();
                return;
            }
            if (!animating)
            {
                // 尺寸已经到位，只剩各行错峰淡入
                if (expandAnim && Environment.TickCount - expandStart <= StaggerTotalMs)
                {
                    Invalidate();
                    return;
                }
                expandAnim = false;
                animT.Stop();
                return;
            }
            int elapsed = Environment.TickCount - animStart;
            double t = Math.Min(1.0, Math.Max(0.0, elapsed / (double)animDuration));
            double e = 1 - Math.Pow(1 - t, 3);
            int w = (int)Math.Round(animFromW + (animToW - animFromW) * e);
            int h = (int)Math.Round(animFromH + (animToH - animFromH) * e);
            int x = (int)Math.Round(animFromX + (animToX - animFromX) * e);
            bool wasCollapsed = collapsed;
            collapsed = animToW == S(DesignCollapsedW);
            if (wasCollapsed != collapsed) ApplyRegion();
            SetBounds(x, Top, Math.Max(1, w), Math.Max(1, h));
            if (t >= 1.0)
            {
                animating = false;
                collapsed = animToW == S(DesignCollapsedW);
                Invalidate();
                if (!collapsed) ResetAutoCollapse();
                if (expandAnim && Environment.TickCount - expandStart <= StaggerTotalMs) return;
                animT.Stop();
            }
        }

        private void SetCollapsed(bool value)
        {
            // 提醒期间不收起，但**允许展开**：日程提醒常驻时，点小卡要能看大卡
            if (reminding && value) return;
            if (salaryAnimating) FinishSalaryAnimNow();
            // 上一段动画万一没走完（掉帧、被系统挂起），这里先把它收尾，
            // 否则 animating 一直为 true，点击就彻底没反应了
            if (animating) FinishAnimNow();
            if (collapsed == value)
            {
                SyncBounds();
                return;
            }
            StartAnim(value);
        }

        /// <summary>单击右上角 ¥：展开 / 收起工资面板。没配月工资就直接跳到「工资」设置页。</summary>
        private void ToggleSalary()
        {
            if (settings.salary == null || settings.salary.monthly <= 0m)
            {
                OpenSettings(5);
                return;
            }
            if (salaryAnimating) FinishSalaryAnimNow();

            if (!salaryOpen)
            {
                salaryOpen = true;
                store.Log("工资面板: 展开");
                if (collapsed) { SetCollapsed(false); return; }
                BeginSalaryAnim(true);
            }
            else
            {
                store.Log("工资面板: 收起");
                BeginSalaryAnim(false);
            }
        }

        /// <summary>展开 / 收起工资面板：尺寸按缓动长高 / 缩短，内容按行浮现（见 SalaryRowProgress）。</summary>
        private void BeginSalaryAnim(bool open)
        {
            if (screenReminder) return;
            if (animating) FinishAnimNow();
            int baseH = S(CardHeightDip(ShowLunchRow));
            if (open) salaryOpen = true;
            int toH = open ? baseH + S(SalaryPanelDip) : baseH;
            salaryAnimating = true;
            salaryAnimOpen = open;
            salaryAnimStart = Environment.TickCount;
            salaryAnimDuration = open ? 430 : 300;
            salaryAnimFromH = Height;
            salaryAnimToH = toH;
            if (Math.Abs(Height - toH) <= 1)
            {
                salaryAnimating = false;
                if (!open) salaryOpen = false;
                SetBounds(Left, Top, Width, toH);
                ApplyRegion();
                ResetAutoCollapse();
                Invalidate();
                return;
            }
            store.Log((open ? "工资面板展开动画" : "工资面板收起动画") + ": " + Height + " -> " + toH);
            animT.Interval = 15;
            animT.Start();
        }

        private void StepSalaryAnim()
        {
            int elapsed = Environment.TickCount - salaryAnimStart;
            double t = Math.Min(1.0, Math.Max(0.0, elapsed / (double)Math.Max(1, salaryAnimDuration)));
            double e = salaryAnimOpen ? 1 - Math.Pow(1 - t, 3) : t * t;
            int h = (int)Math.Round(salaryAnimFromH + (salaryAnimToH - salaryAnimFromH) * e);
            SetBounds(Left, Top, Width, Math.Max(1, h));
            ApplyRegion();
            if (t >= 1.0)
            {
                salaryAnimating = false;
                if (!salaryAnimOpen)
                {
                    salaryOpen = false;
                    SetBounds(Left, Top, Width, salaryAnimToH);
                    ApplyRegion();
                }
                animT.Stop();
                ResetAutoCollapse();
            }
            Invalidate();
        }

        private void FinishSalaryAnimNow()
        {
            if (!salaryAnimating) return;
            salaryAnimating = false;
            salaryOpen = salaryAnimOpen;
            SetBounds(Left, Top, Width, salaryAnimToH);
            ApplyRegion();
            animT.Stop();
            Invalidate();
        }

        /// <summary>工资面板的逐行进度：header / 数字 / 本月收入 三行依次浮现；收起时反过来。</summary>
        private void SalaryRowProgress(out double head, out double money, out double stats)
        {
            double elapsed;
            bool closing = false;
            if (salaryAnimating)
            {
                elapsed = Environment.TickCount - salaryAnimStart;
                closing = !salaryAnimOpen;
            }
            else if (expandAnim && salaryOpen)
            {
                elapsed = Environment.TickCount - expandStart;
            }
            else
            {
                head = 1; money = 1; stats = 1;
                return;
            }
            if (closing)
            {
                head = Clamp01(1 - elapsed / 140.0);
                money = Clamp01(1 - Math.Max(0, elapsed - 30) / 150.0);
                stats = Clamp01(1 - Math.Max(0, elapsed - 70) / 160.0);
            }
            else
            {
                head = Clamp01(elapsed / 180.0);
                money = Clamp01((elapsed - 60) / 220.0);
                stats = Clamp01((elapsed - 140) / 220.0);
            }
        }

        /// <summary>工资面板展开 / 收起后，把卡片高度直接调到 ExpandedH（截图探针 / 设置变化用）。</summary>
        private void ResizeForSalary()
        {
            if (screenReminder) return;
            if (salaryAnimating) FinishSalaryAnimNow();
            if (collapsed)
            {
                SetCollapsed(false);
                ResetAutoCollapse();
                Invalidate();
                return;
            }
            if (animating) FinishAnimNow();
            int h = ExpandedH;
            if (Height != h)
            {
                Size = new Size(S(DesignExpandedW), h);
                ApplyRegion();
            }
            ResetAutoCollapse();
            Invalidate();
        }

        /// <summary>把动画直接推到终点：尺寸、状态、展开错峰一起收干净。</summary>
        private void FinishAnimNow()
        {
            animT.Stop();
            animating = false;
            collapsed = animToW == S(DesignCollapsedW);
            if (!collapsed)
            {
                // 直接给最终布局，不再错峰
                expandAnim = false;
                SetBounds(animToX, Top, animToW, animToH);
                ResetAutoCollapse();
            }
            else
            {
                expandAnim = false;
                SetBounds(animToX, Top, animToW, animToH);
            }
            SyncBounds();
            Invalidate();
        }

        /// <summary>尺寸和 collapsed 状态对不上时（极少数情况）自己纠正一次。</summary>
        private void SyncBounds()
        {
            if (screenReminder) return;
            int w = collapsed ? S(DesignCollapsedW) : S(DesignExpandedW);
            int h = collapsed ? S(DesignCollapsedH) : ExpandedH;
            if (Width != w || Height != h) SetBounds(Left, Top, w, h);
        }

        private void ResetAutoCollapse()
        {
            autoCollapse.Stop();
            if (collapsed) return;
            int seconds = settings.ui.autoCollapseSeconds;
            if (seconds <= 0) return;
            autoCollapse.Interval = Math.Max(1000, seconds * 1000);
            autoCollapse.Start();
        }

        private void MaybeAutoCollapse()
        {
            if (screenReminder) return;
            if (collapsed || animating || reminding) return;
            if (ClientRectangle.Contains(PointToClient(Cursor.Position))) { ResetAutoCollapse(); return; }
            SetCollapsed(true);
        }

        // ---------------- 绘制 ----------------

        // ---------------- 分层窗口 ----------------

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
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly IntPtr HwndTopMost = new IntPtr(-1);
        private const int GwlExStyle = -20;
        private const int WsExTopMost = 0x00000008;
        private const int WsExLayered = 0x00080000;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoOwnerZOrder = 0x0200;

        private const int UlwAlpha = 0x2;
        private const byte AcSrcAlpha = 0x1;


        /// <summary>把整帧位图按像素 alpha 推到屏幕上：圆角、文字、图标都走同一条抗锯齿路径。</summary>
        private void PushFrame()
        {
            if (frame == null) return;
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = frame.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                oldBitmap = SelectObject(memDc, hBitmap);
                var size = new NativeSize();
                size.cx = screenReminder ? screenW : frame.Width;
                size.cy = screenReminder ? screenH : frame.Height;
                var src = new NativePoint();
                var dst = new NativePoint();
                dst.X = screenReminder ? screenBounds.Left : Left;
                dst.Y = screenReminder ? screenBounds.Top : Top;
                var blend = new BlendFunction();
                blend.BlendOp = 0;
                blend.SourceConstantAlpha = frameAlpha;
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

        /// <summary>分层窗口由 PushFrame 推像素，这里不能清背景，否则会闪一下白底。</summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            int fw = screenReminder ? screenRenderW : Width;
            int fh = screenReminder ? screenRenderH : Height;
            if (frame == null || frame.Width != fw || frame.Height != fh)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(fw, fh, PixelFormat.Format32bppArgb);
            }
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // 卡面是不透明底色，ClearType 才有意义；边缘的圆角靠 alpha 收，所以不会出现彩边
                g.TextRenderingHint = screenReminder || !UseClearType ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.ClearTypeGridFit;
                if (screenReminder) RenderScreenReminder(g); else RenderCard(g);
            }
            // 离屏渲染（测试探针）拿到的就是这个位图；真实窗口再由 PushFrame 推像素
            if (e.Graphics != null) e.Graphics.DrawImage(frame, 0, 0);
            if (IsHandleCreated && Visible) PushFrame();
        }

        /// <summary>整张卡片的内容：底色、文字、图标、提醒层，全在这一个 Graphics 上完成。</summary>
        private void RenderCard(Graphics g)
        {
            if (status == null) Recompute();
            if (theme == null) theme = Skins.ResolveTheme(settings.ui);
            SkinDef skin = Skin;
            using (var path = CardPath())
            {
                if (Skins.UsesGlass(settings.ui))
                    Frost.PaintSurface(g, path, new Rectangle(0, 0, Width, Height), theme, 1, 1);
                else
                    using (var brush = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), theme.bgTop, theme.bgBottom, 90f))
                        g.FillPath(brush, path);
                // 皮肤质感：顶部高光（默认皮肤 sheenAlpha=0，什么都不画）→ 纸纹（宣纸）
                PaintSkinSheen(g, path, skin);
                if (skin.paperGrain)
                    Frost.PaintPaperGrain(g, path, new Rectangle(0, 0, Width, Height), theme.textMain, 2.4);
                PaintCardGlow(g, path);
                if (!reminding)
                {
                    if (collapsed) PaintCollapsed(g); else PaintExpanded(g);
                }
                else if (remindEvent != null && remindEvent.source == "alert")
                {
                    double elapsed = ReminderClock();
                    bool quietNow = settings.ui.reducedMotion || SystemInformation.HighContrast;
                    double exit = elapsed - RemindInMs - RemindHoldMs;
                    double contentA = quietNow ? 1 : Clamp01((elapsed - 470) / 300) * (1 - Clamp01(exit / 150.0));
                    if (collapsed) PaintCollapsed(g); else PaintExpanded(g);
                    if (elapsed < RemindInMs + RemindHoldMs + RemindOutMs)
                    {
                        using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                        {
                            bitmap.SetResolution(g.DpiX, g.DpiY);
                            using (var layer = Graphics.FromImage(bitmap))
                            {
                                // 提醒层是半透明的：用灰阶抗锯齿，ClearType 在透明底上会镶彩边
                                layer.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                                layer.SmoothingMode = SmoothingMode.AntiAlias;
                                layer.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                layer.Clear(Color.Transparent);
                                var clip = layer.Clip;
                                // 提醒层也按卡片的圆角裁一刀，否则两块色块会把圆角糊成直角
                                using (var cardPath = CardPath())
                                    layer.SetClip(cardPath, CombineMode.Intersect);
                                PaintReminder(layer, elapsed, quietNow, 1, contentA);
                                layer.Clip = clip;
                            }
                            g.DrawImage(bitmap, 0, 0, Width, Height);
                        }
                    }
                }
                else
                {
                    // 警戒带模式：卡片不做色块扫入/覆盖，保持普通卡片，信息交给警戒带和抽屉
                    if (tapeActive)
                    {
                        if (collapsed) PaintCollapsed(g); else PaintExpanded(g);
                    }
                    else PaintCardReminder(g, ReminderClock());
                }
                // 提醒动画期间不描边：浅色主题下这一圈线会压在色块上显出一条亮边。
                if (!reminding)
                {
                    DrawCardBorder(g, path, skin);
                    if (skin.screws) DrawScrews(g);
                    DrawNoteRim(g, path);
                    DrawNoteCorner(g, path);
                }
            }
        }

        // ---------------- 皮肤质感（顶部高光 / 纸纹 / 描边 / 霓虹边 / 螺丝） ----------------

        /// <summary>
        /// 皮肤指定的圆角：默认皮肤沿用原来的两条路径（收起 14 / 展开 18），提醒层和光晕层也走这里。
        /// </summary>
        private GraphicsPath CardPath()
        {
            return RoundPath(new Rectangle(0, 0, Width, Height), CardRadiusPx);
        }

        /// <summary>
        /// 卡片描边。默认皮肤照旧（1 物理像素、accent @ 24/255）；
        /// 其它皮肤按 skin.borderWidth + Skins.CardBorderColor；霓虹走品红→青渐变 + 内侧辉光。
        /// </summary>
        private void DrawCardBorder(Graphics g, GraphicsPath path, SkinDef skin)
        {
            if (skin.IsDefault)
            {
                using (var pen = new Pen(Color.FromArgb(24, theme.accent), Math.Max(1, scale))) g.DrawPath(pen, path);
                return;
            }
            if (skin.neonEdge)
            {
                DrawNeonEdge(g, path, skin);
                return;
            }
            // 笔画正好落在圆角路径内侧：Path 贴着窗口边，笔画一半会在窗口外，
            // 所以按 2 倍线宽画、再用卡片路径裁一刀，露出来的就是 borderWidth 那么宽的一条。
            int width = Math.Max(1, skin.borderWidth);
            var saved = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            using (var pen = new Pen(Skins.CardBorderColor(skin, theme), Math.Max(1f, 2f * S(width))))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, path);
            }
            g.Restore(saved);
        }

        /// <summary>霓虹：品红→青渐变笔画，外加三层递减的内侧辉光（卡片外面的辉光交给 CardGlowForm）。</summary>
        private void DrawNeonEdge(Graphics g, GraphicsPath path, SkinDef skin)
        {
            var saved = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            // 由外到内：宽而淡的辉光 → 贴着边的一条实线
            float[] widths = new float[] { 13f, 8.5f, 5.2f, Math.Max(1f, skin.borderWidth * 2f) };
            double[] alphas = new double[] { 0.035, 0.07, 0.15, 1.0 };
            for (int i = 0; i < widths.Length; i++)
            {
                using (var brush = NeonEdgeBrush(alphas[i]))
                using (var pen = new Pen(brush, Math.Max(1f, S(widths[i]))))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }
            g.Restore(saved);
        }

        /// <summary>霓虹描边用的品红→紫→青渐变（按整张卡片对角线铺色）。</summary>
        private LinearGradientBrush NeonEdgeBrush(double alpha)
        {
            Color hot = theme.accent;
            Color cool = Skin.accent2.IsEmpty ? theme.accent : Skin.accent2;
            var rect = new Rectangle(0, 0, Math.Max(2, Width), Math.Max(2, Height));
            var brush = new LinearGradientBrush(rect, Color.Black, Color.Black, 35f);
            var blend = new ColorBlend();
            blend.Colors = new[] { Skins.Alpha(hot, alpha), Skins.Alpha(Skins.Mix(hot, cool, 0.5), alpha), Skins.Alpha(cool, alpha) };
            blend.Positions = new[] { 0f, 0.55f, 1f };
            brush.InterpolationColors = blend;
            return brush;
        }

        /// <summary>顶部高光：skin.sheenAlpha &gt; 0 时在卡片内画一层自上而下、透明收边的白色高光。</summary>
        private void PaintSkinSheen(Graphics g, GraphicsPath path, SkinDef skin)
        {
            double sheen = skin == null ? 0 : Math.Max(0, Math.Min(1, skin.sheenAlpha));
            if (sheen <= 0.004) return;
            int top = (int)Math.Round(255 * sheen);
            if (top <= 0) return;
            var saved = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            var rect = new Rectangle(0, 0, Math.Max(2, Width), Math.Max(2, Height));
            using (var brush = new LinearGradientBrush(rect, Color.FromArgb(top, Color.White), Color.FromArgb(0, Color.White), 90f))
            {
                var blend = new ColorBlend();
                blend.Colors = new[]
                {
                    Color.FromArgb(top, Color.White),
                    Color.FromArgb((int)Math.Round(top * 0.38), Color.White),
                    Color.FromArgb(0, Color.White),
                    Color.FromArgb(0, Color.White)
                };
                blend.Positions = new[] { 0f, 0.22f, 0.48f, 1f };
                brush.InterpolationColors = blend;
                g.FillRectangle(brush, rect);
            }
            g.Restore(saved);
        }

        /// <summary>仪表：四角 4 颗小螺丝（半径约 2.4 DIP，左上受光、右下背光的深色凹凸）。</summary>
        private void DrawScrews(Graphics g)
        {
            int inset = S(7);
            int r = Math.Max(2, S(2.4f));
            Color body = Skins.Mix(theme.bgBottom, Color.Black, 0.26);
            Color slot = Skins.Mix(theme.bgBottom, Color.Black, 0.55);
            var corners = new[]
            {
                new Point(inset, inset), new Point(Width - inset, inset),
                new Point(inset, Height - inset), new Point(Width - inset, Height - inset)
            };
            foreach (var c in corners)
            {
                var box = new Rectangle(c.X - r, c.Y - r, r * 2, r * 2);
                using (var brush = new SolidBrush(body)) g.FillEllipse(brush, box);
                // 左上受光、右下背光：一圈 1 物理像素的斜向高光/暗边
                float w = Math.Max(1f, scale);
                using (var pen = new Pen(Color.FromArgb(74, Color.White), w))
                    g.DrawArc(pen, box.X, box.Y, box.Width, box.Height, 175f, 100f);
                using (var pen = new Pen(Color.FromArgb(96, Color.Black), w))
                    g.DrawArc(pen, box.X + w, box.Y + w, Math.Max(2, box.Width - w * 2), Math.Max(2, box.Height - w * 2), 10f, 90f);
                // 中间的十字槽
                using (var pen = new Pen(slot, Math.Max(1f, scale)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    int s = Math.Max(1, r / 2);
                    g.DrawLine(pen, c.X - s, c.Y, c.X + s, c.Y);
                    g.DrawLine(pen, c.X, c.Y - s, c.X, c.Y + s);
                }
            }
        }

        // ---------------- 卡片背景光效 ----------------

        private const double CardGlowStrength = 1.6;

        /// <summary>
        /// 卡片背景的三层色光：角落柔光 A、进度条同色光 B、内缘轮廓光 D。
        /// 设置里可单选或三种都开；强度固定 1.6×（和 HTML 预览的 1.6 对齐）。
        /// 光色取 Skins.GlowColor：默认皮肤就是原来的 theme.accent。
        /// </summary>
        private void PaintCardGlow(Graphics g, GraphicsPath cardPath)
        {
            string mode = settings.ui.cardGlow;
            if (mode != "corner" && mode != "bar" && mode != "all") return;   // classic = 上个版本原样
            bool all = mode == "all";
            bool a = all || mode == "corner";
            bool b = all || mode == "bar";
            double damp = all ? 0.70 : 1.0;
            Color accent = Skins.GlowColor(Skin, theme);
            var saved = g.Save();
            g.SetClip(cardPath, CombineMode.Intersect);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (a)
            {
                DrawGlowEllipse(g, new RectangleF(-Width * 0.28f, -Height * 0.35f, Width * 1.1f, Height * 1.2f),
                    new PointF(0, 0), accent, 0.30 * CardGlowStrength * damp);
                DrawGlowEllipse(g, new RectangleF(Width * 0.25f, Height * 0.3f, Width * 1.1f, Height * 1.2f),
                    new PointF(Width, Height), accent, 0.15 * CardGlowStrength * damp);
            }
            if (b)
            {
                float cx = Width * 0.58f;
                float cy = (collapsed ? S(59) : S(102)) + S(2);
                float rx = Width * 0.60f;
                float ry = Height * 0.38f;
                DrawGlowEllipse(g, new RectangleF(cx - rx, cy - ry, rx * 2, ry * 2),
                    new PointF(cx, cy), accent, 0.32 * CardGlowStrength * damp);
            }
            g.Restore(saved);
        }

        private static void DrawGlowEllipse(Graphics g, RectangleF rect, PointF center, Color color, double alpha)
        {
            alpha = Math.Max(0, Math.Min(1, alpha));
            if (alpha <= 0.003) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterPoint = center;
                    br.CenterColor = Color.FromArgb((int)Math.Round(255 * alpha), color);
                    br.SurroundColors = new[] { Color.FromArgb(0, color) };
                    br.FocusScales = new PointF(0.05f, 0.05f);
                    g.FillPath(br, path);
                }
            }
        }

        private const double CardFloodDelayMs = 100;
        private const double CardCircleFloodMs = 420;
        private const double CardBarHMs = 200;
        private const double CardBarVStartMs = 280;
        private const double CardBarVMs = 240;
        private const double CardRevealStartMs = 520;
        private const double CardRevealMs = 300;
        private const double CardOutMs = 240;
        private const double CardShrinkMs = 360;

        /// <summary>
        /// 卡片内的提醒入场：先让普通卡片露出来，再从进度条已走段的末端做一块柔边扩散，
        /// 铺满卡片后标题和下方第二块同时擦出。退场反过来。
        /// </summary>
        private void PaintCardReminder(Graphics g, double t)
        {
            bool bar = settings.StyleOf(Reminders.KindOf(remindEvent)).shape == "bar";
            if (settings.ui.reducedMotion || SystemInformation.HighContrast)
            {
                using (var hold = ReminderHoldBitmap())
                    g.DrawImageUnscaled(hold, 0, 0);
                return;
            }

            if (collapsed) PaintCollapsed(g); else PaintExpanded(g);

            double end = RemindInMs + RemindHoldMs;
            if (t >= CardFloodDelayMs) DrawCardFlood(g, t, bar, end);
            double reveal = CardReveal(t, end);
            if (reveal > 0) DrawCardReveal(g, reveal, bar);
        }

        private double CardReveal(double t, double end)
        {
            double p = EaseOutCubic(Clamp01((t - CardRevealStartMs) / CardRevealMs));
            double o = EaseInCubic(Clamp01((t - end) / CardOutMs));
            return Math.Max(0, p * (1 - o));
        }

        /// <summary>扩散的软边：整块 A 色按逐像素 alpha 叠在普通卡片上，边缘按距离柔化。</summary>
        private void DrawCardFlood(Graphics g, double t, bool bar, double end)
        {
            int pad = S(18);
            int barY = collapsed ? S(59) : S(102);
            int barH = collapsed ? S(3) : S(4);
            double progress = status == null ? 0 : Math.Max(0, Math.Min(1, status.progress));
            double tipX = pad + (Width - pad * 2) * progress;
            double tipY = barY + barH / 2.0;

            double outStart = end + CardOutMs;
            Func<int, int, double> mask;
            if (bar)
            {
                double p1 = Smooth(Clamp01((t - CardFloodDelayMs) / CardBarHMs));
                double p2 = Smooth(Clamp01((t - CardBarVStartMs) / CardBarVMs));
                int left = (int)Math.Round(tipX - p1 * tipX);
                int right = (int)Math.Round(tipX + p1 * (Width - tipX));
                int top = (int)Math.Round(tipY - 3 - p2 * (tipY - 3));
                int bottom = (int)Math.Round(tipY + 3 + p2 * (Height - tipY - 3));
                double colY = EaseInCubic(Clamp01((t - outStart) / 160.0));
                double colX = EaseInCubic(Clamp01((t - (outStart + 140)) / 220.0));
                if (t > outStart) { left = (int)Math.Round(left + colX * (tipX - left)); right = (int)Math.Round(right - colX * (right - tipX)); }
                if (colY > 0) { top = (int)Math.Round(top + colY * (tipY - 3 - top)); bottom = (int)Math.Round(bottom - colY * (bottom - (tipY + 3))); }
                if (right - left < 2 || bottom - top < 2) return;
                double fl = left > 0 ? S(10) : 0, fr = right < Width ? S(10) : 0;
                double ft = top > 0 ? Math.Min(S(10), (bottom - top) * 0.25) : 0;
                double fb = bottom < Height ? Math.Min(S(10), (bottom - top) * 0.25) : 0;
                mask = (x, y) =>
                {
                    if (x < left || x >= right || y < top || y >= bottom) return 0;
                    double a = 1;
                    if (fl > 0) a = Math.Min(a, Smooth((x - left) / fl));
                    if (fr > 0) a = Math.Min(a, Smooth((right - x) / fr));
                    if (ft > 0) a = Math.Min(a, Smooth((y - top) / ft));
                    if (fb > 0) a = Math.Min(a, Smooth((bottom - y) / fb));
                    return a;
                };
            }
            else
            {
                double p = Smooth(Clamp01((t - CardFloodDelayMs) / CardCircleFloodMs));
                double outP = EaseInCubic(Clamp01((t - outStart) / CardShrinkMs));
                double maxR = 0;
                foreach (var pt in new[] { new Point(0, 0), new Point(Width, 0), new Point(0, Height), new Point(Width, Height) })
                    maxR = Math.Max(maxR, Math.Sqrt((pt.X - tipX) * (pt.X - tipX) + (pt.Y - tipY) * (pt.Y - tipY)));
                double r = (maxR + S(8)) * p * (1 - outP);
                if (r < 1) return;
                double feather = Math.Max(S(7), Math.Min(S(18), r * 0.06));
                mask = (x, y) =>
                {
                    double dx = x - tipX, dy = y - tipY;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= r - feather) return 1;
                    if (d >= r) return 0;
                    return Smooth((r - d) / feather);
                };
            }

            Color ca, cbn, cink;
            ReminderColors(out ca, out cbn, out cink);
            using (var layer = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                var rect = new Rectangle(0, 0, Width, Height);
                var bits = layer.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                int w = Width, h = Height;
                var data = new int[w * h];
                int cr = ca.R, cg = ca.G, cb = ca.B;
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        double al = mask(x, y);
                        if (al <= 0.004) continue;
                        int ai = (int)Math.Round(255 * Clamp01(al));
                        data[row + x] = (ai << 24) | (cr << 16) | (cg << 8) | cb;
                    }
                }
                Marshal.Copy(data, 0, bits.Scan0, data.Length);
                layer.UnlockBits(bits);

                var saved = g.Save();
                using (var cardPath = CardPath())
                    g.SetClip(cardPath, CombineMode.Replace);
                g.DrawImageUnscaled(layer, 0, 0);
                g.Restore(saved);
            }
        }

        /// <summary>下方第二块和上方标题同时擦出：圆形样式上下来、进度条样式共用一条左→右擦线。</summary>
        private void DrawCardReveal(Graphics g, double reveal, bool bar)
        {
            using (var hold = ReminderHoldBitmap())
            {
                int seam = collapsed ? S(37) : S(64);
                var saved = g.Save();
                using (var cardPath = CardPath())
                {
                    g.SetClip(cardPath, CombineMode.Replace);
                    if (bar)
                    {
                        int wipeX = (int)Math.Round(Width * reveal);
                        if (wipeX > 0)
                        {
                            var top = new Rectangle(0, 0, wipeX, seam);
                            var bottom = new Rectangle(0, seam, wipeX, Height - seam);
                            g.SetClip(top, CombineMode.Intersect);
                            g.DrawImage(hold, top, 0, 0, wipeX, seam, GraphicsUnit.Pixel);
                            g.SetClip(cardPath, CombineMode.Replace);
                            g.SetClip(bottom, CombineMode.Intersect);
                            g.DrawImage(hold, bottom, 0, seam, wipeX, Height - seam, GraphicsUnit.Pixel);
                        }
                    }
                    else
                    {
                        int topH = (int)Math.Round(seam * reveal);
                        if (topH > 0)
                        {
                            var top = new Rectangle(0, 0, Width, topH);
                            g.SetClip(top, CombineMode.Intersect);
                            g.DrawImage(hold, top, 0, 0, Width, topH, GraphicsUnit.Pixel);
                        }
                        int off = (int)Math.Round((1 - reveal) * (Height - seam + 4));
                        var dest = new Rectangle(0, seam + off, Width, Height - seam);
                        g.SetClip(cardPath, CombineMode.Replace);
                        g.DrawImage(hold, dest, 0, seam, Width, Height - seam, GraphicsUnit.Pixel);
                    }
                }
                g.Restore(saved);
            }
        }

        /// <summary>提醒停留时的整卡画面（两块色 + 文字 + 关闭按钮），用作擦出时的源图。</summary>
        private Bitmap ReminderHoldBitmap()
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                using (var cardPath = CardPath())
                    g.SetClip(cardPath, CombineMode.Intersect);
                PaintReminder(g, RemindInMs + RemindHoldMs, false, 1, 1);
            }
            return bmp;
        }

        /// <summary>全屏提醒：柔边扩散从进度条末端铺满整屏，然后标题和下方色块同时擦出。</summary>
        private void RenderScreenReminder(Graphics g)
        {
            if (remindEvent == null || screenKindFont == null) return;
            int W = screenRenderW, H = screenRenderH;
            float L = (float)screenLayout;
            double t = ReminderClock();
            double end = RemindInMs + RemindHoldMs;
            bool bar = settings.StyleOf(Reminders.KindOf(remindEvent)).shape == "bar";
            Color a, b, ink;
            ReminderColors(out a, out b, out ink);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (screenCardUnder != null) g.DrawImageUnscaled(screenCardUnder, screenRestoreBounds.Left - screenBounds.Left, screenRestoreBounds.Top - screenBounds.Top);

            double reveal = ScreenReveal(t, bar, end);
            bool full = false;
            if (bar)
            {
                double p1 = Smooth(Clamp01((t - 100) / 280.0));
                double p2 = Smooth(Clamp01((t - 360) / 260.0));
                double outStart = end + 260;
                double colY = EaseInCubic(Clamp01((t - outStart) / 200.0));
                double colX = EaseInCubic(Clamp01((t - (outStart + 140)) / 220.0));
                int left = (int)Math.Round(screenTipX - p1 * screenTipX);
                int right = (int)Math.Round(screenTipX + p1 * (W - screenTipX));
                int top = (int)Math.Round(screenTipY - 3 - p2 * (screenTipY - 3));
                int bottom = (int)Math.Round(screenTipY + 3 + p2 * (H - screenTipY - 3));
                if (t > outStart) { left = (int)Math.Round(left + colX * (screenTipX - left)); right = (int)Math.Round(right - colX * (right - screenTipX)); }
                if (colY > 0) { top = (int)Math.Round(top + colY * (screenTipY - 3 - top)); bottom = (int)Math.Round(bottom - colY * (bottom - (screenTipY + 3))); }
                full = left <= 0 && right >= W && top <= 0 && bottom >= H;
                if (t >= 100 && right - left > 1 && bottom - top > 1)
                {
                    if (full)
                    {
                        using (var br = new SolidBrush(a)) g.FillRectangle(br, 0, 0, W, H);
                    }
                    else
                    {
                        bool horizontal = p2 < 0.5;
                        var band = new Rectangle(left, top, right - left, bottom - top);
                        double f = Math.Min(40 * L, (horizontal ? band.Width : band.Height) / 3.0);
                        using (var br = new LinearGradientBrush(band, Color.Transparent, a, horizontal ? 0f : 90f))
                        {
                            var cb = new ColorBlend(4);
                            if (horizontal)
                            {
                                float u = (float)(f / Math.Max(1.0, band.Width));
                                cb.Positions = new[] { 0f, u, 1f - u, 1f };
                            }
                            else
                            {
                                float u = (float)(f / Math.Max(1.0, band.Height));
                                cb.Positions = new[] { 0f, u, 1f - u, 1f };
                            }
                            cb.Colors = new[] { Color.FromArgb(0, a), a, a, Color.FromArgb(0, a) };
                            br.InterpolationColors = cb;
                            g.FillRectangle(br, band);
                        }
                    }
                }
            }
            else
            {
                double p = Smooth(Clamp01((t - 100) / 500.0));
                double outStart = end + 260;
                double outP = EaseInCubic(Clamp01((t - outStart) / 400.0));
                double maxR = 0;
                foreach (var pt in new[] { new Point(0, 0), new Point(W, 0), new Point(0, H), new Point(W, H) })
                    maxR = Math.Max(maxR, Math.Sqrt((pt.X - screenTipX) * (pt.X - screenTipX) + (pt.Y - screenTipY) * (pt.Y - screenTipY)));
                maxR += 40 * L;
                double r = maxR * p * (1 - outP);
                full = r >= maxR;
                if (r > 1)
                {
                    if (full)
                    {
                        using (var br = new SolidBrush(a)) g.FillRectangle(br, 0, 0, W, H);
                    }
                    else
                    {
                        double feather = Math.Max(10 * L, Math.Min(90 * L, r * 0.07));
                        float focus = (float)Math.Max(0.86, Math.Min(0.985, 1 - feather / r));
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse((float)(screenTipX - r), (float)(screenTipY - r), (float)(r * 2), (float)(r * 2));
                            using (var br = new PathGradientBrush(path))
                            {
                                br.CenterPoint = new PointF(screenTipX, screenTipY);
                                br.CenterColor = a;
                                br.SurroundColors = new[] { Color.FromArgb(0, a) };
                                br.FocusScales = new PointF(focus, focus);
                                g.FillPath(br, path);
                            }
                        }
                    }
                }
            }

            if (reveal <= 0) return;

            int blockTop = (int)Math.Round(420 * L);
            Action<Graphics> title = delegate (Graphics gg) { DrawScreenTitle(gg, ink, L); };
            Action<Graphics> block = delegate (Graphics gg) { DrawScreenBlock(gg, b, ink, L); };
            if (bar)
            {
                int wipeX = (int)Math.Round(W * reveal);
                var clip = new Rectangle(0, 0, wipeX, H);
                var sv = g.Save();
                g.IntersectClip(clip);
                title(g);
                block(g);
                g.Restore(sv);
            }
            else
            {
                int titleH = (int)Math.Round(260 * L * reveal);
                if (titleH > 0)
                {
                    var sv = g.Save();
                    g.IntersectClip(new Rectangle(0, 0, W, titleH));
                    title(g);
                    g.Restore(sv);
                }
                int off = (int)Math.Round((1 - reveal) * (H - blockTop + 8 * L));
                var sv2 = g.Save();
                g.TranslateTransform(0, off);
                block(g);
                g.Restore(sv2);
            }
        }

        private double ScreenReveal(double t, bool bar, double end)
        {
            double start = bar ? 650 : 600;
            double p = EaseOutCubic(Clamp01((t - start) / 320.0));
            double o = EaseInCubic(Clamp01((t - end) / 260.0));
            return Math.Max(0, p * (1 - o));
        }

        private void DrawScreenTitle(Graphics g, Color ink, float L)
        {
            int W = screenRenderW;
            string kind = KindLabel(remindEvent, Reminders.IsMeeting(remindEvent),
                FixedOf(remindEvent) == null ? null : FixedOf(remindEvent).name);
            ScreenText(g, kind, screenKindFont, WithAlphaLocal(ink, 0.62), SX(74), SY(56), SX(600), SY(32));
            ScreenText(g, remindEvent.title, screenTitleFont, ink, SX(68), SY(88), W - SX(136), SY(104));
            string count = Reminders.CountdownText(settings, remindEvent, DateTime.Now);
            if (!string.IsNullOrEmpty(count))
                ScreenText(g, count, screenCountFont, WithAlphaLocal(ink, 0.78), SX(74), SY(202), SX(700), SY(44));

            float x = W - SX(74), y = SY(58), d = SX(20);
            using (var pen = new Pen(WithAlphaLocal(ink, 0.62), Math.Max(1.6f, SX(1.7f))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, x, y, x + d, y + d);
                g.DrawLine(pen, x + d, y, x, y + d);
            }
            rectDismiss = new RectangleF(x * screenW / (float)screenRenderW, y * screenH / (float)screenRenderH,
                d * screenW / (float)screenRenderW, d * screenH / (float)screenRenderH);
        }

        private void DrawScreenBlock(Graphics g, Color b, Color ink, float L)
        {
            int W = screenRenderW, H = screenRenderH;
            int top = SY(420);
            using (var br = new SolidBrush(b)) g.FillRectangle(br, 0, top, W, H - top);
            using (var pen = new Pen(Color.FromArgb(28, ink), Math.Max(1f, L))) g.DrawLine(pen, 0, top, W, top);
            string when = Reminders.TimeRange(remindEvent);
            if (!string.IsNullOrEmpty(when))
                ScreenText(g, when, screenWhenFont, ink, SX(72), SY(454), SX(700), SY(64));
            if (!string.IsNullOrEmpty(remindEvent.place))
                ScreenText(g, remindEvent.place, screenPlaceFont, WithAlphaLocal(ink, 0.92), SX(74), SY(524), SX(700), SY(40));
            if (!string.IsNullOrEmpty(remindEvent.detail))
                ScreenText(g, remindEvent.detail, screenDetailFont, WithAlphaLocal(ink, 0.70), SX(74), SY(564), SX(820), SY(36));
            ScreenText(g, "今天", screenHintFont, WithAlphaLocal(ink, 0.55), W - SX(194), SY(464), SX(120), SY(30), StringAlignment.Far);
            ScreenText(g, "点任意处关闭", screenHintFont, WithAlphaLocal(ink, 0.45), W - SX(254), SY(652), SX(180), SY(30), StringAlignment.Far);
        }

        private static Color WithAlphaLocal(Color c, double a)
        {
            return Color.FromArgb((int)Math.Round(255 * Math.Max(0, Math.Min(1, a))), c.R, c.G, c.B);
        }

        private static void ScreenText(Graphics g, string s, Font f, Color c, float x, float y, float w, float h,
            StringAlignment align = StringAlignment.Near)
        {
            if (f == null || string.IsNullOrEmpty(s)) return;
            using (var sf = new StringFormat
            {
                Alignment = align,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            using (var br = new SolidBrush(c))
                g.DrawString(s, f, br, new RectangleF(x, y, w, h), sf);
        }

        private void PaintExpanded(Graphics g)
        {
            int pad=S(18); float w=Width-pad*2;
            Color muted=MixColor(theme.textMain,theme.bgBottom,MutedMix);
            Color dim=MixColor(theme.textMain,theme.bgBottom,DimMix);
            int gap=S(RowGapDip);
            int y=S(CardTopDip);

            // 展开时：先出时间和进度条（主视觉），其余行按顺序一行一行错峰浮上来，
            // 同一行里的几个元素共用一条时间线，所以是整行一起出现。
            double flow = expandAnim ? Math.Max(0, Environment.TickCount - expandStart) : 100000;
            double[] p = new double[6];
            for (int i = 0; i < p.Length; i++) p[i] = Clamp01((flow + 40 - i * StaggerGapMs) / 190.0);
            double pFocal = p[0];
            barAlpha = pFocal;

            // 1. 状态行：状态点 + 文案 + 右上角三个图标，同一行的中心对齐
            {
                double a = p[1];
                int dy = (int)Math.Round((1 - a) * S(6));
                int head=S(HeadHDip);
                using(var brush=new SolidBrush(Fade(status.accent, a))) g.FillEllipse(brush,pad,y+head/2-S(3)+dy,S(6),S(6));
                Line(g,status.headline,fontStatus,muted,new RectangleF(pad+S(13),y+dy,w-S(13),head),a,false);
                int iconSize=S(24); int iconY=y+(head-iconSize)/2+dy;
                rectSalary=new Rectangle(Width-S(118),iconY,iconSize,iconSize);
                rectNote=new Rectangle(Width-S(146),iconY,iconSize,iconSize);
                rectGear=new Rectangle(Width-S(90),iconY,iconSize,iconSize);
                rectRefresh=new Rectangle(Width-S(62),iconY,iconSize,iconSize);
                rectChevron=new Rectangle(Width-S(34),iconY,iconSize,iconSize);
                DrawGlyph(g,"plane",rectNote,Fade(NoteUnread?status.accent:dim,a));
                if(NoteUnread)
                    using(var brush=new SolidBrush(Fade(theme.accent,1)))
                        g.FillEllipse(brush,rectNote.Right-S(5),rectNote.Top+S(1),S(5),S(5));
                DrawGlyph(g,"salary",rectSalary,Fade(salaryOpen?status.accent:dim,a));
                DrawGlyph(g,"gear",rectGear,Fade(dim, a));
                DrawGlyph(g,"refresh",rectRefresh,Fade(sync.Snapshot().AttendanceFetching?status.accent:dim, a));
                DrawGlyph(g,"chevron",rectChevron,Fade(dim, a));
                y+=head+gap;
            }

            // 2. 说明行
            {
                double a = p[2]; int dy = (int)Math.Round((1 - a) * S(6));
                Line(g,status.bigLabel,fontSmall,muted,new RectangleF(pad,y+dy,w,S(LineDip)),a,false);
                y+=S(LineDip)+gap;
            }

            // 3. 大号倒计时（主视觉，和进度条一起最先出现）
            {
                int dy = (int)Math.Round((1 - pFocal) * S(8));
                Font basis=status.bigIsClock?fontBig:fontBigText;
                Font fit=FitFont(g,status.big,basis,w);
                // 云母 / 仪表 / 霓虹：数字带发光；默认皮肤还是原来的 Line
                LineNumber(g,status.big,fit,new RectangleF(pad-S(2),y+dy,w+S(2),S(BigDip)),pFocal,false);
                if(fit!=basis) fit.Dispose();
                y+=S(BigDip)+gap;
            }

            // 4. 进度条
            {
                int dy = (int)Math.Round((1 - pFocal) * S(8));
                var saved = g.Save();
                g.TranslateTransform(0, dy);
                DrawProgress(g,pad,y,Width-pad*2,S(BarDip),true);
                g.Restore(saved);
                y+=S(BarDip)+gap;
            }

            // 5. 下一段
            {
                double a = p[3]; int dy = (int)Math.Round((1 - a) * S(6));
                Line(g,status.sub,fontSmall,muted,new RectangleF(pad,y+dy,w,S(LineDip)),a,false);
                y+=S(LineDip)+gap;
            }

            // 6. 午休行（有才占一行）
            if(ShowLunchRow)
            {
                double a = p[4]; int dy = (int)Math.Round((1 - a) * S(6));
                string row=(status.lunchLabel??"");
                if(!string.IsNullOrEmpty(status.lunchValue)) row+="  "+status.lunchValue;
                Line(g,row,fontSmallBold,
                    status.lunchCounting?theme.textMain:muted,new RectangleF(pad,y+dy,w,S(LineDip)),a,false);
                y+=S(LineDip)+gap;
            }

            // 7. 最后一行小字
            {
                double a = p[5]; int dy = (int)Math.Round((1 - a) * S(6));
                Line(g,status.extra,fontTiny,dim,new RectangleF(pad,y+dy,w,S(FootDip)),a,false);
            }

            // 8. 工资面板：接在原有内容下面，卡片整体拉长
            if (salaryOpen) DrawSalaryPanel(g);
        }

        /// <summary>
        /// 工资面板（方案 A：主数字）。今日收入实时累计、保留 3 位小数；
        /// 下面是本月收入 + 发薪日；再点一次右上角 ¥ 收起。
        /// </summary>
        private void DrawSalaryPanel(Graphics g)
        {
            var ss = SalaryCalc.Compute(settings, snapshot, DateTime.Now);
            int top = S(CardHeightDip(ShowLunchRow));
            int pad = S(18), w = Width - pad * 2;
            Color muted = MixColor(theme.textMain, theme.bgBottom, MutedMix);
            Color dim = MixColor(theme.textMain, theme.bgBottom, DimMix);
            Color accent = theme.accent;
            double ph, pm, ps;
            SalaryRowProgress(out ph, out pm, out ps);
            int dyH = (int)Math.Round((1 - ph) * S(6));
            int dyM = (int)Math.Round((1 - pm) * S(6));
            int dyS = (int)Math.Round((1 - ps) * S(6));

            using (var pen = new Pen(Color.FromArgb((int)(30 * ph), theme.textMain), Math.Max(1f, scale)))
                g.DrawLine(pen, pad, top, Width - pad, top);

            if (!ss.configured)
            {
                Line(g, "还没设置月工资，点右上角 ¥ 去设置", fontSmall, muted,
                    new RectangleF(pad, top + S(30), w, S(24)), 1, false);
                return;
            }

            // 标题 + 实时（右边是「单击收起」弱提示）
            int headY = top + S(8) + dyH;
            Line(g, "今日收入", fontSmall, muted, new RectangleF(pad, headY, S(60), S(16)), ph, false);
            float titleW = g.MeasureString("今日收入", fontSmall).Width;
            int pillX = pad + (int)titleW + S(8);
            var live = new Rectangle(pillX, headY + S(1), S(48), S(13));
            using (var path = RoundPath(live, S(6)))
            using (var br = new SolidBrush(Color.FromArgb((int)(24 * ph), 79, 209, 161)))
                g.FillPath(br, path);
            using (var br = new SolidBrush(Color.FromArgb((int)(220 * ph), 79, 209, 161)))
                g.FillEllipse(br, live.X + S(6), live.Y + S(4), S(5), S(5));
            Line(g, "实时", fontTiny, Color.FromArgb(210, 79, 209, 161),
                new RectangleF(live.X + S(14), live.Y, S(32), live.Height), ph, false);

            // 右边只留一个「单击收起」的弱提示（v3.2 起没有双击固定）
            Line(g, "单击收起", fontTiny, dim,
                new RectangleF(Width - pad - S(60), headY, S(60), S(16)), ph, true);

            // 今日收入：只把这个大数字往下让约半个字高，本月收入/发薪日不动
            int moneyY = top + S(32) + dyM;
            Line(g, "¥", fontSmallBold, muted, new RectangleF(pad, moneyY + S(8), S(18), S(24)), pm, false);
            DrawSalaryAmount(g, pad + S(18), moneyY, w - S(18), S(36), ss.today, pm);

            // 本月收入 / 发薪日（和 HTML 一样，上面一条细分隔线）
            int lineY = top + S(68) + dyS;
            using (var pen = new Pen(Color.FromArgb((int)(28 * ps), theme.textMain), Math.Max(1f, scale)))
                g.DrawLine(pen, pad, lineY, Width - pad, lineY);

            int statY = top + S(74) + dyS;
            Line(g, "本月收入", fontTiny, dim, new RectangleF(pad, statY, S(60), S(14)), ps, false);
            Line(g, "¥" + SalaryCalc.Money3(ss.month), fontSmallBold, theme.textMain,
                new RectangleF(pad, statY + S(16), S(190), S(18)), ps, false);

            Line(g, "发薪日 " + ss.paydayDate.ToString("MM/dd", CultureInfo.InvariantCulture), fontTiny, dim,
                new RectangleF(Width - pad - S(150), statY, S(150), S(14)), ps, true);
            string pay = ss.daysToPayday <= 0 ? "今天发薪"
                : ss.daysToPayday == 1 ? "明天发薪" : ss.daysToPayday + " 天后";
            Line(g, pay, fontSmallBold, ss.daysToPayday <= 3 ? theme.success : theme.textMain,
                new RectangleF(Width - pad - S(150), statY + S(16), S(150), S(18)), ps, true);
        }

        /// <summary>
        /// 今日收入数字：整数位用主题色渐变 + 常驻柔光，再叠一道 6.5 秒的流动高光；
        /// 小数位保持主题文字色（深色主题下就是白色）。对应 design\salary-number-final.html。
        /// </summary>
        private void DrawSalaryAmount(Graphics g, int x, int y, int w, int h, decimal value, double alpha)
        {
            alpha = Clamp01(alpha);
            if (alpha < 0.01) return;
            string text = SalaryCalc.Money3(value);
            int dot = text.LastIndexOf('.');
            string intPart = dot >= 0 ? text.Substring(0, dot) : text;
            string decPart = dot >= 0 ? text.Substring(dot) : "";
            if (intPart.Length == 0) intPart = "0";

            using (var fmt = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
            })
            {
                float iw = Math.Min(w, g.MeasureString(intPart, fontSalary, PointF.Empty,
                    StringFormat.GenericTypographic).Width + S(1));
                if (iw < S(8)) iw = S(8);
                var intRect = new RectangleF(x, y, iw, h);
                Color accent = theme.accent;
                // 浅色卡片上不能再用「主题色 + 白」，否则整数位会被背景吃掉：
                // 底色往黑里压、亮部往文字色靠；柔光改成很淡的深色阴影，流光也压低亮度。
                bool onLight = Luminance(theme.bgBottom) > 0.55;
                Color glowBase = onLight ? MixColor(accent, Color.Black, 0.45) : accent;

                // 轻一档的常驻柔光：两圈低透明度文字，不呼吸
                using (var inner = new SolidBrush(Color.FromArgb((int)((onLight ? 10 : 18) * alpha), glowBase)))
                {
                    float r = S(2);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = Math.PI * 2 * i / 8.0;
                        g.DrawString(intPart, fontSalary, inner,
                            new RectangleF(x + (float)Math.Cos(a) * r, y + (float)Math.Sin(a) * r, iw, h), fmt);
                    }
                }
                using (var outer = new SolidBrush(Color.FromArgb((int)((onLight ? 4 : 8) * alpha), glowBase)))
                {
                    float r = S(4);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = Math.PI * 2 * i / 8.0;
                        g.DrawString(intPart, fontSalary, outer,
                            new RectangleF(x + (float)Math.Cos(a) * r, y + (float)Math.Sin(a) * r, iw, h), fmt);
                    }
                }

                // 主题色渐变底
                Color dark = Fade(onLight
                    ? MixColor(accent, Color.Black, 0.42)
                    : MixColor(accent, theme.bgBottom, 0.28), alpha);
                Color light = Fade(onLight
                    ? MixColor(accent, theme.textMain, 0.30)
                    : MixColor(accent, Color.White, 0.52), alpha);
                using (var brush = new LinearGradientBrush(new RectangleF(x, y, Math.Max(2f, iw), h), dark, light, 10f))
                    g.DrawString(intPart, fontSalary, brush, intRect, fmt);

                // 6.5 秒一轮的流动高光，只在整数位字形上显影
                double phase = (Environment.TickCount % 6500) / 6500.0;
                float band = Math.Max(iw * 0.95f, S(64));
                float bx = x - band + (float)(phase * (iw + band * 2));
                using (var brush = new LinearGradientBrush(new RectangleF(bx, y, band, h),
                    Color.Transparent, Color.Transparent, 0f))
                {
                    var blend = new ColorBlend();
                    Color shine = onLight ? MixColor(accent, Color.White, 0.28) : Color.White;
                    int shineAlpha = onLight ? 80 : 150;
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(0, shine),
                        Color.FromArgb((int)(shineAlpha * alpha), shine),
                        Color.FromArgb(0, shine)
                    };
                    blend.Positions = new float[] { 0f, 0.5f, 1f };
                    brush.InterpolationColors = blend;
                    g.DrawString(intPart, fontSalary, brush, intRect, fmt);
                }

                if (decPart.Length > 0)
                {
                    using (var brush = new SolidBrush(Fade(theme.textMain, alpha)))
                        g.DrawString(decPart, fontSalary, brush,
                            new RectangleF(x + iw, y, Math.Max(0, w - iw), h), fmt);
                }
            }
        }

        private static Color Fade(Color c, double alpha)
        {
            return Color.FromArgb((int)Math.Round(255 * Clamp01(alpha)), c);
        }

        /// <summary>距离下班还剩几分钟；拿不到时间点就返回 -1。</summary>
        private double MinutesToOffWork()
        {
            DateTime t;
            if (status == null || string.IsNullOrEmpty(status.targetIso)) return -1;
            if (!DateTime.TryParse(status.targetIso, out t)) return -1;
            return (t - DateTime.Now).TotalMinutes;
        }

        /// <summary>快到点了吗：剩不到 30 分钟、而且还没到。</summary>
        internal static bool HotBar(double minutesLeft)
        {
            return minutesLeft > 0 && minutesLeft <= 30;
        }

        /// <summary>
        /// 流光在「一圈」里的不透明度：从左端出来时淡入，快到尽头时淡出。
        /// 老做法是一整圈都按 150 的不透明度画，光带中心走到右端时被进度条的圆角裁掉，
        /// 下一帧又跳回左边——看起来就是「走到尽头突然没了」。现在两头都过一遍
        /// 平滑曲线，最后一段是渐渐变淡，回到左边时也是从无到有。
        /// </summary>
        internal static double FlowOpacity(double phase)
        {
            if (phase <= 0 || phase >= 1) return 0;
            double fadeIn = SmoothStep01(phase / FlowFadeIn);
            double fadeOut = SmoothStep01((1 - phase) / FlowFadeOut);
            return Math.Min(fadeIn, fadeOut);
        }

        /// <summary>0→1 的平滑曲线（smoothstep），避免透明度线性变化看着发「硬」。</summary>
        private static double SmoothStep01(double t)
        {
            t = Clamp01(t);
            return t * t * (3 - 2 * t);
        }

        /// <summary>进度条：走过的一段带发光，最后半小时整条变彩色。</summary>
        /// <summary>进度条：走过的一段带发光，最后半小时整条变彩色。</summary>
        private void DrawProgress(Graphics g, int x, int y, int width, int height, bool glowOn)
        {
            if (barAlpha <= 0.01) return;
            // 五套皮肤各自一种画法：高度按皮肤，在原来的行盒（height，还是 S(BarDip)）里垂直居中，
            // 行高推进不变，所以卡片高度一点没动。
            SkinProgress skinBar = Skin.progress;
            if (skinBar != SkinProgress.Default)
            {
                int barH = Math.Max(1, S(ProgressBarDip(skinBar)));
                DrawSkinProgress(g, skinBar, x, y + (height - barH) / 2, Math.Max(2, width), barH);
                return;
            }
            using (var path = RoundPath(new Rectangle(x, y, width, height), height / 2))
            using (var brush = new SolidBrush(Fade(theme.track, barAlpha)))
                g.FillPath(brush, path);

            int filled = (int)(width * Math.Max(0, Math.Min(1, status.progress)));
            if (filled < height) return;
            var bar = new Rectangle(x, y, filled, height);
            bool hot = HotBar(MinutesToOffWork());

            // 外发光：两圈比条子大一点的同色圆角矩形，越靠外越淡，前缘像在发亮。
            // 颜色必须跟着条子走：普通模式是 accent 的深浅渐变，最后半小时是同一套彩虹，
            // 不能一律用 accent —— 那样条子变黄了外圈还是绿的。
            if (glowOn && filled > height * 2)
            {
                Color[] glowStops;
                float[] glowPos;
                if (hot)
                {
                    glowStops = new Color[]
                    {
                        Color.FromArgb(255, 212, 122), Color.FromArgb(255, 214, 61),
                        Color.FromArgb(255, 138, 61), Color.FromArgb(255, 92, 122)
                    };
                    glowPos = new float[] { 0f, 0.42f, 0.74f, 1f };
                }
                else
                {
                    glowStops = new Color[] { MixColor(MixColor(status.accent, theme.bgBottom, 0.22), Color.White, 0.15), MixColor(status.accent, Color.White, 0.15) };
                    glowPos = new float[] { 0f, 1f };
                }
                int glowSpan = hot ? Math.Max(2, width) : Math.Max(2, filled);

                for (int pass = 0; pass < 2; pass++)
                {
                    int grow = (int)Math.Round(height * (pass == 0 ? 1.8f : 0.9f));
                    int feather = grow * 3;
                    int alpha = pass == 0 ? (hot ? 32 : 16) : (hot ? 70 : 32);
                    // 右边多留一截做羽毛状的尾巴，光才不会在前缘突然断掉
                    var halo = new Rectangle(x - grow, y - grow, filled + grow * 2 + feather, height + grow * 2);
                    using (var gp = RoundPath(halo, halo.Height / 2))
                    using (var br = new LinearGradientBrush(halo, Color.Black, Color.Black, 0f))
                    {
                        float keep = Math.Min(1f, (float)(filled + grow) / Math.Max(1, halo.Width));
                        var cols = new List<Color>();
                        var pos = new List<float>();
                        int a = (int)Math.Round(alpha * barAlpha);
                        cols.Add(Color.FromArgb(a, glowStops[0]));
                        pos.Add(0f);
                        int lastIdx = 0;                       // 已经画到的最靠右的颜色
                        for (int i = 1; i < glowStops.Length; i++)
                        {
                            float u = (float)((x + glowPos[i] * glowSpan - halo.X) / (double)halo.Width);
                            if (u <= 0f) { cols[0] = Color.FromArgb(a, glowStops[i]); lastIdx = i; }
                            else if (u >= keep) break;
                            else { cols.Add(Color.FromArgb(a, glowStops[i])); pos.Add(u); lastIdx = i; }
                        }
                        // GDI+ 要求最后一个位置正好是 1.0：先到 keep 保持颜色，再从 keep 淡到透明。
                        // 淡出用的是「条子前缘」的颜色，不能拿渐变的最后一个色（那样尾巴会忽然变粉）。
                        float fadeAt = Math.Max(keep, pos[pos.Count - 1] + 0.0005f);
                        if (fadeAt < 0.999f)
                        {
                            cols.Add(Color.FromArgb(a, glowStops[lastIdx]));
                            pos.Add(fadeAt);
                        }
                        cols.Add(Color.FromArgb(0, glowStops[lastIdx]));
                        pos.Add(1f);
                        var fblend = new ColorBlend();
                        fblend.Colors = cols.ToArray();
                        fblend.Positions = pos.ToArray();
                        br.InterpolationColors = fblend;
                        g.FillPath(br, gp);
                    }
                }
            }

            using (var path = RoundPath(bar, height / 2))
            {
                if (hot)
                {
                    // 彩色：整条轨道从左到右 绿→黄→橙→粉，走到哪段都是该有的颜色
                    using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(2, width), height),
                        Color.FromArgb((int)Math.Round(255 * barAlpha), 212, 122), Color.FromArgb((int)Math.Round(255 * barAlpha), 92, 122), 0f))
                    {
                        var blend = new ColorBlend();
                        blend.Colors = new Color[]
                        {
                            Color.FromArgb((int)Math.Round(255 * barAlpha), 212, 122), Color.FromArgb((int)Math.Round(255 * barAlpha), 214, 61),
                            Color.FromArgb((int)Math.Round(255 * barAlpha), 138, 61), Color.FromArgb((int)Math.Round(255 * barAlpha), 92, 122)
                        };
                        blend.Positions = new float[] { 0f, 0.42f, 0.74f, 1f };
                        brush.InterpolationColors = blend;
                        g.FillPath(brush, path);
                    }
                }
                else
                {
                    using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(2, filled), height),
                        Fade(MixColor(status.accent, theme.bgBottom, 0.22), barAlpha), Fade(status.accent, barAlpha), 0f))
                        g.FillPath(brush, path);
                }
                // 上面压一道高光，条子才有厚度
                using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(2, filled), Math.Max(2, height)),
                    Color.FromArgb((int)Math.Round((90) * barAlpha), Color.White), Color.FromArgb((int)Math.Round((0) * barAlpha), Color.White), 90f))
                    g.FillPath(brush, path);
                // 流动光带：一道柔光沿着已经走过的部分往前走。两头都淡入淡出——
                // 走到尽头先慢慢变淡再回到左边，不会在终点「啪」一下整条消失。
                if (!settings.ui.reducedMotion && !SystemInformation.HighContrast && filled > height * 4)
                {
                    double phase = flowPhase >= 0
                        ? flowPhase
                        : (Environment.TickCount % FlowPeriodMs) / (double)FlowPeriodMs;
                    double opacity = FlowOpacity(phase);
                    if (opacity > 0.01)
                    {
                        int bandW = Math.Max(S(24), filled / 3);
                        // 光带是在整窗坐标里画的：起点要从条的左端（x）算，不能从 0 算，
                        // 否则光还没走到条的真正尽头就先没了。
                        int cx = (int)Math.Round(x - bandW + (filled + bandW) * phase);
                        var savedFlow = g.Save();
                        g.SetClip(path, CombineMode.Intersect);
                        var flowRect = new Rectangle(cx - bandW, y, bandW * 2, Math.Max(2, height));
                        using (var fb = new LinearGradientBrush(flowRect,
                            Color.FromArgb((int)Math.Round((0) * barAlpha), Color.White), Color.FromArgb((int)Math.Round((0) * barAlpha), Color.White), 0f))
                        {
                            var fblend = new ColorBlend();
                            fblend.Colors = new Color[] {
                                Color.FromArgb((int)Math.Round((0) * barAlpha), Color.White),
                                Color.FromArgb((int)Math.Round((150) * barAlpha * opacity), Color.White),
                                Color.FromArgb((int)Math.Round((0) * barAlpha), Color.White) };
                            fblend.Positions = new float[] { 0f, 0.5f, 1f };
                            fb.InterpolationColors = fblend;
                            g.FillRectangle(fb, flowRect);
                        }
                        g.Restore(savedFlow);
                    }
                }
            }
        }

        // ---------------- 五套皮肤的进度条（默认皮肤的 DrawProgress 一行没改） ----------------

        /// <summary>皮肤进度条的高度（DIP）：每种画法一条，画的时候在 S(BarDip) 的行盒里垂直居中。</summary>
        private static int ProgressBarDip(SkinProgress style)
        {
            switch (style)
            {
                case SkinProgress.Hairline: return 2;
                case SkinProgress.Pill: return 5;
                case SkinProgress.Brush: return 3;
                case SkinProgress.Segmented: return 8;
                case SkinProgress.Neon: return 4;
            }
            return BarDip;
        }

        /// <summary>当前走过比例（0..1）。</summary>
        private double BarProgress { get { return status == null ? 0 : Math.Max(0, Math.Min(1, status.progress)); } }

        /// <summary>这套皮肤的进度条有没有逐帧动的东西（流动亮带）：没有就不必 18fps 重画。</summary>
        private bool BarAnimated
        {
            get
            {
                SkinProgress style = Skin.progress;
                return style == SkinProgress.Default || style == SkinProgress.Pill || style == SkinProgress.Neon;
            }
        }

        /// <summary>
        /// 非默认皮肤的轨道色（已经带好透明度）：
        /// 深色皮肤用半透明白（设计稿全是 rgba(255,255,255,.09~.14)，云母压在玻璃上才不会被盖成一块深灰），
        /// 宣纸用深一档的纸色（设计稿 --trk:#E4D9C3）。
        /// </summary>
        private Color BarTrackInk(double alpha)
        {
            alpha = Clamp01(alpha);
            int a = (int)Math.Round(255 * alpha);
            if (a <= 0) return Color.Transparent;
            if (Skin.light) return Color.FromArgb(a, Skins.Mix(theme.edge, theme.track, 0.12));
            double k = Skin.glass ? 0.14 : 0.11;
            return Color.FromArgb((int)Math.Round(a * k), Color.White);
        }

        private void DrawSkinProgress(Graphics g, SkinProgress style, int x, int y, int width, int height)
        {
            switch (style)
            {
                case SkinProgress.Hairline: DrawBarHairline(g, x, y, width, height); break;
                case SkinProgress.Pill: DrawBarPill(g, x, y, width, height); break;
                case SkinProgress.Brush: DrawBarBrush(g, x, y, width, height); break;
                case SkinProgress.Segmented: DrawBarSegmented(g, x, y, width, height); break;
                case SkinProgress.Neon: DrawBarNeon(g, x, y, width, height); break;
            }
        }

        /// <summary>无垠：2 DIP 细线，只留一条实线，不发光、不流动。</summary>
        private void DrawBarHairline(Graphics g, int x, int y, int width, int height)
        {
            int radius = height / 2;
            using (var path = RoundPath(new Rectangle(x, y, width, height), radius))
            using (var brush = new SolidBrush(BarTrackInk(barAlpha * 0.9)))
                g.FillPath(brush, path);
            int filled = (int)Math.Round(width * BarProgress);
            if (filled < 1) return;
            // 无垠的进度是一条近白的细线（accent2 往主文字色提亮），不是强调色
            Color ink = Skin.accent2.IsEmpty ? theme.accent : Skins.Mix(Skin.accent2, theme.textMain, 0.3);
            using (var path = RoundPath(new Rectangle(x, y, filled, height), radius))
            using (var brush = new SolidBrush(Fade(ink, barAlpha)))
                g.FillPath(brush, path);
        }

        /// <summary>云母：5 DIP 渐变胶囊 + 柔光 + 流动亮带。</summary>
        private void DrawBarPill(Graphics g, int x, int y, int width, int height)
        {
            int radius = height / 2;
            int filled = (int)Math.Round(width * BarProgress);
            Color start = theme.accent;
            Color end = Skin.accent2.IsEmpty ? theme.accent : Skin.accent2;
            // 柔光：三圈同色、越大越淡（对应 CSS 的 0 0 14px）
            if (filled > height)
            {
                for (int i = 3; i >= 1; i--)
                {
                    int grow = S(i * 1.2f);
                    if (grow <= 0) continue;
                    var halo = new Rectangle(x - grow, y - grow, filled + grow * 2, height + grow * 2);
                    using (var path = RoundPath(halo, (height + grow * 2) / 2))
                    using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(22 * barAlpha / i), start)))
                        g.FillPath(brush, path);
                }
            }
            using (var path = RoundPath(new Rectangle(x, y, width, height), radius))
            using (var brush = new SolidBrush(BarTrackInk(barAlpha)))
                g.FillPath(brush, path);
            if (filled < Math.Max(2, height / 2)) return;
            using (var path = RoundPath(new Rectangle(x, y, filled, height), radius))
            {
                using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(2, filled), height),
                    Fade(start, barAlpha), Fade(end, barAlpha), 0f))
                    g.FillPath(brush, path);
                // 上面压一道高光，胶囊才有玻璃厚度
                using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(2, filled), Math.Max(2, height)),
                    Color.FromArgb((int)Math.Round(70 * barAlpha), Color.White), Color.FromArgb(0, Color.White), 90f))
                    g.FillPath(brush, path);
                PaintBarFlow(g, path, x, y, height, filled);
            }
        }

        /// <summary>宣纸：3 DIP 朱砂笔触（accent2 → accent 横向渐变），两端收圆，不流动、不发光。</summary>
        private void DrawBarBrush(Graphics g, int x, int y, int width, int height)
        {
            int radius = height / 2;
            using (var path = RoundPath(new Rectangle(x, y, width, height), radius))
            using (var brush = new SolidBrush(BarTrackInk(barAlpha)))
                g.FillPath(brush, path);
            int filled = (int)Math.Round(width * BarProgress);
            if (filled < height) return;
            Color start = Skin.accent2.IsEmpty ? theme.accent : Skin.accent2;   // 朱砂亮面
            Color end = theme.accent;                                           // 朱砂暗面
            using (var path = RoundPath(new Rectangle(x, y, filled, height), radius))
            {
                using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(2, filled), height),
                    Fade(start, barAlpha), Fade(end, barAlpha), 0f))
                    g.FillPath(brush, path);
                using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(2, filled), Math.Max(2, height)),
                    Color.FromArgb((int)Math.Round(46 * barAlpha), Color.White), Color.FromArgb(0, Color.White), 90f))
                    g.FillPath(brush, path);
            }
        }

        /// <summary>仪表：8 DIP 分刻刻度（24 格），琥珀色，像仪表刻度；格子在整条轨道上等分。</summary>
        private void DrawBarSegmented(Graphics g, int x, int y, int width, int height)
        {
            const int cells = 24;
            int gap = Math.Max(1, S(1.6f));
            int radius = Math.Max(1, S(1));
            double progress = BarProgress;
            Color on = theme.accent;                                    // 琥珀
            // 没走到的暗刻：半透明白（设计稿 --trk 是 rgba(255,255,255,.11) 的重复条纹）
            Color off = Color.FromArgb((int)Math.Round(255 * Clamp01(barAlpha) * 0.11), Color.White);
            int filledW = (int)Math.Round(width * progress);
            if (filledW > height)
            {
                // 已走段底下一圈琥珀柔光（对应 CSS 的 0 0 10px）
                for (int i = 2; i >= 1; i--)
                {
                    int grow = S(i * 1.6f);
                    if (grow <= 0) continue;
                    var halo = new Rectangle(x - grow, y - grow, filledW + grow * 2, height + grow * 2);
                    using (var path = RoundPath(halo, radius + grow))
                    using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(16 * barAlpha / i), on)))
                        g.FillPath(brush, path);
                }
            }
            for (int i = 0; i < cells; i++)
            {
                int left = x + (int)Math.Round(width * (double)i / cells);
                int right = x + (int)Math.Round(width * (double)(i + 1) / cells);
                int cw = Math.Max(1, right - left - gap);
                var cell = new Rectangle(left, y, cw, height);
                using (var path = RoundPath(cell, radius))
                using (var brush = new SolidBrush(off))
                    g.FillPath(brush, path);
                double fill = Clamp01(progress * cells - i);
                if (fill <= 0.001) continue;
                int fw = (int)Math.Round(cw * fill);
                if (fw < 1) continue;
                using (var path = RoundPath(new Rectangle(cell.X, y, fw, height), radius))
                using (var brush = new SolidBrush(Fade(on, barAlpha)))
                    g.FillPath(brush, path);
            }
        }

        /// <summary>霓虹：4 DIP 品红→青渐变 + 双层辉光（外层青、内层品红）+ 亮带。</summary>
        private void DrawBarNeon(Graphics g, int x, int y, int width, int height)
        {
            int radius = height / 2;
            int filled = (int)Math.Round(width * BarProgress);
            Color hot = theme.accent;
            Color cool = Skin.accent2.IsEmpty ? theme.accent : Skin.accent2;
            if (filled > height)
            {
                for (int i = 4; i >= 2; i--)
                {
                    int grow = S(i * 1.1f);
                    var halo = new Rectangle(x - grow, y - grow, filled + grow * 2, height + grow * 2);
                    using (var path = RoundPath(halo, (height + grow * 2) / 2))
                    using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(9 * barAlpha), cool)))
                        g.FillPath(brush, path);
                }
                for (int i = 2; i >= 1; i--)
                {
                    int grow = S(i * 1.0f);
                    var halo = new Rectangle(x - grow, y - grow, filled + grow * 2, height + grow * 2);
                    using (var path = RoundPath(halo, (height + grow * 2) / 2))
                    using (var brush = new SolidBrush(Color.FromArgb((int)Math.Round(26 * barAlpha / i), hot)))
                        g.FillPath(brush, path);
                }
            }
            using (var path = RoundPath(new Rectangle(x, y, width, height), radius))
            using (var brush = new SolidBrush(BarTrackInk(barAlpha)))
                g.FillPath(brush, path);
            if (filled < Math.Max(2, height / 2)) return;
            using (var path = RoundPath(new Rectangle(x, y, filled, height), radius))
            {
                using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(2, filled), height), Color.Black, Color.Black, 0f))
                {
                    var blend = new ColorBlend();
                    blend.Colors = new[] { Fade(hot, barAlpha), Fade(Skins.Mix(hot, cool, 0.5), barAlpha), Fade(cool, barAlpha) };
                    blend.Positions = new[] { 0f, 0.55f, 1f };
                    brush.InterpolationColors = blend;
                    g.FillPath(brush, path);
                }
                using (var brush = new LinearGradientBrush(new Rectangle(x, y, Math.Max(2, filled), Math.Max(2, height)),
                    Color.FromArgb((int)Math.Round(70 * barAlpha), Color.White), Color.FromArgb(0, Color.White), 90f))
                    g.FillPath(brush, path);
                PaintBarFlow(g, path, x, y, height, filled);
            }
        }

        /// <summary>云母 / 霓虹那条流动光带：节奏和默认皮肤一致（两头淡入淡出，2.6s 一圈）。</summary>
        private void PaintBarFlow(Graphics g, GraphicsPath clip, int x, int y, int height, int filled)
        {
            if (settings.ui.reducedMotion || SystemInformation.HighContrast) return;
            if (filled <= height * 4) return;
            double phase = flowPhase >= 0
                ? flowPhase
                : (Environment.TickCount % FlowPeriodMs) / (double)FlowPeriodMs;
            double opacity = FlowOpacity(phase);
            if (opacity <= 0.01) return;
            int bandW = Math.Max(S(24), filled / 3);
            int cx = (int)Math.Round(x - bandW + (filled + bandW) * phase);
            int peak = (int)Math.Round(150 * barAlpha * opacity);
            if (peak <= 0) return;
            var saved = g.Save();
            g.SetClip(clip, CombineMode.Intersect);
            var flowRect = new Rectangle(cx - bandW, y, bandW * 2, Math.Max(2, height));
            using (var brush = new LinearGradientBrush(flowRect, Color.FromArgb(0, Color.White), Color.FromArgb(0, Color.White), 0f))
            {
                var blend = new ColorBlend();
                blend.Colors = new[] { Color.FromArgb(0, Color.White), Color.FromArgb(peak, Color.White), Color.FromArgb(0, Color.White) };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                brush.InterpolationColors = blend;
                g.FillRectangle(brush, flowRect);
            }
            g.Restore(saved);
        }

        // ---------------- 大数字发光（云母 / 仪表 / 霓虹） ----------------

        /// <summary>数字发光的两圈偏移半径（DIP），借 DrawSalaryAmount / PaintCardGlow 的多圈低透明度画法。</summary>
        private static readonly float[] NumberHaloDip = new float[] { 1.5f, 3.4f };

        /// <summary>
        /// 大号数字：颜色取 skin.numberColor（null = theme.textMain，即默认皮肤的原样）；
        /// skin.numberGlow 时先按 8 个方向铺两圈低透明度偏移（内圈 accent2、外圈 textMain），
        /// 再把正文压上去。默认皮肤仍然是一次 Line，一个像素都不变。
        /// </summary>
        private void LineNumber(Graphics g, string text, Font font, RectangleF rect, double alpha, bool far)
        {
            SkinDef skin = Skin;
            // 皮肤可以用 numberColor 指定数字颜色（仪表是琥珀数码）；null 就是原来的 theme.textMain
            Color numInk = skin.numberColor.HasValue ? skin.numberColor.Value : theme.textMain;
            if (!skin.numberGlow)
            {
                Line(g, text, font, numInk, rect, alpha, far);
                return;
            }
            double a = Clamp01(alpha);
            if (a < 0.01) return;
            string clean = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            if (clean.Length == 0) return;
            double strength = skin.numberGlowAlpha <= 0 ? 0.35 : Math.Min(1.2, skin.numberGlowAlpha);
            Color warm = skin.accent2.IsEmpty ? theme.accent : skin.accent2;
            using (var format = new StringFormat
            {
                Alignment = far ? StringAlignment.Far : StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                for (int ring = 0; ring < NumberHaloDip.Length; ring++)
                {
                    float r = Math.Max(1f, S(NumberHaloDip[ring]));
                    double k = ring == 0 ? 0.55 : 0.22;
                    int al = (int)Math.Round(255 * a * strength * k);
                    if (al <= 1) continue;
                    Color ink = ring == 0 ? Skins.Mix(warm, theme.textMain, 0.25) : theme.textMain;
                    using (var brush = new SolidBrush(Color.FromArgb(al, ink)))
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            double ang = Math.PI * 2 * i / 8.0;
                            g.DrawString(clean, font, brush, new RectangleF(
                                rect.X + (float)Math.Cos(ang) * r,
                                rect.Y + (float)Math.Sin(ang) * r,
                                rect.Width, rect.Height), format);
                        }
                    }
                }
            }
            Line(g, text, font, numInk, rect, alpha, far);
        }


        /// <summary>右上角图标的命中区。正常由绘制时填好；万一还没画过，就按当前宽度算一个。</summary>
        private Rectangle GlyphRect(Rectangle stored, int fromRightDip)
        {
            if (stored.Width > 0 && stored.Bottom <= Height) return stored;
            int size = S(24);
            return new Rectangle(Width - S(fromRightDip), S(15), size, size);
        }
        private void PaintCollapsed(Graphics g)
        {
            int pad=S(14); float w=Width-pad*2;
            Color muted=MixColor(theme.textMain,theme.bgBottom,MutedMix);
            using(var brush=new SolidBrush(status.accent)) g.FillEllipse(brush,pad,S(15),S(5),S(5));
            Line(g,status.bigLabel,fontCollapsedLabel,muted,new RectangleF(pad+S(10),S(7),S(90),S(20)),1,false);
            string right=string.IsNullOrEmpty(status.targetText)?"":"下班 "+status.targetText;
            if(ShowLunchRow && status.lunchCounting && !string.IsNullOrEmpty(status.lunchValue))
            {
                string tag=status.lunch==LunchState.EveningBefore||status.lunch==LunchState.EveningDuring?"晚":"休";
                right=tag+" "+(status.lunchValue??"").Replace("还剩 ","");
            }
            Line(g,right,fontCollapsedLabel,muted,new RectangleF(Width-pad-S(86),S(7),S(86),S(20)),1,true);
            LineNumber(g,status.big,status.bigIsClock?fontCollapsedNum:fontCollapsedText,
                new RectangleF(pad-S(1),S(27),w,S(27)),1,false);
            DrawProgress(g,pad,S(59),Width-pad*2,S(3),true);
            if(lastFetchFailed) Line(g,"!",fontStatus,Color.FromArgb(242,179,76),new RectangleF(Width-pad-S(14),S(32),S(14),S(20)),1,true);
        }

        private void DrawGlyph(Graphics g, string kind, Rectangle r, Color color)
        {
            float f = scale;
            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            // 五个图标统一：几何外框 12 DIP，加上 1.5 DIP 线宽后墨迹外框约 13.5 DIP 见方，
            // 全部绕命中区中心画。拐角用 Round：尖角 miter 会把外框拉长（飞机左尖角尤其明显）。
            float half = 6f * f;
            using (var pen = new Pen(color, 1.5f * f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (kind == "gear")
                {
                    // 8 齿：0° / 90° 都有齿尖，横竖外框都正好 12 DIP
                    g.DrawEllipse(pen, cx - half * 0.66f, cy - half * 0.66f, half * 1.32f, half * 1.32f);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        float x1 = cx + (float)Math.Cos(a) * half * 0.66f;
                        float y1 = cy + (float)Math.Sin(a) * half * 0.66f;
                        float x2 = cx + (float)Math.Cos(a) * half;
                        float y2 = cy + (float)Math.Sin(a) * half;
                        g.DrawLine(pen, x1, y1, x2, y2);
                    }
                    using (var b = new SolidBrush(color))
                        g.FillEllipse(b, cx - 1.2f * f, cy - 1.2f * f, 2.4f * f, 2.4f * f);
                }
                else if (kind == "refresh")
                {
                    // 原来的圆弧 + 箭头等比缩到 12 DIP 外框（箭头留在缺口右上角，别缩丢了）
                    g.DrawArc(pen, cx - 6.0f * f, cy - 4.051f * f, 10.426f * f, 10.05f * f, 35f, 275f);
                    using (var b = new SolidBrush(color))
                        g.FillPolygon(b, new PointF[]
                        {
                            new PointF(cx + 3.738f * f, cy - 5.997f * f),
                            new PointF(cx + 6.0f * f, cy - 0.769f * f),
                            new PointF(cx + 1.279f * f, cy - 1.384f * f)
                        });
                }
                else if (kind == "plane")
                {
                    // 小飞机：一条折线 + 尾翼，和面板里那颗图标同一形状，整体缩到 12 DIP 外框
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(cx + 6.0f * f, cy - 6.0f * f),
                        new PointF(cx - 6.0f * f, cy - 0.74f * f),
                        new PointF(cx - 0.99f * f, cy + 1.39f * f),
                        new PointF(cx + 6.0f * f, cy - 6.0f * f)
                    });
                    g.DrawLine(pen, cx - 0.99f * f, cy + 1.39f * f, cx - 0.25f * f, cy + 6.0f * f);
                }
                else if (kind == "salary")
                {
                    // ¥ 用字体轮廓画：字体排版框上下留白不对称，直接 DrawString 居中会偏低；
                    // 这里量出字形真实墨迹框，缩放到和线条图标同高，再把墨迹中心对到命中区中心。
                    // 万一当前数字字体没有 ¥ 字形（比如被换成了别的字体），退回原来的 DrawString。
                    float targetW = 12.4f * f, targetH = 13.4f * f;
                    bool drawn = false;
                    try
                    {
                        using (var path = new GraphicsPath())
                        {
                            float em = fontGlyphSalary.Size * g.DpiY / 72f;
                            path.AddString("¥", fontGlyphSalary.FontFamily, (int)fontGlyphSalary.Style,
                                em, PointF.Empty, StringFormat.GenericTypographic);
                            RectangleF box = path.GetBounds();
                            if (box.Width > 0.01f && box.Height > 0.01f)
                            {
                                var pts = path.PathPoints;
                                var types = path.PathTypes;
                                float kx = targetW / box.Width, ky = targetH / box.Height;
                                float mx = box.X + box.Width / 2f, my = box.Y + box.Height / 2f;
                                for (int i = 0; i < pts.Length; i++)
                                {
                                    pts[i].X = (pts[i].X - mx) * kx + cx;
                                    pts[i].Y = (pts[i].Y - my) * ky + cy;
                                }
                                using (var scaled = new GraphicsPath(pts, types))
                                using (var b = new SolidBrush(color))
                                    g.FillPath(b, scaled);
                                drawn = true;
                            }
                        }
                    }
                    catch { }
                    if (!drawn)
                        using (var b = new SolidBrush(color))
                            g.DrawString("¥", fontGlyphSalary, b, new RectangleF(r.X, r.Y, r.Width, r.Height), sfCenter);
                }
                else
                {
                    // 折叠箭头：宽度也撑到 11 DIP，视觉上不再比别的图标瘦小
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(cx - 5.5f * f, cy - 6.0f * f),
                        new PointF(cx + 5.5f * f, cy),
                        new PointF(cx - 5.5f * f, cy + 6.0f * f)
                    });
                }
            }
        }

        // ---------------- 交互 ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (screenReminder) return;
            if (e.Button == MouseButtons.Right)
            {
                OpenBubbleMenu();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                CloseBubbleMenu();
                CloseNotePanel();
                // 卡片被按住（要拖 / 要展开）：抽屉先收掉，别跟着卡片一起飘
                CloseDrawer();
                dragging = true;
                downAt = Environment.TickCount;
                moved = false;
                dragOrigin = Cursor.Position;
                formOrigin = Location;
                autoCollapse.Stop();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (screenReminder) return;
            if (dragging && (Control.MouseButtons & MouseButtons.Left) != 0)
            {
                Point cur = Cursor.Position;
                int dx = cur.X - dragOrigin.X;
                int dy = cur.Y - dragOrigin.Y;
                if (!moved && (Math.Abs(dx) > S(3) || Math.Abs(dy) > S(3))) moved = true;
                if (moved)
                {
                    Location = new Point(formOrigin.X + dx, formOrigin.Y + dy);
                    settings.ui.edge = "free";
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (screenReminder)
            {
                if (e.Button == MouseButtons.Left && reminding) DismissReminder();
                return;
            }
            if (Environment.GetEnvironmentVariable("WC_TRACE") == "1") store.Log("up " + e.Location + " " + Width + "x" + Height + " col=" + collapsed + " anim=" + animating + " rem=" + reminding + " chev=" + rectChevron + " gear=" + rectGear);
            if (e.Button != MouseButtons.Left) return;
            // 手抖一两像素不算拖：把窗口挪回去，照常当点击处理（不然点半天不展开）
            if (moved && Math.Abs(Location.X - formOrigin.X) + Math.Abs(Location.Y - formOrigin.Y) <= S(24) && Environment.TickCount - downAt < 450)
            {
                Location = formOrigin;
                moved = false;
            }
            bool wasDragging = moved;
            dragging = false;
            moved = false;

            if (wasDragging)
            {
                SnapToEdge();
                return;
            }

            // 提醒卡片右上角的 ×：任何时候都能把当前这条提醒关掉
            if (reminding && remindEvent != null && rectDismiss.Contains(e.Location))
            {
                DismissReminder();
                return;
            }

            // 判「现在是不是小卡片」看实际尺寸，不看 collapsed 字段：万一状态和窗口尺寸不同步，点一下自愈
            if (collapsed || Width <= S(DesignCollapsedW) + S(8))
            {
                collapsed = true;
                store.Log("点击小卡片: 展开 (animating=" + animating + ", 当前 " + Width + "x" + Height + ")");
                SetCollapsed(false);
                ResetAutoCollapse();
                return;
            }

            // 提醒卡片上点哪儿都能把它收起来——不然一张卡片杵四十分钟、点它又没反应，最烦人
            if (reminding) { store.Log("点击提醒卡片: 收起「" + (remindEvent == null ? "" : remindEvent.title) + "」"); DismissReminder(); return; }
            if (animating) FinishAnimNow();   // 动画还没走完也照样响应，先把它收尾
            if (GlyphRect(rectNote, 146).Contains(e.Location)) { ToggleNotePanel(); return; }
            if (GlyphRect(rectSalary, 118).Contains(e.Location))
            {
                ToggleSalary();
                return;
            }
            if (GlyphRect(rectGear, 90).Contains(e.Location)) { OpenSettings(); return; }
            if (GlyphRect(rectRefresh, 62).Contains(e.Location)) { RequestRefresh(true); return; }
            if (GlyphRect(rectChevron, 34).Contains(e.Location)) { SetCollapsed(true); return; }
            ResetAutoCollapse();
        }

        private void SnapToEdge()
        {
            Screen scr = CurrentScreen();
            Rectangle wa = scr.WorkingArea;
            int centerX = Left + Width / 2;
            if (Left + Width > wa.Right - S(20) || centerX > wa.Right - S(80))
            {
                settings.ui.edge = "right";
                Left = wa.Right - Width;
            }
            else if (Left < wa.Left + S(20) || centerX < wa.Left + S(80))
            {
                settings.ui.edge = "left";
                Left = wa.Left;
            }
            else
            {
                settings.ui.edge = "free";
            }
            settings.ui.x = Left;
            settings.ui.y = Top;
            SaveSettingsQuietly("设置变化");
        }

        public void OpenSettingsPublic()
        {
            OpenSettings();
        }

        private void OpenSettings()
        {
            OpenSettings(0);
        }

        /// <summary>打开设置，并直接切到某一页（0 考勤 / 1 固定日程 / 2 提醒 / 3 外观）。</summary>
        private void OpenSettings(int tab)
        {
            CloseBubbleMenu();
            autoCollapse.Stop();
            using (var dlg = new SettingsForm(store, settings, tab))
            {
                dlg.TopMost = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    apply.Apply(store.Load().Settings);
                    RequestRefresh(false);
                    RequestCalendar(false);
                    RequestOutlook(false);
                    var next = engine.NextEvent(settings, ScheduleSnapshot(), DateTime.Now);
                    if (next != null)
                    {
                        DateTime startTime = Reminders.ParseStart(next);
                        DateTime fireAt = startTime.AddMinutes(-Reminders.LeadOf(settings, next));
                        ShowBalloon("已排好：" + next.title + " " + startTime.ToString("HH:mm") + " 开始，" +
                            fireAt.ToString("HH:mm") + " 提醒");
                    }
                }
            }
            ResetAutoCollapse();
        }

        // ---------------- 胶囊菜单 ----------------

        /// <summary>右键卡片：贴着外侧弹出四颗胶囊。已经开着就再按一次收起。</summary>
        private void OpenBubbleMenu()
        {
            if (bubbleMenu != null)
            {
                // 正在播消失动画的那块菜单不算「开着」：右键直接开一份新的，不用等它淡完
                if (!bubbleMenu.IsClosing)
                {
                    CloseBubbleMenu();
                    return;
                }
                bubbleMenu = null;
            }
            if (screenReminder) return;
            autoCollapse.Stop();
            var m = new BubbleMenuForm(settings, theme, Bounds, settings.ui.alwaysOnTop,
                ApplyBubbleTheme,
                ApplyBubblePin,
                delegate(bool clockIn) { CloseBubbleMenu(); SendClock(clockIn); },
                AddQuickEvent,
                delegate(int tab) { CloseBubbleMenu(); OpenSettings(tab); },
                CreateBubbleTheme,
                PreviewNotification,
                BeginUpdateCheck,
                BeginUpdateApply);
            bubbleMenu = m;
            m.FormClosed += delegate
            {
                if (bubbleMenu == m) bubbleMenu = null;
                ResetAutoCollapse();
            };
            m.Show();
            // 菜单重新打开时把上次的更新状态带回来（下载完 / 有新版本别丢）
            if (updatePhase != UpdatePhase.Idle)
                m.SetUpdateState(updatePhase, updateRelease == null ? "" : updateRelease.version,
                    updateDetail, updateRelease == null ? "" : updateRelease.notes, updatePercent,
                    updateRelease != null && updateRelease.HasAsset);
            store.Log("打开胶囊菜单 @" + Bounds.Left + "," + Bounds.Top);
        }

        private void CloseBubbleMenu()
        {
            BubbleMenuForm m = bubbleMenu;
            bubbleMenu = null;
            if (m != null && !m.IsDisposed) m.CloseMenu();
        }

        // ---------------- 小纸条 ----------------

        private bool NoteUnread
        {
            get { return settings.note != null && settings.note.TotalUnread() > 0; }
        }

        /// <summary>重新连公共中继（只有连接身份变化 / 首次启动才由 applier 调用）。</summary>
        private void RestartNoteRelay()
        {
            if (noteRelay != null)
            {
                try { noteRelay.Dispose(); } catch { }
                noteRelay = null;
            }
            if (settings.note == null) settings.note = new NoteSettings();
            // 测试 / 出图探针不连真实中继（避免后台线程和网络依赖）
            if (RelayDisabledByEnvironment()) return;
            // 中继每次读取当前 settings.note：换了一份 Settings 也不用重建连接，
            // 昵称 / 对话变化只需 RefreshSubscriptions 原地刷新。
            var relay = new NoteRelay(delegate { return settings.note; });
            relay.Message += OnNoteMessage;
            relay.Profile += OnNoteProfile;
            relay.ConnectedChanged += delegate(bool on) { store.Log("小纸条中继: " + (on ? "已连接" : "断开")); };
            noteRelay = relay;
            relay.Start();
        }

        private void ToggleNotePanel()
        {
            if (notePanel != null) { CloseNotePanel(); return; }
            if (screenReminder || collapsed) return;
            autoCollapse.Stop();
            var p = new NotePanelForm(delegate { return settings; }, theme, Bounds, settings.ui.alwaysOnTop,
                SendNoteText, ReadNoteThread, ProbeNoteId, AddNoteContact,
                delegate { CloseNotePanel(); OpenSettings(6); },
                RenameNoteContact, DeleteNoteContact, SetNoteNickname);
            notePanel = p;
            p.FormClosed += delegate
            {
                if (notePanel == p) notePanel = null;
                ResetAutoCollapse();
            };
            p.Show();
            noteFlashAt = 0;      // 打开面板就把闪光停掉，未读点/徽标还在
            Invalidate();
            store.Log("打开小纸条面板 @" + Bounds.Left + "," + Bounds.Top);
        }

        private void CloseNotePanel()
        {
            NotePanelForm p = notePanel;
            notePanel = null;
            if (p != null && !p.IsDisposed) p.ClosePanel();
        }

        private bool SendNoteText(string toId, string text)
        {
            if (string.IsNullOrEmpty(toId) || string.IsNullOrEmpty(text)) return false;
            if (noteRelay == null || !noteRelay.Connected)
            {
                store.Log("小纸条发送失败: 中继未连接");
                return false;
            }
            bool ok = noteRelay.SendMessage(toId, text);
            if (!ok)
            {
                store.Log("小纸条发送失败: publish 失败");
                return false;
            }
            settings.note.Append(new NoteMessage(settings.note.id, toId, text, NoteRelay.Now()), false);
            SaveSettingsQuietly("设置变化");
            Invalidate();
            return true;
        }

        private void ReadNoteThread(string contactId)
        {
            if (string.IsNullOrEmpty(contactId)) return;
            settings.note.MarkRead(contactId);
            SaveSettingsQuietly("设置变化");
            Invalidate();
        }

        private void ProbeNoteId(string id, Action<bool> done)
        {
            if (noteRelay == null) { if (done != null) done(false); return; }
            noteRelay.Probe(id, done);
        }

        /// <summary>新建对话：5 位数字 = 和这个 ID 的人 1:1；其它 5 位字符 = 对话码（同码的人进同一个对话）。</summary>
        private bool AddNoteContact(string id, string remark)
        {
            bool added = settings.note.AddConversation(id, remark);
            settings.note.Thread(NoteSettings.NormalizeCode(id));
            SaveSettingsQuietly("设置变化");
            Invalidate();
            store.Log("小纸条新建对话: " + id + (settings.note.IsRoom(id) ? "（对话码）" : "") + (string.IsNullOrEmpty(remark) ? "" : " / " + remark));
            // 对话码要订阅对应主题；个人对话要订阅对方 presence（retained）拿昵称。顺便重发自己的 presence。
            if (noteRelay != null) noteRelay.RefreshSubscriptions();
            return added;
        }

        /// <summary>收到别人的「在线状态 / 昵称」：把昵称补到本机这个对话上，列表 / 聊天头部就直接显示名字。</summary>
        private void OnNoteProfile(string id, string nick, bool online)
        {
            if (settings.note == null || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(nick)) return;
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (settings.note == null) return;
                    if (!settings.note.SetContactNick(id, nick)) return;
                    SaveSettingsQuietly("设置变化");
                    if (notePanel != null) notePanel.RefreshData();
                    Invalidate();
                    store.Log("小纸条昵称: " + id + " = " + nick);
                });
            }
            catch { }
        }

        /// <summary>右键删除对话：本机这段会话一起删掉。</summary>
        private void DeleteNoteContact(string id)
        {
            if (settings.note == null || string.IsNullOrEmpty(id)) return;
            settings.note.RemoveContact(id);
            SaveSettingsQuietly("设置变化");
            Invalidate();
            store.Log("小纸条删除对话: " + id);
        }

        private void RenameNoteContact(string id, string remark)
        {
            if (settings.note == null || string.IsNullOrEmpty(id)) return;
            var c = settings.note.Contact(id);
            if (c == null) return;
            remark = (remark ?? "").Trim();
            if (remark.Length > NoteSettings.MaxRemark) remark = remark.Substring(0, NoteSettings.MaxRemark);
            c.remark = remark;
            SaveSettingsQuietly("设置变化");
            Invalidate();
            store.Log("小纸条改备注: " + id + (string.IsNullOrEmpty(remark) ? " (空)" : " / " + remark));
        }

        /// <summary>
        /// 卡片页「昵称」胶囊双击改本机昵称：落盘 + 重启中继（presence 里带新昵称，15s keepalive 也会重播）。
        /// 面板自己已经先把头部画成新昵称，这里只负责持久化和广播。
        /// </summary>
        private void SetNoteNickname(string nick)
        {
            if (settings.note == null) settings.note = new NoteSettings();
            nick = NoteSettings.CleanNick(nick);
            string previousNick = settings.note.nickname ?? "";
            if (string.Equals(previousNick, nick, StringComparison.Ordinal)) return;
            settings.note.nickname = nick;
            var committed = commit.Commit(settings, "小纸条改昵称");
            if (!committed.IsSuccess)
            {
                settings.note.nickname = previousNick;
                if (committed.IsFailed)
                    ShowBalloon("昵称没有保存成功：" + committed.Message);
                else
                    ShowBalloon("昵称未提交：本次运行不写配置（WC_NO_SAVE=1）。");
                return;
            }
            // 连接身份没变：变化计划选择原地重发 presence，不重建中继。
            if (notePanel != null) notePanel.RefreshData();
            Invalidate();
            store.Log("小纸条改昵称: " + (string.IsNullOrEmpty(nick) ? "(空)" : nick));
        }

        /// <summary>收到别人的小纸条：存进 8 条、置未读、卡片边框闪两次。</summary>
        private void OnNoteMessage(NoteIncoming msg)
        {
            if (msg == null || settings.note == null) return;
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (settings.note == null) return;
                    var note = settings.note;
                    bool room = note.IsRoom(msg.to);
                    string key;
                    if (room)
                    {
                        key = msg.to;
                        // 对话码对话：只认本机已经存过的那个对话；删掉的对话即使中继还订阅着，也不该再冒出来
                        if (note.Contact(key) == null) return;
                        // 自己发进对话码的消息：本地已经记过一条，中继会把发布回显给订阅者，这里丢掉
                        if (string.Equals(msg.from, note.id, StringComparison.Ordinal)) return;
                    }
                    else
                    {
                        if (!string.Equals(msg.to, note.id, StringComparison.Ordinal)) return;
                        key = msg.from;
                        // 陌生人发来的纸条：先按 ID 建一个对话（备注空着，显示 ID），不然用户读不到
                        if (note.Contact(key) == null) note.AddContact(key, "");
                    }
                    var m = new NoteMessage(msg.from, room ? key : note.id, msg.text, msg.ts, msg.nick);
                    note.Append(m, true);
                    // 顺手把发送者昵称补到本机对话上（个人对话会显示成名字；群聊气泡里用消息自带的昵称）
                    if (!string.IsNullOrEmpty(msg.nick)) note.SetContactNick(msg.from, msg.nick);
                    SaveSettingsQuietly("设置变化");
                    noteFlashAt = Environment.TickCount;
                    noteBlinkLast = Environment.TickCount;
                    noteBlinkAt = Environment.TickCount;
                    if (notePanel != null)
                    {
                        if (notePanel.SelectedId == key)
                        {
                            note.MarkRead(key);
                            SaveSettingsQuietly("设置变化");
                        }
                        notePanel.RefreshData();
                    }
                    Invalidate();
                    store.Log((room ? "收到对话码消息: " : "收到小纸条: ") + msg.from + " / " + msg.text);
                });
            }
            catch { }
        }

        private static readonly double[] RimT = new double[] { 0, 0.08, 0.24, 0.40, 0.60, 0.75, 0.90, 1 };
        private static readonly double[] RimA = new double[] { 0, 0.95, 0.12, 0.90, 0.10, 0.50, 0, 0 };

        private static double RimKeyframes(double p)
        {
            if (p <= 0) return 0;
            if (p >= 1) return 0;
            for (int i = 1; i < RimT.Length; i++)
            {
                if (p <= RimT[i])
                {
                    double span = RimT[i] - RimT[i - 1];
                    if (span <= 0) return RimA[i];
                    double k = (p - RimT[i - 1]) / span;
                    return RimA[i - 1] + (RimA[i] - RimA[i - 1]) * k;
                }
            }
            return 0;
        }

        /// <summary>收到时的整圈贴边闪光（亮两次，1300ms）；未读的每 5 秒快闪改到左上角（DrawNoteCorner）。</summary>
        private double NoteRimAlpha()
        {
            int now = Environment.TickCount;
            double a = 0;
            if (noteFlashAt != 0)
            {
                int el = now - noteFlashAt;
                if (el >= 0 && el < 1300) a = RimKeyframes(el / 1300.0);
            }
            return Math.Max(0, Math.Min(1, a));
        }

        // ---------------- 未读：左上角每 5 秒快闪两下 ----------------
        internal const int NoteBlinkIntervalMs = 5000;   // 每 5 秒
        internal const int NoteBlinkSpanMs = 420;        // 一轮两次快闪共占 ~420ms

        /// <summary>快闪包络：elapsedMs 处的亮度（0 = 不亮）。两下：0-150ms、250-400ms，中间有暗隙。</summary>
        internal static double CornerFlashAlpha(int elapsedMs)
        {
            if (elapsedMs < 0 || elapsedMs >= NoteBlinkSpanMs) return 0;
            return Math.Max(FlashPulse(elapsedMs, 0, 150), FlashPulse(elapsedMs, 250, 150));
        }

        private static double FlashPulse(int elapsedMs, int startMs, int durMs)
        {
            double u = (elapsedMs - startMs) / (double)Math.Max(1, durMs);
            if (u <= 0 || u >= 1) return 0;
            return Math.Sin(u * Math.PI);            // 快进快出，像闪两下
        }

        /// <summary>当前角落快闪的强度（0..1）；减少动态效果 / 高对比度 / 没有未读时为 0。</summary>
        private double NoteCornerAlpha()
        {
            if (!NoteUnread || settings.ui.reducedMotion || SystemInformation.HighContrast) return 0;
            if (noteBlinkAt == 0) return 0;
            return CornerFlashAlpha(Environment.TickCount - noteBlinkAt);
        }

        /// <summary>每 5 秒安排一次左上角快闪（1 秒的 tick 里调用；不用额外定时器）。</summary>
        private void MaybeBlinkUnread()
        {
            if (!NoteUnread || settings.ui.reducedMotion || SystemInformation.HighContrast) return;
            int now = Environment.TickCount;
            if (noteBlinkLast == 0 || now - noteBlinkLast >= NoteBlinkIntervalMs)
            {
                noteBlinkLast = now;
                noteBlinkAt = now;
                Invalidate();                         // flowT 会因为 NoteRimActive() 在闪的这 420ms 里持续重画
            }
        }

        /// <summary>
        /// 左上角快闪：沿圆角左上角画一段亮带（顶部 + 左侧各延伸一小段），加一团柔光。
        /// 收起态 / 展开态共用，圆角跟着卡片走；只画在卡片里面，不会超出窗口。
        /// </summary>
        private void DrawNoteCorner(Graphics g, GraphicsPath cardPath)
        {
            double a = NoteCornerAlpha();
            if (a < 0.02) return;
            var saved = g.Save();
            g.SetClip(cardPath, CombineMode.Intersect);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int inset = S(1);
            int r = S(Skin.IsDefault ? (collapsed ? 13 : 17) : Math.Max(2, CardRadiusDip - 1));
            float ext = (float)Math.Min((double)S(20), (double)(Width * 0.14f));
            using (var gp = new GraphicsPath())
            {
                gp.AddArc(new Rectangle(inset, inset, r * 2, r * 2), 180, 90);
                gp.StartFigure();
                gp.AddLine(inset, inset + r, inset, (int)(inset + r + ext));
                gp.StartFigure();
                gp.AddLine(inset + r, inset, (int)(inset + r + ext), inset);
                for (int i = 3; i >= 1; i--)
                    using (var pen = new Pen(Color.FromArgb((int)(a * 26 / i), theme.accent), Math.Max(2f, S(2) * i)))
                    { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, gp); }
                using (var pen = new Pen(Color.FromArgb((int)(a * 235), theme.accent), Math.Max(1.6f, S(2.2f))))
                { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, gp); }
                using (var pen = new Pen(Color.FromArgb((int)(a * 140), Color.White), Math.Max(1f, S(1))))
                { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, gp); }
            }
            // 角上一团柔光，让「闪两下」在深色/浅色主题上都看得见
            DrawGlowEllipse(g, new RectangleF(-S(12), -S(12), S(64), S(64)), new PointF(S(2), S(2)), theme.accent, 0.40 * a);
            g.Restore(saved);
        }

        private bool NoteRimActive() { return NoteRimAlpha() > 0.02 || NoteCornerAlpha() > 0.02; }

        private void DrawNoteRim(Graphics g, GraphicsPath cardPath)
        {
            double a = NoteRimAlpha();
            if (a < 0.02) return;
            var rim = new Rectangle(S(1), S(1), Math.Max(2, Width - S(2)), Math.Max(2, Height - S(2)));
            // 贴边闪光跟着皮肤的圆角走：默认皮肤保持原来的 13 / 17 DIP（比卡片小 1 DIP）
            int rimRadius = Skin.IsDefault ? (collapsed ? 13 : 17) : Math.Max(1, CardRadiusDip - 1);
            using (var path = RoundPath(rim, S(rimRadius)))
            {
                for (int i = 3; i >= 1; i--)
                    using (var pen = new Pen(Color.FromArgb((int)(a * 24 / i), theme.accent), Math.Max(2f, S(2) * i)))
                        g.DrawPath(pen, path);
                using (var pen = new Pen(Color.FromArgb((int)(a * 225), theme.accent), Math.Max(1.5f, scale * 2f)))
                    g.DrawPath(pen, path);
                using (var pen = new Pen(Color.FromArgb((int)(a * 80), Color.White), Math.Max(1f, scale)))
                    g.DrawPath(pen, path);
            }
        }


        // ---------------- 预览通知（气泡菜单「预览通知」） ----------------

        /// <summary>
        /// 预览某一类通知：造一条 id=preview 的临时日程，走真实提醒动画
        /// （按该类日程的配色 / 范围 / 扩散样式），只放一遍就自动退场；不写配置、不弹托盘气泡。
        /// </summary>
        private void PreviewNotification(string kind)
        {
            if (reminding) DismissReminder();
            if (collapsed) SetCollapsed(false);
            DateTime now = DateTime.Now;
            var e = new ReminderEvent();
            e.id = "preview";
            if (kind == "off")
            {
                e.source = "alert";
                e.title = "该下班啦";
                e.detail = ManualClock.ShouldUseLark(settings)
                    ? "在飞书发「2」打卡后自动消失"
                    : "右键「下班」记录本地打卡后自动消失";
                e.start = Reminders.FormatStart(now);
            }
            else if (kind == "lark")
            {
                e.source = "lark";
                e.title = "产品部周会";
                e.place = "3F 会议室";
                e.detail = "过一下本期主题背景和小纸条改动";
                e.start = Reminders.FormatStart(now);
                e.end = Reminders.FormatStart(now.AddMinutes(30));
            }
            else if (kind == "outlook")
            {
                e.source = "outlook";
                e.title = "和设计对稿";
                e.place = "Teams 线上";
                e.detail = "磨砂玻璃背景视觉确认";
                e.start = Reminders.FormatStart(now);
                e.end = Reminders.FormatStart(now.AddMinutes(30));
            }
            else if (kind == "local")
            {
                e.source = "local";
                e.title = "整理周报";
                e.place = "";
                e.detail = "把本周进度写进周报";
                e.start = Reminders.FormatStart(now);
                e.end = Reminders.FormatStart(now.AddMinutes(30));
            }
            else if (kind == "lunch")
            {
                e.source = "break";
                e.title = "午休时间到";
                e.detail = "去吃饭，先歇一会儿";
                e.start = Reminders.FormatStart(now);
                e.end = Reminders.FormatStart(now.AddMinutes(60));
            }
            else
            {
                e.source = "break";
                e.title = "上班时间到";
                e.detail = "到点了，收收心";
                e.start = Reminders.FormatStart(now);
            }
            store.Log("预览通知: " + kind);
            StartReminder(e);
        }

        // ---------------- 检查更新（气泡菜单「检查更新」） ----------------

        /// <summary>开始检查 GitHub 最新 Release（后台线程，结果推给气泡菜单）。</summary>
        private void BeginUpdateCheck()
        {
            if (updateBusy || updatePhase == UpdatePhase.Downloading) return;
            updateBusy = true;
            updatePhase = UpdatePhase.Checking;
            updateDetail = "";
            ApplyUpdateStateToBubble();
            store.Log("检查更新: 开始");
            var th = new Thread(delegate()
            {
                try
                {
                    UpdateRelease rel = UpdateService.Check();
                    updateRelease = rel;
                    if (!UpdateService.IsNewer(rel.version, AppInfo.Version))
                    {
                        updatePhase = UpdatePhase.UpToDate;
                        updateDetail = "";
                        store.Log("检查更新: 已是最新 v" + AppInfo.Version);
                    }
                    else
                    {
                        updatePhase = UpdatePhase.Available;
                        updateDetail = "";
                        store.Log("检查更新: 发现新版本 v" + rel.version + "（" + (rel.HasAsset ? "有自动更新包" : "只能打开发布页") + "）");
                    }
                }
                catch (Exception ex)
                {
                    updatePhase = UpdatePhase.Error;
                    updateDetail = ex.Message;
                    store.Log("检查更新失败: " + ex.Message);
                }
                finally
                {
                    updateBusy = false;
                }
                TryPostUpdateState();
            });
            th.IsBackground = true;
            th.Start();
        }

        /// <summary>下载并应用全量更新；已经下载好就直接替换重启。</summary>
        private void BeginUpdateApply()
        {
            if (updatePhase == UpdatePhase.Ready && !string.IsNullOrEmpty(updateExePath) && File.Exists(updateExePath))
            {
                ApplyDownloadedUpdate();
                return;
            }
            if (updateBusy || updateRelease == null || !updateRelease.HasAsset) return;
            updateBusy = true;
            updatePhase = UpdatePhase.Downloading;
            updatePercent = 0;
            ApplyUpdateStateToBubble();
            store.Log("自动更新: 开始下载 v" + updateRelease.version);
            UpdateRelease rel = updateRelease;
            var th = new Thread(delegate()
            {
                try
                {
                    string path = UpdateService.Download(rel, delegate(long done, long total)
                    {
                        int pct = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
                        if (pct == updatePercent) return;
                        updatePercent = pct;
                        TryPostUpdateState();
                    });
                    string message;
                    if (!UpdateService.VerifyDownloaded(path, rel, out message))
                        throw new InvalidOperationException(message);
                    updateExePath = path;
                    updatePhase = UpdatePhase.Ready;
                    updatePercent = 100;
                    store.Log("自动更新: 下载完成 " + path);
                }
                catch (Exception ex)
                {
                    updatePhase = UpdatePhase.Error;
                    updateDetail = "下载失败：" + ex.Message;
                    store.Log("自动更新下载失败: " + ex.Message);
                }
                finally
                {
                    updateBusy = false;
                }
                TryPostUpdateState();
                if (updatePhase == UpdatePhase.Ready)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            ShowBalloon("新版本 v" + updateRelease.version + " 已下载\r\n右键卡片 → 检查更新 → 立即重启");
                        });
                    }
                    catch { }
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        /// <summary>把下载好的 exe 交给临时 updater 脚本替换并重启（本进程随即退出）。</summary>
        private void ApplyDownloadedUpdate()
        {
            try
            {
                UpdateService.LaunchUpdater(updateExePath);
                store.Log("自动更新: 启动替换脚本，退出当前实例");
                Close();
            }
            catch (Exception ex)
            {
                updatePhase = UpdatePhase.Error;
                updateDetail = "启动更新失败：" + ex.Message;
                ApplyUpdateStateToBubble();
                Invalidate();
                store.Log("自动更新失败: " + ex.Message);
            }
        }

        /// <summary>后台线程推进状态后，回到 UI 线程刷新气泡菜单。</summary>
        private void TryPostUpdateState()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    ApplyUpdateStateToBubble();
                    Invalidate();
                });
            }
            catch { }
        }

        private void ApplyUpdateStateToBubble()
        {
            BubbleMenuForm m = bubbleMenu;
            if (m == null || m.IsDisposed) return;
            m.SetUpdateState(updatePhase, updateRelease == null ? "" : updateRelease.version,
                updateDetail, updateRelease == null ? "" : updateRelease.notes,
                updatePercent, updateRelease != null && updateRelease.HasAsset);
        }

        /// <summary>菜单里点色卡：立刻换主题，菜单自己会更新选中环。</summary>
        private void ApplyBubbleTheme(string id)
        {
            if (string.IsNullOrEmpty(id) || id == settings.ui.theme) return;
            string previous = settings.ui.theme;
            settings.ui.theme = id;
            var committed = commit.Commit(settings, "胶囊菜单换主题");
            if (!committed.IsSuccess)
            {
                settings.ui.theme = previous;
                if (committed.IsFailed) ShowBalloon("主题没有保存成功：" + committed.Message);
                else ShowBalloon("主题未提交：本次运行不写配置（WC_NO_SAVE=1）。");
                return;
            }
            Invalidate();
            store.Log("胶囊菜单换主题: " + id);
        }

        /// <summary>菜单里点「＋ 新建主题」：存一套自建主题、立刻应用，返回新 id 给气泡刷新列表。</summary>
        private string CreateBubbleTheme(string name, string bg, string bar)
        {
            var ct = new CustomTheme();
            ct.id = "my-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            ct.name = string.IsNullOrWhiteSpace(name) ? "我的主题" : name.Trim();
            ct.bg = bg;
            ct.bar = bar;
            ct.accent = bar;
            if (settings.customThemes == null) settings.customThemes = new List<CustomTheme>();
            settings.customThemes.Add(ct);
            Themes.SetCustom(settings.customThemes);

            string previous = settings.ui.theme;
            settings.ui.theme = ct.id;
            var committed = commit.Commit(settings, "胶囊菜单新建主题");
            if (!committed.IsSuccess)
            {
                settings.ui.theme = previous;
                settings.customThemes.Remove(ct);
                Themes.SetCustom(settings.customThemes);
                if (committed.IsFailed) ShowBalloon("主题没有保存成功：" + committed.Message);
                else ShowBalloon("主题未提交：本次运行不写配置（WC_NO_SAVE=1）。");
                return "";
            }
            Invalidate();
            store.Log("胶囊菜单新建主题: " + ct.name + " (" + ct.id + ")");
            return ct.id;
        }

        /// <summary>菜单里点「窗口置顶」：直接切换，双向同步设置页的勾选。</summary>
        private void ApplyBubblePin(bool on)
        {
            settings.ui.alwaysOnTop = on;
            KeepTopMost();
            SaveSettingsQuietly("设置变化");
            Invalidate();
            store.Log("胶囊菜单窗口置顶: " + (on ? "开" : "关"));
        }

        /// <summary>菜单里的 +15 / +30 / +60：用面板里填的标题和地点，建一条 30 分钟的日程。</summary>
        private void AddQuickEvent(string title, string place, int minutes)
        {
            if (minutes <= 0) minutes = 30;
            if (string.IsNullOrWhiteSpace(title)) title = "临时日程";
            DateTime start = DateTime.Now.AddMinutes(minutes);
            DateTime end = start.AddMinutes(30);
            var e = new ReminderEvent();
            e.id = "local:" + Guid.NewGuid().ToString("N").Substring(0, 8);
            e.source = "local";
            e.title = title.Trim();
            e.detail = "";
            e.place = place == null ? "" : place.Trim();
            e.start = Reminders.FormatStart(start);
            e.end = Reminders.FormatStart(end);
            e.leadMinutes = 0;
            if (settings.events == null) settings.events = new List<ReminderEvent>();
            settings.events.Add(e);
            SaveSettingsQuietly("设置变化");
            Recompute();
            ShowBalloon("已添加：" + e.title + " " + start.ToString("HH:mm") + "–" + end.ToString("HH:mm") +
                (string.IsNullOrEmpty(e.place) ? "" : "\r\n" + e.place));
            store.Log("胶囊菜单新增日程: " + e.title + " @ " + e.start);
        }

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            var miSettings = new ToolStripMenuItem("设置…");
            miSettings.Click += delegate { OpenSettings(); };
            var miRrefresh = new ToolStripMenuItem("立即同步飞书");
            miRrefresh.Click += delegate { RequestRefresh(true); };
            var miCal = new ToolStripMenuItem("刷新飞书日程");
            miCal.Click += delegate { RequestCalendar(true); };
            var miOutlook = new ToolStripMenuItem("刷新 Outlook 日程");
            miOutlook.Click += delegate { RequestOutlook(true); };
            miOutlook.Enabled = settings.outlook != null && settings.outlook.HasEnabled;
            var miNext = new ToolStripMenuItem("下一个日程");
            miNext.Enabled = false;
            var miPreview = new ToolStripMenuItem("预览提醒动画");
            miPreview.Click += delegate
            {
                var e = new ReminderEvent();
                e.id = "preview";
                e.title = "产品部周会";
                e.start = Reminders.FormatStart(DateTime.Now.AddMinutes(5));
                e.source = "local";
                StartReminder(e);
            };
            bool useLarkClock = ManualClock.ShouldUseLark(settings);
            var miIn = new ToolStripMenuItem(useLarkClock ? "在飞书发「1」上班打卡" : "记录本地上班打卡");
            miIn.Click += delegate { SendClock(true); };
            var miOut = new ToolStripMenuItem(useLarkClock ? "在飞书发「2」下班打卡" : "记录本地下班打卡");
            miOut.Click += delegate { SendClock(false); };
            var miMute = new ToolStripMenuItem("忽略今天的下班提醒");
            miMute.Click += delegate { MuteOffAlert(); };
            var miDismiss = new ToolStripMenuItem("关闭当前提醒");
            miDismiss.Click += delegate { DismissReminder(); };
            var miExpand = new ToolStripMenuItem("展开 / 收起");
            miExpand.Click += delegate { SetCollapsed(!collapsed); };
            var miToday = new ToolStripMenuItem("今天设为休息日 / 上班日");
            miToday.Click += delegate { ToggleToday(); };
            var miTop = new ToolStripMenuItem("窗口置顶");
            miTop.CheckOnClick = true;
            miTop.Checked = settings.ui.alwaysOnTop;
            miTop.Click += delegate
            {
                settings.ui.alwaysOnTop = miTop.Checked;
                KeepTopMost();
                SaveSettingsQuietly("设置变化");
            };
            var miStart = new ToolStripMenuItem("开机自动启动");
            miStart.CheckOnClick = true;
            miStart.Checked = Startup.IsEnabled();
            miStart.Click += delegate
            {
                Startup.SetEnabled(miStart.Checked);
            };
            var miExport = new ToolStripMenuItem("导出配置…");
            miExport.Click += delegate { ExportSettings(); };
            var miImport = new ToolStripMenuItem("导入配置…");
            miImport.Click += delegate { ImportSettings(); };
            var miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { Close(); };

            m.Items.Add(miSettings);
            m.Items.Add(miRrefresh);
            m.Items.Add(miCal);
            m.Items.Add(miOutlook);
            m.Items.Add(miNext);
            m.Items.Add(miPreview);
            m.Items.Add(miIn);
            m.Items.Add(miOut);
            m.Items.Add(miDismiss);
            m.Items.Add(miMute);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(miExpand);
            m.Items.Add(miToday);
            m.Items.Add(miTop);
            m.Items.Add(miStart);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(miExport);
            m.Items.Add(miImport);
            m.Items.Add(miExit);
            m.Opening += delegate
            {
                // 设置窗口里也能改「窗口置顶」，菜单每次打开都按当前配置刷新勾选状态，
                // 否则勾是旧的：点一下会把用户刚打开/关掉的置顶又改回去。
                miTop.Checked = settings.ui.alwaysOnTop;
                miNext.Text = "下一个日程：" + NextEventText();
                miDismiss.Enabled = reminding && remindEvent != null;
                miDismiss.Text = remindEvent != null ? "关闭当前提醒：" + remindEvent.title : "关闭当前提醒";
                bool hasChat = ManualClock.ShouldUseLark(settings);
                // 没飞书对话也能用：记在本机，文案跟着来源变
                miIn.Enabled = true;
                miOut.Enabled = true;
                miIn.Text = hasChat ? "在飞书发「1」上班打卡" : "记录本地上班打卡";
                miOut.Text = hasChat ? "在飞书发「2」下班打卡" : "记录本地下班打卡";
                miCal.Enabled = hasChat && settings.calendarEnabled;
            };
            return m;
        }

        private void ExportSettings()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "导出配置";
                dlg.Filter = "配置文件 (*.json)|*.json";
                dlg.FileName = "上班工具箱配置.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var exported = store.Export(settings, dlg.FileName);
                if (exported.Succeeded)
                    ShowBalloon("配置已导出\r\n" + dlg.FileName);
                else
                    ShowBalloon("导出失败：" + exported.Message);
            }
        }

        private void ImportSettings()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "导入配置";
                dlg.Filter = "配置文件 (*.json)|*.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var imported = store.Import(dlg.FileName);
                if (!imported.Succeeded)
                {
                    ShowBalloon("导入失败：" + imported.Message);
                    return;
                }
                Settings previous = settings;
                settings = imported.Settings;
                var committed = commit.Commit(settings, "导入配置");
                if (!committed.IsSuccess)
                {
                    settings = previous;
                    Themes.SetCustom(settings.customThemes);
                    if (committed.IsFailed)
                        ShowBalloon("导入没有保存成功：\r\n" + committed.Message + "\r\n上一份有效配置没有被改动。");
                    else
                        ShowBalloon("导入未提交：本次运行不写配置（WC_NO_SAVE=1）。");
                    return;
                }
                RequestRefresh(false);
                RequestCalendar(false);
                Invalidate();
                ShowBalloon("配置已导入");
            }
        }

        /// <summary>首次使用时的引导，回来之后重新应用设置。</summary>
        public void RunFirstRun()
        {
            autoCollapse.Stop();
            using (var dlg = new FirstRunForm(store, settings))
            {
                dlg.TopMost = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    apply.Apply(store.Load().Settings);
                    RequestRefresh(false);
                    RequestCalendar(false);
                    Invalidate();
                }
            }
            ResetAutoCollapse();
        }

        private void SendClock(bool clockIn)
        {
            string label = clockIn ? "上班" : "下班";

            // 没有飞书对话：不弹错误框，直接把今天这次打卡记在本机并落盘
            if (!ManualClock.ShouldUseLark(settings))
            {
                DateTime now = DateTime.Now;
                ManualClock.Record(settings, clockIn, now);
                SaveSettingsQuietly("本地打卡");
                Recompute();
                ShowBalloon(ManualClock.BalloonText(clockIn, now));
                return;
            }

            string text = clockIn ? "1" : "2";
            string question = "将以你本人的身份，向「" + settings.lark.botName + "」发送「" + text + "」，记一条" + label + "打卡。\r\n\r\n确定发送吗？";
            if (MessageBox.Show(this, question, "飞书打卡", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            var cfg = settings.lark;
            autoCollapse.Stop();
            ThreadPool.QueueUserWorkItem(delegate (object state)
            {
                string error;
                bool ok = lark.SendText(cfg, text, out error);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (ok)
                        {
                            ShowBalloon("已发送「" + text + "」· " + label + "打卡");
                            RequestRefresh(false);
                        }
                        else if (error != null && error.IndexOf("scope", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            MessageBox.Show(this,
                                "飞书拒绝以你本人身份发消息：当前授权缺少 im:message.send_as_user 权限。\r\n\r\n" +
                                "在终端执行一次下面这条命令并完成授权即可启用一键打卡：\r\n\r\n" +
                                "lark-cli auth login --scope \"im:message.send_as_user\"",
                                "需要补充飞书授权", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            ShowBalloon("没有发消息权限，已在弹窗中说明授权方法");
                        }
                        else
                        {
                            ShowBalloon("发送失败：" + LarkClient.Shorten(error, 90));
                        }
                        ResetAutoCollapse();
                    });
                }
                catch { }
            });
        }

        private void ToggleToday()
        {
            string key = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            HolidayEntry existing = null;
            foreach (var ex in settings.exceptions)
            {
                if (ex != null && ex.date == key) { existing = ex; break; }
            }
            if (existing != null)
            {
                settings.exceptions.Remove(existing);
            }
            else
            {
                var plan = Policy.Build(settings, DateTime.Today);
                settings.exceptions.Add(new HolidayEntry { date = key, kind = plan.isWorkday ? "off" : "work" });
            }
            SaveSettingsQuietly("设置变化");
            Recompute();
        }

        public void EnableTray()
        {
            try
            {
                tray = new NotifyIcon();
                tray.Icon = MakeTrayIcon();
                tray.Text = "上班工具箱";
                tray.Visible = true;
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += delegate
                {
                    if (!Visible) { Show(); }
                    SetCollapsed(!collapsed);
                };
            }
            catch { }
        }

        private Icon MakeTrayIcon()
        {
            int size = 32;
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Color accent = theme != null ? theme.accent : Color.FromArgb(76, 141, 255);
                    Color bg = theme != null ? theme.bgBottom : Color.FromArgb(28, 34, 48);
                    Color fg = theme != null ? theme.textMain : Color.FromArgb(232, 237, 247);
                    using (var bgBrush = new SolidBrush(bg))
                        g.FillEllipse(bgBrush, 1, 1, 30, 30);
                    using (var pen = new Pen(accent, 2.4f))
                        g.DrawEllipse(pen, 4, 4, 24, 24);
                    using (var pen = new Pen(fg, 2.2f))
                    {
                        g.DrawLine(pen, 16, 16, 16, 9);
                        g.DrawLine(pen, 16, 16, 22, 19);
                    }
                }
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }
    }

    public static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WorkCountdown";

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }
}
