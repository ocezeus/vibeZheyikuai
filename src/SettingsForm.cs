using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.IO;
using System.Windows.Forms;

namespace WorkCountdown
{
    public class SettingsForm : Form
    {
        // 颜色全部放在 Ui（可变 static，由 ApplySkin() 按当前皮肤刷新）；以前这里是 readonly 写死的浅色。
        private static readonly string[] RepeatIds = new string[] { "daily", "odd", "even", "every30", "hourly", "every2h" };
        private static readonly string[] RepeatBaseIds = new string[] { "daily", "odd", "even" };
        // 当前下拉对应的值（内置三条只给基础三项，自定义日程才给循环三项）
        private string[] repeatIdsNow = RepeatBaseIds;

        /// <summary>左栏导航宽（DIP）。</summary>
        private const int RailW = 148;
        /// <summary>内容区标题条高（DIP）。</summary>
        private const int HeadH = 44;
        /// <summary>底部按钮条高（DIP）。</summary>
        private const int FootH = 50;
        /// <summary>页面宽（DIP）。和重构前一样是 508：页面里的卡片坐标一个都没动。</summary>
        private const int PageW = 508;

        private readonly SettingsStore store;
        private readonly Settings original;   // 调用者当前有效配置；关闭时恢复全局主题静态状态
        private Settings s;                   // 本次编辑的深拷贝草稿
        private bool globalsRestored;
        private readonly int initialTab;

        // ---- 新骨架：左栏导航 + 内容区（TabControl 只用来装 7 个页，表头被裁掉）----
        private TabControl tabs;
        private Panel tabClip;              // 把 TabControl 的表头裁到可视区外
        private Panel contentHost;          // 右侧内容区，AutoScroll
        private SettingsRail rail;          // 左栏导航（自绘）
        private SettingsHead head;          // 内容区标题条（自绘，宣纸时画印章）
        private Panel footerBar;            // 底部按钮条
        private SkinPicker skinPicker;      // 外观页「界面风格」的 6 个块
        private ArcSwitch swArc;             // 外观页「弧光」开关
        private Label lblArcState;           // 弧光状态行（随背景风格 / 皮肤变）
        private Label lblArcLock;            // 弧光可用性标签
        private ArcSwatchStrip arcSwatches;  // 弧光配色：种子 → 四颗派生色
        private bool loadingArc;             // 载入控件的瞬间不要回写草稿
        private Label lblThemeLocked;       // 非默认皮肤时「主题配色」上的提示
        private Button btnNewTheme;         // ＋ 新建主题（非默认皮肤时置灰）
        private int curPage = 0;
        private int tabChrome;              // 表头 + 边框占的高度
        private int tabHeader;              // 表头高度（要裁掉这么多）
        private int tabBorder;              // 底部边框高度
        private int lastWheelTick;          // 同一条滚轮消息去重

        // ---- 考勤 ----
        private RadioButton rbFixed;
        private RadioButton rbFlex;
        private TextBox tbStart;
        private TextBox tbEnd;
        private NumericUpDown numFlexH;
        private NumericUpDown numFlexM;
        private CheckBox[] cbDays;
        private ListBox lstExceptions;
        private DateTimePicker dpException;
        private ComboBox cmbExceptionKind;

        // ---- 固定日程 ----
        private ScheduleList listSched;
        private TextBox tbSchedName;
        private TextBox tbSchedStart;
        private TextBox tbSchedEnd;
        private ComboBox cmbRepeat;
        private DateTimePicker dpUntil;
        private CheckBox cbForever;
        private CheckBox cbSchedOn;
        private CheckBox cbSchedNotify;
        private CheckBox cbSchedEndNotify;
        private CheckBox cbSchedDeduct;
        private ComboBox cmbSchedStyle;
        private Label lblSchedHint;
        private int schedIndex = -1;
        private bool loadingSched;

        // ---- 提醒 ----
        private CheckBox cbLark;
        private CheckBox cbCalendar;
        private TextBox tbChatId;
        private TextBox tbBotName;
        private NumericUpDown numPoll;
        private NumericUpDown numLead;
        private NumericUpDown numLeadEvent;
        private Label lblTest;
        private EventCardList cardEvents;
        private TextBox tbTitle;
        private TextBox tbDetail;
        private TextBox tbPlace;
        private DateTimePicker dpEvent;
        private ComboBox cmbEventTime;
        private ComboBox cmbEventDur;
        private Label lblEventEnd;

        // ---- 日程（多份日历订阅 + 四类通知样式） ----
        private CheckedListBox lstCalendars;
        private Label lblCalCount;
        private Label lblCalendarState;
        private NumericUpDown numOutlookLead;
        private Button btnCalLogin;
        private List<CalendarSource> editCals = new List<CalendarSource>();
        private bool loadingCals;
        private ComboBox[] cmbSpread;      // 四类日程的「提醒范围」
        private ComboBox[] cmbShape;       // 「扩散样式」
        private ComboBox[] cmbColor;       // 「配色」
        private static readonly string[] StyleKinds = new string[]
            { Reminders.KindFixed, Reminders.KindLark, Reminders.KindLocal, Reminders.KindOutlook };

        // ---- 工资 ----
        private NumericUpDown numSalary;
        private NumericUpDown numPayday;
        private Label lblSalaryPreview;

        // ---- 小纸条 ----
        private RadioButton rbNoteDrawer;
        private RadioButton rbNoteColumns;
        private TextBox tbNoteRelay;
        private NumericUpDown numNotePort;
        private Label lblNoteRelayState;
        private Label lblNoteId;
        private TextBox tbNoteNick;
        private ListBox lstNoteContacts;
        private string editNoteId = "";
        private List<NoteContact> editNoteContacts = new List<NoteContact>();

        // ---- 字体 ----
        private string editTextFont = "";
        private string editNumberFont = "Consolas";

        // ---- 外观 ----
        private ThemePicker picker;
        private Label lblThemeName;
        private NumericUpDown numOpacity;
        private NumericUpDown numAutoCollapse;
        private NumericUpDown numReminderHold;
        private CheckBox cbTopMost;
        private CheckBox cbAutoStart;
        private CheckBox cbShowLunch;
        private CheckBox cbReducedMotion;
        private ComboBox cmbGlow;
        private Button btnEditTheme;
        private RadioButton rbBgSolid;
        private RadioButton rbBgGlass;

        public SettingsForm(SettingsStore store, Settings settings)
            : this(store, settings, 0)
        {
        }

        /// <summary>initialTab：0 考勤 / 1 固定日程 / 2 提醒 / 3 外观 / 4 日程 / 5 工资；胶囊菜单的「详细编辑」直接落到对应页。</summary>
        public SettingsForm(SettingsStore store, Settings settings, int initialTab)
        {
            this.store = store;
            this.original = settings == null ? new Settings() : settings;
            // 设置窗口改的是一份深拷贝草稿：预览（界面风格 / 自建主题）只作用于草稿，
            // 取消或保存失败都不会把未提交的修改漏进调用者的运行配置。
            this.s = settings == null ? new Settings() : settings.Clone();
            if (this.s == null) this.s = new Settings();
            this.initialTab = initialTab;
            UiFont.Configure(this.s.ui.textFont, this.s.ui.numberFont);
            this.s.NormalizeSchedules();          // 直接 new 出来的 Settings 也要有固定日程
            this.s.NormalizeCustomThemes();

            // 先按当前皮肤定色：下面创建控件时拿到的就是对的颜色（默认皮肤 = 原来的浅色）
            Ui.Apply(this.s.ui);

            Text = "上班工具箱 " + AppInfo.Version + " · 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96f, 96f);   // 按 96 DPI 设计，运行时整体放大
            AutoScaleMode = AutoScaleMode.None;          // 建好后自己按 DPI 放大，字体和控件一起走
            // 148 左栏 + 542 内容区（508 页面 + 右侧滚动条余量）；高度 640 不超过用户 150% 时的屏幕
            ClientSize = new Size(RailW + 542, 640);
            BackColor = Ui.WindowBg;
            Font = UiFont.Make(9f, FontStyle.Regular);
            DoubleBuffered = true;

            BuildChrome();
            BuildTabs();
            tabs.SelectedIndexChanged += delegate { OnPageChanged(); };
            BuildFooter();
            LoadValues();
            SelectPage(initialTab > 0 && initialTab < tabs.TabPages.Count ? initialTab : 0);
            ApplyDpi();
            OnPageChanged();          // DPI 放大后重新量一遍页面高度
            ApplySkin();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OnPageChanged();          // 句柄建好后 TabControl 的表头高度才量得准
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            RestoreGlobalPreviewState();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) RestoreGlobalPreviewState();
            base.Dispose(disposing);
        }

        /// <summary>
        /// 草稿里的预览可能改过全局主题表 / 设置窗口配色；取消 / 失败关闭时恢复调用者的状态。
        /// 保存成功时 Widget 会重新加载落盘配置并再设置一次，不会被这里覆盖。
        /// </summary>
        private void RestoreGlobalPreviewState()
        {
            if (globalsRestored) return;
            globalsRestored = true;
            Themes.SetCustom(original.customThemes);
            Ui.Apply(original.ui);
        }

        /// <summary>按系统 DPI 整体放大：字体按点数渲染，控件按比例放大才对得上。</summary>
        private void ApplyDpi()
        {
            float k = ScreenScale();
            if (Math.Abs(k - 1f) < 0.01f) return;
            SuspendLayout();
            Scale(new SizeF(k, k));
            ResumeLayout(true);
        }

        private static float ScreenScale()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        // ---------------- 骨架：左栏导航 + 内容区 + 底部条 ----------------

