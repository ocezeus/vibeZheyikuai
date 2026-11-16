using System;
using System.Collections.Generic;

namespace WorkCountdown
{
    /// <summary>
    /// 应用设置时能观察到的运行状态：哪些计时器正在跑、小纸条连接是否已经建立。
    /// 只记录“正在运行”的事实，不保存配置值；配置值由变化计划比较旧/新规范化配置得到。
    /// </summary>
    public sealed class SettingsApplyState
    {
        public bool AttendancePollRunning;
        public bool CalendarPollRunning;
        public bool OutlookPollRunning;
        public bool NoteRelayRunning;
        /// <summary>同步模块当前配置指纹；变化计划用它判断是否真的需要通知同步模块。</summary>
        public string SyncFingerprint;
    }

    /// <summary>
    /// 配置变化计划：按旧/新规范化配置和运行状态，列出这次真正需要通知模块的变化。
    /// 调用者只把计划交给 applier，不再自己写字段比较，也不需要理解副作用先后。
    /// </summary>
    public sealed class SettingsChangePlan
    {
        /// <summary>第一次应用（启动 / 首次引导）：所有仍然需要的模块都要初始化一次。</summary>
        public bool FirstApply { get; internal set; }
        /// <summary>中文字体 / 数字字体变化，只需重建字体资源。</summary>
        public bool FontsChanged { get; internal set; }
        /// <summary>主题、皮肤、透明度、背景风格等显示资源变化，只需刷新卡片与托盘显示。</summary>
        public bool DisplayChanged { get; internal set; }
        /// <summary>窗口置顶开关变化；位置 / 贴边仍由调用者按既有即时契约处理。</summary>
        public bool TopMostChanged { get; internal set; }
        /// <summary>考勤轮询开关或间隔变化。</summary>
        public bool AttendancePollChanged { get; internal set; }
        /// <summary>飞书日程轮询开关或间隔变化。</summary>
        public bool CalendarPollChanged { get; internal set; }
        /// <summary>Outlook 日历轮询开关或间隔变化。</summary>
        public bool OutlookPollChanged { get; internal set; }
        /// <summary>会改变同步结果的配置变化；真正的代次推进与旧请求作废由 SyncEngine.Configure 负责。</summary>
        public bool SyncConfigChanged { get; internal set; }
        /// <summary>小纸条连接身份（ID / 中继地址 / 端口）变化，需要重建连接。</summary>
        public bool NoteRelayRestart { get; internal set; }
        /// <summary>昵称 / 对话订阅变化，保持现有连接原地刷新。</summary>
        public bool NoteSubscriptionsRefresh { get; internal set; }
        /// <summary>小纸条面板需要刷新数据或配色。</summary>
        public bool NotePanelRefresh { get; internal set; }
        /// <summary>小纸条面板布局（抽屉 / 双栏）变化，需要重建面板。</summary>
        public bool NotePanelRelayout { get; internal set; }
        /// <summary>工资被清空，已经展开的工资面板要收起。</summary>
        public bool SalaryPanelClose { get; internal set; }
        /// <summary>班次 / 工作日 / 固定日程 / 手动日程 / 工资等卡片内容变化，需要重算一次。</summary>
        public bool CardContentChanged { get; internal set; }

        public bool HasChanges
        {
            get
            {
                return FirstApply || FontsChanged || DisplayChanged || TopMostChanged
                    || AttendancePollChanged || CalendarPollChanged || OutlookPollChanged
                    || SyncConfigChanged || NoteRelayRestart || NoteSubscriptionsRefresh
                    || NotePanelRefresh || NotePanelRelayout || SalaryPanelClose || CardContentChanged;
            }
        }
    }

