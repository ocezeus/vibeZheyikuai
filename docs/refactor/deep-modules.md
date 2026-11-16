# 深模块归属与设计审视收尾（工单 #6）

> 父工单 #1 的验收材料。范围为分支 `refactor/deep-modules` 在 #2–#6 之后的实现归属整理与测试收敛；
> 基线为 `main` 上的文档提交 fde2587（源码即 v3.8.1 / 604f053 附近）。
> 术语按 `GLOSSARY.md`：班次、工作日、调休上班日、考勤记录、预计下班时间、下班倒计时、午休、
> 工资估算、日程、固定日程、日历来源、提醒、提前提醒、补提醒、关闭提醒、小纸条、对话、对话码、个人 ID、主题、界面风格。

## 0. 结论摘要

- 前序四个工单已经把提醒决策、同步生命周期、配置保存与配置变化应用收进各自模块；本工单
  **不再新建模块**，只做归属收尾：删除/合并确认过的 shallow 接口与转发，把业务断言移回模块接口，
  并补齐构建/测试/探针的枚举。
- 本工单实际删除：`ReminderFeedback`（关闭提醒的返回标志位，窗口从未读取）、
  `SyncSnapshot.AttendanceError`、`OutlookSettings.CachedCount`、`CalendarSourceConfig.HasSource`、
  `SettingsLoadResult.IsFallback`、`SettingsCommitResult.ShouldKeepEditing`、
  `ReminderEngine` 内部的 `BuildAgenda` 转发、`WidgetForm.ApplySettingsChange` 转发。
- 本工单新增一个真实 seam：`NoteRelay.AcceptPublish(topic, payload)`（中继主题 → 收件箱 / 对话码 / 在线状态），
  测试不再反射私有 `DeliverPresence`，并顺带覆盖主题路由与对话码串台拒绝。
- 测试替换：9 条旧的 `Reminders.IsDue / IsLateDue` 直接断言改由 `ReminderEngine.Advance` 的行为断言覆盖
  （逐条映射见 §4）；没有删除任何仍然独立有效的规则或 UI 回归断言。
- 后续定向补丁（#5 AC3）：`SyncEngine` 的配置代次从全局一个改为按 `attendance` / `lark-calendar` /
  `outlook` 三个范围各自维护；只改一个范围不再取消其它范围的在途结果（见 §6.4）。
- 最终基线：`Test` **556** 条 / `Check` **350** 条，全绿；`build.ps1` 生产编译通过；
  72 个探针入口逐个编译 **72 OK / 0 FAIL**。
- 倒计时、午休、调休、工资估算的样例输出整理前后 **逐字节一致**（47 行，见 §5）；
  在岗时长与计薪时长的三种口径差异作为事实记录，未统一（见 §6）。

## 1. 概念 → 状态拥有者 → 接口 → 行为测试

“状态拥有者”指该概念的可变状态只在这一处；纯计算模块没有可变状态，列在同一个表里并注明“纯”。