        private void BuildChrome()
        {
            int w = ClientSize.Width, h = ClientSize.Height;

            rail = new SettingsRail();
            rail.SetBounds(0, 0, RailW, h);
            rail.PageChanged += delegate(int index) { SelectPage(index); };
            Controls.Add(rail);

            head = new SettingsHead();
            head.SetBounds(RailW, 0, w - RailW, HeadH);
            Controls.Add(head);

            contentHost = new Panel();
            contentHost.SetBounds(RailW, HeadH, w - RailW, h - HeadH - FootH);
            contentHost.BackColor = Ui.WindowBg;
            contentHost.AutoScroll = true;
            contentHost.MouseWheel += ScrollContentWheel;
            Controls.Add(contentHost);

            // TabControl 的表头不用：把整个控件往上挪一个表头的高度，再用 tabClip 裁掉。
            // 页面本身还是 508 宽，和重构前一样，页面里控件的坐标一个都没动。
            tabClip = new Panel();
            tabClip.SetBounds(8, 0, PageW, contentHost.Height);
            tabClip.BackColor = Ui.WindowBg;
            tabClip.MouseWheel += ScrollContentWheel;
            contentHost.Controls.Add(tabClip);

            tabs = new TabControl();
            tabs.SetBounds(0, 0, PageW + 4, contentHost.Height);
            tabs.Appearance = TabAppearance.FlatButtons;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(0, 1);
            tabs.Multiline = false;
            tabs.BackColor = Ui.WindowBg;
            tabClip.Controls.Add(tabs);

            footerBar = new Panel();
            footerBar.SetBounds(RailW, h - FootH, w - RailW, FootH);
            footerBar.BackColor = Ui.FooterBg;
            footerBar.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var p = new Pen(Ui.WindowEdge, 1f))
                    e.Graphics.DrawLine(p, 0, 0, footerBar.Width, 0);
            };
            Controls.Add(footerBar);
        }

        private void BuildTabs()
        {
            var p1 = new TabPage(SettingsHead.Titles[0]);
            var p2 = new TabPage(SettingsHead.Titles[1]);
            var p3 = new TabPage(SettingsHead.Titles[2]);
            var p4 = new TabPage(SettingsHead.Titles[3]);
            var p5 = new TabPage(SettingsHead.Titles[4]);
            var p6 = new TabPage(SettingsHead.Titles[5]);
            var p7 = new TabPage(SettingsHead.Titles[6]);
            foreach (var p in new TabPage[] { p1, p2, p3, p4, p5, p6, p7 })
            {
                p.BackColor = Ui.WindowBg;
                p.Padding = new Padding(0);
                p.AutoScroll = false;          // 滚动交给外面的 contentHost，页面只负责高度
                p.MouseWheel += ScrollContentWheel;
            }
            tabs.TabPages.Add(p1);
            tabs.TabPages.Add(p2);
            tabs.TabPages.Add(p3);
            tabs.TabPages.Add(p4);
            tabs.TabPages.Add(p5);
            tabs.TabPages.Add(p6);
            tabs.TabPages.Add(p7);

            BuildPolicy(p1);
            BuildDays(p1);
            BuildExceptions(p1);

            BuildSchedules(p2);

            BuildReminders(p3);

            BuildAgenda(p5);

            BuildSalary(p6);

            BuildNotes(p7);

            BuildThemes(p4);
            BuildArcCard(p4);
            BuildLook(p4);
            BuildBackup(p4);
        }

        /// <summary>内容区滚轮：控件的滚轮消息会往上冒，统一在这里滚 contentHost（外观页/纸条页比可视高度长）。</summary>
        private void ScrollContentWheel(object sender, MouseEventArgs e)
        {
            if (contentHost == null) return;
            int range = contentHost.VerticalScroll.Maximum - contentHost.ClientSize.Height;
            if (range <= 0) return;
            int now = Environment.TickCount;
            if (now - lastWheelTick < 25) return;      // 同一条滚轮消息不要滚两次
            lastWheelTick = now;
            int step = (int)Math.Round(48 * ScreenScale());
            int y = -contentHost.AutoScrollPosition.Y - (e.Delta / 120) * step;
            if (y < 0) y = 0;
            if (y > range) y = range;
            contentHost.AutoScrollPosition = new Point(0, y);
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;    // 别让 AutoScroll 再滚一次
        }

        /// <summary>切到第 index 页（左栏、标题、页面高度一起走）。</summary>
        private void SelectPage(int index)
        {
            if (tabs == null || tabs.TabPages.Count == 0) return;
            if (index < 0 || index >= tabs.TabPages.Count) index = 0;
            if (tabs.SelectedIndex != index) tabs.SelectedIndex = index;
            else OnPageChanged();
        }

        /// <summary>页码变化：同步左栏高亮、标题条、页面高度和滚动位置。</summary>
        private void OnPageChanged()
        {
            if (tabs == null) return;
            int i = tabs.SelectedIndex;
            if (i < 0) i = 0;
            curPage = i;
            if (rail != null) rail.SelectedIndex = i;
            if (head != null) head.PageIndex = i;
            ApplyPageHeight();
            if (contentHost != null) contentHost.AutoScrollPosition = new Point(0, 0);
            if (rail != null) rail.Invalidate();
            if (head != null) head.Invalidate();
        }

        /// <summary>当前页内容高度：最后一张卡片的下边 + 14 DIP 留白（跟着 DPI 一起放大）。</summary>
        private int MeasurePage(int index)
        {
            int bottom = 0;
            if (tabs != null && index >= 0 && index < tabs.TabPages.Count)
                foreach (Control c in tabs.TabPages[index].Controls)
                    if (c.Bottom > bottom) bottom = c.Bottom;
            if (bottom <= 0) bottom = 520;
            return bottom + (int)Math.Round(14 * ScreenScale());
        }

        /// <summary>把当前页撑到内容高度；表头裁到 tabClip 外面，内容超出可视高度就出滚动条。</summary>
        private void ApplyPageHeight()
        {
            if (tabs == null || tabClip == null) return;
            if (!tabs.IsHandleCreated) return;      // 表头几何要等句柄建好才量得准（OnShown 里补一次）
            int h = MeasurePage(curPage);

            Rectangle dr = tabs.DisplayRectangle;
            tabHeader = Math.Max(0, dr.Y);
            tabBorder = Math.Max(0, tabs.Height - dr.Y - dr.Height);
            tabChrome = tabHeader + tabBorder;

            tabs.Height = h + tabChrome;
            dr = tabs.DisplayRectangle;
            tabHeader = Math.Max(0, dr.Y);
            tabBorder = Math.Max(0, tabs.Height - dr.Y - dr.Height);
            tabChrome = tabHeader + tabBorder;

            tabs.Top = -tabHeader;
            tabs.Left = -dr.X;
            tabClip.Height = h + tabBorder;
            float scale = ScreenScale();
            tabClip.Left = (int)Math.Round(8 * scale);
            tabClip.Width = (int)Math.Round(PageW * scale);
            if (contentHost != null) contentHost.PerformLayout();
        }

        // ---------------- 皮肤 ----------------

        /// <summary>
        /// 换肤：所有颜色重新从当前皮肤的 SkinDef / ThemeDef 取一遍，然后整棵控件树刷色重画。
        /// 默认皮肤走的是和 v3.6 一样的那套浅色，观感不变。
        /// </summary>
        private void ApplySkin()
        {
            Ui.Apply(s.ui);

            BackColor = Ui.WindowBg;
            if (contentHost != null) contentHost.BackColor = Ui.WindowBg;
            if (tabClip != null) tabClip.BackColor = Ui.WindowBg;
            if (tabs != null)
            {
                tabs.BackColor = Ui.WindowBg;
                foreach (TabPage p in tabs.TabPages) p.BackColor = Ui.WindowBg;
            }
            if (footerBar != null) { footerBar.BackColor = Ui.FooterBg; footerBar.Invalidate(); }
            if (picker != null) { picker.BackColor = Ui.Surface; picker.Invalidate(); }
            if (skinPicker != null) { skinPicker.BackColor = Ui.Surface; skinPicker.Invalidate(); }
            if (head != null) { head.Invalidate(); }
            if (rail != null)
            {
                rail.FootLines = RailFootLines();
                rail.Invalidate();
            }
            SkinTree(Controls);
            UpdateSkinUi();
            Invalidate(true);
        }

        private string[] RailFootLines()
        {
            string start = string.IsNullOrWhiteSpace(s.startTime) ? "09:00" : s.startTime;
            string end = string.IsNullOrWhiteSpace(s.endTime) ? "18:00" : s.endTime;
            return new string[] { store.Dir, "上班 " + start + " · 下班 " + end };
        }

        /// <summary>一次遍历控件树，把 Label / CheckBox / RadioButton / Button / 输入框的颜色刷成皮肤色。</summary>
        private void SkinTree(Control.ControlCollection controls)
        {
            foreach (Control c in controls)
            {
                // 滚轮：谁有焦点谁收消息，所以每个控件都挂一份（列表/下拉自己吃滚轮，不挂）
                if (c != contentHost && !OwnsWheel(c))
                {
                    c.MouseWheel -= ScrollContentWheel;
                    c.MouseWheel += ScrollContentWheel;
                }
                var card = c as CardPanel;
                if (card != null)
                {
                    card.BackColor = Color.Transparent;
                    card.Invalidate();
                }
                else
                {
                    var lbl = c as Label;
                    if (lbl != null)
                    {
                        lbl.ForeColor = (lbl.Tag as string) == "hint" ? Ui.Muted : Ui.Ink;
                        lbl.BackColor = Color.Transparent;
                    }
                    else if (c is CheckBox || c is RadioButton)
                    {
                        c.ForeColor = Ui.Ink;
                        c.BackColor = Color.Transparent;
                    }
                    else if (c is Button)
                    {
                        SkinButton((Button)c);
                    }
                    else if (c is TextBox || c is ListBox || c is CheckedListBox
                        || c is ComboBox || c is NumericUpDown)
                    {
                        c.BackColor = Ui.InputBg;
                        c.ForeColor = Ui.InputInk;
                    }
                    else if (c is TabPage || c is TabControl)
                    {
                        c.BackColor = Ui.WindowBg;
                    }
                    else if (c is ScheduleList || c is EventCardList || c is ThemePicker || c is SkinPicker)
                    {
                        c.BackColor = Ui.Surface;
                        c.Invalidate();
                    }
                }
                SkinTree(c.Controls);
            }
        }

        /// <summary>自己处理滚轮的控件（列表 / 下拉 / 数字框），不让它们再把整页滚一遍。</summary>
        private static bool OwnsWheel(Control c)
        {
            return c is ScheduleList || c is EventCardList
                || c is ComboBox || c is NumericUpDown || c is DateTimePicker
                || c is ListBox || c is CheckedListBox;
        }

        private static void SkinButton(Button b)
        {
            string tag = b.Tag as string;
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            if (tag == "primary")
            {
                b.BackColor = Ui.Accent;
                b.ForeColor = Ui.AccentInk;
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Skins.Mix(Ui.Accent, Color.White, 0.12);
                b.FlatAppearance.MouseDownBackColor = Skins.Mix(Ui.Accent, Color.Black, 0.16);
            }
            else if (tag == "accent" || tag == "accent-edit")
            {
                b.BackColor = Ui.Surface;
                b.ForeColor = tag == "accent-edit" && b.Enabled ? Ui.Ink : Ui.Accent;
                if (tag == "accent-edit" && !b.Enabled) b.ForeColor = Ui.Muted;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = tag == "accent-edit" && !b.Enabled ? Ui.Edge : Ui.Accent;
                b.FlatAppearance.MouseOverBackColor = Ui.Hover;
                b.FlatAppearance.MouseDownBackColor = Ui.Surface;
            }
            else
            {
                b.BackColor = Ui.Surface;
                b.ForeColor = Ui.Ink;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Ui.Edge;
                b.FlatAppearance.MouseOverBackColor = Ui.Hover;
                b.FlatAppearance.MouseDownBackColor = Ui.Surface;
            }
        }

        /// <summary>皮肤相关的联动：风格块选中态、主题色卡能不能点、提示文字。</summary>
        private void UpdateSkinUi()
        {
            var skin = Skins.Current(s.ui);
            bool def = skin.IsDefault;
            if (skinPicker != null) { skinPicker.SelectedId = skin.id; skinPicker.Invalidate(); }
            if (picker != null)
            {
                picker.Locked = !def;
                picker.Enabled = def;
                picker.Invalidate();
            }
            if (lblThemeLocked != null)
            {
                lblThemeLocked.Visible = !def;
                lblThemeLocked.ForeColor = Ui.Muted;
            }
            if (btnNewTheme != null)
            {
                btnNewTheme.Enabled = def;
                btnNewTheme.ForeColor = def ? Ui.Accent : Ui.Muted;
                btnNewTheme.FlatAppearance.BorderColor = def ? Ui.Accent : Ui.Edge;
            }
            UpdateThemeName();
            if (rail != null) rail.Invalidate();
            if (head != null) head.Invalidate();
        }

        private void BuildFooter()
        {
            var footer = new Label();
            footer.Text = "配置存在 " + store.Dir;
            footer.SetBounds(14, 17, footerBar.Width - 2 * 14 - 2 * 106 - 10 - 14, 18);
            footer.Font = UiFont.Make(8f, FontStyle.Regular);
            footer.ForeColor = Ui.Muted;
            footer.BackColor = Color.Transparent;
            footer.Tag = "hint";
            footer.AutoEllipsis = true;
            footerBar.Controls.Add(footer);

            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.SetBounds(footerBar.Width - 14 - 106 - 10 - 106, 8, 106, 34);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderColor = Ui.Edge;
            btnCancel.BackColor = Ui.Surface;
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            footerBar.Controls.Add(btnCancel);

            var btnOk = new Button();
            btnOk.Text = "保存";
            btnOk.Tag = "primary";
            btnOk.SetBounds(footerBar.Width - 14 - 106, 8, 106, 34);
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.BackColor = Ui.Accent;
            btnOk.ForeColor = Ui.AccentInk;
            btnOk.Click += delegate { Save(); };
            footerBar.Controls.Add(btnOk);

            CancelButton = btnCancel;
        }

        // ---------------- 考勤 ----------------

        private void BuildPolicy(TabPage page)
        {
var g = CardBox(page, "班次与工时", 8, 8, 492, 170);

            rbFixed = new RadioButton();
            rbFixed.Text = "固定班次";
            rbFixed.SetBounds(14, 26, 110, 22);
            rbFixed.CheckedChanged += delegate { UpdateFlexState(); };
            g.Controls.Add(rbFixed);

            rbFlex = new RadioButton();
            rbFlex.Text = "弹性工时";
            rbFlex.SetBounds(132, 26, 120, 22);
            rbFlex.CheckedChanged += delegate { UpdateFlexState(); };
            g.Controls.Add(rbFlex);

            g.Controls.Add(Lbl("上班", 14, 58, 40, 24));
            tbStart = Tb("09:00", 58, 58, 74);
            g.Controls.Add(tbStart);

            g.Controls.Add(Lbl("下班", 146, 58, 40, 24));
            tbEnd = Tb("18:00", 190, 58, 74);
            g.Controls.Add(tbEnd);

g.Controls.Add(Lbl("应勤", 276, 58, 44, 24));
numFlexH = Num(8, 320, 58, 48, 0, 16);
            g.Controls.Add(numFlexH);
g.Controls.Add(Lbl("小时", 372, 58, 42, 24));
numFlexM = Num(0, 414, 58, 46, 0, 59);
            g.Controls.Add(numFlexM);
g.Controls.Add(Lbl("分", 462, 58, 22, 24));

            g.Controls.Add(Hint("固定班次：到点下班。弹性工时：上班打卡 + 应勤时长 = 下班时间。", 14, 94, 466, 18));
            g.Controls.Add(Hint("时间格式 HH:mm。午休 / 晚休 / 下班的提醒都在「固定日程」页里配。", 14, 118, 466, 18));
        }

        private void UpdateFlexState()
        {
            if (numFlexH == null) return;
            bool flex = rbFlex != null && rbFlex.Checked;
            numFlexH.Enabled = flex;
            numFlexM.Enabled = flex;
            if (tbEnd != null) tbEnd.Enabled = !flex;
        }

        private void BuildDays(TabPage page)
        {
            var g = CardBox(page, "工作日", 8, 186, 492, 116);
            cbDays = new CheckBox[7];
            // 界面顺序是「周一到周日」，而 Settings.workdays 的下标是 0=周日 .. 6=周六（DayOfWeek），
            // 两边差一天。以前直接按下标读写，勾了周一到周五保存后周五变成休息日（v3.5 修）。
            string[] names = new string[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            for (int i = 0; i < 7; i++)
            {
                var cb = new CheckBox();
                cb.Text = names[i];
                cb.SetBounds(14 + i * 66, 40, 62, 22);
                g.Controls.Add(cb);
                cbDays[i] = cb;
            }
            g.Controls.Add(Hint("勾的是「固定班次」里要上班的日子；节假日 / 调休在下一条单独设。", 14, 66, 466, 18));
            var btnMonFri = FlatButton("周一~周五", 14, 88, 96, 24);
            btnMonFri.Click += delegate { SetWorkdayPreset(5); };
            g.Controls.Add(btnMonFri);
            var btnMonSat = FlatButton("周一~周六", 116, 88, 96, 24);
            btnMonSat.Click += delegate { SetWorkdayPreset(6); };
            g.Controls.Add(btnMonSat);
            var btnEvery = FlatButton("每天", 218, 88, 72, 24);
            btnEvery.Click += delegate { SetWorkdayPreset(7); };
            g.Controls.Add(btnEvery);
        }

        /// <summary>界面第 i 个复选框（0=周一 .. 6=周日）对应的 workdays 下标（0=周日 .. 6=周六）。</summary>
        internal static int WorkdayArrayIndex(int uiIndex)
        {
            return (uiIndex + 1) % 7;
        }

        /// <summary>快捷设置工作日：前 count 天（周一开头）上班，其余休息。</summary>
        private void SetWorkdayPreset(int count)
        {
            if (cbDays == null) return;
            for (int i = 0; i < 7; i++) cbDays[i].Checked = i < count;
        }

        private void BuildExceptions(TabPage page)
        {
            var g = CardBox(page, "节假日与调休", 8, 314, 492, 224);

            dpException = new DateTimePicker();
            dpException.Format = DateTimePickerFormat.Custom;
            dpException.CustomFormat = "yyyy-MM-dd";
            dpException.SetBounds(14, 26, 120, 24);
            g.Controls.Add(dpException);

            cmbExceptionKind = new ComboBox();
            cmbExceptionKind.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbExceptionKind.Items.Add("休息");
            cmbExceptionKind.Items.Add("调休上班");
            cmbExceptionKind.SelectedIndex = 0;
            cmbExceptionKind.SetBounds(144, 26, 108, 24);
            g.Controls.Add(cmbExceptionKind);

            var btnAdd = FlatButton("添加", 264, 24, 84, 26);
            btnAdd.Click += delegate { AddException(); };
            g.Controls.Add(btnAdd);

            var btnDel = FlatButton("删除", 356, 24, 84, 26);
            btnDel.Click += delegate { RemoveException(); };
            g.Controls.Add(btnDel);

            lstExceptions = new ListBox();
lstExceptions.SetBounds(14, 60, 466, 124);
            lstExceptions.IntegralHeight = false;
            lstExceptions.BorderStyle = BorderStyle.FixedSingle;
            g.Controls.Add(lstExceptions);

g.Controls.Add(Hint("添加后会覆盖「工作日」：休息日不提醒，调休日按工作日算。", 14, 192, 466, 18));
        }

        private void RefreshExceptions()
        {
            if (lstExceptions == null) return;
            lstExceptions.BeginUpdate();
            lstExceptions.Items.Clear();
            var sorted = new List<HolidayEntry>(s.exceptions);
            sorted.Sort(delegate(HolidayEntry a, HolidayEntry b) { return string.Compare(a.date, b.date, StringComparison.Ordinal); });
            foreach (var h in sorted)
                lstExceptions.Items.Add(h.date + "  ·  " + (h.kind == "work" ? "调休上班" : "休息"));
            lstExceptions.EndUpdate();
        }

        private void AddException()
        {
            string date = dpException.Value.ToString("yyyy-MM-dd");
            string kind = cmbExceptionKind.SelectedIndex == 1 ? "work" : "off";
            for (int i = s.exceptions.Count - 1; i >= 0; i--)
                if (s.exceptions[i].date == date) s.exceptions.RemoveAt(i);
            var entry = new HolidayEntry();
            entry.date = date;
            entry.kind = kind;
            s.exceptions.Add(entry);
            RefreshExceptions();
        }

        private void RemoveException()
        {
            int index = lstExceptions.SelectedIndex;
            if (index < 0) return;
            var sorted = new List<HolidayEntry>(s.exceptions);
            sorted.Sort(delegate(HolidayEntry a, HolidayEntry b) { return string.Compare(a.date, b.date, StringComparison.Ordinal); });
            if (index < sorted.Count) s.exceptions.Remove(sorted[index]);
            RefreshExceptions();
        }

        // ---------------- 固定日程 ----------------

        private void BuildSchedules(TabPage page)
        {
var g = CardBox(page, "固定日程", 8, 8, 492, 208);
            g.Controls.Add(Hint("午休 / 晚休 / 下班也在这张表里；点一条改一条，也可以再新建。", 14, 24, 466, 18));

            listSched = new ScheduleList();
listSched.SetBounds(14, 46, 466, 150);
            listSched.SelectedIndexChanged += delegate { SelectSched(listSched.SelectedIndex); };
            g.Controls.Add(listSched);

var e = CardBox(page, "这一条", 8, 224, 492, 214);

            e.Controls.Add(Lbl("名称", 14, 26, 34, 24));
            tbSchedName = Tb("", 52, 26, 132);
            tbSchedName.TextAlign = HorizontalAlignment.Left;
            e.Controls.Add(tbSchedName);

            e.Controls.Add(Lbl("开始", 196, 26, 34, 24));
            tbSchedStart = Tb("", 234, 26, 56);
            e.Controls.Add(tbSchedStart);
            e.Controls.Add(Lbl("至", 294, 26, 18, 24));
            tbSchedEnd = Tb("", 314, 26, 56);
            e.Controls.Add(tbSchedEnd);
            e.Controls.Add(Hint("留空 = 只响一次", 378, 26, 100, 24));

            e.Controls.Add(Lbl("重复", 14, 58, 34, 24));
            cmbRepeat = new ComboBox();
            cmbRepeat.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRepeat.SelectedIndexChanged += delegate { if (!loadingSched) UpdateSchedState(); };
            cmbRepeat.SetBounds(52, 58, 160, 24);
            cmbRepeat.DropDownWidth = 220;      // 循环文案较长，下拉里完整显示
            e.Controls.Add(cmbRepeat);

            e.Controls.Add(Lbl("重复至", 224, 58, 50, 24));
            dpUntil = new DateTimePicker();
            dpUntil.Format = DateTimePickerFormat.Custom;
            dpUntil.CustomFormat = "yyyy-MM-dd";
            dpUntil.SetBounds(276, 58, 108, 24);
            e.Controls.Add(dpUntil);
            cbForever = new CheckBox();
            cbForever.Text = "永久";
            cbForever.SetBounds(392, 58, 60, 22);
            cbForever.CheckedChanged += delegate { UpdateSchedState(); };
            e.Controls.Add(cbForever);

            e.Controls.Add(Lbl("配色", 14, 90, 34, 24));
            cmbSchedStyle = StyleCombo("theme", 52, 90, 152);
            e.Controls.Add(cmbSchedStyle);

            cbSchedOn = new CheckBox();
            cbSchedOn.Text = "启用";
            cbSchedOn.SetBounds(220, 90, 58, 22);
            cbSchedOn.CheckedChanged += delegate { UpdateSchedState(); };
            e.Controls.Add(cbSchedOn);

            cbSchedNotify = new CheckBox();
            cbSchedNotify.Text = "到点提醒";
            cbSchedNotify.SetBounds(282, 90, 90, 22);
            e.Controls.Add(cbSchedNotify);

            cbSchedEndNotify = new CheckBox();
            cbSchedEndNotify.Text = "结束时也提醒";
            cbSchedEndNotify.SetBounds(372, 90, 108, 22);
            e.Controls.Add(cbSchedEndNotify);

            cbSchedDeduct = new CheckBox();
            cbSchedDeduct.Text = "计入工时扣除";
            cbSchedDeduct.SetBounds(14, 122, 120, 22);
            e.Controls.Add(cbSchedDeduct);

            var btnAdd = AccentButton("新建一条", 296, 120, 84, 26);
            btnAdd.Click += delegate { AddSched(); };
            e.Controls.Add(btnAdd);

            var btnDel = FlatButton("删除这条", 386, 120, 84, 26);
            btnDel.Click += delegate { RemoveSched(); };
            e.Controls.Add(btnDel);

lblSchedHint = Hint("", 14, 156, 466, 44);
            e.Controls.Add(lblSchedHint);

var n = CardBox(page, "说明", 8, 446, 492, 66);
            n.Controls.Add(Hint("同一时刻只响一次；单/双周按 ISO 周算；「重复至」含当天。", 14, 22, 466, 18));
            n.Controls.Add(Hint("午休的「计入工时扣除」会影响下班时间，其它固定日程只负责提醒。", 14, 40, 466, 18));
        }

        private void RefreshSchedules()
        {
            if (listSched == null) return;
            if (s.schedules == null) s.schedules = new List<FixedSchedule>();
            listSched.Items = s.schedules;
            if (schedIndex < 0 && s.schedules.Count > 0) schedIndex = 0;
            if (schedIndex >= s.schedules.Count) schedIndex = s.schedules.Count - 1;
            listSched.SelectedIndex = schedIndex;
            LoadSchedEditor();
        }

        private void SelectSched(int index)
        {
            if (loadingSched) return;
            if (index < 0 || index >= s.schedules.Count) return;
            StoreSchedEditor();          // 先把上一条的改动写回去
            schedIndex = index;
            LoadSchedEditor();
        }

        private void LoadSchedEditor()
        {
            if (tbSchedName == null) return;
            loadingSched = true;
            var f = (schedIndex >= 0 && schedIndex < s.schedules.Count) ? s.schedules[schedIndex] : null;
            bool on = f != null;
            tbSchedName.Text = on ? f.name : "";
            tbSchedStart.Text = on ? f.start : "";
            tbSchedEnd.Text = on ? f.end : "";
            FillRepeatCombo(f);
            int repeatIndex = on ? Array.IndexOf(repeatIdsNow, f.repeat) : 0;
            cmbRepeat.SelectedIndex = repeatIndex < 0 ? 0 : repeatIndex;
            bool forever = !on || string.IsNullOrWhiteSpace(f.until);
            cbForever.Checked = forever;
            DateTime untilDate = DateTime.Today.AddMonths(3);
            DateTime parsed;
            if (!forever && DateTime.TryParse(f.until, out parsed)) untilDate = parsed;
            if (untilDate < dpUntil.MinDate) untilDate = DateTime.Today;
            dpUntil.Value = untilDate;
            cbSchedOn.Checked = on && f.enabled;
            cbSchedNotify.Checked = on && f.notify;
            cbSchedEndNotify.Checked = on && f.endNotify;
            cbSchedDeduct.Checked = on && f.deduct;
            string want = on ? f.styleId : "theme";
            for (int i = 0; i < ReminderStyles.Ids.Length; i++)
                if (ReminderStyles.Ids[i] == want) { cmbSchedStyle.SelectedIndex = i; break; }
            tbSchedName.Enabled = on;
            cbSchedDeduct.Enabled = on;
            loadingSched = false;
            UpdateSchedState();
        }

        /// <summary>内置三条只给每天/单双周；自定义日程才提供「上班期间循环」三项。</summary>
        private void FillRepeatCombo(FixedSchedule f)
        {
            repeatIdsNow = f != null && f.role == "custom" ? RepeatIds : RepeatBaseIds;
            cmbRepeat.BeginUpdate();
            try
            {
                cmbRepeat.Items.Clear();
                for (int i = 0; i < repeatIdsNow.Length; i++)
                    cmbRepeat.Items.Add(FixedSchedule.RepeatName(repeatIdsNow[i]));
            }
            finally { cmbRepeat.EndUpdate(); }
        }

        private string SelectedRepeatId()
        {
            if (cmbRepeat == null || repeatIdsNow == null || cmbRepeat.SelectedIndex < 0
                || cmbRepeat.SelectedIndex >= repeatIdsNow.Length) return "";
            return repeatIdsNow[cmbRepeat.SelectedIndex];
        }

        private void UpdateSchedState()
        {
            if (loadingSched) return;
            bool on = schedIndex >= 0 && schedIndex < s.schedules.Count;
            FixedSchedule f = on ? s.schedules[schedIndex] : null;
            bool off = f != null && f.IsOffWork;
            bool rec = FixedSchedule.RepeatMinutesOf(SelectedRepeatId()) > 0;
            if (dpUntil != null) dpUntil.Enabled = on && !cbForever.Checked;
            if (tbSchedStart != null) tbSchedStart.Enabled = on && !off && !rec;
            if (tbSchedEnd != null) tbSchedEnd.Enabled = on && !off && !rec;
            if (cbSchedEndNotify != null) cbSchedEndNotify.Enabled = on && !off && !rec;
            if (lblSchedHint != null)
            {
                if (!on) lblSchedHint.Text = "";
                else if (off) lblSchedHint.Text = "「下班」按班次算时间（今天 " + OffWorkText()
                    + "），不用填开始；重复规则决定哪天弹「该下班啦」。";
                else if (rec) lblSchedHint.Text = "只在工作日、上班期间按间隔提醒（第一次在上班后 "
                    + Policy.Hm(FixedSchedule.RepeatMinutesOf(SelectedRepeatId()))
                    + "）；开始/结束时间和结束提醒不参与。";
                else lblSchedHint.Text = "时间格式 HH:mm。结束留空就只响一次；有结束时间时会一直盖到结束。只在工作日触发。";
            }
        }

        private void StoreSchedEditor()
        {
            if (schedIndex < 0 || schedIndex >= s.schedules.Count) return;
            var f = s.schedules[schedIndex];
            f.name = tbSchedName.Text.Trim();
            if (f.name.Length == 0) f.name = "固定日程";
            if (!f.IsOffWork)
            {
                f.start = tbSchedStart.Text.Trim();
                f.end = tbSchedEnd.Text.Trim();
            }
            string repeatId = SelectedRepeatId();
            f.repeat = repeatId.Length > 0 ? repeatId : "daily";
            f.until = cbForever.Checked ? "" : dpUntil.Value.ToString("yyyy-MM-dd");
            f.enabled = cbSchedOn.Checked;
            f.notify = cbSchedNotify.Checked;
            f.endNotify = cbSchedEndNotify.Checked;
            f.deduct = cbSchedDeduct.Checked;
            f.styleId = ReminderStyles.Ids[Math.Max(0, cmbSchedStyle.SelectedIndex)];
            if (f.role == "lunch") s.lunchDeduct = f.deduct;
        }

        private string OffWorkText()
        {
            try
            {
                var plan = Policy.Build(s, DateTime.Now);
                return plan.TargetEnd(DateTime.Now).ToString("HH:mm");
            }
            catch { return "20:04"; }
        }

        private void AddSched()
        {
            StoreSchedEditor();
            var f = FixedSchedule.Make("custom-" + Guid.NewGuid().ToString("N").Substring(0, 6), "custom", "新的固定日程",
                SettingsExtensions.FormatTime(Math.Min(23 * 60 + 45, (DateTime.Now.Hour + 1) * 60)), "",
                ReminderStyles.Ids.Length > 1 ? ReminderStyles.Ids[ReminderStyles.Ids.Length - 1] : "theme");
            var list2 = new List<FixedSchedule>(s.schedules);
            list2.Add(f);          // 加在最后：内置三条不动，新的排在后面
            s.schedules = list2;
            schedIndex = list2.Count - 1;
            RefreshSchedules();
            tbSchedName.Focus();
            tbSchedName.SelectAll();
        }

        private void RemoveSched()
        {
            if (schedIndex < 0 || schedIndex >= s.schedules.Count) return;
            var f = s.schedules[schedIndex];
            if (f.role == "lunch" || f.role == "evening" || f.role == "offwork")
            {
                MessageBox.Show(this, "午休、晚休、下班这三条是内置的，只能改或停用，不能删。", "内置日程",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "删除「" + f.name + "」？", "确认删除", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
            var list2 = new List<FixedSchedule>(s.schedules);
            list2.RemoveAt(schedIndex);
            s.schedules = list2;
            if (schedIndex >= s.schedules.Count) schedIndex = s.schedules.Count - 1;
            RefreshSchedules();
        }

        // ---------------- 提醒 ----------------

        private void BuildReminders(TabPage page)
        {
            var g = CardBox(page, "飞书考勤对话", 8, 8, 492, 168);

            cbLark = new CheckBox();
            cbLark.Text = "读取飞书考勤对话中的上下班打卡";
            cbLark.SetBounds(14, 26, 320, 22);
            cbLark.CheckedChanged += delegate { UpdateLarkState(); };
            g.Controls.Add(cbLark);

            g.Controls.Add(Lbl("会话 ID", 14, 58, 60, 24));
            tbChatId = Tb("", 80, 58, 320);
            tbChatId.TextAlign = HorizontalAlignment.Left;
            g.Controls.Add(tbChatId);

            var btnDiscover = FlatButton("自动发现", 410, 56, 76, 28);
            btnDiscover.Click += delegate { DiscoverChat(); };
            g.Controls.Add(btnDiscover);

            g.Controls.Add(Lbl("机器人", 14, 92, 60, 24));
            tbBotName = Tb("", 80, 92, 180);
            tbBotName.TextAlign = HorizontalAlignment.Left;
            g.Controls.Add(tbBotName);

            g.Controls.Add(Lbl("刷新", 274, 92, 40, 24));
            numPoll = Num(5, 316, 92, 56, 1, 120);
            g.Controls.Add(numPoll);
            g.Controls.Add(Lbl("分钟", 378, 92, 40, 24));

            var btnTest = FlatButton("测试连接", 14, 124, 96, 28);
            btnTest.Click += delegate { TestLark(); };
            g.Controls.Add(btnTest);

            lblTest = Hint("点「测试连接」看看能不能读到今天的打卡。", 118, 124, 362, 40);
            g.Controls.Add(lblTest);

            var d = CardBox(page, "日程与提醒", 8, 184, 492, 352);

            cbCalendar = new CheckBox();
            cbCalendar.Text = "读取飞书日程";
            cbCalendar.SetBounds(14, 26, 128, 22);
            d.Controls.Add(cbCalendar);

            d.Controls.Add(Lbl("会议提前", 152, 26, 60, 24));
            numLead = Num(5, 216, 26, 50, 0, 120);
            d.Controls.Add(numLead);
            d.Controls.Add(Lbl("日程提前", 274, 26, 60, 24));
            numLeadEvent = Num(10, 338, 26, 50, 0, 120);
            d.Controls.Add(numLeadEvent);
            d.Controls.Add(Lbl("分钟", 394, 26, 40, 24));

            d.Controls.Add(Hint("列表里是真正会响的时刻，点小卡片展开看详情。", 14, 52, 466, 18));

            cardEvents = new EventCardList();
            cardEvents.SetBounds(14, 74, 232, 236);
            d.Controls.Add(cardEvents);

            d.Controls.Add(Lbl("标题", 256, 76, 34, 24));
            tbTitle = Tb("", 292, 74, 186);
            tbTitle.TextAlign = HorizontalAlignment.Left;
            d.Controls.Add(tbTitle);

            d.Controls.Add(Lbl("内容", 256, 106, 34, 24));
            tbDetail = new TextBox();
            tbDetail.Multiline = true;
            tbDetail.ScrollBars = ScrollBars.Vertical;
            tbDetail.SetBounds(292, 104, 186, 58);
            d.Controls.Add(tbDetail);

            dpEvent = new DateTimePicker();
            dpEvent.Format = DateTimePickerFormat.Custom;
            dpEvent.CustomFormat = "yyyy-MM-dd";
            dpEvent.SetBounds(256, 172, 120, 24);
            dpEvent.ValueChanged += delegate { UpdateEventEnd(); };
            d.Controls.Add(dpEvent);

            cmbEventTime = new ComboBox();
            cmbEventTime.DropDownStyle = ComboBoxStyle.DropDown;
            for (int h = 0; h < 24; h++)
            {
                cmbEventTime.Items.Add(h.ToString("00", CultureInfo.InvariantCulture) + ":00");
                cmbEventTime.Items.Add(h.ToString("00", CultureInfo.InvariantCulture) + ":30");
            }
            cmbEventTime.SetBounds(382, 172, 96, 24);
            cmbEventTime.TextChanged += delegate { UpdateEventEnd(); };
            DateTime seed = DateTime.Now.AddMinutes(30);
            cmbEventTime.Text = new DateTime(seed.Year, seed.Month, seed.Day, seed.Hour, seed.Minute < 30 ? 30 : 0, 0)
                .AddHours(seed.Minute < 30 ? 0 : 1).ToString("HH:mm", CultureInfo.InvariantCulture);
            d.Controls.Add(cmbEventTime);

            d.Controls.Add(Lbl("时长", 256, 202, 34, 24));
            cmbEventDur = new ComboBox();
            cmbEventDur.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbEventDur.Items.Add("15 分钟");
            cmbEventDur.Items.Add("30 分钟");
            cmbEventDur.Items.Add("45 分钟");
            cmbEventDur.Items.Add("60 分钟");
            cmbEventDur.Items.Add("90 分钟");
            cmbEventDur.Items.Add("120 分钟");
            cmbEventDur.Items.Add("180 分钟");
            cmbEventDur.SelectedIndex = 1;
            cmbEventDur.SetBounds(292, 202, 90, 24);
            cmbEventDur.SelectedIndexChanged += delegate { UpdateEventEnd(); };
            d.Controls.Add(cmbEventDur);

            lblEventEnd = Hint("结束 --:--", 388, 202, 92, 24);
            d.Controls.Add(lblEventEnd);

            d.Controls.Add(Lbl("位置", 256, 232, 34, 24));
            tbPlace = Tb("", 292, 230, 186);
            tbPlace.TextAlign = HorizontalAlignment.Left;
            d.Controls.Add(tbPlace);

            var btnAdd = AccentButton("添加日程", 256, 264, 108, 28);
            btnAdd.Click += delegate { AddEvent(); };
            d.Controls.Add(btnAdd);

            // 快捷选项：临时记一笔时点一下就把时间填好，不用去拨日期和时间
            d.Controls.Add(Hint("快速填入", 14, 322, 60, 22));
            {
                int[] quickMinutes = new int[] { 3, 5, 15, 30, 60 };
                string[] quickLabels = new string[] { "3 分钟后", "5 分钟后", "15 分钟后", "30 分钟后", "1 小时后" };
                int qx = 76;
                for (int qi = 0; qi < quickMinutes.Length; qi++)
                {
                    int minutes = quickMinutes[qi];
                    var chip = FlatButton(quickLabels[qi], qx, 320, 76, 26);
                    chip.Font = UiFont.Make(9f, FontStyle.Regular);
                    chip.Tag = minutes;
                    chip.Click += delegate { QuickEvent(minutes, -1); };
                    d.Controls.Add(chip);
                    qx += 80;
                }
            }
            var btnDel = FlatButton("删除选中", 372, 264, 106, 28);
            btnDel.Click += delegate { RemoveEvent(); };
            d.Controls.Add(btnDel);
        }

        private void UpdateLarkState()
        {
            bool on = cbLark != null && cbLark.Checked;
            if (tbChatId != null) tbChatId.Enabled = on;
            if (tbBotName != null) tbBotName.Enabled = on;
            if (numPoll != null) numPoll.Enabled = on;
        }

        private void DiscoverChat()
        {
            var lark = new LarkClient(store);
            var cfg = new LarkSettings();
            cfg.enabled = true;
            cfg.command = s.lark.command;
            cfg.chatId = tbChatId.Text.Trim();
            cfg.botName = tbBotName.Text.Trim();
            string error;
            string id = lark.DiscoverChatId(cfg, out error);
            if (string.IsNullOrEmpty(id))
            {
                lblTest.Text = "自动发现失败：" + (string.IsNullOrEmpty(error) ? "没找到对话" : error);
                return;
            }
            tbChatId.Text = id;
            lblTest.Text = "已找到会话：" + id;
        }

        private void TestLark()
        {
            var lark = new LarkClient(store);
            var cfg = new LarkSettings();
            cfg.enabled = true;
            cfg.command = s.lark.command;
            cfg.chatId = tbChatId.Text.Trim();
            cfg.botName = tbBotName.Text.Trim();
            var snap = lark.ReadToday(cfg);
            if (!string.IsNullOrEmpty(snap.error))
            {
                lblTest.Text = "读取失败：" + snap.error;
                return;
            }
            string text = "读取成功（" + snap.messageCount + " 条消息）：";
            text += snap.clockIn.HasValue ? "上班 " + snap.clockIn.Value.ToString("HH:mm") : "无上班记录";
            text += "，";
            text += snap.clockOut.HasValue ? "下班 " + snap.clockOut.Value.ToString("HH:mm") : "尚未下班";
            lblTest.Text = text;
        }

        private void RefreshEvents()
        {
            if (cardEvents == null) return;
            cardEvents.Items = SortedEvents();
            cardEvents.LeadOf = Reminders.LeadOf(s, null);
            cardEvents.Invalidate();
        }

        private List<ReminderEvent> SortedEvents()
        {
            var sorted = new List<ReminderEvent>(s.events);
            sorted.Sort(delegate(ReminderEvent a, ReminderEvent b) { return Reminders.ParseStart(a).CompareTo(Reminders.ParseStart(b)); });
            return sorted;
        }

        private void RemoveEvent()
        {
            int index = cardEvents != null ? cardEvents.SelectedIndex : -1;
            if (index < 0) return;
            var sorted = SortedEvents();
            if (index < sorted.Count)
            {
                s.events.Remove(sorted[index]);
                cardEvents.SelectedIndex = -1;
            }
            RefreshEvents();
            UpdateEventEnd();
        }

        /// <summary>快捷填时间：afterMinutes 为「几分钟后」，timeOfDay 为「明天几点」。</summary>
        private void QuickEvent(int afterMinutes, int timeOfDay)
        {
            DateTime start;
            if (timeOfDay >= 0)
                start = DateTime.Today.AddDays(1).AddMinutes(timeOfDay);
            else
                start = DateTime.Now.AddMinutes(afterMinutes);
            dpEvent.Value = start.Date;
            cmbEventTime.Text = start.ToString("HH:mm", CultureInfo.InvariantCulture);
            UpdateEventEnd();
        }

        private bool TryEventStart(out DateTime start)
        {
            start = DateTime.MinValue;
            int minutes;
            if (!SettingsExtensions.TryParseTime(cmbEventTime.Text, out minutes)) return false;
            start = dpEvent.Value.Date.AddMinutes(minutes);
            return true;
        }

        private int EventDuration()
        {
            int[] minutes = new int[] { 15, 30, 45, 60, 90, 120, 180 };
            int i = Math.Max(0, Math.Min(minutes.Length - 1, cmbEventDur.SelectedIndex));
            return minutes[i];
        }

        private void UpdateEventEnd()
        {
            if (lblEventEnd == null || cmbEventDur == null) return;
            DateTime start;
            if (!TryEventStart(out start))
            {
                lblEventEnd.Text = "结束 --:--";
                return;
            }
            DateTime end = start.AddMinutes(EventDuration());
            lblEventEnd.Text = "结束 " + end.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private void AddEvent()
        {
            string title = tbTitle.Text.Trim();
            DateTime start;
            if (title.Length == 0)
            {
                MessageBox.Show(this, "给这条日程起个标题吧。", "还差一点", MessageBoxButtons.OK, MessageBoxIcon.Information);
                tbTitle.Focus();
                return;
            }
            if (!TryEventStart(out start))
            {
                MessageBox.Show(this, "时间格式应为 HH:mm，例如 18:30。", "时间格式有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var e = new ReminderEvent();
            e.id = "local:" + Guid.NewGuid().ToString("N").Substring(0, 8);
            e.source = "local";
            e.title = title;
            e.detail = tbDetail.Text.Trim();
            e.place = tbPlace.Text.Trim();
            e.start = start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            e.end = start.AddMinutes(EventDuration()).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            e.leadMinutes = 0;
            s.events.Add(e);
            tbTitle.Text = "";
            tbDetail.Text = "";
            tbPlace.Text = "";
            RefreshEvents();
        }

        // ---------------- 工资 ----------------

        /// <summary>
        /// 「工资」页：月工资 + 发薪日。应出勤天数按「考勤」页的工作日 / 节假日 / 调休自动算，
        /// 不再单独填；今日收入按打卡时间实时累计，保留 3 位小数。
        /// </summary>
        private void BuildSalary(TabPage page)
        {
            var g = CardBox(page, "工资设置", 8, 8, 492, 170);

            g.Controls.Add(Lbl("月工资", 14, 30, 54, 24));
            numSalary = new NumericUpDown();
            numSalary.DecimalPlaces = 2;
            numSalary.Minimum = 0m;
            numSalary.Maximum = 10000000m;
            numSalary.Increment = 500m;
            numSalary.SetBounds(72, 28, 140, 24);
            numSalary.TextAlign = HorizontalAlignment.Right;
            numSalary.ValueChanged += delegate { UpdateSalaryPreview(); };
            g.Controls.Add(numSalary);
            g.Controls.Add(Lbl("元 / 月", 220, 30, 54, 24));

            g.Controls.Add(Lbl("发薪日", 292, 30, 50, 24));
            numPayday = Num(15, 344, 28, 56, 1, 31);
            numPayday.ValueChanged += delegate { UpdateSalaryPreview(); };
            g.Controls.Add(numPayday);
            g.Controls.Add(Lbl("号", 406, 30, 20, 24));

            g.Controls.Add(Hint("发薪日填 1–31；当月没有这一天（比如 31 号）时按当月最后一天算。", 14, 64, 466, 20));

            lblSalaryPreview = Hint("", 14, 92, 466, 62);
            g.Controls.Add(lblSalaryPreview);

            var n = CardBox(page, "怎么算", 8, 186, 492, 306);
            string[] lines = new string[]
            {
                "日薪 = 月工资 ÷ 本月应出勤天数（按「考勤」页的工作日、节假日、调休自动算）",
                "时薪 = 日薪 ÷ 当日应勤时长（固定班次扣掉午休 / 晚休，弹性班次用填的应勤时长）",
                "今日收入 = 时薪 × 今天已工作秒数，实时累计，最多到日薪；保留 3 位小数",
                "本月收入 = 日薪 × 本月已完成的工作日 + 今日收入",
                "只按「日期 + 今天这一次打卡」推算：没有历史打卡记录，默认之前的工作日都正常出勤",
                "卡片右上角 ¥：单击展开 / 收起工资面板，双击固定在展开状态"
            };
            for (int i = 0; i < lines.Length; i++)
                n.Controls.Add(Hint("· " + lines[i], 14, 28 + i * 42, 466, 38));

            n.Controls.Add(Hint("改完点下面的「保存」，卡片右上角就会出现工资按钮。", 14, 278, 466, 20));
        }

        private void UpdateSalaryPreview()
        {
            if (lblSalaryPreview == null || numSalary == null) return;
            decimal monthly = numSalary.Value;
            if (monthly <= 0m)
            {
                lblSalaryPreview.Text = "填上月工资后，卡片右上角会出现 ¥ 按钮；没填时点它会直接打开这一页。";
                return;
            }
            int days = SalaryCalc.RequiredWorkdays(s, DateTime.Now);
            decimal daily = monthly / Math.Max(1, days);
            lblSalaryPreview.Text = "本月应出勤 " + days + " 个工作日 · 日薪约 ¥" +
                daily.ToString("N2", CultureInfo.InvariantCulture) +
                " · 按今天 " + DateTime.Now.ToString("M月d日", CultureInfo.InvariantCulture) + " 实时累计";
        }

        // ---------------- 小纸条（第 7 页） ----------------

        private void BuildNotes(TabPage page)
        {
            var styleCard = CardBox(page, "会话面板样式", 8, 8, 492, 122);
            rbNoteDrawer = new RadioButton();
            rbNoteDrawer.Text = "抽屉式（A）";
            rbNoteDrawer.SetBounds(16, 30, 180, 24);
            rbNoteDrawer.ForeColor = Ui.Ink;
            rbNoteDrawer.BackColor = Color.Transparent;
            styleCard.Controls.Add(rbNoteDrawer);
            rbNoteColumns = new RadioButton();
            rbNoteColumns.Text = "极简双栏（C）";
            rbNoteColumns.SetBounds(16, 78, 180, 24);
            rbNoteColumns.ForeColor = Ui.Ink;
            rbNoteColumns.BackColor = Color.Transparent;
            styleCard.Controls.Add(rbNoteColumns);
            styleCard.Controls.Add(Hint("392 宽抽屉 + 横排胶囊 + 8 条紧凑列表", 200, 32, 280, 20));
            styleCard.Controls.Add(Hint("470 宽双栏：左栏联系人 + 右栏对话", 200, 80, 280, 20));

            var idCard = CardBox(page, "本机 ID 与昵称", 8, 138, 492, 132);
            lblNoteId = new Label();
            lblNoteId.Text = "00000";
            lblNoteId.SetBounds(14, 26, 150, 34);
            lblNoteId.Font = UiFont.MakeNumber(18f, FontStyle.Bold);
            lblNoteId.ForeColor = Ui.Ink;
            idCard.Controls.Add(lblNoteId);
            idCard.Controls.Add(Hint("别人加你 / 群里发消息会显示这个昵称", 190, 30, 290, 18));
            idCard.Controls.Add(Lbl("昵称", 14, 66, 40, 22));
            tbNoteNick = Tb("", 58, 64, 200);
            tbNoteNick.MaxLength = NoteSettings.MaxNick;
            idCard.Controls.Add(tbNoteNick);
            var btnCopy = FlatButton("复制 ID", 14, 96, 84, 28);
            btnCopy.Click += delegate { try { Clipboard.SetText(editNoteId); } catch { } };
            idCard.Controls.Add(btnCopy);
            var btnNewId = FlatButton("重新生成…", 106, 96, 116, 28);
            btnNewId.Click += delegate
            {
                if (MessageBox.Show(this, "重新生成后这台电脑的 ID 会变，旧联系人需要重新加。继续？",
                    "重新生成 ID", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                editNoteId = NoteSettings.GenerateId();
                lblNoteId.Text = editNoteId;
            };
            idCard.Controls.Add(btnNewId);
            idCard.Controls.Add(Hint("每台电脑一个 5 位数，别人用它直接和你对话", 236, 100, 250, 18));

            var relayCard = CardBox(page, "公共中继", 8, 278, 492, 110);
            relayCard.Controls.Add(Lbl("中继地址", 14, 28, 66, 24));
            tbNoteRelay = Tb("", 84, 28, 246);
            relayCard.Controls.Add(tbNoteRelay);
            relayCard.Controls.Add(Lbl("端口", 340, 28, 40, 24));
            numNotePort = Num(1883, 382, 28, 66, 1, 65535);
            relayCard.Controls.Add(numNotePort);
            var btnRelay = FlatButton("测试连接", 14, 66, 96, 28);
            btnRelay.Click += delegate { TestNoteRelay(); };
            relayCard.Controls.Add(btnRelay);
            lblNoteRelayState = Hint("", 118, 68, 360, 24);
            relayCard.Controls.Add(lblNoteRelayState);
            relayCard.Controls.Add(Hint("公共 MQTT：只按 ID 转发、不保存，别发敏感内容", 14, 88, 466, 18));

            var ctCard = CardBox(page, "对话", 8, 396, 492, 174);
            lstNoteContacts = new ListBox();
            lstNoteContacts.SetBounds(14, 26, 464, 70);
            lstNoteContacts.BorderStyle = BorderStyle.FixedSingle;
            lstNoteContacts.Font = UiFont.Make(9f, FontStyle.Regular);
            ctCard.Controls.Add(lstNoteContacts);
            var btnAddNote = AccentButton("＋ 新建对话", 14, 104, 96, 28);
            btnAddNote.Click += delegate { AddNoteContactDialog(); };
            ctCard.Controls.Add(btnAddNote);
            var btnRemark = FlatButton("改名字", 118, 104, 84, 28);
            btnRemark.Click += delegate { EditNoteRemarkDialog(); };
            ctCard.Controls.Add(btnRemark);
            var btnDel = FlatButton("删除", 210, 104, 84, 28);
            btnDel.Click += delegate { DeleteNoteContact(); };
            ctCard.Controls.Add(btnDel);
            ctCard.Controls.Add(Hint("5 位数字 = 单独对话；其它 5 位字符 = 对话码，同码的人进同一个对话", 300, 96, 186, 36));
        }

        private void FillNoteContacts()
        {
            if (lstNoteContacts == null) return;
            int keep = lstNoteContacts.SelectedIndex;
            lstNoteContacts.BeginUpdate();
            lstNoteContacts.Items.Clear();
            foreach (var c in editNoteContacts)
            {
                var th = s.note.Thread(c.id);
                string last = "";
                if (th.messages.Count > 0)
                {
                    var m = th.messages[th.messages.Count - 1];
                    last = " · " + (m.text.Length > 12 ? m.text.Substring(0, 12) + "…" : m.text);
                }
                string tag = c.IsRoom ? "对话码 " + c.id : "ID " + c.id;
                string name = string.IsNullOrWhiteSpace(c.remark) ? (c.nick ?? "") : c.remark;
                string line = string.IsNullOrWhiteSpace(name) ? tag : name + " · " + tag;
                lstNoteContacts.Items.Add(line + last);
            }
            lstNoteContacts.EndUpdate();
            if (keep >= 0 && keep < lstNoteContacts.Items.Count) lstNoteContacts.SelectedIndex = keep;
        }

        private void AddNoteContactDialog()
        {
            using (var dlg = new NoteContactDialog("新建对话", "", "", true))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string code = NoteSettings.NormalizeCode(dlg.ContactId);
                if (!NoteSettings.ValidCode(code))
                {
                    MessageBox.Show(this, "要 5 位字符：5 位数字 = 和这个人单独对话；其它 5 位字符 = 对话码，别人输入同一个码就进同一个对话。",
                        "对话码格式不对", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                bool dup = false;
                foreach (var c in editNoteContacts) if (c.id == code) { c.remark = dlg.Remark; dup = true; break; }
                if (!dup)
                {
                    var c = new NoteContact();
                    c.id = code;
                    c.remark = dlg.Remark;
                    editNoteContacts.Add(c);
                }
                FillNoteContacts();
            }
        }

        private void EditNoteRemarkDialog()
        {
            if (lstNoteContacts.SelectedIndex < 0 || lstNoteContacts.SelectedIndex >= editNoteContacts.Count) return;
            var c = editNoteContacts[lstNoteContacts.SelectedIndex];
            using (var dlg = new NoteContactDialog("改名字", c.id, c.remark, false))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                c.remark = dlg.Remark;
                FillNoteContacts();
            }
        }

        private void DeleteNoteContact()
        {
            if (lstNoteContacts.SelectedIndex < 0 || lstNoteContacts.SelectedIndex >= editNoteContacts.Count) return;
            var c = editNoteContacts[lstNoteContacts.SelectedIndex];
            if (MessageBox.Show(this, "删除对话「" + c.Display() + "」？本机这段对话（最多 8 条消息）也一起删掉。",
                "删除对话", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            editNoteContacts.RemoveAt(lstNoteContacts.SelectedIndex);
            s.note.RemoveContact(c.id);
            FillNoteContacts();
        }

        private void TestNoteRelay()
        {
            string host = tbNoteRelay.Text.Trim();
            int port = (int)numNotePort.Value;
            lblNoteRelayState.Text = "连接中…";
            lblNoteRelayState.ForeColor = Ui.Muted;
            Application.DoEvents();
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    var ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(5000)) throw new Exception("连接超时");
                    c.EndConnect(ar);
                    var st = c.GetStream();
                    var pkt = NoteRelay.ConnectPacket();
                    st.Write(pkt, 0, pkt.Length);
                    st.ReadTimeout = 5000;
                    var buf = new byte[4];
                    int n = st.Read(buf, 0, 4);
                    if (n < 4 || buf[0] != 0x20 || buf[3] != 0) throw new Exception("MQTT 拒绝");
                    lblNoteRelayState.Text = "● 已连接";
                    lblNoteRelayState.ForeColor = Color.FromArgb(44, 139, 90);
                }
            }
            catch (Exception ex)
            {
                lblNoteRelayState.Text = "连接失败：" + ex.Message;
                lblNoteRelayState.ForeColor = Color.FromArgb(205, 90, 70);
            }
        }

        private static List<NoteContact> CloneNoteContacts(List<NoteContact> src)
        {
            var list = new List<NoteContact>();
            if (src == null) return list;
            foreach (var c in src)
            {
                if (c == null) continue;
                var n = new NoteContact();
                n.id = c.id;
                n.remark = c.remark;
                n.nick = c.nick;      // 对方自己设的昵称：保存时别丢（否则列表会退回显示 ID）
                list.Add(n);
            }
            return list;
        }

        private class NoteContactDialog : Form
        {
            private readonly TextBox tbId;
            private readonly TextBox tbRemark;
            public string ContactId { get { return tbId.Text.Trim(); } }
            public string Remark { get { return tbRemark.Text.Trim(); } }

            public NoteContactDialog(string title, string id, string remark, bool idEditable)
            {
                Text = title;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(380, 196);
                BackColor = Ui.WindowBg;
                Font = UiFont.Make(9f, FontStyle.Regular);
                AutoScaleMode = AutoScaleMode.None;

                var l1 = new Label(); l1.Text = "5 位 ID / 对话码"; l1.SetBounds(14, 18, 110, 22);
                l1.ForeColor = Ui.Ink; Controls.Add(l1);
                tbId = new TextBox(); tbId.SetBounds(126, 16, 240, 24);
                tbId.Text = id; tbId.MaxLength = 5; tbId.Enabled = idEditable;
                Controls.Add(tbId);

                var l2 = new Label(); l2.Text = "名字（≤8 字）"; l2.SetBounds(14, 56, 110, 22);
                l2.ForeColor = Ui.Ink; Controls.Add(l2);
                tbRemark = new TextBox(); tbRemark.SetBounds(126, 54, 240, 24);
                tbRemark.Text = remark; tbRemark.MaxLength = NoteSettings.MaxRemark;
                Controls.Add(tbRemark);

                var hint = new Label();
                hint.SetBounds(14, 90, 352, 48);
                hint.ForeColor = Ui.Muted;
                hint.Text = "5 位数字 = 和这个 ID 的人单独对话；其它任意 5 位字符 = 对话码，别人输入同一个码就进同一个对话。";
                Controls.Add(hint);

                var ok = new Button();
                ok.Text = "确定";
                ok.SetBounds(170, 142, 96, 34);
                ok.FlatStyle = FlatStyle.Flat;
                ok.FlatAppearance.BorderSize = 0;
                ok.BackColor = Ui.Accent;
                ok.ForeColor = Ui.AccentInk;
                ok.DialogResult = DialogResult.OK;
                Controls.Add(ok);
                AcceptButton = ok;

                var cancel = new Button();
                cancel.Text = "取消";
                cancel.SetBounds(272, 142, 96, 34);
                cancel.FlatStyle = FlatStyle.Flat;
                cancel.FlatAppearance.BorderColor = Ui.Edge;
                cancel.BackColor = Ui.Surface;
                cancel.DialogResult = DialogResult.Cancel;
                Controls.Add(cancel);
                CancelButton = cancel;

                float k;
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
                if (Math.Abs(k - 1f) > 0.01f) { SuspendLayout(); Scale(new SizeF(k, k)); ResumeLayout(true); }
            }
        }

        // ---------------- 日程（四类 + Outlook） ----------------

        /// <summary>
        /// 「日程」页：多份 Outlook/Exchange 日历订阅 + 四类日程各自的通知样式。
        /// 四类 = 固定日程 / 飞书会议 / 普通日程 / Outlook 日程，每类一套「范围 + 扩散样式 + 配色」。
        /// </summary>
        private void BuildAgenda(TabPage page)
        {
            var ol = CardBox(page, "Outlook / Exchange 日历", 8, 8, 492, 264);

            lblCalCount = Lbl("0 份日历", 14, 28, 96, 22);
            ol.Controls.Add(lblCalCount);
            ol.Controls.Add(Lbl("默认提前", 118, 28, 56, 22));
            numOutlookLead = Num(10, 178, 26, 50, 0, 120);
            ol.Controls.Add(numOutlookLead);
            ol.Controls.Add(Lbl("分", 232, 28, 20, 22));

            var btnAdd = AccentButton("＋ 添加", 286, 24, 88, 28);
            btnAdd.Click += delegate { EditCalendar(null); };
            ol.Controls.Add(btnAdd);

            btnCalLogin = FlatButton("Outlook 登录", 382, 24, 96, 28);
            btnCalLogin.Click += delegate { OpenOutlookLogin(); };
            ol.Controls.Add(btnCalLogin);

            lstCalendars = new CheckedListBox();
            lstCalendars.SetBounds(14, 58, 464, 112);
            lstCalendars.CheckOnClick = true;
            lstCalendars.IntegralHeight = false;
            lstCalendars.BorderStyle = BorderStyle.FixedSingle;
            lstCalendars.Font = UiFont.Make(9f, FontStyle.Regular);
            lstCalendars.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (loadingCals || e.Index < 0 || e.Index >= editCals.Count) return;
                // ItemCheck 触发时勾选状态还没变，按 NewValue 预写
                editCals[e.Index].enabled = e.NewValue == CheckState.Checked;
                if (!IsDisposed) BeginInvoke((MethodInvoker)delegate { RefreshCalendarLabel(); });
            };
            lstCalendars.DoubleClick += delegate { EditCalendar(SelectedCalendar()); };
            ol.Controls.Add(lstCalendars);

            var btnEdit = FlatButton("编辑…", 14, 178, 76, 28);
            btnEdit.Click += delegate { EditCalendar(SelectedCalendar()); };
            ol.Controls.Add(btnEdit);

            var btnDelete = FlatButton("删除", 98, 178, 64, 28);
            btnDelete.Click += delegate { DeleteCalendar(SelectedCalendar()); };
            ol.Controls.Add(btnDelete);

            var btnSync = AccentButton("立即同步", 170, 178, 96, 28);
            btnSync.Click += delegate { SyncCalendarsNow(); };
            ol.Controls.Add(btnSync);

            lblCalendarState = Hint("还没同步过。", 274, 176, 204, 52);
            ol.Controls.Add(lblCalendarState);

            ol.Controls.Add(Hint("可同时订阅多份日历（工作日历 / 个人日历）；双击一行编辑。", 14, 232, 464, 22));

            var st = CardBox(page, "四类日程的通知样式", 8, 280, 492, 258);
            st.Controls.Add(Lbl("日程类型", 14, 30, 90, 24));
            st.Controls.Add(Lbl("提醒范围", 116, 30, 70, 24));
            st.Controls.Add(Lbl("扩散样式", 196, 30, 80, 24));
            st.Controls.Add(Lbl("配色", 314, 30, 50, 24));

            cmbSpread = new ComboBox[4];
            cmbShape = new ComboBox[4];
            cmbColor = new ComboBox[4];
            string[] spreadText = new string[] { "卡内", "全屏" };
            string[] shapeText = new string[] { "圆形柔光", "进度条拉伸", "警戒带" };
            for (int i = 0; i < StyleKinds.Length; i++)
            {
                int y = 58 + i * 44;
                st.Controls.Add(Lbl(Reminders.KindName(StyleKinds[i]), 14, y, 100, 24));

                cmbSpread[i] = new ComboBox();
                cmbSpread[i].DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (string x in spreadText) cmbSpread[i].Items.Add(x);
                cmbSpread[i].SetBounds(106, y - 2, 78, 24);
                st.Controls.Add(cmbSpread[i]);

                cmbShape[i] = new ComboBox();
                cmbShape[i].DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (string x in shapeText) cmbShape[i].Items.Add(x);
                cmbShape[i].SetBounds(190, y - 2, 112, 24);
                st.Controls.Add(cmbShape[i]);

                cmbColor[i] = new ComboBox();
                cmbColor[i].DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (string id in ReminderStyles.Ids) cmbColor[i].Items.Add(ReminderStyles.ShortNameOf(id));
                cmbColor[i].SetBounds(306, y - 2, 170, 24);
                st.Controls.Add(cmbColor[i]);
            }
            st.Controls.Add(Hint("固定日程的每一条还能在「固定日程」页单独选配色，那里选的优先。", 14, 232, 466, 22));
        }

        private int ColorIndex(string id)
        {
            if (string.IsNullOrEmpty(id) || id == ReminderStyles.Follow) return 0;
            var all = Themes.All;
            for (int i = 0; i < all.Length; i++)
                if (all[i].id == id) return i + 1;
            return 0;
        }

        /// <summary>日历列表一行：名字 · 类型 · 提前量。</summary>
        private void FillCalendarList()
        {
            if (lstCalendars == null) return;
            int sel = lstCalendars.SelectedIndex;
            loadingCals = true;
            lstCalendars.BeginUpdate();
            lstCalendars.Items.Clear();
            foreach (var c in editCals) lstCalendars.Items.Add(c.DisplayName, c.enabled);
            lstCalendars.EndUpdate();
            loadingCals = false;
            if (lstCalendars.Items.Count > 0)
                lstCalendars.SelectedIndex = (sel >= 0 && sel < lstCalendars.Items.Count) ? sel : 0;
            RefreshCalendarLabel();
            UpdateOutlookState();
        }

        private CalendarSource SelectedCalendar()
        {
            if (lstCalendars == null) return null;
            int i = lstCalendars.SelectedIndex;
            if (i < 0 || i >= editCals.Count) return null;
            return editCals[i];
        }

        private void RefreshCalendarLabel()
        {
            if (lblCalCount == null) return;
            int active = 0;
            foreach (var c in editCals) if (c.enabled && c.HasSource) active++;
            lblCalCount.Text = editCals.Count + " 份 · " + active + " 启用";
        }

        private static List<CalendarSource> CloneCalendars(List<CalendarSource> src)
        {
            var list = new List<CalendarSource>();
            if (src == null) return list;
            foreach (var c in src)
            {
                if (c == null) continue;
                var n = new CalendarSource();
                n.id = c.id;
                n.name = c.name;
                n.enabled = c.enabled;
                n.source = c.source;
                n.url = c.url;
                n.path = c.path;
                n.leadMinutes = c.leadMinutes;
                n.lastSync = c.lastSync;
                n.lastError = c.lastError;
                n.cached = c.cached == null ? new List<ReminderEvent>() : new List<ReminderEvent>(c.cached);
                list.Add(n);
            }
            return list;
        }

        private void UpdateOutlookState()
        {
            if (btnCalLogin != null)
                btnCalLogin.Text = string.IsNullOrWhiteSpace(s.outlook.refreshToken) ? "Outlook 登录" : "Outlook 账号";
            if (lblCalendarState == null) return;
            int active = 0;
            string newest = "";
            foreach (var c in editCals)
            {
                if (c.enabled && c.HasSource) active++;
                if (!string.IsNullOrEmpty(c.lastSync) && c.lastSync.CompareTo(newest) > 0) newest = c.lastSync;
            }
            string text = active + " 份启用";
            if (!string.IsNullOrEmpty(newest)) text += " · 上次同步 " + newest;
            else if (!string.IsNullOrEmpty(s.outlook.lastSync)) text += " · 上次同步 " + s.outlook.lastSync;
            if (!string.IsNullOrEmpty(s.outlook.lastError))
                text += "\n" + LarkClient.Shorten(s.outlook.lastError, 62);
            lblCalendarState.Text = text;
        }

        /// <summary>添加 / 编辑一份日历；existing 为 null 表示新增。</summary>
        private void EditCalendar(CalendarSource existing)
        {
            bool isNew = existing == null;
            var seed = isNew ? new CalendarSource() : CloneCalendars(new List<CalendarSource> { existing })[0];
            if (isNew)
            {
                seed.id = "cal-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                seed.name = "我的日历";
            }
            using (var dlg = new CalendarEditForm(seed, isNew, !string.IsNullOrWhiteSpace(s.outlook.refreshToken)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var result = dlg.Result;
                if (result.source == "graph")
                {
                    foreach (var c in editCals)
                    {
                        if (c == existing) continue;
                        if (c.source == "graph")
                        {
                            MessageBox.Show(this, "只能添加一份「登录 Outlook」日历；其它日历请用 .ics 订阅。",
                                "已经有一份登录日历", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                    }
                }
                if (isNew) editCals.Add(result);
                else
                {
                    int idx = editCals.IndexOf(existing);
                    if (idx >= 0) editCals[idx] = result;
                }
                FillCalendarList();
            }
        }

        private void DeleteCalendar(CalendarSource c)
        {
            if (c == null) return;
            if (MessageBox.Show(this, "删除日历「" + c.DisplayName + "」？只会删本地订阅，不会动 Outlook 里的日历。",
                    "删除日历", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            editCals.Remove(c);
            FillCalendarList();
        }

        /// <summary>把工作副本里的所有启用日历拉一遍（点击时同步，不等待定时器）。</summary>
        private void SyncCalendarsNow()
        {
            var active = new List<CalendarSource>();
            foreach (var c in editCals) if (c.enabled && c.HasSource) active.Add(c);
            if (active.Count == 0)
            {
                MessageBox.Show(this, "先添加一份日历并勾选启用。", "没有可同步的日历",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                int total = 0;
                var errors = new List<string>();
                foreach (var c in active)
                {
                    var request = CalendarSync.Request(c, s.outlook, DateTime.Today, DateTime.Today.AddDays(14), 20000);
                    var fetched = CalendarSync.Fetch(request);
                    if (fetched.RotatedRefreshToken != null)
                    {
                        s.outlook.refreshToken = fetched.RotatedRefreshToken;
                        s.outlook.tokenAt = fetched.TokenAt;
                    }
                    if (fetched.Succeeded)
                    {
                        c.cached = fetched.Events == null ? new List<ReminderEvent>() : fetched.Events;
                        c.lastSync = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                        c.lastError = "";
                        total += c.cached.Count;
                    }
                    else
                    {
                        c.lastError = fetched.Error;
                        errors.Add((string.IsNullOrWhiteSpace(c.name) ? "日历" : c.name) + "：" + fetched.Error);
                    }
                }
                s.outlook.lastError = errors.Count == 0 ? "" : string.Join("；", errors.ToArray());
                FillCalendarList();
                lblCalendarState.Text = errors.Count == 0
                    ? "同步成功：" + total + " 个日程（未来 14 天）"
                    : "部分失败：" + LarkClient.Shorten(s.outlook.lastError, 70);
            }
            finally { Cursor = Cursors.Default; }
        }

        /// <summary>打开 Outlook 登录窗；首次登录成功后自动加一份 Graph 日历并同步。</summary>
        private void OpenOutlookLogin()
        {
            bool wasLogged = !string.IsNullOrWhiteSpace(s.outlook.refreshToken);
            using (var dlg = new OutlookLoginForm(s, original, store))
            {
                dlg.ShowDialog(this);
                bool logged = !string.IsNullOrWhiteSpace(s.outlook.refreshToken);
                bool hasGraph = editCals.Exists(delegate (CalendarSource c) { return c.source == "graph"; });
                if (logged)
                {
                    // 刚登录：把已有的 Graph 日历重新启用（之前没登录会被 Normalize 停用）
                    foreach (var c in editCals) if (c.source == "graph") c.enabled = true;
                }
                if (logged && !hasGraph)
                {
                    var c = new CalendarSource();
                    c.id = "cal-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    c.name = string.IsNullOrWhiteSpace(s.outlook.account) ? "Outlook 登录日历" : s.outlook.account;
                    c.source = "graph";
                    c.enabled = true;
                    editCals.Add(c);
                    FillCalendarList();
                }
                else if (!logged && hasGraph)
                {
                    // 退出登录：把 graph 日历停用，避免一直去拉失败
                    foreach (var c in editCals) if (c.source == "graph") c.enabled = false;
                    FillCalendarList();
                }
                if (!wasLogged && logged) SyncCalendarsNow();
            }
        }
        // ---------------- 外观 ----------------

        private void BuildThemes(TabPage page)
        {
            BuildSkinCard(page);

            var g = CardBox(page, "主题配色", 8, 116, 492, 232);
            lblThemeLocked = new Label();
            lblThemeLocked.Text = "该风格自带配色，切回《默认》可选 " + Themes.All.Length + " 套";
            lblThemeLocked.SetBounds(150, 5, 326, 18);
            lblThemeLocked.TextAlign = ContentAlignment.MiddleRight;
            lblThemeLocked.Font = UiFont.Make(8f, FontStyle.Regular);
            lblThemeLocked.ForeColor = Ui.Muted;
            lblThemeLocked.BackColor = Color.Transparent;
            lblThemeLocked.Tag = "hint";
            lblThemeLocked.Visible = false;
            g.Controls.Add(lblThemeLocked);

            picker = new ThemePicker();
            picker.SetBounds(14, 30, 456, 122);
            picker.SelectedIdChanged += delegate { UpdateThemeName(); };
            g.Controls.Add(picker);
            lblThemeName = Lbl("", 14, 160, 210, 20);
            lblThemeName.ForeColor = Ui.Muted;
            lblThemeName.Tag = "hint";
            g.Controls.Add(lblThemeName);

            btnEditTheme = AccentButton("编辑", 240, 156, 70, 28);
            btnEditTheme.Tag = "accent-edit";
            btnEditTheme.Click += delegate { EditCustomTheme(); };
            g.Controls.Add(btnEditTheme);

            btnNewTheme = AccentButton("＋ 新建主题", 316, 156, 154, 28);
            btnNewTheme.Click += delegate { NewCustomTheme(); };
            g.Controls.Add(btnNewTheme);

            // 背景风格：纯色 / 磨砂玻璃（作用于悬浮卡片、小纸条面板、右键胶囊气泡）
            g.Controls.Add(Lbl("背景风格", 14, 198, 62, 22));
            rbBgSolid = new RadioButton();
            rbBgSolid.Text = "纯色";
            rbBgSolid.SetBounds(84, 196, 64, 24);
            rbBgSolid.ForeColor = Ui.Ink;
            rbBgSolid.BackColor = Color.Transparent;
            g.Controls.Add(rbBgSolid);
            rbBgGlass = new RadioButton();
            rbBgGlass.Text = "磨砂玻璃";
            rbBgGlass.SetBounds(156, 196, 96, 24);
            rbBgGlass.ForeColor = Ui.Ink;
            rbBgGlass.BackColor = Color.Transparent;
            g.Controls.Add(rbBgGlass);
            g.Controls.Add(Hint("磨砂玻璃：半透明 + 磨砂颗粒", 258, 198, 222, 20));

            rbBgSolid.CheckedChanged += delegate { RefreshArcUi(); };
            rbBgGlass.CheckedChanged += delegate { RefreshArcUi(); };
        }

        /// <summary>
        /// 外观页「弧光」卡片：开关 + 状态行 + 派生色。可用性只看《默认》皮肤，
        /// 纯色 / 磨砂玻璃只是卡片底怎么画，两样都能开。
        /// </summary>
        private void BuildArcCard(TabPage page)
        {
            var g = CardBox(page, "弧光", 8, 356, 492, 116);
            g.Controls.Add(Hint("取当前主题的强调色作种子，沿 −100° / −45° / 0° / +42° 派生", 14, 26, 360, 20));

            swArc = new ArcSwitch();
            swArc.SetBounds(414, 22, 52, 28);
            swArc.Click += delegate
            {
                if (loadingArc) return;
                s.ui.arcGlow = swArc.Checked;
                RefreshArcUi();
            };
            g.Controls.Add(swArc);

            lblArcState = Hint("", 14, 58, 330, 20);
            lblArcState.ForeColor = Ui.Muted;
            g.Controls.Add(lblArcState);

            lblArcLock = new Label();
            lblArcLock.SetBounds(300, 58, 166, 20);
            lblArcLock.TextAlign = ContentAlignment.MiddleRight;
            lblArcLock.Font = UiFont.Make(8f, FontStyle.Regular);
            lblArcLock.ForeColor = Ui.Muted;
            lblArcLock.BackColor = Color.Transparent;
            lblArcLock.Tag = "hint";
            g.Controls.Add(lblArcLock);

            arcSwatches = new ArcSwatchStrip();
            arcSwatches.SetBounds(14, 82, 464, 26);
            g.Controls.Add(arcSwatches);
        }

        /// <summary>弧光开关的可用性与说明：非默认皮肤置灰、设置值不清空。</summary>
        private void RefreshArcUi()
        {
            if (swArc == null) return;
            var skin = Skins.Current(s.ui);
            bool supported = skin.IsDefault;
            bool glass = rbBgGlass != null ? rbBgGlass.Checked : s.ui.bgStyle == Frost.Glass;
            string where = glass ? "磨砂玻璃" : "纯色卡片";
            swArc.Enabled = supported;
            if (!supported)
                lblArcState.Text = "仅《默认》支持弧光，其它皮肤不叠加";
            else if (s.ui.arcGlow)
                lblArcState.Text = "已开启 · 叠加在" + where + "上";
            else
                lblArcState.Text = "已关闭 · 回到普通" + where;
            lblArcLock.Text = supported ? "默认 · 纯色 / 磨砂玻璃" : "仅默认皮肤";
            if (arcSwatches != null)
            {
                ThemeDef theme = (supported && picker != null) ? Themes.Get(picker.SelectedId) : Skins.ResolveTheme(s.ui);
                swArc.Palette = ArcGlow.For(theme);
                swArc.Invalidate();
                arcSwatches.Seed = theme.accent;
                arcSwatches.Palette = swArc.Palette;
                arcSwatches.Invalidate();
            }
        }

        /// <summary>「界面风格」卡片：6 个可点的风格块，点一下立刻预览，点「保存」才落盘。</summary>
        private void BuildSkinCard(TabPage page)
        {
            var g = CardBox(page, "界面风格", 8, 8, 492, 100);

            var tip = new Label();
            tip.Text = "6 套 · 点一下立刻预览，点「保存」后悬浮窗跟着换";
            tip.SetBounds(150, 5, 326, 18);
            tip.TextAlign = ContentAlignment.MiddleRight;
            tip.Font = UiFont.Make(8f, FontStyle.Regular);
            tip.ForeColor = Ui.Muted;
            tip.BackColor = Color.Transparent;
            tip.Tag = "hint";
            g.Controls.Add(tip);

            skinPicker = new SkinPicker();
            skinPicker.SetBounds(14, 26, 464, 60);
            skinPicker.SelectedId = s.ui.skin;
            skinPicker.SelectedIdChanged += delegate { SelectSkin(skinPicker.SelectedId); };
            g.Controls.Add(skinPicker);
        }

        /// <summary>点一个风格块：只改内存里的 ui.skin，并立刻按新皮肤重画整个设置窗口。</summary>
        private void SelectSkin(string id)
        {
            s.ui.skin = Skins.Get(id).id;
            ApplySkin();
        }

        private void UpdateThemeName()
        {
            if (lblThemeName == null || picker == null) return;
            var t = Themes.Get(picker.SelectedId);
            lblThemeName.Text = "当前：" + (Themes.IsCustom(t.id) ? "我的主题 · " : "") + t.name;
            if (btnEditTheme != null)
            {
                bool locked = !Skins.Current(s.ui).IsDefault;
                bool custom = Themes.IsCustom(t.id);
                btnEditTheme.Enabled = !locked && custom;
                btnEditTheme.ForeColor = (!locked && custom) ? Ui.Accent : Ui.Muted;
                btnEditTheme.FlatAppearance.BorderColor = (!locked && custom) ? Ui.Accent : Ui.Edge;
            }
            RefreshArcUi();
        }

        private void NewCustomTheme()
        {
            var seed = Themes.Get(picker.SelectedId);
            using (var dlg = new ColorWheelForm("新建主题", "我的主题", seed.bgTop, seed.accent))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var ct = new CustomTheme();
                ct.id = "my-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                ct.name = dlg.ThemeName;
                ct.bg = dlg.BgHex;
                ct.bar = dlg.BarHex;
                ct.accent = dlg.BarHex;
                s.customThemes.Add(ct);
                Themes.SetCustom(s.customThemes);
                picker.SelectedId = ct.id;
                picker.Invalidate();
                UpdateThemeName();
            }
        }

        private void EditCustomTheme()
        {
            CustomTheme ct = null;
            foreach (var item in s.customThemes)
                if (item != null && item.id == picker.SelectedId) { ct = item; break; }
            if (ct == null)
            {
                MessageBox.Show(this, "内置主题不能直接改。点「＋ 新建主题」可以基于它另存一套。",
                    "编辑主题", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var t = Themes.Get(ct.id);
            Color bg = ParseHex(ct.bg, t.bgTop);
            Color bar = ParseHex(string.IsNullOrWhiteSpace(ct.bar) ? ct.accent : ct.bar, t.accent);
            using (var dlg = new ColorWheelForm("编辑主题", ct.name, bg, bar))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ct.name = dlg.ThemeName;
                ct.bg = dlg.BgHex;
                ct.bar = dlg.BarHex;
                ct.accent = dlg.BarHex;
                Themes.SetCustom(s.customThemes);
                picker.Invalidate();
                picker.SelectedId = ct.id;
                UpdateThemeName();
            }
        }

        private static Color ParseHex(string text, Color fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(text) && text.Length == 7 && text[0] == '#')
                    return Color.FromArgb(Convert.ToInt32(text.Substring(1, 2), 16),
                        Convert.ToInt32(text.Substring(3, 2), 16), Convert.ToInt32(text.Substring(5, 2), 16));
            }
            catch { }
            return fallback;
        }

        private void BuildLook(TabPage page)
        {
            var g = CardBox(page, "外观与提醒", 8, 480, 492, 190);

            g.Controls.Add(Lbl("不透明度", 14, 28, 58, 24));
            numOpacity = Num(94, 76, 28, 56, 30, 100);
            g.Controls.Add(numOpacity);
            g.Controls.Add(Lbl("%", 136, 28, 20, 24));

            g.Controls.Add(Lbl("自动收起", 180, 28, 58, 24));
            numAutoCollapse = Num(20, 242, 28, 56, 0, 120);
            g.Controls.Add(numAutoCollapse);
            g.Controls.Add(Lbl("秒（0=不自动）", 302, 28, 110, 24));

            cbTopMost = new CheckBox();
            cbTopMost.Text = "窗口置顶";
            cbTopMost.SetBounds(14, 58, 96, 22);
            g.Controls.Add(cbTopMost);

            cbAutoStart = new CheckBox();
            cbAutoStart.Text = "开机自启";
            cbAutoStart.SetBounds(120, 58, 100, 22);
            g.Controls.Add(cbAutoStart);

            cbShowLunch = new CheckBox();
            cbShowLunch.Text = "显示午休倒计时";
            cbShowLunch.SetBounds(230, 58, 150, 22);
            g.Controls.Add(cbShowLunch);


            cbReducedMotion = new CheckBox();
            cbReducedMotion.Text = "减少动态效果";
            cbReducedMotion.SetBounds(14, 80, 142, 22);
            g.Controls.Add(cbReducedMotion);
            g.Controls.Add(Hint("少动一点，提醒内容照旧。", 164, 80, 310, 22));

            g.Controls.Add(Lbl("最短停留", 14, 106, 58, 24));
            numReminderHold = Num(6, 76, 106, 56, 3, 20);
            g.Controls.Add(numReminderHold);
            g.Controls.Add(Lbl("秒（3–20），之后常驻到时段结束", 140, 106, 330, 24));

            g.Controls.Add(Lbl("背景光效", 14, 134, 58, 24));
            cmbGlow = new ComboBox();
            cmbGlow.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbGlow.SetBounds(76, 132, 170, 24);
            cmbGlow.Items.AddRange(new object[] { "原样式（默认）", "角落柔光 A", "进度条同色光 B", "两种都开 A+B" });
            g.Controls.Add(cmbGlow);
            g.Controls.Add(Lbl("强度 1.6×", 254, 134, 90, 22));

            var btnFonts = FlatButton("字体…", 350, 132, 116, 28);
            btnFonts.Click += delegate { ChooseFonts(); };
            g.Controls.Add(btnFonts);

            g.Controls.Add(Hint("提醒的范围 / 扩散样式 / 配色按四类日程分开设，在「日程」页。", 14, 160, 466, 20));
        }

        private void ChooseFonts()
        {
            using (var dlg = new FontsForm(editTextFont, editNumberFont))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                editTextFont = dlg.ResultTextFont;
                editNumberFont = dlg.ResultNumberFont;
            }
        }

        private void BuildBackup(TabPage page)
        {
            var g = CardBox(page, "配置备份", 8, 678, 492, 112);
            g.Controls.Add(Hint("换电脑或给同事复用：导出一份，带过去再导入。", 14, 24, 466, 18));

            var btnExport = FlatButton("导出配置…", 14, 48, 108, 30);
            btnExport.Click += delegate { ExportConfig(); };
            g.Controls.Add(btnExport);

            var btnImport = FlatButton("导入配置…", 132, 48, 108, 30);
            btnImport.Click += delegate { ImportConfig(); };
            g.Controls.Add(btnImport);

            var btnReset = FlatButton("恢复默认", 250, 48, 108, 30);
            btnReset.Click += delegate { ResetDefaults(); };
            g.Controls.Add(btnReset);

            g.Controls.Add(Hint("导入后重启悬浮窗完全生效；飞书会话 ID 会一起带过来。", 14, 84, 466, 18));
        }

        private void ExportConfig()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "导出配置";
                dlg.Filter = "配置文件 (*.json)|*.json";
                dlg.FileName = "work-countdown-settings.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                StoreSchedEditor();
                if (!Save(false)) return;
                var exported = store.Export(s, dlg.FileName);
                MessageBox.Show(this, exported.Succeeded ? "已导出到：\n" + dlg.FileName : "导出失败：\n" + exported.Message,
                    exported.Succeeded ? "导出完成" : "导出失败", MessageBoxButtons.OK,
                    exported.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }

        private void ImportConfig()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "导入配置";
                dlg.Filter = "配置文件 (*.json)|*.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var imported = store.Import(dlg.FileName);
                if (!imported.Succeeded)
                {
                    MessageBox.Show(this, "这个文件读不出来，换一份试试。\n" + imported.Message, "导入失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Settings before = s.Clone();
                s.CopyFrom(imported.Settings);
                s.Normalize();
                Themes.SetCustom(s.customThemes);
                var saved = store.Save(s);
                if (!saved.Succeeded)
                {
                    // 导入没有真正提交：回到导入前的草稿，调用者的运行配置完全没被碰过。
                    s.CopyFrom(before);
                    Themes.SetCustom(s.customThemes);
                    schedIndex = 0;
                    LoadValues();
                    ApplySkin();
                    if (saved.IsFailed)
                        MessageBox.Show(this, "导入没有保存成功：\n" + saved.Message + "\n\n上一份有效配置没有被改动。", "导入失败",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else
                        MessageBox.Show(this, "导入未提交：本次运行不写配置（WC_NO_SAVE=1）。", "导入未提交",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                schedIndex = 0;
                LoadValues();
                ApplySkin();
                MessageBox.Show(this, "配置已导入，重启悬浮窗后完全生效。", "导入完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ResetDefaults()
        {
            if (MessageBox.Show(this, "恢复默认会清掉现在的所有设置（含自建日程和主题），确定吗？", "恢复默认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Settings before = s.Clone();
            var fresh = new Settings();
            fresh.Normalize();
            s.CopyFrom(fresh);
            Themes.SetCustom(s.customThemes);
            var saved = store.Save(s);
            if (!saved.Succeeded)
            {
                // 没有真正提交：保留恢复默认之前的内容，调用者的运行配置不变。
                s.CopyFrom(before);
                Themes.SetCustom(s.customThemes);
                schedIndex = 0;
                LoadValues();
                ApplySkin();
                if (saved.IsFailed)
                    MessageBox.Show(this, "默认设置没有保存成功：\n" + saved.Message + "\n\n上一份有效配置没有被改动。", "保存失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    MessageBox.Show(this, "恢复默认未提交：本次运行不写配置（WC_NO_SAVE=1）。", "设置未提交",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            schedIndex = 0;
            LoadValues();
            ApplySkin();
        }

        // ---------------- 载入 / 保存 ----------------

        private void LoadValues()
        {
            editTextFont = string.IsNullOrWhiteSpace(s.ui.textFont) ? "" : s.ui.textFont;
            editNumberFont = string.IsNullOrWhiteSpace(s.ui.numberFont) ? "Consolas" : s.ui.numberFont;
            bool flex = s.mode == "flex";
            rbFlex.Checked = flex;
            rbFixed.Checked = !flex;
            tbStart.Text = s.startTime;
            tbEnd.Text = s.endTime;
            numFlexH.Value = Math.Max(0, Math.Min(16, s.flexMinutes / 60));
            numFlexM.Value = Math.Max(0, Math.Min(59, s.flexMinutes % 60));
            UpdateFlexState();

            for (int i = 0; i < 7; i++) cbDays[i].Checked = s.workdays[WorkdayArrayIndex(i)];
            RefreshExceptions();

            RefreshSchedules();

            cbLark.Checked = s.lark.enabled;
            tbChatId.Text = s.lark.chatId;
            tbBotName.Text = s.lark.botName;
            numPoll.Value = Math.Max(1, Math.Min(120, s.lark.pollMinutes));
            cbCalendar.Checked = s.calendarEnabled;
            numLead.Value = Math.Max(0, Math.Min(120, s.calendarLeadMinutes));
            numLeadEvent.Value = Math.Max(0, Math.Min(120, s.eventLeadMinutes));
            UpdateLarkState();
            RefreshEvents();
            UpdateEventEnd();

            if (numSalary != null)
            {
                numSalary.Value = Math.Max(0m, Math.Min(10000000m, s.salary.monthly));
                numPayday.Value = Math.Max(1, Math.Min(31, s.salary.payday));
                UpdateSalaryPreview();
            }

            numOpacity.Value = Math.Max(30, Math.Min(100, s.ui.opacity));
            numAutoCollapse.Value = Math.Max(0, Math.Min(120, s.ui.autoCollapseSeconds));
            cbTopMost.Checked = s.ui.alwaysOnTop;
            cbAutoStart.Checked = Startup.IsEnabled();
            cbShowLunch.Checked = s.ui.showLunch;
            cbReducedMotion.Checked = s.ui.reducedMotion;
            numReminderHold.Value = Math.Max(3, Math.Min(20, s.ui.reminderHoldSeconds));
            cmbGlow.SelectedIndex = s.ui.cardGlow == "corner" ? 1
                : s.ui.cardGlow == "bar" ? 2 : s.ui.cardGlow == "all" ? 3 : 0;
            if (rbBgSolid != null)
            {
                bool glass = Frost.On(s.ui);
                loadingArc = true;
                rbBgSolid.Checked = !glass;
                rbBgGlass.Checked = glass;
                loadingArc = false;
            }
            if (swArc != null)
            {
                loadingArc = true;
                swArc.Checked = s.ui.arcGlow;
                loadingArc = false;
                RefreshArcUi();
            }

            if (picker != null)
            {
                picker.SelectedId = s.ui.theme;
                picker.Invalidate();
            }
            if (skinPicker != null)
            {
                skinPicker.SelectedId = s.ui.skin;
                skinPicker.Invalidate();
            }
            UpdateThemeName();

            if (lstCalendars != null)
            {
                editCals = CloneCalendars(s.outlook.calendars);
                numOutlookLead.Value = Math.Max(0, Math.Min(120, s.outlook.leadMinutes));
                FillCalendarList();
            }
            for (int i = 0; i < StyleKinds.Length; i++)
            {
                ReminderStyleDef d = s.StyleOf(StyleKinds[i]);
                cmbSpread[i].SelectedIndex = d.spread == "screen" ? 1 : 0;
                cmbShape[i].SelectedIndex = d.shape == "tape" ? 2 : d.shape == "bar" ? 1 : 0;
                cmbColor[i].SelectedIndex = ColorIndex(d.color);
            }

            if (rbNoteDrawer != null)
            {
                if (s.note == null) s.note = new NoteSettings();
                s.note.Normalize();
                editNoteId = s.note.id;
                lblNoteId.Text = editNoteId;
                tbNoteNick.Text = s.note.nickname ?? "";
                rbNoteDrawer.Checked = s.note.style != "columns";
                rbNoteColumns.Checked = s.note.style == "columns";
                tbNoteRelay.Text = s.note.relayHost;
                numNotePort.Value = Math.Max(1, Math.Min(65535, s.note.relayPort));
                editNoteContacts = CloneNoteContacts(s.note.contacts);
                FillNoteContacts();
                lblNoteRelayState.Text = "还没测试";
                lblNoteRelayState.ForeColor = Ui.Muted;
            }
        }

        private bool Save()
        {
            return Save(true);
        }

        /// <summary>校验并写盘。closeAfter=false 时（导出前先存一份）只写盘不关窗。</summary>
        private bool Save(bool closeAfter)
        {
            int startMin, endMin;
            if (!SettingsExtensions.TryParseTime(tbStart.Text, out startMin))
            {
                SelectPage(0);
                MessageBox.Show(this, "上班时间的格式应为 HH:mm，例如 09:00。", "时间格式有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!SettingsExtensions.TryParseTime(tbEnd.Text, out endMin))
            {
                SelectPage(0);
                MessageBox.Show(this, "下班时间的格式应为 HH:mm，例如 18:00。", "时间格式有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // 固定日程里的时间也校验一下，别存进没法解析的字符串
            StoreSchedEditor();
            foreach (var f in s.schedules)
            {
                if (f == null || f.IsOffWork) continue;
                int probe;
                if (!SettingsExtensions.TryParseTime(f.start, out probe))
                {
                    SelectPage(1);
                    MessageBox.Show(this, "「" + f.name + "」的开始时间应为 HH:mm，例如 11:50。", "时间格式有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(f.end) && !SettingsExtensions.TryParseTime(f.end, out probe))
                {
                    SelectPage(1);
                    MessageBox.Show(this, "「" + f.name + "」的结束时间应为 HH:mm，留空表示只响一次。", "时间格式有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            int flex = (int)numFlexH.Value * 60 + (int)numFlexM.Value;
            if (rbFlex.Checked && flex <= 0)
            {
                MessageBox.Show(this, "弹性工时需要大于 0 的每日应勤时长。", "设置不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            s.mode = rbFixed.Checked ? "fixed" : "flex";
            s.startTime = SettingsExtensions.FormatTime(startMin);
            s.endTime = SettingsExtensions.FormatTime(endMin);
            s.flexMinutes = flex;
            for (int i = 0; i < 7; i++) s.workdays[WorkdayArrayIndex(i)] = cbDays[i].Checked;

            s.lark.enabled = cbLark.Checked;
            s.lark.chatId = tbChatId.Text.Trim();
            s.lark.botName = tbBotName.Text.Trim();
            s.lark.pollMinutes = (int)numPoll.Value;

            s.calendarEnabled = cbCalendar.Checked;
            s.calendarLeadMinutes = (int)numLead.Value;
            s.eventLeadMinutes = (int)numLeadEvent.Value;

            s.ui.opacity = (int)numOpacity.Value;
            s.ui.autoCollapseSeconds = (int)numAutoCollapse.Value;
            s.ui.theme = Themes.Get(picker.SelectedId).id;
            s.ui.skin = skinPicker != null ? Skins.Get(skinPicker.SelectedId).id : Skins.DefaultId;
            s.ui.alwaysOnTop = cbTopMost.Checked;
            s.ui.showLunch = cbShowLunch.Checked;
            s.ui.reducedMotion = cbReducedMotion.Checked;
            s.ui.reminderHoldSeconds = (int)numReminderHold.Value;
            string[] glowIds = new string[] { "classic", "corner", "bar", "all" };
            s.ui.cardGlow = glowIds[Math.Max(0, Math.Min(glowIds.Length - 1, cmbGlow.SelectedIndex))];
            if (rbBgGlass != null) s.ui.bgStyle = rbBgGlass.Checked ? Frost.Glass : Frost.Solid;
            if (swArc != null) s.ui.arcGlow = swArc.Checked;
            s.ui.textFont = editTextFont ?? "";
            s.ui.numberFont = string.IsNullOrWhiteSpace(editNumberFont) ? "Consolas" : editNumberFont;
            UiFont.Configure(s.ui.textFont, s.ui.numberFont);

            if (numSalary != null)
            {
                s.salary.monthly = numSalary.Value;
                s.salary.payday = (int)numPayday.Value;
            }

            if (rbNoteDrawer != null)
            {
                s.note.style = rbNoteColumns.Checked ? "columns" : "drawer";
                s.note.relayHost = tbNoteRelay.Text.Trim();
                s.note.relayPort = (int)numNotePort.Value;
                s.note.id = editNoteId;
                s.note.nickname = tbNoteNick.Text.Trim();
                s.note.contacts = CloneNoteContacts(editNoteContacts);
            }

            // 日程页：多份日历 + 四类日程各自的样式
            s.outlook.calendars = CloneCalendars(editCals);
            s.outlook.leadMinutes = (int)numOutlookLead.Value;
            // 老字段继续写第一份日历的值，降级回旧版本时还能读
            CalendarSource first = null;
            foreach (var c in editCals) { if (c.enabled && c.HasSource) { first = c; break; } }
            if (first == null && editCals.Count > 0) first = editCals[0];
            if (first != null)
            {
                s.outlook.source = first.source;
                s.outlook.url = first.url;
                s.outlook.path = first.path;
            }
            s.outlook.enabled = s.outlook.HasEnabled;
            string[] styleColorIds = ReminderStyles.Ids;
            for (int i = 0; i < StyleKinds.Length; i++)
            {
                int ci = Math.Max(0, Math.Min(styleColorIds.Length - 1, cmbColor[i].SelectedIndex));
                s.SetStyleOf(StyleKinds[i], new ReminderStyleDef(
                    cmbSpread[i].SelectedIndex == 1 ? "screen" : "card",
                    cmbShape[i].SelectedIndex == 2 ? "tape" : cmbShape[i].SelectedIndex == 1 ? "bar" : "circle",
                    styleColorIds[ci]));
            }
            if (cbAutoStart.Checked != Startup.IsEnabled()) Startup.SetEnabled(cbAutoStart.Checked);

            var saved = PersistDraft();
            if (saved.IsFailed)
            {
                // 保存失败不得关窗 / 不得表现为成功：明确告知并保留上一份有效配置。
                MessageBox.Show(this, "设置没有保存成功：\n" + saved.Message + "\n\n上一份有效配置没有被改动，可以修正后重试。", "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (saved.IsSkipped && closeAfter)
            {
                // WC_NO_SAVE 是探针语义：不能显示成功，也不能关窗假装已经提交。
                MessageBox.Show(this, "本次运行不写配置文件（WC_NO_SAVE=1），设置没有保存。", "设置未提交",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            ApplySkin();            // 设置窗口自己立刻按新皮肤重画（不重启窗口；悬浮窗由 Widget 在 OK 后重建）

            // closeAfter=false 只用于导出前取得一份通过校验的草稿：Skipped 时也允许继续导出，
            // 但这里不产生任何“保存成功”的提示。
            if (closeAfter)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            return true;
        }

        /// <summary>
        /// 把当前草稿规范化并写盘（不弹窗、不关窗）；Save() 在控件校验通过后调用它。
        /// 抽出来是为了让“保存失败保留编辑”的行为可以在不触发模态框的情况下验收。
        /// </summary>
        internal SettingsSaveResult PersistDraft()
        {
            s.Normalize();          // 固定日程同步回午休 / 晚休 / 下班的旧字段
            Themes.SetCustom(s.customThemes);
            return store.Save(s);
        }

        // ---------------- 小工具 ----------------

        private static CardPanel CardBox(TabPage page, string title, int x, int y, int w, int h)
        {
            var card = new CardPanel();
            card.Text = title;
            card.SetBounds(x, y, w, h);
            page.Controls.Add(card);
            return card;
        }

        private static Label Lbl(string text, int x, int y, int w, int h)
        {
            var l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, h);
            l.ForeColor = Ui.Ink;
            l.BackColor = Color.Transparent;
            return l;
        }

        private static Label Hint(string text, int x, int y, int w, int h)
        {
            var l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, h);
            l.ForeColor = Ui.Muted;
            l.BackColor = Color.Transparent;
            l.Tag = "hint";
            return l;
        }

        private static TextBox Tb(string text, int x, int y, int w)
        {
            var t = new TextBox();
            t.Text = text;
            t.SetBounds(x, y, w, 24);
            t.TextAlign = HorizontalAlignment.Center;
            return t;
        }

        private static NumericUpDown Num(int value, int x, int y, int w, int min, int max)
        {
            var n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.SetBounds(x, y, w, 24);
            n.TextAlign = HorizontalAlignment.Center;
            return n;
        }

        private static Button FlatButton(string text, int x, int y, int w, int h)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderColor = Ui.Edge;
            b.BackColor = Ui.Surface;
            b.ForeColor = Ui.Ink;
            return b;
        }

        /// <summary>强调色按钮（描边 + 强调色文字），换肤时由 ApplySkin 统一刷色。</summary>
        private static Button AccentButton(string text, int x, int y, int w, int h)
        {
            var b = FlatButton(text, x, y, w, h);
            b.Tag = "accent";
            b.ForeColor = Ui.Accent;
            b.FlatAppearance.BorderColor = Ui.Accent;
            return b;
        }

        /// <summary>提醒配色下拉：每项左边画一条两层色带，直接看出换上去什么样。</summary>
        private ComboBox StyleCombo(string current, int x, int y, int w)
        {
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.DrawMode = DrawMode.OwnerDrawFixed;
            c.ItemHeight = 22;
            foreach (var id in ReminderStyles.Ids) c.Items.Add(ReminderStyles.ShortNameOf(id));
            c.DropDownHeight = 336;
            c.DropDownWidth = 168;
            var seed = Themes.Get(picker != null ? picker.SelectedId : s.ui.theme);
            c.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= ReminderStyles.Ids.Length) return;
                using (var bg = new SolidBrush(Ui.InputBg)) e.Graphics.FillRectangle(bg, e.Bounds);
                Color a, b, ink;
                ReminderStyles.Resolve(ReminderStyles.Ids[e.Index], seed, out a, out b, out ink);
                var band = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 4, 26, e.Bounds.Height - 8);
                using (var br = new SolidBrush(a)) e.Graphics.FillRectangle(br, band.X, band.Y, band.Width, band.Height / 2);
                using (var br = new SolidBrush(b)) e.Graphics.FillRectangle(br, band.X, band.Y + band.Height / 2, band.Width, band.Height - band.Height / 2);
                using (var br = new SolidBrush(Ui.Ink))
                    e.Graphics.DrawString(c.Items[e.Index].ToString(), e.Font, br, e.Bounds.X + 36, e.Bounds.Y + 3);
                e.DrawFocusRectangle();
            };
            c.SetBounds(x, y, w, 24);
            for (int i = 0; i < ReminderStyles.Ids.Length; i++)
                if (ReminderStyles.Ids[i] == current) { c.SelectedIndex = i; break; }
            return c;
        }


    }

    /// <summary>添加 / 编辑一份订阅日历：名称、来源（.ics 链接 / 本地文件 / 登录 Outlook）、提前量。</summary>
    internal class CalendarEditForm : Form
    {
        private readonly bool isNew;
        private readonly bool graphLogged;
        private readonly CalendarSource seed;
        private TextBox tbName;
        private TextBox tbAddress;
        private ComboBox cmbSource;
        private NumericUpDown numLead;
        private Button btnBrowse;
        private Label lblHint;

        public CalendarSource Result;

        public CalendarEditForm(CalendarSource seed, bool isNew, bool graphLogged)
        {
            this.seed = seed;
            this.isNew = isNew;
            this.graphLogged = graphLogged;

            Text = isNew ? "添加订阅日历" : "编辑订阅日历";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 286);
            BackColor = Ui.WindowBg;
            Font = UiFont.Make(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;

            var l1 = new Label();
            l1.Text = "名称";
            l1.SetBounds(14, 16, 60, 20);
            l1.ForeColor = Ui.Ink;
            Controls.Add(l1);
            tbName = new TextBox();
            tbName.SetBounds(80, 14, 346, 24);
            tbName.Text = seed.name;
            Controls.Add(tbName);

            var l2 = new Label();
            l2.Text = "来源";
            l2.SetBounds(14, 54, 60, 20);
            l2.ForeColor = Ui.Ink;
            Controls.Add(l2);
            cmbSource = new ComboBox();
            cmbSource.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSource.Items.Add("订阅地址（.ics）");
            cmbSource.Items.Add("本地 .ics");
            cmbSource.Items.Add("登录 Outlook（Graph）");
            cmbSource.SetBounds(80, 52, 180, 24);
            cmbSource.SelectedIndex = seed.source == "file" ? 1 : seed.source == "graph" ? 2 : 0;
            cmbSource.SelectedIndexChanged += delegate { UpdateSourceState(); };
            Controls.Add(cmbSource);

            var l3 = new Label();
            l3.Text = "提前";
            l3.SetBounds(272, 54, 36, 20);
            l3.ForeColor = Ui.Ink;
            Controls.Add(l3);
            numLead = new NumericUpDown();
            numLead.Minimum = 0;
            numLead.Maximum = 120;
            numLead.Value = Math.Max(0, Math.Min(120, seed.leadMinutes));
            numLead.SetBounds(310, 52, 50, 24);
            numLead.TextAlign = HorizontalAlignment.Center;
            Controls.Add(numLead);
            var l4 = new Label();
            l4.Text = "分（0=默认）";
            l4.SetBounds(364, 56, 76, 20);
            l4.ForeColor = Ui.Muted;
            Controls.Add(l4);

            var l5 = new Label();
            l5.Text = "地址";
            l5.SetBounds(14, 92, 60, 20);
            l5.ForeColor = Ui.Ink;
            Controls.Add(l5);
            tbAddress = new TextBox();
            tbAddress.SetBounds(80, 90, 260, 24);
            Controls.Add(tbAddress);
            btnBrowse = new Button();
            btnBrowse.Text = "选文件";
            btnBrowse.SetBounds(348, 88, 78, 28);
            btnBrowse.FlatStyle = FlatStyle.Flat;
            btnBrowse.FlatAppearance.BorderColor = Ui.Edge;
            btnBrowse.BackColor = Ui.Surface;
            btnBrowse.Click += delegate { BrowseFile(); };
            Controls.Add(btnBrowse);

            lblHint = new Label();
            lblHint.SetBounds(14, 124, 412, 46);
            lblHint.ForeColor = Ui.Muted;
            Controls.Add(lblHint);

            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.SetBounds(224, 232, 96, 32);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderColor = Ui.Edge;
            btnCancel.BackColor = Ui.Surface;
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            var btnOk = new Button();
            btnOk.Text = "确定";
            btnOk.SetBounds(330, 232, 96, 32);
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.BackColor = Ui.Accent;
            btnOk.ForeColor = Ui.AccentInk;
            btnOk.Click += delegate { Confirm(); };
            Controls.Add(btnOk);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            UpdateSourceState();
            FitDpi();
        }

        private void FitDpi()
        {
            float k;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            if (Math.Abs(k - 1f) < 0.01f) return;
            SuspendLayout();
            Scale(new SizeF(k, k));
            ResumeLayout(true);
        }

        private void UpdateSourceState()
        {
            int src = cmbSource.SelectedIndex;
            bool file = src == 1;
            bool graph = src == 2;
            if (tbAddress.Tag as string != (file ? "file" : "url"))
            {
                tbAddress.Tag = file ? "file" : "url";
                tbAddress.Text = file ? seed.path : seed.url;
            }
            tbAddress.Visible = !graph;
            btnBrowse.Visible = file;
            if (graph)
            {
                lblHint.Text = graphLogged
                    ? "使用已登录的 Outlook 账号读主日历；登录状态在「Outlook 账号」里管理。"
                    : "还没登录 Outlook。保存后点「Outlook 登录」，登录成功会自动加一份登录日历。";
            }
            else
            {
                lblHint.Text = file
                    ? "选一个 Outlook 导出的 .ics 文件；文件更新后同步时会重新读。"
                    : "Outlook 网页版 → 设置 → 日历 → 共享日历 → 发布日历，把 .ics 链接粘到这里。";
            }
        }

        private void BrowseFile()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 Outlook 导出的 .ics 文件";
                dlg.Filter = "iCalendar 文件 (*.ics)|*.ics|所有文件 (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                cmbSource.SelectedIndex = 1;
                tbAddress.Text = dlg.FileName;
            }
        }

        private void Confirm()
        {
            string name = tbName.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "给这份日历起个名字吧。", "还差一点", MessageBoxButtons.OK, MessageBoxIcon.Information);
                tbName.Focus();
                return;
            }
            int src = cmbSource.SelectedIndex;
            string address = tbAddress.Text.Trim();
            if (src != 2 && address.Length == 0)
            {
                MessageBox.Show(this, src == 1 ? "先选一个 .ics 文件。" : "先填发布日历给的 .ics 链接。",
                    "还没填地址", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var r = new CalendarSource();
            r.id = seed.id;
            r.name = name;
            r.enabled = seed.enabled;
            r.source = src == 1 ? "file" : src == 2 ? "graph" : "url";
            r.url = src == 0 ? address : "";
            r.path = src == 1 ? address : "";
            r.leadMinutes = (int)numLead.Value;
            r.lastSync = seed.lastSync;
            r.lastError = seed.lastError;
            r.cached = seed.cached == null ? new List<ReminderEvent>() : new List<ReminderEvent>(seed.cached);
            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Outlook 登录：设备码流程 + 退出登录。登录成功会把刷新令牌 DPAPI 加密存进设置。</summary>
    internal class OutlookLoginForm : Form
    {
        private readonly Settings s;
        private readonly Settings original;   // 立即登录 / 退出登录也要同步到调用者的运行配置（保持既有即时契约）
        private readonly SettingsStore store;
        private Label lblAccount;
        private TextBox tbClientId;
        private Label lblState;
        private Button btnLogin;

        public OutlookLoginForm(Settings s, Settings original, SettingsStore store)
        {
            this.s = s;
            this.original = original;
            this.store = store;

            Text = "Outlook 登录";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 300);
            BackColor = Ui.WindowBg;
            Font = UiFont.Make(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;

            lblAccount = new Label();
            lblAccount.SetBounds(14, 16, 432, 22);
            lblAccount.ForeColor = Ui.Ink;
            Controls.Add(lblAccount);

            var l1 = new Label();
            l1.Text = "client ID";
            l1.SetBounds(14, 52, 70, 20);
            l1.ForeColor = Ui.Ink;
            Controls.Add(l1);
            tbClientId = new TextBox();
            tbClientId.SetBounds(80, 50, 366, 24);
            tbClientId.Text = s.outlook.clientId;
            Controls.Add(tbClientId);

            var hint = new Label();
            hint.SetBounds(14, 82, 432, 52);
            hint.ForeColor = Ui.Muted;
            hint.Text = "Azure 应用注册成公共客户端，并授予 Microsoft Graph 的 Calendars.Read 权限。" +
                "\r\n点「登录 Outlook」后浏览器会打开，把复制的设备码粘进去授权即可。";
            Controls.Add(hint);

            lblState = new Label();
            lblState.SetBounds(14, 142, 432, 54);
            lblState.ForeColor = Ui.Ink;
            Controls.Add(lblState);

            btnLogin = new Button();
            btnLogin.SetBounds(14, 218, 150, 34);
            btnLogin.FlatStyle = FlatStyle.Flat;
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.BackColor = Ui.Accent;
            btnLogin.ForeColor = Ui.AccentInk;
            btnLogin.Click += delegate { ToggleLogin(); };
            Controls.Add(btnLogin);

            var btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.SetBounds(350, 218, 96, 34);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderColor = Ui.Edge;
            btnClose.BackColor = Ui.Surface;
            btnClose.DialogResult = DialogResult.Cancel;
            Controls.Add(btnClose);
            CancelButton = btnClose;

            UpdateUi();
            FitDpi();
        }

        private void FitDpi()
        {
            float k;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            if (Math.Abs(k - 1f) < 0.01f) return;
            SuspendLayout();
            Scale(new SizeF(k, k));
            ResumeLayout(true);
        }

        /// <summary>登录 / 退出登录是既有即时设置：把认证字段同步到调用者的运行配置，取消设置窗也不回退。</summary>
        private void MirrorAuthToOriginal()
        {
            if (original == null || original.outlook == null || s.outlook == null) return;
            original.outlook.clientId = s.outlook.clientId;
            original.outlook.tenant = s.outlook.tenant;
            original.outlook.account = s.outlook.account;
            original.outlook.refreshToken = s.outlook.refreshToken;
            original.outlook.tokenAt = s.outlook.tokenAt;
            original.outlook.lastError = s.outlook.lastError;
        }

        private void UpdateUi()
        {
            bool logged = !string.IsNullOrWhiteSpace(s.outlook.refreshToken);
            string account = string.IsNullOrWhiteSpace(s.outlook.account) ? "未登录" : s.outlook.account;
            lblAccount.Text = "账号：" + account +
                (string.IsNullOrEmpty(s.outlook.tokenAt) ? "" : " · 授权于 " + s.outlook.tokenAt);
            btnLogin.Text = logged ? "退出登录" : "登录 Outlook";
            if (logged) lblState.Text = "已登录，可以关闭这个窗口。";
            else if (string.IsNullOrEmpty(lblState.Text) || lblState.Text == "已登录，可以关闭这个窗口。")
                lblState.Text = "还没登录。";
        }

        private void ToggleLogin()
        {
            if (!string.IsNullOrWhiteSpace(s.outlook.refreshToken))
            {
                if (MessageBox.Show(this, "退出 Outlook 登录？之后不再同步 Graph 日历。", "退出登录",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                s.outlook.refreshToken = "";
                s.outlook.account = "";
                s.outlook.tokenAt = "";
                s.outlook.lastError = "";
                MirrorAuthToOriginal();
                var saved = store.Save(s);
                lblState.Text = saved.IsFailed
                    ? "已退出登录，但配置没有保存成功：" + saved.Message
                    : saved.IsSkipped ? "已退出登录（本次运行不写配置）。" : "已退出登录。";
                UpdateUi();
                return;
            }

            string cid = tbClientId.Text.Trim();
            if (cid.Length == 0)
            {
                MessageBox.Show(this, OutlookGraph.SetupHint, "先填 client ID", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            s.outlook.clientId = cid;
            OutlookGraph.DeviceCodeInfo info;
            string err;
            lblState.Text = "正在向微软要设备码…";
            if (!OutlookGraph.StartDeviceCode(cid, s.outlook.tenant, 20000, out info, out err))
            {
                lblState.Text = "要设备码失败：" + err;
                return;
            }
            try { Clipboard.SetText(info.userCode); } catch { }
            try { if (!string.IsNullOrEmpty(info.verifyUrl)) Process.Start(info.verifyUrl); } catch { }
            lblState.Text = "浏览器里输入代码 " + info.userCode + "（已复制），授权后这里会自动变。";

            string deviceCode = info.deviceCode;
            int waitMs = Math.Min(600, Math.Max(90, info.expiresIn)) * 1000;
            int interval = info.interval;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                OutlookGraph.TokenInfo tk;
                string perr;
                bool ok = OutlookGraph.PollToken(cid, s.outlook.tenant, deviceCode, interval, waitMs, out tk, out perr);
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!ok)
                        {
                            s.outlook.lastError = perr;
                            lblState.Text = "登录失败：" + perr;
                            return;
                        }
                        s.outlook.refreshToken = OutlookGraph.Protect(tk.refreshToken);
                        s.outlook.tokenAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                        string name, mail, meErr;
                        OutlookGraph.FetchMe(tk.accessToken, 15000, out name, out mail, out meErr);
                        s.outlook.account = mail.Length > 0 ? mail : name;
                        s.outlook.lastError = "";
                        MirrorAuthToOriginal();
                        var saved = store.Save(s);
                        UpdateUi();
                        lblState.Text = saved.IsFailed
                            ? "登录成功，但配置没有保存成功：" + saved.Message
                            : saved.IsSkipped ? "登录成功（本次运行不写配置）。关闭后回到设置页点「立即同步」即可。"
                            : "登录成功。关闭后回到设置页点「立即同步」即可。";
                    });
                }
                catch { }
            });
        }
    }

    /// <summary>
    /// 设置窗口 / 对话框的共用色。以前是一组 readonly 浅色，现在由 Apply(ui) 按当前皮肤刷新：
    /// 默认皮肤算出来的值和 v3.6 完全一样，其它五套换成 SkinDef 里那套令牌。
    /// </summary>
    internal static class Ui
    {
        public static readonly Color AccentDefault = Color.FromArgb(64, 124, 245);

        public static SkinDef Skin = Skins.Get(Skins.DefaultId);
        public static ThemeDef Theme = Themes.Get("midnight");

        public static Color Accent = AccentDefault;
        public static Color AccentInk = Color.White;
        public static Color Ink = Color.FromArgb(38, 46, 64);
        public static Color Muted = Color.FromArgb(120, 128, 146);
        public static Color Dim = Color.FromArgb(160, 168, 182);
        public static Color WindowBg = Color.FromArgb(246, 248, 251);
        public static Color WindowEdge = Color.FromArgb(226, 231, 239);
        public static Color CardBg = Color.White;
        public static Color CardBorder = Color.FromArgb(230, 234, 242);
        public static Color Surface = Color.White;          // 卡片表面（无垠 = 窗口底：不填卡片底）
        public static Color Edge = Color.FromArgb(214, 220, 232);
        public static Color InputBg = Color.White;
        public static Color InputInk = Color.Black;
        public static Color Hover = Color.FromArgb(240, 243, 249);
        public static Color FooterBg = Color.White;
        public static Color RailBg = Color.FromArgb(238, 241, 246);
        public static Color RailActive = Color.White;
        public static Color RailHover = Color.FromArgb(244, 246, 250);
        public static Color SealBg = Color.Transparent;
        public static Color SealInk = Color.White;
        // 自绘列表（固定日程 / 日程小卡片）里的行色
        public static Color RowBg = Color.FromArgb(250, 251, 253);
        public static Color RowActive = Color.FromArgb(240, 245, 255);
        public static Color RowText = Color.FromArgb(38, 46, 64);
        public static Color RowSub = Color.FromArgb(120, 128, 146);
        public static Color RowBorder = Color.FromArgb(226, 231, 240);
        public static Color Track = Color.FromArgb(238, 241, 246);
        public static Color Thumb = Color.FromArgb(188, 196, 210);
        public static ThemeDef RowTheme = Themes.Get("midnight");

        public static int RadiusCard = 10;
        public static int RadiusWindow = 12;
        public static int RadiusPill = 20;
        public static bool HairlineCards;
        public static bool Screws;
        public static bool NeonEdge;
        public static bool Seal;
        public static double Sheen;
        public static string HeadingFont;

        public static void Apply(UiSettings ui)
        {
            var skin = Skins.Current(ui);
            var theme = Skins.ResolveTheme(ui);
            Skin = skin;
            Theme = theme;
            bool def = skin.IsDefault;

            Accent = def ? AccentDefault : theme.accent;
            AccentInk = skin.accentInk;
            Ink = skin.windowInk;
            Muted = skin.windowMuted;
            Dim = Skins.Mix(skin.windowMuted, skin.windowBg, 0.35);
            WindowBg = skin.windowBg;
            WindowEdge = skin.windowEdge;
            CardBg = skin.cardBg;
            CardBorder = skin.cardEdge;
            Surface = skin.hairlineCards ? skin.windowBg : skin.cardBg;
            Edge = def ? Color.FromArgb(214, 220, 232) : skin.windowEdge;
            InputBg = def ? Color.White : Skins.Mix(skin.cardBg, skin.windowBg, 0.45);
            InputInk = def ? Color.Black : skin.windowInk;
            Hover = Skins.Mix(skin.windowBg, Accent, def ? 0.05 : 0.12);
            FooterBg = skin.footerBg;
            RailBg = skin.railBg;
            RailActive = skin.railActiveBg;
            RailHover = Skins.Mix(skin.railActiveBg, skin.railBg, 0.55);
            SealBg = skin.seal ? (skin.colors != null ? skin.colors.accent : theme.accent) : Color.Transparent;
            SealInk = skin.accentInk;

            RowBg = def ? Color.FromArgb(250, 251, 253) : Skins.Mix(skin.cardBg, skin.windowInk, 0.05);
            RowActive = def ? Color.FromArgb(240, 245, 255) : Skins.Mix(skin.cardBg, Accent, 0.14);
            RowText = def ? Color.FromArgb(38, 46, 64) : skin.windowInk;
            RowSub = def ? Color.FromArgb(120, 128, 146) : skin.windowMuted;
            RowBorder = def ? Color.FromArgb(226, 231, 240) : skin.windowEdge;
            Track = def ? Color.FromArgb(238, 241, 246) : Skins.Mix(skin.cardBg, skin.windowBg, 0.5);
            Thumb = def ? Color.FromArgb(188, 196, 210) : Skins.Mix(skin.windowMuted, skin.cardBg, 0.4);
            RowTheme = def ? Themes.Get("midnight") : theme;

            RadiusCard = skin.radiusCard;
            RadiusWindow = skin.radiusWindow;
            RadiusPill = skin.radiusPill;
            HairlineCards = skin.hairlineCards;
            Screws = skin.screws;
            NeonEdge = skin.neonEdge;
            Seal = skin.seal;
            Sheen = skin.sheenAlpha;
            HeadingFont = skin.headingFont;
        }

        /// <summary>圆角矩形路径；半径 0 时退化成直角矩形。</summary>
        public static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            int max = Math.Max(0, Math.Min(r.Width, r.Height) / 2);
            radius = Math.Max(0, Math.Min(radius, max));
            var path = new GraphicsPath();
            if (radius <= 0) { path.AddRectangle(r); path.CloseFigure(); return path; }
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// 设置页里的分组卡片。默认皮肤还是白底 + 1px 边 + 10 DIP 圆角；
    /// 无垠只画一条顶部细线不填底，云母加顶部高光，仪表画四角螺丝，霓虹加品红辉光边。
    /// </summary>
    public class CardPanel : GroupBox
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;   // 白底上的小字用 ClearType 才实
            float k = g.DpiX / 96f;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Max(1, (int)Math.Round(Ui.RadiusCard * k));

            using (var path = Ui.RoundPath(r, radius))
            {
                if (Ui.HairlineCards)
                {
                    // 无垠：不填卡片底，只留一条 1px 分组线（和设计稿的 .sc 一样）
                    using (var b = new SolidBrush(Ui.WindowBg)) g.FillPath(b, path);
                    using (var p = new Pen(Ui.WindowEdge, Math.Max(1f, k)))
                        g.DrawLine(p, r.X, 0.5f, r.Right, 0.5f);
                }
                else
                {
                    using (var b = new SolidBrush(Ui.CardBg)) g.FillPath(b, path);
                    if (Ui.Sheen > 0.001)
                    {
                        // 顶部高光：云母 / 宣纸的「质感」来源
                        var sr = new Rectangle(0, 0, Width, Math.Max(2, (int)(Height * 0.45f)));
                        var state = g.Save();
                        g.SetClip(path, CombineMode.Intersect);
                        using (var br = new LinearGradientBrush(sr,
                            Skins.Alpha(Color.White, Ui.Sheen), Color.FromArgb(0, Color.White), 90f))
                            g.FillRectangle(br, sr);
                        g.Restore(state);
                    }
                    using (var p = new Pen(Ui.CardBorder, Math.Max(1f, k))) g.DrawPath(p, path);
                    if (Ui.NeonEdge)
                    {
                        using (var p = new Pen(Skins.Alpha(Ui.Theme.accent, 0.18), 3f * k)) g.DrawPath(p, path);
                        using (var p = new Pen(Skins.Alpha(Ui.Theme.accent, 0.55), 1.4f * k)) g.DrawPath(p, path);
                    }
                }
            }

            if (Ui.Screws) PaintScrews(g, k);

            using (var f = UiFont.MakeFamily(Ui.HeadingFont, 9.5f, FontStyle.Bold))
            using (var b = new SolidBrush(Ui.Ink))
                g.DrawString(Text, f, b, 14, 6);
            g.SmoothingMode = SmoothingMode.Default;
        }

        /// <summary>仪表皮肤：四角小螺丝。</summary>
        private void PaintScrews(Graphics g, float k)
        {
            float rad = 2.1f * k;
            float inset = 7f * k;
            Color body = Skins.Mix(Ui.CardBg, Ui.Ink, 0.32);
            Color slot = Skins.Mix(Ui.CardBg, Color.White, 0.55);
            PointF[] pts = new PointF[]
            {
                new PointF(inset, inset), new PointF(Width - inset, inset),
                new PointF(inset, Height - inset), new PointF(Width - inset, Height - inset)
            };
            foreach (var p in pts)
            {
                using (var b = new SolidBrush(body))
                    g.FillEllipse(b, p.X - rad, p.Y - rad, rad * 2, rad * 2);
                using (var pen = new Pen(slot, Math.Max(1f, 0.9f * k)))
                    g.DrawLine(pen, p.X - rad * 0.55f, p.Y, p.X + rad * 0.55f, p.Y);
            }
        }
    }

    /// <summary>主题色卡：一格一套配色，底色为主、高亮色压一条底边。非默认皮肤时置灰并可点。</summary>
    public class ThemePicker : Control
    {
        private string selectedId = "";
        public event EventHandler SelectedIdChanged;
        /// <summary>非默认皮肤：色卡不可点、画成置灰（提示「该风格自带配色」）。</summary>
        public bool Locked;

        public ThemePicker()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        public string SelectedId
        {
            get { return selectedId; }
            set { selectedId = value == null ? "" : value; Invalidate(); }
        }

        private Rectangle ChipRect(int index)
        {
            int n = Themes.All.Length;
            if (n <= 0) return Rectangle.Empty;
            int cols = 10;
            int rows = (n + cols - 1) / cols;
            int gap = 6, pad = 2;
            int cw = Math.Max(8, (Width - pad * 2 - (cols - 1) * gap) / cols);
            int ch = Math.Max(8, (Height - pad * 2 - (rows - 1) * gap) / rows);
            int row = index / cols, col = index % cols;
            return new Rectangle(pad + col * (cw + gap), pad + row * (ch + gap), cw, ch);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;   // 白底上的小字用 ClearType 才实
            g.Clear(BackColor);
            for (int i = 0; i < Themes.All.Length; i++)
            {
                var t = Themes.All[i];
                Rectangle r = ChipRect(i);
                if (r.Bottom > Height) break;
                Color top = t.bgTop, bottom = t.bgBottom, accent = t.accent;
                if (Locked)
                {
                    top = Skins.Mix(t.bgTop, Ui.CardBg, 0.62);
                    bottom = Skins.Mix(t.bgBottom, Ui.CardBg, 0.62);
                    accent = Skins.Mix(t.accent, Ui.CardBg, 0.55);
                }
                using (var path = Ui.RoundPath(r, 5))
                {
                    using (var b = new LinearGradientBrush(r, top, bottom, 90f)) g.FillPath(b, path);
                    bool sel = !Locked && t.id == selectedId;
                    using (var p = new Pen(sel ? Ui.Accent : Ui.CardBorder, sel ? 2f : 1f))
                        g.DrawPath(p, path);
                }
                int barH = Math.Max(3, r.Height / 5);
                using (var b = new SolidBrush(accent))
                    g.FillRectangle(b, r.X + 2, r.Bottom - barH - 2, r.Width - 4, barH);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Locked || !Enabled) return;
            int index = HitTest(e.Location);
            if (index < 0) return;
            SelectedId = Themes.All[index].id;
            Invalidate();
            if (SelectedIdChanged != null) SelectedIdChanged(this, EventArgs.Empty);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < Themes.All.Length; i++)
                if (ChipRect(i).Contains(p)) return i;
            return -1;
        }
    }

    /// <summary>
    /// 外观页「界面风格」：默认 + 5 套皮肤，一排 6 个可点的风格块（缩略配色 + 名字 + 一句定位）。
    /// 选中画 2px 强调色描边；点一下由 SettingsForm 立刻预览（只改内存里的 ui.skin）。
    /// </summary>
    public class SkinPicker : Control
    {
        private string selectedId = Skins.DefaultId;
        private int hover = -1;
        public event EventHandler SelectedIdChanged;

        public SkinPicker()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
        }

        public string SelectedId
        {
            get { return selectedId; }
            set { selectedId = Skins.Get(value).id; Invalidate(); }
        }

        /// <summary>6 个风格块（顺序 = Skins.All：默认 / 无垠 / 云母 / 宣纸 / 仪表 / 霓虹）。</summary>
        public SkinDef[] Items { get { return Skins.All; } }

        /// <summary>第 index 个风格块的命中矩形（给验收/探针反射用）。</summary>
        public Rectangle RectOf(int index)
        {
            int n = Skins.All.Length;
            if (n <= 0 || index < 0 || index >= n) return Rectangle.Empty;
            var g = CreateGraphics();
            try
            {
                float k = g.DpiX / 96f;
                int gap = (int)Math.Round(6 * k);
                int w = Math.Max(1, (Width - (n - 1) * gap) / n);
                int h = Math.Max(1, Math.Min(Height, (int)Math.Round(60 * k)));
                return new Rectangle(index * (w + gap), 0, w, h);
            }
            finally { g.Dispose(); }
        }

        /// <summary>点在哪一块上（-1 = 空白）。</summary>
        public int HitTest(Point p)
        {
            for (int i = 0; i < Skins.All.Length; i++)
                if (RectOf(i).Contains(p)) return i;
            return -1;
        }

        /// <summary>每个风格块底下的一句定位（比 SkinDef.tagline 短，72 DIP 宽放得下）。</summary>
        private static string ShortTag(SkinDef s)
        {
            if (s == null) return "";
            switch (s.id)
            {
                case "quiet": return "近黑细线";
                case "mica": return "磨砂玻璃";
                case "paper": return "宣纸朱砂";
                case "instrument": return "金属琥珀";
                case "neon": return "品红霓虹";
                default: return Themes.All.Length + " 套主题";
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);
            float k = g.DpiX / 96f;
            var all = Skins.All;
            for (int i = 0; i < all.Length; i++)
            {
                var skin = all[i];
                Rectangle r = RectOf(i);
                if (r.Width <= 2 || r.Height <= 2) continue;
                bool sel = skin.id == selectedId;
                bool hov = i == hover;

                int radius = Math.Max(2, (int)Math.Round(6 * k));
                using (var path = Ui.RoundPath(r, radius))
                {
                    using (var b = new SolidBrush(skin.windowBg)) g.FillPath(b, path);
                    // 上半截是这套皮肤的「窗口底 → 卡片底」渐变，底边压一条强调色
                    int stripH = Math.Max(6, (int)Math.Round(26 * k));
                    var strip = new Rectangle(r.X, r.Y, r.Width, stripH);
                    var state = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    Color bgTop = skin.colors != null ? skin.colors.bgTop : Ui.Theme.bgTop;
                    Color bgBottom = skin.colors != null ? skin.colors.bgBottom : Ui.Theme.bgBottom;
                    using (var b = new LinearGradientBrush(strip, bgTop, bgBottom, 90f)) g.FillRectangle(b, strip);
                    Color accent = skin.colors != null ? skin.colors.accent : Ui.Theme.accent;
                    Color accent2 = skin.accent2.IsEmpty ? accent : skin.accent2;
                    using (var b = new LinearGradientBrush(
                        new Rectangle(strip.X, strip.Bottom - Math.Max(2, (int)(3 * k)), strip.Width, Math.Max(2, (int)(3 * k))),
                        accent, accent2, 0f))
                        g.FillRectangle(b, strip.X, strip.Bottom - Math.Max(2, (int)(3 * k)),
                            strip.Width, Math.Max(2, (int)(3 * k)));
                    // 缩略图左上角一个小圆点（和设计稿的色卡一样）
                    float dot = 2.2f * k;
                    using (var b = new SolidBrush(accent))
                        g.FillEllipse(b, r.X + 5 * k, r.Y + 5 * k, dot * 2, dot * 2);
                    g.Restore(state);
                }

                using (var f = UiFont.MakeFamily(skin.headingFont, 9f, FontStyle.Bold))
                using (var b = new SolidBrush(skin.windowInk))
                {
                    var nr = new RectangleF(r.X, r.Y + 25 * k, r.Width, 16 * k);
                    var sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(skin.name, f, b, nr, sf);
                }
                using (var f = UiFont.MakeFamily(skin.headingFont, 7.5f, FontStyle.Regular))
                using (var b = new SolidBrush(skin.windowMuted))
                {
                    var tr = new RectangleF(r.X, r.Y + 40 * k, r.Width, 14 * k);
                    var sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(ShortTag(skin), f, b, tr, sf);
                }

                int border = sel || hov ? 2 : 1;
                using (var path = Ui.RoundPath(r, radius))
                using (var p = new Pen(sel ? Ui.Accent : (hov ? Skins.Mix(Ui.Accent, Ui.Muted, 0.45) : Ui.Edge), border * k))
                    g.DrawPath(p, path);
            }
            g.SmoothingMode = SmoothingMode.Default;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != -1) { hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = HitTest(e.Location);
            if (index < 0) return;
            string id = Skins.All[index].id;
            if (id == selectedId) return;
            selectedId = id;
            Invalidate();
            if (SelectedIdChanged != null) SelectedIdChanged(this, EventArgs.Empty);
        }
    }


    /// <summary>
    /// 弧光开关：50×28 的自绘胶囊。轨道用当前的派生色做渐变（关的时候是界面强调色），
    /// 关 / 开只换轨道和滑块位置，不用普通 CheckBox 那种系统样式。
    /// </summary>
    public class ArcSwitch : Control
    {
        public bool Checked;
        public ArcPalette Palette;

        public override bool AutoSize { get { return false; } }

        public ArcSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(52, 28);
            Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            Checked = !Checked;
            Invalidate();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = g.DpiX / 96f;
            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Max(1, (int)Math.Round(box.Height / 2f));
            using (var path = Ui.RoundPath(box, radius))
            {
                if (Checked && Palette != null)
                {
                    var inner = new Rectangle(box.X, box.Y, box.Width, box.Height);
                    using (var brush = new LinearGradientBrush(inner, Palette.Arc1, Palette.Arc4, 0f))
                    {
                        var blend = new ColorBlend();
                        blend.Colors = new Color[] { Palette.Arc1, Palette.Arc2, Palette.Arc4 };
                        blend.Positions = new float[] { 0f, 0.55f, 1f };
                        brush.InterpolationColors = blend;
                        g.FillPath(brush, path);
                    }
                    using (var p = new Pen(Skins.Alpha(Palette.Arc2, 0.55), Math.Max(1f, k))) g.DrawPath(p, path);
                }
                else
                {
                    // 关掉的轨道不能是纯白：浅色界面下会和卡片底连成一片，看不出这是个开关
                    using (var b = new SolidBrush(Checked ? Ui.Accent : Skins.Mix(Ui.Surface, Ui.Ink, 0.12)))
                        g.FillPath(b, path);
                    using (var p = new Pen(Ui.Edge, Math.Max(1f, k))) g.DrawPath(p, path);
                }
                if (!Enabled)
                    using (var b = new SolidBrush(Skins.Alpha(Ui.WindowBg, 0.55))) g.FillPath(b, path);
            }

            int knob = Math.Max(6, (int)Math.Round(Math.Min(box.Height, box.Width) * 0.60f));
            int margin = Math.Max(2, (int)Math.Round((box.Height - knob) / 2f));
            int x = Checked ? box.Right - knob - margin : box.X + margin;
            var knobRect = new Rectangle(x, box.Y + margin, knob, knob);
            using (var path = Ui.RoundPath(knobRect, knob / 2))
            {
                using (var b = new SolidBrush(Enabled ? Color.White : Skins.Alpha(Color.White, 0.72)))
                    g.FillPath(b, path);
                // 关掉时滑块压在同一片浅色轨道上，补一圈细边才分得开
                if (!Checked)
                    using (var p = new Pen(Skins.Alpha(Ui.Ink, 0.22), Math.Max(1f, k)))
                        g.DrawPath(p, path);
            }
        }
    }

    /// <summary>
    /// 「弧光配色」一行：种子色 → 四颗派生色，右边标出色相偏移。
    /// 颜色直接来自 ArcGlow.Derive，所以换主题时这一行跟着变，不能手选。
    /// </summary>
    public class ArcSwatchStrip : Control
    {
        public Color Seed = Color.Transparent;
        public ArcPalette Palette;

        public ArcSwatchStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float k = g.DpiX / 96f;
            int d = Math.Max(8, (int)Math.Round(12 * k));
            int gap = Math.Max(5, (int)Math.Round(13 * k));
            int top = Math.Max(1, (int)Math.Round(2 * k));
            int x = 0;

            using (var b = new SolidBrush(Seed)) g.FillEllipse(b, x, top, d, d);
            x += d + gap;

            using (var p = new Pen(Ui.Muted, Math.Max(1f, k)))
            {
                int ay = top + d / 2;
                g.DrawLine(p, x, ay, x + gap * 2, ay);
                g.DrawLine(p, x + gap * 2 - (int)(3 * k), ay - (int)(3 * k), x + gap * 2, ay);
                g.DrawLine(p, x + gap * 2 - (int)(3 * k), ay + (int)(3 * k), x + gap * 2, ay);
            }
            x += gap * 2 + gap;

            if (Palette != null)
            {
                Color[] tones = new Color[] { Palette.Arc3, Palette.Arc1, Palette.Arc2, Palette.Arc4 };
                string[] hues = new string[] { "−100°", "−45°", "0°", "+42°" };
                using (var font = UiFont.Make(6.5f))
                using (var ink = new SolidBrush(Ui.Muted))
                {
                    for (int i = 0; i < tones.Length; i++)
                    {
                        using (var b = new SolidBrush(tones[i])) g.FillRectangle(b, x, top, d, d);
                        // 色相标签摆在色块正中，太靠左会和前一块的标签粘在一起
                        SizeF size = g.MeasureString(hues[i], font);
                        g.DrawString(hues[i], font, ink, x + (d - size.Width) / 2f, top + d + Math.Max(1, (int)Math.Round(1.5 * k)));
                        x += d + gap;
                    }
                }
            }

            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    /// <summary>设置窗口左栏：顶部 app 标识 + 版本号，7 项导航（考勤/固定日程/提醒/外观/日程/工资/纸条），底部配置位置。</summary>
    internal class SettingsRail : Control
    {
        public int SelectedIndex;
        public string[] FootLines = new string[0];
        public event Action<int> PageChanged;

        private int hover = -1;

        public SettingsRail()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.RailBg;
            Cursor = Cursors.Hand;
        }

        private float K
        {
            get
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
            }
        }

        private int NavTop { get { return (int)Math.Round(76 * K); } }
        private int ItemH { get { return (int)Math.Round(33 * K); } }
        private int ItemGap { get { return (int)Math.Round(2 * K); } }
        private int Pad { get { return (int)Math.Round(12 * K); } }

        private Rectangle NavRect(int i)
        {
            return new Rectangle(Pad, NavTop + i * (ItemH + ItemGap), Width - Pad - (int)Math.Round(8 * K), ItemH);
        }

        private int IndexAt(int y)
        {
            for (int i = 0; i < SettingsHead.Titles.Length; i++)
                if (NavRect(i).Contains(new Point(Pad + 2, y))) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float k = g.DpiX / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Ui.RailBg)) g.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(Ui.WindowEdge, 1f)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);

            int pad = Pad;
            var mark = new Rectangle(pad, (int)Math.Round(15 * k), (int)Math.Round(20 * k), (int)Math.Round(20 * k));
            using (var path = Ui.RoundPath(mark, (int)Math.Round(6 * k)))
            using (var b = new SolidBrush(Ui.Accent)) g.FillPath(b, path);
            using (var f = UiFont.MakeFamily(Ui.HeadingFont, 9.5f, FontStyle.Bold))
            {
                var sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (var b = new SolidBrush(Ui.AccentInk)) g.DrawString("班", f, b, mark, sf);
            }
            using (var f = UiFont.Make(9.5f, FontStyle.Bold))
            using (var b = new SolidBrush(Ui.Ink))
                g.DrawString("上班工具箱", f, b, mark.Right + (int)Math.Round(7 * k), mark.Y - (int)Math.Round(1 * k));

            // 版本号小胶囊
            string ver = "v" + AppInfo.Version;
            using (var f = UiFont.MakeNumber(7.5f, FontStyle.Bold))
            {
                SizeF vs = g.MeasureString(ver, f);
                int cw = (int)Math.Round(vs.Width + 10 * k), ch = (int)Math.Round(15 * k);
                var chip = new Rectangle(pad, mark.Bottom + (int)Math.Round(7 * k), cw, ch);
                using (var path = Ui.RoundPath(chip, (int)Math.Round(5 * k)))
                {
                    using (var b = new SolidBrush(Ui.RailBg)) g.FillPath(b, path);
                    using (var p = new Pen(Ui.WindowEdge, 1f)) g.DrawPath(p, path);
                }
                using (var b = new SolidBrush(Ui.Muted))
                {
                    var sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(ver, f, b, chip, sf);
                }
            }

            // 7 项导航
            for (int i = 0; i < SettingsHead.Titles.Length; i++)
            {
                Rectangle r = NavRect(i);
                bool sel = i == SelectedIndex;
                bool hov = i == hover;
                if (sel || hov)
                {
                    using (var path = Ui.RoundPath(r, (int)Math.Round(8 * k)))
                    using (var b = new SolidBrush(sel ? Ui.RailActive : Ui.RailHover)) g.FillPath(b, path);
                }
                if (sel)
                {
                    var bar = new Rectangle(pad - (int)Math.Round(6 * k), r.Y + (int)Math.Round(9 * k),
                        (int)Math.Round(2 * k), r.Height - (int)Math.Round(18 * k));
                    using (var path = Ui.RoundPath(bar, bar.Width))
                    using (var b = new SolidBrush(Ui.Accent)) g.FillPath(b, path);
                }
                var icon = new Rectangle(r.X + (int)Math.Round(10 * k), r.Y + (r.Height - (int)Math.Round(14 * k)) / 2,
                    (int)Math.Round(14 * k), (int)Math.Round(14 * k));
                DrawIcon(g, i, icon, sel ? Ui.Accent : Ui.Dim, k);
                using (var f = UiFont.Make(9.5f, sel ? FontStyle.Bold : FontStyle.Regular))
                using (var b = new SolidBrush(sel ? Ui.Ink : Ui.Muted))
                {
                    var sf = new StringFormat();
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(SettingsHead.Titles[i], f, b,
                        new RectangleF(r.X + 32 * k, r.Y, r.Width - 34 * k, r.Height), sf);
                }
            }

            // 底部：配置位置 + 上下班时间
            int lineH = (int)Math.Round(12 * k);
            int footTop = Height - pad - (int)Math.Round(12 * k) - lineH * (FootLines.Length + 1);
            using (var f = UiFont.Make(8f, FontStyle.Bold))
            using (var b = new SolidBrush(Ui.Dim))
                g.DrawString("配置", f, b, pad, footTop);
            for (int i = 0; i < FootLines.Length; i++)
            {
                Rectangle lr = new Rectangle(pad, footTop + lineH * (i + 1), Width - pad * 2 + 2, lineH);
                using (var f = UiFont.Make(7.5f, FontStyle.Regular))
                    TextRenderer.DrawText(g, FootLines[i] ?? "", f, lr, Ui.Muted,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            g.SmoothingMode = SmoothingMode.Default;
        }

        /// <summary>7 个极简图标：圆钟 / 日历 / 铃铛 / 调色盘 / 2×2 格子 / ¥ / 纸飞机。</summary>
        private static void DrawIcon(Graphics g, int kind, Rectangle r, Color c, float k)
        {
            using (var pen = new Pen(c, Math.Max(1f, 1.3f * k)))
            using (var brush = new SolidBrush(c))
            {
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                switch (kind)
                {
                    case 0:   // 考勤：圆钟
                        g.DrawEllipse(pen, x + w * 0.08f, y + h * 0.08f, w * 0.84f, h * 0.84f);
                        g.DrawLine(pen, x + w * 0.5f, y + h * 0.3f, x + w * 0.5f, y + h * 0.52f);
                        g.DrawLine(pen, x + w * 0.5f, y + h * 0.52f, x + w * 0.7f, y + h * 0.64f);
                        break;
                    case 1:   // 固定日程：日历
                        g.DrawRectangle(pen, x + w * 0.1f, y + h * 0.2f, w * 0.8f, h * 0.7f);
                        g.DrawLine(pen, x + w * 0.1f, y + h * 0.42f, x + w * 0.9f, y + h * 0.42f);
                        g.DrawLine(pen, x + w * 0.32f, y + h * 0.1f, x + w * 0.32f, y + h * 0.3f);
                        g.DrawLine(pen, x + w * 0.68f, y + h * 0.1f, x + w * 0.68f, y + h * 0.3f);
                        break;
                    case 2:   // 提醒：铃铛
                        g.DrawArc(pen, x + w * 0.16f, y + h * 0.14f, w * 0.68f, h * 0.56f, 180, 180);
                        g.DrawLine(pen, x + w * 0.16f, y + h * 0.42f, x + w * 0.24f, y + h * 0.78f);
                        g.DrawLine(pen, x + w * 0.84f, y + h * 0.42f, x + w * 0.76f, y + h * 0.78f);
                        g.DrawLine(pen, x + w * 0.24f, y + h * 0.78f, x + w * 0.76f, y + h * 0.78f);
                        g.FillEllipse(brush, x + w * 0.42f, y + h * 0.8f, w * 0.16f, h * 0.12f);
                        break;
                    case 3:   // 外观：调色盘
                        g.DrawEllipse(pen, x + w * 0.08f, y + h * 0.08f, w * 0.84f, h * 0.84f);
                        g.FillEllipse(brush, x + w * 0.3f, y + h * 0.3f, w * 0.13f, h * 0.13f);
                        g.FillEllipse(brush, x + w * 0.56f, y + h * 0.28f, w * 0.13f, h * 0.13f);
                        g.FillEllipse(brush, x + w * 0.62f, y + h * 0.56f, w * 0.13f, h * 0.13f);
                        break;
                    case 4:   // 日程：2×2 格子
                        g.DrawRectangle(pen, x + w * 0.12f, y + h * 0.12f, w * 0.34f, h * 0.34f);
                        g.DrawRectangle(pen, x + w * 0.54f, y + h * 0.12f, w * 0.34f, h * 0.34f);
                        g.DrawRectangle(pen, x + w * 0.12f, y + h * 0.54f, w * 0.34f, h * 0.34f);
                        g.DrawRectangle(pen, x + w * 0.54f, y + h * 0.54f, w * 0.34f, h * 0.34f);
                        break;
                    case 5:   // 工资：¥
                        using (var f = UiFont.MakeNumber(8.5f, FontStyle.Bold))
                        {
                            var sf = new StringFormat();
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            g.DrawString("¥", f, brush, r, sf);
                        }
                        break;
                    default:  // 纸条：纸飞机
                        var plane = new PointF[]
                        {
                            new PointF(x + w * 0.08f, y + h * 0.5f),
                            new PointF(x + w * 0.92f, y + h * 0.12f),
                            new PointF(x + w * 0.5f, y + h * 0.9f),
                            new PointF(x + w * 0.42f, y + h * 0.58f)
                        };
                        g.DrawPolygon(pen, plane);
                        g.DrawLine(pen, x + w * 0.42f, y + h * 0.58f, x + w * 0.92f, y + h * 0.12f);
                        break;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = IndexAt(e.Y);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != -1) { hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = IndexAt(e.Y);
            if (i < 0) return;
            SelectedIndex = i;
            Invalidate();
            if (PageChanged != null) PageChanged(i);
        }
    }

    /// <summary>内容区标题条：当前页名字 + 一句说明；宣纸皮肤下「外观」页右上角画朱砂印章。</summary>
    internal class SettingsHead : Control
    {
        public static readonly string[] Titles = new string[]
        {
            "考勤", "固定日程", "提醒", "外观", "日程", "工资", "纸条"
        };

        public static readonly string[] SubTitles = new string[]
        {
            "班次、工作日与节假日",
            "午休 / 晚休 / 下班与自建日程",
            "飞书考勤对话与日程提醒",
            "主题、字体、提醒动画与配置备份",
            "多份日历订阅与四类通知样式",
            "月工资与发薪日",
            "小纸条的会话面板与公共中继"
        };

        private int pageIndex;
        public int PageIndex
        {
            get { return pageIndex; }
            set { pageIndex = Math.Max(0, Math.Min(Titles.Length - 1, value)); Invalidate(); }
        }

        public SettingsHead()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.WindowBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float k = g.DpiX / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Ui.WindowBg)) g.FillRectangle(b, ClientRectangle);

            int left = (int)Math.Round(16 * k);
            using (var f = UiFont.MakeFamily(Ui.HeadingFont, 13f, FontStyle.Bold))
            {
                string title = Titles[pageIndex];
                SizeF ts = g.MeasureString(title, f);
                using (var b = new SolidBrush(Ui.Ink))
                    g.DrawString(title, f, b, left, (int)Math.Round(9 * k));
                using (var sf = UiFont.Make(9f, FontStyle.Regular))
                using (var sb = new SolidBrush(Ui.Muted))
                    g.DrawString(SubTitles[pageIndex], sf, sb,
                        left + (int)ts.Width + (int)Math.Round(14 * k),
                        (int)Math.Round(9 * k) + (int)Math.Round(ts.Height * 0.42f));
            }

            // 宣纸：外观页标题右侧一枚朱砂「班」印（26 DIP，圆角 3，白字，略歪 -4°）
            if (Ui.Seal && pageIndex == 3)
            {
                int size = (int)Math.Round(26 * k);
                int x = Width - (int)Math.Round(16 * k) - size;
                int y = (int)Math.Round(9 * k);
                var state = g.Save();
                g.TranslateTransform(x + size / 2f, y + size / 2f);
                g.RotateTransform(-4f);
                var box = new Rectangle(-size / 2, -size / 2, size, size);
                using (var path = Ui.RoundPath(box, (int)Math.Round(3 * k)))
                using (var b = new SolidBrush(Ui.SealBg)) g.FillPath(b, path);
                using (var f = UiFont.MakeFamily(Ui.HeadingFont, 12f, FontStyle.Bold))
                {
                    var sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    using (var b = new SolidBrush(Ui.SealInk)) g.DrawString("班", f, b, box, sf);
                }
                g.Restore(state);
            }

            g.SmoothingMode = SmoothingMode.Default;
        }
    }

        /// <summary>固定日程列表：一行一张小卡片，左边是这套配色，右边是名字和时间。</summary>
        public class ScheduleList : Control
        {
            private List<FixedSchedule> items = new List<FixedSchedule>();
            private int selected = -1;
            private int scroll;            // 像素，0 表示顶到头
            private bool dragging;
            private int dragStartY, dragStartScroll;

            public List<FixedSchedule> Items
            {
                get { return items; }
                set
                {
                    items = value != null ? value : new List<FixedSchedule>();
                    ClampScroll();
                    Invalidate();
                }
            }

            public int SelectedIndex
            {
                get { return selected; }
                set
                {
                    selected = value;
                    EnsureVisible(value);
                    Invalidate();
                }
            }

            public event EventHandler SelectedIndexChanged;

            public ScheduleList()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.White;
            }

            // 行高按字体缩放，150% 下也不会把字压到卡片边线上
            // 控件已经被 ApplyDpi 放大过，行高按同一比例走
            private float K { get { return DpiK; } }
            private static float DpiK
            {
                get
                {
                    float k;
                    using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
                    return k < 1f ? 1f : k;
                }
            }
            private int RowH { get { return (int)Math.Round(44 * K); } }
            private int GapH { get { return (int)Math.Round(6 * K); } }
            private int Pitch { get { return RowH + GapH; } }
            private int BarW { get { return (int)Math.Round(6 * K); } }
            private int ContentH { get { return items.Count * Pitch; } }
            public int MaxScroll { get { return Math.Max(0, ContentH - Height); } }
            /// <summary>只有真正超出几像素以上才画滚动条，免得差 2px 也冒出一条。</summary>
            private bool NeedsBar { get { return MaxScroll > (int)Math.Round(6 * K); } }
            public int Scroll
            {
                get { return scroll; }
                set { scroll = value; ClampScroll(); Invalidate(); }
            }

            private void ClampScroll()
            {
                int max = MaxScroll;
                if (scroll < -max) scroll = -max;
                if (scroll > 0) scroll = 0;
            }

            /// <summary>把某一行滚进可视范围（新建、选中时都要用）。</summary>
            public void EnsureVisible(int i)
            {
                if (i < 0) return;
                int top = i * Pitch + scroll;
                int bottom = top + RowH;
                if (top < 0) scroll -= top;
                else
                {
                    int limit = Height - (int)Math.Round(2 * K);   // 留 2px，别贴着边线
                    if (bottom > limit) scroll -= bottom - limit;
                }
                ClampScroll();
                Invalidate();
            }

            private Rectangle RowRect(int i)
            {
                int w = Width - 1 - (NeedsBar ? BarW + (int)Math.Round(4 * K) : 0);
                return new Rectangle(0, i * Pitch + scroll, w, RowH);
            }

            /// <summary>一行的副标题：列表绘制和验收共用，循环日程只显示 RepeatLabel。</summary>
            public string SubTextOf(int i)
            {
                if (i < 0 || i >= items.Count) return "";
                FixedSchedule f = items[i];
                if (f == null) return "";
                string sub;
                if (f.IsOffWork) sub = "按班次下班时间 · " + f.RepeatLabel;
                else if (f.IsRecurring) sub = f.RepeatLabel;
                else sub = f.start + (string.IsNullOrWhiteSpace(f.end) ? "" : "–" + f.end) + " · " + f.RepeatLabel;
                if (!f.notify) sub += " · 不提醒";
                return sub;
            }

            private Rectangle ThumbRect()
            {
                int h = Height * Height / Math.Max(1, ContentH);
                h = Math.Max((int)Math.Round(18 * K), h);
                int track = Height;
                int y = MaxScroll == 0 ? 0 : (int)Math.Round((-scroll) * (double)(track - h) / MaxScroll);
                return new Rectangle(Width - BarW, y, BarW, h);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;   // 白底上的小字用 ClearType 才实
                g.Clear(BackColor);
                float k = K;
                using (var fName = UiFont.Make(9.5f, FontStyle.Bold))
                using (var fSub = UiFont.Make(8.5f))
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var f = items[i];
                        if (f == null) continue;
                        Rectangle r = RowRect(i);
                        if (r.Y > Height) break;
                        if (r.Bottom < 0) continue;
                        bool sel = i == selected;
                        using (var path = RoundRect(r, (int)Math.Round(9 * k)))
                        {
                            using (var b = new SolidBrush(sel ? Ui.RowActive : Ui.RowBg))
                                g.FillPath(b, path);
                            using (var pen = new Pen(sel ? Ui.Accent : Ui.RowBorder, sel ? 2f : 1f))
                                g.DrawPath(pen, path);
                        }
                        Color a, b2, ink;
                        ReminderStyles.Resolve(f.styleId, Ui.RowTheme, out a, out b2, out ink);
                        int pad = (int)Math.Round(8 * k);
                        var chip = new Rectangle(r.X + pad, r.Y + pad, (int)Math.Round(8 * k), r.Height - pad * 2);
                        using (var br = new SolidBrush(a)) g.FillRectangle(br, chip.X, chip.Y, chip.Width, chip.Height / 2 + 1);
                        using (var br = new SolidBrush(b2)) g.FillRectangle(br, chip.X, chip.Y + chip.Height / 2 + 1, chip.Width, chip.Height - chip.Height / 2 - 1);
                        int tx = r.X + pad * 2 + chip.Width;
                        string title = f.name + (f.role == "lunch" && f.deduct ? "（扣工时）" : "");
                        if (!f.enabled) title += "  · 已停用";
                        using (var br = new SolidBrush(f.enabled ? Ui.RowText : Ui.Dim))
                            g.DrawString(title, fName, br, tx, r.Y + (int)Math.Round(4 * k));
                        string sub = SubTextOf(i);
                        using (var br = new SolidBrush(Ui.RowSub))
                            g.DrawString(sub, fSub, br, tx, r.Y + (int)Math.Round(24 * k));
                    }
                }

                // 内容超出一屏就画一条细滚动条，告诉用户下面还有
                if (NeedsBar)
                {
                    using (var track = new SolidBrush(Ui.Track))
                        g.FillRectangle(track, Width - BarW, 0, BarW, Height);
                    var th = ThumbRect();
                    using (var path = RoundRect(new Rectangle(th.X, th.Y, th.Width - 1, th.Height - 1), BarW / 2))
                    using (var br = new SolidBrush(Ui.Thumb))
                        g.FillPath(br, path);
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (NeedsBar && e.X >= Width - BarW - (int)Math.Round(2 * K))
                {
                    var th = ThumbRect();
                    if (!th.Contains(e.Location))
                    {
                        // 点空白处：把滑块挪到点击位置
                        int want = (int)Math.Round((e.Y - th.Height / 2.0) * MaxScroll / Math.Max(1, Height - th.Height));
                        Scroll = -want;
                    }
                    dragging = true;
                    dragStartY = e.Y;
                    dragStartScroll = scroll;
                    Capture = true;
                    return;
                }
                for (int i = 0; i < items.Count; i++)
                {
                    if (!RowRect(i).Contains(e.Location)) continue;
                    if (selected == i) return;
                    selected = i;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                    return;
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!dragging) return;
                var th = ThumbRect();
                int span = Math.Max(1, Height - th.Height);
                int want = dragStartScroll - (int)Math.Round((e.Y - dragStartY) * (double)MaxScroll / span);
                Scroll = want;
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (dragging) { dragging = false; Capture = false; }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (MaxScroll <= 0) return;
                // 往下滚 = Delta 为负 = scroll 变小（scroll 用负值表示已经滚下去多少）
                int step = (int)Math.Round(e.Delta * K / 2f);
                if (step == 0) step = e.Delta > 0 ? 1 : -1;
                Scroll = scroll + step;
            }

            private static GraphicsPath RoundRect(Rectangle r, int radius)
            {
                int d = Math.Max(2, radius * 2);
                var path = new GraphicsPath();
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>一次性日程列表：小卡片，点一下展开看详情，再点收起。</summary>
        public class EventCardList : Control
        {
            private List<ReminderEvent> items = new List<ReminderEvent>();
            private int selected = -1;
            private int expanded = -1;
            private int scroll;

            public List<ReminderEvent> Items
            {
                get { return items; }
                set { items = value != null ? value : new List<ReminderEvent>(); Invalidate(); }
            }

            public int LeadOf = 10;

            public int SelectedIndex
            {
                get { return selected; }
                set { selected = value; if (value < 0) expanded = -1; Invalidate(); }
            }

            public event EventHandler SelectedIndexChanged;

            public EventCardList()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.White;
            }

            // 控件已经被 ApplyDpi 放大过，行高按同一比例走
            private float K { get { return DpiK; } }
            private static float DpiK
            {
                get
                {
                    float k;
                    using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
                    return k < 1f ? 1f : k;
                }
            }
            private int CollapsedH { get { return (int)Math.Round(40 * K); } }
            private int ExpandedH { get { return (int)Math.Round(92 * K); } }
            private int GapH { get { return (int)Math.Round(6 * K); } }

            private int HeightOf(int i)
            {
                return i == expanded ? ExpandedH : CollapsedH;
            }

            private Rectangle RowRect(int i)
            {
                int y = scroll;
                for (int k = 0; k < i; k++) y += HeightOf(k) + GapH;
                return new Rectangle(0, y, Width - 1, HeightOf(i));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;   // 白底上的小字用 ClearType 才实
                g.Clear(BackColor);
                using (var bg = new SolidBrush(Ui.Surface))
                    g.FillRectangle(bg, 0, 0, Width, Height);
                using (var bp = new Pen(Ui.RowBorder))
                    g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
                float k = K;
                if (items.Count == 0)
                {
                    var sfEmpty = new StringFormat();
                    sfEmpty.Alignment = StringAlignment.Center;
                    sfEmpty.LineAlignment = StringAlignment.Center;
                    using (var brEmpty = new SolidBrush(Ui.Dim))
                    using (var fEmpty = UiFont.Make(8.5f))
                        g.DrawString("还没有日程\r\n右边填一条，点「添加日程」", fEmpty, brEmpty,
                            new RectangleF(0, 0, Width, Height), sfEmpty);
                }
                using (var fHead = UiFont.Make(8f))
                using (var fTitle = UiFont.Make(9f, FontStyle.Bold))
                using (var fBody = UiFont.Make(8.5f))
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var ev = items[i];
                        if (ev == null) continue;
                        Rectangle r = RowRect(i);
                        if (r.Y > Height) break;
                        if (r.Bottom < 0) continue;
                        bool sel = i == selected, open = i == expanded;
                        using (var path = RoundRect(r, (int)Math.Round(8 * k)))
                        {
                            using (var b = new SolidBrush(sel ? Ui.RowActive : Ui.RowBg))
                                g.FillPath(b, path);
                            using (var pen = new Pen(sel ? Ui.Accent : Ui.RowBorder, sel ? 2f : 1f))
                                g.DrawPath(pen, path);
                        }
                        DateTime start = Reminders.ParseStart(ev);
                        string head = start == DateTime.MinValue ? "时间无效" : start.ToString("MM-dd HH:mm") + " 提醒";
                        if (start != DateTime.MinValue && start < DateTime.Now) head = "已过 · " + start.ToString("MM-dd HH:mm");
                        int pad = (int)Math.Round(8 * k);
                        using (var br = new SolidBrush(Ui.RowSub))
                            g.DrawString(head, fHead, br, r.X + pad, r.Y + (int)Math.Round(4 * k));
                        using (var br = new SolidBrush(Ui.RowText))
                            g.DrawString(Clip(ev.title, r.Width - pad * 2, fTitle, g), fTitle, br, r.X + pad, r.Y + (int)Math.Round(18 * k));
                        if (open)
                        {
                            string when = Reminders.TimeRange(ev);
                            string place = string.IsNullOrWhiteSpace(ev.place) ? "未填位置" : ev.place;
                            string body = when + " · " + place;
                            using (var br = new SolidBrush(Ui.Dim))
                                g.DrawString(body, fBody, br, r.X + pad, r.Y + (int)Math.Round(46 * k));
                            string detail = string.IsNullOrWhiteSpace(ev.detail) ? "（没有备注）" : ev.detail;
                            using (var br = new SolidBrush(Ui.Dim))
                                g.DrawString(Clip(detail, r.Width - pad * 2, fBody, g), fBody, br, r.X + pad, r.Y + (int)Math.Round(68 * k));
                        }
                    }
                }
            }

            private static string Clip(string text, int width, Font f, Graphics g)
            {
                if (string.IsNullOrEmpty(text)) return "";
                if (g.MeasureString(text, f).Width <= width) return text;
                string t = text;
                while (t.Length > 1 && g.MeasureString(t + "…", f).Width > width) t = t.Substring(0, t.Length - 1);
                return t + "…";
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                for (int i = 0; i < items.Count; i++)
                {
                    if (!RowRect(i).Contains(e.Location)) continue;
                    selected = i;
                    expanded = expanded == i ? -1 : i;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                    return;
                }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                int total = 0;
                for (int i = 0; i < items.Count; i++) total += HeightOf(i) + GapH;
                int max = Math.Max(0, total - Height);
                scroll = Math.Max(-max, Math.Min(0, scroll + e.Delta / 3));
                Invalidate();
            }

            private static GraphicsPath RoundRect(Rectangle r, int radius)
            {
                int d = Math.Max(2, radius * 2);
                var path = new GraphicsPath();
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>字体设置：中文和数字分开选，下面给一张和卡片同色的小预览。</summary>
    internal class FontsForm : Form
    {
        private readonly ComboBox cmbText;
        private readonly ComboBox cmbNumber;
        private readonly FontPreview preview;
        private readonly string[] families;
        private string selText = "";
        private string selNumber = "Consolas";

        public string ResultTextFont { get; private set; }
        public string ResultNumberFont { get; private set; }

        public FontsForm(string textFont, string numberFont)
        {
            Text = "字体设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 368);
            BackColor = Ui.WindowBg;
            Font = UiFont.Make(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;

            families = UiFont.Families();
            selText = string.IsNullOrWhiteSpace(textFont) ? "" : textFont;
            selNumber = string.IsNullOrWhiteSpace(numberFont) ? "Consolas" : numberFont;

            var l1 = new Label();
            l1.Text = "中文字体";
            l1.SetBounds(16, 16, 200, 20);
            l1.ForeColor = Ui.Ink;
            Controls.Add(l1);

            cmbText = new ComboBox();
            cmbText.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbText.SetBounds(16, 38, 200, 26);
            cmbText.Items.Add("跟随系统（推荐）");
            foreach (var f in families) cmbText.Items.Add(f);
            int ti = 0;
            for (int i = 1; i < cmbText.Items.Count; i++)
                if (string.Equals(cmbText.Items[i].ToString(), selText, StringComparison.OrdinalIgnoreCase)) { ti = i; break; }
            if (ti == 0 && selText.Length > 0) { cmbText.Items.Add(selText); ti = cmbText.Items.Count - 1; }
            cmbText.SelectedIndex = ti;
            cmbText.SelectedIndexChanged += delegate { OnPicked(); };
            Controls.Add(cmbText);

            var l2 = new Label();
            l2.Text = "数字字体";
            l2.SetBounds(240, 16, 200, 20);
            l2.ForeColor = Ui.Ink;
            Controls.Add(l2);

            cmbNumber = new ComboBox();
            cmbNumber.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbNumber.SetBounds(240, 38, 200, 26);
            cmbNumber.Items.Add("Consolas（默认）");
            foreach (var f in families) cmbNumber.Items.Add(f);
            int ni = 0;
            for (int i = 1; i < cmbNumber.Items.Count; i++)
                if (string.Equals(cmbNumber.Items[i].ToString(), selNumber, StringComparison.OrdinalIgnoreCase)) { ni = i; break; }
            if (ni == 0 && !string.Equals(selNumber, "Consolas", StringComparison.OrdinalIgnoreCase))
            {
                cmbNumber.Items.Add(selNumber);
                ni = cmbNumber.Items.Count - 1;
            }
            cmbNumber.SelectedIndex = ni;
            cmbNumber.SelectedIndexChanged += delegate { OnPicked(); };
            Controls.Add(cmbNumber);

            var hint = new Label();
            hint.Text = "中文影响界面文字；数字影响大号倒计时、工资数字和 ¥ 图标。\r\n数字建议选等宽字体，倒计时不会左右跳。";
            hint.SetBounds(16, 70, 428, 38);
            hint.ForeColor = Ui.Muted;
            Controls.Add(hint);

            preview = new FontPreview();
            preview.SetBounds(16, 112, 428, 174);
            Controls.Add(preview);

            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.SetBounds(248, 312, 96, 32);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderColor = Ui.Edge;
            btnCancel.BackColor = Ui.Surface;
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            var btnOk = new Button();
            btnOk.Text = "确定";
            btnOk.SetBounds(352, 312, 92, 32);
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.BackColor = Ui.Accent;
            btnOk.ForeColor = Ui.AccentInk;
            btnOk.Click += delegate
            {
                ResultTextFont = selText;
                ResultNumberFont = selNumber;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnOk);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            OnPicked();
            FitDpi();
        }

        private void OnPicked()
        {
            selText = cmbText.SelectedIndex <= 0 ? "" : cmbText.SelectedItem.ToString();
            selNumber = cmbNumber.SelectedIndex <= 0 ? "Consolas" : cmbNumber.SelectedItem.ToString();
            preview.TextFamily = selText;
            preview.NumberFamily = selNumber;
            preview.Invalidate();
        }

        private void FitDpi()
        {
            float k;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            if (Math.Abs(k - 1f) < 0.01f) return;
            SuspendLayout();
            Scale(new SizeF(k, k));
            ResumeLayout(true);
        }

        /// <summary>和卡片同色的小预览：中文样张 + 数字样张。</summary>
        private class FontPreview : Control
        {
            public string TextFamily = "";
            public string NumberFamily = "Consolas";

            public FontPreview()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.White);
                float k = g.DpiX / 96f;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = Round(r, (int)Math.Round(14 * k)))
                {
                    using (var b = new LinearGradientBrush(r, Color.FromArgb(26, 32, 46), Color.FromArgb(38, 46, 64), 90f))
                        g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(30, 255, 255, 255))) g.DrawPath(p, path);
                }

                Color ink = Color.FromArgb(236, 241, 250);
                Color muted = Color.FromArgb(154, 166, 189);
                using (var f = MakeTextFont(11.5f))
                using (var b = new SolidBrush(muted))
                    g.DrawString("中文字体 · 上班工具箱", f, b, 22 * k, 20 * k);
                using (var f = MakeNumberFont(30f, FontStyle.Bold))
                using (var b = new SolidBrush(ink))
                    g.DrawString("05:05:17", f, b, 20 * k, 48 * k);
                using (var f = MakeNumberFont(18f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.FromArgb(76, 141, 255)))
                    g.DrawString("¥12,345.678", f, b, 22 * k, 108 * k);
                using (var f = MakeTextFont(9f))
                using (var b = new SolidBrush(muted))
                    g.DrawString("数字字体 · 等宽显示更整齐", f, b, 22 * k, 150 * k);
            }

            private Font MakeTextFont(float size)
            {
                if (!string.IsNullOrWhiteSpace(TextFamily))
                {
                    try { return new Font(TextFamily, size, FontStyle.Regular, GraphicsUnit.Point); }
                    catch { }
                }
                return UiFont.Make(size);
            }

            private Font MakeNumberFont(float size, FontStyle style)
            {
                if (!string.IsNullOrWhiteSpace(NumberFamily))
                {
                    try { return new Font(NumberFamily, size, style, GraphicsUnit.Point); }
                    catch { }
                }
                return UiFont.MakeNumber(size, style);
            }

            private static GraphicsPath Round(Rectangle r, int radius)
            {
                int d = Math.Max(2, radius * 2);
                var path = new GraphicsPath();
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }
    }

    /// <summary>自己配色：色相条 + 明度/饱和方块，选完起个名字存成主题。</summary>
        public class ColorWheelForm : Form
        {
            public string ThemeName = "我的主题";
            public string AccentHex = "#4C8DFF";
            public string BgHex = "#14180E";
            public string BarHex = "#A4CBFF";

            private double hue, sat = 0.75, val = 0.85;
            private SvPanel sv;
            private HuePanel hueBar;
            private TextBox tbName;
            private Label lblHex;
            private PreviewSwatch preview;
            private Button btnBg, btnBar;
            private Color bgColor, barColor;
            private bool bgActive = true;

            public ColorWheelForm(ThemeDef seed)
                : this("新建主题", "我的主题", seed == null ? Color.FromArgb(20, 24, 14) : seed.bgTop,
                       seed == null ? Color.FromArgb(76, 141, 255) : seed.accent)
            {
            }

            public ColorWheelForm(string title, string name, Color bg, Color bar)
            {
                Text = title;
                ThemeName = name;
                bgColor = bg;
                barColor = bar;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;
                AutoScaleDimensions = new SizeF(96f, 96f);
                AutoScaleMode = AutoScaleMode.None;
                ClientSize = new Size(452, 390);
                Font = UiFont.Make(9f, FontStyle.Regular);
                BackColor = Ui.WindowBg;
                FromColor(bgColor);

                sv = new SvPanel();
                sv.SetBounds(16, 16, 220, 190);
                sv.Changed += delegate { sat = sv.Sat; val = sv.Val; SyncFromHsv(); };
                Controls.Add(sv);

                hueBar = new HuePanel();
                hueBar.SetBounds(244, 16, 22, 190);
                hueBar.Changed += delegate { hue = hueBar.Hue; SyncFromHsv(); };
                Controls.Add(hueBar);

                preview = new PreviewSwatch();
                preview.SetBounds(280, 16, 156, 92);
                Controls.Add(preview);

                btnBg = new Button();
                btnBg.SetBounds(280, 116, 74, 28);
                btnBg.FlatStyle = FlatStyle.Flat;
                btnBg.FlatAppearance.BorderSize = 2;
                btnBg.UseVisualStyleBackColor = false;
                btnBg.TabStop = false;
                btnBg.Click += delegate { bgActive = true; FromColor(bgColor); SyncFromHsv(); };
                Controls.Add(btnBg);

                btnBar = new Button();
                btnBar.SetBounds(362, 116, 74, 28);
                btnBar.FlatStyle = FlatStyle.Flat;
                btnBar.FlatAppearance.BorderSize = 2;
                btnBar.UseVisualStyleBackColor = false;
                btnBar.TabStop = false;
                btnBar.Click += delegate { bgActive = false; FromColor(barColor); SyncFromHsv(); };
                Controls.Add(btnBar);

                var l1 = new Label();
                l1.Text = "背景";
                l1.SetBounds(280, 146, 74, 18);
                l1.ForeColor = Ui.Muted;
                Controls.Add(l1);

                var l2 = new Label();
                l2.Text = "进度条";
                l2.SetBounds(362, 146, 74, 18);
                l2.ForeColor = Ui.Muted;
                Controls.Add(l2);

                var l3 = new Label();
                l3.Text = "名字";
                l3.SetBounds(280, 174, 40, 22);
                l3.ForeColor = Ui.Muted;
                Controls.Add(l3);

                tbName = new TextBox();
                tbName.SetBounds(280, 196, 156, 24);
                tbName.Text = name;
                tbName.TextChanged += delegate { ThemeName = tbName.Text.Trim(); };
                Controls.Add(tbName);

                lblHex = new Label();
                lblHex.SetBounds(280, 226, 156, 52);
                lblHex.ForeColor = Ui.Muted;
                Controls.Add(lblHex);

                var tips = new Label();
                tips.Text = "点「背景」或「进度条」切换要调的颜色；\r\n色相条选颜色家族，方块里选深浅。";
                tips.SetBounds(16, 216, 250, 70);
                tips.ForeColor = Ui.Muted;
                Controls.Add(tips);

                var ok = new Button();
                ok.Text = "保存";
                ok.SetBounds(240, 336, 96, 32);
                ok.FlatStyle = FlatStyle.Flat;
                ok.FlatAppearance.BorderSize = 0;
                ok.BackColor = Ui.Accent;
                ok.ForeColor = Ui.AccentInk;
                ok.Click += delegate
                {
                    ThemeName = tbName.Text.Trim();
                    if (ThemeName.Length == 0) ThemeName = "我的主题";
                    BgHex = HexOf(bgColor);
                    BarHex = HexOf(barColor);
                    AccentHex = BarHex;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(ok);

                var cancel = new Button();
                cancel.Text = "取消";
                cancel.SetBounds(344, 336, 92, 32);
                cancel.FlatStyle = FlatStyle.Flat;
                cancel.FlatAppearance.BorderColor = Ui.Edge;
                cancel.BackColor = Ui.Surface;
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(cancel);

                sv.Hue = hue;
                SyncFromHsv();
                ApplyDpi();
            }

            private void ApplyDpi()
            {
                float k;
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
                if (Math.Abs(k - 1f) < 0.01f) return;
                SuspendLayout();
                    Scale(new SizeF(k, k));
                ResumeLayout(true);
            }

            private void FromColor(Color c)
            {
                double max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0;
                double min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255.0;
                double d = max - min;
                val = max;
                sat = max <= 0 ? 0 : d / max;
                if (d < 1e-6) hue = 0;
                else if (max == c.R / 255.0) hue = 60 * (((c.G - c.B) / 255.0) / d % 6);
                else if (max == c.G / 255.0) hue = 60 * (((c.B - c.R) / 255.0) / d + 2);
                else hue = 60 * (((c.R - c.G) / 255.0) / d + 4);
                if (hue < 0) hue += 360;
            }

            private void SyncFromHsv()
            {
                if (sv != null) { sv.Hue = hue; sv.Sat = sat; sv.Val = val; sv.Invalidate(); }
                if (hueBar != null) { hueBar.Hue = hue; hueBar.Invalidate(); }
                Color c = ColorFromHsv(hue, sat, val);
                if (bgActive) bgColor = c; else barColor = c;
                if (lblHex != null)
                    lblHex.Text = "背景  " + HexOf(bgColor) + "\r\n进度条  " + HexOf(barColor);
                if (preview != null)
                {
                    preview.Bg = bgColor;
                    preview.Bar = barColor;
                    preview.Invalidate();
                }
                if (btnBg != null)
                {
                    btnBg.BackColor = bgColor;
                    btnBar.BackColor = barColor;
                    btnBg.FlatAppearance.BorderColor = bgActive ? Ui.Accent : Color.FromArgb(205, 211, 222);
                    btnBar.FlatAppearance.BorderColor = bgActive ? Color.FromArgb(205, 211, 222) : Ui.Accent;
                }
            }

            public static Color ColorFromHsv(double h, double sat0, double v)
            {
                if (sat0 <= 0)
                {
                    int gray = (int)Math.Round(v * 255);
                    return Color.FromArgb(gray, gray, gray);
                }
                double c = v * sat0;
                double hh = h / 60.0;
                double x = c * (1 - Math.Abs(hh % 2 - 1));
                double r1 = 0, g1 = 0, b1 = 0;
                if (hh < 1) { r1 = c; g1 = x; }
                else if (hh < 2) { r1 = x; g1 = c; }
                else if (hh < 3) { g1 = c; b1 = x; }
                else if (hh < 4) { g1 = x; b1 = c; }
                else if (hh < 5) { r1 = x; b1 = c; }
                else { r1 = c; b1 = x; }
                double m = v - c;
                return Color.FromArgb(
                    (int)Math.Round((r1 + m) * 255), (int)Math.Round((g1 + m) * 255), (int)Math.Round((b1 + m) * 255));
            }

            public static string HexOf(Color c)
            {
                return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            }

            /// <summary>饱和/明度方块。</summary>
            public class SvPanel : Control
            {
                public double Hue, Sat = 0.8, Val = 0.9;
                public event EventHandler Changed;

                public SvPanel()
                {
                    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    var r = new Rectangle(0, 0, Width - 1, Height - 1);
                    using (var b = new LinearGradientBrush(r, ColorFromHsv(Hue, 1, 1), Color.White, 0f))
                        g.FillRectangle(b, r);
                    using (var b = new LinearGradientBrush(r, Color.FromArgb(0, 0, 0, 0), Color.Black, 90f))
                        g.FillRectangle(b, r);
                    using (var p = new Pen(Color.FromArgb(210, 216, 228))) g.DrawRectangle(p, r);
                    int x = (int)Math.Round(Sat * (Width - 1)), y = (int)Math.Round((1 - Val) * (Height - 1));
                    using (var p = new Pen(Color.White, 2f)) g.DrawEllipse(p, x - 6, y - 6, 12, 12);
                    using (var p = new Pen(Color.FromArgb(90, 0, 0, 0), 1f)) g.DrawEllipse(p, x - 7, y - 7, 14, 14);
                }

                private void Pick(Point p)
                {
                    Sat = Math.Max(0, Math.Min(1, p.X / (double)Math.Max(1, Width - 1)));
                    Val = 1 - Math.Max(0, Math.Min(1, p.Y / (double)Math.Max(1, Height - 1)));
                    Invalidate();
                    if (Changed != null) Changed(this, EventArgs.Empty);
                }

                protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pick(e.Location); }
                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    if (e.Button == MouseButtons.Left) Pick(e.Location);
                }
            }

            /// <summary>色相条。</summary>
            public class HuePanel : Control
            {
                public double Hue;
                public event EventHandler Changed;

                public HuePanel()
                {
                    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    for (int y = 0; y < Height; y++)
                    {
                        double h = 360.0 * y / Math.Max(1, Height - 1);
                        using (var b = new SolidBrush(ColorFromHsv(h, 0.95, 0.95)))
                            g.FillRectangle(b, 0, y, Width, 1);
                    }
                    using (var p = new Pen(Color.FromArgb(210, 216, 228))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                    int y2 = (int)Math.Round(Hue / 360.0 * (Height - 1));
                    using (var p = new Pen(Color.White, 2f)) g.DrawRectangle(p, 2, y2 - 2, Width - 5, 4);
                    using (var p = new Pen(Color.FromArgb(90, 0, 0, 0), 1f)) g.DrawRectangle(p, 1, y2 - 3, Width - 3, 6);
                }

                private void Pick(Point p)
                {
                    Hue = Math.Max(0, Math.Min(359, 360.0 * p.Y / Math.Max(1, Height - 1)));
                    Invalidate();
                    if (Changed != null) Changed(this, EventArgs.Empty);
                }

                protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pick(e.Location); }
                protected override void OnMouseMove(MouseEventArgs e)
                {
                    base.OnMouseMove(e);
                    if (e.Button == MouseButtons.Left) Pick(e.Location);
                }
            }

            /// <summary>预览：背景色 + 进度条色做出来的小卡片。</summary>
            public class PreviewSwatch : Control
            {
                public Color Bg = Color.FromArgb(20, 24, 14);
                public Color Bar = Color.FromArgb(164, 203, 114);

                public PreviewSwatch()
                {
                    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.White);
                    // 控件本身已被 DPI 放大，画笔坐标也要跟着放大，否则 150% 下文字和进度条会叠在一起
                    float k = g.DpiX / 96f;
                    ThemeDef t = Themes.FromColors("preview", "预览", HexOf(Bg), HexOf(Bar));
                    var r = new Rectangle(0, 0, Width - 1, Height - 1);
                    using (var path = Round(r, (int)Math.Round(12 * k)))
                    {
                        using (var b = new LinearGradientBrush(r, t.bgTop, t.bgBottom, 90f))
                            g.FillPath(b, path);
                        using (var p = new Pen(Color.FromArgb(28, 255, 255, 255))) g.DrawPath(p, path);
                    }
                    using (var b = new SolidBrush(Bar))
                        g.FillEllipse(b, 12 * k, 12 * k, 7 * k, 7 * k);
                    using (var f = UiFont.Make(7.5f))
                    using (var b = new SolidBrush(t.textMuted))
                        g.DrawString("工作中", f, b, 25 * k, 10 * k);
                    using (var f = UiFont.MakeNumber(18f, FontStyle.Bold))
                    using (var b = new SolidBrush(t.textMain))
                        g.DrawString("12:34", f, b, 10 * k, 26 * k);
                    int barY = (int)Math.Round(58 * k);
                    int barH = Math.Max(1, (int)Math.Round(5 * k));
                    int barX = (int)Math.Round(12 * k);
                    int barW = Width - (int)Math.Round(26 * k);
                    using (var b = new SolidBrush(t.track)) g.FillRectangle(b, barX, barY, barW, barH);
                    using (var b = new SolidBrush(Bar)) g.FillRectangle(b, barX, barY, barW * 3 / 5, barH);
                    using (var b = new SolidBrush(t.textDim))
                    using (var f = UiFont.Make(6.5f))
                    {
                        g.DrawString("背景  " + HexOf(Bg), f, b, 12 * k, 64 * k);
                        g.DrawString("进度条  " + HexOf(Bar), f, b, 12 * k, 76 * k);
                    }
                }

                private static GraphicsPath Round(Rectangle r, int radius)
                {
                    int d = radius * 2;
                    var path = new GraphicsPath();
                    path.AddArc(r.X, r.Y, d, d, 180, 90);
                    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    return path;
                }
            }
        }
}
