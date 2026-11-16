using System;
using System.Collections.Generic;

namespace WorkCountdown
{
    /// <summary>提醒的原因：调用方只按它决定展示路径，不用自己推断排序或去重。</summary>
    public static class ReminderReasons
    {
        public const string Due = "due";                       // 提前提醒
        public const string Late = "late";                     // 补提醒
        public const string FixedStart = "fixed-start";         // 固定日程开始
        public const string FixedEnd = "fixed-end";             // 固定日程结束
        public const string FixedResume = "fixed-resume";       // 中途启动，正处在固定日程里
        public const string OffWorkStart = "offwork-start";     // 常驻下班提醒开始
        public const string OffWorkResume = "offwork-resume";   // 被别的提醒打断后恢复
        public const string OffWorkRepeat = "offwork-repeat";   // 每 5 分钟重播
        public const string OffWorkStopped = "offwork-stopped"; // 打卡同步 / 忽略今天后收起
    }

    /// <summary>一条可检查的提醒决定：只带业务信息和原因，不带计时器 / Form / 动画标志。</summary>
    public class ReminderDecision
    {
        /// <summary>ReminderReasons 之一。</summary>
        public string Reason { get; set; }

        /// <summary>要展示的日程；offwork-stopped 时为空。</summary>
        public ReminderEvent Event { get; set; }
    }

    /// <summary>一次推进拿到的所有决定，按应用顺序排列（窗口依次展示，最后一条留在前台）。</summary>
    public class ReminderOutcome
    {
        public List<ReminderDecision> Decisions { get; private set; }

        public ReminderOutcome()
        {
            Decisions = new List<ReminderDecision>();
        }

        /// <summary>这次推进是否起了 / 换了前台提醒（offwork-stopped 不算）。</summary>
        public bool Started
        {
            get
            {
                foreach (var d in Decisions)
                    if (d != null && d.Event != null) return true;
                return false;
            }
        }
    }

    /// <summary>推进一次时间时，窗口已经知道的展示与考勤信号。</summary>
    public class ReminderSignals
    {
        /// <summary>考勤推算：现在该提示下班。</summary>
        public bool OffWorkDue { get; set; }

        /// <summary>预计下班时间（status.targetIso，形如 yyyy-MM-ddTHH:mm）。</summary>
        public string OffWorkTarget { get; set; }

        /// <summary>当前有提醒在展示。</summary>
        public bool Showing { get; set; }
    }

    /// <summary>各日历最近一次同步下来的日程快照；模块自己负责排序与来源过滤。</summary>
    public class ReminderScheduleSnapshot
    {
        public List<ReminderEvent> Lark { get; private set; }
        public List<ReminderEvent> Outlook { get; private set; }

        public ReminderScheduleSnapshot() : this(null, null) { }

        public ReminderScheduleSnapshot(List<ReminderEvent> lark, List<ReminderEvent> outlook)
        {
            Lark = lark ?? new List<ReminderEvent>();
            Outlook = outlook ?? new List<ReminderEvent>();
        }
    }

    /// <summary>
    /// 拥有提醒决策状态的深模块：日程选择、固定日程、补提醒、去重与关闭反馈都在这里。
    /// 输入是明确时间、业务配置和日程快照，输出是 ReminderDecision；窗口只负责展示与交互。
    /// </summary>
    public class ReminderEngine
    {
        private const int NotifiedLimit = 300;
        private const int OffWorkRepeatMs = 5 * 60 * 1000;

        // 去重集合：key -> 这条提醒彻底不再可能触发的时刻（到期即可清理，不靠清空）
        private readonly Dictionary<string, DateTime> notified = new Dictionary<string, DateTime>();
        private DateTime mutedOffWorkDay = DateTime.MinValue;
        private bool offWorkActive;
        private DateTime offWorkNext = DateTime.MinValue;
        private DateTime lastSweepDay = DateTime.MinValue;

        public ReminderEngine()
        {
        }