| 概念 | 状态拥有者 | 接口（调用约定） | 行为测试 |
| --- | --- | --- | --- |
| 班次、工作日、调休上班日 | `Policy`（纯计算） | `Policy.Build(settings, day)` / `NextWorkdayStart`：显式日期，返回 `DayPlan`，不读时钟 | `test/Test.cs:109-119`（周六调休 / 周三设为休息）、`test/Test.cs:104-107`（周六） |
| 考勤记录 → 预计下班时间、下班倒计时、卡片文案 | `StatusBuilder` + `DayPlan`（纯计算） | `StatusBuilder.Build(settings, snapshot, now, theme)`：显式 `now` 与考勤快照，返回 `CardStatus` | `test/Test.cs:78-107`（10:00 / 19:57 / 20:20 / 未打卡 / 上班前）、`test/Test.cs:160-205`（午休 / 晚休 / 常驻下班） |
| 午休 | 配置：`Settings.schedules`（唯一真相，`SettingsExtensions.NormalizeSchedules` 维护）；展示：`StatusBuilder.FillLunch`；计薪扣减：`SalaryCalc.BreakMinutes` | 三处各只做自己的关注点，不互相写状态 | `test/Test.cs:131-147`（扣午休）、`test/Test.cs:160-205`、`test/Test.cs:793-795` |
| 工资估算 | `SalaryCalc`（纯计算） | `SalaryCalc.Compute(settings, snapshot, now)`：显式 `now`，返回 `SalarySnapshot` | `test/Test.cs:768-830`（应出勤 / 日薪 / 时薪 / 今日 / 本月 / 发薪日 / 封顶 / 亚秒累计） |
| 提醒：提前提醒、补提醒、固定日程、常驻下班提醒、去重、关闭提醒 | `ReminderEngine`（`notified`、`mutedOffWorkDay`、`offWorkActive`、`offWorkNext`） | `Advance(settings, snapshot, now, signals)` 返回有序 `ReminderDecision`；`Dismiss(e, now)` 只改模块状态；`HoldUntil` / `Agenda` / `NextEvent` 只读 | `test/Test.cs:1279-1565`（20/21 全部用例：边界、去重、关闭、跨日清理） |
| 日程合并与日历来源过滤 | `ReminderEngine`（合并视图）+ `SyncEngine`（每份日历缓存） | `Agenda(settings, ReminderScheduleSnapshot)`：调用方不排序；`NextEvent` 直接给最早未来项 | `test/Test.cs:1291-1315`（乱序输入、来源关停）、`test/Test.cs:1575-1627`（启动缓存 / 来源过滤） |
| 同步生命周期：按范围代次、在途去重、有效结果发布、令牌轮换 | `SyncEngine`（`tickets`、每范围 `SyncScopeState.Version/Fingerprint`、`configRevision`、`calendarCaches`） | `Configure` / `RequestAttendance` / `RequestLarkCalendar` / `RequestOutlook` / `Snapshot` / `Published`；结果只与所属范围当前代次比较，过期不发布 | `test/Test.cs:1566-2520`（22.1–22.6，含 22.3a–22.3d 范围失效） |
| 外部考勤与日历读取 | `IAttendanceReader` / `ICalendarSourceReader` 的生产 adapter（`src/SyncAdapters.cs`）与测试 adapter（`test/SyncFakes.cs`） | 请求是快照，结果自带错误 / 缓存 / 受保护令牌，不写共享配置 | `test/Test.cs:2376-2457`（adapter 只读快照）、`test/Test.cs:2458-2520`（`CalendarSync.Fetch` 不写配置） |
| 配置加载、迁移、保存、导入导出 | `SettingsStore` + `ISettingsStorage`（生产 `FileSettingsStorage` / 测试 fake） | `Load` 返回 `SettingsLoadResult`；`Save` 只规范化独立快照并返回 `SettingsSaveResult` | `test/Test.cs:2521-2926`（23/24）、`test/Check.cs:4060-4095`（保存失败的用户可见反馈） |
| 配置变化应用 | `SettingsChangePlanner`（纯比较）+ `SettingsApplyCoordinator`（`applied` 快照与副作用顺序） | `Plan(previous, next, state)` → `SettingsChangePlan`；`Apply(next)` 只调有变化的 `ISettingsApplyTarget` 方法 | `test/Test.cs:2927-3323`（25.1–25.11）、`test/Check.cs:3986-4006`（重复应用不关闭面板） |
| 配置提交（保存成功才应用） | `SettingsCommitCoordinator` | `Commit(candidate, reason)` → `SettingsCommitResult`；失败 / `WC_NO_SAVE` 不应用 | `test/Test.cs:3178-3245`（25.9）、`test/Check.cs:4085` |
| 小纸条连接、对话 / 对话码、在线状态 | `NoteRelay`（连接、订阅、主题分发） | `Start/Stop`、`SendMessage`、`RefreshSubscriptions`、`AcceptPublish(topic, payload)` + `Message/Profile/ConnectedChanged` 事件 | `test/Test.cs:1046-1085`（在线状态 / 收件箱 / 对话码 / 无关主题） |
| 主题、界面风格、提醒配色 | `Themes` / `Skins` / `ReminderStyles`（静态表 + 纯函数） | 按 id 取配色；非法值回退 | `test/Test.cs:207-224`、`test/Test.cs:1091-1270` |
| 字体 | `UiFont`（进程级字体选择） | `Configure` / `Make` / `MakeNumber`；变化由 `SettingsChangePlan.FontsChanged` 触发 | `test/Test.cs:832+`、`test/Check.cs:3149-3169` |
| 模型与 JSON | `Settings` 及嵌套类型（数据）；`SettingsExtensions.Normalize`（迁移） | 所有入口先 `Normalize`，模型不自带 I/O | `test/Test.cs:733-782`（工作日迁移）、`test/Test.cs:2729-2780`（临时目录迁移） |

一句话归属：**窗口（`WidgetForm`、`SettingsForm`、`NotePanelForm`、`DrawerForm`、`ScreenReminderForm`）
只拥有绘制、动画、输入和界面线程调度**；规则与生命周期在 `ReminderEngine`、`SyncEngine`、
`SettingsStore`、`SettingsApplyPlan`、`NoteRelay` 与 `Policy/SalaryCalc/StatusBuilder/Reminders`。
窗口侧只剩“把决定映射成展示”的一段：`Widget.cs:737 ApplyReminderDecision`。

## 2. 六条设计审视标准：优化前后对照与证据

### 2.1 跨模块跳转

**优化前**：提醒决策要在 `WidgetForm` 里读 `CheckReminders`（fde2587:`src/Widget.cs:735`）、
`CheckBreaks`（fde2587:`src/Widget.cs:860`）、`CheckSticky`（fde2587:`src/Widget.cs:1245`）
和 `notified` 集合（fde2587:`src/Widget.cs:75`）才能拼出“这条提醒会不会响”；
同步请求还会在窗口线程里改配置（fde2587:`src/Widget.cs:696` → `CalendarSync.Poll`，
fde2587:`src/Models.cs:363` 直接写 `cached/lastSync/lastError`）。

