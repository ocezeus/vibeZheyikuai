using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WorkCountdown
{
    /// <summary>
    /// Outlook / Microsoft 365 的 Graph 接入：设备码登录 + 刷新令牌 + 读 calendarView。
    /// 需要一个 Azure 应用注册的 client ID（公共客户端，开 Allow public client flows）。
    /// 这里全都是「拿字符串换字符串」的纯函数 + 三个 HTTP 调用，异常一律折成 error 串。
    /// </summary>
    public static class OutlookGraph
    {
        public const string DefaultTenant = "common";
        public const string Scope = "https://graph.microsoft.com/Calendars.Read offline_access openid profile";
        public const string GraphBase = "https://graph.microsoft.com/v1.0";

        // Azure 门户里的应用注册 → 身份验证 → 允许公共客户端流 = 是；
        // API 权限加 Microsoft Graph → Delegated → Calendars.Read。
        public const string SetupHint = "先在 Azure 门户注册一个应用（公共客户端），API 权限加 Microsoft Graph 的 Calendars.Read，然后把它的 client ID 填到下面。";

        public static string Authority(string tenant)
        {
            string t = string.IsNullOrWhiteSpace(tenant) ? DefaultTenant : tenant.Trim();
            return "https://login.microsoftonline.com/" + t + "/oauth2/v2.0/";
        }

        public class DeviceCodeInfo
        {
            public string deviceCode = "";
            public string userCode = "";
            public string verifyUrl = "";
            public string message = "";
            public int interval = 5;
            public int expiresIn = 900;
        }

        public class TokenInfo
        {
            public string accessToken = "";
            public string refreshToken = "";
            public int expiresIn = 3600;
        }

        // ---------------- 纯解析（单元测试用） ----------------

        public static bool ParseDeviceCode(string json, out DeviceCodeInfo info, out string error)
        {
            info = new DeviceCodeInfo();
            error = ErrorText(json);
            if (error.Length > 0) return false;
            var map = Map(json);
            if (map == null) { error = "响应不是 JSON"; return false; }
            info.deviceCode = Str(map, "device_code");
            info.userCode = Str(map, "user_code");
            info.verifyUrl = Str(map, "verification_uri");
            if (info.verifyUrl.Length == 0) info.verifyUrl = Str(map, "verification_url");
            info.message = Str(map, "message");
            info.interval = Int(map, "interval", 5);
            info.expiresIn = Int(map, "expires_in", 900);
            if (info.deviceCode.Length == 0 || info.userCode.Length == 0)
            {
                error = "设备码响应里没有 device_code / user_code";
                return false;
            }
            return true;
        }

        public static bool ParseToken(string json, out TokenInfo token, out string error)
        {
            token = new TokenInfo();
            error = ErrorText(json);
            if (error.Length > 0) return false;
            var map = Map(json);
            if (map == null) { error = "响应不是 JSON"; return false; }
            token.accessToken = Str(map, "access_token");
            token.refreshToken = Str(map, "refresh_token");
            token.expiresIn = Int(map, "expires_in", 3600);
            if (token.accessToken.Length == 0) { error = "没有 access_token"; return false; }
            return true;
        }

        public static string BuildCalendarUrl(DateTime from, DateTime to)
        {
            return GraphBase + "/me/calendarView" +
                "?startDateTime=" + from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) +
                "&endDateTime=" + to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) +
                "&$select=subject,start,end,location,isAllDay,isCancelled,showAs,onlineMeetingUrl" +
                "&$orderby=start/dateTime&$top=100";
        }

        /// <summary>把 calendarView 的 JSON 解成待提醒事件；全天 / 取消 / 空闲的不算。</summary>
        public static List<ReminderEvent> ParseCalendar(string json, out string nextLink)
        {
            nextLink = "";
            var list = new List<ReminderEvent>();
            var map = Map(json);
            if (map == null) return list;
            nextLink = Str(map, "nextLink");
            if (nextLink.Length == 0) nextLink = Str(map, "@odata.nextLink");
            object raw;
            if (!map.TryGetValue("value", out raw)) return list;
            var arr = raw as System.Collections.IEnumerable;
            if (arr == null) return list;
            foreach (object one in arr)
            {
                var ev = one as Dictionary<string, object>;
                if (ev == null) continue;
                if (Bool(ev, "isAllDay")) continue;
                if (Bool(ev, "isCancelled")) continue;
                string showAs = Str(ev, "showAs");
                DateTime start = GraphTime(ev, "start");
                DateTime end = GraphTime(ev, "end");
                if (start == DateTime.MinValue) continue;
                if (end <= start) end = start.AddMinutes(30);

                var e = new ReminderEvent();
                string id = Str(ev, "id");
                if (id.Length == 0) id = Str(ev, "iCalUId");
                if (id.Length == 0) id = start.Ticks.ToString(CultureInfo.InvariantCulture);
                e.id = "ol:" + id + "@" + start.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
                string subject = Str(ev, "subject");
                e.title = string.IsNullOrWhiteSpace(subject) ? "Outlook 日程" : subject.Trim();
                e.start = Reminders.FormatStart(start);
                e.end = Reminders.FormatStart(end);
                object loc;
                if (ev.TryGetValue("location", out loc))
                {
                    var lm = loc as Dictionary<string, object>;
                    e.place = lm == null ? "" : Str(lm, "displayName");
                }
                e.detail = showAs == "free" ? "" : "";
                e.source = "outlook";
                e.leadMinutes = 0;
                list.Add(e);
            }
            return list;
        }

        private static DateTime GraphTime(Dictionary<string, object> ev, string key)
        {
            object raw;
            if (!ev.TryGetValue(key, out raw)) return DateTime.MinValue;
            var map = raw as Dictionary<string, object>;
            if (map == null) return DateTime.MinValue;
            string text = Str(map, "dateTime");
            string zone = Str(map, "timeZone");
            DateTime t;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                return DateTime.MinValue;
            if (zone.IndexOf("UTC", StringComparison.OrdinalIgnoreCase) >= 0)
                t = DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();
            else
                t = DateTime.SpecifyKind(t, DateTimeKind.Local);
            return t;
        }

        // ---------------- HTTP ----------------

        public static bool StartDeviceCode(string clientId, string tenant, int timeoutMs,
            out DeviceCodeInfo info, out string error)
        {
            info = null;
            error = "";
            if (string.IsNullOrWhiteSpace(clientId)) { error = "还没有 client ID"; return false; }
            string json = Post(Authority(tenant) + "devicecode",
                "client_id=" + Uri.EscapeDataString(clientId.Trim()) + "&scope=" + Uri.EscapeDataString(Scope),
                timeoutMs, out error);
            if (json == null) return false;
            return ParseDeviceCode(json, out info, out error);
        }

        public static bool PollToken(string clientId, string tenant, string deviceCode, int intervalSec, int timeoutMs,
            out TokenInfo token, out string error)
        {
            token = null;
            error = "";
            DateTime deadline = DateTime.Now.AddSeconds(Math.Max(30, timeoutMs / 1000));
            int wait = Math.Max(2, intervalSec);
            while (DateTime.Now < deadline)
            {
                System.Threading.Thread.Sleep(wait * 1000);
                string json = Post(Authority(tenant) + "token",
                    "grant_type=urn:ietf:params:oauth:grant-type:device_code" +
                    "&client_id=" + Uri.EscapeDataString(clientId.Trim()) +
                    "&device_code=" + Uri.EscapeDataString(deviceCode),
                    timeoutMs, out error);
                if (json == null) return false;
                string code = ErrorCode(json);
                if (code == "authorization_pending") continue;
                if (code == "slow_down") { wait += 5; continue; }
                if (code.Length > 0) { error = ErrorText(json); return false; }
                return ParseToken(json, out token, out error);
            }
            error = "登录超时，重新点一次「登录 Outlook」。";
            return false;
        }

        public static bool Refresh(string clientId, string tenant, string refreshToken, int timeoutMs,
            out TokenInfo token, out string error)
        {
            token = null;
            error = "";
            if (string.IsNullOrWhiteSpace(refreshToken)) { error = "还没有登录"; return false; }
            string json = Post(Authority(tenant) + "token",
                "grant_type=refresh_token&client_id=" + Uri.EscapeDataString(clientId.Trim()) +
                "&refresh_token=" + Uri.EscapeDataString(refreshToken) +
                "&scope=" + Uri.EscapeDataString(Scope),
                timeoutMs, out error);
            if (json == null) return false;
            if (ErrorCode(json).Length > 0) { error = ErrorText(json); return false; }
            return ParseToken(json, out token, out error);
        }

        public static bool FetchCalendar(string accessToken, DateTime from, DateTime to, int timeoutMs,
            out List<ReminderEvent> events, out string error)
        {
            events = new List<ReminderEvent>();
            error = "";
            string url = BuildCalendarUrl(from, to);
            int pages = 0;
            while (!string.IsNullOrEmpty(url) && pages++ < 10)
            {
                string json = Get(url, accessToken, timeoutMs, out error);
                if (json == null) return false;
                string next;
                var page = ParseCalendar(json, out next);
                events.AddRange(page);
                url = next;
            }
            events.Sort(delegate(ReminderEvent a, ReminderEvent b)
            {
                return Reminders.ParseStart(a).CompareTo(Reminders.ParseStart(b));
            });
            return true;
        }

        public static bool FetchMe(string accessToken, int timeoutMs, out string name, out string mail, out string error)
        {
            name = ""; mail = ""; error = "";
            string json = Get(GraphBase + "/me?$select=displayName,mail,userPrincipalName", accessToken, timeoutMs, out error);
            if (json == null) return false;
            var map = Map(json);
            if (map == null) { error = "响应不是 JSON"; return false; }
            name = Str(map, "displayName");
            mail = Str(map, "mail");
            if (mail.Length == 0) mail = Str(map, "userPrincipalName");
            return true;
        }

        private static string Post(string url, string body, int timeoutMs, out string error)
        {
            error = "";
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.UserAgent = "WorkCountdown/2.15";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentLength = data.Length;
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                    return ReadBody(resp);
            }
            catch (WebException we)
            {
                // 4xx 的正文里也有 error / error_description，要读出来
                var resp = we.Response as HttpWebResponse;
                if (resp != null)
                {
                    try
                    {
                        string text = ReadBody(resp);
                        if (!string.IsNullOrEmpty(text)) return text;
                    }
                    catch { }
                    error = "HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription;
                    return null;
                }
                error = we.Status.ToString();
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static string Get(string url, string accessToken, int timeoutMs, out string error)
        {
            error = "";
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = "WorkCountdown/2.15";
                req.Headers.Add("Authorization", "Bearer " + accessToken);
                req.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                using (var resp = (HttpWebResponse)req.GetResponse())
                    return ReadBody(resp);
            }
            catch (WebException we)
            {
                var resp = we.Response as HttpWebResponse;
                if (resp != null)
                {
                    try { error = "HTTP " + (int)resp.StatusCode + " " + ErrorText(ReadBody(resp)); }
                    catch { error = "HTTP " + (int)resp.StatusCode; }
                    return null;
                }
                error = we.Status.ToString();
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static string ReadBody(HttpWebResponse resp)
        {
            using (var s = resp.GetResponseStream())
            {
                if (s == null) return "";
                using (var reader = new StreamReader(s, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        // ---------------- JSON 小工具 ----------------

        private static Dictionary<string, object> Map(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            }
            catch { return null; }
        }

        private static string Str(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static int Int(Dictionary<string, object> map, string key, int fallback)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return fallback;
            int n;
            return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out n) ? n : fallback;
        }

        private static bool Bool(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            return string.Equals(Convert.ToString(v, CultureInfo.InvariantCulture), "true", StringComparison.OrdinalIgnoreCase);
        }

        public static string ErrorCode(string json)
        {
            var map = Map(json);
            return map == null ? "" : Str(map, "error");
        }

        public static string ErrorText(string json)
        {
            var map = Map(json);
            if (map == null) return "";
            string code = Str(map, "error");
            if (code.Length == 0) return "";
            string desc = Str(map, "error_description");
            if (desc.Length == 0) return code;
            int cut = desc.IndexOf("\r\n", StringComparison.Ordinal);
            if (cut > 0) desc = desc.Substring(0, cut);
            return code + " · " + desc;
        }

        // ---------------- 令牌存取（DPAPI 加密，只在本机当前用户下可解） ----------------

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(enc);
            }
            catch { return ""; }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith("dpapi:", StringComparison.Ordinal)) return "";
            try
            {
                byte[] enc = Convert.FromBase64String(stored.Substring(6));
                byte[] data = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }
    }
}
