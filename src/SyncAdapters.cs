using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace WorkCountdown
{
    /// <summary>生产考勤 adapter：只在后台读一份请求快照，不写共享 Settings。</summary>
    public class LarkAttendanceReader : IAttendanceReader
    {
        private readonly LarkClient lark;

        public LarkAttendanceReader(LarkClient lark)
        {
            this.lark = lark;
        }

        public AttendanceSnapshot Read(AttendanceReadRequest request)
        {
            var cfg = new LarkSettings();
            cfg.enabled = true;
            cfg.command = request == null ? "" : request.Command;
            cfg.chatId = request == null ? "" : request.ChatId;
            return lark.ReadToday(cfg);
        }
    }

    /// <summary>生产日历 adapter：飞书、.ics 文件/订阅与 Graph 的差异都留在实现内部。</summary>
    public class LiveCalendarSourceReader : ICalendarSourceReader
    {
        private readonly LarkClient lark;

        public LiveCalendarSourceReader(LarkClient lark)
        {
            this.lark = lark;
        }

        public CalendarFetchResult Read(CalendarFetchRequest request)
        {
            if (request == null)
            {
                var missing = new CalendarFetchResult();
                missing.Error = "读取失败：没有同步请求";
                return missing;
            }
            if (request.Kind == "lark")
            {
                var cfg = new LarkSettings();
                cfg.enabled = true;
                cfg.command = request.LarkCommand ?? "";
                cfg.chatId = request.LarkChatId ?? "";
                string error;
                var list = lark.ReadCalendar(cfg, request.From, request.To, out error);
                var result = new CalendarFetchResult();
                result.SourceId = request.SourceId;
                result.SourceName = request.SourceName;
                if (error != null)
                {
                    result.Error = error;
                }
                else
                {
                    result.Events = list == null ? new List<ReminderEvent>() : list;
                }
                return result;
            }
            return CalendarSync.Fetch(request);
        }
    }

    /// <summary>生产调度：每个同步请求一个线程池工作项。</summary>
    public class ThreadPoolSyncScheduler : ISyncScheduler
    {
        public void Queue(Action work)
        {
            if (work == null) return;
            ThreadPool.QueueUserWorkItem(delegate { work(); });
        }
    }

    /// <summary>生产投递：结果回到窗口所在界面线程；窗口已释放时返回 false。</summary>
    public class ControlSyncDispatcher : ISyncDispatcher
    {
        private readonly Control control;

        public ControlSyncDispatcher(Control control)
        {
            this.control = control;
        }

        public bool TryPost(Action action)
        {
            if (action == null) return false;
            if (control == null || control.IsDisposed || !control.IsHandleCreated) return false;
            try
            {
                control.BeginInvoke((MethodInvoker)delegate { action(); });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