**优化后**：
- 提醒的全部决策状态在 `src/ReminderEngine.cs:115 Advance`；窗口只按 `Reason` 走展示分支
  （`src/Widget.cs:725 AdvanceReminders` → `src/Widget.cs:737 ApplyReminderDecision`）。
- 同步的结果合并、来源过滤、按范围代次校验在 `src/SyncEngine.cs:558 Accept` 与
  `src/SyncEngine.cs:866 BuildSnapshot`；窗口只读 `SyncSnapshot`（`src/Widget.cs:615 OnSyncPublished`）。
- 配置应用不再由一个 52 行的 `ApplySettings`（fde2587:`src/Widget.cs:1594-1645`）一次性重建，
  而是 `SettingsChangePlanner.Plan`（`src/SettingsApplyPlan.cs:70`）比较、
  `SettingsApplyCoordinator.Apply`（`src/SettingsApplyPlan.cs:333`）排序。

**证据**：`test/Test.cs:1279-1565`（提醒，无需创建 Form）、`test/Test.cs:1566-2520`（同步）、
`test/Test.cs:2927-3323`（配置应用）。同样的验收在窗口层由 `test/Check.cs:936-1178`
（常驻下班提醒 / 常驻到时段结束）只验证“决定被应用成动画与像素”。

### 2.2 shallow 接口

对每个疑似接口做了 deletion test（“删掉它，知识会去哪里”），逐条记录在 §3。
结论：删除了 8 处确认的转发/冗余（见 §0），保留了 `Policy`、`SalaryCalc`、`StatusBuilder`、
`Reminders` 等“方法小但删了会把规则散回调用者”的纯计算。**没有以类数或行数作为完成指标**：
`Models.cs` 仍是数据模型 + 纯计算的归宿，不做机械分拆。

**证据**：`ReminderFeedback` 删除后，窗口的关闭路径仍只有
`src/Widget.cs:1117 DismissReminder` 一处；关闭语义由
`test/Test.cs:1493/1509/1516`（去重）与 `test/Test.cs:1496-1501`（忽略今天 / 次日恢复）经
`Advance` 行为验证。

### 2.3 纯函数调用处风险

保留纯函数并不等于没有风险，实际风险与处理：

| 纯函数 | 调用处 | 风险 | 现状 |
| --- | --- | --- | --- |
| `Policy.Build` | `StatusBuilder`、`SalaryCalc`、`ReminderEngine`、`Widget`（下次上班） | 调用者必须传已 `Normalize` 的 `Settings` 和显式日期；`Policy` 不读时钟 | 所有持久化入口（Load/Save/Import/Apply）都会 `Normalize`；测试固定日期 |
| `SalaryCalc.Compute` | `Widget` 工资面板 | 传入的 `AttendanceSnapshot` 必须是“今天”的；跨天/未来打卡的裁剪规则与 `StatusBuilder` 各有一份 | **有意保留**，口径差异记录在 §6，本工单不改 |
| `StatusBuilder.Build` | `Widget`、`Check` 探针 | 显示文案随 `now` 变化；测试必须固定 `now` | 测试全部显式传时间；`Check` 用固定状态或离屏绘制 |
| `Reminders.*`（解析 / 提前量 / 补提醒窗口 / 文案） | `ReminderEngine`、多个 UI 模块、`SettingsForm` | 时间解析失败返回 `MinValue`；调用者若忘记过滤会得到空日程 | `ReminderEngine.Agenda` 统一过滤无效项（`src/ReminderEngine.cs:308`） |
| `Policy.Hm/CompactDuration/Clock` | 卡片、抽屉、全屏、提醒 | 只是格式化；负数会取绝对值（既有行为） | 由 `test/Test.cs:156-159` 与样例输出钉住 |

`Reminders.IsDue / IsLateDue` 不再由窗口决定“要不要提醒”：窗口只消费 `ReminderEngine.Advance` 的
决定。测试里保留的直接断言只覆盖引擎用例未覆盖的取值（0 提前量、会议/日程各自的默认提前量，
见 §4.2）；窗口边界由 `test/Test.cs:1316-1349` 经模块接口覆盖。

### 2.4 seam 穿透

**优化前**：测试反射私有 `NoteRelay.DeliverPresence`（旧 `test/Test.cs`），`Check` 反射调用私有
`WidgetForm.ApplySettings`（旧 `test/Check.cs:1712/3165/3994/4000`），等于绕过模块接口改内部状态。

**优化后**：
- `NoteRelay.AcceptPublish`（`src/NoteRelay.cs:206`）成为“一条中继 PUBLISH 包进来”的正式入口；
  `test/Test.cs:1046-1085` 用它验证主题路由，不再反射。