    /// <summary>配置变化计划的唯一比较入口；调用者不写字段比较，也不判断先后顺序。</summary>
    public static class SettingsChangePlanner
    {
        public static SettingsChangePlan Plan(Settings previous, Settings next, SettingsApplyState state)
        {
            var plan = new SettingsChangePlan();
            if (next == null) return plan;
            if (state == null) state = new SettingsApplyState();

            if (previous == null)
            {
                // 首次应用：把仍然需要初始化的模块各通知一次。
                plan.FirstApply = true;
                plan.FontsChanged = true;
                plan.DisplayChanged = true;
                plan.TopMostChanged = true;
                plan.AttendancePollChanged = true;
                plan.CalendarPollChanged = true;
                plan.OutlookPollChanged = true;
                plan.SyncConfigChanged = true;
                plan.NoteRelayRestart = true;
                return plan;
            }

            UiSettings prevUi = previous.ui == null ? new UiSettings() : previous.ui;
            UiSettings nextUi = next.ui == null ? new UiSettings() : next.ui;
            LarkSettings prevLark = previous.lark == null ? new LarkSettings() : previous.lark;
            LarkSettings nextLark = next.lark == null ? new LarkSettings() : next.lark;

            plan.FontsChanged = !SameText(FontKey(next), FontKey(previous), true);
            plan.DisplayChanged = !SameText(prevUi.skin, nextUi.skin, false)
                || !SameText(prevUi.theme, nextUi.theme, false)
                || prevUi.opacity != nextUi.opacity
                || !SameText(prevUi.bgStyle, nextUi.bgStyle, false)
                || prevUi.showLunch != nextUi.showLunch
                || prevUi.reducedMotion != nextUi.reducedMotion
                || !SameText(prevUi.cardGlow, nextUi.cardGlow, false)
                || !SameThemes(previous.customThemes, next.customThemes);
            plan.TopMostChanged = prevUi.alwaysOnTop != nextUi.alwaysOnTop;

            bool wantAttendance = nextLark.enabled;
            bool wantCalendar = next.calendarEnabled && nextLark.enabled;
            bool wantOutlook = next.outlook != null && next.outlook.HasEnabled;
            plan.AttendancePollChanged = prevLark.enabled != nextLark.enabled
                || prevLark.pollMinutes != nextLark.pollMinutes
                || state.AttendancePollRunning != wantAttendance;
            plan.CalendarPollChanged = previous.calendarEnabled != next.calendarEnabled
                || prevLark.enabled != nextLark.enabled
                || previous.calendarPollMinutes != next.calendarPollMinutes
                || state.CalendarPollRunning != wantCalendar;
            bool prevWantOutlook = previous.outlook != null && previous.outlook.HasEnabled;
            plan.OutlookPollChanged = prevWantOutlook != wantOutlook
                || previous.calendarPollMinutes != next.calendarPollMinutes
                || state.OutlookPollRunning != wantOutlook;
            plan.SyncConfigChanged = !string.Equals(state.SyncFingerprint ?? "",
                SyncEngine.Fingerprint(next) ?? "", StringComparison.Ordinal);

            NoteSettings prevNote = previous.note == null ? new NoteSettings() : previous.note;
            NoteSettings nextNote = next.note == null ? new NoteSettings() : next.note;
            bool noteIdentityChanged = !SameText(RelayKey(prevNote), RelayKey(nextNote), false);
            bool noteRuntimeMissing = !state.NoteRelayRunning;
            plan.NoteRelayRestart = noteIdentityChanged || noteRuntimeMissing;

            bool nicknameChanged = !SameText(NoteSettings.CleanNick(prevNote.nickname),
                NoteSettings.CleanNick(nextNote.nickname), false);
            bool contactsChanged = !SameContacts(prevNote.contacts, nextNote.contacts);
            plan.NoteSubscriptionsRefresh = !plan.NoteRelayRestart && (nicknameChanged || contactsChanged);
            plan.NotePanelRelayout = !SameText(prevNote.style, nextNote.style, false);
            plan.NotePanelRefresh = plan.DisplayChanged || plan.FontsChanged || plan.TopMostChanged
                || plan.NotePanelRelayout || nicknameChanged || contactsChanged;

            plan.SalaryPanelClose = previous.salary != null && previous.salary.monthly > 0m
                && (next.salary == null || next.salary.monthly <= 0m);
            plan.CardContentChanged = !SameSchedule(previous, next)
                || !SameSalary(previous.salary, next.salary)
                || !SameEvents(previous.events, next.events);
            return plan;
        }

