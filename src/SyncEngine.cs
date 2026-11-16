using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WorkCountdown
{
    /// <summary>同步范围：考勤、飞书日历、Outlook 日历来源。</summary>
    public static class SyncScopes
    {
        public const string Attendance = "attendance";
        public const string LarkCalendar = "lark-calendar";
        public const string Outlook = "outlook";
    }

    /// <summary>一次同步的结果状态；成功、部分成功、失败与取消可区分。</summary>
    public static class SyncStatuses
    {
        public const string Success = "success";
        public const string Partial = "partial";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
    }

    /// <summary>后台读考勤时使用的请求时配置快照（不再引用共享 Settings）。</summary>
    public class AttendanceReadRequest
    {
        public string Command { get; set; }
        public string ChatId { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    public interface IAttendanceReader
    {
        AttendanceSnapshot Read(AttendanceReadRequest request);
    }

    /// <summary>读一个日历来源（飞书 / .ics 文件 / .ics 订阅 / Graph）；认证细节在实现内部。</summary>
    public interface ICalendarSourceReader
    {
        CalendarFetchResult Read(CalendarFetchRequest request);
    }

    /// <summary>把后台工作放到线程池（生产）或测试的可控队列里。</summary>
    public interface ISyncScheduler
    {
        void Queue(Action work);
    }

    /// <summary>把结果送回界面线程；返回 false 表示窗口已经不可用。</summary>
    public interface ISyncDispatcher
    {
        bool TryPost(Action action);
    }

    /// <summary>接受结果时在状态拥有者处应用配置更新（令牌轮换、每份日历缓存与同步时间）。</summary>
    public interface ISyncConfigSink
    {
        void ApplySyncConfigUpdate(SyncConfigUpdate update);
    }

    /// <summary>一份日历来源的配置更新，由状态拥有者写回 Settings。</summary>
    public class CalendarCacheUpdate
    {
        public string SourceId { get; set; }
        public bool Succeeded { get; set; }
        public List<ReminderEvent> Events { get; set; }
        public string LastSync { get; set; }
        public string LastError { get; set; }
    }

    public class SyncConfigUpdate
    {
        public List<CalendarCacheUpdate> Calendars { get; set; }
        public List<ReminderEvent> OutlookCached { get; set; }
        public string OutlookLastSync { get; set; }
        public string OutlookLastError { get; set; }
        /// <summary>DPAPI 加密后的新刷新令牌；null 表示这次没有轮换。</summary>
        public string RefreshToken { get; set; }
        public string TokenAt { get; set; }

        public SyncConfigUpdate()
        {
            Calendars = new List<CalendarCacheUpdate>();
        }
    }

    /// <summary>一次在途同步请求的凭据；调用方只拿它去重与判断是否还在跑。</summary>
    public class SyncTicket
    {
        public string Id { get; internal set; }
        public string Scope { get; internal set; }
        public bool Manual { get; internal set; }
        public long ConfigVersion { get; internal set; }
        public DateTime RequestedAt { get; internal set; }
        public bool Cancelled { get; internal set; }
        internal SyncConfig Config { get; set; }
    }

    /// <summary>同步模块对外发布的只读状态快照；窗口只读它，不再自己合并来源。</summary>
    public class SyncSnapshot
    {
        public long ConfigVersion { get; set; }
        public AttendanceSnapshot Attendance { get; set; }
        public DateTime AttendanceLastSuccess { get; set; }
        public List<ReminderEvent> LarkEvents { get; set; }
        public string LarkError { get; set; }
        public DateTime LarkLastSuccess { get; set; }
        public List<ReminderEvent> OutlookEvents { get; set; }
        public string OutlookError { get; set; }
        public string OutlookLastSync { get; set; }
        public bool AttendanceFetching { get; set; }
        public bool LarkFetching { get; set; }
        public bool OutlookFetching { get; set; }

        public SyncSnapshot()
        {
            LarkEvents = new List<ReminderEvent>();
            OutlookEvents = new List<ReminderEvent>();
            AttendanceLastSuccess = DateTime.MinValue;
            LarkLastSuccess = DateTime.MinValue;
            OutlookLastSync = "";
        }
    }

    /// <summary>接受一次结果后给界面的通知：状态、错误是否变化、以及需要落盘的配置更新。</summary>
    public class SyncPublication
    {
        public string Scope { get; set; }
        public string Status { get; set; }
        public bool Manual { get; set; }
        public bool ErrorChanged { get; set; }
        public SyncSnapshot Snapshot { get; set; }
        public SyncConfigUpdate ConfigUpdate { get; set; }
    }

    /// <summary>后台 adapter 返回的原始结果；状态在模块里统一判定。</summary>
    public class SyncResult
    {
        public string Scope { get; set; }
        public long ConfigVersion { get; set; }
        public bool Manual { get; set; }
        public string Status { get; set; }
        public AttendanceSnapshot Attendance { get; set; }
        public List<CalendarFetchResult> Calendars { get; set; }
        public string Error { get; set; }
        public DateTime FinishedAt { get; set; }
        public string RefreshToken { get; set; }
        public string TokenAt { get; set; }

        public SyncResult()
        {
            Calendars = new List<CalendarFetchResult>();
        }
    }

    /// <summary>请求时抓下来的同步配置；后台线程只读这一份，不碰共享 Settings。</summary>
    internal class LarkConfig
    {
        public bool Enabled;
        public string Command = "";
        public string ChatId = "";
    }

    internal class CalendarSourceConfig
    {
        public string Id = "";
        public string Name = "";
        public string Kind = "url";
        public string Url = "";
        public string Path = "";
        public int LeadMinutes;
    }

    internal class OutlookConfig
    {
        public string ClientId = "";
        public string Tenant = "common";
        public string Account = "";
        public string RefreshToken = "";
        public int LeadMinutes;
        public readonly List<CalendarSourceConfig> Sources = new List<CalendarSourceConfig>();

        public bool HasEnabled
        {
            get { return Sources.Count > 0; }
        }
    }

    internal class SyncConfig
    {
        public long Version;
        public readonly LarkConfig Lark = new LarkConfig();
        public bool LarkCalendarEnabled;
        public readonly OutlookConfig Outlook = new OutlookConfig();

        public static SyncConfig Capture(Settings settings, long version)
        {
            var config = new SyncConfig();
            config.Version = version;
            if (settings == null) return config;
            if (settings.lark != null)
            {
                config.Lark.Enabled = settings.lark.enabled;
                config.Lark.Command = settings.lark.command ?? "";
                config.Lark.ChatId = settings.lark.chatId ?? "";
            }
            config.LarkCalendarEnabled = settings.calendarEnabled && config.Lark.Enabled;
            OutlookSettings source = settings.outlook;
            if (source != null)
            {
                config.Outlook.ClientId = source.clientId ?? "";
                config.Outlook.Tenant = source.tenant ?? "";
                config.Outlook.Account = source.account ?? "";
                config.Outlook.RefreshToken = source.refreshToken ?? "";
                config.Outlook.LeadMinutes = Math.Max(0, source.leadMinutes);
                // 启用且有来源的日历由 OutlookSettings.Active 统一判定，避免同步模块再写一份过滤规则
                foreach (var c in source.Active)
                {
                    var item = new CalendarSourceConfig();
                    item.Id = c.id ?? "";
                    item.Name = c.name ?? "";
                    item.Kind = c.source == "file" || c.source == "graph" ? c.source : "url";
                    item.Url = c.url ?? "";
                    item.Path = c.path ?? "";
                    item.LeadMinutes = Math.Max(0, c.leadMinutes);
                    config.Outlook.Sources.Add(item);
                }
            }
            return config;
        }

        /// <summary>
        /// 只把「会改变同步结果」的配置算进去；主题、窗口位置等变化不会让在途结果作废。
        /// 缓存 / 最后同步时间 / 错误不参与，否则每次接受结果都会把自己判成过期。
        /// </summary>
        public string Fingerprint()
        {
            var sb = new StringBuilder();
            Field(sb, "sync-v1");
            Field(sb, Lark.Enabled ? "1" : "0");
            Field(sb, Lark.Command);
            Field(sb, Lark.ChatId);
            Field(sb, LarkCalendarEnabled ? "1" : "0");
            Field(sb, Outlook.ClientId);
            Field(sb, Outlook.Tenant);
            Field(sb, Outlook.Account);
            Field(sb, Outlook.RefreshToken);
            Field(sb, Outlook.LeadMinutes.ToString(CultureInfo.InvariantCulture));
            foreach (var c in Outlook.Sources)
            {
                Field(sb, "source");
                Field(sb, c.Id);
                Field(sb, c.Name);
                Field(sb, c.Kind);
                Field(sb, c.Url);
                Field(sb, c.Path);
                Field(sb, c.LeadMinutes.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static void Field(StringBuilder sb, string value)
        {
            sb.Append(value ?? "");
            sb.Append('\u0001');
        }
    }

    /// <summary>
    /// 拥有同步任务生命周期与按日历来源最后成功缓存的深模块：
    /// 请求时抓配置快照，后台 adapter 只读快照；结果带配置代次，回到界面线程后
    /// 由这里校验代次与在途凭据，过期或已释放的结果不发布、不写缓存、不碰令牌。
    /// </summary>
    public class SyncEngine : IDisposable
    {
        private class CalendarCache
        {
            public string SourceId = "";
            public List<ReminderEvent> Events = new List<ReminderEvent>();
            public string LastSync = "";
            public string LastError = "";
        }

        private readonly ISyncScheduler scheduler;
        private readonly ISyncDispatcher dispatcher;
        private readonly IAttendanceReader attendanceReader;
        private readonly ICalendarSourceReader calendarReader;
        private readonly Func<DateTime> clock;
        private readonly object gate = new object();
        private readonly Dictionary<string, SyncTicket> tickets = new Dictionary<string, SyncTicket>();
        private readonly Dictionary<string, CalendarCache> calendarCaches = new Dictionary<string, CalendarCache>();

        private SyncConfig config = new SyncConfig();
        private string configFingerprint = "";
        private long configVersion;
        private volatile bool released;

        private AttendanceSnapshot attendance;
        private DateTime attendanceLastSuccess = DateTime.MinValue;
        private List<ReminderEvent> larkEvents = new List<ReminderEvent>();
        private string larkError;
        private DateTime larkLastSuccess = DateTime.MinValue;

        private SyncSnapshot snapshot;

        public SyncEngine(ISyncScheduler scheduler, ISyncDispatcher dispatcher,
            IAttendanceReader attendanceReader, ICalendarSourceReader calendarReader, Func<DateTime> clock)
        {
            this.scheduler = scheduler;
            this.dispatcher = dispatcher;
            this.attendanceReader = attendanceReader;
            this.calendarReader = calendarReader;
            this.clock = clock == null ? delegate { return DateTime.Now; } : clock;
            this.snapshot = BuildSnapshot();
        }

        public event Action<SyncPublication> Published;

        public ISyncConfigSink ConfigSink { get; set; }

        public long ConfigVersion
        {
            get { return configVersion; }
        }

        /// <summary>同步模块当前实际使用的配置指纹；配置变化计划用它避免重复通知。</summary>
        public string ConfigFingerprint()
        {
            return configFingerprint;
        }

        public SyncSnapshot Snapshot()
        {
            return snapshot;
        }

        /// <summary>
        /// 同步相关配置的稳定指纹：配置变化计划用它判断是否需要通知同步模块，
        /// Configure 用它判断是否推进代次；两处共用同一份规则，避免手写两份字段比较。
        /// </summary>
        public static string Fingerprint(Settings settings)
        {
            return SyncConfig.Capture(settings, 0).Fingerprint();
        }

        /// <summary>设置页保存 / 启动 / 导入配置后调用；只有同步相关配置变化才推进代次。</summary>
        public void Configure(Settings settings)
        {
            if (released || settings == null) return;
            SyncConfig next = SyncConfig.Capture(settings, 0);
            string fingerprint = next.Fingerprint();
            bool changed = fingerprint != configFingerprint;
            if (changed)
            {
                configVersion++;
                next.Version = configVersion;
                configFingerprint = fingerprint;
                CancelAllTickets();
            }
            else
            {
                next.Version = configVersion;
            }
            config = next;
            ApplyConfig(settings);
            snapshot = BuildSnapshot();
        }

        /// <summary>跨日清掉昨天的考勤快照；新结果回来前卡片不显示昨天的打卡。</summary>
        public void ResetAttendance()
        {
            if (released) return;
            attendance = null;
            attendanceLastSuccess = DateTime.MinValue;
            snapshot = BuildSnapshot();
        }

        public SyncTicket RequestAttendance(bool manual)
        {
            if (released || !config.Lark.Enabled) return null;
            return StartTicket(SyncScopes.Attendance, manual);
        }

        public SyncTicket RequestLarkCalendar(bool manual)
        {
            if (released || !config.LarkCalendarEnabled) return null;
            return StartTicket(SyncScopes.LarkCalendar, manual);
        }

        /// <summary>一次刷新所有启用的日历来源；每份来源各自成功 / 失败，失败保留上次成功缓存。</summary>
        public SyncTicket RequestOutlook(bool manual)
        {
            if (released || !config.Outlook.HasEnabled) return null;
            return StartTicket(SyncScopes.Outlook, manual);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (released) return;
                released = true;
                foreach (var ticket in tickets.Values)
                    if (ticket != null) ticket.Cancelled = true;
                tickets.Clear();
            }
            Published = null;
            ConfigSink = null;
            snapshot = BuildSnapshot();
        }

        private SyncTicket StartTicket(string scope, bool manual)
        {
            SyncTicket ticket;
            lock (gate)
            {
                if (released) return null;
                if (tickets.ContainsKey(scope)) return null;
                ticket = new SyncTicket();
                ticket.Id = Guid.NewGuid().ToString("N");
                ticket.Scope = scope;
                ticket.Manual = manual;
                ticket.ConfigVersion = config.Version;
                ticket.RequestedAt = clock();
                ticket.Config = config;
                tickets[scope] = ticket;
                snapshot = BuildSnapshot();
            }
            try
            {
                scheduler.Queue(delegate { Run(ticket); });
            }
            catch
            {
                Discard(ticket);
                return null;
            }
            return ticket;
        }

        private void Run(SyncTicket ticket)
        {
            SyncResult result;
            try
            {
                result = Execute(ticket);
            }
            catch (Exception ex)
            {
                result = FailedResult(ticket, ex.Message);
            }
            result.ConfigVersion = ticket.ConfigVersion;
            result.Manual = ticket.Manual;
            result.FinishedAt = clock();
            Complete(ticket, result);
        }

        private void Complete(SyncTicket ticket, SyncResult result)
        {
            if (released) return;
            bool posted;
            try
            {
                posted = dispatcher != null && dispatcher.TryPost(delegate { Accept(ticket, result); });
            }
            catch
            {
                posted = false;
            }
            if (!posted) Discard(ticket);
        }

        private void Discard(SyncTicket ticket)
        {
            bool removed = false;
            lock (gate)
            {
                SyncTicket current;
                if (ticket != null && tickets.TryGetValue(ticket.Scope, out current)
                    && current != null && current.Id == ticket.Id)
                {
                    tickets.Remove(ticket.Scope);
                    removed = true;
                }
            }
            if (removed && !released) snapshot = BuildSnapshot();
        }

        private void Accept(SyncTicket ticket, SyncResult result)
        {
            if (released || ticket == null || result == null) return;
            lock (gate)
            {
                SyncTicket current;
                if (!tickets.TryGetValue(ticket.Scope, out current) || current == null || current.Id != ticket.Id)
                    return;
                if (ticket.ConfigVersion != config.Version)
                {
                    tickets.Remove(ticket.Scope);
                    snapshot = BuildSnapshot();
                    return;
                }
                tickets.Remove(ticket.Scope);
            }

            bool errorChanged = false;
            SyncConfigUpdate update = null;
            if (ticket.Scope == SyncScopes.Attendance)
            {
                string before = attendance == null ? null : attendance.error;
                attendance = result.Attendance;
                string after = attendance == null ? null : attendance.error;
                errorChanged = !SameError(before, after);
                if (attendance != null && attendance.ok) attendanceLastSuccess = result.FinishedAt;
            }
            else if (ticket.Scope == SyncScopes.LarkCalendar)
            {
                string before = larkError;
                CalendarFetchResult one = result.Calendars.Count > 0 ? result.Calendars[0] : null;
                if (one != null && one.Succeeded)
                {
                    larkEvents = Copy(one.Events);
                    larkLastSuccess = result.FinishedAt;
                }
                larkError = result.Error;
                errorChanged = !SameError(before, larkError);
            }
            else if (ticket.Scope == SyncScopes.Outlook)
            {
                update = ApplyOutlook(result, out errorChanged);
                if (update != null && update.RefreshToken != null)
                {
                    // 令牌轮换后，本会话后续刷新继续用新令牌；配置代次不变，窗口稍后保存到 Settings。
                    config.Outlook.RefreshToken = update.RefreshToken;
                    configFingerprint = config.Fingerprint();
                }
            }

            snapshot = BuildSnapshot();
            if (update != null && ConfigSink != null)
            {
                try { ConfigSink.ApplySyncConfigUpdate(update); }
                catch { }
            }

            var publication = new SyncPublication();
            publication.Scope = ticket.Scope;
            publication.Status = result.Status;
            publication.Manual = ticket.Manual;
            publication.ErrorChanged = errorChanged;
            publication.Snapshot = snapshot;
            publication.ConfigUpdate = update;
            Action<SyncPublication> handler = Published;
            if (handler != null) handler(publication);
        }

        private SyncResult Execute(SyncTicket ticket)
        {
            var result = new SyncResult();
            result.Scope = ticket.Scope;
            SyncConfig cfg = ticket.Config;
            if (ticket.Scope == SyncScopes.Attendance)
            {
                var request = new AttendanceReadRequest();
                request.Command = cfg.Lark.Command;
                request.ChatId = cfg.Lark.ChatId;
                request.RequestedAt = ticket.RequestedAt;
                AttendanceSnapshot read = null;
                try
                {
                    read = attendanceReader.Read(request);
                }
                catch (Exception ex)
                {
                    read = new AttendanceSnapshot();
                    read.error = ex.Message;
                }
                if (read == null)
                {
                    read = new AttendanceSnapshot();
                    read.error = "同步没有返回考勤记录";
                }
                result.Attendance = read;
                result.Status = read.ok ? SyncStatuses.Success : SyncStatuses.Failed;
                result.Error = read.error;
            }
            else if (ticket.Scope == SyncScopes.LarkCalendar)
            {
                CalendarFetchResult one = RunCalendar(BuildLarkRequest(cfg, ticket.RequestedAt));
                result.Calendars.Add(one);
                result.Error = one.Error;
                result.Status = one.Succeeded ? SyncStatuses.Success : SyncStatuses.Failed;
            }
            else if (ticket.Scope == SyncScopes.Outlook)
            {
                string refreshToken = cfg.Outlook.RefreshToken;
                foreach (var source in cfg.Outlook.Sources)
                {
                    CalendarFetchResult one = RunCalendar(BuildOutlookRequest(cfg, source, ticket.RequestedAt, refreshToken));
                    if (one.RotatedRefreshToken != null)
                    {
                        refreshToken = one.RotatedRefreshToken;
                        result.RefreshToken = one.RotatedRefreshToken;
                        result.TokenAt = one.TokenAt;
                    }
                    result.Calendars.Add(one);
                }
                int ok = 0;
                var errors = new List<string>();
                foreach (var one in result.Calendars)
                {
                    if (one.Succeeded) ok++;
                    else errors.Add((string.IsNullOrWhiteSpace(one.SourceName) ? "日历" : one.SourceName)
                        + "：" + (string.IsNullOrEmpty(one.Error) ? "同步失败" : one.Error));
                }
                result.Error = errors.Count == 0 ? null : string.Join("；", errors.ToArray());
                if (ok == 0) result.Status = SyncStatuses.Failed;
                else if (ok < result.Calendars.Count) result.Status = SyncStatuses.Partial;
                else result.Status = SyncStatuses.Success;
            }
            return result;
        }

        private CalendarFetchResult RunCalendar(CalendarFetchRequest request)
        {
            try
            {
                CalendarFetchResult result = calendarReader.Read(request);
                if (result == null)
                {
                    result = new CalendarFetchResult();
                    result.Error = "同步没有返回结果";
                }
                if (string.IsNullOrEmpty(result.SourceId)) result.SourceId = request.SourceId;
                if (string.IsNullOrEmpty(result.SourceName)) result.SourceName = request.SourceName;
                return result;
            }
            catch (Exception ex)
            {
                var result = new CalendarFetchResult();
                result.SourceId = request.SourceId;
                result.SourceName = request.SourceName;
                result.Error = "读取失败：" + ex.Message;
                return result;
            }
        }

        private CalendarFetchRequest BuildLarkRequest(SyncConfig cfg, DateTime now)
        {
            var request = new CalendarFetchRequest();
            request.Kind = "lark";
            request.SourceId = "lark:calendar";
            request.SourceName = "飞书日程";
            request.LarkCommand = cfg.Lark.Command;
            request.LarkChatId = cfg.Lark.ChatId;
            request.From = now.Date;
            request.To = now.Date.AddDays(2);
            request.TimeoutMs = 30000;
            return request;
        }

        private CalendarFetchRequest BuildOutlookRequest(SyncConfig cfg, CalendarSourceConfig source, DateTime now, string refreshToken)
        {
            var request = new CalendarFetchRequest();
            request.Kind = source.Kind;
            request.SourceId = source.Id;
            request.SourceName = source.Name;
            request.Url = source.Url;
            request.Path = source.Path;
            request.LeadMinutes = source.LeadMinutes;
            request.DefaultLeadMinutes = cfg.Outlook.LeadMinutes;
            request.ClientId = cfg.Outlook.ClientId;
            request.Tenant = cfg.Outlook.Tenant;
            request.RefreshToken = refreshToken;
            request.From = now.Date;
            request.To = now.Date.AddDays(14);
            request.TimeoutMs = 20000;
            return request;
        }

        private SyncResult FailedResult(SyncTicket ticket, string error)
        {
            var result = new SyncResult();
            result.Scope = ticket.Scope;
            result.ConfigVersion = ticket.ConfigVersion;
            result.Manual = ticket.Manual;
            result.Status = SyncStatuses.Failed;
            result.Error = error;
            if (ticket.Scope == SyncScopes.Attendance)
            {
                var read = new AttendanceSnapshot();
                read.error = error;
                result.Attendance = read;
            }
            else if (ticket.Scope == SyncScopes.Outlook && ticket.Config != null)
            {
                // 兜底：整个任务在 adapter 之外失败时，也让每份来源留下可检查的错误
                foreach (var source in ticket.Config.Outlook.Sources)
                {
                    var one = new CalendarFetchResult();
                    one.SourceId = source.Id;
                    one.SourceName = source.Name;
                    one.Error = error;
                    result.Calendars.Add(one);
                }
            }
            return result;
        }

        private SyncConfigUpdate ApplyOutlook(SyncResult result, out bool errorChanged)
        {
            string before = BuildOutlookError();
            var update = new SyncConfigUpdate();
            foreach (var one in result.Calendars)
            {
                CalendarCache cache;
                if (!calendarCaches.TryGetValue(one.SourceId, out cache))
                {
                    cache = new CalendarCache();
                    cache.SourceId = one.SourceId;
                    calendarCaches[one.SourceId] = cache;
                }
                var calendarUpdate = new CalendarCacheUpdate();
                calendarUpdate.SourceId = one.SourceId;
                if (one.Succeeded)
                {
                    cache.Events = Copy(one.Events);
                    cache.LastSync = FormatTime(result.FinishedAt);
                    cache.LastError = "";
                    calendarUpdate.Succeeded = true;
                    calendarUpdate.Events = Copy(cache.Events);
                    calendarUpdate.LastSync = cache.LastSync;
                    calendarUpdate.LastError = "";
                }
                else
                {
                    cache.LastError = string.IsNullOrEmpty(one.Error) ? "同步失败" : one.Error;
                    calendarUpdate.Succeeded = false;
                    calendarUpdate.LastError = cache.LastError;
                }
                update.Calendars.Add(calendarUpdate);
            }
            update.OutlookCached = Copy(BuildOutlookEvents());
            update.OutlookLastSync = BuildOutlookLastSync();
            update.OutlookLastError = BuildOutlookError();
            if (result.RefreshToken != null)
            {
                update.RefreshToken = result.RefreshToken;
                update.TokenAt = result.TokenAt;
            }
            errorChanged = !SameError(before, update.OutlookLastError);
            return update;
        }

        private void ApplyConfig(Settings settings)
        {
            var ids = new HashSet<string>();
            foreach (var source in config.Outlook.Sources) ids.Add(source.Id);

            var stale = new List<string>();
            foreach (var id in calendarCaches.Keys)
                if (!ids.Contains(id)) stale.Add(id);
            foreach (var id in stale) calendarCaches.Remove(id);

            if (settings.outlook != null && settings.outlook.calendars != null)
            {
                foreach (var c in settings.outlook.calendars)
                {
                    if (c == null || !ids.Contains(c.id) || calendarCaches.ContainsKey(c.id)) continue;
                    var cache = new CalendarCache();
                    cache.SourceId = c.id;
                    cache.Events = Copy(c.cached);
                    cache.LastSync = c.lastSync ?? "";
                    cache.LastError = c.lastError ?? "";
                    calendarCaches[c.id] = cache;
                }
            }

            if (!config.Lark.Enabled)
            {
                attendance = null;
                attendanceLastSuccess = DateTime.MinValue;
            }
            if (!config.LarkCalendarEnabled)
            {
                larkEvents = new List<ReminderEvent>();
                larkError = null;
                larkLastSuccess = DateTime.MinValue;
            }
        }

        private SyncSnapshot BuildSnapshot()
        {
            var state = new SyncSnapshot();
            state.ConfigVersion = configVersion;
            state.Attendance = attendance;
            state.AttendanceLastSuccess = attendanceLastSuccess;
            state.AttendanceFetching = HasTicket(SyncScopes.Attendance);
            state.LarkEvents = config.LarkCalendarEnabled ? Copy(larkEvents) : new List<ReminderEvent>();
            state.LarkError = config.LarkCalendarEnabled ? larkError : null;
            state.LarkLastSuccess = larkLastSuccess;
            state.LarkFetching = HasTicket(SyncScopes.LarkCalendar);
            state.OutlookEvents = BuildOutlookEvents();
            state.OutlookError = BuildOutlookError();
            state.OutlookLastSync = BuildOutlookLastSync();
            state.OutlookFetching = HasTicket(SyncScopes.Outlook);
            return state;
        }

        private bool HasTicket(string scope)
        {
            lock (gate) return tickets.ContainsKey(scope);
        }

        /// <summary>
        /// 唯一的日程合并规则：只看当前启用且有效的来源，按 id 去重（没有 id 用标题@开始时间），
        /// 再按开始时间排序。启动缓存、成功、部分失败、全部失败都走这里。
        /// </summary>
        private List<ReminderEvent> BuildOutlookEvents()
        {
            var all = new List<ReminderEvent>();
            var seen = new HashSet<string>();
            foreach (var source in config.Outlook.Sources)
            {
                CalendarCache cache;
                if (!calendarCaches.TryGetValue(source.Id, out cache) || cache.Events == null) continue;
                foreach (var e in cache.Events)
                {
                    if (e == null) continue;
                    string key = string.IsNullOrEmpty(e.id) ? e.title + "@" + e.start : e.id;
                    if (!seen.Add(key)) continue;
                    all.Add(e);
                }
            }
            all.Sort(delegate(ReminderEvent a, ReminderEvent b)
            {
                return Reminders.ParseStart(a).CompareTo(Reminders.ParseStart(b));
            });
            return all;
        }

        private string BuildOutlookError()
        {
            var errors = new List<string>();
            foreach (var source in config.Outlook.Sources)
            {
                CalendarCache cache;
                if (!calendarCaches.TryGetValue(source.Id, out cache)) continue;
                if (string.IsNullOrEmpty(cache.LastError)) continue;
                errors.Add((string.IsNullOrWhiteSpace(source.Name) ? "日历" : source.Name) + "：" + cache.LastError);
            }
            return errors.Count == 0 ? null : string.Join("；", errors.ToArray());
        }

        private string BuildOutlookLastSync()
        {
            string newest = "";
            foreach (var source in config.Outlook.Sources)
            {
                CalendarCache cache;
                if (!calendarCaches.TryGetValue(source.Id, out cache)) continue;
                if (!string.IsNullOrEmpty(cache.LastSync) && cache.LastSync.CompareTo(newest) > 0)
                    newest = cache.LastSync;
            }
            return newest;
        }

        private void CancelAllTickets()
        {
            lock (gate)
            {
                foreach (var ticket in tickets.Values)
                    if (ticket != null) ticket.Cancelled = true;
                tickets.Clear();
            }
        }

        private static string FormatTime(DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static bool SameError(string a, string b)
        {
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        private static List<ReminderEvent> Copy(List<ReminderEvent> source)
        {
            var copy = new List<ReminderEvent>();
            if (source != null)
                foreach (var e in source)
                    copy.Add(e);
            return copy;
        }
    }
}