- `WidgetForm.ApplySettings` 改为 `internal`（`src/Widget.cs:1453`），`Check` 直接调用
  （`test/Check.cs` 的置顶 / 字体 / 面板冒烟），不再反射；它调用的仍是生产同一个
  `SettingsApplyCoordinator`。
- 保留的反射只服务真实绘制/交互：设置 `scale/collapsed/status/remindEvent` 等界面状态、
  调 `OnPaint/OnMouseDown/OnMouseUp/StepReminder`、读命中区 `kind/rect/key`
  （`test/Check.cs`、`test/NoteClick.cs`、`test/verify/*`）。理由：这些不是业务状态，
  而是“屏幕上的像素与鼠标坐标”，换成公开接口反而要为测试造一套 UI API。

seam 清单（都同时有生产实现与测试实现/多个来源，不是为测试发明）：

| seam | 生产 | 测试 / 多来源 | 验证 |
| --- | --- | --- | --- |
| `ISettingsStorage` | `FileSettingsStorage` | `test/SettingsStoreFakes.cs` | 序列化 / 临时写 / 提交任一步失败 |
| `IAttendanceReader` | `LarkAttendanceReader` | `test/SyncFakes.cs` | 成功 / 失败 / 异常 |
| `ICalendarSourceReader` | `LiveCalendarSourceReader` | `test/SyncFakes.cs` | 每份日历独立成功 / 失败 |
| `ISyncScheduler` | `ThreadPoolSyncScheduler` | `test/SyncFakes.cs` 可控队列 | 乱序 / 并发 / 释放 |
| `ISyncDispatcher` | `ControlSyncDispatcher` | `ImmediateSyncDispatcher` | 界面线程投递失败 |
| `ISyncConfigSink` | `WidgetForm` | `test/SyncFakes.cs` 记录型 | 每份缓存 / 令牌回写 |
| `ISettingsApplyTarget` | `WidgetForm` | `test/SettingsApplyFakes.cs` | 只通知有变化的副作用 |

### 2.5 难测行为

**优化前**：提醒时机、5 分钟重播、跨日清理都挂在窗口计时器与成员字段上；同步请求在后台线程直接
改 `Settings`；保存失败只能靠真实磁盘故障观察。

**优化后**：
- 提醒：`Advance` 接收显式 `now` 与 `ReminderSignals`，返回可检查的 `ReminderOutcome`；
  5 分钟重播、跨日清理、300 条阈值都能在 `test/Test.cs:1447-1565` 直接推进时间验证。
- 同步：`SyncEngine` 注入调度器 / 投递器 / 时钟；`test/Test.cs:1713-1822` 用可控队列验证
  “A 在途时再请求同来源”“不同来源并发”“异常后清掉在途”，`test/Test.cs:1823-2005` 验证
  “请求 A 期间配置变化 → 旧结果不发布、不写缓存、不改令牌”，`test/Test.cs:2006-2297` 验证
  “只改一个范围只作废该范围，其它范围照常发布”。
- 配置：`SettingsApplyFakes` 记录每个副作用；`test/Test.cs:2927-3323` 验证“相同配置零副作用、
  外观只刷显示、字体只重建字体、保存失败不应用”。
- 仍难测：真实网络 / 真实飞书账号 / 真实 Outlook 登录 / 真实用户配置。本工单**没有**触碰这些，
  也没有宣称验证过；桌面像素与输入在 `Check` 里用离屏绘制 + 注入坐标完成。

### 2.6 deletion test

见 §3 的完整记录。核心判断：
- **删掉 `Policy` / `SalaryCalc`**：工作日 / 调休 / 应勤 / 计薪规则会散回 `StatusBuilder`、
  `SalaryCalc` 的调用者和 `ReminderEngine`，属于“小方法但有价值”，保留。
- **删掉 `ReminderEngine`**：去重、关闭、常驻下班提醒状态机回到窗口，约 400 行；
  `test/Test.cs:1279-1565` 将不得不创建 Form，保留。
- **删掉 `ReminderFeedback`**：窗口本来就没读它的返回，测试改为断言后续 `Advance` 行为，删除。
- **删掉 `SettingsCommitCoordinator`**：4 个生产调用点都要重新写“先保存、成功才应用、失败记日志”，
  保留。

## 3. 疑似 shallow 接口的 deletion test 记录

