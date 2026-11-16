using System;
using WorkCountdown;

// 配置变化计划的调用记录 adapter：业务测试只断言“哪些模块被通知了几次”，
// 不读 Widget 私有字段，也不需要真的启动计时器或连接网络。
internal sealed class RecordingSettingsApplyTarget : ISettingsApplyTarget
{
    public readonly SettingsApplyState State = new SettingsApplyState();
    public SyncEngine Engine { get; set; }
    public Settings Adopted;
    public int AdoptCalls;
    public int SyncCalls;
    public int AttendancePollCalls;
    public int CalendarPollCalls;
    public int OutlookPollCalls;
    public int FontCalls;
    public int DisplayCalls;
    public int TopMostCalls;
    public int RelayRestartCalls;
    public int RelayRefreshCalls;
    public int NotePanelCalls;
    public int NotePanelRelayoutCalls;
    public int SalaryCalls;
    public int AfterCalls;

    public void ResetCounts()
    {
        AdoptCalls = 0;
        SyncCalls = 0;
        AttendancePollCalls = 0;
        CalendarPollCalls = 0;
        OutlookPollCalls = 0;
        FontCalls = 0;
        DisplayCalls = 0;
        TopMostCalls = 0;
        RelayRestartCalls = 0;
        RelayRefreshCalls = 0;
        NotePanelCalls = 0;
        NotePanelRelayoutCalls = 0;
        SalaryCalls = 0;
        AfterCalls = 0;
    }

    public string Counts()
    {
        return "sync=" + SyncCalls + " attendance=" + AttendancePollCalls
            + " calendar=" + CalendarPollCalls + " outlook=" + OutlookPollCalls
            + " fonts=" + FontCalls + " display=" + DisplayCalls
            + " relayRestart=" + RelayRestartCalls + " relayRefresh=" + RelayRefreshCalls
            + " panel=" + NotePanelCalls + " panelRelayout=" + NotePanelRelayoutCalls
            + " salary=" + SalaryCalls + " after=" + AfterCalls;
    }

    public SettingsApplyState CaptureState()
    {
        var copy = new SettingsApplyState();
        copy.AttendancePollRunning = State.AttendancePollRunning;
        copy.CalendarPollRunning = State.CalendarPollRunning;
        copy.OutlookPollRunning = State.OutlookPollRunning;
        copy.NoteRelayRunning = State.NoteRelayRunning;
        copy.SyncFingerprint = State.SyncFingerprint;
        return copy;
    }

    public void AdoptSettings(Settings settings) { AdoptCalls++; Adopted = settings; }
    public void ConfigureSync(Settings settings)
    {
        SyncCalls++;
        if (Engine != null) Engine.Configure(settings);
        State.SyncFingerprint = SyncEngine.Fingerprint(settings);
    }
    public void ApplyAttendancePoll(Settings settings) { AttendancePollCalls++; State.AttendancePollRunning = settings.lark.enabled; }
    public void ApplyCalendarPoll(Settings settings) { CalendarPollCalls++; State.CalendarPollRunning = settings.calendarEnabled && settings.lark.enabled; }
    public void ApplyOutlookPoll(Settings settings) { OutlookPollCalls++; State.OutlookPollRunning = settings.outlook != null && settings.outlook.HasEnabled; }
    public void RestartNoteRelay(Settings settings) { RelayRestartCalls++; State.NoteRelayRunning = true; }
    public void RefreshNoteRelay(Settings settings) { RelayRefreshCalls++; }
    public void ApplyFonts(Settings settings) { FontCalls++; }
    public void ApplyDisplayResources(Settings settings) { DisplayCalls++; }
    public void ApplyTopMost(Settings settings) { TopMostCalls++; }
    public void ApplyNotePanel(Settings settings, bool relayout)
    {
        if (relayout) NotePanelRelayoutCalls++;
        else NotePanelCalls++;
    }
    public void ApplySalaryState(Settings settings) { SalaryCalls++; }
    public void AfterApply(Settings settings)
    {
        AfterCalls++;
    }
}
