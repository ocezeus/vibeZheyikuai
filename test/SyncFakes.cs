using System;
using System.Collections.Generic;
using WorkCountdown;

// 同步模块的可控 adapter / 调度器：测试用它决定任务什么时候完成、返回成功还是失败，
// 不碰真实网络、账号或线程等待。
internal class ManualSyncScheduler : ISyncScheduler
{
    public readonly List<Action> Pending = new List<Action>();
    public int Queued;

    public void Queue(Action work)
    {
        Queued++;
        Pending.Add(work);
    }

    public void Run(int index)
    {
        Action work = Pending[index];
        Pending.RemoveAt(index);
        work();
    }

    public void RunAll()
    {
        while (Pending.Count > 0) Run(0);
    }
}

internal class ImmediateSyncDispatcher : ISyncDispatcher
{
    public int Posted;

    public bool TryPost(Action action)
    {
        Posted++;
        action();
        return true;
    }
}

internal class RecordingSyncConfigSink : ISyncConfigSink
{
    public readonly List<SyncConfigUpdate> Updates = new List<SyncConfigUpdate>();

    public void ApplySyncConfigUpdate(SyncConfigUpdate update)
    {
        Updates.Add(update);
    }
}

internal class ControlledAttendanceReader : IAttendanceReader
{
    public readonly List<AttendanceReadRequest> Requests = new List<AttendanceReadRequest>();
    public Func<AttendanceReadRequest, AttendanceSnapshot> Respond = null;
    public int Calls;

    public AttendanceSnapshot Read(AttendanceReadRequest request)
    {
        Calls++;
        Requests.Add(request);
        if (Respond == null) throw new Exception("no attendance response");
        return Respond(request);
    }
}

internal class ControlledCalendarReader : ICalendarSourceReader
{
    public readonly List<CalendarFetchRequest> Requests = new List<CalendarFetchRequest>();
    public Func<CalendarFetchRequest, CalendarFetchResult> Respond = null;
    public int Calls;

    public CalendarFetchResult Read(CalendarFetchRequest request)
    {
        Calls++;
        Requests.Add(request);
        if (Respond == null) throw new Exception("no calendar response");
        return Respond(request);
    }
}

internal static class SyncTestKit
{
    public static ReminderEvent Ev(string id, string title, string start, string source)
    {
        var e = new ReminderEvent();
        e.id = id;
        e.title = title;
        e.start = start;
        e.end = start;
        e.source = source;
        return e;
    }

    public static AttendanceSnapshot Attendance(DateTime clockIn)
    {
        var s = new AttendanceSnapshot();
        s.ok = true;
        s.clockIn = clockIn;
        return s;
    }

    public static AttendanceSnapshot AttendanceFailed(string error)
    {
        var s = new AttendanceSnapshot();
        s.ok = false;
        s.error = error;
        return s;
    }

    public static CalendarFetchResult CalendarOk(string sourceId, params ReminderEvent[] events)
    {
        var r = new CalendarFetchResult();
        r.SourceId = sourceId;
        r.SourceName = sourceId;
        r.Events = new List<ReminderEvent>(events);
        return r;
    }

    public static CalendarFetchResult CalendarFail(string sourceId, string error)
    {
        var r = new CalendarFetchResult();
        r.SourceId = sourceId;
        r.SourceName = sourceId;
        r.Error = error;
        return r;
    }
}