| 接口 / 符号 | 位置 | 删除/合并后会怎样 | 结论 |
| --- | --- | --- | --- |
| `ReminderFeedback`（`MarkedNotified` / `MutedOffWork`） | 原 `src/ReminderEngine.cs:81-88` | 窗口从未读取（`src/Widget.cs:1121` 直接丢弃返回值）；只有测试读标志位 | **删除**；`Dismiss` 改 `void`，测试改为断言下一次 `Advance` 的去重/静音行为（`test/Test.cs:1375-1383`、`1491-1516`） |
| `SyncSnapshot.AttendanceError` | 原 `src/SyncEngine.cs:116-119` | 只是 `Attendance == null ? null : Attendance.error`；唯一调用是测试 | **删除**；断言直接读 `Snapshot().Attendance.error`（`test/Test.cs:1802-1804`） |
| `ReminderEngine.BuildAgenda` 私有转发 | 原 `src/ReminderEngine.cs:117-120 / 323` | `Agenda` 只是转调，内部又直接调 `BuildAgenda`，两套入口 | **合并**：`Agenda` 即唯一实现（`src/ReminderEngine.cs:308`），`NextEvent/Advance` 直接调用 |
| `WidgetForm.ApplySettingsChange` | 原 `src/Widget.cs:1459-1463` | 单行转发 `apply.Apply(next)`；4 个调用点 | **删除**：调用点直接 `apply.Apply(...)`；保留 `internal ApplySettings()` 作为 UI 冒烟入口（`src/Widget.cs:1453`） |
| `CalendarSourceConfig.HasSource` | 原 `src/SyncEngine.cs:179-187` | 与 `CalendarSource.HasSource` 同一规则，且无调用者 | **删除**；`SyncConfig.Capture` 改用 `OutlookSettings.Active`（`src/SyncEngine.cs:228-239`） |
| `OutlookSettings.CachedCount` | 原 `src/Models.cs:308-317` | 无任何调用者 | **删除** |
| `SettingsLoadResult.IsFallback` | 原 `src/SettingsStore.cs:61` | 无调用者；与 `Status` 重复 | **删除** |
| `SettingsCommitResult.ShouldKeepEditing` | 原 `src/SettingsApplyPlan.cs:389` | 只等于 `!IsSuccess`，仅测试使用 | **删除**；测试直接断言 `!IsSuccess` |
| `Reminders`（静态纯计算） | `src/Models.cs:36` | 删除后：分类/解析/配色/文案散到 5 个 UI 模块，提前量窗口散回引擎 | **保留**：纯计算，测试见 `test/Test.cs:235-310` |
| `Reminders.CountdownText / TimeRange / KindOf / ColorIdOf` | `src/Models.cs:36-158` | 每个都只有一个语义，多处复用；删除会复制文案与分类规则 | **保留**（小方法是知识，不是转发） |
| `Policy` | `src/Models.cs:1976` | 工作日 / 调休 / 下次上班 / 目标下班散回三个调用者 | **保留** |
| `SalaryCalc` | `src/Models.cs:2103` | 应出勤 / 日薪 / 时薪 / 封顶 / 跨天裁剪散回工资面板 | **保留** |
| `StatusBuilder` | `src/Models.cs:2257` | 状态与文案矩阵散回窗口和探针 | **保留** |
| `SyncSnapshot`（整体） | `src/SyncEngine.cs:101` | 删掉后窗口要直接读引擎的可变字段，跨线程不安全 | **保留**：界面线程只读快照 |
| `SyncTicket` | `src/SyncEngine.cs:89` | 删掉后调用方拿不到“这次请求还在不在跑” | **保留**：只暴露 Id/Scope/Manual/ConfigVersion（所属范围代次）/RequestedAt/Cancelled |
| `SyncPublication` | `src/SyncEngine.cs:127` | 事件需要一次带出状态、错误变化与配置更新 | **保留** |
| `SyncEngine.ConfigFingerprint()` 与 `Fingerprint(Settings)` | `src/SyncEngine.cs:381 / 395` | 一个是“当前配置”，一个是“候选配置”；合并会让变化计划拿不到候选指纹 | **保留**（命名上区分当前/候选；契约仍是单个稳定字符串） |
| `SettingsStore` | `src/SettingsStore.cs:233` | `Load/Save/Import/Export` 与目录/日志/迁移集中；删除会散回 Program/Widget/SettingsForm | **保留**；`Exists` 被 `src/Program.cs:70` 使用，不是薄包装 |
| `SettingsApplyCoordinator` | `src/SettingsApplyPlan.cs:333` | 删掉后“上次已应用快照 + 副作用顺序”要回到窗口 | **保留** |
| `SettingsChangePlanner / SettingsChangePlan` | `src/SettingsApplyPlan.cs:68 / 24` | 删掉后字段比较与“哪些副作用”重新写进窗口 | **保留** |
| `SettingsCommitCoordinator` | `src/SettingsApplyPlan.cs:397` | 4 个调用点重复“保存成功才应用 + 失败日志” | **保留** |
| `NoteRelay` 的配置 provider（`Func<NoteSettings>`） | `src/NoteRelay.cs:69` | 删掉后换配置必须重建连接；测试只能用固定配置 | **保留**：生产读 `settings.note`，测试用常量重载 |
| `ISyncConfigSink` | `src/SyncEngine.cs:57` | 删掉后同步模块直接依赖 `WidgetForm` | **保留**：生产 Widget + 测试记录型 |
| `SettingsApplyState` | `src/SettingsApplyPlan.cs:10` | 删掉后计划无法知道计时器/连接是否真在跑 | **保留** |
| `ReminderReasons / SyncStatuses / SyncScopes` | `src/ReminderEngine.cs:7`、`src/SyncEngine.cs:17/9` | 字符串常量是模块与窗口的共同词汇；删掉会魔法字符串化 | **保留** |