        /// <summary>当前仍在去重集合里的条目数（清理策略可检查）。</summary>
        public int NotifiedCount
        {
            get { return notified.Count; }
        }

        /// <summary>最早一个还没开始的日程；调用方不需要先排序。</summary>
        public ReminderEvent NextEvent(Settings settings, ReminderScheduleSnapshot snapshot, DateTime now)
        {
            foreach (var e in Agenda(settings, snapshot))
                if (Reminders.ParseStart(e) > now) return e;
            return null;
        }

        /// <summary>推进一次时间，返回这一 tick 要应用的决定。</summary>
        public ReminderOutcome Advance(Settings settings, ReminderScheduleSnapshot snapshot, DateTime now, ReminderSignals signals)
        {
            var outcome = new ReminderOutcome();
            if (settings == null) return outcome;
            if (signals == null) signals = new ReminderSignals();
            Sweep(now);
            settings.NormalizeSchedules();

            // 1. 常驻「该下班啦」：打卡后收起；到点后每 5 分钟重播
            var offSched = settings.ScheduleOf("offwork");
            bool offOk = offSched == null || (offSched.enabled && offSched.notify && offSched.OccursOn(now));
            bool need = signals.OffWorkDue && offOk && mutedOffWorkDay != now.Date;
            if (!need)
            {
                if (offWorkActive)
                {
                    offWorkActive = false;
                    offWorkNext = DateTime.MinValue;
                    outcome.Decisions.Add(new ReminderDecision { Reason = ReminderReasons.OffWorkStopped });
                }
            }
            else if (!offWorkActive)
            {
                offWorkActive = true;
                offWorkNext = now.AddMilliseconds(OffWorkRepeatMs);
                outcome.Decisions.Add(new ReminderDecision
                {
                    Reason = ReminderReasons.OffWorkStart,
                    Event = BuildOffWorkEvent(signals, now)
                });
            }
            else if (!signals.Showing)
            {
                // 被普通日程 / 固定日程打断过：它收起后回到常驻下班提醒
                outcome.Decisions.Add(new ReminderDecision
                {
                    Reason = ReminderReasons.OffWorkResume,
                    Event = BuildOffWorkEvent(signals, now)
                });
            }
            else if (now >= offWorkNext)
            {
                // 每 5 分钟原地重播一次入场动画（展示层决定重播表现）
                offWorkNext = now.AddMilliseconds(OffWorkRepeatMs);
                outcome.Decisions.Add(new ReminderDecision
                {
                    Reason = ReminderReasons.OffWorkRepeat,
                    Event = BuildOffWorkEvent(signals, now)
                });
            }

            // 2. 固定日程：先开始、再结束、最后「中途启动」；一次推进最多起一条，优先于普通日程
            if (Policy.Build(settings, now).isWorkday && settings.schedules != null)
            {
                bool fixedFired = false;
                foreach (var f in settings.schedules)
                    if (FixedEligible(f, now) && TryFixed(f, now, false, outcome)) { fixedFired = true; break; }
                if (!fixedFired)
                    foreach (var f in settings.schedules)
                        if (FixedEligible(f, now) && TryFixed(f, now, true, outcome)) { fixedFired = true; break; }
                if (!fixedFired)
                    foreach (var f in settings.schedules)
                        if (FixedEligible(f, now) && TryFixedResume(f, now, outcome)) { fixedFired = true; break; }
                if (fixedFired) return outcome;
            }

            // 3. 日程：正常提前提醒 / 补提醒；同一 tick 只起一条
            foreach (var e in Agenda(settings, snapshot))
            {
                string key = EventKey(e);
                if (notified.ContainsKey(key)) continue;
                bool due = Reminders.IsDue(settings, e, now);
                bool late = !due && Reminders.IsLateDue(settings, e, now);
                if (!due && !late) continue;
                Remember(key, EventExpiry(e));
                outcome.Decisions.Add(new ReminderDecision
                {
                    Reason = due ? ReminderReasons.Due : ReminderReasons.Late,
                    Event = e
                });
                break;
            }
            return outcome;
        }

