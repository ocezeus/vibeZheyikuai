using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WorkCountdown
{
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

        /// <summary>飞书日历里的会算会议，手动加的是日程。</summary>
        public static bool IsMeeting(ReminderEvent e)
        {
            return e != null && e.source == "lark";
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

        /// <summary>手动日程 + 飞书日程，按开始时间排序。</summary>
        public static List<ReminderEvent> Merge(Settings s, List<ReminderEvent> larkEvents)
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
        public string reminderSpreadStyle { get; set; } // circle | bar：提醒扩散样式
        public string lunchReminderStyle { get; set; }   // 午休提醒配色（ReminderStyles 的 id）
        public string eveningReminderStyle { get; set; } // 晚休提醒配色
        public string offReminderStyle { get; set; }     // 下班提醒配色

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
            lunchReminderStyle = "lan-yuming";
            eveningReminderStyle = "zi-jiegeng";
            offReminderStyle = "forest";
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
                            loaded.Normalize();
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

    /// <summary>用户自建的主题：一个主色，其余由主色推导（和内置主题同一套算法）。</summary>
    public class CustomTheme
    {
        public string id { get; set; }
        public string name { get; set; }
        public string accent { get; set; }      // #RRGGBB
    }

    public static class SettingsExtensions
    {
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
            if (s.ui.reminderSpreadStyle != "bar") s.ui.reminderSpreadStyle = "circle";
            s.ui.lunchReminderStyle = ReminderStyles.Normalize(s.ui.lunchReminderStyle, "lan-yuming");
            s.ui.eveningReminderStyle = ReminderStyles.Normalize(s.ui.eveningReminderStyle, "zi-jiegeng");
            s.ui.offReminderStyle = ReminderStyles.Normalize(s.ui.offReminderStyle, "forest");
            s.ui.x = s.ui.x < -30000 ? -1 : s.ui.x;
            s.NormalizeSchedules();
            s.NormalizeCustomThemes();
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
                if (string.IsNullOrWhiteSpace(t.accent) || t.accent.Length != 7 || t.accent[0] != '#') t.accent = "#4C8DFF";
            }
            Themes.SetCustom(s.customThemes);
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
                custom.Add(FromAccent(t.id, t.name, t.accent));
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
            // 这条固定日程今天不该出现（单/双周、截止日期、停用）就不显示这一行
            if (lunchSched != null && (!lunchSched.enabled || !lunchSched.OccursOn(now)))
            {
                st.lunch = LunchState.None;
                st.lunchLabel = "";
                st.lunchValue = "";
                return;
            }
            int a, b;
            if (!SettingsExtensions.TryParseTime(s.lunchStart, out a) || !SettingsExtensions.TryParseTime(s.lunchEnd, out b) || b <= a)
            {
                st.lunch = LunchState.None;
                st.lunchLabel = "";
                st.lunchValue = "";
                return;
            }

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

            st.lunch = LunchState.After;
            st.lunchLabel = "";
            st.lunchValue = "";
            st.lunchCounting = false;
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

        public static string Family
        {
            get
            {
                if (regular == null) Resolve();
                return regular;
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

            regular = First(names, Prefer);
            if (regular == null) regular = "Microsoft YaHei UI";
            bold = First(names, PreferBold);
        }
    }
}