## 4. 被替换 / 保留的旧测试清单

### 4.1 替换（先确认新模块测试覆盖同一行为，再删旧断言）

| 旧断言（原位置） | 覆盖它的模块接口测试 | 说明 |
| --- | --- | --- |
| “提前量到点触发” | `test/Test.cs:1317-1318` 提前量边界：差 1 秒不提醒、正点提醒 | 同一个窗口边界，经 `Advance` |
| “没到提醒时间” | 同上 | 同上 |
| “开始后 1 分钟内仍提醒” | `test/Test.cs:1321-1322` 开始后 59 秒仍提醒 | 同上 |
| “开始 1 分钟后过期” | `test/Test.cs:1323-1325` 满 60 秒转补提醒 | 同一边界，且同时验证原因 |
| “晚同步：开始 10 分钟后补提醒” | `test/Test.cs:1323-1325` + `1330-1332` | 补提醒窗口 |
| “正常提醒窗口内不算补提醒” | `test/Test.cs:1321-1322` | 59 秒不进入补提醒 |
| “会议结束后不补提醒” | `test/Test.cs:1333-1341` 已结束事项不再提醒 | 结束边界 |
| “没结束时间按 60 分钟兜底” | `test/Test.cs:1326-1332` 无结束时间 59 分补 | 60 分钟兜底 |
| “超过 60 分钟不补提醒” | `test/Test.cs:1326-1332` 满 60 分不补 | 同上 |
| 反射 `NoteRelay.DeliverPresence` 的在线状态断言 | `test/Test.cs:1046-1054` `AcceptPublish(PresenceTopic, PresenceJson)` | 不再反射私有方法，并覆盖主题路由 |
| 4 条 `ReminderFeedback` 标志位断言 | `test/Test.cs:1378/1380/1493/1509/1516` + `1496-1501` | 改断言“关闭之后下一次推进的行为” |

### 4.2 保留（仍提供独立规则或 UI 回归价值）

| 测试 | 位置 | 保留理由 |
| --- | --- | --- |
| `Reminders.LeadOf` 会议 / 日程 / 单条覆盖 | `test/Test.cs:287/299/306/308` | 提前量取值规则；模块测试只覆盖“单条覆盖”一个点 |
| “提前量为 0 也有窗口” | `test/Test.cs:249` | 引擎测试没有 0 提前量用例，属独立边界 |
| `Reminders.ParseStart/ParseEnd/TimeRange/IsMeeting/KindOf` | `test/Test.cs:235/302-310` | 纯解析与分类；5 个 UI 模块共用 |
| `StatusBuilder` 全部状态 / 倒计时 / 午休 / 晚休 | `test/Test.cs:78-205` | 产品文案矩阵，删除会让显示回归无网 |
| `Policy` 调休 / 休息 / 下次上班 | `test/Test.cs:104-119` | 考勤推算规则，独立于提醒 |
| `SalaryCalc` 全部 | `test/Test.cs:768-830` | 工资估算口径，属于必须保留的纯计算 |
| `Check` 的常驻下班提醒 / 常驻到时段结束 / × 关闭 | `test/Check.cs:936-1178` | 验证“决定被应用成动画与像素”，是窗口应用层，不是重复的决策测试 |
| `Check` 的同步晚于开始补提醒 / 关闭窗口不发布 | `test/Check.cs:2939/3034` | 真实窗口时序与释放路径 |
| `Check` 的保存失败提示 / 重复应用不重建面板 | `test/Check.cs:4068/4085/3996-4004` | 用户可见反馈与副作用隔离 |
| `test/NoteClick.cs`、`test/verify/NoteVerifyProbeV35.cs` | 全文件 | 真实点击 / 绘制探针，反射只用于命中区与私有绘制入口 |

**没有为了让编译通过或测试变绿而删除断言**：本工单所有删除都伴随上表的接口级替代，或属于删除的
shallow 接口（§3）。

## 5. 样例输出整理前后对照（倒计时 / 午休 / 调休 / 工资估算）

探针：`test/verify/SampleOutputs.cs`（可重复运行）。它在**整理前的 HEAD** 与**整理后**各跑一次，
输出共 47 行，`diff` 为 0（逐字节一致）。下表摘录关键行（左“整理前”= 右“整理后”）：