        /// <summary>
        /// 用户关闭当前提醒（卡片 ×、全屏关闭、右键关闭都走这里）。
        /// 关闭的两种含义都在模块内生效：记入去重（同一事项不再提醒）与下班提醒的「忽略今天」；
        /// 调用方不需要读返回标志位，下一次 Advance 的结果就是行为。
        /// </summary>
        public void Dismiss(ReminderEvent e, DateTime now)
        {
            if (e == null || e.id == "preview") return;   // 预览不写真实提醒决策状态
            Remember(EventKey(e), EventExpiry(e));
            if (e.source == "alert")
            {
                // 下班提醒的 × = 忽略今天（第二天自动恢复）
                mutedOffWorkDay = now.Date;
                offWorkActive = false;
                offWorkNext = DateTime.MinValue;
            }
        }

        /// <summary>右键「忽略今天的下班提醒」。</summary>
        public void MuteOffWork(DateTime now)
        {
            mutedOffWorkDay = now.Date;
            offWorkActive = false;
            offWorkNext = DateTime.MinValue;
        }

        /// <summary>提醒常驻到什么时候（到时段结束 / 下班打卡 / 至少停留一会儿）。</summary>
        public DateTime HoldUntil(Settings settings, ReminderEvent e, DateTime now)
        {
            DateTime min = now.AddMilliseconds(HoldMs(settings));
            if (e == null) return min;
            if (e.id == "preview") return min;
            if (e.source == "alert") return DateTime.MaxValue;
            DateTime end = Reminders.ParseEnd(e);
            if (e.source == "break")
            {
                if (end == DateTime.MinValue) return min;
                return end > min ? end : min;
            }
            if (end == DateTime.MinValue) end = now.AddMinutes(30);
            if (end <= now) return min;
            return end > min ? end : min;
        }

        private static int HoldMs(Settings settings)
        {
            int seconds = settings == null || settings.ui == null ? 6 : settings.ui.reminderHoldSeconds;
            return Math.Max(3, Math.Min(20, seconds)) * 1000;
        }

        private static bool FixedEligible(FixedSchedule f, DateTime now)
        {
            return f != null && f.enabled && f.notify && !f.IsOffWork && f.OccursOn(now);
        }

        /// <summary>固定日程开始（ending=false）/ 结束（ending=true）提醒，每天各一次。</summary>
        private bool TryFixed(FixedSchedule f, DateTime now, bool ending, ReminderOutcome outcome)
        {
            if (ending && (!f.endNotify || string.IsNullOrWhiteSpace(f.end))) return false;
            DateTime at = ending ? f.EndAt(now) : f.StartAt(now);
            if (at == DateTime.MinValue) return false;
            if (now.Hour * 60 + now.Minute != at.Hour * 60 + at.Minute) return false;
            string key = "fix:" + f.id + (ending ? ":end" : "") + "@" + now.ToString("yyyyMMdd");
            if (notified.ContainsKey(key)) return false;
            Remember(key, now.Date.AddDays(1));

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
            outcome.Decisions.Add(new ReminderDecision
            {
                Reason = ending ? ReminderReasons.FixedEnd : ReminderReasons.FixedStart,
                Event = e
            });
            return true;
        }

        /// <summary>中途启动（刚开机 / 重启）时正处在某个固定日程里：直接立起「xx中」。</summary>
        private bool TryFixedResume(FixedSchedule f, DateTime now, ReminderOutcome outcome)
        {
            DateTime start = f.StartAt(now), end = f.EndAt(now);
            if (start == DateTime.MinValue || end == DateTime.MinValue) return false;
            if (end <= start) return false;
            if (now < start.AddSeconds(50) || now >= end) return false;   // 正点那一下交给 TryFixed
            string key = "fix:" + f.id + "@" + now.ToString("yyyyMMdd");
            if (notified.ContainsKey(key)) return false;
            Remember(key, now.Date.AddDays(1));

            var e = new ReminderEvent();
            e.id = "fix:" + f.id;
            e.title = f.name + "中";
            e.detail = f.role == "lunch" ? "去吃饭，先歇一会儿" : f.role == "evening" ? "吃个晚饭再继续" : "现在是在这段时间里";
            e.place = "";
            e.source = "break";
            e.start = Reminders.FormatStart(start);
            e.end = Reminders.FormatStart(end);
            outcome.Decisions.Add(new ReminderDecision { Reason = ReminderReasons.FixedResume, Event = e });
            return true;
        }