        private static string FontKey(Settings settings)
        {
            UiSettings ui = settings.ui == null ? new UiSettings() : settings.ui;
            string text = (ui.textFont == null ? "" : ui.textFont.Trim());
            string number = string.IsNullOrWhiteSpace(ui.numberFont) ? "Consolas" : ui.numberFont.Trim();
            return text + "\u0001" + number;
        }

        private static string RelayKey(NoteSettings note)
        {
            string host = note.relayHost == null ? "" : note.relayHost.Trim();
            return (note.id == null ? "" : note.id.Trim()) + "\u0001" + host + "\u0001" + note.relayPort.ToString();
        }

        private static bool SameText(string a, string b, bool ignoreCase)
        {
            return string.Equals(a == null ? "" : a.Trim(), b == null ? "" : b.Trim(),
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        private static bool SameThemes(List<CustomTheme> a, List<CustomTheme> b)
        {
            int countA = a == null ? 0 : a.Count;
            int countB = b == null ? 0 : b.Count;
            if (countA != countB) return false;
            for (int i = 0; i < countA; i++)
            {
                CustomTheme x = a[i];
                CustomTheme y = b[i];
                if (x == null || y == null)
                {
                    if (x != y) return false;
                    continue;
                }
                if (!SameText(x.id, y.id, false) || !SameText(x.name, y.name, false)
                    || !SameText(x.accent, y.accent, false) || !SameText(x.bg, y.bg, false)
                    || !SameText(x.bar, y.bar, false)) return false;
            }
            return true;
        }


        private static bool SameSchedule(Settings a, Settings b)
        {
            if (!SameText(a.mode, b.mode, false) || !SameText(a.startTime, b.startTime, false)
                || !SameText(a.endTime, b.endTime, false) || a.flexMinutes != b.flexMinutes
                || a.lunchDeduct != b.lunchDeduct || !SameText(a.lunchStart, b.lunchStart, false)
                || !SameText(a.lunchEnd, b.lunchEnd, false) || a.eveningEnabled != b.eveningEnabled
                || !SameText(a.eveningStart, b.eveningStart, false) || !SameText(a.eveningEnd, b.eveningEnd, false)
                || a.breakNotify != b.breakNotify || a.calendarLeadMinutes != b.calendarLeadMinutes
                || a.eventLeadMinutes != b.eventLeadMinutes) return false;
            bool[] wa = a.workdays == null ? new bool[0] : a.workdays;
            bool[] wb = b.workdays == null ? new bool[0] : b.workdays;
            if (wa.Length != wb.Length) return false;
            for (int i = 0; i < wa.Length; i++) if (wa[i] != wb[i]) return false;
            if (!SameExceptions(a.exceptions, b.exceptions)) return false;
            if (!SameSchedules(a.schedules, b.schedules)) return false;
            return true;
        }

        private static bool SameSalary(SalarySettings a, SalarySettings b)
        {
            decimal am = a == null ? 0m : a.monthly;
            decimal bm = b == null ? 0m : b.monthly;
            int ap = a == null ? 0 : a.payday;
            int bp = b == null ? 0 : b.payday;
            return am == bm && ap == bp;
        }

        private static bool SameExceptions(List<HolidayEntry> a, List<HolidayEntry> b)
        {
            int countA = a == null ? 0 : a.Count;
            int countB = b == null ? 0 : b.Count;
            if (countA != countB) return false;
            for (int i = 0; i < countA; i++)
            {
                HolidayEntry x = a[i];
                HolidayEntry y = b[i];
                if (x == null || y == null)
                {
                    if (x != y) return false;
                    continue;
                }
                if (!SameText(x.date, y.date, false) || !SameText(x.kind, y.kind, false)) return false;
            }
            return true;
        }

        private static bool SameSchedules(List<FixedSchedule> a, List<FixedSchedule> b)
        {
            int countA = a == null ? 0 : a.Count;
            int countB = b == null ? 0 : b.Count;
            if (countA != countB) return false;
            for (int i = 0; i < countA; i++)
            {
                FixedSchedule x = a[i];
                FixedSchedule y = b[i];
                if (x == null || y == null)
                {
                    if (x != y) return false;
                    continue;
                }
                if (!SameText(x.id, y.id, false) || !SameText(x.name, y.name, false)
                    || !SameText(x.role, y.role, false) || !SameText(x.start, y.start, false)
                    || !SameText(x.end, y.end, false) || !SameText(x.repeat, y.repeat, false)
                    || !SameText(x.until, y.until, false) || x.enabled != y.enabled
                    || x.notify != y.notify || x.endNotify != y.endNotify
                    || x.deduct != y.deduct || !SameText(x.styleId, y.styleId, false)) return false;
            }
            return true;
        }

        private static bool SameEvents(List<ReminderEvent> a, List<ReminderEvent> b)
        {
            int countA = a == null ? 0 : a.Count;
            int countB = b == null ? 0 : b.Count;
            if (countA != countB) return false;
            for (int i = 0; i < countA; i++)
            {
                ReminderEvent x = a[i];
                ReminderEvent y = b[i];
                if (x == null || y == null)
                {
                    if (x != y) return false;
                    continue;
                }
                if (!SameText(x.id, y.id, false) || !SameText(x.title, y.title, false)
                    || !SameText(x.start, y.start, false) || !SameText(x.end, y.end, false)
                    || !SameText(x.place, y.place, false) || !SameText(x.detail, y.detail, false)
                    || !SameText(x.source, y.source, false) || x.leadMinutes != y.leadMinutes) return false;
            }
            return true;
        }

        private static bool SameContacts(List<NoteContact> a, List<NoteContact> b)
        {
            int countA = a == null ? 0 : a.Count;
            int countB = b == null ? 0 : b.Count;
            if (countA != countB) return false;
            for (int i = 0; i < countA; i++)
            {
                NoteContact x = a[i];
                NoteContact y = b[i];
                if (x == null || y == null)
                {
                    if (x != y) return false;
                    continue;
                }
                if (!SameText(x.id, y.id, false) || !SameText(x.remark, y.remark, false)
                    || !SameText(x.nick, y.nick, false)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 应用设置的副作用 seam：生产实现由 WidgetForm 提供，测试用可记录调用的 adapter。
    /// 每个方法只做一件具体的事，先后顺序由 SettingsApplyCoordinator 统一安排。
    /// </summary>
    public interface ISettingsApplyTarget
    {
        SettingsApplyState CaptureState();
        /// <summary>让状态拥有者切换到新配置；提交失败时不会被调用。</summary>
        void AdoptSettings(Settings settings);
        /// <summary>把同步相关配置交给 SyncEngine.Configure（代次 / 旧 ticket 作废由同步模块负责）。</summary>
        void ConfigureSync(Settings settings);
        void ApplyAttendancePoll(Settings settings);
        void ApplyCalendarPoll(Settings settings);
        void ApplyOutlookPoll(Settings settings);
        /// <summary>小纸条连接身份变化时重建连接。</summary>
        void RestartNoteRelay(Settings settings);
        /// <summary>连接不变，原地刷新订阅与 presence（昵称 / 对话变化）。</summary>
        void RefreshNoteRelay(Settings settings);
        void ApplyFonts(Settings settings);
        void ApplyDisplayResources(Settings settings);
        void ApplyTopMost(Settings settings);
        /// <summary>刷新小纸条面板；relayout=true 表示布局变化，需要重建。</summary>
        void ApplyNotePanel(Settings settings, bool relayout);
        void ApplySalaryState(Settings settings);
        /// <summary>所有变化落地后的统一收尾：重算卡片、刷新区域。</summary>
        void AfterApply(Settings settings);
    }

    /// <summary>
    /// 应用协调器：持有上一份已应用配置的规范化快照，比较并只在确有变化时按固定顺序通知 target。
    /// 相同配置重复应用不会重建连接、重启计时器或关闭面板。
    /// </summary>
    public sealed class SettingsApplyCoordinator
    {
        private readonly ISettingsApplyTarget target;
        private Settings applied;

        public SettingsApplyCoordinator(ISettingsApplyTarget target)
        {
            if (target == null) throw new ArgumentNullException("target");
            this.target = target;
        }

        public SettingsChangePlan Apply(Settings next)
        {
            if (next == null) return new SettingsChangePlan();
            SettingsApplyState state = target.CaptureState();
            SettingsChangePlan plan = SettingsChangePlanner.Plan(applied, next, state);
            target.AdoptSettings(next);
            if (plan.HasChanges)
            {
                // 顺序集中在这里：先让同步模块推进代次，再启动/停止轮询与连接，最后刷新显示。
                if (plan.SyncConfigChanged) target.ConfigureSync(next);
                if (plan.AttendancePollChanged) target.ApplyAttendancePoll(next);
                if (plan.CalendarPollChanged) target.ApplyCalendarPoll(next);
                if (plan.OutlookPollChanged) target.ApplyOutlookPoll(next);
                if (plan.NoteRelayRestart) target.RestartNoteRelay(next);
                else if (plan.NoteSubscriptionsRefresh) target.RefreshNoteRelay(next);
                if (plan.FontsChanged) target.ApplyFonts(next);
                if (plan.DisplayChanged) target.ApplyDisplayResources(next);
                if (plan.TopMostChanged) target.ApplyTopMost(next);
                if (plan.NotePanelRelayout) target.ApplyNotePanel(next, true);
                else if (plan.NotePanelRefresh) target.ApplyNotePanel(next, false);
                if (plan.SalaryPanelClose) target.ApplySalaryState(next);
                target.AfterApply(next);
            }
            applied = next.Clone();
            return plan;
        }
    }

    /// <summary>一次显式配置提交的结果：保存是否成功、是否真的应用了变化、是否需要保留编辑。</summary>
    public sealed class SettingsCommitResult
    {
        public SettingsSaveResult Save { get; private set; }
        public SettingsChangePlan Plan { get; private set; }
        public bool Applied { get; private set; }

        public SettingsCommitResult(SettingsSaveResult save, SettingsChangePlan plan, bool applied)
        {
            Save = save;
            Plan = plan;
            Applied = applied;
        }

        public bool IsSuccess { get { return Save != null && Save.Succeeded && Applied; } }
        public bool IsFailed { get { return Save == null || Save.IsFailed; } }
        public bool IsSkipped { get { return Save != null && Save.IsSkipped; } }
        public bool ShouldKeepEditing { get { return !IsSuccess; } }
        public string Message { get { return Save == null ? "" : Save.Message; } }
    }

    /// <summary>
    /// 配置提交：校验在设置窗口完成，这里只负责“先保存、成功后才应用”的先后与失败行为。
    /// 失败 / WC_NO_SAVE 跳过都不应用变化，调用者据此保留编辑内容并给出可见反馈。
    /// </summary>
    public sealed class SettingsCommitCoordinator
    {
        private readonly SettingsStore store;
        private readonly SettingsApplyCoordinator apply;

        public SettingsCommitCoordinator(SettingsStore store, SettingsApplyCoordinator apply)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (apply == null) throw new ArgumentNullException("apply");
            this.store = store;
            this.apply = apply;
        }

        public SettingsCommitResult Commit(Settings candidate, string reason)
        {
            if (candidate == null)
                return new SettingsCommitResult(SettingsSaveResult.Failure("没有可提交的配置。"), null, false);
            SettingsSaveResult save = store.Save(candidate);
            if (!save.Succeeded)
            {
                // WC_NO_SAVE 跳过属于探针语义，不写日志文件；只有真正写失败才留诊断记录。
                if (save.IsFailed)
                    store.Log("提交设置失败（" + (reason == null ? "" : reason) + "）：" + save.Message);
                return new SettingsCommitResult(save, null, false);
            }
            SettingsChangePlan plan = apply.Apply(candidate);
            return new SettingsCommitResult(save, plan, true);
        }
    }
}