| 场景 | 输出 |
| --- | --- |
| 固定班次 09:00–18:00，09:05 打卡，10:00 | `Working / 工作中 / big=08:00:00 / sub=上班 09:05 · 预计下班 18:00 / extra=已工作 55m · 应勤 9h00m / lunch=Before[距午休\|1h50m]` |
| 12:00（午休中） | `... / lunch=During[午休中\|还剩 1h30m]` |
| 18:10（已过下班点，晚休中） | `Overtime / 已达标 / big=10:00.00 / lunch=EveningDuring[晚休中\|还剩 35m]` |
| 倒计时 14:55 / 14:59:40 / 15:00 / 15:00→17:00 | `还有 5 分钟` / `马上开始` / `正在进行` / `还有 2 小时` |
| 调休：2026-11-21（周六）设为上班 | `True / 周六 · 调休上班` |
| 休息：2026-11-18（周三）设为休息 | `False / 周三 · 休息` |
| 普通周六 2026-11-14 | `False / 周六` |
| 下次上班（从周六休息日 11-14 起算） | `2026-11-16 09:00` |
| 弹性 480 分钟、09:30 打卡、扣午休 | 预计下班 `19:10` |
| 固定 09:00–18:00 不扣午休 / 扣午休 | 预计下班 `18:00` / `19:40` |
| 工资：月薪 10000、发薪日 15、11-16 15:30 | `daily=476.190 / hourly=64.935 / requiredWorkdays=21 / elapsedWorkdays=10 / today=308.442 / month=5,070.346 / worked=4h45m / progress=0.6477` |
| 工资：未接飞书（按班次 09:00 起算，16:00） | `today=346.320 / month=5,108.225 / worked=5h20m` |
| 发薪日 | `paydayDate=2026-12-15 / daysToPayday=29` |
| 提醒常驻 | 午休 12:00 → `13:30`；下班提醒 → `MaxValue`；会议 19:00 → `21:00` |

说明：样例里的“固定班次扣午休 19:40”来自 `DayPlan.TargetEnd`（09:00 + 9h + 100 分钟午休）；
“工资 hourly=64.935”来自 `SalaryCalc` 的计薪时长（9h − 100 分钟午休 = 7h20m）。两者口径不同，
本工单只记录、不统一（见 §6）。

## 6. 有意保留的差异与剩余风险

### 6.1 在岗时长与工资计薪时长（事实记录，不统一）

同一组输入下三处口径不同：

1. **卡片显示的在岗时长**（`StatusBuilder`）：`now - clockIn`（Done 时 `clockOut - clockIn`），
   **不扣午休/晚休**。证据：`test/Test.cs:91` “20:20 工时 => 10h42m”（09:27→20:09）。
2. **工资计薪的工作时长**（`SalaryCalc.Compute`）：按秒累计并减去 `BreakMinutes`（午休 + 启用时的晚休），
   与 `Settings.lunchDeduct` 无关。证据：`test/Test.cs:793-795` “应勤时长扣午休 440 / 今日已工作扣午休 200”。
3. **考勤推算的预计下班时间**（`DayPlan.TargetEnd`）：仅当 `lunchDeduct == true` 时把午休重叠加回。
   同一天同一班次，`lunchDeduct` 开关会让“预计下班”不同，但不会改变工资计薪时长。

结论：这是既有产品口径，`GLOSSARY.md` 也把“下班倒计时（日历时间）”与“工资估算”列为不同概念。
本工单**没有**在岗时长与计薪时长的统一改造；若未来要统一，必须单独立项并给出新的验收样例。

### 6.2 其他已观察差异

- `StatusBuilder` 在未接飞书时把 `clockOut` 也置空；`SalaryCalc` 只在“未接飞书”时改写 `clockIn`，
  若快照仍带 `clockOut` 则会保留。两者只在“快照与配置不一致”的构造场景下不同，未改动。
- `StatusBuilder` 的“应勤”在固定班次下是 `fixedEnd - suggestStart`（样例 9h00m），`SalaryCalc.required`
  是计薪时长（7h20m）。这是“显示目标”和“计薪基准”的差异，同上。
- `Reminders.IsLateDue` 的 60 分钟上限从“开始时间”起算（不是从提醒窗口起算），与 `HoldUntil` 的
  30 分钟兜底不同；既有测试分别钉住，未合并。

### 6.3 剩余风险

- 真实飞书 / Outlook 账号、真实网络、真实用户配置未验证；本工单只用临时目录与可控 adapter。
- 桌面探针在本机（Windows，.NET Framework 4.x）通过；其它 DPI / 多屏 / 输入法场景未覆盖。
- `ReminderEngine` 的 300 条去重清理与跨日键依赖系统时钟跳变；只用注入时间测试，未验证真实
  休眠唤醒后的时钟回拨。
- `SyncEngine` 的线程调度使用生产线程池；测试用可控队列覆盖顺序，不覆盖真实线程饥饿。
- `SettingsStore.Log` 仍直接用 `File.AppendAllText`（不在 `ISettingsStorage` seam 内），日志写失败
  被吞掉；这是既有行为，未改动。
- `WidgetForm.ApplySettings()` 是 internal 的 UI 探针入口（生产不调用），为避免测试反射而保留；
  它不是业务接口，不应被生产代码使用。
- 配置代次已按考勤 / 飞书日程 / Outlook 三个范围失效（#5 AC3 补丁，见 §6.4），
  “一个范围变化取消全部在途请求”不再是剩余风险。

