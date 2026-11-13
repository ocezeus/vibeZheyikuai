using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WorkCountdown
{
    /// <summary>GitHub 上一个 Release 的信息（自动更新用）。</summary>
    public class UpdateRelease
    {
        public string version;       // 3.6.1（tag 去掉 v 前缀）
        public string tag;           // v3.6.1
        public string name;          // 上班工具箱 v3.6.1
        public string notes;         // release 说明（可空）
        public string assetUrl;      // 新 exe / zip 的下载地址（可空：HTML 兜底时拿不到）
        public string assetName;
        public long assetSize;
        public DateTime publishedAt;

        public bool HasAsset { get { return !string.IsNullOrEmpty(assetUrl); } }
    }

    /// <summary>自动更新状态机（气泡菜单「检查更新」胶囊用）。</summary>
    public enum UpdatePhase
    {
        Idle = 0,
        Checking = 1,
        UpToDate = 2,
        Available = 3,
        Downloading = 4,
        Ready = 5,
        Error = 6
    }

    /// <summary>
    /// 软件更新：从 GitHub Release 检查新版本、下载全量 exe、替换旧 exe 并重启。
    ///
    /// 设计取舍：
    /// * 不用 System.IO.Compression / HttpClient，避免给 build.ps1、build-tests.ps1 和一堆出图探针
    ///   的编译命令加新引用（它们都硬编码了 /reference 列表）。所以「全量更新」下载的是 Release 里的
    ///   单个 WorkCountdown.exe —— exe 本身包含全部功能，字体目录（pack\fonts）与配置（%APPDATA%）不动。
    /// * 发布时 assets 里同时传 zip 和 exe：普通用户手动下载 zip，自动更新只挑 exe。
    /// * 检查走 api.github.com（未登录 60 次/小时/IP，够用）；被限流或不通时退回 releases/latest 的
    ///   HTML 跳转，只能拿到版本号，界面就只给「去 GitHub 下载」。
    /// </summary>
    public static class UpdateService
    {
        public const string Owner = "ocezeus";
        public const string Repo = "vibeZheyikuai";

        public static string RepoUrl { get { return "https://github.com/" + Owner + "/" + Repo; } }
        public static string ReleasesUrl { get { return RepoUrl + "/releases"; } }
        public static string LatestPageUrl { get { return RepoUrl + "/releases/latest"; } }
        public static string LatestApiUrl { get { return "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest"; } }

        private const string UserAgent = "WorkCountdown-Updater";
        private const int DefaultTimeout = 20000;

        /// <summary>
        /// 这台机器的 .NET Framework 默认可能只开 TLS 1.0/SSL3，而 GitHub 只收 TLS 1.2。
        /// 老版本框架的 SecurityProtocolType 枚举里没有 Tls12，直接用数值 3072。
        /// </summary>
        static UpdateService()
        {
            try
            {
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
            }
            catch { }
            try { ServicePointManager.Expect100Continue = false; } catch { }
        }

        // ---------------- 版本比较 ----------------

        /// <summary>把 "v3.6.0" / "3.6" / "3.6.0.0" 拆成数字段；解析失败返回 false。</summary>
        public static bool TryParseVersion(string text, out int[] parts)
        {
            parts = new int[0];
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
            // 只取前面的数字和点（"3.6.0-beta1" → "3.6.0"）
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if ((c >= '0' && c <= '9') || c == '.') sb.Append(c);
                else break;
            }
            string core = sb.ToString().Trim('.');
            if (core.Length == 0) return false;
            string[] bits = core.Split('.');
            var list = new List<int>();
            foreach (var b in bits)
            {
                int n;
                if (!int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return false;
                list.Add(n);
            }
            parts = list.ToArray();
            return true;
        }

        /// <summary>candidate 比 current 新？"3.6.1" &gt; "3.6.0"。</summary>
        public static bool IsNewer(string candidate, string current)
        {
            int[] a, b;
            if (!TryParseVersion(candidate, out a)) return false;
            if (!TryParseVersion(current, out b)) return false;
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x > y) return true;
                if (x < y) return false;
            }
            return false;
        }

        public static string CurrentVersion { get { return AppInfo.Version; } }

        // ---------------- 检查更新 ----------------

        /// <summary>
        /// 查 GitHub 最新 Release（同步调用，网络操作，别在 UI 线程直接调）。
        /// 先走 API；失败再抓 releases/latest 的 HTML 跳转兜底（只能拿版本号）。
        /// 失败抛 WebException / 自定义 Exception，调用方显示「检查失败」。
        /// </summary>
        public static UpdateRelease Check()
        {
            Exception apiEx = null;
            try
            {
                UpdateRelease viaApi = CheckViaApi();
                if (viaApi != null) return viaApi;
            }
            catch (Exception ex)
            {
                apiEx = ex;
            }
            try
            {
                UpdateRelease viaHtml = CheckViaHtml();
                if (viaHtml != null) return viaHtml;
            }
            catch { }
            if (apiEx != null)
            {
                var wex = apiEx as WebException;
                var resp = wex == null ? null : wex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException("GitHub 上还没有发布版本。", apiEx);
                throw new InvalidOperationException("检查更新失败：" + apiEx.Message, apiEx);
            }
            throw new InvalidOperationException("检查更新失败：GitHub 没有返回可用的版本信息。");
        }

        private static UpdateRelease CheckViaApi()
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(LatestApiUrl);
            req.UserAgent = UserAgent;
            req.Accept = "application/vnd.github+json";
            req.Timeout = DefaultTimeout;
            req.ReadWriteTimeout = DefaultTimeout;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = sr.ReadToEnd();

            var ser = new JavaScriptSerializer();
            var dict = ser.Deserialize<Dictionary<string, object>>(json);
            if (dict == null) return null;

            var rel = new UpdateRelease();
            rel.tag = Str(dict, "tag_name");
            rel.name = Str(dict, "name");
            rel.notes = Str(dict, "body");
            rel.publishedAt = Time(dict, "published_at");
            rel.version = VersionFromTag(rel.tag);
            if (string.IsNullOrEmpty(rel.version)) return null;

            object assetsObj;
            if (dict.TryGetValue("assets", out assetsObj))
            {
                var assets = assetsObj as System.Collections.IEnumerable;
                if (assets != null)
                {
                    // 优先挑 WorkCountdown.exe（Release 里同时放 exe + zip）
                    foreach (var item in assets)
                    {
                        var a = item as Dictionary<string, object>;
                        if (a == null) continue;
                        string nm = Str(a, "name");
                        if (nm == null) continue;
                        if (nm.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            rel.assetUrl = Str(a, "browser_download_url");
                            rel.assetName = nm;
                            rel.assetSize = Long(a, "size");
                            break;
                        }
                    }
                }
            }
            return rel;
        }

        /// <summary>HTML 兜底：follow releases/latest 跳转，从最终 URL / 页面里抠 tag 版本号。</summary>
        private static UpdateRelease CheckViaHtml()
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(LatestPageUrl);
            req.UserAgent = UserAgent;
            req.Timeout = DefaultTimeout;
            req.ReadWriteTimeout = DefaultTimeout;
            req.AllowAutoRedirect = true;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                string finalUrl = resp.ResponseUri == null ? "" : resp.ResponseUri.ToString();
                string body;
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    body = sr.ReadToEnd();
                string tag = null;
                var m = Regex.Match(finalUrl, @"/releases/tag/([^/?#]+)");
                if (m.Success) tag = m.Groups[1].Value;
                if (string.IsNullOrEmpty(tag))
                {
                    m = Regex.Match(body, @"/releases/tag/([A-Za-z0-9._-]+)");
                    if (m.Success) tag = m.Groups[1].Value;
                }
                if (string.IsNullOrEmpty(tag)) return null;
                var rel = new UpdateRelease();
                rel.tag = tag;
                rel.version = VersionFromTag(tag);
                rel.name = "上班工具箱 " + tag;
                if (string.IsNullOrEmpty(rel.version)) return null;
                return rel;
            }
        }

        public static string VersionFromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            string s = tag.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
            int[] parts;
            if (!TryParseVersion(s, out parts)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append(parts[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // ---------------- 下载 ----------------

        /// <summary>下载 Release 里的 exe 到临时目录，返回文件路径。progress(已下载字节, 总字节)。</summary>
        public static string Download(UpdateRelease rel, Action<long, long> progress)
        {
            if (rel == null || !rel.HasAsset) throw new InvalidOperationException("这个版本没有可自动下载的安装包。");
            string dir = Path.Combine(Path.GetTempPath(), "WorkCountdown-update");
            Directory.CreateDirectory(dir);
            string name = string.IsNullOrEmpty(rel.assetName) ? "WorkCountdown.exe" : rel.assetName;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string target = Path.Combine(dir, name);
            if (File.Exists(target)) { try { File.Delete(target); } catch { } }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(rel.assetUrl);
            req.UserAgent = UserAgent;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            try { req.AllowAutoRedirect = true; } catch { }
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : (rel.assetSize > 0 ? rel.assetSize : -1);
                using (var input = resp.GetResponseStream())
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long done = 0;
                    var buffer = new byte[64 * 1024];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        done += read;
                        if (progress != null) progress(done, total);
                    }
                }
            }
            return target;
        }

        /// <summary>下载完整性检查：必须存在、像 exe、FileVersion 不低于目标版本。</summary>
        public static bool VerifyDownloaded(string path, UpdateRelease rel, out string message)
        {
            message = "";
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                message = "下载的文件不存在。";
                return false;
            }
            var info = new FileInfo(path);
            if (info.Length < 100 * 1024)
            {
                message = "下载的文件太小，可能不是安装包。";
                return false;
            }
            if (rel != null && rel.assetSize > 0 && info.Length < rel.assetSize * 0.9)
            {
                message = "下载不完整（" + HumanSize(info.Length) + " / " + HumanSize(rel.assetSize) + "）。";
                return false;
            }
            if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                message = "";
                return true;      // 非 exe 只能交给用户手动处理，不做自动替换
            }
            string ver = "";
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                ver = vi.FileVersion;
            }
            catch { }
            int[] a, b;
            if (!string.IsNullOrEmpty(ver) && TryParseVersion(ver, out a)
                && rel != null && TryParseVersion(rel.version, out b) && IsNewer(rel.version, ver))
            {
                message = "下载的版本是 " + ver + "，比目标版本 " + rel.version + " 旧。";
                return false;
            }
            return true;
        }

        // ---------------- 应用更新（全量替换 exe + 重启） ----------------

        /// <summary>
        /// 写一个临时 .cmd：反复尝试覆盖 exe（进程一退出文件锁就放开）→ 重新启动 → 自删。
        /// 调完立刻自己退出（调用方决定何时 Close）。返回 updater 脚本路径。
        /// </summary>
        public static string LaunchUpdater(string newExePath)
        {
            if (string.IsNullOrEmpty(newExePath) || !File.Exists(newExePath))
                throw new InvalidOperationException("找不到下载好的新版本 exe。");
            string currentExe = Application.ExecutablePath;
            string updater = UpdaterScriptPath();
            File.WriteAllText(updater, BuildUpdaterScript(currentExe, newExePath), new UTF8Encoding(false));

            var psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c \"" + updater + "\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = Path.GetDirectoryName(currentExe);
            Process.Start(psi);
            return updater;
        }

        /// <summary>更新脚本的临时路径（同一毫秒里也唯一）。</summary>
        public static string UpdaterScriptPath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "WorkCountdown-update");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "apply-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".cmd");
        }

        /// <summary>
        /// 生成替换脚本正文（独立出来方便测试）：备份 → 最多 40 次、每秒一次尝试覆盖 → 启动 → 自删。
        /// 不用 tasklist 判 PID（会被别的 PID 子串误伤），直接靠「文件锁没放开就覆盖失败」来判断。
        /// </summary>
        public static string BuildUpdaterScript(string currentExe, string newExePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal enableextensions");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("set \"SRC=" + newExePath + "\"");
            sb.AppendLine("set \"DST=" + currentExe + "\"");
            sb.AppendLine("set \"BAK=" + currentExe + ".bak\"");
            sb.AppendLine("set /a N=0");
            sb.AppendLine(":retry");
            sb.AppendLine("set /a N+=1");
            // 备份也放在重试里：旧进程还锁着时读不到，等锁放开的那一轮再备份
            sb.AppendLine("if exist \"%DST%\" if not exist \"%BAK%\" copy /y \"%DST%\" \"%BAK%\" >nul 2>nul");
            sb.AppendLine("copy /y \"%SRC%\" \"%DST%\" >nul 2>nul");
            sb.AppendLine("if not errorlevel 1 goto done");
            sb.AppendLine("if %N% GEQ 40 goto fail");
            sb.AppendLine("ping -n 2 127.0.0.1 >nul");
            sb.AppendLine("goto retry");
            sb.AppendLine(":done");
            sb.AppendLine("start \"\" \"%DST%\"");
            sb.AppendLine("del \"%~f0\" >nul 2>nul");
            sb.AppendLine("exit /b 0");
            sb.AppendLine(":fail");
            sb.AppendLine("echo 更新失败：新版本替换不进去，程序目录可能没有写权限。");
            sb.AppendLine("echo 已回滚到旧版本，按任意键打开旧版本。");
            sb.AppendLine("if exist \"%BAK%\" copy /y \"%BAK%\" \"%DST%\" >nul 2>nul");
            sb.AppendLine("pause >nul");
            sb.AppendLine("start \"\" \"%DST%\"");
            sb.AppendLine("del \"%~f0\" >nul 2>nul");
            sb.AppendLine("exit /b 1");
            return sb.ToString();
        }

        // ---------------- 小工具 ----------------

        public static string HumanSize(long bytes)
        {
            if (bytes < 0) return "?";
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>Release 说明压成一行摘要（气泡里只放一两行）。</summary>
        public static string ShortNotes(string notes, int maxChars)
        {
            if (string.IsNullOrEmpty(notes)) return "";
            string s = notes.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = s.Split('\n');
            var sb = new StringBuilder();
            foreach (var raw in lines)
            {
                string line = raw.Trim().TrimStart('#', '-', '*', ' ');
                if (line.Length == 0) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(line);
                if (sb.Length >= maxChars) break;
            }
            string text = sb.ToString();
            if (text.Length > maxChars) text = text.Substring(0, Math.Max(1, maxChars - 1)) + "…";
            return text;
        }

        private static string Str(Dictionary<string, object> dict, string key)
        {
            object v;
            if (dict == null || !dict.TryGetValue(key, out v) || v == null) return "";
            return v as string ?? v.ToString();
        }

        private static long Long(Dictionary<string, object> dict, string key)
        {
            object v;
            if (dict == null || !dict.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static DateTime Time(Dictionary<string, object> dict, string key)
        {
            string text = Str(dict, key);
            DateTime t;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out t)) return t;
            return DateTime.MinValue;
        }
    }
}
