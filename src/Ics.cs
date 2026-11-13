using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WorkCountdown
{
    /// <summary>
    /// .ics（iCalendar）读取：只覆盖 Outlook / Exchange「发布日历」里实际会出现的那些字段。
    /// 认得：单次事件、DAILY / WEEKLY / MONTHLY / YEARLY 重复（INTERVAL / COUNT / UNTIL /
    /// BYDAY / BYMONTHDAY）、EXDATE、全天事件（默认跳过）、LOCATION / DESCRIPTION。
    /// 不认：VTIMEZONE 的夏令时换算（带 TZID 的一律按本机时区算）、RECURRENCE-ID 改单次、VALARM。
    /// 纯函数，出问题只返回空列表 + 错误串，不抛异常。
    /// </summary>
    public static class Ics
    {
        // 一次解析最多展开这么多次，防止「每天重复、起始 2010 年」的日程把 CPU 吃满
        private const int MaxOccurrences = 800;

        /// <summary>抓一份 .ics 文本：source = url | file。失败返回 null，错误写在 error 里。</summary>
        public static string ReadText(string source, string url, string path, int timeoutMs, out string error)
        {
            error = "";
            try
            {
                if (source == "file")
                {
                    if (string.IsNullOrWhiteSpace(path)) { error = "还没选 .ics 文件"; return null; }
                    if (!File.Exists(path)) { error = "找不到文件：" + path; return null; }
                    return ReadAllTextSmart(path);
                }
                if (string.IsNullOrWhiteSpace(url)) { error = "还没填订阅地址"; return null; }
                return Download(url, Math.Max(3000, timeoutMs));
            }
            catch (WebException we)
            {
                error = "下载失败：" + Describe(we);
                return null;
            }
            catch (Exception ex)
            {
                error = "读取失败：" + ex.Message;
                return null;
            }
        }

        private static string Describe(WebException we)
        {
            var resp = we.Response as HttpWebResponse;
            if (resp != null) return "HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription;
            return we.Status.ToString();
        }

        private static string ReadAllTextSmart(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            return Encoding.UTF8.GetString(bytes);
        }

        private static string Download(string url, int timeoutMs)
        {
            // .NET 4.0 默认可能不带 TLS 1.2，Outlook 的日历地址只收 TLS 1.2+
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.UserAgent = "WorkCountdown/2.13 (+ics)";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AllowAutoRedirect = true;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            {
                if (s == null) return "";
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    byte[] bytes = ms.ToArray();
                    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                    return Encoding.UTF8.GetString(bytes);
                }
            }
        }

        // ---------------- 解析 ----------------

        /// <summary>把 .ics 文本解析成 [from, to] 之间会发生的日程。</summary>
        public static List<ReminderEvent> Parse(string text, DateTime from, DateTime to)
        {
            var list = new List<ReminderEvent>();
            if (string.IsNullOrEmpty(text) || text.IndexOf("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase) < 0)
                return list;

            from = from.Date;
            to = to.Date.AddDays(1);

            foreach (var vevent in SplitEvents(text))
                Expand(vevent, from, to, list);
            list.Sort(delegate (ReminderEvent a, ReminderEvent b)
            {
                return Reminders.ParseStart(a).CompareTo(Reminders.ParseStart(b));
            });
            return list;
        }

        /// <summary>把文本按 BEGIN:VEVENT … END:VEVENT 切一段一段（已经展开折行）。</summary>
        private static List<List<string>> SplitEvents(string text)
        {
            string unfolded = Unfold(text);
            var events = new List<List<string>>();
            List<string> cur = null;
            foreach (string raw in unfolded.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                string upper = line.Trim().ToUpperInvariant();
                if (upper == "BEGIN:VEVENT")
                {
                    cur = new List<string>();
                    continue;
                }
                if (upper == "END:VEVENT")
                {
                    if (cur != null) events.Add(cur);
                    cur = null;
                    continue;
                }
                if (cur != null) cur.Add(line);
            }
            return events;
        }

        /// <summary>去掉折行：ICS 里长行会拆成「\n + 一个空格」，拼回去。</summary>
        private static string Unfold(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return Regex.Replace(text, "\n[ \t]", "");
        }

        private static void Expand(List<string> lines, DateTime from, DateTime to, List<ReminderEvent> outList)
        {
            string uid = "", summary = "", location = "", desc = "", status = "";
            DateTime start = DateTime.MinValue, end = DateTime.MinValue;
            bool allDay = false, cancelled = false, overrideOccurrence = false;
            var exdates = new HashSet<long>();
            string rrule = null;
            TimeSpan duration = TimeSpan.Zero;
            bool hasEnd = false;

            foreach (string line in lines)
            {
                string name, value;
                string[] pars;
                if (!SplitProperty(line, out name, out pars, out value)) continue;
                switch (name)
                {
                    case "UID": uid = value.Trim(); break;
                    case "SUMMARY": summary = Unescape(value); break;
                    case "LOCATION": location = Unescape(value); break;
                    case "DESCRIPTION": desc = Unescape(value); break;
                    case "STATUS": status = value.Trim().ToUpperInvariant(); break;
                    case "RRULE": rrule = value.Trim(); break;
                    case "RECURRENCE-ID": overrideOccurrence = true; break;
                    case "EXDATE":
                        foreach (string one in value.Split(','))
                        {
                            DateTime dt; bool ad;
                            if (TryParseDate(one.Trim(), pars, out dt, out ad)) exdates.Add(dt.Ticks);
                        }
                        break;
                    case "DTSTART":
                        if (!TryParseDate(value.Trim(), pars, out start, out allDay)) start = DateTime.MinValue;
                        break;
                    case "DTEND":
                        if (TryParseDate(value.Trim(), pars, out end, out allDay)) hasEnd = true;
                        break;
                    case "DURATION":
                        duration = ParseDuration(value.Trim());
                        break;
                }
            }

            if (start == DateTime.MinValue) return;
            if (status == "CANCELLED") cancelled = true;
            if (cancelled || overrideOccurrence) return;     // 取消的、被改单次的，一律不提醒
            if (allDay) return;                              // 全天事件（节假日之类）不提醒

            if (!hasEnd) end = start + (duration > TimeSpan.Zero ? duration : TimeSpan.FromMinutes(30));
            if (end <= start) end = start.AddMinutes(30);

            string title = string.IsNullOrWhiteSpace(summary) ? "Outlook 日程" : Collapse(summary);
            string detail = string.IsNullOrWhiteSpace(desc) ? "" : Collapse(desc);

            var occurrences = new List<DateTime>();
            if (string.IsNullOrEmpty(rrule))
            {
                if (start >= from && start < to && !exdates.Contains(start.Ticks)) occurrences.Add(start);
            }
            else
            {
                ExpandRecurrence(start, ParseRule(rrule), from, to, exdates, occurrences);
            }

            foreach (DateTime occ in occurrences)
            {
                if (occ < from || occ >= to) continue;
                DateTime occEnd = occ + (end - start);
                var e = new ReminderEvent();
                e.id = "ol:" + (string.IsNullOrEmpty(uid) ? "x" : uid) + "@" + occ.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
                e.title = title;
                e.start = Reminders.FormatStart(occ);
                e.end = Reminders.FormatStart(occEnd);
                e.place = location == null ? "" : location.Trim();
                e.detail = detail;
                e.source = "outlook";
                e.leadMinutes = 0;      // 由同步那边按设置补
                outList.Add(e);
            }
        }

        private static bool SplitProperty(string line, out string name, out string[] pars, out string value)
        {
            name = null; pars = new string[0]; value = null;
            int colon = line.IndexOf(':');
            if (colon <= 0) return false;
            string head = line.Substring(0, colon);
            value = line.Substring(colon + 1);
            int semi = head.IndexOf(';');
            if (semi > 0)
            {
                name = head.Substring(0, semi).Trim().ToUpperInvariant();
                pars = head.Substring(semi + 1).Split(';');
            }
            else name = head.Trim().ToUpperInvariant();
            return name.Length > 0;
        }

        private static string ParamValue(string[] pars, string key)
        {
            if (pars == null) return null;
            foreach (string p in pars)
            {
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(p.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                    return p.Substring(eq + 1).Trim('"');
            }
            return null;
        }

        private static bool TryParseDate(string value, string[] pars, out DateTime dt, out bool allDay)
        {
            dt = DateTime.MinValue;
            allDay = false;
            if (string.IsNullOrEmpty(value)) return false;

            string v = value.Trim();
            string valueType = ParamValue(pars, "VALUE");
            if (valueType != null && valueType.Equals("DATE", StringComparison.OrdinalIgnoreCase)) allDay = true;

            bool utc = v.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
            if (utc) v = v.Substring(0, v.Length - 1);

            string[] formats = new string[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd" };
            if (!DateTime.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return false;

            if (v.Length == 8) allDay = true;
            if (utc) dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
            else dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
            return true;
        }

        private static TimeSpan ParseDuration(string value)
        {
            // 只认 PnDTnHnMnS 里最常见的几种
            var m = Regex.Match(value, @"^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?)?$", RegexOptions.IgnoreCase);
            if (!m.Success) return TimeSpan.Zero;
            int d = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
            int h = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            int mi = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
            return new TimeSpan(d, h, mi, 0);
        }

        /// <summary>RRULE 拆成字段表：FREQ / INTERVAL / COUNT / UNTIL / BYDAY / BYMONTHDAY。</summary>
        private class RRule
        {
            public string freq = "";
            public int interval = 1;
            public int count = 0;
            public DateTime until = DateTime.MaxValue;
            public int[] byDay = null;      // 0=周日 … 6=周六
            public int[] byMonthDay = null;
        }

        private static RRule ParseRule(string rrule)
        {
            var r = new RRule();
            foreach (string part in rrule.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq).Trim().ToUpperInvariant();
                string val = part.Substring(eq + 1).Trim();
                if (key == "FREQ") r.freq = val.ToUpperInvariant();
                else if (key == "INTERVAL")
                {
                    int n;
                    if (int.TryParse(val, out n) && n > 0) r.interval = n;
                }
                else if (key == "COUNT")
                {
                    int n;
                    if (int.TryParse(val, out n) && n > 0) r.count = n;
                }
                else if (key == "UNTIL")
                {
                    DateTime u; bool ad;
                    if (TryParseDate(val, null, out u, out ad)) r.until = u;
                }
                else if (key == "BYDAY")
                {
                    var days = new List<int>();
                    foreach (string d in val.Split(','))
                    {
                        int idx = DayIndex(d.Trim());
                        if (idx >= 0) days.Add(idx);
                    }
                    if (days.Count > 0) r.byDay = days.ToArray();
                }
                else if (key == "BYMONTHDAY")
                {
                    var md = new List<int>();
                    foreach (string d in val.Split(','))
                    {
                        int n;
                        if (int.TryParse(d.Trim(), out n) && n >= 1 && n <= 31) md.Add(n);
                    }
                    if (md.Count > 0) r.byMonthDay = md.ToArray();
                }
            }
            return r;
        }

        private static int DayIndex(string token)
        {
            // 去掉 "2TU" 这种序号，只认星期
            string t = token.Length >= 2 ? token.Substring(token.Length - 2) : token;
            switch (t.ToUpperInvariant())
            {
                case "SU": return 0;
                case "MO": return 1;
                case "TU": return 2;
                case "WE": return 3;
                case "TH": return 4;
                case "FR": return 5;
                case "SA": return 6;
            }
            return -1;
        }

        private static void ExpandRecurrence(DateTime start, RRule r, DateTime from, DateTime to,
            HashSet<long> exdates, List<DateTime> outList)
        {
            int emitted = 0;      // 已经「数过」的次数（COUNT 用）
            int guard = 0;
            string freq = r.freq;

            if (freq == "DAILY")
            {
                DateTime cur = start;
                if (cur < from)
                {
                    int skip = (int)((from.Date - cur.Date).TotalDays / r.interval);
                    if (skip > 0) { cur = cur.AddDays(skip * r.interval); emitted += skip; }
                }
                while (guard++ < MaxOccurrences)
                {
                    if (r.count > 0 && emitted >= r.count) break;
                    if (cur > r.until || cur >= to) break;
                    if (cur >= from && !exdates.Contains(cur.Ticks)) outList.Add(cur);
                    cur = cur.AddDays(r.interval);
                    emitted++;
                }
            }
            else if (freq == "WEEKLY")
            {
                int[] days = r.byDay ?? new int[] { (int)start.DayOfWeek };
                // 从 start 所在那周的周一算起
                DateTime week = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7));
                int spanDays = 7 * r.interval;
                if (week.AddDays(7) < from)     // 起始很早的周会：先整周整周地跳过去
                {
                    int weeks = (int)((from.Date - week).TotalDays / spanDays);
                    if (weeks > 0) { week = week.AddDays(weeks * spanDays); emitted += weeks * days.Length; }
                }
                while (guard++ < MaxOccurrences * 2)
                {
                    foreach (int d in days)
                    {
                        DateTime cur = week.AddDays(((d + 6) % 7)).Add(start.TimeOfDay);
                        if (cur < start) continue;
                        if (r.count > 0 && emitted >= r.count) return;
                        if (cur > r.until || cur >= to) continue;
                        emitted++;
                        if (cur >= from && !exdates.Contains(cur.Ticks)) outList.Add(cur);
                    }
                    if (r.count > 0 && emitted >= r.count) break;
                    week = week.AddDays(spanDays);
                    if (week >= to) break;
                }
            }
            else if (freq == "MONTHLY")
            {
                int[] days = r.byMonthDay ?? new int[] { start.Day };
                DateTime month = new DateTime(start.Year, start.Month, 1);
                while (guard++ < MaxOccurrences * 2)
                {
                    foreach (int d in days)
                    {
                        if (d > DateTime.DaysInMonth(month.Year, month.Month)) continue;
                        DateTime cur = new DateTime(month.Year, month.Month, d) + start.TimeOfDay;
                        if (cur < start) continue;
                        if (r.count > 0 && emitted >= r.count) return;
                        if (cur > r.until || cur >= to) continue;
                        emitted++;
                        if (cur >= from && !exdates.Contains(cur.Ticks)) outList.Add(cur);
                    }
                    if (r.count > 0 && emitted >= r.count) break;
                    month = month.AddMonths(r.interval);
                    if (month >= to) break;
                }
            }
            else if (freq == "YEARLY")
            {
                DateTime cur = start;
                while (guard++ < MaxOccurrences)
                {
                    if (r.count > 0 && emitted >= r.count) break;
                    if (cur > r.until || cur >= to) break;
                    if (cur >= from && !exdates.Contains(cur.Ticks)) outList.Add(cur);
                    cur = cur.AddYears(r.interval);
                    emitted++;
                }
            }
            else
            {
                if (start >= from && start < to) outList.Add(start);
            }
        }

        // ---------------- 文本处理 ----------------

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 'n' || n == 'N') sb.Append('\n');
                    else if (n == 't' || n == 'T') sb.Append('\t');
                    else sb.Append(n);
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Collapse(string s)
        {
            string one = Regex.Replace(s.Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
            if (one.Length > 160) one = one.Substring(0, 160) + "…";
            return one;
        }
    }
}
