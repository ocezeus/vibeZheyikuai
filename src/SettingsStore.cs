using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace WorkCountdown
{
    /// <summary>保存结果：成功、失败、WC_NO_SAVE 只读跳过必须分开表达。</summary>
    public enum SettingsSaveStatus
    {
        Saved,
        Failed,
        Skipped
    }

    /// <summary>一次保存的结果。成功表示完整配置已落盘，失败表示上一份有效配置仍保留。</summary>
    public sealed class SettingsSaveResult
    {
        public SettingsSaveStatus Status { get; private set; }
        public string Message { get; private set; }
        public bool Succeeded { get { return Status == SettingsSaveStatus.Saved; } }
        public bool IsFailed { get { return Status == SettingsSaveStatus.Failed; } }
        public bool IsSkipped { get { return Status == SettingsSaveStatus.Skipped; } }

        private SettingsSaveResult(SettingsSaveStatus status, string message)
        {
            Status = status;
            Message = message == null ? "" : message;
        }

        public static SettingsSaveResult Success()
        {
            return new SettingsSaveResult(SettingsSaveStatus.Saved, "");
        }

        public static SettingsSaveResult Failure(string message)
        {
            return new SettingsSaveResult(SettingsSaveStatus.Failed, message);
        }

        public static SettingsSaveResult Skip(string message)
        {
            return new SettingsSaveResult(SettingsSaveStatus.Skipped, message);
        }
    }

    /// <summary>加载结果：读到已有配置 / 首次使用 / 已有配置读不出来而回退默认值。</summary>
    public enum SettingsLoadStatus
    {
        Loaded,
        Fresh,
        Fallback
    }

    public sealed class SettingsLoadResult
    {
        public Settings Settings { get; private set; }
        public SettingsLoadStatus Status { get; private set; }
        public string Message { get; private set; }
        public bool IsFallback { get { return Status == SettingsLoadStatus.Fallback; } }

        private SettingsLoadResult(Settings settings, SettingsLoadStatus status, string message)
        {
            Settings = settings;
            Status = status;
            Message = message == null ? "" : message;
        }

        public static SettingsLoadResult FromFile(Settings settings)
        {
            return new SettingsLoadResult(settings, SettingsLoadStatus.Loaded, "");
        }

        public static SettingsLoadResult FromDefaults(Settings settings)
        {
            return new SettingsLoadResult(settings, SettingsLoadStatus.Fresh, "");
        }

        public static SettingsLoadResult FromFallback(Settings settings, string message)
        {
            return new SettingsLoadResult(settings, SettingsLoadStatus.Fallback, message);
        }
    }

    /// <summary>导入结果：读文件成功（返回独立快照）或失败。</summary>
    public enum SettingsImportStatus
    {
        Imported,
        Failed
    }

    public sealed class SettingsImportResult
    {
        public Settings Settings { get; private set; }
        public SettingsImportStatus Status { get; private set; }
        public string Message { get; private set; }
        public bool Succeeded { get { return Status == SettingsImportStatus.Imported; } }

        private SettingsImportResult(Settings settings, SettingsImportStatus status, string message)
        {
            Settings = settings;
            Status = status;
            Message = message == null ? "" : message;
        }

        public static SettingsImportResult FromSettings(Settings settings)
        {
            return new SettingsImportResult(settings, SettingsImportStatus.Imported, "");
        }

        public static SettingsImportResult Failure(string message)
        {
            return new SettingsImportResult(null, SettingsImportStatus.Failed, message);
        }
    }

    /// <summary>导出结果：成功 / 失败。失败不会破坏目标文件原来的一份。</summary>
    public enum SettingsExportStatus
    {
        Exported,
        Failed
    }

    public sealed class SettingsExportResult
    {
        public SettingsExportStatus Status { get; private set; }
        public string Message { get; private set; }
        public string Path { get; private set; }
        public bool Succeeded { get { return Status == SettingsExportStatus.Exported; } }

        private SettingsExportResult(SettingsExportStatus status, string message, string path)
        {
            Status = status;
            Message = message == null ? "" : message;
            Path = path == null ? "" : path;
        }

        public static SettingsExportResult Success(string path)
        {
            return new SettingsExportResult(SettingsExportStatus.Exported, "", path);
        }

        public static SettingsExportResult Failure(string message)
        {
            return new SettingsExportResult(SettingsExportStatus.Failed, message, "");
        }
    }

    /// <summary>
    /// 配置文件持久化 seam：序列化、读文件、写同目录临时文件、原子提交、清理临时文件。
    /// 生产实现是 FileSettingsStorage；测试 adapter 可在序列化 / 临时写 / 替换任一步注入失败，
    /// 不依赖真实磁盘故障。这里不抽象成通用存储框架，只服务配置快照的保存与加载。
    /// </summary>
    public interface ISettingsStorage
    {
        void EnsureDirectory(string path);
        bool FileExists(string path);
        string ReadAllText(string path);
        string Serialize(Settings settings);
        Settings Deserialize(string text);
        void WriteTemp(string tempPath, string content);
        void Commit(string tempPath, string path);
        void Delete(string path);
    }

    /// <summary>生产存储：JSON + 同目录临时文件 + 同卷原子改名提交。</summary>
    public sealed class FileSettingsStorage : ISettingsStorage
    {
        private const uint MoveFileReplaceExisting = 0x1;
        private const uint MoveFileWriteThrough = 0x8;

        // File.Replace 在 FAT 等便携盘文件系统上不可用；同卷 MoveFileEx 既能替换已有文件，
        // 也能让首次保存原子出现，失败时目标文件保持原样。
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

        public void EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public string Serialize(Settings settings)
        {
            return new JavaScriptSerializer().Serialize(settings);
        }

        public Settings Deserialize(string text)
        {
            return new JavaScriptSerializer().Deserialize<Settings>(text);
        }

        public void WriteTemp(string tempPath, string content)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
        }

        public void Commit(string tempPath, string path)
        {
            // 同目录、同卷的改名操作：已有文件直接替换，首次保存直接出现。
            // 失败时目标文件保持原样；调用方负责删除没提交的临时文件。
            if (!MoveFileEx(tempPath, path, MoveFileReplaceExisting | MoveFileWriteThrough))
                throw new IOException("原子替换配置失败（Win32 " + Marshal.GetLastWin32Error() + "）。");
        }

        public void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// 配置模块：加载、规范化、迁移与安全写入都收在这里。
    /// Save 只规范化一份独立快照，不原地改调用者的 Settings；保存结果区分成功、失败与只读跳过。
    /// 原子写入只保证单次保存不会留下半文件，不解决不同内存快照之间“旧覆盖新”的问题，
    /// 代次/生命周期检查由配置拥有者负责（见 ADR-0002）。
    /// </summary>
    public class SettingsStore
    {
        private const string SaveFailureMessage = "保存配置失败：写入没有完成，上一份有效配置保持不变。详情见 widget.log。";
        private const string SkipMessage = "WC_NO_SAVE=1：本次运行不写配置文件。";
        private const string LoadFailureMessage = "读取配置失败：已使用默认设置，详情见 widget.log。";
        private const string ImportFailureMessage = "配置文件读不出来，详情见 widget.log。";
        private const string ExportFailureMessage = "导出配置失败，详情见 widget.log。";

        // 同一进程内的保存串行化：临时文件名各自唯一，但提交顺序仍需排队，避免并发调用交错。
        // 这不解决跨进程多写者，也不负责旧快照代次仲裁。
        private static readonly object SaveLock = new object();
        private readonly ISettingsStorage storage;

        public string Dir { get; private set; }
        public string SettingsPath { get; private set; }
        public string LogPath { get; private set; }
        /// <summary>便携模式：配置放在 exe 旁边的 data 目录（U 盘 / 免安装）。</summary>
        public bool Portable { get; private set; }

        public SettingsStore()
        {
            storage = new FileSettingsStorage();
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            Portable = DetectPortable(exeDir);
            Dir = ResolveSettingsDir(exeDir, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Portable);
            InitPaths();
        }

        /// <summary>测试 / 宿主注入存储与目录；生产用无参构造。</summary>
        public SettingsStore(ISettingsStorage storage, string dir, bool portable)
        {
            if (storage == null) throw new ArgumentNullException("storage");
            if (string.IsNullOrEmpty(dir)) throw new ArgumentException("配置目录不能为空。", "dir");
            this.storage = storage;
            Dir = dir;
            Portable = portable;
            InitPaths();
        }

        /// <summary>保持历史位置：普通模式在 %APPDATA%\WorkCountdown，便携模式在 exe 旁的 data。</summary>
        public static string ResolveSettingsDir(string exeDir, string appDataRoot, bool portable)
        {
            return portable ? Path.Combine(exeDir, "data") : Path.Combine(appDataRoot, "WorkCountdown");
        }

        /// <summary>便携模式判定沿用 exe 旁是否存在 portable.txt。</summary>
        public static bool DetectPortable(string exeDir)
        {
            try { return File.Exists(Path.Combine(exeDir, "portable.txt")); }
            catch { return false; }
        }

        private void InitPaths()
        {
            try { storage.EnsureDirectory(Dir); } catch { }
            SettingsPath = Path.Combine(Dir, "settings.json");
            LogPath = Path.Combine(Dir, "widget.log");
        }

        public bool Exists
        {
            get { return storage.FileExists(SettingsPath); }
        }

        /// <summary>把当前设置另存为一份（给别人复用 / 备份），同样走临时文件 + 原子提交。</summary>
        public SettingsExportResult Export(Settings settings, string path)
        {
            if (settings == null || string.IsNullOrEmpty(path))
                return SettingsExportResult.Failure(ExportFailureMessage);
            lock (SaveLock)
            {
                string tempPath = "";
                try
                {
                    string json = storage.Serialize(settings);
                    Settings snapshot = storage.Deserialize(json);
                    if (snapshot == null) return SettingsExportResult.Failure(ExportFailureMessage);
                    snapshot.Normalize();
                    string pretty = SettingsJson.Pretty(storage.Serialize(snapshot));
                    tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
                    storage.WriteTemp(tempPath, pretty);
                    storage.Commit(tempPath, path);
                    return SettingsExportResult.Success(path);
                }
                catch (Exception ex)
                {
                    if (tempPath.Length > 0) { try { storage.Delete(tempPath); } catch { } }
                    Log("导出配置失败: " + ex.GetType().Name + " / " + ex.Message);
                    return SettingsExportResult.Failure(ExportFailureMessage);
                }
            }
        }

        /// <summary>从别的配置文件导入（覆盖当前设置）。返回规范化后的独立快照，失败返回可检查结果。</summary>
        public SettingsImportResult Import(string path)
        {
            if (string.IsNullOrEmpty(path)) return SettingsImportResult.Failure(ImportFailureMessage);
            try
            {
                string text = storage.ReadAllText(path);
                Settings loaded = storage.Deserialize(text);
                if (loaded == null) return SettingsImportResult.Failure(ImportFailureMessage);
                loaded.Normalize();
                return SettingsImportResult.FromSettings(loaded);
            }
            catch (Exception ex)
            {
                Log("导入配置失败: " + ex.GetType().Name + " / " + ex.Message);
                return SettingsImportResult.Failure(ImportFailureMessage);
            }
        }

        /// <summary>
        /// 加载配置。旧配置的迁移/规范化一旦产生差异就用安全写入落盘，之后重复加载不会再变；
        /// WC_NO_SAVE=1 时只在内存里规范化，不写真实配置。
        /// </summary>
        public SettingsLoadResult Load()
        {
            try
            {
                if (storage.FileExists(SettingsPath))
                {
                    string text = storage.ReadAllText(SettingsPath);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        Settings loaded = storage.Deserialize(text);
                        if (loaded != null)
                        {
                            loaded.Normalize();
                            bool changed = false;
                            try
                            {
                                string canonical = SettingsJson.Pretty(storage.Serialize(loaded));
                                changed = !string.Equals(canonical, text, StringComparison.Ordinal);
                            }
                            catch (Exception ex)
                            {
                                Log("比较配置快照失败: " + ex.GetType().Name);
                            }
                            // 规范化 / 迁移后的快照与磁盘不一致：落盘，保证下次加载不再重复生成 id 或迁移。
                            if (changed) Save(loaded);
                            return SettingsLoadResult.FromFile(loaded);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("读取设置失败: " + ex.GetType().Name + " / " + ex.Message);
                var fallback = new Settings();
                fallback.Normalize();
                return SettingsLoadResult.FromFallback(fallback, LoadFailureMessage);
            }
            var fresh = new Settings();
            fresh.Normalize();
            return SettingsLoadResult.FromDefaults(fresh);
        }

        /// <summary>
        /// 保存配置。调用者的 Settings 不会被修改；成功表示完整快照已落盘，失败时上一份有效配置保持不变。
        /// WC_NO_SAVE=1 返回 Skipped（不写文件），不能被当成保存成功。
        /// </summary>
        public SettingsSaveResult Save(Settings settings)
        {
            // 探针 / 截图程序只读配置：设了 WC_NO_SAVE=1 就不回写，免得把用户自己的设置冲掉
            if (string.Equals(Environment.GetEnvironmentVariable("WC_NO_SAVE"), "1", StringComparison.Ordinal))
                return SettingsSaveResult.Skip(SkipMessage);
            if (settings == null) return SettingsSaveResult.Failure(SaveFailureMessage);
            lock (SaveLock)
            {
                string tempPath = "";
                try
                {
                    // 先序列化再反序列化出一份独立快照，规范化只作用于快照。
                    string json = storage.Serialize(settings);
                    Settings snapshot = storage.Deserialize(json);
                    if (snapshot == null) return SettingsSaveResult.Failure(SaveFailureMessage);
                    snapshot.Normalize();
                    string pretty = SettingsJson.Pretty(storage.Serialize(snapshot));
                    tempPath = SettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
                    storage.WriteTemp(tempPath, pretty);
                    storage.Commit(tempPath, SettingsPath);
                    return SettingsSaveResult.Success();
                }
                catch (Exception ex)
                {
                    if (tempPath.Length > 0) { try { storage.Delete(tempPath); } catch { } }
                    Log("保存设置失败: " + ex.GetType().Name + " / " + ex.Message);
                    return SettingsSaveResult.Failure(SaveFailureMessage);
                }
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
    }

    /// <summary>配置 JSON 的稳定排版；SettingsStore 用它比较“磁盘上的快照”和“规范化后的快照”。</summary>
    internal static class SettingsJson
    {
        public static string Pretty(string json)
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
}
