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
    public class WidgetForm : Form
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

        private AttendanceSnapshot snapshot;
        private CardStatus status;
        private ThemeDef theme;
        private float scale = 1f;
        private bool fetching;
        private bool lastFetchFailed;
        private DateTime lastDay = DateTime.Today;

        private bool collapsed;
        private readonly System.Windows.Forms.Timer tick;
        private readonly System.Windows.Forms.Timer poll;
        private readonly System.Windows.Forms.Timer autoCollapse;
        private readonly System.Windows.Forms.Timer animT;
        private readonly System.Windows.Forms.Timer calPoll;
        private readonly System.Windows.Forms.Timer remindT;
        private readonly System.Windows.Forms.Timer flowT;
        private readonly System.Windows.Forms.Timer topT;

        // 分层窗口：整帧画在 32 位位图上，圆角由像素 alpha 收边（Region 剪出来的是硬锯齿边）
        private Bitmap frame;
        private byte frameAlpha = 240;

        // 日程提醒
        private List<ReminderEvent> larkEvents = new List<ReminderEvent>();
        private string larkEventError;
        private bool calFetching;
        private readonly HashSet<string> notified = new HashSet<string>();
        private bool reminding;
        private DateTime remindAt;
        private ReminderEvent remindEvent;
        private const int RemindInMs = 1000;    // 两块色块先后扫入（A 0-300ms，B 260-660ms）
        private int RemindHoldMs { get { return Math.Max(3, Math.Min(20, settings.ui.reminderHoldSeconds)) * 1000; } } // // 完全覆盖
        private const int RemindOutMs = 960;   // 两块色块扫出

        // 常驻「该下班啦」：到点没打卡就一直在，每 5 分钟重播一次入场动画
        private const int OffAlertRepeatMs = 5 * 60 * 1000;
        private const int OffAlertAnimMs = 1100;   // 只有这段需要 30fps，其余时间静止不耗 CPU
        private bool offAlertOn;
        private DateTime offAlertNext;
        private DateTime offAlertMutedDay = DateTime.MinValue;   // 手动「忽略今天」
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

        private bool animating;
        private int animFromW, animFromH, animFromX, animToW, animToH, animToX;
        private int animStart;
        private const int AnimMs = 150;
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
        private NotifyIcon tray;
        private readonly ToolTip reminderTip = new ToolTip { InitialDelay = 800, ReshowDelay = 200, AutoPopDelay = 20000, ShowAlways = true };

        private readonly Font fontBig;
        private readonly Font fontBigText;
        private readonly Font fontStatus;
        private readonly Font fontTiny;
        private readonly Font fontSmallBold;
        private readonly Font fontSmall;
        private readonly Font fontCollapsedNum;
        private readonly Font fontCollapsedLabel;

        private readonly StringFormat sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private readonly StringFormat sfNear = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        private readonly StringFormat sfFar = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        private Rectangle rectGear, rectRefresh, rectChevron;

        public WidgetForm(SettingsStore store, Settings settings)
        {
            this.store = store;
            this.settings = settings;
            this.lark = new LarkClient(store);
            settings.NormalizeSchedules();      // 老配置 / 直接 new 出来的 Settings 也要有固定日程
            settings.NormalizeCustomThemes();
            this.theme = Themes.Get(settings.ui.theme);

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
            Text = "下班倒计时";
            MinimumSize = new Size(S(DesignCollapsedW), S(DesignCollapsedH));

            fontBig = MakeFont("Consolas", 28f, FontStyle.Bold);
            fontBigText = UiFont.Make(17f, FontStyle.Bold);
            fontStatus = UiFont.Make(TypeStatusPt, FontStyle.Regular);
            fontTiny = UiFont.Make(TypeTinyPt, FontStyle.Regular);
            fontSmall = UiFont.Make(TypeSmallPt, FontStyle.Regular);
            fontSmallBold = UiFont.Make(TypeSmallPt, FontStyle.Bold);
            fontCollapsedNum = MakeFont("Consolas", 17f, FontStyle.Bold);
            fontCollapsedLabel = UiFont.Make(TypeSmallPt, FontStyle.Regular);
            fontCollapsedText = UiFont.Make(13f, FontStyle.Bold);
            fontRemindBig = UiFont.Make(17f, FontStyle.Bold);
            fontRemindSmall = UiFont.Make(10f, FontStyle.Regular);
            fontRemindTitle = UiFont.Make(17f, FontStyle.Bold);
            fontRemindMeta = UiFont.Make(10f, FontStyle.Regular);
            fontRemindTime = UiFont.Make(12f, FontStyle.Bold);

            menu = BuildMenu();
            ContextMenuStrip = menu;

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 1000;
            tick.Tick += delegate
            {
                if (lastDay != DateTime.Today)
                {
                    lastDay = DateTime.Today;
                    snapshot = null;
                    RequestRefresh(false);
                    RequestCalendar(false);
                }
                Recompute();
                CheckOffWork();
                CheckSticky();
                CheckReminders();
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

            remindT = new System.Windows.Forms.Timer();
            remindT.Interval = 16;

            // 进度条上的流光要一直走：只在窗口可见、没在放提醒动画时按 18fps 重画
            flowT = new System.Windows.Forms.Timer();
            flowT.Interval = 55;
            flowT.Tick += delegate
            {
                if (!Visible || reminding || animating || expandAnim) return;
                if (settings.ui.reducedMotion || SystemInformation.HighContrast) return;
                Invalidate();
            };
            remindT.Tick += delegate { StepReminder(); };

            // 置顶看门狗：Windows 有时会把 WS_EX_TOPMOST 摘掉（换显示器 / Explorer 重启 /
            // 别的顶层窗口抢 z 序），每 2 秒看一眼，掉了就补回来。已经是置顶时不动 z 序，
            // 免得把设置窗、右键菜单压下去。
            topT = new System.Windows.Forms.Timer();
            topT.Interval = 2000;
            topT.Tick += delegate { KeepTopMost(); };

            ApplySettings();
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
            get { return S(CardHeightDip(ShowLunchRow)); }
        }

        private static Font MakeFont(string family, float size, FontStyle style)
        {
            try { return new Font(family, size, style, GraphicsUnit.Point); }
            catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
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
            tick.Start();
            flowT.Start();
            topT.Start();
            KeepTopMost();
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
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            tick.Stop(); poll.Stop(); autoCollapse.Stop(); animT.Stop(); flowT.Stop(); topT.Stop(); calPoll.Stop(); remindT.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reminderTip.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                foreach (var timer in new System.Windows.Forms.Timer[] { tick, poll, autoCollapse, animT, calPoll, remindT, flowT, topT })
                    if (timer != null) timer.Dispose();
                foreach (var font in new Font[] { fontBig, fontBigText, fontStatus, fontTiny, fontSmall,
                    fontCollapsedNum, fontCollapsedLabel, fontCollapsedText, fontRemindBig, fontRemindSmall,
                    fontRemindTitle, fontRemindMeta, fontRemindTime }) if (font != null) font.Dispose();
                sfCenter.Dispose(); sfNear.Dispose(); sfFar.Dispose();
                DisposeScreenFonts();
                if (screenForm != null) { screenForm.CloseNow(); screenForm = null; }
                if (screenCardUnder != null) { screenCardUnder.Dispose(); screenCardUnder = null; }
                if (frame != null) { frame.Dispose(); frame = null; }
                if (menu != null) menu.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------------- 状态 ----------------

        private void Recompute()
        {
            theme = Themes.Get(settings.ui.theme);
            // 窗口底色必须和卡片一致：圆角抗锯齿边缘会与底色混合，浅灰底会出现一圈白边
            if (BackColor != theme.bgBottom) BackColor = theme.bgBottom;
            status = StatusBuilder.Build(settings, snapshot, DateTime.Now, theme);
            UpdateTickRate();
            Invalidate();
        }

        private void RequestRefresh(bool manual)
        {
            if (!settings.lark.enabled)
            {
                snapshot = null;
                Recompute();
                return;
            }
            if (fetching) return;
            fetching = true;
            Invalidate();
            var cfg = settings.lark;
            ThreadPool.QueueUserWorkItem(delegate (object state)
            {
                AttendanceSnapshot result;
                try { result = lark.ReadToday(cfg); }
                catch (Exception ex)
                {
                    result = new AttendanceSnapshot();
                    result.error = ex.Message;
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        fetching = false;
                        snapshot = result;
                        lastFetchFailed = !result.ok;
                        if (!result.ok) store.Log("飞书读取失败: " + result.error);
                        Recompute();
                        if (manual) ShowBalloon(result.ok
                            ? "已同步：" + DescribeSnapshot(result)
                            : "同步失败：" + LarkClient.Shorten(result.error, 90));
                    });
                }
                catch { fetching = false; }
            });
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
            if (!settings.calendarEnabled || !settings.lark.enabled)
            {
                larkEvents = new List<ReminderEvent>();
                larkEventError = null;
                return;
            }
            if (calFetching) return;
            calFetching = true;
            var cfg = settings.lark;
            ThreadPool.QueueUserWorkItem(delegate (object state)
            {
                List<ReminderEvent> list = null;
                string error = null;
                try { list = lark.ReadCalendar(cfg, DateTime.Today, DateTime.Today.AddDays(2), out error); }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        calFetching = false;
                        if (list != null) larkEvents = list;
                        string previous = larkEventError;
                        larkEventError = error;
                        if (!string.IsNullOrEmpty(error) && error != previous)
                            store.Log("飞书日程读取失败: " + error);
                        if (manual)
                        {
                            if (error != null) ShowBalloon("日程同步失败：" + LarkClient.Shorten(error, 90));
                            else if (larkEvents.Count == 0) ShowBalloon("飞书日程：这两天没有日程");
                            else ShowBalloon("飞书日程：" + larkEvents.Count + " 个 · " + NextEventText());
                        }
                        Invalidate();
                    });
                }
                catch { calFetching = false; }
            });
        }

        private string NextEventText()
        {
            var all = Reminders.Merge(settings, larkEvents);
            var next = Reminders.Next(settings, all, DateTime.Now);
            if (next == null) return "没有待提醒的日程";
            DateTime start = Reminders.ParseStart(next);
            return start.ToString("HH:mm") + " " + next.title;
        }

        /// <summary>每秒检查一次：到点（开始时间 - 提前量）就放一次提醒动画。</summary>
        private void CheckReminders()
        {
            DateTime now = DateTime.Now;
            if (CheckBreaks(now)) return;
            var all = Reminders.Merge(settings, larkEvents);
            if (all.Count == 0) return;
            if (notified.Count > 300) notified.Clear();
            foreach (var e in all)
            {
                DateTime start = Reminders.ParseStart(e);
                string key = (e.id ?? e.title) + "@" + start.Ticks;
                if (notified.Contains(key)) continue;
                if (Reminders.IsDue(settings, e, now))
                {
                    notified.Add(key);
                    StartReminder(e);
                    return;
                }
            }
        }

        /// <summary>
        /// 到了下班时间还没打卡：常驻「注意 / 该下班啦」，每 5 分钟重播一次动画，
        /// 飞书同步到下班打卡（或跨天）就自动收掉。
        /// </summary>
        private void CheckOffWork()
        {
            DateTime now = DateTime.Now;
            var offSched = settings.ScheduleOf("offwork");
            bool offOk = offSched == null || (offSched.enabled && offSched.notify && offSched.OccursOn(now));
            bool need = status != null && status.offAlert && offOk && offAlertMutedDay != DateTime.Today;
            if (!need)
            {
                if (!offAlertOn) return;
                offAlertOn = false;
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
            if (!offAlertOn)
            {
                offAlertOn = true;
                offAlertNext = now.AddMilliseconds(OffAlertRepeatMs);
                StartOffAlert();
                return;
            }
            // 中间插进来的会议/日程播完后，回到常驻提醒
            if (!reminding && (remindEvent == null || remindEvent.source != "alert"))
            {
                StartOffAlert();
                return;
            }
            if (now >= offAlertNext)
            {
                offAlertNext = now.AddMilliseconds(OffAlertRepeatMs);
                remindEvent = BuildOffAlertEvent(now);
                remindAt = now;                 // 原地重播入场：色块不动，文字重淡入 + 扫光
                reminding = true;
                autoCollapse.Stop();
                remindT.Interval = 33; remindT.Start();
                Invalidate();
                store.Log("该下班啦: 重新提醒 @ " + now.ToString("HH:mm"));
            }
        }

        private ReminderEvent BuildOffAlertEvent(DateTime now)
        {
            var e = new ReminderEvent();
            e.id = "alert:offwork";
            e.source = "alert";
            e.title = "该下班啦";
            e.detail = "在飞书发「2」打卡后自动消失";
            e.place = "";
            string target = status != null ? status.targetIso : null;
            DateTime t;
            if (!string.IsNullOrEmpty(target) && DateTime.TryParse(target, out t)) e.start = Reminders.FormatStart(t);
            else e.start = Reminders.FormatStart(now);
            return e;
        }

        /// <summary>右键「忽略今天的下班提醒」：今天不再弹，第二天自动恢复。</summary>
        private void MuteOffAlert()
        {
            offAlertMutedDay = DateTime.Today;
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

        private void StartOffAlert()
        {
            if (animating) { animT.Stop(); animating = false; SetBounds(animToX, Top, animToW, animToH); collapsed = animToW == S(DesignCollapsedW); ApplyRegion(); }
            remindEvent = BuildOffAlertEvent(DateTime.Now);
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

        /// <summary>固定日程（午休 / 晚休 / 自己加的）：到点用提醒动画提示，每天各一次。</summary>
        private bool CheckBreaks(DateTime now)
        {
            settings.NormalizeSchedules();       // 兜底：手改的 Settings 也保证有三条固定日程
            if (settings.schedules == null) return false;
            if (!Policy.Build(settings, now).isWorkday) return false;
            foreach (var f in settings.schedules)
            {
                if (f == null || !f.enabled || !f.notify || f.IsOffWork) continue;
                if (!f.OccursOn(now)) continue;
                if (TryBreak(f, now, false)) return true;
            }
            foreach (var f in settings.schedules)
            {
                if (f == null || !f.enabled || !f.notify || f.IsOffWork) continue;
                if (!f.OccursOn(now)) continue;
                if (TryBreak(f, now, true)) return true;
            }
            // 中途才打开/重启：正处在某个固定日程里，就直接立起常驻卡片（每天仍然只一次）
            foreach (var f in settings.schedules)
            {
                if (f == null || !f.enabled || !f.notify || f.IsOffWork) continue;
                if (!f.OccursOn(now)) continue;
                if (ResumeBreak(f, now)) return true;
            }
            return false;
        }

        /// <summary>已经处在时段中间（比如刚开机）：直接把「xx中」的常驻提醒立起来。</summary>
        private bool ResumeBreak(FixedSchedule f, DateTime now)
        {
            DateTime start = f.StartAt(now), end = f.EndAt(now);
            if (start == DateTime.MinValue || end == DateTime.MinValue) return false;
            if (end <= start) return false;
            if (now < start.AddSeconds(50) || now >= end) return false;    // 正点那一下交给 TryBreak
            if (!notified.Add("fix:" + f.id + "@" + now.ToString("yyyyMMdd"))) return false;
            var e = new ReminderEvent();
            e.id = "fix:" + f.id;
            e.title = f.name + "中";
            e.detail = f.role == "lunch" ? "去吃饭，先歇一会儿" : f.role == "evening" ? "吃个晚饭再继续" : "现在是在这段时间里";
            e.place = "";
            e.source = "break";
            e.start = Reminders.FormatStart(start);
            e.end = Reminders.FormatStart(end);
            StartReminder(e);
            return true;
        }

        /// <summary>开始提醒（ending=false）或结束提醒（ending=true）。</summary>
        private bool TryBreak(FixedSchedule f, DateTime now, bool ending)
        {
            if (ending && (!f.endNotify || string.IsNullOrWhiteSpace(f.end))) return false;
            DateTime at = ending ? f.EndAt(now) : f.StartAt(now);
            if (at == DateTime.MinValue) return false;
            if (now.Hour * 60 + now.Minute != at.Hour * 60 + at.Minute) return false;
            string key = "fix:" + f.id + (ending ? ":end" : "") + "@" + now.ToString("yyyyMMdd");
            if (!notified.Add(key)) return false;
            var e = new ReminderEvent();
            e.id = "fix:" + f.id + (ending ? ":end" : "");
            e.title = ending ? f.name + "结束" : (f.IsOffWork ? "下班时间到" : f.name + "时间到");
            e.detail = ending ? "该回来继续上班了"
                : f.role == "lunch" ? "去吃饭，先歇一会儿" : f.role == "evening" ? "吃个晚饭再继续" : "到点了";
            e.place = "";
            e.source = "break";
            e.start = Reminders.FormatStart(at);
            if (ending) e.end = "";
            else if (!string.IsNullOrWhiteSpace(f.end)) e.end = Reminders.FormatStart(f.EndAt(now));
            StartReminder(e);
            return true;
        }

        private void StartReminder(ReminderEvent e)
        {
            if (screenReminder) ExitScreenMode();
            if (animating) { animT.Stop(); animating = false; SetBounds(animToX, Top, animToW, animToH); collapsed = animToW == S(DesignCollapsedW); ApplyRegion(); }
            remindEvent = e;
            remindUntil = ComputeRemindUntil(e, DateTime.Now);
            remindExiting = false;
            AccessibleDescription = e.title + " · " + Reminders.TimeRange(e) + " · " + e.place + " · " + e.detail +
                "（常驻提醒，右上角可关闭）";
            reminderTip.SetToolTip(this, "最近提醒\r\n" + e.title + "\r\n" + Reminders.TimeRange(e) + " · " + e.place + "\r\n" + e.detail);
            remindAt = DateTime.Now;
            reminding = true;
            autoCollapse.Stop();
            remindT.Interval = (settings.ui.reducedMotion || SystemInformation.HighContrast) ? 100 : 33; remindT.Start();
            if (UseScreenReminder(e)) ShowScreenReminder(e);
            Invalidate();
            store.Log("日程提醒: " + e.title + " @" + Reminders.ParseStart(e).ToString("yyyy-MM-dd HH:mm"));
            ShowBalloon("日程提醒 · " + e.title + "\r\n" + Reminders.ParseStart(e).ToString("HH:mm") +
                " 开始，" + Reminders.CountdownText(settings, e, DateTime.Now));
        }

        /// <summary>
        /// 提醒要常驻到什么时候：日程/会议到结束时间，休息到休息结束，下班到打卡。
        /// 至少停留「提醒停留」里设的秒数，免得时段已经过去还闪一下就消失。
        /// </summary>
        private DateTime ComputeRemindUntil(ReminderEvent e, DateTime now)
        {
            DateTime min = now.AddMilliseconds(RemindHoldMs);
            if (e == null) return min;
            if (e.source == "alert") return DateTime.MaxValue;
            if (e.id == "preview") return min;              // 右键预览：只放一遍动画，别常驻
            DateTime end = Reminders.ParseEnd(e);
            if (e.source == "break")
            {
                // 休息「结束」那一刻的提醒没有结束时间：按最短停留放一下就走
                if (end == DateTime.MinValue) return min;
                return end > min ? end : min;
            }
            if (end == DateTime.MinValue) end = now.AddMinutes(30);
            if (end <= now) return min;
            return end > min ? end : min;
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
            reminding = false;
            remindExiting = false;
            remindEvent = null;
            if (screenReminder) ExitScreenMode();
            ResetAutoCollapse();
            Invalidate();
        }

        private bool UseScreenReminder(ReminderEvent e)
        {
            return settings.ui.reminderSpread == "screen"
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
            if (remindEvent.source == "alert") return;      // 下班提醒由 CheckOffWork 管
            if (DateTime.Now < remindUntil) return;
            store.Log("提醒时段结束: 收起「" + remindEvent.title + "」");
            BeginExitReminder();
        }

        /// <summary>手动关闭当前提醒（卡片右上角的 × 或右键菜单）。</summary>
        private void DismissReminder()
        {
            if (!reminding || remindEvent == null) return;
            var e = remindEvent;
            notified.Add((e.id ?? e.title) + "@" + Reminders.ParseStart(e).Ticks);
            store.Log("手动关闭提醒: " + e.title);
            if (screenForm != null)
            {
                // 全屏时手动关闭：直接结束提醒，不要让卡片再留着
                EndReminder();
                return;
            }
            if (e.source == "alert")
            {
                offAlertMutedDay = DateTime.Today;     // 今天不再弹
                offAlertOn = false;
                EndReminder();
                return;
            }
            BeginExitReminder();
        }

        private void StepReminder()
        {
            if (remindEvent == null) { remindT.Stop(); return; }
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
            if (theme == null) theme = Themes.Get(settings.ui.theme);
            var fixedSched = FixedOf(remindEvent);
            string styleId = fixedSched != null && !string.IsNullOrWhiteSpace(fixedSched.styleId)
                ? fixedSched.styleId : ReminderStyles.Follow;
            ReminderStyles.Resolve(styleId, theme, out a, out b, out ink);
        }

        /// <summary>提醒卡片第一行的类型名（会议/日程/固定日程名/注意）。</summary>
        internal static string KindLabel(ReminderEvent e, bool meeting, string fixedName)
        {
            if (e != null && e.source == "alert") return "注意";
            if (e != null && e.source == "break")
                return (string.IsNullOrWhiteSpace(fixedName) ? "固定日程" : fixedName) + "提醒";
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
                tray.BalloonTipTitle = "下班倒计时";
                tray.BalloonTipText = text;
                tray.ShowBalloonTip(2500);
            }
            catch { }
        }

        private void ApplySettings()
        {
            frameAlpha = (byte)Math.Round(255 * Math.Max(0.3, Math.Min(1.0, settings.ui.opacity / 100.0)));
            KeepTopMost();
            int ms = Math.Max(1, settings.lark.pollMinutes) * 60 * 1000;
            poll.Interval = ms;
            poll.Stop();
            if (settings.lark.enabled) poll.Start();

            calPoll.Interval = Math.Max(3, settings.calendarPollMinutes) * 60 * 1000;
            calPoll.Stop();
            if (settings.calendarEnabled && settings.lark.enabled) calPoll.Start();
            if (tray != null)
            {
                try
                {
                    Icon old = tray.Icon;
                    tray.Icon = MakeTrayIcon();
                    if (old != null) old.Dispose();
                }
                catch { }
            }
            Recompute();
            if (!collapsed && !animating && Height != ExpandedH)
            {
                Size = new Size(S(DesignExpandedW), ExpandedH);
                ApplyRegion();
            }
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
            if (animating) FinishAnimNow();   // 动画还没走完也照样响应，先把它收尾
            animating = true;
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
            // 看门狗：动画超时还没结束就直接收尾，免得一直卡在「点击没反应」的状态
            if (animating && Environment.TickCount - animStart > AnimMs + 1200)
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
            double t = Math.Min(1.0, Math.Max(0.0, elapsed / (double)AnimMs));
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
            if (reminding) return;
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
            if (theme == null) theme = Themes.Get(settings.ui.theme);
            using (var path = RoundPath(new Rectangle(0, 0, Width, Height), S(collapsed ? 14 : 18)))
            {
                using (var brush = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), theme.bgTop, theme.bgBottom, 90f))
                    g.FillPath(brush, path);
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
                                using (var cardPath = RoundPath(new Rectangle(0, 0, Width, Height), S(collapsed ? 14 : 18)))
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
                    PaintCardReminder(g, ReminderClock());
                }
                // 提醒动画期间不描边：浅色主题下这一圈线会压在色块上显出一条亮边。
                if (!reminding)
                    using (var pen = new Pen(Color.FromArgb(24, theme.accent), Math.Max(1, scale))) g.DrawPath(pen, path);
            }
        }

        // ---------------- 卡片背景光效 ----------------

        private const double CardGlowStrength = 1.6;

        /// <summary>
        /// 卡片背景的三层色光：角落柔光 A、进度条同色光 B、内缘轮廓光 D。
        /// 设置里可单选或三种都开；强度固定 1.6×（和 HTML 预览的 1.6 对齐）。
        /// </summary>
        private void PaintCardGlow(Graphics g, GraphicsPath cardPath)
        {
            string mode = settings.ui.cardGlow;
            if (mode != "corner" && mode != "bar" && mode != "rim" && mode != "all") return;
            bool all = mode == "all";
            bool a = all || mode == "corner";
            bool b = all || mode == "bar";
            bool d = all || mode == "rim";
            double damp = all ? 0.55 : 1.0;
            Color accent = theme.accent;
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
            if (d)
            {
                DrawRimGlow(g, accent, 0.46 * CardGlowStrength * damp);
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

        private void DrawRimGlow(Graphics g, Color accent, double alpha)
        {
            alpha = Math.Max(0, Math.Min(1, alpha));
            if (alpha <= 0.003) return;
            int radius = S(collapsed ? 14 : 18);
            int step = Math.Max(1, S(2));
            for (int i = 0; i < 10; i++)
            {
                int inset = i * step;
                var r = new Rectangle(inset, inset, Width - inset * 2, Height - inset * 2);
                if (r.Width <= 6 || r.Height <= 6) break;
                using (var path = RoundPath(r, Math.Max(3, radius - inset / 3)))
                using (var pen = new Pen(Color.FromArgb((int)Math.Round(255 * alpha * (1 - i / 11.0)), accent), Math.Max(1, step)))
                    g.DrawPath(pen, path);
            }
            using (var path = RoundPath(new Rectangle(0, 0, Width, Height), radius))
            using (var pen = new Pen(Color.FromArgb((int)Math.Round(255 * Math.Min(0.9, alpha * 1.15)), accent), Math.Max(1, S(1))))
                g.DrawPath(pen, path);
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
            bool bar = settings.ui.reminderSpreadStyle == "bar";
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
                using (var cardPath = RoundPath(new Rectangle(0, 0, Width, Height), S(collapsed ? 14 : 18)))
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
                using (var cardPath = RoundPath(new Rectangle(0, 0, Width, Height), S(collapsed ? 14 : 18)))
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
                using (var cardPath = RoundPath(new Rectangle(0, 0, Width, Height), S(collapsed ? 14 : 18)))
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
            bool bar = settings.ui.reminderSpreadStyle == "bar";
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
                rectGear=new Rectangle(Width-S(90),iconY,iconSize,iconSize);
                rectRefresh=new Rectangle(Width-S(62),iconY,iconSize,iconSize);
                rectChevron=new Rectangle(Width-S(34),iconY,iconSize,iconSize);
                DrawGlyph(g,"gear",rectGear,Fade(dim, a));
                DrawGlyph(g,"refresh",rectRefresh,Fade(fetching?status.accent:dim, a));
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
                Line(g,status.big,fit,theme.textMain,new RectangleF(pad-S(2),y+dy,w+S(2),S(BigDip)),pFocal,false);
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
            using (var path = RoundPath(new Rectangle(x, y, width, height), height / 2))
            using (var brush = new SolidBrush(Fade(theme.track, barAlpha)))
                g.FillPath(brush, path);

            int filled = (int)(width * Math.Max(0, Math.Min(1, status.progress)));
            if (filled < height) return;
            var bar = new Rectangle(x, y, filled, height);
            bool hot = HotBar(MinutesToOffWork());

            // 外发光：两圈比条子大一点的同色圆角矩形，越靠外越淡，前缘像在发亮
            if (glowOn && filled > height * 2)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    int grow = (int)Math.Round(height * (pass == 0 ? 1.8f : 0.9f));
                    int feather = grow * 3;
                    int alpha = pass == 0 ? (hot ? 32 : 16) : (hot ? 70 : 32);
                    // 右边多留一截做羽毛状的尾巴，光才不会在前缘突然断掉
                    var halo = new Rectangle(x - grow, y - grow, filled + grow * 2 + feather, height + grow * 2);
                    using (var gp = RoundPath(halo, halo.Height / 2))
                    using (var br = new LinearGradientBrush(halo, Color.FromArgb((int)Math.Round((alpha) * barAlpha), status.accent),
                        Color.FromArgb((int)Math.Round((0) * barAlpha), status.accent), 0f))
                    {
                        var fblend = new ColorBlend();
                        float keep = Math.Min(1f, (float)(filled + grow) / halo.Width);
                        fblend.Colors = new Color[] { Color.FromArgb((int)Math.Round((alpha) * barAlpha), status.accent), Color.FromArgb((int)Math.Round((alpha) * barAlpha), status.accent), Color.FromArgb((int)Math.Round((0) * barAlpha), status.accent) };
                        fblend.Positions = new float[] { 0f, keep, 1f };
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
            Line(g,status.big,status.bigIsClock?fontCollapsedNum:fontCollapsedText,theme.textMain,
                new RectangleF(pad-S(1),S(27),w,S(27)),1,false);
            DrawProgress(g,pad,S(59),Width-pad*2,S(3),true);
            if(lastFetchFailed) Line(g,"!",fontStatus,Color.FromArgb(242,179,76),new RectangleF(Width-pad-S(14),S(32),S(14),S(20)),1,true);
        }

        private void DrawGlyph(Graphics g, string kind, Rectangle r, Color color)
        {
            float f = scale;
            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            using (var pen = new Pen(color, 1.5f * f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (kind == "gear")
                {
                    g.DrawEllipse(pen, cx - 4.4f * f, cy - 4.4f * f, 8.8f * f, 8.8f * f);
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * Math.PI / 3.0;
                        float x1 = cx + (float)Math.Cos(a) * 4.4f * f;
                        float y1 = cy + (float)Math.Sin(a) * 4.4f * f;
                        float x2 = cx + (float)Math.Cos(a) * 6.6f * f;
                        float y2 = cy + (float)Math.Sin(a) * 6.6f * f;
                        g.DrawLine(pen, x1, y1, x2, y2);
                    }
                    using (var b = new SolidBrush(color))
                        g.FillEllipse(b, cx - 1.7f * f, cy - 1.7f * f, 3.4f * f, 3.4f * f);
                }
                else if (kind == "refresh")
                {
                    g.DrawArc(pen, cx - 5f * f, cy - 4.6f * f, 10f * f, 9.2f * f, 35f, 275f);
                    using (var b = new SolidBrush(color))
                    {
                        var pts = new PointF[]
                        {
                            new PointF(cx + 4.3f * f, cy - 6.4f * f),
                            new PointF(cx + 6.6f * f, cy - 1.6f * f),
                            new PointF(cx + 2.0f * f, cy - 2.2f * f)
                        };
                        g.FillPolygon(b, pts);
                    }
                }
                else
                {
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(cx - 2.0f * f, cy - 4.2f * f),
                        new PointF(cx + 2.0f * f, cy),
                        new PointF(cx - 2.0f * f, cy + 4.2f * f)
                    });
                }
            }
        }

        // ---------------- 交互 ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (screenReminder) return;
            if (e.Button == MouseButtons.Left)
            {
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
            store.Save(settings);
        }

        public void OpenSettingsPublic()
        {
            OpenSettings();
        }

        private void OpenSettings()
        {
            autoCollapse.Stop();
            using (var dlg = new SettingsForm(store, settings))
            {
                dlg.TopMost = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    settings = store.Load();
                    ApplySettings();
                    RequestRefresh(false);
                    RequestCalendar(false);
                    var next = Reminders.Next(settings, Reminders.Merge(settings, larkEvents), DateTime.Now);
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

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            var miSettings = new ToolStripMenuItem("设置…");
            miSettings.Click += delegate { OpenSettings(); };
            var miRrefresh = new ToolStripMenuItem("立即同步飞书");
            miRrefresh.Click += delegate { RequestRefresh(true); };
            var miCal = new ToolStripMenuItem("刷新飞书日程");
            miCal.Click += delegate { RequestCalendar(true); };
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
            var miIn = new ToolStripMenuItem("在飞书发「1」上班打卡");
            miIn.Click += delegate { SendClock(true); };
            var miOut = new ToolStripMenuItem("在飞书发「2」下班打卡");
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
                store.Save(settings);
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
                bool hasChat = settings.lark.enabled && !string.IsNullOrWhiteSpace(settings.lark.chatId);
                miIn.Enabled = hasChat;
                miOut.Enabled = hasChat;
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
                dlg.FileName = "下班倒计时配置.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (store.Export(settings, dlg.FileName))
                    ShowBalloon("配置已导出\r\n" + dlg.FileName);
                else
                    ShowBalloon("导出失败，详情见 widget.log");
            }
        }

        private void ImportSettings()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "导入配置";
                dlg.Filter = "配置文件 (*.json)|*.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var loaded = store.Import(dlg.FileName);
                if (loaded == null)
                {
                    ShowBalloon("导入失败：文件不是有效的配置文件");
                    return;
                }
                settings = loaded;
                store.Save(settings);
                ApplySettings();
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
                    settings = store.Load();
                    ApplySettings();
                    RequestRefresh(false);
                    RequestCalendar(false);
                    Invalidate();
                }
            }
            ResetAutoCollapse();
        }

        private void SendClock(bool clockIn)
        {
            string text = clockIn ? "1" : "2";
            string label = clockIn ? "上班" : "下班";
            if (!settings.lark.enabled || string.IsNullOrWhiteSpace(settings.lark.chatId))
            {
                MessageBox.Show(this, "还没有配置飞书考勤会话，请先在设置里填写会话 ID。", "飞书打卡", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
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
            store.Save(settings);
            Recompute();
        }

        public void EnableTray()
        {
            try
            {
                tray = new NotifyIcon();
                tray.Icon = MakeTrayIcon();
                tray.Text = "下班倒计时";
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
