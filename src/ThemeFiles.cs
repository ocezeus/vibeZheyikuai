using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WorkCountdown
{
    /// <summary>
    /// 用户主题文件（themes\*.theme.json）：v5.0 起用户主题的唯一真源。
    ///
    /// 目录：SettingsStore.Dir\themes（便携模式即 exe\data\themes），由一个主题一个文件。
    /// 文件：schema=1，UTF-8（带不带 BOM 都能读），Pretty 排版，颜色支持 #RRGGBB / #AARRGGBB。
    /// 失败语义：所有异常都收敛成 error 文本；坏文件 LoadAll 跳过、不影响内置 39 套和合法用户主题。
    /// 线程模型：静态目录，进程内单线程使用（设置窗口 / 启动流程），不做并发加锁。
    /// </summary>
    public static class ThemeFiles
    {
        /// <summary>当前文件 schema；不认识的 schema 直接当坏文件跳过。</summary>
        public const int Schema = 1;
        /// <summary>文件里的 app 标记，避免把别人的 .theme.json 导进来。</summary>
        public const string AppTag = "WorkCountdown";
        /// <summary>主题文件扩展名（一个主题一个文件）。</summary>
        public const string Extension = ".theme.json";

        private const string SubDir = "themes";
        private const int MaxSlugLength = 40;
        /// <summary>迁移出来的主题统一带这句说明，用来判断「上次是否已经迁移过」。</summary>
        private const string LegacyMigrationDescription = "从 v4 旧配置迁移";

        private static string dir = "";

        /// <summary>用户主题目录 = SettingsStore.Dir\themes（便携模式自动变成 exe\data\themes）。</summary>
        public static string Dir { get { return dir; } }

        /// <summary>由配置目录推出主题目录；SettingsStore 构造时调用。</summary>
        public static void Configure(string settingsDir)
        {
            dir = string.IsNullOrEmpty(settingsDir) ? "" : Path.Combine(settingsDir, SubDir);
        }

        /// <summary>确保主题目录存在；没 Configure 过会抛 InvalidOperationException（调用方自己收敛）。</summary>
        public static void EnsureDir()
        {
            if (dir.Length == 0) throw new InvalidOperationException("主题目录还没配置（先调用 ThemeFiles.Configure）。");
            Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// 扫描主题目录，返回全部合法用户主题（按文件名排序，同 id 只认第一个）。
        /// 目录不存在 / 单个文件坏 / 读取失败都跳过，不抛异常。
        /// </summary>
        public static List<ThemeDef> LoadAll()
        {
            var list = new List<ThemeDef>();
            if (dir.Length == 0) return list;
            string[] files;
            try
            {
                if (!Directory.Exists(dir)) return list;
                files = Directory.GetFiles(dir, "*" + Extension);
            }
            catch { return list; }
            Array.Sort(files, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                ThemeDef theme;
                string error;
                if (!TryRead(file, out theme, out error)) continue;
                if (!seen.Add(theme.id)) continue;   // 同 id 只认文件名排在前面的那份
                list.Add(theme);
            }
            return list;
        }

        /// <summary>读一个主题文件；格式 / 字段 / 颜色不对都返回 false 和可读原因。</summary>
        public static bool TryRead(string path, out ThemeDef theme, out string error)
        {
            theme = null;
            error = "";
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "主题文件路径为空。";
                return false;
            }
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }   // 带 BOM 自动剥掉，无 BOM 也按 UTF-8 读
            catch (Exception ex)
            {
                error = "读取主题文件失败：" + ex.Message;
                return false;
            }
            return TryParse(text, path, out theme, out error);
        }

        /// <summary>
        /// 保存主题文件（path 为空时存到主题目录里、按 id 自动起名）。
        /// 不修改传入的 ThemeDef 元数据；成功即完整落盘（临时文件 + 替换）。
        /// 与 TryExport 同义，保留两个名字给「保存」和「导出给别人」两种调用点。
        /// </summary>
        public static bool TrySave(ThemeDef theme, string path, out string error)
        {
            return WriteFile(theme, path, out error);
        }

        /// <summary>把主题导出到任意路径（规则同 TrySave；不修改传入对象，内置主题也能直接导出）。</summary>
        public static bool TryExport(ThemeDef theme, string path, out string error)
        {
            return WriteFile(theme, path, out error);
        }

        /// <summary>
        /// 导入一个主题文件：先校验，再复制进主题目录（重新排版成标准文件）。
        /// id / 文件名冲突自动加 -2 / -3… 后缀；成功返回写进本机的新 ThemeDef（含新 id 和 filePath）。
        /// </summary>
        public static bool TryImport(string srcPath, out ThemeDef theme, out string error)
        {
            theme = null;
            error = "";
            ThemeDef read;
            if (!TryRead(srcPath, out read, out error)) return false;
            if (dir.Length == 0)
            {
                error = "主题目录还没配置。";
                return false;
            }
            try { EnsureDir(); }
            catch (Exception ex)
            {
                error = "创建主题目录失败：" + ex.Message;
                return false;
            }

            var copy = read.Clone();
            copy.id = UniqueId(read.id);
            if (string.IsNullOrWhiteSpace(copy.created)) copy.created = NowText();
            string target = Path.Combine(dir, FileNameFor(copy.id));
            if (!WriteFile(copy, target, out error)) return false;
            copy.filePath = target;
            copy.isUser = true;
            theme = copy;
            return true;
        }

        /// <summary>删除一个用户主题文件；只允许删主题目录里的文件（内置主题没有文件，返回 false）。</summary>
        public static bool TryDelete(ThemeDef theme, out string error)
        {
            error = "";
            if (theme == null || string.IsNullOrWhiteSpace(theme.filePath))
            {
                error = "只能删除用户主题文件。";
                return false;
            }
            if (dir.Length == 0)
            {
                error = "主题目录还没配置。";
                return false;
            }
            string target, root;
            try
            {
                target = Path.GetFullPath(theme.filePath);
                root = Path.GetFullPath(dir);
            }
            catch (Exception ex)
            {
                error = "主题文件路径不对：" + ex.Message;
                return false;
            }
            string rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!target.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                error = "只能删除主题目录里的主题文件。";
                return false;
            }
            try
            {
                if (File.Exists(target)) File.Delete(target);
                return true;
            }
            catch (Exception ex)
            {
                error = "删除主题文件失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>按名字起一个新 id（slug，挂在 my- 下），已存在时自动加 -2 / -3 后缀。</summary>
        public static string NewId(string name)
        {
            string slug = Slug(name);
            if (slug.Length == 0) slug = "theme";
            string baseId = slug.StartsWith("my-", StringComparison.OrdinalIgnoreCase) ? slug : "my-" + slug;
            return UniqueId(baseId);
        }

        /// <summary>以 seed 的配色起一份新主题（不落盘）：换名字、新 id、重置文件元数据。</summary>
        public static ThemeDef Duplicate(string name, ThemeDef seed)
        {
            ThemeDef source = seed != null ? seed : Themes.Get(Themes.FallbackId);
            string display = string.IsNullOrWhiteSpace(name)
                ? (source.name + " 副本")
                : name.Trim();
            var t = new ThemeDef();
            t.id = NewId(display);
            t.name = display;
            t.author = source.author == null ? "" : source.author;
            t.description = source.description == null ? "" : source.description;
            t.created = NowText();
            t.filePath = null;
            t.isUser = false;
            t.accent = source.accent;
            t.success = source.success;
            t.bgTop = source.bgTop;
            t.bgBottom = source.bgBottom;
            t.track = source.track;
            t.textMain = source.textMain;
            t.textMuted = source.textMuted;
            t.textDim = source.textDim;
            t.edge = source.edge;
            return t;
        }

        /// <summary>把主题 id 变成安全的文件名（非法字符换成 '-'，过长截断），不含目录。</summary>
        public static string FileNameFor(string id)
        {
            string slug = Slug(id);
            if (slug.Length == 0) slug = "theme";
            if (slug.Length > MaxSlugLength) slug = slug.Substring(0, MaxSlugLength).TrimEnd('-');
            return slug + Extension;
        }

        /// <summary>
        /// 旧配置 settings.customThemes → themes\*.theme.json（一次性、幂等）：已存在的 id 跳过。
        /// 返回成功写出的文件数；out failed 是写失败 / 无法识别的条目数。
        /// 不抛异常：目录建不出来或单个主题写失败都只计数。
        /// </summary>
        public static int MigrateLegacy(List<CustomTheme> legacy)
        {
            int failed;
            return MigrateLegacy(legacy, out failed);
        }

        /// <summary>同上，额外返回失败条数（SettingsStore 用它决定要不要保留旧列表下次重试）。</summary>
        public static int MigrateLegacy(List<CustomTheme> legacy, out int failed)
        {
            failed = 0;
            if (legacy == null || legacy.Count == 0) return 0;
            if (dir.Length == 0)
            {
                failed = legacy.Count;
                return 0;
            }
            try { EnsureDir(); }
            catch
            {
                failed = legacy.Count;
                return 0;
            }

            // 先读一遍目录里已有的用户主题：既用于「同 id 跳过」，也用于识别上次迁移时
            // 因为撞内置 id 被改名成 legacyId-2 的那份（保证重复迁移幂等、不重复写文件）。
            var existing = new List<ThemeDef>();
            try
            {
                foreach (string file in Directory.GetFiles(dir, "*" + Extension))
                {
                    ThemeDef t;
                    string readError;
                    if (TryRead(file, out t, out readError) && t != null) existing.Add(t);
                }
            }
            catch { }

            int written = 0;
            foreach (CustomTheme item in legacy)
            {
                if (item == null) { failed++; continue; }
                string id = Clean(item.id);
                if (id.Length == 0) id = "my-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                string name = Clean(item.name);
                if (name.Length == 0) name = "我的主题";
                if (FindMigrated(existing, id, name) != null) continue;
                if (IdInUse(id)) id = UniqueId(id);   // 和内置 / 已有用户主题撞 id → 换一个

                string target = Path.Combine(dir, FileNameFor(id));
                if (File.Exists(target)) { failed++; continue; }   // 文件名被别的主题占了，不覆盖
                ThemeDef theme = FromLegacy(item, id);
                string error;
                if (WriteFile(theme, target, out error))
                {
                    written++;
                    existing.Add(theme);
                }
                else failed++;
            }
            return written;
        }

        /// <summary>这条旧主题是否已经迁移过（同 id，或上次因撞 id 被改名成 legacyId-N 的那份）。</summary>
        private static ThemeDef FindMigrated(List<ThemeDef> existing, string legacyId, string legacyName)
        {
            foreach (ThemeDef t in existing)
            {
                if (t == null) continue;
                if (string.Equals(t.id, legacyId, StringComparison.OrdinalIgnoreCase)) return t;
                if (t.id != null && t.id.StartsWith(legacyId + "-", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.name, legacyName, StringComparison.Ordinal)
                    && string.Equals(t.description, LegacyMigrationDescription, StringComparison.Ordinal)) return t;
            }
            return null;
        }

        /// <summary>
        /// id 是否已被内置主题 / 主题目录里的文件占用（大小写不敏感）。
        /// 直接看磁盘而不是看 Themes 注册表，避免「刚 Configure 到新目录、注册表还是旧的」时误判。
        /// </summary>
        public static bool IdInUse(string id)
        {
            string clean = Clean(id);
            if (clean.Length == 0) return true;
            foreach (ThemeDef t in Themes.BuiltIn)
                if (string.Equals(t.id, clean, StringComparison.OrdinalIgnoreCase)) return true;
            if (dir.Length == 0) return false;
            string[] files;
            try
            {
                if (!Directory.Exists(dir)) return false;
                files = Directory.GetFiles(dir, "*" + Extension);
            }
            catch { return false; }
            foreach (string file in files)
            {
                ThemeDef existing;
                string error;
                if (TryRead(file, out existing, out error) && existing != null
                    && string.Equals(existing.id, clean, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>在当前占用情况（内置 + 用户主题 + 主题目录）下保证 id 唯一。</summary>
        public static string UniqueId(string id)
        {
            string baseId = Clean(id);
            if (baseId.Length == 0) baseId = "my-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            if (!IdInUse(baseId)) return baseId;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseId + "-" + i.ToString(CultureInfo.InvariantCulture);
                if (!IdInUse(candidate)) return candidate;
            }
            return baseId + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        // ==================================================================
        // 序列化 / 解析：手写有序 JSON（Pretty 复用 SettingsJson），读侧走字典更抗字段增减。
        // ==================================================================

        private static string ToJson(ThemeDef theme)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"schema\":").Append(Schema.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"app\":").Append(Quote(AppTag));
            sb.Append(",\"id\":").Append(Quote(theme.id));
            sb.Append(",\"name\":").Append(Quote(theme.name));
            sb.Append(",\"author\":").Append(Quote(theme.author));
            sb.Append(",\"description\":").Append(Quote(theme.description));
            sb.Append(",\"created\":").Append(Quote(theme.created));
            sb.Append(",\"colors\":{");
            sb.Append("\"accent\":").Append(Quote(ColorText(theme.accent)));
            sb.Append(",\"success\":").Append(Quote(ColorText(theme.success)));
            sb.Append(",\"bgTop\":").Append(Quote(ColorText(theme.bgTop)));
            sb.Append(",\"bgBottom\":").Append(Quote(ColorText(theme.bgBottom)));
            sb.Append(",\"track\":").Append(Quote(ColorText(theme.track)));
            sb.Append(",\"textMain\":").Append(Quote(ColorText(theme.textMain)));
            sb.Append(",\"textMuted\":").Append(Quote(ColorText(theme.textMuted)));
            sb.Append(",\"textDim\":").Append(Quote(ColorText(theme.textDim)));
            sb.Append(",\"edge\":").Append(Quote(ColorText(theme.edge)));
            sb.Append("}}");
            return SettingsJson.Pretty(sb.ToString());
        }

        private static bool TryParse(string text, string path, out ThemeDef theme, out string error)
        {
            theme = null;
            error = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "主题文件是空的。";
                return false;
            }
            Dictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
            }
            catch
            {
                error = "主题文件不是合法 JSON。";
                return false;
            }
            if (root == null)
            {
                error = "主题文件内容不是 JSON 对象。";
                return false;
            }

            int schema;
            if (!TryInt(Get(root, "schema"), out schema))
            {
                error = "主题文件缺少 schema。";
                return false;
            }
            if (schema != Schema)
            {
                error = "不支持的主题文件版本：" + schema.ToString(CultureInfo.InvariantCulture);
                return false;
            }
            string app = Clean(Get(root, "app"));
            if (!string.Equals(app, AppTag, StringComparison.Ordinal))
            {
                error = "不是上班工具箱的主题文件（app 不是 WorkCountdown）。";
                return false;
            }
            string id = Clean(Get(root, "id"));
            if (id.Length == 0)
            {
                error = "主题缺少 id。";
                return false;
            }
            string name = Clean(Get(root, "name"));
            if (name.Length == 0)
            {
                error = "主题缺少 name。";
                return false;
            }
            var colors = Get(root, "colors") as Dictionary<string, object>;
            if (colors == null)
            {
                error = "主题缺少 colors。";
                return false;
            }

            Color accent, success, bgTop, bgBottom, track, textMain, textMuted, textDim, edge;
            if (!TryColor(colors, "accent", out accent)) { error = "颜色 accent 格式不对（要 #RRGGBB 或 #AARRGGBB）。"; return false; }
            if (!TryColor(colors, "success", out success)) { error = "颜色 success 格式不对（要 #RRGGBB 或 #AARRGGBB）。"; return false; }
            if (!TryColor(colors, "bgTop", out bgTop)) { error = "颜色 bgTop 格式不对（要 #RRGGBB 或 #AARRGGBB）。"; return false; }
            if (!TryColor(colors, "bgBottom", out bgBottom)) { error = "颜色 bgBottom 格式不对（要 #RRGGBB 或 #AARRGGBB）。"; return false; }
            if (!TryColor(colors, "track", out track)) { error = "颜色 track 格式不对（要 #RRGGBB 或 #AARRGGBB）。"; return false; }
            if (!TryColor(colors, "textMain", out textMain)) { error = "颜色 textMain 格式不对（要 #RRGGBB 或 #AARRGGBB）。"; return false; }
            if (!TryColor(colors, "textMuted", out textMuted)) { error = "颜色 textMuted 格式不对（要 #RRGGBB 或 #AARRGGBB）。"; return false; }
            if (!TryColor(colors, "textDim", out textDim)) { error = "颜色 textDim 格式不对（要 #RRGGBB 或 #AARRGGBB）。"; return false; }
            if (!TryColor(colors, "edge", out edge)) { error = "颜色 edge 格式不对（要 #RRGGBB 或 #AARRGGBB）。"; return false; }

            var t = new ThemeDef();
            t.id = id;
            t.name = name;
            t.author = Clean(Get(root, "author"));
            t.description = Clean(Get(root, "description"));
            t.created = Clean(Get(root, "created"));
            t.filePath = path;
            t.isUser = true;
            t.accent = accent;
            t.success = success;
            t.bgTop = bgTop;
            t.bgBottom = bgBottom;
            t.track = track;
            t.textMain = textMain;
            t.textMuted = textMuted;
            t.textDim = textDim;
            t.edge = edge;
            theme = t;
            return true;
        }

        private static bool WriteFile(ThemeDef theme, string path, out string error)
        {
            error = "";
            if (theme == null)
            {
                error = "没有要保存的主题。";
                return false;
            }
            string id = Clean(theme.id);
            if (id.Length == 0)
            {
                error = "主题缺少 id。";
                return false;
            }
            string name = Clean(theme.name);
            if (name.Length == 0)
            {
                error = "主题缺少 name。";
                return false;
            }
            string target = Clean(path);
            if (target.Length == 0)
            {
                if (dir.Length == 0)
                {
                    error = "主题目录还没配置。";
                    return false;
                }
                target = Path.Combine(dir, FileNameFor(id));
            }
            else if (Path.GetExtension(target).Length == 0)
            {
                target = target + Extension;
            }

            string temp = null;
            try
            {
                string parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                var snapshot = theme.Clone();   // 只把规范化后的值写进文件，不动调用者的对象
                snapshot.id = id;
                snapshot.name = name;
                if (snapshot.author == null) snapshot.author = "";
                if (snapshot.description == null) snapshot.description = "";
                if (string.IsNullOrWhiteSpace(snapshot.created)) snapshot.created = NowText();

                string json = ToJson(snapshot);
                temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                ReplaceFile(temp, target);
                temp = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "写入主题文件失败：" + ex.Message;
                return false;
            }
            finally
            {
                if (temp != null)
                {
                    try { File.Delete(temp); } catch { }
                }
            }
        }

        private static void ReplaceFile(string temp, string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, null, true);
                    return;
                }
                catch
                {
                    // FAT / 便携盘等不支持 File.Replace：退回删除 + 改名（主题文件坏了会被跳过，不会炸）。
                    File.Delete(path);
                }
            }
            File.Move(temp, path);
        }

        private static ThemeDef FromLegacy(CustomTheme item, string id)
        {
            string name = Clean(item.name);
            if (name.Length == 0) name = "我的主题";
            string bar = !string.IsNullOrWhiteSpace(item.bar) ? item.bar : item.accent;
            ThemeDef t = string.IsNullOrWhiteSpace(item.bg)
                ? Themes.FromAccent(id, name, bar)          // 最老的自建主题只有主色：按主色推导整套
                : Themes.FromColors(id, name, item.bg, bar); // v3.3+ 背景色 + 进度条色分开
            t.author = "";
            t.description = LegacyMigrationDescription;
            t.created = NowText();
            t.filePath = null;
            t.isUser = true;
            return t;
        }

        // ---------------- 小工具 ----------------

        private static string NowText()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string Clean(object text)
        {
            string value = Convert.ToString(text, CultureInfo.InvariantCulture);
            return value == null ? "" : value.Trim();
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static bool TryInt(object value, out int result)
        {
            result = 0;
            if (value == null) return false;
            try
            {
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }

        private static bool TryColor(Dictionary<string, object> colors, string key, out Color color)
        {
            color = Color.Magenta;
            return TryParseColor(Convert.ToString(Get(colors, key), CultureInfo.InvariantCulture), out color);
        }

        /// <summary>颜色文本：#RRGGBB 或 #AARRGGBB（大小写都认，前后空白忽略）。</summary>
        public static bool TryParseColor(string text, out Color color)
        {
            color = Color.Magenta;
            string hex = Clean(text);
            if (hex.Length == 0 || hex[0] != '#') return false;
            string digits = hex.Substring(1);
            if (digits.Length != 6 && digits.Length != 8) return false;
            int value;
            if (!int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return false;
            if (digits.Length == 6)
                color = Color.FromArgb(255, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
            else
                color = Color.FromArgb((value >> 24) & 0xFF, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
            return true;
        }

        /// <summary>颜色转文件文本：不透明写 #RRGGBB，带 alpha 写 #AARRGGBB（edge 的 alpha 保真）。</summary>
        public static string ColorText(Color color)
        {
            if (color.A == 255)
                return "#" + color.R.ToString("X2", CultureInfo.InvariantCulture)
                    + color.G.ToString("X2", CultureInfo.InvariantCulture)
                    + color.B.ToString("X2", CultureInfo.InvariantCulture);
            return "#" + color.A.ToString("X2", CultureInfo.InvariantCulture)
                + color.R.ToString("X2", CultureInfo.InvariantCulture)
                + color.G.ToString("X2", CultureInfo.InvariantCulture)
                + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>id / 文件名 slug：保留字母数字（含中文）和 - _，其余换成 '-'，连续 '-' 折叠。</summary>
        private static string Slug(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var sb = new StringBuilder();
            bool lastDash = false;
            foreach (char c in text.Trim())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                    lastDash = false;
                }
                else if (c == '-' || c == '_')
                {
                    if (!lastDash) { sb.Append(c); lastDash = true; }
                }
                else
                {
                    if (!lastDash) { sb.Append('-'); lastDash = true; }
                }
            }
            string slug = sb.ToString().Trim('-', '_');
            if (slug.Length > MaxSlugLength) slug = slug.Substring(0, MaxSlugLength).Trim('-', '_');
            return slug;
        }

        private static string Quote(string text)
        {
            if (text == null) text = "";
            var sb = new StringBuilder(text.Length + 2);
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
