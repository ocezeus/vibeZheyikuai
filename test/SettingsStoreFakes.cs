using System;
using System.IO;
using System.Threading;
using WorkCountdown;

// 配置存储的可控失败 adapter：只在测试里决定序列化 / 临时写 / 替换哪一步失败，
// 以及提交时是否停下来，让并发保存的串行化可以被观察。生产仍走 FileSettingsStorage。
internal sealed class ControlledSettingsStorage : ISettingsStorage
{
    private readonly FileSettingsStorage inner = new FileSettingsStorage();

    public string FailStage = null;         // null | "serialize" | "temp" | "commit"
    public string FailMessage = "controlled storage failure";
    public int SerializeCalls;
    public int TempWrites;
    public int Commits;
    public int Deletes;
    public int Reads;
    public int ActiveWrites;
    public int MaxActiveWrites;
    public ManualResetEvent BlockCommit = null;
    public ManualResetEvent CommitEntered = null;

    public void EnsureDirectory(string path) { inner.EnsureDirectory(path); }
    public bool FileExists(string path) { return inner.FileExists(path); }

    public string ReadAllText(string path)
    {
        Interlocked.Increment(ref Reads);
        return inner.ReadAllText(path);
    }

    public string Serialize(Settings settings)
    {
        Interlocked.Increment(ref SerializeCalls);
        if (FailStage == "serialize") throw new InvalidOperationException(FailMessage);
        return inner.Serialize(settings);
    }

    public Settings Deserialize(string text)
    {
        if (FailStage == "deserialize") throw new InvalidOperationException(FailMessage);
        return inner.Deserialize(text);
    }

    public void WriteTemp(string tempPath, string content)
    {
        Interlocked.Increment(ref TempWrites);
        MarkMax(Interlocked.Increment(ref ActiveWrites));
        try
        {
            if (FailStage == "temp")
            {
                // 模拟只写了一半的临时文件：调用方必须丢弃它，不能让它被当成配置
                inner.WriteTemp(tempPath, content.Length > 2 ? content.Substring(0, content.Length / 2) : content);
                throw new IOException(FailMessage);
            }
            inner.WriteTemp(tempPath, content);
        }
        catch
        {
            Interlocked.Decrement(ref ActiveWrites);
            throw;
        }
    }

    public void Commit(string tempPath, string path)
    {
        Interlocked.Increment(ref Commits);
        try
        {
            if (BlockCommit != null)
            {
                if (CommitEntered != null) CommitEntered.Set();
                if (!BlockCommit.WaitOne(5000)) throw new IOException("commit gate timeout");
            }
            if (FailStage == "commit") throw new IOException(FailMessage);
            inner.Commit(tempPath, path);
        }
        finally
        {
            Interlocked.Decrement(ref ActiveWrites);
        }
    }

    public void Delete(string path)
    {
        Interlocked.Increment(ref Deletes);
        inner.Delete(path);
    }

    private void MarkMax(int active)
    {
        int current = MaxActiveWrites;
        while (active > current)
        {
            int seen = Interlocked.CompareExchange(ref MaxActiveWrites, active, current);
            if (seen == current) break;
            current = seen;
        }
    }
}
