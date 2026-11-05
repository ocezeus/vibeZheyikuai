using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace WorkCountdown
{
    public class SettingsForm : Form
    {
        private static readonly Color AccentColor = Color.FromArgb(64, 124, 245);
        private static readonly Color Ink = Color.FromArgb(38, 46, 64);
        private static readonly Color Muted = Color.FromArgb(120, 128, 146);
        private static readonly Color CardBg = Color.White;
        private static readonly Color CardBorder = Color.FromArgb(230, 234, 242);
        private static readonly string[] RepeatIds = new string[] { "daily", "odd", "even" };

        private readonly SettingsStore store;
        private Settings s;

        private TabControl tabs;

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
        private RadioButton rbSpreadCard;
        private RadioButton rbSpreadScreen;
        private RadioButton rbStyleCircle;
        private RadioButton rbStyleBar;

        public SettingsForm(SettingsStore store, Settings settings)
        {
            this.store = store;
            this.s = settings;
            settings.NormalizeSchedules();          // 直接 new 出来的 Settings 也要有固定日程
            settings.NormalizeCustomThemes();

            Text = "下班倒计时 · 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96f, 96f);   // 按 96 DPI 设计，运行时整体放大
            AutoScaleMode = AutoScaleMode.None;          // 建好后自己按 DPI 放大，字体和控件一起走
            ClientSize = new Size(524, 640);
            BackColor = Color.FromArgb(246, 248, 251);
            Font = UiFont.Make(9f, FontStyle.Regular);
            DoubleBuffered = true;

            BuildTabs();
            BuildFooter();
            LoadValues();
            ApplyDpi();
        }

        /// <summary>按系统 DPI 整体放大：字体按点数渲染，控件按比例放大才对得上。</summary>
        private void ApplyDpi()
        {
            float k;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            if (Math.Abs(k - 1f) < 0.01f) return;
            SuspendLayout();
            Scale(new SizeF(k, k));
            ResumeLayout(true);
        }

        // ---------------- 骨架 ----------------

        private void BuildTabs()
        {
            tabs = new TabControl();
            tabs.SetBounds(8, 8, 508, 578);
            tabs.ItemSize = new Size(112, 34);
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                var page = tabs.TabPages[e.Index];
                Rectangle r = e.Bounds;
                bool sel = tabs.SelectedIndex == e.Index;
                using (var b = new SolidBrush(sel ? Color.White : Color.FromArgb(240, 243, 249)))
                    e.Graphics.FillRectangle(b, r);
                if (sel)
                {
                    using (var b = new SolidBrush(AccentColor))
                        e.Graphics.FillRectangle(b, r.X + 16, r.Bottom - 3, r.Width - 32, 3);
                }
                using (var f = UiFont.Make(9.5f, sel ? FontStyle.Bold : FontStyle.Regular))
                using (var b = new SolidBrush(sel ? Ink : Muted))
                {
                    var sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    e.Graphics.DrawString(page.Text, f, b, r, sf);
                }
            };

            var p1 = new TabPage("考勤");
            var p2 = new TabPage("固定日程");
            var p3 = new TabPage("提醒");
            var p4 = new TabPage("外观");
            foreach (var p in new TabPage[] { p1, p2, p3, p4 })
            {
                p.BackColor = Color.FromArgb(246, 248, 251);
                p.Padding = new Padding(0);
            }
            tabs.TabPages.Add(p1);
            tabs.TabPages.Add(p2);
            tabs.TabPages.Add(p3);
            tabs.TabPages.Add(p4);
            Controls.Add(tabs);

            BuildPolicy(p1);
            BuildDays(p1);
            BuildExceptions(p1);

            BuildSchedules(p2);

            BuildReminders(p3);

            BuildThemes(p4);
            BuildLook(p4);
            BuildBackup(p4);
        }

        private void BuildFooter()
        {
            var footer = new Label();
            footer.Text = "配置存在 " + store.Dir;
footer.SetBounds(16, 600, 258, 20);
            footer.ForeColor = Muted;
            footer.AutoEllipsis = true;
            Controls.Add(footer);

            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.SetBounds(286, 592, 106, 34);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(214, 220, 232);
            btnCancel.BackColor = Color.White;
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            var btnOk = new Button();
            btnOk.Text = "保存";
            btnOk.SetBounds(400, 592, 106, 34);
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.BackColor = AccentColor;
            btnOk.ForeColor = Color.White;
            btnOk.Click += delegate { Save(); };
            Controls.Add(btnOk);

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
var g = CardBox(page, "工作日", 8, 186, 492, 84);
            cbDays = new CheckBox[7];
            string[] names = new string[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            for (int i = 0; i < 7; i++)
            {
                var cb = new CheckBox();
                cb.Text = names[i];
                cb.SetBounds(14 + i * 66, 40, 62, 22);
                g.Controls.Add(cb);
                cbDays[i] = cb;
            }
        }

        private void BuildExceptions(TabPage page)
        {
var g = CardBox(page, "节假日与调休", 8, 282, 492, 224);

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
            cmbRepeat.Items.Add("每天");
            cmbRepeat.Items.Add("单数周");
            cmbRepeat.Items.Add("双数周");
            cmbRepeat.SelectedIndex = 0;
            cmbRepeat.SetBounds(52, 58, 84, 24);
            e.Controls.Add(cmbRepeat);

            e.Controls.Add(Lbl("重复至", 146, 58, 50, 24));
            dpUntil = new DateTimePicker();
            dpUntil.Format = DateTimePickerFormat.Custom;
            dpUntil.CustomFormat = "yyyy-MM-dd";
            dpUntil.SetBounds(198, 58, 108, 24);
            e.Controls.Add(dpUntil);
            cbForever = new CheckBox();
            cbForever.Text = "永久";
            cbForever.SetBounds(314, 58, 60, 22);
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

            var btnAdd = FlatButton("新建一条", 296, 120, 84, 26);
            btnAdd.FlatAppearance.BorderColor = AccentColor;
            btnAdd.ForeColor = AccentColor;
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
            cmbRepeat.SelectedIndex = on ? Math.Max(0, Array.IndexOf(RepeatIds, f.repeat)) : 0;
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
            bool off = on && f.IsOffWork;
            tbSchedStart.Enabled = on && !off;
            tbSchedEnd.Enabled = on && !off;
            tbSchedName.Enabled = on;
            cbSchedDeduct.Enabled = on;
            lblSchedHint.Text = off
                ? "「下班」按班次算时间（今天 " + OffWorkText() + "），不用填开始；重复规则决定哪天弹「该下班啦」。"
                : "时间格式 HH:mm。结束留空就只响一次；有结束时间时会一直盖到结束。只在工作日触发。";
            loadingSched = false;
            UpdateSchedState();
        }

        private void UpdateSchedState()
        {
            bool on = schedIndex >= 0 && schedIndex < s.schedules.Count;
            if (dpUntil != null) dpUntil.Enabled = on && !cbForever.Checked;
            if (cbSchedEndNotify != null && on)
                cbSchedEndNotify.Enabled = !s.schedules[schedIndex].IsOffWork;
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
            f.repeat = RepeatIds[Math.Max(0, cmbRepeat.SelectedIndex)];
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

            var btnAdd = FlatButton("添加日程", 256, 264, 108, 28);
            btnAdd.FlatAppearance.BorderColor = AccentColor;
            btnAdd.ForeColor = AccentColor;
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

        // ---------------- 外观 ----------------

        private void BuildThemes(TabPage page)
        {
            var g = CardBox(page, "主题配色", 8, 8, 492, 196);
            picker = new ThemePicker();
            picker.SetBounds(14, 30, 456, 122);
            picker.SelectedIdChanged += delegate { UpdateThemeName(); };
            g.Controls.Add(picker);

            lblThemeName = Lbl("", 14, 160, 300, 20);
            lblThemeName.ForeColor = Muted;
            g.Controls.Add(lblThemeName);

            var btnCustom = FlatButton("＋ 新建主题", 330, 156, 140, 28);
            btnCustom.FlatAppearance.BorderColor = AccentColor;
            btnCustom.ForeColor = AccentColor;
            btnCustom.Click += delegate { NewCustomTheme(); };
            g.Controls.Add(btnCustom);
        }

        private void UpdateThemeName()
        {
            if (lblThemeName == null || picker == null) return;
            var t = Themes.Get(picker.SelectedId);
            lblThemeName.Text = "当前：" + (Themes.IsCustom(t.id) ? "我的主题 · " : "") + t.name;
        }

        private void NewCustomTheme()
        {
            var dlg = new ColorWheelForm(Themes.Get(picker.SelectedId));
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var ct = new CustomTheme();
            ct.id = "my-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            ct.name = dlg.ThemeName;
            ct.accent = dlg.AccentHex;
            s.customThemes.Add(ct);
            Themes.SetCustom(s.customThemes);
            picker.SelectedId = ct.id;
            picker.Invalidate();
            UpdateThemeName();
        }

        private void BuildLook(TabPage page)
        {
            var g = CardBox(page, "外观与提醒", 8, 212, 492, 224);

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
            cbReducedMotion.SetBounds(14, 86, 142, 22);
            g.Controls.Add(cbReducedMotion);
            g.Controls.Add(Hint("少动一点，提醒内容照旧。", 164, 86, 310, 22));

            g.Controls.Add(Lbl("最短停留", 14, 116, 58, 24));
            numReminderHold = Num(6, 76, 116, 56, 3, 20);
            g.Controls.Add(numReminderHold);
            g.Controls.Add(Lbl("秒（3–20），之后常驻到时段结束", 140, 116, 330, 24));

            g.Controls.Add(Lbl("提醒扩散", 14, 146, 58, 24));
            var spreadPair = new Panel();
            spreadPair.SetBounds(76, 146, 220, 24);
            spreadPair.BackColor = Ui.CardBg;
            g.Controls.Add(spreadPair);
            rbSpreadCard = new RadioButton();
            rbSpreadCard.Text = "仅卡片";
            rbSpreadCard.SetBounds(0, 0, 76, 22);
            spreadPair.Controls.Add(rbSpreadCard);
            rbSpreadScreen = new RadioButton();
            rbSpreadScreen.Text = "全屏";
            rbSpreadScreen.SetBounds(80, 0, 62, 22);
            spreadPair.Controls.Add(rbSpreadScreen);

            g.Controls.Add(Lbl("扩散样式", 14, 176, 58, 24));
            var stylePair = new Panel();
            stylePair.SetBounds(76, 176, 260, 24);
            stylePair.BackColor = Ui.CardBg;
            g.Controls.Add(stylePair);
            rbStyleCircle = new RadioButton();
            rbStyleCircle.Text = "圆形柔光";
            rbStyleCircle.SetBounds(0, 0, 86, 22);
            stylePair.Controls.Add(rbStyleCircle);
            rbStyleBar = new RadioButton();
            rbStyleBar.Text = "进度条拉伸";
            rbStyleBar.SetBounds(90, 0, 100, 22);
            stylePair.Controls.Add(rbStyleBar);

            g.Controls.Add(Hint("固定日程按各自配色；其余提醒跟随当前主题。", 14, 202, 466, 18));
        }

        private void BuildBackup(TabPage page)
        {
            var g = CardBox(page, "配置备份", 8, 444, 492, 112);
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
                bool ok = store.Export(s, dlg.FileName);
                MessageBox.Show(this, ok ? "已导出到：\n" + dlg.FileName : "导出失败，日志里记录了原因。",
                    ok ? "导出完成" : "导出失败", MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }

        private void ImportConfig()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "导入配置";
                dlg.Filter = "配置文件 (*.json)|*.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var loaded = store.Import(dlg.FileName);
                if (loaded == null)
                {
                    MessageBox.Show(this, "这个文件读不出来，换一份试试。", "导入失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                s.CopyFrom(loaded);
                s.Normalize();
                Themes.SetCustom(s.customThemes);
                store.Save(s);
                schedIndex = 0;
                LoadValues();
                MessageBox.Show(this, "配置已导入，重启悬浮窗后完全生效。", "导入完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ResetDefaults()
        {
            if (MessageBox.Show(this, "恢复默认会清掉现在的所有设置（含自建日程和主题），确定吗？", "恢复默认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var fresh = new Settings();
            fresh.Normalize();
            s.CopyFrom(fresh);
            Themes.SetCustom(s.customThemes);
            store.Save(s);
            schedIndex = 0;
            LoadValues();
        }

        // ---------------- 载入 / 保存 ----------------

        private void LoadValues()
        {
            bool flex = s.mode == "flex";
            rbFlex.Checked = flex;
            rbFixed.Checked = !flex;
            tbStart.Text = s.startTime;
            tbEnd.Text = s.endTime;
            numFlexH.Value = Math.Max(0, Math.Min(16, s.flexMinutes / 60));
            numFlexM.Value = Math.Max(0, Math.Min(59, s.flexMinutes % 60));
            UpdateFlexState();

            for (int i = 0; i < 7; i++) cbDays[i].Checked = s.workdays[i];
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

            numOpacity.Value = Math.Max(30, Math.Min(100, s.ui.opacity));
            numAutoCollapse.Value = Math.Max(0, Math.Min(120, s.ui.autoCollapseSeconds));
            cbTopMost.Checked = s.ui.alwaysOnTop;
            cbAutoStart.Checked = Startup.IsEnabled();
            cbShowLunch.Checked = s.ui.showLunch;
            cbReducedMotion.Checked = s.ui.reducedMotion;
            numReminderHold.Value = Math.Max(3, Math.Min(20, s.ui.reminderHoldSeconds));
            rbSpreadCard.Checked = s.ui.reminderSpread != "screen";
            rbSpreadScreen.Checked = s.ui.reminderSpread == "screen";
            rbStyleCircle.Checked = s.ui.reminderSpreadStyle != "bar";
            rbStyleBar.Checked = s.ui.reminderSpreadStyle == "bar";

            if (picker != null)
            {
                picker.SelectedId = s.ui.theme;
                picker.Invalidate();
            }
            UpdateThemeName();
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
                tabs.SelectedIndex = 0;
                MessageBox.Show(this, "上班时间的格式应为 HH:mm，例如 09:00。", "时间格式有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!SettingsExtensions.TryParseTime(tbEnd.Text, out endMin))
            {
                tabs.SelectedIndex = 0;
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
                    tabs.SelectedIndex = 1;
                    MessageBox.Show(this, "「" + f.name + "」的开始时间应为 HH:mm，例如 11:50。", "时间格式有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(f.end) && !SettingsExtensions.TryParseTime(f.end, out probe))
                {
                    tabs.SelectedIndex = 1;
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
            for (int i = 0; i < 7; i++) s.workdays[i] = cbDays[i].Checked;

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
            s.ui.alwaysOnTop = cbTopMost.Checked;
            s.ui.showLunch = cbShowLunch.Checked;
            s.ui.reducedMotion = cbReducedMotion.Checked;
            s.ui.reminderHoldSeconds = (int)numReminderHold.Value;
            s.ui.reminderSpread = rbSpreadScreen.Checked ? "screen" : "card";
            s.ui.reminderSpreadStyle = rbStyleBar.Checked ? "bar" : "circle";
            if (cbAutoStart.Checked != Startup.IsEnabled()) Startup.SetEnabled(cbAutoStart.Checked);

            s.Normalize();          // 固定日程同步回午休 / 晚休 / 下班的旧字段
            Themes.SetCustom(s.customThemes);
            store.Save(s);

            if (closeAfter)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            return true;
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
            l.ForeColor = Ink;
            l.BackColor = Color.Transparent;
            return l;
        }

        private static Label Hint(string text, int x, int y, int w, int h)
        {
            var l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, h);
            l.ForeColor = Muted;
            l.BackColor = Color.Transparent;
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
            b.FlatAppearance.BorderColor = Color.FromArgb(214, 220, 232);
            b.BackColor = Color.White;
            b.ForeColor = Ink;
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
                e.DrawBackground();
                Color a, b, ink;
                ReminderStyles.Resolve(ReminderStyles.Ids[e.Index], seed, out a, out b, out ink);
                var band = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 4, 26, e.Bounds.Height - 8);
                using (var br = new SolidBrush(a)) e.Graphics.FillRectangle(br, band.X, band.Y, band.Width, band.Height / 2);
                using (var br = new SolidBrush(b)) e.Graphics.FillRectangle(br, band.X, band.Y + band.Height / 2, band.Width, band.Height - band.Height / 2);
                using (var br = new SolidBrush(Color.FromArgb(38, 46, 64)))
                    e.Graphics.DrawString(c.Items[e.Index].ToString(), e.Font, br, e.Bounds.X + 36, e.Bounds.Y + 3);
                e.DrawFocusRectangle();
            };
            c.SetBounds(x, y, w, 24);
            for (int i = 0; i < ReminderStyles.Ids.Length; i++)
                if (ReminderStyles.Ids[i] == current) { c.SelectedIndex = i; break; }
            return c;
        }


    }

    /// <summary>设置窗口的共用色。</summary>
    internal static class Ui
    {
        public static readonly Color Accent = Color.FromArgb(64, 124, 245);
        public static readonly Color Ink = Color.FromArgb(38, 46, 64);
        public static readonly Color Muted = Color.FromArgb(120, 128, 146);
        public static readonly Color CardBg = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(230, 234, 242);
    }

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
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = Round(r, 10))
                {
                    using (var b = new SolidBrush(Ui.CardBg)) g.FillPath(b, path);
                    using (var p = new Pen(Ui.CardBorder, 1f)) g.DrawPath(p, path);
                }
                using (var f = UiFont.Make(9.5f, FontStyle.Bold))
                using (var b = new SolidBrush(Ui.Ink))
                    g.DrawString(Text, f, b, 14, 6);
                g.SmoothingMode = SmoothingMode.Default;
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

        /// <summary>主题色卡：一格一套配色，底色为主、高亮色压一条底边。</summary>
        public class ThemePicker : Control
        {
            private string selectedId = "";
            public event EventHandler SelectedIdChanged;

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
                    using (var path = Round(r, 5))
                    {
                        using (var b = new LinearGradientBrush(r, t.bgTop, t.bgBottom, 90f)) g.FillPath(b, path);
                        bool sel = t.id == selectedId;
                        using (var p = new Pen(sel ? Color.FromArgb(64, 124, 245) : Ui.CardBorder, sel ? 2f : 1f))
                            g.DrawPath(p, path);
                    }
                    int barH = Math.Max(3, r.Height / 5);
                    using (var b = new SolidBrush(t.accent))
                        g.FillRectangle(b, r.X + 2, r.Bottom - barH - 2, r.Width - 4, barH);
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

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
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
                            using (var b = new SolidBrush(sel ? Color.FromArgb(240, 245, 255) : Color.FromArgb(250, 251, 253)))
                                g.FillPath(b, path);
                            using (var pen = new Pen(sel ? Color.FromArgb(64, 124, 245) : Color.FromArgb(226, 231, 240), sel ? 2f : 1f))
                                g.DrawPath(pen, path);
                        }
                        Color a, b2, ink;
                        ReminderStyles.Resolve(f.styleId, Themes.Get("midnight"), out a, out b2, out ink);
                        int pad = (int)Math.Round(8 * k);
                        var chip = new Rectangle(r.X + pad, r.Y + pad, (int)Math.Round(8 * k), r.Height - pad * 2);
                        using (var br = new SolidBrush(a)) g.FillRectangle(br, chip.X, chip.Y, chip.Width, chip.Height / 2 + 1);
                        using (var br = new SolidBrush(b2)) g.FillRectangle(br, chip.X, chip.Y + chip.Height / 2 + 1, chip.Width, chip.Height - chip.Height / 2 - 1);
                        int tx = r.X + pad * 2 + chip.Width;
                        string title = f.name + (f.role == "lunch" && f.deduct ? "（扣工时）" : "");
                        if (!f.enabled) title += "  · 已停用";
                        using (var br = new SolidBrush(f.enabled ? Color.FromArgb(38, 46, 64) : Color.FromArgb(150, 156, 168)))
                            g.DrawString(title, fName, br, tx, r.Y + (int)Math.Round(4 * k));
                        string sub = f.IsOffWork
                            ? "按班次下班时间 · " + f.RepeatLabel
                            : f.start + (string.IsNullOrWhiteSpace(f.end) ? "" : "–" + f.end) + " · " + f.RepeatLabel;
                        if (!f.notify) sub += " · 不提醒";
                        using (var br = new SolidBrush(Color.FromArgb(120, 128, 146)))
                            g.DrawString(sub, fSub, br, tx, r.Y + (int)Math.Round(24 * k));
                    }
                }

                // 内容超出一屏就画一条细滚动条，告诉用户下面还有
                if (NeedsBar)
                {
                    using (var track = new SolidBrush(Color.FromArgb(238, 241, 246)))
                        g.FillRectangle(track, Width - BarW, 0, BarW, Height);
                    var th = ThumbRect();
                    using (var path = RoundRect(new Rectangle(th.X, th.Y, th.Width - 1, th.Height - 1), BarW / 2))
                    using (var br = new SolidBrush(Color.FromArgb(188, 196, 210)))
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
                using (var bg = new SolidBrush(Color.FromArgb(247, 248, 251)))
                    g.FillRectangle(bg, 0, 0, Width, Height);
                using (var bp = new Pen(Color.FromArgb(232, 236, 244)))
                    g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
                float k = K;
                if (items.Count == 0)
                {
                    var sfEmpty = new StringFormat();
                    sfEmpty.Alignment = StringAlignment.Center;
                    sfEmpty.LineAlignment = StringAlignment.Center;
                    using (var brEmpty = new SolidBrush(Color.FromArgb(150, 156, 168)))
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
                            using (var b = new SolidBrush(sel ? Color.FromArgb(240, 245, 255) : Color.White))
                                g.FillPath(b, path);
                            using (var pen = new Pen(sel ? Color.FromArgb(64, 124, 245) : Color.FromArgb(226, 231, 240), sel ? 2f : 1f))
                                g.DrawPath(pen, path);
                        }
                        DateTime start = Reminders.ParseStart(ev);
                        string head = start == DateTime.MinValue ? "时间无效" : start.ToString("MM-dd HH:mm") + " 提醒";
                        if (start != DateTime.MinValue && start < DateTime.Now) head = "已过 · " + start.ToString("MM-dd HH:mm");
                        int pad = (int)Math.Round(8 * k);
                        using (var br = new SolidBrush(Color.FromArgb(130, 138, 154)))
                            g.DrawString(head, fHead, br, r.X + pad, r.Y + (int)Math.Round(4 * k));
                        using (var br = new SolidBrush(Color.FromArgb(38, 46, 64)))
                            g.DrawString(Clip(ev.title, r.Width - pad * 2, fTitle, g), fTitle, br, r.X + pad, r.Y + (int)Math.Round(18 * k));
                        if (open)
                        {
                            string when = Reminders.TimeRange(ev);
                            string place = string.IsNullOrWhiteSpace(ev.place) ? "未填位置" : ev.place;
                            string body = when + " · " + place;
                            using (var br = new SolidBrush(Color.FromArgb(96, 104, 122)))
                                g.DrawString(body, fBody, br, r.X + pad, r.Y + (int)Math.Round(46 * k));
                            string detail = string.IsNullOrWhiteSpace(ev.detail) ? "（没有备注）" : ev.detail;
                            using (var br = new SolidBrush(Color.FromArgb(96, 104, 122)))
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

        /// <summary>自己配色：色相条 + 明度/饱和方块，选完起个名字存成主题。</summary>
        public class ColorWheelForm : Form
        {
            public string ThemeName = "我的主题";
            public string AccentHex = "#4C8DFF";

            private double hue, sat = 0.75, val = 0.85;
            private SvPanel sv;
            private HuePanel hueBar;
            private TextBox tbName;
            private Label lblHex;
            private PreviewSwatch preview;

            public ColorWheelForm(ThemeDef seed)
            {
                Text = "新建主题";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;
                AutoScaleDimensions = new SizeF(96f, 96f);
                AutoScaleMode = AutoScaleMode.None;
                ClientSize = new Size(452, 330);
                Font = UiFont.Make(9f, FontStyle.Regular);
                BackColor = Color.White;
                if (seed != null) FromColor(seed.accent);

                sv = new SvPanel();
                sv.SetBounds(16, 16, 220, 180);
                sv.Changed += delegate { SyncFromHsv(); };
                Controls.Add(sv);

                hueBar = new HuePanel();
                hueBar.SetBounds(244, 16, 22, 180);
                hueBar.Changed += delegate { hue = hueBar.Hue; SyncFromHsv(); };
                Controls.Add(hueBar);

                preview = new PreviewSwatch();
                preview.SetBounds(280, 16, 156, 76);
                Controls.Add(preview);

                var l1 = new Label();
                l1.Text = "名字";
                l1.SetBounds(280, 100, 40, 22);
                l1.ForeColor = Ui.Muted;
                Controls.Add(l1);

                tbName = new TextBox();
                tbName.SetBounds(280, 122, 156, 24);
                tbName.Text = "我的主题";
                tbName.TextChanged += delegate { ThemeName = tbName.Text.Trim(); };
                Controls.Add(tbName);

                lblHex = new Label();
                lblHex.SetBounds(280, 152, 156, 20);
                lblHex.ForeColor = Ui.Muted;
                Controls.Add(lblHex);

                var tips = new Label();
                tips.Text = "色相条选颜色家族，方块里选深浅；右边是这套配色做出来的卡片。";
                tips.SetBounds(16, 206, 420, 40);
                tips.ForeColor = Ui.Muted;
                Controls.Add(tips);

                var ok = new Button();
                ok.Text = "保存";
                ok.SetBounds(240, 262, 96, 32);
                ok.FlatStyle = FlatStyle.Flat;
                ok.FlatAppearance.BorderSize = 0;
                ok.BackColor = Ui.Accent;
                ok.ForeColor = Color.White;
                ok.Click += delegate
                {
                    ThemeName = tbName.Text.Trim();
                    if (ThemeName.Length == 0) ThemeName = "我的主题";
                    AccentHex = HexOf(ColorFromHsv(hue, sat, val));
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(ok);

                var cancel = new Button();
                cancel.Text = "取消";
                cancel.SetBounds(344, 262, 92, 32);
                cancel.FlatStyle = FlatStyle.Flat;
                cancel.FlatAppearance.BorderColor = Color.FromArgb(214, 220, 232);
                cancel.BackColor = Color.White;
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
                if (lblHex != null) lblHex.Text = HexOf(c) + " · 主色";
                if (preview != null) { preview.Accent = c; preview.Invalidate(); }
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

            /// <summary>预览：这套主色做出来的卡片和提醒色带。</summary>
            public class PreviewSwatch : Control
            {
                public Color Accent = Color.FromArgb(76, 141, 255);

                public PreviewSwatch()
                {
                    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;   // 白底上的小字用 ClearType 才实
                    g.Clear(Color.White);
                    ThemeDef t = Themes.FromAccent("preview", "预览", HexOf(Accent));
                    var r = new Rectangle(0, 0, Width - 1, 44);
                    using (var b = new LinearGradientBrush(r, t.bgTop, t.bgBottom, 90f))
                        g.FillRectangle(b, r);
                    using (var f = UiFont.Make(12f, FontStyle.Bold))
                    using (var b = new SolidBrush(t.textMain))
                        g.DrawString("12:34", f, b, 8, 10);
                    using (var b = new SolidBrush(t.accent)) g.FillRectangle(b, 8, 34, 40, 4);
                    Color a, b2, ink;
                    ReminderStyles.Resolve("theme", t, out a, out b2, out ink);
                    var band = new Rectangle(0, 48, Width - 1, 28);
                    using (var br = new SolidBrush(a)) g.FillRectangle(br, band.X, band.Y, band.Width, 16);
                    using (var br = new SolidBrush(b2)) g.FillRectangle(br, band.X, band.Y + 16, band.Width, 12);
                    using (var p = new Pen(Color.FromArgb(210, 216, 228))) g.DrawRectangle(p, band.X, band.Y, band.Width - 1, band.Height - 1);
                }
            }
        }
}
