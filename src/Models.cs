using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WorkCountdown
{
    /// <summary>程序版本：界面标题、关于文案和打包脚本都从这里取。</summary>
    public static class AppInfo
    {
        public const string Version = "3.6.0";
    }

    public class HolidayEntry
    {
        public string date { get; set; }   // yyyy-MM-dd
        public string kind { get; set; }   // "off" 休息 / "work" 调休上班
    }

    /// <summary>一条待提醒的日程：手动添加，或从飞书日历读到。</summary>
    public class ReminderEvent
    {
        public string id { get; set; }
        public string title { get; set; }
        public string start { get; set; }       // yyyy-MM-dd HH:mm
        public string end { get; set; }         // yyyy-MM-dd HH:mm，可空
        public string place { get; set; }       // 位置 / 线上会议
        public string detail { get; set; }      // 内容 / 描述
        public string source { get; set; }      // "lark" | "local"
        public int leadMinutes { get; set; }    // 0 表示用全局默认
    }

    public static class Reminders
    {
        public const string DateFormat = "yyyy-MM-dd HH:mm";

        // 四类日程
        public const string KindFixed = "fixed";       // 午休 / 晚休 / 下班 / 自建重复日程
        public const string KindLark = "lark";         // 飞书日历里的会
        public const string KindLocal = "local";       // 手动加的普通日程
        public const string KindOutlook = "outlook";   // Outlook/Exchange 订阅来的

        public static readonly string[] Kinds = new string[] { KindFixed, KindLark, KindLocal, KindOutlook };

        public static string KindName(string kind)
        {
            if (kind == KindLark) return "飞书会议";
            if (kind == KindOutlook) return "Outlook 日程";
            if (kind == KindFixed) return "固定日程";
            return "普通日程";
        }

        /// <summary>这条提醒属于哪一类日程（决定用哪套通知样式）。</summary>
        public static string KindOf(ReminderEvent e)
        {
            if (e == null) return KindLocal;
            if (e.source == "lark") return KindLark;
            if (e.source == "outlook") return KindOutlook;
            if (e.source == "break" || e.source == "alert") return KindFixed;
            return KindLocal;
        }

        /// <summary>飞书日历里的会算会议，手动加的是日程。</summary>
        public static bool IsMeeting(ReminderEvent e)
        {
            return e != null && e.source == "lark";
        }

        /// <summary>
        /// 一条提醒用哪套配色：固定日程优先用它自己选的那套（旧规则），
        /// 其余三类用「该类日程」的配色设置。
        /// </summary>
        public static string ColorIdOf(Settings s, ReminderEvent e, FixedSchedule fixedSched)
        {
            if (fixedSched != null && !string.IsNullOrWhiteSpace(fixedSched.styleId)) return fixedSched.styleId;
            return s == null ? ReminderStyles.Follow : s.StyleOf(KindOf(e)).color;
        }

        public static DateTime ParseStart(ReminderEvent e)
        {
            DateTime t;
            if (e == null || string.IsNullOrWhiteSpace(e.start)) return DateTime.MinValue;
            if (DateTime.TryParseExact(e.start, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return t;
            if (DateTime.TryParse(e.start, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return t;
            return DateTime.MinValue;
        }

        public static string FormatStart(DateTime t)
        {
            return t.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        public static DateTime ParseEnd(ReminderEvent e)
        {
            DateTime t;
            if (e == null || string.IsNullOrWhiteSpace(e.end)) return DateTime.MinValue;
            if (DateTime.TryParseExact(e.end, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return t;
            if (DateTime.TryParse(e.end, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return t;
            return DateTime.MinValue;
        }

        /// <summary>「15:30–16:00」，没有结束时间就只给开始时间。</summary>
        public static string TimeRange(ReminderEvent e)
        {
            DateTime start = ParseStart(e);
            if (start == DateTime.MinValue) return "";
            DateTime end = ParseEnd(e);
            if (end == DateTime.MinValue || end <= start)
                return start.ToString("HH:mm", CultureInfo.InvariantCulture);
            return start.ToString("HH:mm", CultureInfo.InvariantCulture) + "–" + end.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        public static int LeadOf(Settings s, ReminderEvent e)
        {
            if (e != null && e.leadMinutes > 0) return e.leadMinutes;
            if (IsMeeting(e)) return Math.Max(0, s.calendarLeadMinutes);
            return Math.Max(0, s.eventLeadMinutes);
        }

        /// <summary>手动日程 + 飞书日程 + Outlook 日程，按开始时间排序。</summary>
        public static List<ReminderEvent> Merge(Settings s, List<ReminderEvent> larkEvents)
        {
            return Merge(s, larkEvents, null);
        }

        public static List<ReminderEvent> Merge(Settings s, List<ReminderEvent> larkEvents, List<ReminderEvent> outlookEvents)
        {
            var list = new List<ReminderEvent>();
            if (s.events != null)
            {
                foreach (var e in s.events)
                    if (e != null && ParseStart(e) != DateTime.MinValue) list.Add(e);
            }
            if (s.calendarEnabled && larkEvents != null)
            {
                foreach (var e in larkEvents)
                    if (e != null && ParseStart(e) != DateTime.MinValue) list.Add(e);
            }
            if (s.outlook != null && s.outlook.HasEnabled && outlookEvents != null)
            {
                foreach (var e in outlookEvents)
                    if (e != null && ParseStart(e) != DateTime.MinValue) list.Add(e);
            }
            list.Sort(delegate (ReminderEvent a, ReminderEvent b) { return ParseStart(a).CompareTo(ParseStart(b)); });
            return list;
        }

        /// <summary>提醒窗口：从「开始时间 - 提前量」到「开始后 1 分钟」（提前量为 0 时也留个窗口）。</summary>
        public static bool IsDue(Settings s, ReminderEvent e, DateTime now)
        {
            DateTime start = ParseStart(e);
            if (start == DateTime.MinValue) return false;
            DateTime fire = start.AddMinutes(-LeadOf(s, e));
            return now >= fire && now < start.AddSeconds(60);
        }

        /// <summary>
        /// 「补提醒」：日历同步/程序启动晚于开始时间时，严格窗口已经错过，
        /// 但只要日程还没结束、且开始不超过 60 分钟，就补放一次（文案会显示「正在进行」）。
        /// 这样 Outlook/飞书在会开始后才同步下来时，不会整场会都没有提醒。
        /// </summary>
        public static bool IsLateDue(Settings s, ReminderEvent e, DateTime now)
        {
            DateTime start = ParseStart(e);
            if (start == DateTime.MinValue) return false;
            if (now < start.AddSeconds(60)) return false;       // 还在正常窗口里
            DateTime end = ParseEnd(e);
            if (end == DateTime.MinValue || end <= start) end = start.AddMinutes(60);
            if (now >= end) return false;                       // 已经结束了，不补
            return (now - start).TotalMinutes <= 60;
        }

        public static ReminderEvent Next(Settings s, List<ReminderEvent> all, DateTime now)
        {
            if (all == null) return null;
            foreach (var e in all)
                if (ParseStart(e) > now) return e;
            return null;
        }

        /// <summary>「还有 5 分钟」这种说法。</summary>
        public static string CountdownText(Settings s, ReminderEvent e, DateTime now)
        {
            DateTime start = ParseStart(e);
            if (start == DateTime.MinValue) return "";
            if (now >= start) return "正在进行";
            TimeSpan left = start - now;
            if (left.TotalMinutes < 1) return "马上开始";
            if (left.TotalMinutes < 60) return "还有 " + (int)Math.Ceiling(left.TotalMinutes) + " 分钟";
            return "还有 " + Policy.Hm((int)Math.Round(left.TotalMinutes));
        }
    }

    public class LarkSettings
    {
        public bool enabled { get; set; }
        public string chatId { get; set; }
        public string botName { get; set; }
        public int pollMinutes { get; set; }
        public string command { get; set; }

        public LarkSettings()
        {
            enabled = false;   // 没配置会话前不读飞书
            chatId = "";
            botName = "考勤";  // 只是文案占位，设置里会改
            pollMinutes = 5;
            command = "lark-cli";
        }
    }

    /// <summary>
    /// 一个被订阅的日历。多份日历各自存来源、提前量和上次同步结果，
    /// 一条拉失败不会影响其它日历。
    /// </summary>
    public class CalendarSource
    {
        public string id { get; set; }
        public string name { get; set; }
        public bool enabled { get; set; }
        public string source { get; set; }      // url | file | graph
        public string url { get; set; }         // 订阅地址（发布日历给的 .ics 链接）
        public string path { get; set; }        // 本地 .ics 路径
        public int leadMinutes { get; set; }    // 0 = 用全局默认
        public string lastSync { get; set; }    // yyyy-MM-dd HH:mm
        public string lastError { get; set; }
        /// <summary>这个日历上次同步下来的日程（未来 14 天），存盘，重启后先拿它顶着。</summary>
        public List<ReminderEvent> cached { get; set; }

        public CalendarSource()
        {
            id = "";
            name = "";
            enabled = true;
            source = "url";
            url = "";
            path = "";
            leadMinutes = 0;
            lastSync = "";
            lastError = "";
            cached = new List<ReminderEvent>();
        }

        public bool HasSource
        {
            get
            {
                if (source == "file") return !string.IsNullOrWhiteSpace(path);
                if (source == "graph") return true;      // 登录状态在 OutlookSettings 上
                return !string.IsNullOrWhiteSpace(url);
            }
        }

        public string SourceName
        {
            get
            {
                if (source == "file") return "本地 .ics";
                if (source == "graph") return "Outlook 登录";
                return "订阅";
            }
        }

        /// <summary>下拉 / 列表里的一行：名字 · 类型 · 提前 N 分。</summary>
        public string DisplayName
        {
            get
            {
                string n = string.IsNullOrWhiteSpace(name) ? "未命名日历" : name;
                string tail = leadMinutes > 0 ? "提前 " + leadMinutes + " 分" : "跟随默认提前";
                return n + " · " + SourceName + " · " + tail;
            }
        }
    }

    /// <summary>
    /// Outlook / Exchange 日历：既可以订阅多份 .ics（发布日历链接 / 本地导出），
    /// 也可以登录 Graph 读主日历。登录凭据是账号级的，日历列表在 calendars 里。
    /// </summary>
    public class OutlookSettings
    {
        public bool enabled { get; set; }       // 派生字段：只要有一份日历在同步就是 true
        public string source { get; set; }      // 老配置字段（url | file | graph），只为迁移保留
        public string url { get; set; }         // 老配置字段
        public string path { get; set; }        // 老配置字段
        public int leadMinutes { get; set; }    // 默认提前分钟数
        public string lastSync { get; set; }    // yyyy-MM-dd HH:mm（所有日历里最近一次成功）
        public string lastError { get; set; }
        // Graph（登录）用：Azure 应用注册的 client ID、租户、账号显示名、DPAPI 加密的刷新令牌
        public string clientId { get; set; }
        public string tenant { get; set; }
        public string account { get; set; }
        public string refreshToken { get; set; }
        public string tokenAt { get; set; }
        /// <summary>所有日历的日程汇总（未来 14 天），给旧版本 / 托盘读缓存用。</summary>
        public List<ReminderEvent> cached { get; set; }
        /// <summary>订阅的日历列表（2.16 起：一份或多份）。</summary>
        public List<CalendarSource> calendars { get; set; }

        public OutlookSettings()
        {
            enabled = false;
            source = "url";
            url = "";
            path = "";
            leadMinutes = 10;
            lastSync = "";
            lastError = "";
            clientId = "";
            tenant = "common";
            account = "";
            refreshToken = "";
            tokenAt = "";
            cached = new List<ReminderEvent>();
            calendars = new List<CalendarSource>();
        }

        public bool HasEnabled
        {
            get
            {
                if (calendars == null) return false;
                foreach (var c in calendars)
                    if (c != null && c.enabled && c.HasSource) return true;
                return false;
            }
        }

        public List<CalendarSource> Active
        {
            get
            {
                var list = new List<CalendarSource>();
                if (calendars == null) return list;
                foreach (var c in calendars)
                    if (c != null && c.enabled && c.HasSource) list.Add(c);
                return list;
            }
        }

        public int CachedCount
        {
            get
            {
                int n = 0;
                if (calendars != null)
                    foreach (var c in calendars) if (c != null && c.cached != null) n += c.cached.Count;
                return n;
            }
        }
    }

    /// <summary>
    /// 把 OutlookSettings 里所有启用的日历拉一遍：.ics 走网络/本地文件，graph 走登录令牌。
    /// 每份日历各自更新 lastSync / lastError / cached，最后把结果汇总给调用方；
    /// 一份失败不影响其它日历，错误串会带上日历名。
    /// </summary>
    public static class CalendarSync
    {
        public static List<ReminderEvent> Poll(OutlookSettings cfg, DateTime from, DateTime to, int timeoutMs, out string error)
        {
            var all = new List<ReminderEvent>();
            error = "";
            if (cfg == null) return all;
            if (cfg.calendars == null) cfg.calendars = new List<CalendarSource>();

            var errors = new List<string>();
            int okCount = 0;
            string newest = "";
            foreach (var c in cfg.calendars)
            {
                if (c == null || !c.enabled || !c.HasSource) continue;
                string oneError;
                var list = Fetch(cfg, c, from, to, timeoutMs, out oneError);
                if (list != null)
                {
                    c.cached = list;
                    c.lastSync = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    c.lastError = "";
                    okCount++;
                    if (c.lastSync.CompareTo(newest) > 0) newest = c.lastSync;
                }
                else
                {
                    c.lastError = oneError ?? "同步失败";
                    errors.Add((string.IsNullOrWhiteSpace(c.name) ? "日历" : c.name) + "：" + c.lastError);
                }
            }

            // 汇总用每份日历「最后一次成功」的缓存：某一份这次失败，也不该把它的日程从提醒池里抹掉
            foreach (var c in cfg.calendars)
            {
                if (c == null || !c.enabled || !c.HasSource || c.cached == null) continue;
                foreach (var e in c.cached) if (e != null) all.Add(e);
            }
            // 同一个 id 可能被两份日历都推下来（比如同一份日历订阅了两次），只留一条
            all = Dedupe(all);
            if (okCount > 0)
            {
                cfg.cached = all;
                cfg.lastSync = newest;
            }
            cfg.lastError = errors.Count == 0 ? "" : string.Join("；", errors.ToArray());
            cfg.enabled = cfg.HasEnabled;
            if (errors.Count > 0) error = cfg.lastError;
            return all;
        }

        /// <summary>拉单份日历。成功返回日程列表（可能是空表），失败返回 null。</summary>
        public static List<ReminderEvent> Fetch(OutlookSettings cfg, CalendarSource c, DateTime from, DateTime to,
            int timeoutMs, out string error)
        {
            error = "";
            List<ReminderEvent> list = null;
            try
            {
                if (c.source == "graph")
                {
                    string refresh = OutlookGraph.Unprotect(cfg.refreshToken);
                    if (refresh.Length == 0) { error = "还没登录 Outlook"; return null; }
                    OutlookGraph.TokenInfo tk;
                    string err;
                    if (!OutlookGraph.Refresh(cfg.clientId, cfg.tenant, refresh, timeoutMs, out tk, out err))
                    {
                        error = err.IndexOf("invalid_grant", StringComparison.OrdinalIgnoreCase) >= 0
                            ? "登录已失效，去设置里重新登录 Outlook" : err;
                        return null;
                    }
                    if (!string.IsNullOrEmpty(tk.refreshToken))
                    {
                        cfg.refreshToken = OutlookGraph.Protect(tk.refreshToken);
                        cfg.tokenAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    }
                    if (!OutlookGraph.FetchCalendar(tk.accessToken, from, to, timeoutMs, out list, out err))
                    {
                        error = err;
                        return null;
                    }
                }
                else
                {
                    string text = Ics.ReadText(c.source, c.url, c.path, timeoutMs, out error);
                    if (text == null) return null;
                    list = Ics.Parse(text, from, to);
                }
                int lead = c.leadMinutes > 0 ? c.leadMinutes : Math.Max(0, cfg.leadMinutes);
                foreach (var e in list) e.leadMinutes = lead;
                return list;
            }
            catch (Exception ex)
            {
                error = "读取失败：" + ex.Message;
                return null;
            }
        }

        private static List<ReminderEvent> Dedupe(List<ReminderEvent> list)
        {
            var seen = new Dictionary<string, ReminderEvent>();
            var order = new List<ReminderEvent>();
            foreach (var e in list)
            {
                if (e == null) continue;
                string key = (string.IsNullOrEmpty(e.id) ? e.title + "@" + e.start : e.id);
                if (seen.ContainsKey(key)) continue;
                seen[key] = e;
                order.Add(e);
            }
            order.Sort(delegate (ReminderEvent a, ReminderEvent b)
            {
                return Reminders.ParseStart(a).CompareTo(Reminders.ParseStart(b));
            });
            return order;
        }
    }

    /// <summary>
    /// 提醒卡片的配色：不新增配色，直接从 39 套主题里挑一套（或「跟随当前主题」），
    /// 取那套主题的主色做「深一块 + 浅一块」两层色带。
    /// </summary>
    public static class ReminderStyles
    {
        public const string Follow = "theme";

        /// <summary>C：0=只压暗 1=再往主题色相靠 2=主题底色淡染（对照用）</summary>
        public static int FitMode = 2;

        /// <summary>下拉里的选项：跟随当前主题 + 全部主题（内置 + 自建），每次现算，自建主题加了立刻能选。</summary>
        public static string[] Ids
        {
            get
            {
                var list = new List<string>();
                list.Add(Follow);
                foreach (var t in Themes.All) list.Add(t.id);
                return list.ToArray();
            }
        }

        public static string NameOf(string id)
        {
            if (id == null || id == Follow) return "跟随当前主题";
            return Themes.Get(id).name;
        }

        /// <summary>下拉框里用短名（去掉「经典 · 」这类分组前缀），窄框里也看得全。</summary>
        public static string ShortNameOf(string id)
        {
            if (id == null || id == Follow) return "跟随主题";
            string name = Themes.Get(id).name;
            int cut = name.IndexOf(" · ", StringComparison.Ordinal);
            return cut >= 0 ? name.Substring(cut + 3) : name;
        }

        public static string Normalize(string id, string fallback)
        {
            if (id == Follow) return id;
            if (id != null)
                foreach (var t in Themes.All) if (t.id == id) return id;
            return fallback;
        }

        /// <summary>把一套主题折算成提醒卡片的两层底色和文字色。</summary>
        public static void Resolve(string id, ThemeDef current, out Color a, out Color b, out Color ink)
        {
            ResolveNoFit(id, current, out a, out b, out ink);
            FitToTheme(current != null ? current : Themes.Get(id), ref a, ref b, ref ink);
        }

        /// <summary>只按这套配色自己折算，不贴合当前主题（常驻的下班提醒要走这条：绿就是绿）。</summary>
        public static void ResolveNoFit(string id, ThemeDef current, out Color a, out Color b, out Color ink)
        {
            ThemeDef t = (id == null || id == Follow)
                ? (current != null ? current : Themes.Get("midnight"))
                : Themes.Get(id);
            Color accent = t.accent;
            double lum = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255.0;
            if (lum > 0.55)
            {
                // 亮主色：两块都走浅色，用深字（像「经典 · 松林绿」那种明快的卡）
                a = Mix(accent, Color.Black, 0.10);
                b = Mix(accent, Color.White, 0.42);
                ink = Color.FromArgb(14, 20, 18);
                // 主色偏暗时往上提，保证深字仍然够清楚
                for (int i = 0; i < 10 && Math.Min(Contrast(ink, a), Contrast(ink, b)) < 4.6; i++)
                {
                    a = Mix(a, Color.White, 0.07);
                    b = Mix(b, Color.White, 0.07);
                }
            }
            else
            {
                // 深主色：两块都压深，用浅字（像青绿、靛紫那种沉稳的卡）
                a = Mix(accent, Color.Black, 0.18);
                b = Mix(accent, Color.Black, 0.46);
                ink = Color.FromArgb(246, 249, 255);
                // 主色偏亮时压到白字够清楚为止
                for (int i = 0; i < 10 && Math.Min(Contrast(ink, a), Contrast(ink, b)) < 4.6; i++)
                {
                    a = Mix(a, Color.Black, 0.08);
                    b = Mix(b, Color.Black, 0.08);
                }
            }
        }

        /// <summary>把提醒色块贴到当前主题的明暗上：深色主题别跳出一张浅色卡，浅色主题反过来。</summary>
        public static void FitToTheme(ThemeDef theme, ref Color a, ref Color b, ref Color ink)
        {
            if (theme == null) return;
            double bgLum = (0.2126 * theme.bgBottom.R + 0.7152 * theme.bgBottom.G + 0.0722 * theme.bgBottom.B) / 255.0;
            bool themeDark = bgLum < 0.45;

            if (FitMode == 2)
            {
                // C（默认）：底色就是主题底色，类别色只染一层——但先把类别色提得够纯，染出来才看得见。
                if (themeDark)
                {
                    a = Mix(theme.bgTop, Punch(a), 0.46);
                    b = Mix(theme.bgBottom, Punch(b), 0.30);
                    ink = Mix(theme.textMain, Color.White, 0.28);
                }
                else
                {
                    a = Mix(theme.bgTop, Punch(a), 0.40);
                    b = Mix(theme.bgBottom, Punch(b), 0.28);
                    ink = Color.FromArgb(18, 24, 30);
                }
                Guard(themeDark, ref a, ref b, ref ink);
                return;
            }

            double inkLum = (0.2126 * ink.R + 0.7152 * ink.G + 0.0722 * ink.B) / 255.0;
            bool cardLight = inkLum < 0.5;          // 深字 = 浅色卡
            if (themeDark == !cardLight) return;    // 已经在同一边，不用动

            // 不走「往底色里兑」那条路：深色底 + 暖色会兑成脏灰绿。
            // 改成保色相、只压明度——还是那张橙色的卡，但暗到能和深色主题住在一起。
            if (themeDark)
            {
                a = Reshape(a, theme, 0.62, FitMode == 1 ? 0.60 : 0.0);
                b = Reshape(b, theme, 0.44, FitMode == 1 ? 0.60 : 0.0);
                ink = Mix(theme.textMain, Color.White, 0.30);
            }
            else
            {
                a = Reshape(a, theme, 0.93, FitMode == 1 ? 0.55 : 0.0);
                b = Reshape(b, theme, 0.78, FitMode == 1 ? 0.55 : 0.0);
                ink = Color.FromArgb(16, 22, 26);
            }
            Guard(themeDark, ref a, ref b, ref ink);
        }

        /// <summary>对比度不够就再压一层暗 / 提一点字，两层分不清就再拉开一点。</summary>
        private static void Guard(bool dark, ref Color a, ref Color b, ref Color ink)
        {
            for (int i = 0; i < 12; i++)
            {
                Color step = dark ? Color.Black : Color.White;
                if (Math.Min(Contrast(ink, a), Contrast(ink, b)) >= 4.6) break;
                a = Mix(a, step, 0.10);
                b = Mix(b, step, 0.10);
                ink = Mix(ink, dark ? Color.White : Color.Black, 0.06);
            }
            for (int i = 0; i < 6 && Distance(a, b) < 34; i++)
                b = Mix(b, dark ? Color.Black : Color.White, 0.16);
        }

        /// <summary>先把颜色提到「够纯」，再拿去跟底色混，否则混完就只剩一层灰。</summary>
        private static Color Punch(Color c)
        {
            double h, s, v;
            RgbToHsv(c, out h, out s, out v);
            return HsvToRgb(h, Math.Min(1.0, s * 1.4 + 0.30), Math.Max(v, 0.92));
        }

        /// <summary>保色相、改明度（顺带把饱和度提一点，压暗之后才不至于发灰）。</summary>
        /// <summary>
        /// 把一张卡往当前主题的色相拉近（只拉一部分，保留各自的身份），再改明度压到主题的亮度区间。
        /// pull = 拉多少；直接往底色里兑会把暖色兑成脏灰，所以只在 HSV 里动色相和明度。
        /// </summary>
        private static Color Reshape(Color c, ThemeDef theme, double v, double pull)
        {
            double h, s, val;
            RgbToHsv(c, out h, out s, out val);
            double th, ts, tv;
            RgbToHsv(theme.accent, out th, out ts, out tv);
            h = BlendHue(h, th, pull);
            s = Math.Min(1.0, s * 1.45 + 0.16);   // 压暗之后反而要提饱和度，不然会发灰
            return HsvToRgb(h, s, v);
        }

        /// <summary>沿最短弧把色相往目标挪 pull（0..1）。</summary>
        private static double BlendHue(double h, double target, double pull)
        {
            double d = target - h;
            while (d > 0.5) d -= 1;
            while (d < -0.5) d += 1;
            double r = h + d * pull;
            return r - Math.Floor(r);
        }

        private static void RgbToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            v = max;
            double d = max - min;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) { h = 0; return; }
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }

        private static Color HsvToRgb(double h, double s, double v)
        {
            if (s <= 0)
            {
                int g0 = (int)Math.Round(v * 255);
                return Color.FromArgb(g0, g0, g0);
            }
            double hh = (h - Math.Floor(h)) * 6;
            int i = (int)Math.Floor(hh);
            double f = hh - i;
            double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            double r, g, b;
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromArgb((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
        }

        private static double Distance(Color x, Color y)
        {
            int dr = x.R - y.R, dg = x.G - y.G, db = x.B - y.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static double Contrast(Color fg, Color bg)
        {
            double l1 = RelLum(fg), l2 = RelLum(bg);
            if (l1 < l2) { double tmp = l1; l1 = l2; l2 = tmp; }
            return (l1 + 0.05) / (l2 + 0.05);
        }

        private static double RelLum(Color c)
        {
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        private static double Linear(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static Color Mix(Color x, Color y, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(x.R + (y.R - x.R) * t),
                (int)Math.Round(x.G + (y.G - x.G) * t),
                (int)Math.Round(x.B + (y.B - x.B) * t));
        }
    }

    /// <summary>
    /// 一类日程的提醒样式：扩散范围（卡内 / 全屏）+ 扩散样式（圆形柔光 / 进度条拉伸 / 警戒带）+ 配色。
    /// 四类日程（固定 / 飞书会议 / 普通 / Outlook）各存一份。
    /// </summary>
    public class ReminderStyleDef
    {
        public string spread { get; set; }   // card | screen
        public string shape { get; set; }    // circle | bar | tape
        public string color { get; set; }    // ReminderStyles 的 id；"theme" = 跟随当前主题

        public ReminderStyleDef() { }

        public ReminderStyleDef(string spread, string shape, string color)
        {
            this.spread = spread;
            this.shape = shape;
            this.color = color;
        }

        public ReminderStyleDef Clone()
        {
            return new ReminderStyleDef(spread, shape, color);
        }

        public bool SameAs(ReminderStyleDef o)
        {
            return o != null && spread == o.spread && shape == o.shape && color == o.color;
        }
    }

    public class UiSettings
    {
        public int x { get; set; }
        public int y { get; set; }
        public bool docked { get; set; }
        public string edge { get; set; }        // right | left | free
        public int opacity { get; set; }        // 40..100
        public string accent { get; set; }      // #RRGGBB（旧字段，保留兼容）
        public string theme { get; set; }       // 主题 id
        public bool alwaysOnTop { get; set; }
        public int autoCollapseSeconds { get; set; }
        public bool showLunch { get; set; }     // 是否显示午休倒计时
        public string reminderEffect { get; set; } // none | glow | particles | aurora
        public bool reducedMotion { get; set; }
        public int reminderHoldSeconds { get; set; } // 3..20
        public string reminderSpread { get; set; }      // card | screen：提醒扩散范围
        public string reminderSpreadStyle { get; set; } // circle | bar | tape：提醒扩散样式（tape=全屏警戒带）
        public string cardGlow { get; set; }            // classic | corner | bar | all：卡片背景光效
        public string bgStyle { get; set; }             // solid | glass：主题背景风格（glass = 磨砂玻璃）
        public string lunchReminderStyle { get; set; }   // 午休提醒配色（ReminderStyles 的 id）
        public string eveningReminderStyle { get; set; } // 晚休提醒配色
        public string offReminderStyle { get; set; }     // 下班提醒配色
        public string textFont { get; set; }             // 中文字体族，空 = 跟随默认（苹方 / Noto / 雅黑）
        public string numberFont { get; set; }           // 数字字体族，默认 Consolas

        // 四类日程各自的通知样式：固定 / 飞书会议 / 普通 / Outlook
        public ReminderStyleDef styleFixed { get; set; }
        public ReminderStyleDef styleLark { get; set; }
        public ReminderStyleDef styleLocal { get; set; }
        public ReminderStyleDef styleOutlook { get; set; }

        public UiSettings()
        {
            x = -1;
            y = 260;
            docked = true;
            edge = "right";
            opacity = 94;
            accent = "#4C8DFF";
            theme = "midnight";
            alwaysOnTop = true;
            autoCollapseSeconds = 20;
            showLunch = true;
            reminderEffect = "glow";
            reducedMotion = false;
            reminderHoldSeconds = 6;
            reminderSpread = "card";
            reminderSpreadStyle = "circle";
            cardGlow = "classic";
            bgStyle = "solid";
            lunchReminderStyle = "lan-yuming";
            eveningReminderStyle = "zi-jiegeng";
            offReminderStyle = "forest";
            styleFixed = new ReminderStyleDef("card", "circle", ReminderStyles.Follow);
            styleLark = new ReminderStyleDef("card", "circle", ReminderStyles.Follow);
            styleLocal = new ReminderStyleDef("card", "circle", ReminderStyles.Follow);
            styleOutlook = new ReminderStyleDef("card", "circle", ReminderStyles.Follow);
            textFont = "";
            numberFont = "Consolas";
        }
    }

    /// <summary>工资计算器：月薪 + 发薪日。应出勤天数按「考勤」页的工作日 / 节假日 / 调休自动算。</summary>
    public class SalarySettings
    {
        public decimal monthly { get; set; }    // 月工资
        public int payday { get; set; }         // 发薪日 1..31（当月没有这一天时按最后一天）

        public SalarySettings()
        {
            monthly = 0m;
            payday = 15;
        }
    }

    // ---------------- 小纸条（会话） ----------------

    /// <summary>
    /// 小纸条的一个「对话」：5 位数字 = 和某个人的 1:1 对话（id 是对方 ID）；
    /// 其它任意 5 位字符 = 对话码，输同一个码的人自动进同一个对话（id 就是对话码）。
    /// 两种都只在本机存备注。
    /// </summary>
    public class NoteContact
    {
        public string id { get; set; }
        public string remark { get; set; }      // 本机给对方起的备注（只存在本机）
        public string nick { get; set; }        // 对方自己设置的昵称（从在线状态 / 消息里自动拿到，不是本机备注）

        public NoteContact() { id = ""; remark = ""; nick = ""; }

        /// <summary>是不是「对话码」对话（同一码的人共处一个对话），而不是按个人 ID 的 1:1。</summary>
        public bool IsRoom { get { return NoteSettings.ValidCode(id) && !NoteSettings.ValidId(id); } }

        /// <summary>
        /// 列表里显示的名字：本机备注 &gt; 对方自己起的昵称 &gt; ID / 对话码。
        /// 对话码房间没有昵称概念，昵称为空时和以前一模一样。
        /// </summary>
        public string Display()
        {
            if (!string.IsNullOrWhiteSpace(remark)) return remark;
            if (!string.IsNullOrWhiteSpace(nick)) return nick;
            return NoteSettings.ValidId(id) ? "ID " + id : "对话 " + id;
        }
    }

    /// <summary>一条小纸条消息；本地只留最近 8 条。</summary>
    public class NoteMessage
    {
        public string from { get; set; }        // 发送方 5 位 ID
        public string to { get; set; }          // 接收方 5 位 ID
        public string text { get; set; }        // 一行文字，最长 200 字
        public long ts { get; set; }            // Unix 秒
        public string fromNick { get; set; }    // 发送方自己设置的昵称（没有就是 ""）

        public NoteMessage() { from = ""; to = ""; text = ""; fromNick = ""; }

        public NoteMessage(string from, string to, string text, long ts)
        {
            this.from = from; this.to = to; this.text = text; this.ts = ts;
            this.fromNick = "";
        }

        public NoteMessage(string from, string to, string text, long ts, string fromNick)
        {
            this.from = from; this.to = to; this.text = text; this.ts = ts;
            this.fromNick = fromNick ?? "";
        }

        public bool IsMine(string myId)
        {
            return string.Equals(from, myId, StringComparison.Ordinal);
        }
    }

    /// <summary>和某一个联系人的会话：最多 8 条消息 + 未读数。</summary>
    public class NoteThread
    {
        public string contact { get; set; }
        public List<NoteMessage> messages { get; set; }
        public int unread { get; set; }

        public NoteThread() { contact = ""; messages = new List<NoteMessage>(); unread = 0; }
    }

    /// <summary>小纸条设置：本机 ID、面板样式、公共中继、对话（个人 ID / 对话码）、8 条消息。</summary>
    public class NoteSettings
    {
        public const int MaxMessages = 8;
        public const int MaxText = 200;
        public const int MaxRemark = 8;
        public const int MaxNick = 12;
        public const int CodeLen = 5;

        public string id { get; set; }              // 本机 5 位 ID
        public string nickname { get; set; }        // 本机昵称（对方添加我 / 群聊里直接看到）
        public string style { get; set; }           // drawer（A 抽屉式）| columns（C 极简双栏）
        public string relayHost { get; set; }       // 公共 MQTT 中继
        public int relayPort { get; set; }
        public List<NoteContact> contacts { get; set; }   // 已保存的对话（个人 ID 或对话码）
        public List<NoteThread> threads { get; set; }

        public NoteSettings()
        {
            id = "";
            nickname = "";
            style = "drawer";
            relayHost = "broker.hivemq.com";
            relayPort = 1883;
            contacts = new List<NoteContact>();
            threads = new List<NoteThread>();
        }

        /// <summary>昵称统一处理：null → ""、去首尾空白、最长 MaxNick 个字。</summary>
        public static string CleanNick(string nick)
        {
            nick = (nick ?? "").Trim();
            if (nick.Length > MaxNick) nick = nick.Substring(0, MaxNick);
            return nick;
        }

        /// <summary>第一次用 / 重新生成时给一个 5 位数。</summary>
        public static string GenerateId()
        {
            var rnd = new Random(Guid.NewGuid().GetHashCode());
            return rnd.Next(10000, 100000).ToString(CultureInfo.InvariantCulture);
        }

        public static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length != 5) return false;
            for (int i = 0; i < id.Length; i++) if (id[i] < '0' || id[i] > '9') return false;
            return id[0] != '0';
        }

        /// <summary>
        /// 对话码：正好 5 个字符（中文 / 字母 / 数字 / 混合都行），不含空白和控制字符。
        /// 纯 5 位数字也是合法码，但那种按「个人 ID」处理（见 IsRoom）。
        /// </summary>
        public static bool ValidCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 5) return false;
            for (int i = 0; i < code.Length; i++)
            {
                char ch = code[i];
                if (char.IsWhiteSpace(ch) || char.IsControl(ch)) return false;
            }
            return true;
        }

        /// <summary>去掉首尾空白（面板 / 设置页输入框用）。</summary>
        public static string NormalizeCode(string code)
        {
            return (code ?? "").Trim();
        }

        /// <summary>这个 5 位码是「对话码」对话（同码的人进同一个对话），不是按个人 ID 的 1:1。</summary>
        public bool IsRoom(string code)
        {
            code = NormalizeCode(code);
            return ValidCode(code) && !ValidId(code);
        }

        public void Normalize()
        {
            if (!ValidId(id)) id = GenerateId();
            nickname = CleanNick(nickname);
            if (style != "columns") style = "drawer";
            if (string.IsNullOrWhiteSpace(relayHost)) relayHost = "broker.hivemq.com";
            if (relayPort <= 0 || relayPort > 65535) relayPort = 1883;
            if (contacts == null) contacts = new List<NoteContact>();
            if (threads == null) threads = new List<NoteThread>();
            foreach (var c in contacts)
            {
                if (c == null) continue;
                if (c.remark == null) c.remark = "";
                if (c.remark.Length > MaxRemark) c.remark = c.remark.Substring(0, MaxRemark);
                c.nick = CleanNick(c.nick);
            }
            var seen = new HashSet<string>();
            contacts.RemoveAll(delegate(NoteContact c) { return c == null || !ValidCode(c.id) || !seen.Add(c.id); });
            foreach (var t in threads)
            {
                if (t == null) continue;
                if (t.messages == null) t.messages = new List<NoteMessage>();
                if (t.messages.Count > MaxMessages)
                    t.messages.RemoveRange(0, t.messages.Count - MaxMessages);
                foreach (var m in t.messages)
                {
                    if (m == null) continue;
                    if (m.text == null) m.text = "";
                    if (m.text.Length > MaxText) m.text = m.text.Substring(0, MaxText);
                    m.fromNick = CleanNick(m.fromNick);
                }
                t.messages.RemoveAll(delegate(NoteMessage m) { return m == null; });
                if (t.unread < 0) t.unread = 0;
            }
            threads.RemoveAll(delegate(NoteThread t) { return t == null || !ValidCode(t.contact); });
            // 收到陌生人的纸条后，会话里可能还没有对应联系人：补一个（显示 ID），不然读不到
            foreach (var t in threads)
            {
                if (Contact(t.contact) != null) continue;
                var c = new NoteContact();
                c.id = t.contact;
                c.remark = "";
                c.nick = "";
                contacts.Add(c);
            }
        }

        public NoteContact Contact(string contactId)
        {
            foreach (var c in contacts) if (c.id == contactId) return c;
            return null;
        }

        /// <summary>
        /// 记录某个联系人「对方自己设置的昵称」（从在线状态 / 消息里拿到的）。
        /// 昵称统一 trim + 截 MaxNick；有变化返回 true，联系人不存在或昵称没变返回 false。
        /// 本机备注（remark）不受影响。
        /// </summary>
        public bool SetContactNick(string contactId, string nick)
        {
            var c = Contact(NormalizeCode(contactId));
            if (c == null) return false;
            nick = CleanNick(nick);
            if (string.Equals(c.nick ?? "", nick, StringComparison.Ordinal)) return false;
            c.nick = nick;
            return true;
        }

        public NoteThread Thread(string contactId)
        {
            foreach (var t in threads) if (t.contact == contactId) return t;
            var nt = new NoteThread();
            nt.contact = contactId;
            threads.Add(nt);
            return nt;
        }

        /// <summary>
        /// 收到 / 发出时把消息塞进对应会话，超过 8 条从最旧的丢。返回 true 表示会话里新增了。
        /// 对话码消息的 to 是 5 位码（from 仍是发送者的个人 ID），会话键就是那个码。
        /// </summary>
        public bool Append(NoteMessage m, bool incoming)
        {
            if (m == null) return false;
            bool toRoom = IsRoom(m.to);
            bool fromRoom = IsRoom(m.from);
            if (!toRoom && !ValidId(m.to)) return false;
            if (!fromRoom && !ValidId(m.from)) return false;
            string other = toRoom ? m.to
                : fromRoom ? m.from
                : (string.Equals(m.from, id, StringComparison.Ordinal) ? m.to : m.from);
            var t = Thread(other);
            t.messages.Add(m);
            while (t.messages.Count > MaxMessages) t.messages.RemoveAt(0);
            if (incoming) t.unread++;
            return true;
        }

        public int TotalUnread()
        {
            int n = 0;
            foreach (var t in threads) n += t.unread;
            return n;
        }

        public void MarkRead(string contactId)
        {
            var t = Thread(contactId);
            t.unread = 0;
        }

        /// <summary>
        /// 新建一个对话：5 位数字 = 和这个 ID 的人 1:1；其它任意 5 位字符 = 对话码（同码的人进同一个对话）。
        /// 已存在就只更新备注，返回 false。会话线程一并建好。
        /// </summary>
        public bool AddConversation(string code, string remark)
        {
            code = NormalizeCode(code);
            if (!ValidCode(code)) return false;
            remark = (remark ?? "").Trim();
            if (remark.Length > MaxRemark) remark = remark.Substring(0, MaxRemark);
            var c = Contact(code);
            if (c != null) { c.remark = remark; return false; }
            c = new NoteContact();
            c.id = code;
            c.remark = remark;
            contacts.Add(c);
            Thread(code);
            return true;
        }

        /// <summary>兼容旧调用：按个人 ID 加对话（必须是 5 位数字）。</summary>
        public bool AddContact(string contactId, string remark)
        {
            if (!ValidId(NormalizeCode(contactId))) return false;
            return AddConversation(contactId, remark);
        }

        public void RemoveContact(string contactId)
        {
            contacts.RemoveAll(delegate(NoteContact c) { return c.id == contactId; });
            threads.RemoveAll(delegate(NoteThread t) { return t.contact == contactId; });
        }
    }

    public class Settings
    {
        public int version { get; set; }

        public string mode { get; set; }            // "fixed" 固定班次 | "flex" 弹性工时
        public string startTime { get; set; }       // 固定班次上班时间 / 弹性模式参考上班时间
        public string endTime { get; set; }         // 固定班次下班时间
        public int flexMinutes { get; set; }        // 弹性模式每日应勤分钟数
        public bool lunchDeduct { get; set; }       // 是否扣除午休
        public string lunchStart { get; set; }
        public string lunchEnd { get; set; }
        public bool eveningEnabled { get; set; }     // 是否启用晚休
        public string eveningStart { get; set; }
        public string eveningEnd { get; set; }
        public bool breakNotify { get; set; }        // 午休/晚休开始与结束时用提醒动画提示

        public bool[] workdays { get; set; }        // 索引 0=周日 .. 6=周六
        public List<HolidayEntry> exceptions { get; set; }
        /// <summary>固定日程（午休/晚休/下班 + 自己加的）。</summary>
        public List<FixedSchedule> schedules { get; set; }
        /// <summary>自己建的主题。</summary>
        public List<CustomTheme> customThemes { get; set; }

        public LarkSettings lark { get; set; }
        public OutlookSettings outlook { get; set; }
        public SalarySettings salary { get; set; }
        public NoteSettings note { get; set; }
        public UiSettings ui { get; set; }
        /// <summary>把另一份设置整体拷过来（导入配置 / 恢复默认用）。</summary>
        public void CopyFrom(Settings o)
        {
            version = o.version;
            mode = o.mode;
            startTime = o.startTime;
            endTime = o.endTime;
            flexMinutes = o.flexMinutes;
            lunchDeduct = o.lunchDeduct;
            lunchStart = o.lunchStart;
            lunchEnd = o.lunchEnd;
            eveningEnabled = o.eveningEnabled;
            eveningStart = o.eveningStart;
            eveningEnd = o.eveningEnd;
            breakNotify = o.breakNotify;
            workdays = o.workdays;
            exceptions = o.exceptions;
            schedules = o.schedules;
            customThemes = o.customThemes;
            lark = o.lark;
            outlook = o.outlook;
            salary = o.salary;
            note = o.note;
            ui = o.ui;
            calendarEnabled = o.calendarEnabled;
            calendarLeadMinutes = o.calendarLeadMinutes;
            eventLeadMinutes = o.eventLeadMinutes;
            calendarPollMinutes = o.calendarPollMinutes;
            events = o.events;
        }

        // ---- 日程提醒 ----
        public bool calendarEnabled { get; set; }       // 读取飞书日程
        public int calendarLeadMinutes { get; set; }    // 会议的默认提前分钟数
        public int eventLeadMinutes { get; set; }       // 手动日程的默认提前分钟数
        public int calendarPollMinutes { get; set; }    // 飞书日程刷新间隔
        public List<ReminderEvent> events { get; set; } // 手动添加的日程

        public Settings()
        {
            version = 1;
            mode = "fixed";
            startTime = "09:00";
            endTime = "18:00";
            flexMinutes = 480;
            lunchDeduct = false;
            lunchStart = "11:50";
            lunchEnd = "13:30";
            eveningEnabled = true;
            eveningStart = "18:00";
            eveningEnd = "18:45";
            breakNotify = true;
            workdays = new bool[] { false, true, true, true, true, true, false };
            exceptions = new List<HolidayEntry>();
            schedules = null;                     // 第一次 Normalize 时从班次/午休/晚休长出来
            customThemes = new List<CustomTheme>();
            lark = new LarkSettings();
            outlook = new OutlookSettings();
            salary = new SalarySettings();
            note = new NoteSettings();
            ui = new UiSettings();
            calendarEnabled = true;
            calendarLeadMinutes = 5;
            eventLeadMinutes = 10;
            calendarPollMinutes = 15;
            events = new List<ReminderEvent>();
        }
    }

    public class SettingsStore
    {
        public string Dir { get; private set; }
        public string SettingsPath { get; private set; }
        public string LogPath { get; private set; }
        /// <summary>便携模式：配置放在 exe 旁边的 data 目录（U 盘 / 免安装）。</summary>
        public bool Portable { get; private set; }

        public SettingsStore()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            Portable = File.Exists(Path.Combine(exeDir, "portable.txt"));
            Dir = Portable
                ? Path.Combine(exeDir, "data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkCountdown");
            try { Directory.CreateDirectory(Dir); } catch { }
            SettingsPath = Path.Combine(Dir, "settings.json");
            LogPath = Path.Combine(Dir, "widget.log");
        }

        public bool Exists
        {
            get { return File.Exists(SettingsPath); }
        }

        /// <summary>把当前设置另存为一份（给别人复用 / 备份）。</summary>
        public bool Export(Settings settings, string path)
        {
            try
            {
                settings.Normalize();
                var ser = new JavaScriptSerializer();
                File.WriteAllText(path, PrettyJson(ser.Serialize(settings)), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                Log("导出配置失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>从别的配置文件导入（覆盖当前设置）。</summary>
        public Settings Import(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                var s = ser.Deserialize<Settings>(text);
                if (s == null) return null;
                s.Normalize();
                return s;
            }
            catch (Exception ex)
            {
                Log("导入配置失败: " + ex.Message);
                return null;
            }
        }

        public Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string text = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var ser = new JavaScriptSerializer();
                        var loaded = ser.Deserialize<Settings>(text);
                        if (loaded != null)
                        {
                            int rawVersion = loaded.version;
                            loaded.Normalize();
                            // v3.5 的工作日错位迁移生效后立刻落盘，免得下次启动又按旧数据算
                            if (loaded.version != rawVersion) Save(loaded);
                            return loaded;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("读取设置失败: " + ex.Message);
            }
            var fresh = new Settings();
            fresh.Normalize();
            return fresh;
        }

        public void Save(Settings settings)
        {
            // 探针 / 截图程序只读配置：设了 WC_NO_SAVE=1 就不回写，免得把用户自己的设置冲掉
            if (Environment.GetEnvironmentVariable("WC_NO_SAVE") == "1") return;
            try
            {
                settings.Normalize();
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(settings);
                File.WriteAllText(SettingsPath, PrettyJson(json), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log("保存设置失败: " + ex.Message);
            }
        }

        public void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    string tail = File.ReadAllText(LogPath, Encoding.UTF8);
                    if (tail.Length > 128 * 1024) tail = tail.Substring(tail.Length - 128 * 1024);
                    File.WriteAllText(LogPath, tail, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static string PrettyJson(string json)
        {
            var sb = new StringBuilder(json.Length + 256);
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[i + 1]); i++; }
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        depth++; sb.Append(c); sb.AppendLine(); sb.Append(new string(' ', depth * 2)); break;
                    case '}':
                    case ']':
                        depth--; sb.AppendLine(); sb.Append(new string(' ', depth * 2)); sb.Append(c); break;
                    case ',':
                        sb.Append(c); sb.AppendLine(); sb.Append(new string(' ', depth * 2)); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 固定日程：每天（或单/双周）在固定时刻响的提醒。午休、晚休、下班也是固定日程，只是角色固定。
    /// </summary>
    public class FixedSchedule
    {
        public string id { get; set; }
        public string name { get; set; }
        public string role { get; set; }        // lunch | evening | offwork | custom
        public string start { get; set; }       // "11:50"；下班为空（按班次算）
        public string end { get; set; }         // "13:30"；空 = 只响一次
        public string repeat { get; set; }      // daily | odd | even
        public string until { get; set; }       // "2026-12-31"；空 = 一直重复
        public bool enabled { get; set; }
        public bool notify { get; set; }        // 到点弹提醒
        public bool endNotify { get; set; }     // 结束时也弹（有结束时间才有意义）
        public bool deduct { get; set; }        // 计入工时扣除（午休用）
        public string styleId { get; set; }     // 配色：主题 id / "theme"

        public FixedSchedule() { }

        public static FixedSchedule Make(string id, string role, string name, string start, string end, string styleId)
        {
            var f = new FixedSchedule();
            f.id = id;
            f.role = role;
            f.name = name;
            f.start = start;
            f.end = end;
            f.repeat = "daily";
            f.until = "";
            f.enabled = true;
            f.notify = true;
            f.endNotify = true;
            f.styleId = styleId;
            return f;
        }

        [ScriptIgnore]      // 算出来的，不进 JSON
        public bool IsOffWork { get { return role == "offwork"; } }

        [ScriptIgnore]
        public string RepeatLabel
        {
            get
            {
                string r = repeat == "odd" ? "单数周" : repeat == "even" ? "双数周" : "每天";
                if (!string.IsNullOrWhiteSpace(until)) r += "，至 " + until;
                return r;
            }
        }

        /// <summary>只看重复规则和截止日期（不判断是不是工作日）。</summary>
        public bool OccursOn(DateTime day)
        {
            if (!string.IsNullOrWhiteSpace(until))
            {
                DateTime last;
                if (DateTime.TryParse(until, out last) && day.Date > last.Date) return false;
                if (DateTime.TryParse(until, out last) && last.Date < new DateTime(2000, 1, 1)) return false;
            }
            if (repeat == "odd" || repeat == "even")
            {
                int week = System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                    day, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                bool odd = week % 2 == 1;
                return repeat == "odd" ? odd : !odd;
            }
            return true;
        }

        public DateTime StartAt(DateTime day)
        {
            int m;
            if (!SettingsExtensions.TryParseTime(start, out m)) return DateTime.MinValue;
            return day.Date.AddMinutes(m);
        }

        public DateTime EndAt(DateTime day)
        {
            int m;
            if (!SettingsExtensions.TryParseTime(end, out m)) return DateTime.MinValue;
            return day.Date.AddMinutes(m);
        }

        public FixedSchedule Clone()
        {
            var f = (FixedSchedule)MemberwiseClone();
            return f;
        }
    }

    /// <summary>用户自建的主题：背景色和进度条色分开选，其余由背景色推导。</summary>
    public class CustomTheme
    {
        public string id { get; set; }
        public string name { get; set; }
        public string accent { get; set; }      // 旧字段，等同进度条色
        public string bg { get; set; }          // #RRGGBB 背景色；空 = 老主题，按 accent 推导
        public string bar { get; set; }         // #RRGGBB 进度条色
    }

    public static class SettingsExtensions
    {
        /// <summary>一类日程当前的通知样式（spread / shape / color）。</summary>
        public static ReminderStyleDef StyleOf(this Settings s, string kind)
        {
            UiSettings ui = s == null ? null : s.ui;
            if (ui == null) return new ReminderStyleDef("card", "circle", ReminderStyles.Follow);
            ReminderStyleDef d = kind == Reminders.KindFixed ? ui.styleFixed
                : kind == Reminders.KindLark ? ui.styleLark
                : kind == Reminders.KindOutlook ? ui.styleOutlook
                : ui.styleLocal;
            if (d == null) return new ReminderStyleDef(s.ui.reminderSpread, s.ui.reminderSpreadStyle, ReminderStyles.Follow);
            return d;
        }

        /// <summary>把某一类的通知样式写回去（设置页改了立刻生效）。</summary>
        public static void SetStyleOf(this Settings s, string kind, ReminderStyleDef d)
        {
            if (s == null || s.ui == null || d == null) return;
            if (kind == Reminders.KindFixed) s.ui.styleFixed = d;
            else if (kind == Reminders.KindLark) s.ui.styleLark = d;
            else if (kind == Reminders.KindOutlook) s.ui.styleOutlook = d;
            else s.ui.styleLocal = d;
        }

        public static ReminderStyleDef NormalizeStyle(ReminderStyleDef d, string fallbackSpread, string fallbackShape)
        {
            if (d == null) d = new ReminderStyleDef();
            if (d.spread != "card" && d.spread != "screen")
                d.spread = fallbackSpread == "screen" ? "screen" : "card";
            if (d.shape != "circle" && d.shape != "bar" && d.shape != "tape")
                d.shape = (fallbackShape == "bar" || fallbackShape == "tape") ? fallbackShape : "circle";
            d.color = ReminderStyles.Normalize(d.color, ReminderStyles.Follow);
            return d;
        }

        /// <summary>
        /// v3.5 之前设置页把「周一..周日」的勾选状态直接按下标写进 workdays（0=周日..6=周六），差一天：
        /// 「周一到周五上班」会被存成「周日 + 周一到周四上班、周五和周六休息」。这里按 schema 版本修一次：
        /// version &lt; 2 的旧配置里，如果看到「周日上班、周一到周四都上班、周六休息」这种明显的错位形态，
        /// 就把数组右移一位还原成真实意图（例如 [日一二三四,五,六]=上班 → 周一~周五）。正常配置不受影响，
        /// 迁移后 version 升到 2，只做一次。
        /// </summary>
        public static bool[] MigrateLegacyWorkdays(bool[] w, int version)
        {
            if (w == null || w.Length != 7 || version >= 2) return w;
            bool shifted = w[0] && w[1] && w[2] && w[3] && w[4] && !w[6];
            if (!shifted) return w;
            var fixedDays = new bool[7];
            for (int i = 0; i < 7; i++) fixedDays[(i + 1) % 7] = w[i];
            return fixedDays;
        }

        public static void Normalize(this Settings s)
        {
            if (s.mode != "fixed" && s.mode != "flex") s.mode = "flex";
            if (string.IsNullOrWhiteSpace(s.startTime)) s.startTime = "09:00";
            if (string.IsNullOrWhiteSpace(s.endTime)) s.endTime = "18:00";
            if (s.flexMinutes <= 0 || s.flexMinutes > 24 * 60) s.flexMinutes = 630;
            if (string.IsNullOrWhiteSpace(s.lunchStart)) s.lunchStart = "11:50";
            if (string.IsNullOrWhiteSpace(s.lunchEnd)) s.lunchEnd = "13:30";
            if (string.IsNullOrWhiteSpace(s.eveningStart)) s.eveningStart = "18:00";
            if (string.IsNullOrWhiteSpace(s.eveningEnd)) s.eveningEnd = "18:45";
            if (s.workdays == null || s.workdays.Length != 7) s.workdays = new bool[] { false, true, true, true, true, true, false };
            s.workdays = MigrateLegacyWorkdays(s.workdays, s.version);
            if (s.version < 2) s.version = 2;    // 工作日索引迁移只做一次（v3.5 起 schema = 2）
            if (s.exceptions == null) s.exceptions = new List<HolidayEntry>();
            if (s.events == null) s.events = new List<ReminderEvent>();
            if (s.calendarLeadMinutes < 0) s.calendarLeadMinutes = 0;
            if (s.calendarLeadMinutes > 120) s.calendarLeadMinutes = 120;
            if (s.eventLeadMinutes < 0) s.eventLeadMinutes = 0;
            if (s.eventLeadMinutes > 120) s.eventLeadMinutes = 120;
            if (s.calendarPollMinutes < 3) s.calendarPollMinutes = 15;
            if (s.calendarPollMinutes > 240) s.calendarPollMinutes = 240;
            if (s.lark == null) s.lark = new LarkSettings();
            if (string.IsNullOrWhiteSpace(s.lark.chatId)) s.lark.enabled = false;   // 没会话就没得读
            if (s.ui == null) s.ui = new UiSettings();
            if (string.IsNullOrWhiteSpace(s.lark.command)) s.lark.command = "lark-cli";
            if (s.lark.pollMinutes < 1) s.lark.pollMinutes = 5;
            if (s.ui.opacity < 30) s.ui.opacity = 94;
            if (s.ui.opacity > 100) s.ui.opacity = 100;
            if (string.IsNullOrWhiteSpace(s.ui.accent)) s.ui.accent = "#4C8DFF";
            if (string.IsNullOrWhiteSpace(s.ui.theme)) s.ui.theme = "midnight";
            bool known = false;
            foreach (var t in Themes.All) { if (t.id == s.ui.theme) { known = true; break; } }
            if (!known) s.ui.theme = "midnight";
            if (s.ui.edge != "right" && s.ui.edge != "left" && s.ui.edge != "free") s.ui.edge = "right";
            if (s.ui.autoCollapseSeconds < 0) s.ui.autoCollapseSeconds = 0;
            if (s.ui.reminderEffect != "none" && s.ui.reminderEffect != "glow"
                && s.ui.reminderEffect != "particles" && s.ui.reminderEffect != "aurora")
                s.ui.reminderEffect = "glow";
            s.ui.reminderHoldSeconds = Math.Max(3, Math.Min(20, s.ui.reminderHoldSeconds));
            if (s.ui.reminderSpread != "screen") s.ui.reminderSpread = "card";
            if (s.ui.reminderSpreadStyle != "bar" && s.ui.reminderSpreadStyle != "tape")
                s.ui.reminderSpreadStyle = "circle";
            if (s.ui.cardGlow != "corner" && s.ui.cardGlow != "bar"
                && s.ui.cardGlow != "all")
                s.ui.cardGlow = "classic";
            if (s.ui.bgStyle != "glass") s.ui.bgStyle = "solid";
            s.ui.lunchReminderStyle = ReminderStyles.Normalize(s.ui.lunchReminderStyle, "lan-yuming");
            s.ui.eveningReminderStyle = ReminderStyles.Normalize(s.ui.eveningReminderStyle, "zi-jiegeng");
            s.ui.offReminderStyle = ReminderStyles.Normalize(s.ui.offReminderStyle, "forest");
            // 四类日程的通知样式：老配置里没有这几项，按旧的全局设置补齐，行为不变
            s.ui.styleFixed = NormalizeStyle(s.ui.styleFixed, s.ui.reminderSpread, s.ui.reminderSpreadStyle);
            s.ui.styleLark = NormalizeStyle(s.ui.styleLark, s.ui.reminderSpread, s.ui.reminderSpreadStyle);
            s.ui.styleLocal = NormalizeStyle(s.ui.styleLocal, s.ui.reminderSpread, s.ui.reminderSpreadStyle);
            s.ui.styleOutlook = NormalizeStyle(s.ui.styleOutlook, s.ui.reminderSpread, s.ui.reminderSpreadStyle);
            if (s.ui.textFont == null) s.ui.textFont = "";
            if (string.IsNullOrWhiteSpace(s.ui.numberFont)) s.ui.numberFont = "Consolas";
            if (s.outlook == null) s.outlook = new OutlookSettings();
            if (string.IsNullOrWhiteSpace(s.outlook.tenant)) s.outlook.tenant = OutlookGraph.DefaultTenant;
            if (s.outlook.clientId == null) s.outlook.clientId = "";
            if (s.outlook.account == null) s.outlook.account = "";
            if (s.outlook.refreshToken == null) s.outlook.refreshToken = "";
            s.outlook.leadMinutes = Math.Max(0, Math.Min(120, s.outlook.leadMinutes));
            if (s.outlook.cached == null) s.outlook.cached = new List<ReminderEvent>();
            NormalizeCalendars(s.outlook);
            s.outlook.enabled = s.outlook.HasEnabled;
            if (s.salary == null) s.salary = new SalarySettings();
            s.salary.monthly = Math.Max(0m, Math.Min(10000000m, s.salary.monthly));
            if (s.salary.payday < 1 || s.salary.payday > 31) s.salary.payday = 15;
            if (s.note == null) s.note = new NoteSettings();
            s.note.Normalize();
            s.ui.x = s.ui.x < -30000 ? -1 : s.ui.x;
            s.NormalizeSchedules();
            s.NormalizeCustomThemes();
        }

        /// <summary>
        /// 老配置（单份日历存在 outlook.url / path / source）迁移到 calendars 列表；
        /// 之后每份日历的 id/名称/来源/提前量都补齐。停用的日历保留着，用户还能再打开。
        /// </summary>
        public static void NormalizeCalendars(OutlookSettings o)
        {
            if (o == null) return;
            if (o.calendars == null) o.calendars = new List<CalendarSource>();

            // 老配置迁移：只有一份来源且列表为空时才做，避免每次 Normalize 重复添加
            if (o.calendars.Count == 0)
            {
                bool legacyGraph = o.source == "graph" && !string.IsNullOrWhiteSpace(o.refreshToken);
                bool legacyFile = o.source == "file" && !string.IsNullOrWhiteSpace(o.path);
                bool legacyUrl = o.source != "file" && o.source != "graph" && !string.IsNullOrWhiteSpace(o.url);
                if (legacyGraph || legacyFile || legacyUrl)
                {
                    var c = new CalendarSource();
                    c.id = "cal-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    c.name = legacyGraph ? (string.IsNullOrWhiteSpace(o.account) ? "Outlook 登录日历" : o.account)
                        : legacyFile ? "本地 .ics" : "Outlook 订阅";
                    c.source = legacyGraph ? "graph" : legacyFile ? "file" : "url";
                    c.url = legacyUrl ? o.url : "";
                    c.path = legacyFile ? o.path : "";
                    c.enabled = o.enabled;
                    c.leadMinutes = 0;
                    c.lastSync = o.lastSync;
                    c.lastError = o.lastError;
                    c.cached = o.cached ?? new List<ReminderEvent>();
                    o.calendars.Add(c);
                }
            }

            var seen = new HashSet<string>();
            foreach (var c in o.calendars)
            {
                if (c == null) continue;
                if (string.IsNullOrWhiteSpace(c.id)) c.id = "cal-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                if (!seen.Add(c.id)) c.id = "cal-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                if (c.name == null) c.name = "";
                if (string.IsNullOrWhiteSpace(c.name))
                    c.name = c.source == "file" ? "本地 .ics" : c.source == "graph" ? "Outlook 登录日历" : "Outlook 订阅";
                if (c.source != "file" && c.source != "graph") c.source = "url";
                if (c.url == null) c.url = "";
                if (c.path == null) c.path = "";
                c.leadMinutes = Math.Max(0, Math.Min(120, c.leadMinutes));
                if (c.cached == null) c.cached = new List<ReminderEvent>();
                if (c.lastSync == null) c.lastSync = "";
                if (c.lastError == null) c.lastError = "";
                if (!c.HasSource) c.enabled = false;
                if (c.source == "graph" && string.IsNullOrWhiteSpace(o.refreshToken)) c.enabled = false;
            }

            // 没有来源、也没有登录的旧配置：整个开关关掉
            if (o.calendars.Count == 0) o.enabled = false;
        }

        /// <summary>
        /// 固定日程：老配置从「午休/晚休 + 两个开关 + 两套配色」长出来；之后日程列表就是唯一真相，
        /// 再把这几个老字段同步回去（算工时、卡片文案还在用它们）。
        /// </summary>
        public static void NormalizeSchedules(this Settings s)
        {
            if (s.schedules == null) s.schedules = new List<FixedSchedule>();
            if (s.ScheduleOf("lunch") == null)
            {
                var f = FixedSchedule.Make("lunch", "lunch", "午休", s.lunchStart, s.lunchEnd, s.ui.lunchReminderStyle);
                f.deduct = s.lunchDeduct;
                f.endNotify = true;
                f.styleId = ReminderStyles.Normalize(f.styleId, "lan-yuming");
                s.schedules.Insert(0, f);
            }
            if (s.ScheduleOf("evening") == null)
            {
                var f = FixedSchedule.Make("evening", "evening", "晚休", s.eveningStart, s.eveningEnd, s.ui.eveningReminderStyle);
                f.enabled = s.eveningEnabled;
                f.endNotify = true;
                f.styleId = ReminderStyles.Normalize(f.styleId, "zi-jiegeng");
                s.schedules.Insert(1, f);
            }
            if (s.ScheduleOf("offwork") == null)
            {
                var f = FixedSchedule.Make("offwork", "offwork", "下班", "", "", s.ui.offReminderStyle);
                f.styleId = ReminderStyles.Normalize(f.styleId, "forest");
                s.schedules.Add(f);
            }

            var seen = new List<string>();
            foreach (var f in s.schedules)
            {
                if (f == null) continue;
                if (string.IsNullOrWhiteSpace(f.id)) f.id = (f.role == null ? "custom" : f.role) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                if (seen.Contains(f.id)) f.id = f.id + "-2";
                seen.Add(f.id);
                if (f.role != "lunch" && f.role != "evening" && f.role != "offwork") f.role = "custom";
                if (string.IsNullOrWhiteSpace(f.name)) f.name = f.role == "lunch" ? "午休" : f.role == "evening" ? "晚休" : f.role == "offwork" ? "下班" : "固定日程";
                if (f.repeat != "daily" && f.repeat != "odd" && f.repeat != "even") f.repeat = "daily";
                if (f.until == null) f.until = "";
                f.styleId = ReminderStyles.Normalize(f.styleId, "theme");
                if (f.role == "offwork") { f.start = ""; f.end = ""; }
                else
                {
                    int probe;
                    if (!SettingsExtensions.TryParseTime(f.start, out probe)) f.start = "09:00";
                    if (!string.IsNullOrWhiteSpace(f.end) && !SettingsExtensions.TryParseTime(f.end, out probe)) f.end = "";
                }
            }

            var lunch = s.ScheduleOf("lunch");
            var evening = s.ScheduleOf("evening");
            if (lunch != null)
            {
                s.lunchStart = lunch.enabled ? lunch.start : "";
                s.lunchEnd = string.IsNullOrWhiteSpace(lunch.end) ? lunch.start : lunch.end;
                s.lunchDeduct = lunch.enabled && lunch.deduct;
                s.ui.lunchReminderStyle = lunch.styleId;
            }
            if (evening != null)
            {
                s.eveningEnabled = evening.enabled;
                s.eveningStart = evening.enabled ? evening.start : "";
                s.eveningEnd = string.IsNullOrWhiteSpace(evening.end) ? evening.start : evening.end;
                s.ui.eveningReminderStyle = evening.styleId;
            }
            var off = s.ScheduleOf("offwork");
            if (off != null) s.ui.offReminderStyle = off.styleId;
        }

        public static FixedSchedule ScheduleOf(this Settings s, string role)
        {
            if (s.schedules == null) return null;
            foreach (var f in s.schedules)
                if (f != null && f.role == role) return f;
            return null;
        }

        /// <summary>下班时间（按班次算出来的那个），给固定日程列表显示用。</summary>
        public static void NormalizeCustomThemes(this Settings s)
        {
            if (s.customThemes == null) s.customThemes = new List<CustomTheme>();
            var seen = new List<string>();
            foreach (var t in s.customThemes)
            {
                if (t == null) continue;
                if (string.IsNullOrWhiteSpace(t.id)) t.id = "my-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                if (seen.Contains(t.id)) t.id = t.id + "-2";
                seen.Add(t.id);
                if (string.IsNullOrWhiteSpace(t.name)) t.name = "我的主题";
                if (!IsHexColor(t.bar))
                {
                    t.bar = IsHexColor(t.accent) ? t.accent : "#4C8DFF";
                    t.accent = t.bar;
                }
                if (!IsHexColor(t.accent)) t.accent = t.bar;
                if (!IsHexColor(t.bg)) t.bg = null;   // 老主题没有 bg，继续按 accent 推导
            }
            Themes.SetCustom(s.customThemes);
        }

        private static bool IsHexColor(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length != 7 || text[0] != '#') return false;
            for (int i = 1; i < 7; i++)
            {
                char c = text[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        public static bool TryParseTime(string text, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().Replace('：', ':');
            var parts = t.Split(':');
            int h, m;
            if (parts.Length == 2 && int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m))
            {
                if (h >= 0 && h <= 23 && m >= 0 && m <= 59) { minutes = h * 60 + m; return true; }
            }
            if (parts.Length == 1 && int.TryParse(parts[0], out h) && h >= 0 && h <= 23) { minutes = h * 60; return true; }
            return false;
        }

        public static string FormatTime(int minutes)
        {
            minutes = ((minutes % 1440) + 1440) % 1440;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", minutes / 60, minutes % 60);
        }
    }

    /// <summary>从飞书会话解析出的当日打卡情况。</summary>
    public class AttendanceSnapshot
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public DateTime fetchedAt { get; set; }
        public DateTime? clockIn { get; set; }
        public DateTime? clockOut { get; set; }
        public string note { get; set; }
        public int messageCount { get; set; }

        public AttendanceSnapshot()
        {
            ok = false;
            fetchedAt = DateTime.MinValue;
        }
    }

    /// <summary>一套完整的配色。</summary>
    public class ThemeDef
    {
        public string id;
        public string name;
        public Color accent;
        public Color success;
        public Color bgTop;
        public Color bgBottom;
        public Color track;
        public Color textMain;
        public Color textMuted;
        public Color textDim;
        public Color edge;
    }

    public static class Themes
    {
        /// <summary>内置的 39 套。</summary>
        public static readonly ThemeDef[] BuiltIn = BuildAll();

        private static readonly List<ThemeDef> custom = new List<ThemeDef>();

        /// <summary>内置 + 用户自建（自建的排在最后）。</summary>
        public static ThemeDef[] All
        {
            get
            {
                if (custom.Count == 0) return BuiltIn;
                var list = new List<ThemeDef>(BuiltIn);
                list.AddRange(custom);
                return list.ToArray();
            }
        }

        /// <summary>把用户自建的主题装进来（设置变化后调用）。</summary>
        public static void SetCustom(List<CustomTheme> list)
        {
            custom.Clear();
            if (list == null) return;
            foreach (var t in list)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.id)) continue;
                if (!string.IsNullOrWhiteSpace(t.bg))
                    custom.Add(FromColors(t.id, t.name, t.bg, string.IsNullOrWhiteSpace(t.bar) ? t.accent : t.bar));
                else
                    custom.Add(FromAccent(t.id, t.name, string.IsNullOrWhiteSpace(t.bar) ? t.accent : t.bar));
            }
        }

        public static bool IsCustom(string id)
        {
            foreach (var t in custom) if (t.id == id) return true;
            return false;
        }

        /// <summary>从一个主色推导整套配色（自建主题和内置的是同一套算法，保证观感一致）。</summary>
        public static ThemeDef FromAccent(string id, string name, string hex)
        {
            var one = new List<ThemeDef>();
            AddTinted(one, id, name, hex);
            return one[0];
        }

        /// <summary>背景色和进度条色分开：背景决定底色/文字，bar 只决定进度条和高光。</summary>
        public static ThemeDef FromColors(string id, string name, string bgHex, string barHex)
        {
            Color bg = Hex(string.IsNullOrWhiteSpace(bgHex) ? "#14180E" : bgHex);
            Color bar = Hex(string.IsNullOrWhiteSpace(barHex) ? "#A4CB72" : barHex);
            double lum = (0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B) / 255.0;
            bool light = lum > 0.56;
            var t = new ThemeDef();
            t.id = id;
            t.name = name;
            t.accent = bar;
            t.success = bar;
            t.bgTop = light ? Mix(bg, Color.White, 0.04) : bg;
            t.bgBottom = Mix(bg, light ? Color.Black : Color.Black, light ? 0.07 : 0.12);
            t.track = Mix(bg, light ? Color.Black : Color.White, 0.18);
            t.textMain = light ? Mix(bg, Color.Black, 0.86) : Mix(bg, Color.White, 0.90);
            t.textMuted = light ? Mix(bg, Color.Black, 0.55) : Mix(bg, Color.White, 0.62);
            t.textDim = light ? Mix(bg, Color.Black, 0.38) : Mix(bg, Color.White, 0.44);
            t.edge = light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(22, 255, 255, 255);
            return t;
        }

        private static ThemeDef[] BuildAll()
        {
            var list = new List<ThemeDef>();

            // 经典配色：手工调过对比度
            list.Add(Make("midnight", "经典 · 午夜蓝", "#4C8DFF", "#46D39A", "#12161F", "#1B2130", "#2A3242", "#E8EDF7", "#8B95A8", "#6C768A", true));
            list.Add(Make("aurora",   "经典 · 极光青", "#2FD4C4", "#7FE08A", "#0E1A1C", "#15292A", "#23403F", "#E6F4F3", "#86A3A0", "#66817E", true));
            list.Add(Make("sunset",   "经典 · 落日橙", "#FF9A5A", "#8FD98A", "#211610", "#2E2019", "#46301F", "#FBEEDF", "#B49A83", "#8E7763", true));
            list.Add(Make("sakura",   "经典 · 樱花粉", "#FF7FA8", "#7FD8A8", "#20141A", "#2E1E27", "#45262F", "#FCEAF2", "#B893A3", "#8F7080", true));
            list.Add(Make("forest",   "经典 · 松林绿", "#5FD08A", "#A8E06A", "#101C15", "#18291F", "#27402F", "#E9F5EC", "#8FAF9B", "#6D8A79", true));
            list.Add(Make("graphite", "经典 · 石墨灰", "#B9C2D0", "#8FD3A8", "#141619", "#1F2228", "#2E323A", "#F0F2F6", "#929AA8", "#6F7684", true));
            list.Add(Make("ivory",    "经典 · 象牙白", "#3D6BFF", "#2E9E6B", "#FCFDFF", "#EEF1F7", "#D9DFEA", "#1B2230", "#5C6779", "#8A93A5", false));

            // 中国传统色：主色取自色卡，底色/文字由主色推导
            AddTinted(list, "zi-fengxin", "紫 · 凤信", "#CFACC0");
            AddTinted(list, "zi-tengluo", "紫 · 藤萝", "#7D719F");
            AddTinted(list, "zi-doukou",  "紫 · 豆蔻", "#B76192");
            AddTinted(list, "zi-jiegeng", "紫 · 桔梗", "#8A3684");

            AddTinted(list, "lv-zhuang",  "绿 · 竹篁", "#A2D6C0");
            AddTinted(list, "lv-luwei",   "绿 · 芦莠", "#A4CB72");
            AddTinted(list, "lv-kongque", "绿 · 孔雀", "#00914E");
            AddTinted(list, "lv-yuzan",   "绿 · 玉簪", "#8BC5AF");

            AddTinted(list, "hui-lusui",  "灰 · 芦穗", "#A99795");
            AddTinted(list, "hui-banjiu", "灰 · 斑鸠", "#4C182F");
            AddTinted(list, "hui-langyan","灰 · 狼烟", "#57645D");
            AddTinted(list, "hui-yanyu",  "灰 · 燕羽", "#6B5D42");

            AddTinted(list, "zong-chahe", "棕 · 茶褐", "#663610");
            AddTinted(list, "zong-kuangjin","棕 · 矿金", "#885327");
            AddTinted(list, "zong-guibei","棕 · 龟背", "#8C6D3F");
            AddTinted(list, "zong-kafei", "棕 · 咖啡", "#A4572B");

            AddTinted(list, "lan-jianshi","蓝 · 涧石", "#1EA1BF");
            AddTinted(list, "lan-jingtai","蓝 · 景泰", "#0070AF");
            AddTinted(list, "lan-yuming", "蓝 · 玉茗", "#006D82");
            AddTinted(list, "lan-zangqing","蓝 · 藏青", "#2C4B79");

            AddTinted(list, "huang-gancao","黄 · 甘草", "#F8BE40");
            AddTinted(list, "huang-daimao","黄 · 玳瑁", "#E2A451");
            AddTinted(list, "huang-hupo", "黄 · 琥珀", "#F8B713");
            AddTinted(list, "huang-liuli","黄 · 琉璃", "#F2CC13");

            AddTinted(list, "hong-fengye","红 · 枫叶", "#D61D2F");
            AddTinted(list, "hong-fengxian","红 · 凤仙", "#EA6F8C");
            AddTinted(list, "hong-baochun","红 · 报春", "#EF859C");
            AddTinted(list, "hong-jinkui","红 · 锦葵", "#D03350");

            AddTinted(list, "bai-yudu",   "白 · 鱼肚", "#F7F4ED");
            AddTinted(list, "bai-zhenzhu","白 · 珍珠", "#E6DFD5");
            AddTinted(list, "bai-yinmiao","白 · 银苗", "#E7E3E0");
            AddTinted(list, "bai-hehua",  "白 · 荷花", "#FDECDC");

            return list.ToArray();
        }

        /// <summary>按主色推导一整套配色：浅色做主色调，深色做卡片底。</summary>
        private static void AddTinted(List<ThemeDef> list, string id, string name, string hex)
        {
            Color c = Hex(hex);
            double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            var t = new ThemeDef();
            t.id = id;
            t.name = name;

            if (lum > 0.80)
            {
                // 浅色系：卡片本身就是这个色，主色压暗但保留色相
                t.accent = Adjust(c, 0.45, 1.15, 0.22);
                t.success = Color.FromArgb(0x2E, 0x9E, 0x6B);
                t.bgTop = c;
                t.bgBottom = Mix(c, Color.Black, 0.07);
                t.track = Mix(c, Color.Black, 0.14);
                t.textMain = Mix(c, Color.Black, 0.86);
                t.textMuted = Mix(c, Color.Black, 0.55);
                t.textDim = Mix(c, Color.Black, 0.38);
                t.edge = Color.FromArgb(30, 24, 32, 48);
            }
            else
            {
                // 深色系：卡片底取主色的极暗版，主色太暗时提亮（保住色相而不是兑白）
                t.accent = lum < 0.55 ? Adjust(c, Math.Max(lum, 0.52), 1.10, 0.24) : c;
                t.success = Color.FromArgb(0x46, 0xD3, 0x9A);
                t.bgTop = Mix(c, Color.Black, 0.88);
                t.bgBottom = Mix(c, Color.Black, 0.80);
                t.track = Mix(c, Color.Black, 0.60);
                t.textMain = Mix(c, Color.White, 0.90);
                t.textMuted = Mix(c, Color.White, 0.62);
                t.textDim = Mix(c, Color.White, 0.44);
                t.edge = Color.FromArgb(10, 255, 255, 255);
            }
            list.Add(t);
        }

        /// <summary>调整明度并保持色相（比直接向黑白混合更不容易发灰）。</summary>
        private static Color Adjust(Color c, double targetL, double satMul, double minS)
        {
            double h, s, l;
            ToHsl(c, out h, out s, out l);
            s = Math.Min(1.0, Math.Max(s * satMul, minS));
            if (targetL < 0) targetL = 0;
            if (targetL > 1) targetL = 1;
            return FromHsl(h, s, targetL);
        }

        private static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            l = (max + min) / 2.0;
            if (d < 1e-6) { h = 0; s = 0; return; }
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60.0;
        }

        private static Color FromHsl(double h, double s, double l)
        {
            if (s <= 0) return Color.FromArgb((int)Math.Round(l * 255), (int)Math.Round(l * 255), (int)Math.Round(l * 255));
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            double hk = h / 360.0;
            Func<double, double> hue = delegate (double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 1.0 / 2) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            };
            return Color.FromArgb(
                (int)Math.Round(hue(hk + 1.0 / 3) * 255),
                (int)Math.Round(hue(hk) * 255),
                (int)Math.Round(hue(hk - 1.0 / 3) * 255));
        }

        private static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        private static ThemeDef Make(string id, string name, string accent, string success, string bgTop, string bgBottom,
            string track, string textMain, string textMuted, string textDim, bool dark)
        {
            var t = new ThemeDef();
            t.id = id;
            t.name = name;
            t.accent = Hex(accent);
            t.success = Hex(success);
            t.bgTop = Hex(bgTop);
            t.bgBottom = Hex(bgBottom);
            t.track = Hex(track);
            t.textMain = Hex(textMain);
            t.textMuted = Hex(textMuted);
            t.textDim = Hex(textDim);
            t.edge = dark ? Color.FromArgb(10, 255, 255, 255) : Color.FromArgb(30, 24, 32, 48);
            return t;
        }

        public static Color Hex(string hex)
        {
            try
            {
                if (hex != null && hex.Length == 7 && hex[0] == '#')
                    return Color.FromArgb(
                        Convert.ToInt32(hex.Substring(1, 2), 16),
                        Convert.ToInt32(hex.Substring(3, 2), 16),
                        Convert.ToInt32(hex.Substring(5, 2), 16));
            }
            catch { }
            return Color.Magenta;
        }

        public static ThemeDef Get(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                foreach (var t in All)
                    if (t.id == id) return t;
            }
            return All[0];
        }

        public static string[] Names()
        {
            var names = new string[All.Length];
            for (int i = 0; i < All.Length; i++) names[i] = All[i].name;
            return names;
        }
    }

    public class DayPlan
    {
        public bool isWorkday { get; set; }
        public string dayLabel { get; set; }
        public DateTime suggestStart { get; set; }
        public int requiredMinutes { get; set; }
        public bool lunchDeduct { get; set; }
        public DateTime lunchStart { get; set; }
        public DateTime lunchEnd { get; set; }
        public string mode { get; set; }
        public DateTime fixedEnd { get; set; }

        public DateTime TargetEnd(DateTime clockIn)
        {
            DateTime raw;
            if (mode == "fixed") raw = fixedEnd;
            else raw = clockIn.AddMinutes(requiredMinutes);
            if (lunchDeduct) raw = raw.AddMinutes(LunchOverlap(clockIn, raw));
            return raw;
        }

        public int LunchOverlap(DateTime from, DateTime to)
        {
            if (!lunchDeduct) return 0;
            DateTime a = from > lunchStart ? from : lunchStart;
            DateTime b = to < lunchEnd ? to : lunchEnd;
            if (b <= a) return 0;
            return (int)Math.Round((b - a).TotalMinutes);
        }
    }

    public static class Policy
    {
        public static DayPlan Build(Settings s, DateTime day)
        {
            var plan = new DayPlan();
            plan.mode = s.mode;
            plan.requiredMinutes = s.flexMinutes;
            plan.lunchDeduct = s.lunchDeduct;
            plan.dayLabel = WeekdayName(day.DayOfWeek);

            int startMin, endMin, lunchA, lunchB;
            SettingsExtensions.TryParseTime(s.startTime, out startMin);
            SettingsExtensions.TryParseTime(s.endTime, out endMin);
            SettingsExtensions.TryParseTime(s.lunchStart, out lunchA);
            SettingsExtensions.TryParseTime(s.lunchEnd, out lunchB);

            plan.suggestStart = day.Date.AddMinutes(startMin);
            plan.fixedEnd = day.Date.AddMinutes(endMin);
            plan.lunchStart = day.Date.AddMinutes(lunchA);
            plan.lunchEnd = day.Date.AddMinutes(lunchB);

            bool workday = s.workdays[(int)day.DayOfWeek];
            string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var ex in s.exceptions)
            {
                if (ex == null || ex.date != key) continue;
                if (ex.kind == "off") { workday = false; plan.dayLabel = WeekdayName(day.DayOfWeek) + " · 休息"; }
                else if (ex.kind == "work") { workday = true; plan.dayLabel = WeekdayName(day.DayOfWeek) + " · 调休上班"; }
            }
            if (workday && plan.dayLabel == WeekdayName(day.DayOfWeek) && (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday))
                plan.dayLabel = WeekdayName(day.DayOfWeek) + " · 周末加班";

            plan.isWorkday = workday;
            return plan;
        }

        public static DateTime NextWorkdayStart(Settings s, DateTime from)
        {
            DateTime d = from.Date.AddDays(1);
            for (int i = 0; i < 14; i++)
            {
                var p = Build(s, d);
                if (p.isWorkday) return p.suggestStart;
                d = d.AddDays(1);
            }
            return from.Date.AddDays(1).AddMinutes(9 * 60);
        }

        public static string WeekdayName(DayOfWeek w)
        {
            switch (w)
            {
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                case DayOfWeek.Saturday: return "周六";
                default: return "周日";
            }
        }

        public static string CompactDuration(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = ts.Negate();
            int h = (int)ts.TotalHours;
            int m = ts.Minutes;
            if (h > 0) return string.Format("{0}h{1:00}m", h, m);
            if (m > 0) return string.Format("{0}m", m);
            return string.Format("{0}s", ts.Seconds);
        }

        public static string Clock(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = ts.Negate();
            // 不足一小时：分:秒.厘秒，秒后面那两位每 50ms 跳一次，肉眼能看到它在走
            if (ts.TotalMinutes < 60)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2:00}",
                    (int)ts.TotalMinutes, ts.Seconds, ts.Milliseconds / 10);
            }
            int totalHours = (int)ts.TotalHours;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", totalHours, ts.Minutes, ts.Seconds);
        }

        public static string Hm(int minutes)
        {
            if (minutes >= 60)
            {
                int h = minutes / 60, m = minutes % 60;
                return m == 0 ? string.Format("{0} 小时", h) : string.Format("{0} 小时 {1} 分", h, m);
            }
            return string.Format("{0} 分钟", minutes);
        }
    }

    /// <summary>某一刻的工资面板数据。</summary>
    public class SalarySnapshot
    {
        public bool configured { get; set; }
        public decimal monthly { get; set; }
        public decimal daily { get; set; }
        public decimal hourly { get; set; }
        public decimal today { get; set; }
        public decimal month { get; set; }
        public decimal attendedDays { get; set; }
        public int requiredWorkdays { get; set; }
        public int elapsedWorkdays { get; set; }
        public int payday { get; set; }
        public DateTime paydayDate { get; set; }
        public int daysToPayday { get; set; }
        public bool isWorkday { get; set; }
        public bool clockedIn { get; set; }
        public bool clockedOut { get; set; }
        public bool capped { get; set; }
        public string note { get; set; }
        public double progress { get; set; }
        public TimeSpan worked { get; set; }
        public TimeSpan required { get; set; }
    }

    /// <summary>
    /// 工资计算：日薪 = 月薪 ÷ 本月应出勤天数；时薪 = 日薪 ÷ 当日应勤小时数。
    /// 今日收入 = 时薪 × 今日已工作小时（扣午休/晚休，最多到日薪）。
    /// 本月收入 = 日薪 × 本月已完成的工作日 + 今日收入。
    /// 没有历史打卡记录，所以只按「日期 + 今天这一次打卡」推算，这一点在设置页写清楚。
    /// </summary>
    public static class SalaryCalc
    {
        public static string Money3(decimal v)
        {
            return v.ToString("N3", CultureInfo.InvariantCulture);
        }

        public static SalarySnapshot Compute(Settings s, AttendanceSnapshot snap, DateTime now)
        {
            var r = new SalarySnapshot();
            if (s == null || s.salary == null || s.salary.monthly <= 0m) return r;
            r.configured = true;
            r.monthly = s.salary.monthly;
            r.payday = Math.Max(1, Math.Min(31, s.salary.payday));
            r.note = "";

            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            int reqDays = RequiredWorkdays(s, monthStart);
            r.requiredWorkdays = reqDays;
            r.daily = s.salary.monthly / reqDays;
            r.hourly = r.daily / (DailyMinutes(s, now) / 60m);

            var plan = Policy.Build(s, now);
            r.isWorkday = plan.isWorkday;
            r.required = TimeSpan.FromMinutes(DailyMinutes(s, now));

            // 今天这次打卡（和卡片用的是同一个快照；没接飞书就按班次时间算）
            DateTime? inT = snap != null ? snap.clockIn : null;
            DateTime? outT = snap != null ? snap.clockOut : null;
            if (!s.lark.enabled || string.IsNullOrWhiteSpace(s.lark.chatId))
                inT = plan.isWorkday ? plan.suggestStart : (DateTime?)null;
            if (inT.HasValue && inT.Value.Date != now.Date) inT = null;
            if (outT.HasValue && (outT.Value.Date != now.Date || outT.Value > now)) outT = null;
            if (inT.HasValue && inT.Value > now) inT = null;

            if (plan.isWorkday && inT.HasValue)
            {
                r.clockedIn = true;
                DateTime end = outT.HasValue ? outT.Value : now;
                if (end < inT.Value) end = inT.Value;
                // 收入按秒累计（面板以 18fps 重绘，后三位小数才会高速跳）；显示工时仍按分钟
                double workedSec = Math.Max(0, (end - inT.Value).TotalSeconds
                    - BreakMinutes(s, plan, inT.Value, end) * 60.0);
                r.worked = TimeSpan.FromSeconds(workedSec);
                double requiredSec = Math.Max(1.0, r.required.TotalSeconds);
                r.progress = Math.Min(1.0, workedSec / requiredSec);
                r.today = r.hourly * ((decimal)workedSec / 3600m);
                if (r.today > r.daily) { r.today = r.daily; r.capped = true; }
                if (outT.HasValue) { r.clockedOut = true; r.note = "已下班"; }
            }
            else if (plan.isWorkday) r.note = "还没打卡";
            else r.note = "今天休息";

            // 本月已经完整过去的工作日（今天单独按打卡算，不重复计）
            int elapsed = 0;
            for (DateTime d = monthStart; d < now.Date; d = d.AddDays(1))
                if (Policy.Build(s, d).isWorkday) elapsed++;
            r.elapsedWorkdays = elapsed;
            r.attendedDays = elapsed + (r.daily > 0m ? r.today / r.daily : 0m);
            r.month = r.daily * elapsed + r.today;

            r.paydayDate = PaydayDate(now, r.payday);
            r.daysToPayday = (r.paydayDate - now.Date).Days;
            return r;
        }

        /// <summary>本月应出勤天数：按工作日 + 节假日 / 调休算。整月都是休息日时兜底 21 天。</summary>
        public static int RequiredWorkdays(Settings s, DateTime month)
        {
            int days = DateTime.DaysInMonth(month.Year, month.Month);
            int n = 0;
            for (int d = 1; d <= days; d++)
                if (Policy.Build(s, new DateTime(month.Year, month.Month, d)).isWorkday) n++;
            return n > 0 ? n : 21;
        }

        /// <summary>发薪日：当月没有这一天（比如 31 号）就按当月最后一天；已经过了就取下一月。</summary>
        public static DateTime PaydayDate(DateTime now, int payday)
        {
            int p = Math.Max(1, Math.Min(31, payday));
            DateTime thisMonth = new DateTime(now.Year, now.Month,
                Math.Min(p, DateTime.DaysInMonth(now.Year, now.Month)));
            if (thisMonth.Date >= now.Date) return thisMonth;
            DateTime next = now.AddMonths(1);
            return new DateTime(next.Year, next.Month,
                Math.Min(p, DateTime.DaysInMonth(next.Year, next.Month)));
        }

        /// <summary>今天应勤多少分钟：固定班次扣掉午休/晚休，弹性班次用用户填的应勤时长。</summary>
        private static int DailyMinutes(Settings s, DateTime now)
        {
            var plan = Policy.Build(s, now);
            if (s.mode != "fixed")
                return plan.requiredMinutes > 0 ? plan.requiredMinutes : 480;
            double total = (plan.fixedEnd - plan.suggestStart).TotalMinutes;
            if (total <= 0) return 480;
            total -= BreakMinutes(s, plan, plan.suggestStart, plan.fixedEnd);
            if (total < 60) total = 480;
            return (int)Math.Round(total);
        }

        /// <summary>与 [from,to] 重叠的午休 + 晚休分钟数。</summary>
        private static int BreakMinutes(Settings s, DayPlan plan, DateTime from, DateTime to)
        {
            int sum = Overlap(from, to, plan.lunchStart, plan.lunchEnd);
            if (s.eveningEnabled)
            {
                int a, b;
                if (SettingsExtensions.TryParseTime(s.eveningStart, out a)
                    && SettingsExtensions.TryParseTime(s.eveningEnd, out b) && b > a)
                {
                    DateTime day = to.Date;
                    sum += Overlap(from, to, day.AddMinutes(a), day.AddMinutes(b));
                }
            }
            int cap = (int)Math.Max(0, (to - from).TotalMinutes);
            return Math.Min(sum, cap);
        }

        private static int Overlap(DateTime from, DateTime to, DateTime breakStart, DateTime breakEnd)
        {
            DateTime a = from > breakStart ? from : breakStart;
            DateTime b = to < breakEnd ? to : breakEnd;
            if (b <= a) return 0;
            return (int)Math.Round((b - a).TotalMinutes);
        }
    }

    public enum CardState { Rest, BeforeWork, NotClockedIn, Working, Overtime, Done }

    public enum LunchState { None, Before, During, After, EveningBefore, EveningDuring, OffSoon }

    public class CardStatus
    {
        public CardState state { get; set; }
        public string headline { get; set; }
        public string bigLabel { get; set; }
        public string big { get; set; }
        public string sub { get; set; }
        public string extra { get; set; }
        public double progress { get; set; }
        public Color accent { get; set; }
        public bool bigIsClock { get; set; }
        public string targetText { get; set; }    // 预计下班 19:30（收起态右侧显示）
        public bool offAlert { get; set; }        // 到了下班时间还没打卡 → 常驻「该下班啦」
        public string targetIso { get; set; }     // 下班时间点（用于提醒卡片）

        // 第二段时间：午休倒计时
        public LunchState lunch { get; set; }
        public string lunchLabel { get; set; }
        public string lunchValue { get; set; }
        public bool lunchCounting { get; set; }   // true = 正在倒数（开始前 / 午休中）
    }

    public static class StatusBuilder
    {
        /// <summary>
        /// 第二行时间：距午休 → 午休中 → 距晚休 → 晚休中 → 马上下班；
        /// 没开晚休时，午休结束后这一行改成「距下班」（由 Build 补，因为要先算出下班时间）。
        /// </summary>
        private static void FillLunch(Settings s, CardStatus st, DateTime now)
        {
            var lunchSched = s.ScheduleOf("lunch");
            var eveningSched = s.ScheduleOf("evening");

            // 午休这一段：固定日程被停用/今天不生效就不显示，但**不影响晚休**（这是之前的 bug：
            // 午休时间没填好会直接 return，把晚休也一起吞掉）
            bool lunchOn = lunchSched == null || (lunchSched.enabled && lunchSched.OccursOn(now));
            int a = 0, b = 0;
            bool lunchOk = lunchOn
                && SettingsExtensions.TryParseTime(s.lunchStart, out a)
                && SettingsExtensions.TryParseTime(s.lunchEnd, out b)
                && b > a;
            // 内置午休只填了开始、没填结束（或填反）时，拿午休时段的一条普通日程顶上
            if (lunchOn && !lunchOk) lunchOk = FindLunchWindow(s, now, out a, out b);

            if (lunchOk)
            {
                DateTime start = now.Date.AddMinutes(a);
                DateTime end = now.Date.AddMinutes(b);
                if (now < start)
                {
                    st.lunch = LunchState.Before;
                    st.lunchLabel = "距午休";
                    st.lunchValue = Policy.CompactDuration(start - now);
                    st.lunchCounting = true;
                    return;
                }
                if (now < end)
                {
                    st.lunch = LunchState.During;
                    st.lunchLabel = "午休中";
                    st.lunchValue = "还剩 " + Policy.CompactDuration(end - now);
                    st.lunchCounting = true;
                    return;
                }
            }

            int ea = 0, eb = 0;
            bool eveningOkA = SettingsExtensions.TryParseTime(s.eveningStart, out ea);
            bool eveningOkB = SettingsExtensions.TryParseTime(s.eveningEnd, out eb);
            bool eveningOccurs = eveningSched == null || (eveningSched.enabled && eveningSched.OccursOn(now));
            bool evening = s.eveningEnabled && eveningOkA && eveningOkB && eb > ea && eveningOccurs;
            if (evening)
            {
                DateTime evStart = now.Date.AddMinutes(ea);
                DateTime evEnd = now.Date.AddMinutes(eb);
                if (now < evStart)
                {
                    st.lunch = LunchState.EveningBefore;
                    st.lunchLabel = "距晚休";
                    st.lunchValue = Policy.CompactDuration(evStart - now);
                    st.lunchCounting = true;
                    return;
                }
                if (now < evEnd)
                {
                    st.lunch = LunchState.EveningDuring;
                    st.lunchLabel = "晚休中";
                    st.lunchValue = "还剩 " + Policy.CompactDuration(evEnd - now);
                    st.lunchCounting = true;
                    return;
                }
                st.lunch = LunchState.OffSoon;
                st.lunchLabel = "马上下班";
                st.lunchValue = "";
                st.lunchCounting = true;
                return;
            }

            if (!lunchOn && !evening)
            {
                // 午休这一行被明确关掉、也没配晚休：第二行整行不显示
                st.lunch = LunchState.None;
                st.lunchLabel = "";
                st.lunchValue = "";
                st.lunchCounting = false;
                return;
            }

            st.lunch = LunchState.After;
            st.lunchLabel = "";
            st.lunchValue = "";
            st.lunchCounting = false;
        }

        /// <summary>
        /// 内置午休没配好时，从普通日程里找一条「午休时段」的：今天生效、10:30–14:30 之间开始、有结束时间。
        /// 用户把午休时段写成普通日程（老版本就是这么加的）也能正常显示「距午休」。
        /// </summary>
        private static bool FindLunchWindow(Settings s, DateTime now, out int a, out int b)
        {
            a = 0;
            b = 0;
            if (s.schedules == null) return false;
            int bestA = int.MaxValue, bestB = 0;
            foreach (var f in s.schedules)
            {
                if (f == null || !f.enabled) continue;
                if (f.role == "lunch" || f.role == "evening" || f.role == "offwork") continue;
                if (!f.OccursOn(now)) continue;
                int fa, fb;
                if (!SettingsExtensions.TryParseTime(f.start, out fa)) continue;
                if (!SettingsExtensions.TryParseTime(f.end, out fb)) continue;
                if (fb <= fa) continue;
                if (fa < 10 * 60 + 30 || fa > 14 * 60 + 30) continue;   // 10:30–14:30
                if (fa < bestA) { bestA = fa; bestB = fb; }
            }
            if (bestA == int.MaxValue) return false;
            a = bestA;
            b = bestB;
            return true;
        }

        /// <summary>没开晚休时：午休结束后这一行显示距下班；到了下班点就写「马上下班」。</summary>
        private static void ResolveAfterLunch(CardStatus st, DateTime? target, DateTime now)
        {
            if (st.lunch != LunchState.After) return;
            if (!target.HasValue) { st.lunch = LunchState.None; st.lunchLabel = ""; st.lunchValue = ""; return; }
            TimeSpan left = target.Value - now;
            if (left.TotalMinutes < 1)
            {
                st.lunch = LunchState.OffSoon;
                st.lunchLabel = "马上下班";
                st.lunchValue = "";
                st.lunchCounting = true;
                return;
            }
            st.lunchLabel = "距下班";
            st.lunchValue = Policy.CompactDuration(left);
            st.lunchCounting = true;
        }

        public static CardStatus Build(Settings s, AttendanceSnapshot snap, DateTime now, ThemeDef theme)
        {
            var plan = Policy.Build(s, now.Date);
            var st = new CardStatus();
            st.accent = theme.accent;
            st.bigIsClock = true;
            st.progress = 0;
            FillLunch(s, st, now);

            if (!plan.isWorkday)
            {
                st.state = CardState.Rest;
                st.headline = "休息日";
                st.bigIsClock = false;
                st.big = "今天休息";
                st.bigLabel = plan.dayLabel;
                st.lunch = LunchState.None;
                st.lunchLabel = "";
                st.lunchValue = "";
                DateTime next = Policy.NextWorkdayStart(s, now);
                st.sub = "下次上班 " + next.ToString("M月d日 HH:mm", CultureInfo.InvariantCulture) + "（" + Policy.WeekdayName(next.DayOfWeek) + "）";
                st.extra = "右键可临时设为上班日";
                return st;
            }

            DateTime? inTime = snap != null ? snap.clockIn : null;
            DateTime? outTime = snap != null ? snap.clockOut : null;

            // 没接飞书：按班次推算，别显示「还没打卡」这种必然出现的话
            if (!s.lark.enabled || string.IsNullOrWhiteSpace(s.lark.chatId))
            {
                inTime = plan.suggestStart;
                outTime = null;
            }

            // 跨天保护：只认当天的打卡记录
            if (inTime.HasValue && inTime.Value.Date != now.Date) inTime = null;
            if (outTime.HasValue && outTime.Value.Date != now.Date) outTime = null;

            if (outTime.HasValue && outTime.Value > now)
                outTime = null; // 防御：不可能出现的未来下班时间

            if (inTime.HasValue && inTime.Value > now)
                inTime = null;

            if (outTime.HasValue)
            {
                st.state = CardState.Done;
                st.headline = "已下班";
                st.bigLabel = "今日在岗";
                TimeSpan worked = inTime.HasValue ? outTime.Value - inTime.Value : TimeSpan.Zero;
                st.big = Policy.CompactDuration(worked);
                st.bigIsClock = false;
                st.sub = inTime.HasValue
                    ? inTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture) + " → " + outTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture)
                    : "下班 " + outTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
                int required = plan.mode == "fixed" ? (int)Math.Round((plan.fixedEnd - plan.suggestStart).TotalMinutes) : plan.requiredMinutes;
                st.extra = worked.TotalMinutes >= required ? "工时已达标 · 应勤 " + Policy.Hm(required) : "工时 " + Policy.CompactDuration(worked) + " / 应勤 " + Policy.CompactDuration(TimeSpan.FromMinutes(required));
                st.progress = 1;
                st.accent = theme.success;
                st.lunch = LunchState.None;
                st.lunchLabel = "";
                st.lunchValue = "";
                return st;
            }

            if (!inTime.HasValue)
            {
                if (now < plan.suggestStart)
                {
                    st.state = CardState.BeforeWork;
                    st.headline = "未上班";
                    st.bigLabel = "距离上班";
                    st.big = Policy.Clock(plan.suggestStart - now);
                    st.sub = "上班时间 " + plan.suggestStart.ToString("HH:mm", CultureInfo.InvariantCulture) + " · " + plan.dayLabel;
                    st.extra = s.lark.enabled ? "飞书里发「1」即完成上班打卡" : "未接入飞书，按班次时间计算";
                    if (st.lunch == LunchState.After) { st.lunch = LunchState.None; st.lunchLabel = ""; st.lunchValue = ""; }
                    return st;
                }
                st.state = CardState.NotClockedIn;
                st.headline = "还没打卡";
                st.bigLabel = "上班已过";
                st.big = Policy.Clock(now - plan.suggestStart);
                st.sub = "上班时间 " + plan.suggestStart.ToString("HH:mm", CultureInfo.InvariantCulture) + " · " + plan.dayLabel;
                st.extra = s.lark.enabled
                    ? (snap != null && !string.IsNullOrEmpty(snap.error) ? "飞书读取失败：" + snap.error : "在飞书给「" + s.lark.botName + "」发「1」完成打卡")
                    : "未接入飞书，按班次时间计算";
                st.accent = Color.FromArgb(242, 179, 76);
                if (st.lunch == LunchState.After) { st.lunch = LunchState.None; st.lunchLabel = ""; st.lunchValue = ""; }
                return st;
            }

            DateTime target = plan.TargetEnd(inTime.Value);
            DateTime start = plan.mode == "fixed" ? plan.suggestStart : inTime.Value;
            double total = Math.Max(1, (target - start).TotalSeconds);
            double elapsed = (now - start).TotalSeconds;
            st.progress = Math.Max(0, Math.Min(1, elapsed / total));
            st.sub = "上班 " + inTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture) + " · 预计下班 " + target.ToString("HH:mm", CultureInfo.InvariantCulture);
            st.targetText = target.ToString("HH:mm", CultureInfo.InvariantCulture);
            st.targetIso = target.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
            ResolveAfterLunch(st, target, now);

            if (now < target)
            {
                st.state = CardState.Working;
                st.headline = "工作中";
                st.bigLabel = "距离下班";
                st.big = Policy.Clock(target - now);
                int requiredT = plan.mode == "fixed" ? (int)Math.Round((target - start).TotalMinutes) : plan.requiredMinutes;
                st.extra = "已工作 " + Policy.CompactDuration(now - inTime.Value) + " · 应勤 " + Policy.CompactDuration(TimeSpan.FromMinutes(requiredT));
                return st;
            }

            st.state = CardState.Overtime;
            st.headline = "已达标";
            st.bigLabel = "可下班 · 已超";
            st.big = Policy.Clock(now - target);
            st.extra = "在岗 " + Policy.CompactDuration(now - inTime.Value) + " · 下班后发「2」记录";
            st.accent = theme.success;
            st.offAlert = true;   // 到了下班时间还没打卡：常驻「该下班啦」，直到飞书同步到下班
            return st;
        }
    }
    /// <summary>
    /// 中文字体：优先苹方（PingFang SC），没有就退到 Noto Sans SC（苹方的开源近亲），
    /// 再没有才用微软雅黑。也可以把 PingFang.ttc 放到 exe 旁边的 fonts\ 目录里，免安装直接用。
    /// </summary>
    public static class UiFont
    {
        private static string regular, bold;
        private static string textOverride = "";
        private static string numberFamily = "Consolas";
        private static System.Drawing.Text.PrivateFontCollection privateFonts;
        // 苹方在 Windows 上注册成「苹方 常规 / 苹方 粗体」两个族，macOS 上才叫 PingFang SC，都要认
        private static readonly string[] Prefer = new string[]
        {
            "PingFang SC", "PingFang HK", "PingFang TC", "苹方 常规", "苹方",
            "Noto Sans SC", "Microsoft YaHei UI"
        };
        private static readonly string[] PreferBold = new string[]
        {
            "PingFang SC", "PingFang HK", "PingFang TC", "苹方 粗体",
            "Noto Sans SC", "Microsoft YaHei UI"
        };

        /// <summary>设置里选的字体：空白 = 跟随默认中文字体；number = 数字字体，默认 Consolas。</summary>
        public static void Configure(string textFamily, string number)
        {
            string t = (textFamily ?? "").Trim();
            if (!string.Equals(t, textOverride, StringComparison.OrdinalIgnoreCase))
            {
                textOverride = t;
                regular = null;
                bold = null;
            }
            numberFamily = string.IsNullOrWhiteSpace(number) ? "Consolas" : number.Trim();
        }

        public static string TextFamily
        {
            get
            {
                if (regular == null) Resolve();
                return regular;
            }
            set { Configure(value, numberFamily); }
        }

        public static string NumberFamily
        {
            get { return numberFamily; }
            set { Configure(textOverride, value); }
        }

        public static string Family
        {
            get
            {
                if (regular == null) Resolve();
                return regular;
            }
        }

        /// <summary>系统 / fonts 目录里能用的字族，给设置页的字体下拉用。</summary>
        public static string[] Families()
        {
            if (privateFonts == null) Resolve();
            var set = new List<string>();
            try
            {
                if (privateFonts != null)
                    foreach (var fam in privateFonts.Families)
                        if (!set.Contains(fam.Name)) set.Add(fam.Name);
            }
            catch { }
            try
            {
                var installed = new System.Drawing.Text.InstalledFontCollection();
                foreach (var fam in installed.Families)
                    if (!set.Contains(fam.Name)) set.Add(fam.Name);
            }
            catch { }
            set.Sort(StringComparer.CurrentCultureIgnoreCase);
            return set.ToArray();
        }

        /// <summary>数字字体（时:分:秒、工资等）。选成非等宽也允许，只是倒计时会不那么齐。</summary>
        public static Font MakeNumber(float size, FontStyle style = FontStyle.Regular)
        {
            try { return new Font(numberFamily, size, style, GraphicsUnit.Point); }
            catch
            {
                try { return new Font("Consolas", size, style, GraphicsUnit.Point); }
                catch
                {
                    try { return new Font(FontFamily.GenericMonospace, size, style, GraphicsUnit.Point); }
                    catch { return SystemFonts.DefaultFont; }
                }
            }
        }

        /// <summary>这一套里有没有单独的粗体族（苹方的「粗体」就是另一个族，不能靠 GDI 伪造）。</summary>
        public static bool HasBoldFace
        {
            get { if (regular == null) Resolve(); return bold != null && bold != regular; }
        }

        /// <summary>是否真的用上了苹方（没装也没放字体文件时为 false）。</summary>
        public static bool IsPingFang
        {
            get
            {
                if (regular == null) Resolve();
                return regular.IndexOf("PingFang", StringComparison.OrdinalIgnoreCase) >= 0
                    || regular.StartsWith("苹方", StringComparison.Ordinal);
            }
        }

        public static Font Make(float size, FontStyle style = FontStyle.Regular)
        {
            if (regular == null) Resolve();
            bool wantBold = (style & FontStyle.Bold) != 0;
            if (wantBold && bold != null && bold != regular)
            {
                // 有独立粗体族：用它 + 去掉 Bold 标志，避免二次加粗
                try
                {
                    return new Font(bold, size, style & ~FontStyle.Bold, GraphicsUnit.Point);
                }
                catch { }
            }
            try { return new Font(regular, size, style, GraphicsUnit.Point); }
            catch
            {
                try { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
                catch { return SystemFonts.DefaultFont; }
            }
        }

        private static string First(List<string> names, string[] prefer)
        {
            foreach (var want in prefer)
                foreach (var name in names)
                    if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase)) return name;
            return null;
        }

        private static void Resolve()
        {
            if (privateFonts != null)
            {
                try { privateFonts.Dispose(); } catch { }
                privateFonts = null;
            }
            var names = new List<string>();
            // 1) exe 旁边 fonts\ 里的字体文件（PingFang.ttc 丢进去即可，不用装到系统）
            try
            {
                string exeDir = Path.GetDirectoryName(typeof(UiFont).Assembly.Location);
                string dir = exeDir == null ? null : Path.Combine(exeDir, "fonts");
                if (dir != null && Directory.Exists(dir))
                {
                    privateFonts = new System.Drawing.Text.PrivateFontCollection();
                    foreach (var file in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".ttf" && ext != ".ttc" && ext != ".otf") continue;
                        try { privateFonts.AddFontFile(file); } catch { }
                    }
                    foreach (var fam in privateFonts.Families)
                        if (!names.Contains(fam.Name)) names.Add(fam.Name);
                }
            }
            catch { }

            // 2) 系统里装了的（含当前用户安装的）
            try
            {
                var installed = new System.Drawing.Text.InstalledFontCollection();
                foreach (var fam in installed.Families)
                    if (!names.Contains(fam.Name)) names.Add(fam.Name);
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(textOverride))
            {
                // 用户在设置里明确选了一个字族：系统中找得到就用它，找不到退回默认
                string picked = null;
                foreach (var name in names)
                    if (string.Equals(name, textOverride, StringComparison.OrdinalIgnoreCase)) { picked = name; break; }
                if (picked != null)
                {
                    regular = picked;
                    bold = null;      // 单字族用 FontStyle.Bold 合成，不再找独立粗体族
                    return;
                }
            }

            regular = First(names, Prefer);
            if (regular == null) regular = "Microsoft YaHei UI";
            bold = First(names, PreferBold);
        }
    }
}