        /// <summary>合并后的日程视图：来源过滤、无效项过滤与开始时间排序都在这里，调用方不用先排序。</summary>
        public List<ReminderEvent> Agenda(Settings settings, ReminderScheduleSnapshot snapshot)
        {
            var list = new List<ReminderEvent>();
            if (settings == null) return list;
            if (settings.events != null)
                foreach (var e in settings.events)
                    if (e != null && Reminders.ParseStart(e) != DateTime.MinValue) list.Add(e);
            if (settings.calendarEnabled && snapshot != null && snapshot.Lark != null)
                foreach (var e in snapshot.Lark)
                    if (e != null && Reminders.ParseStart(e) != DateTime.MinValue) list.Add(e);
            if (settings.outlook != null && settings.outlook.HasEnabled && snapshot != null && snapshot.Outlook != null)
                foreach (var e in snapshot.Outlook)
                    if (e != null && Reminders.ParseStart(e) != DateTime.MinValue) list.Add(e);
            list.Sort(delegate(ReminderEvent a, ReminderEvent b)
            {
                return Reminders.ParseStart(a).CompareTo(Reminders.ParseStart(b));
            });
            return list;
        }

        private static string EventKey(ReminderEvent e)
        {
            return (e.id ?? e.title) + "@" + Reminders.ParseStart(e).Ticks;
        }

        /// <summary>这条日程彻底不会再提醒的时刻：提前窗口结束、补提醒窗口结束，取较晚的。</summary>
        private static DateTime EventExpiry(ReminderEvent e)
        {
            DateTime start = Reminders.ParseStart(e);
            if (start == DateTime.MinValue) return DateTime.MaxValue;
            DateTime end = Reminders.ParseEnd(e);
            DateTime lateEnd = (end == DateTime.MinValue || end <= start) ? start.AddMinutes(60) : end;
            DateTime dueEnd = start.AddSeconds(60);
            return lateEnd > dueEnd ? lateEnd : dueEnd;
        }

        private void Remember(string key, DateTime expiry)
        {
            DateTime old;
            if (notified.TryGetValue(key, out old) && old >= expiry) return;
            notified[key] = expiry;
        }

        /// <summary>
        /// 清理策略：到期的键才删（跨日 / 数到阈值时扫一遍），
        /// 不靠清空集合让仍有效的事项重新提醒。
        /// </summary>
        private void Sweep(DateTime now)
        {
            bool dayChanged = lastSweepDay == DateTime.MinValue || lastSweepDay != now.Date;
            lastSweepDay = now.Date;
            if (!dayChanged && notified.Count <= NotifiedLimit) return;
            var expired = new List<string>();
            foreach (var kv in notified)
                if (kv.Value <= now) expired.Add(kv.Key);
            foreach (var key in expired) notified.Remove(key);
        }

        private ReminderEvent BuildOffWorkEvent(ReminderSignals signals, DateTime now)
        {
            var e = new ReminderEvent();
            e.id = "alert:offwork";
            e.source = "alert";
            e.title = "该下班啦";
            e.detail = "在飞书发「2」打卡后自动消失";
            e.place = "";
            DateTime t;
            if (!string.IsNullOrEmpty(signals.OffWorkTarget) && DateTime.TryParse(signals.OffWorkTarget, out t))
                e.start = Reminders.FormatStart(t);
            else
                e.start = Reminders.FormatStart(now);
            return e;
        }
    }
}