### 6.4 已收口：配置代次按范围失效（#5 AC3 后续补丁）

#5 交付时 `SyncEngine` 只有一个全局配置代次：任何同步指纹变化都会 `CancelAllTickets()`，
只改 Outlook 也会作废正在跑的考勤刷新。本补丁把代次改成按范围各自维护，`Configure` 只对
指纹变了的范围执行 `CancelScopeLocked`，`Accept` 只与请求所属范围的当前代次比较；
`SyncSnapshot.ConfigVersion` 仍是“任一范围变化即自增”的全局修订号，只供观测，不再用于判过期。

| 范围（`SyncScopes`） | 指纹字段（来自 adapter 请求快照） |
| --- | --- |
| `attendance` | `lark.enabled`（是否读考勤）、`lark.command`、`lark.chatId` |
| `lark-calendar` | `LarkCalendarEnabled`（`calendarEnabled && lark.enabled`）、`lark.command`、`lark.chatId` |
| `outlook` | `outlook.clientId` / `tenant` / `account` / `refreshToken` / `leadMinutes`，以及每份启用来源的 `id` / `name` / `kind`（`source`）/ `url` / `path` / `leadMinutes` |

飞书凭证与 `lark.enabled` 同时属于考勤与飞书日程两个范围；Outlook 字段只属于 Outlook 范围。
全局日历 / 日程默认提前量（`calendarLeadMinutes` / `eventLeadMinutes`）在提醒决策时读取，
不进入任何拉取请求快照，因此不会作废在途同步结果。

## 7. 构建、测试、打包与探针枚举

| 入口 | 之前 | 本工单后 |
| --- | --- | --- |
| `build.ps1` | 显式列出 23 个 src 文件（已含 5 个新模块） | 保持显式清单，新增“清单与 `src/*.cs` 不一致就失败”的守卫（已用临时文件验证会报错并退出 1） |
| `test/build-tests.ps1` | 用 `src/*.cs` 通配（新文件自动纳入）；只编 Test / Check | 不变；头部注明探针编译入口 |
| `test/build-probes.ps1` | 不存在 | **新增**：枚举 `test/*.cs` 与 `test/verify/*.cs` 中含 `static Main` 的入口，逐个 csc 到临时目录；全部 OK 才 exit 0 |
| `package.ps1` | 调用 `build.ps1`，不维护源码清单 | 不变（源码清单只在 build.ps1） |
| `tools/install-dev.ps1` | 调用 `build.ps1` | 不变 |
| `tools/release.ps1` | 只上传 `dist` 产物，不编译源码 | 不变 |

兼容约定未变：程序名仍是 `WorkCountdown.exe`；普通配置仍是 `%APPDATA%\WorkCountdown\settings.json`，
便携模式仍是 exe 旁 `portable.txt` + `data\`；配置字段、旧工作日迁移、导入导出语义与
`WC_NO_SAVE` 均未改动（`src/SettingsStore.cs` 的路径与常量、`src/Models.cs` 的 `Settings` 字段保持原样）。

验证结果：

- `test/build-tests.ps1`：`Test` 556 条 / `Check` 350 条，连续运行全绿（两套 ALL PASS，exit 0）。
  （#5 AC3 补丁前为 540 / 350；新增 16 条按范围失效与旧结果保护断言。）
- `build.ps1`：exit 0，产出 `dist/WorkCountdown.exe`。
- `test/build-probes.ps1`：**72 个入口 / 72 OK / 0 FAIL**，包括：
  - `test/*.cs` 下 69 个含 Main 的探针、
  - `test/verify/NoteVerifyProbe.cs`、`test/verify/NoteVerifyProbeV35.cs`，
  - 本工单新增的 `test/verify/SampleOutputs.cs`。
  - #4 机械改动的 28 个文件（27 个 `test/*.cs` + `test/verify/NoteVerifyProbeV35.cs`）全部编译通过。
  - 结论：#4 的 `Load() → Load().Settings` 机械适配**没有编译坏点**；此前的问题不是改坏了，而是
    这些探针**从未被任何入口编译**，所以没人发现它们能否编译。三个共享 fake（`SyncFakes` /
    `SettingsStoreFakes` / `SettingsApplyFakes`）没有 Main，不属于探针入口。

## 8. 复现命令

```powershell
# 两套正式测试（Test / Check）
.\test\build-tests.ps1

# 生产编译（含源码清单守卫）
.\build.ps1

# 70+ 个探针入口逐个编译
.\test\build-probes.ps1

# 样例输出（整理前后对照，输出到控制台）
#   临时目录编译 test\verify\SampleOutputs.cs + src（排除 Program.cs），运行 exe 即可
```

## 9. 提交

- `refactor(core): 按深模块整理实现归属并收敛行为测试 (#6)`
- `docs(refactor): 深模块归属与设计审视收尾 (#6)`
- `fix(sync): 配置代次按考勤/飞书/Outlook 范围失效 (#5 AC3)`
