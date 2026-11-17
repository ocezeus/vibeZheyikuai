# iOS 实时活动（Live Activity）技术事实核查

> 核查时间：**2026-11-16**。本文只给事实与可复制骨架，不含 UI 设计建议。
> 来源优先级：Apple 官方文档（developer.apple.com）＞ WWDC session 官方逐字稿 ＞ Apple 支持页面。第三方博客未作为任何结论的唯一依据。凡检索不到官方依据的，一律写「**未能确认（截至检索时）**」，不编造。
> 说明：文档中的「当前」以本次抓取的官方页面为准；Apple 文档页版权年份已显示 2026，可视为最新。

## 0. 结论速览（供方案引用）

| 问题 | 事实结论 | 来源 |
|---|---|---|
| 最新正式版 iOS | **iOS 27.2.1（2026-11-09 发布）；iOS 27 于 2026-09-14 发布**；iOS 26.7.x 仍在维护老机型 | [来源](https://support.apple.com/en-us/100100) |
| iOS 27 的 LA 新变化 | **灵动岛在竖屏 + 横屏都显示**；新增 `isDynamicIslandLimitedInWidth`（iOS 27.0）与锁屏卡宽度受限布局；HIG 2026-10-13 更新了规格表 | [来源](https://developer.apple.com/videos/play/wwdc2026/223/) [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) |
| 展示时长 | **活动态最多 8 小时**（超时系统自动结束并立刻从灵动岛移除），**结束后锁屏最多再留 4 小时**，锁屏总时长上限 **12 小时** | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 数据体积 | **静态 + 动态数据合计 ≤ 4 KB**（含 attributes、content-state、推送 payload）；APNs 请求体本身也 ≤ 4 KB | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) [来源](https://developer.apple.com/documentation/usernotifications/sending-notification-requests-to-apns) |
| 免更新倒计时 | **`Text(timerInterval:…)` / `ProgressView(timerInterval:…)` 由系统渲染，无需推送、无需 App 运行**，精度到秒、无厘秒 | [来源](https://developer.apple.com/documentation/widgetkit/displaying-dynamic-dates) [来源](<https://developer.apple.com/documentation/swiftui/text/init(timerinterval:pausetime:countsdown:showshours:)>) |
| 推送频率 | 官方只说「**每小时有预算，按设备状态决定；超预算会被节流**」，**未公开具体数值**；`apns-priority: 5` 不计入预算，`10` 计入 | [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| 免安装 | **不可能**：LA 由 App 内嵌 widget extension 提供，必须装 App；push-to-start 也要求设备上已有该 App 并上报 token | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 桌面常驻 | **LA 不能常驻桌面/主屏**；桌面常驻是普通 Widget（刷新预算约 40–70 次/天），不能秒级 | [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) [来源](https://developer.apple.com/documentation/widgetkit/keeping-a-widget-up-to-date) |

---

## 1. 版本与关键变更节点（截至 2026-11-16）

**当前最新正式版是 iOS 27.2.1**（2026-11-09）；iOS 27 正式版 2026-09-14 发布，iOS 27.2 于 2026-10-26 发布；老机型仍在收 iOS 26.7.3 安全更新。**iOS 27 已不是「未来版本」，是当前版本。** [来源](https://support.apple.com/en-us/100100)

| 版本 | Live Activity 相关变化 | 核实状态 / 来源 |
|---|---|---|
| iOS 16.1 | ActivityKit 与 Live Activity、灵动岛首次可用；`ActivityConfiguration`、`AlertConfiguration`、`areActivitiesEnabled` 均自 16.1 | 已核实 [来源](https://developer.apple.com/documentation/activitykit) |
| iOS 16.2 | `Activity.request(attributes:content:pushType:)`、`update(_:alertConfiguration:)`、`end(_:dismissalPolicy:)`、`ActivityContent`、`frequentPushesEnabled` 自 16.2（**注意：不是 16.1**） | 已核实（文档 metadata introducedAt）[来源](<https://developer.apple.com/documentation/activitykit/activity/request(attributes:content:pushtype:)>) |
| iOS 17.0 | 交互按钮/开关（`Button(intent:)`、`Toggle(isOn:intent:)`）需要 `LiveActivityIntent`（iOS 17.0） | 已核实 [来源](https://developer.apple.com/documentation/appintents/liveactivityintent) |
| iOS 17.2 | **push-to-start**：`Activity.pushToStartTokenUpdates`（iOS 17.2）；`NSSupportsLiveActivitiesFrequentUpdates` 高频推送 | 已核实 [来源](https://developer.apple.com/documentation/activitykit/activity/pushtostarttokenupdates) |
| iOS 18.0 | **broadcast/channel**：`PushType.channel(_:)`（iOS 18.0）、`apns-channel-id`、payload 里的 `input-push-channel` / `input-push-token`；**不能用广播推送启动 LA** | 已核实 [来源](https://developer.apple.com/documentation/activitykit/pushtype) [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| iOS 26.0 | **定时启动**：`request(attributes:content:pushType:style:alertConfiguration:startDate:)`（iOS 26.0）；同时 LA 自动出现在 Mac 菜单栏与 CarPlay | 已核实 [来源](https://developer.apple.com/documentation/updates/activitykit) |
| iOS 27.0 | **灵动岛支持横屏**（compact/minimal 在竖屏与横屏均可见）；新增 `isDynamicIslandLimitedInWidth`（iOS 27.0），HIG 新增「limited width 28×28」规格 | 已核实 [来源](https://developer.apple.com/videos/play/wwdc2026/223/) [来源](<https://developer.apple.com/documentation/swiftui/environmentvalues/isdynamicislandlimitedinwidth>) |

**需要纠正任务书里的两个假设**：
- **`activityBackgroundTint` 不是 iOS 18 特性**，文档标注 iOS 16.0；`activitySystemActionForegroundColor` 为 iOS 16.1。 [来源](https://developer.apple.com/documentation/swiftui/view/activitybackgroundtint(_:))
- **broadcast channel 是 iOS 18 特性**（`PushType.channel(_:)` iOS 18.0），这点正确。 [来源](https://developer.apple.com/documentation/activitykit/pushtype)

**未能确认**：iOS 27 是否有除横屏灵动岛、limited-width 之外的 LA 专属新 API（WWDC26 只有 223 一场 LA 专题，逐字稿未提其它新 API）。 [来源](https://developer.apple.com/videos/play/wwdc2026/223/)

---

## 2. 生命周期：能活多久、怎么结束、结束后算什么

| 阶段 | 事实 | 来源 |
|---|---|---|
| 活动态 | **最长 8 小时**；到时系统自动结束并**立即从灵动岛移除**。App 或用户可提前结束。 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 结束后 | 默认在锁屏（以及 Mac 菜单栏、Watch Smart Stack）**最多再显示 4 小时**，然后被系统移除；用户可随时手动移除。**锁屏总计最多 12 小时**。 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 结束方式 | `dismissalPolicy` 三种：`.default`（结束后最多 4h 自动移除）、`.immediate`（立即移除）、`.after(Date)`（指定时间，但**上限仍是结束后 4 小时**，取二者更早者）。 | [来源](<https://developer.apple.com/documentation/activitykit/activityuidismissalpolicy/after(_:)>) [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 推送结束 | payload 里 `event: "end"` 必须带最终 `content-state`；可用 `dismissal-date` 覆盖，**传过去时间戳＝立即消失**，也可传 4 小时内的任意时间。 | [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| 结束后转普通通知 | **系统没有「LA 结束 → 自动变普通通知」的机制**（ActivityKit 文档未提供该转换）。要留一条普通通知必须**另外**发一条常规 `apns-push-type: alert` 推送。 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities)（未提供机制） |
| 状态机 | `ActivityState`：`active` / `stale` / `ended` / `dismissed`；用户手动移除或系统移除后为 `dismissed`。 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |

**补充事实**：允许同时存在多个 LA，但**具体上限未公开**，且「已调度未开始」的活动也计入上限；启动可能因超限失败，必须处理错误。 [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities)

---

## 3. 布局尺寸与可用区域（HIG 2026-10-13 版规格表）

**所有数值单位为 point。** 来源统一为 [HIG Live Activities](https://developer.apple.com/design/human-interface-guidelines/live-activities)；下列表格为该页 Specifications 原表。

| 屏幕（竖屏） | Compact（leading / trailing 各自） | Minimal（宽为范围） | Expanded（高为范围） | 锁屏卡（高为范围） | Limited width |
|---|---|---|---|---|---|
| 430×932 | 62.33×36.67 | 36.67–45 × 36.67 | 408 × 84–160 | 408 × 84–160 | 28×28 |
| 393×852 | 52.33×36.67 | 36.67–45 × 36.67 | 371 × 84–160 | 371 × 84–160 | 28×28 |
| iPad 1366×1024 | — | — | — | 500 × 84–160 | — |
| iPad 1194/1012/1080/1024 宽 | — | — | — | 425 × 84–160 | — |

要点（均已核实）：

- **锁屏卡宽度 = 屏宽 − 22pt**（430→408、393→371），左右各 11pt；**高度 84–160pt，超过 160pt 会被系统截断**。 [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities)
- **锁屏标准内容边距 14pt**（与通知对齐）；这是内容 margin，卡片本身的圆角/外框由系统画。 [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) [来源](https://developer.apple.com/videos/play/wwdc2023/10194/)
- **灵动岛四个区域的实际 API**：expanded 分区为 `.leading`（相机左侧）、`.trailing`（相机右侧）、`.center`（**相机正下方**）、`.bottom`；可用 `priority:` 争抢整宽，leading/trailing 太宽时用 `.belowIfTooWide` 折到相机下方。**各分区具体宽高由系统按内容与优先级计算，Apple 未公开固定值**。 [来源](https://developer.apple.com/documentation/widgetkit/dynamicislandexpandedregion)
- **expanded 最大高度 = 160pt**（同规格表 84–160），宽度 = 屏幕宽（408 / 371）。 [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities)
- **limited width（iOS 27 新增，横屏/双机/多活动）**：compact 与 minimal 必须能塞进 **直径 28pt 的圆**；用 `@Environment(\.isDynamicIslandLimitedInWidth)` 切换简化版视图，trailing 可留空由系统隐藏。 [来源](<https://developer.apple.com/documentation/swiftui/environmentvalues/isdynamicislandlimitedinwidth>) [来源](https://developer.apple.com/videos/play/wwdc2026/223/)
- **背景**：灵动岛内**背景固定为黑色**，**不能改背景色/圆角/尺寸**；只能改文字/图形颜色与 `keylineTint`（深色模式下的描边色）。 [来源](https://developer.apple.com/documentation/activitykit/creating-custom-views-for-live-activities)
- **锁屏背景**：默认 Light 模式纯白、Dark 模式纯黑；可用 `.activityBackgroundTint(_:)` 自定义（建议配不透明度），再用 `.activitySystemActionForegroundColor(_:)` 调「结束按钮」文字色。 [来源](https://developer.apple.com/documentation/activitykit/creating-custom-views-for-live-activities)
- **圆角**：**系统未公开具体数值，也禁止硬编码**；Apple 要求用 `ContainerRelativeShape` 让内部元素跟随系统圆角。 [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities)
- **StandBy**：锁屏卡被系统放大 **2×** 铺满屏；可用 `isActivityFullscreen` 环境值区分；StandBy Night Mode 系统加红色滤镜。 [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities)
- **StandBy 全屏 ≠ 自定义全屏覆盖**：这是系统对锁屏卡的放大行为，App 不能控制尺寸或位置。 [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities)
- **内容 margin 可调**（仅 expanded 等 mode）：`.contentMargins(.trailing, 8, for: .expanded)`；默认系统 margin 具体数值未公开。 [来源](https://developer.apple.com/documentation/activitykit/creating-custom-views-for-live-activities)

---

## 4. 零更新倒计时：系统 timer 是 Live Activity 的关键能力

| API | 事实 | 来源 |
|---|---|---|
| `Text(timerInterval:pauseTime:countsDown:showsHours:)`（iOS 16.0+） | **系统自己按秒刷新文本，不需要 App 运行、不需要推送、不需要 timeline reload**；显示 `分:秒`，超过 60 分钟时显示 `时:分:秒`（`showsHours` 控制是否显示小时位）；`pauseTime` 可让计时暂停。**无厘秒/毫秒参数，精度到秒。** | [来源](<https://developer.apple.com/documentation/swiftui/text/init(timerinterval:pausetime:countsdown:showshours:)>) [来源](https://developer.apple.com/documentation/widgetkit/displaying-dynamic-dates) |
| `ProgressView(timerInterval:countsDown:label:)`（iOS 16.0+） | 系统按时间自动推进的连续进度条，**同样无需刷新**；**不支持自定义 style**（文档明确 date-relative 进度视图不支持 custom styles）。 | [来源](<https://developer.apple.com/documentation/swiftui/progressview/init(timerinterval:countsdown:label:)>) [来源](https://developer.apple.com/documentation/widgetkit/displaying-dynamic-dates) |
| 视图刷新机制 | LA 界面由 widget extension 渲染成快照；**推送会唤醒 extension 重渲染**，但 timer 文本不进这个流程。 | [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| 动画限制 | 动画**最长 2 秒**；iOS 16 上 LA 忽略 `withAnimation`/`animation(_:value:)` 等修饰符，用系统时序；可用 `.contentTransition(.numericText(countsDown:))` 让倒计时数字滚动。 | [来源](https://developer.apple.com/documentation/widgetkit/animating-data-updates-in-widgets-and-live-activities) |
| Always-On Display | **AOD 下系统以降低亮度渲染、不播放任何动画**；可用 `isLuminanceReduced` 检测并按需降级配色/描边。**AOD 的具体刷新频率 Apple 未公开**；秒位在 AOD 是否继续跳动**未能确认（截至检索时，需真机验证）**。 | [来源](https://developer.apple.com/documentation/widgetkit/animating-data-updates-in-widgets-and-live-activities) [来源](https://developer.apple.com/design/human-interface-guidelines/always-on) [来源](<https://developer.apple.com/documentation/swiftui/environmentvalues/isluminancereduced>) |

**对倒计时场景的结论**：**「距离下班 05:30:56」这种秒级倒计时在锁屏/灵动岛上可以零推送实现**（把下班时间点放进 ContentState，用 timer 文本渲染）。**但 AOD 状态下秒级可读性不可保证**，且系统不允许自绘逐帧动画。 [来源](https://developer.apple.com/documentation/widgetkit/displaying-dynamic-dates)

---

## 5. 更新通道：本地 update 的条件 + APNs 推送完整要求

### 5.1 本地更新（ActivityKit，不走网络）

| 场景 | 能否更新/结束 | 来源 |
|---|---|---|
| App 在前台 | **能**（`Activity.request` 启动也要求前台） | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| App 在后台（如 Background Tasks 运行中） | **能** `update` / `end` | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| App 被挂起/被杀 | **不能跑代码，因此不能本地 update**；只能靠 APNs 推送，或靠已排定的系统 timer 文本自行走 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 后台启动 LA | **仅能通过 `LiveActivityIntent`（AppIntent）在后台启动**，普通代码不行 | [来源](https://developer.apple.com/documentation/appintents/liveactivityintent) |
| 提前排定启动时间 | `request(...startDate:)`（iOS 26.0+），必须同时给 `AlertConfiguration` | [来源](https://developer.apple.com/documentation/updates/activitykit) |
| LA 内取数据 | **LA 沙盒不能访问网络、不能收定位更新**——数据只能由 App 写入或服务器推送 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |

### 5.2 APNs 推送 LA 的完整要求（已逐项核实）

| 项 | 值 / 要求 | 来源 |
|---|---|---|
| 协议/端点 | **HTTP/2 + TLS 1.2+**；生产 `https://api.push.apple.com:443`，开发 `https://api.sandbox.push.apple.com:443`；也可用 2197 端口。路径 **`/3/device/<push token>`**（token 为 64 hex，勿假设长度） | [来源](https://developer.apple.com/documentation/usernotifications/sending-notification-requests-to-apns) |
| `:method` | `POST` | 同上 |
| `authorization` | `bearer <JWT>`（token-based 鉴权） | [来源](https://developer.apple.com/documentation/usernotifications/establishing-a-token-based-connection-to-apns) |
| `apns-push-type` | **必须是 `liveactivity`** | [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| `apns-topic` | **`<bundleID>.push-type.liveactivity`**（后缀固定） | 同上 |
| `apns-channel-id` | 走 broadcast/channel 更新时用它替代 `apns-topic` | 同上 |
| `apns-priority` | **只允许 `5` 或 `10`**；`5` = 低优先级、**不计入预算**；`10` = 立即投递、**计入预算**；不填默认按 `10` 处理并计入预算 | 同上 |
| `apns-id` / `apns-expiration` | 可选。`apns-expiration: 0` 表示只投一次不存储 | [来源](https://developer.apple.com/documentation/usernotifications/sending-notification-requests-to-apns) |
| payload 上限 | **4 KB（4096 字节）**；LA 的「静态 + 动态数据合计」也是 **4 KB**（attributes、content-state、推送内容都算在内） | [来源](https://developer.apple.com/documentation/usernotifications/sending-notification-requests-to-apns) [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| JSON 编码 | `content-state` 必须能被 **Swift 默认 Codable 策略**解码；**禁用自定义 JSON 编码策略**，否则更新失败 | [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |

**`aps` 字典字段**（全部已核实）：

| 字段 | 说明 |
|---|---|
| `timestamp` | Unix 秒（1970 起）。**每次 update/end 都建议带**，系统据此只显示最新一条更新 [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| `event` | `"start"` / `"update"` / `"end"` 三选一 |
| `content-state` | 必须与 Swift `ContentState` 的属性名一一对应 |
| `attributes-type` + `attributes` | **仅 `start` 需要**，名称必须等于 `ActivityAttributes` 结构体名 |
| `alert` | `start` **必填**（避免静默启动）；`update` 可选（会点亮屏幕/展开灵动岛）；`end` 可选。结构为 `{title, body, sound}`，支持 `loc-key`/`loc-args` 本地化 |
| `stale-date` | Unix 秒，到点后 `activityState` 变 `.stale`、`context.isStale == true` |
| `dismissal-date` | Unix 秒，仅 `end` 用；过去值＝立即从锁屏消失；最大窗口 4 小时 |
| `relevance-score` | 相对数值（如 100 vs 50），决定锁屏排序与哪个 LA 进灵动岛 |
| `input-push-channel` | iOS 18+；用 channel 订阅后续更新（`start` 用） |
| `input-push-token` | iOS 18+；`1` 表示启动后回传新 update token（`start` 用） |

**鉴权（JWT / p8）**：ES256、header `{alg:"ES256", kid:"<10位Key ID>"}`、claims `{iss:"<10位Team ID>", iat:<Unix秒>}`；`iat` 超过 1 小时会被 APNs 以 `ExpiredProviderToken(403)` 拒绝；**建议 20–60 分钟刷新一次 token**（同连接上新 token 不得早于 20 分钟）。p8 私钥**必须保密**，泄露需立即吊销。 [来源](https://developer.apple.com/documentation/usernotifications/establishing-a-token-based-connection-to-apns)

---

## 6. 频率与预算：官方口径 vs 未公开数值

- **官方口径（唯一权威表述）**：系统对 ActivityKit 推送有「**每小时一定预算**」，预算大小**取决于设备状态**；超预算会被系统节流（throttle），效果是更新延迟或丢失。 [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) [来源](https://developer.apple.com/videos/play/wwdc2023/10185/)
- **`apns-priority: 5` 的更新不计入该预算**，官方建议「优先用 5，只在关键更新用 10」以降低被节流概率。 [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications)
- **frequent push（高频推送）**：在 Info.plist 加 **`NSSupportsLiveActivitiesFrequentUpdates = YES`**（文档标注 iOS 16.2+，WWDC23 作为 17.2 特性介绍）可拿到**更高预算**；**用户可在「设置」里单独关掉高频**，需用 `ActivityAuthorizationInfo().frequentPushesEnabled` 与 `frequentPushEnablementUpdates` 感知并把状态同步给服务端。 [来源](https://developer.apple.com/documentation/bundleresources/information-property-list/nssupportsliveactivitiesfrequentupdates) [来源](https://developer.apple.com/documentation/activitykit/activityauthorizationinfo/frequentpushesenabled)
- **「实际能到多快：每分钟几次 / 每秒几次」——未能确认（截至检索时）**。Apple 在所有官方页面与 WWDC23/26 逐字稿中**均未给出数字**，只说「按设备状态」。第三方宣称的「1 次/秒」等数字**无官方依据**，不可写进方案承诺。
- **超预算后果**：更新被节流/延迟（官方原文 "may throttle"），**不会**让 LA 崩溃，但会让界面停在旧数据。 [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications)
- **两类 token 的区别（重要）**：
  - **update push token**：**每个 LA 实例一个**，`Activity.request(..., pushType: .token)` 后通过 `activity.pushTokenUpdates` 异步获得，同一活动期间**可能变化**，变了要作废旧 token。 [来源](https://developer.apple.com/documentation/activitykit/activity/pushtokenupdates-swift.property)
  - **push-to-start token**：**每个 App 每个设备一个**，**不需要先启动 LA**即可获得（iOS 17.2+），用于让服务器**远程创建** LA；通过 `Activity<Attributes>.pushToStartTokenUpdates` 获取。 [来源](https://developer.apple.com/documentation/activitykit/activity/pushtostarttokenupdates)
- **broadcast/channel（iOS 18+）**：适合「几千上万人看同一个活动」，服务器按 channel 广播，设备订阅 channel；**channel 不能用于启动 LA**（只能更新/结束），启动要用 push-to-start token。 [来源](https://developer.apple.com/documentation/activitykit/pushtype) [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications)

---

## 7. 交互：按钮、深链、点击行为

| 能力 | 事实 | 来源 |
|---|---|---|
| 按钮/开关 | **必须用 App Intents**：交互类 LA 用 **`LiveActivityIntent`**（系统在 **App 进程**里执行 `perform()`，因此能直接发网络请求、写数据库）；普通 `AppIntent` 则在 widget extension 进程执行。按钮只在 **expanded 与锁屏** 两种呈现里可用。 | [来源](https://developer.apple.com/documentation/widgetkit/adding-interactivity-to-widgets-and-live-activities) [来源](https://developer.apple.com/documentation/appintents/liveactivityintent) |
| 锁定状态 | **锁屏未解锁时按钮/开关无效**，系统要求先认证解锁。 | [来源](https://developer.apple.com/documentation/widgetkit/adding-interactivity-to-widgets-and-live-activities) |
| 打卡请求 | **可以**在 `LiveActivityIntent.perform()` 里直接发 HTTP（App 进程有网络权限）；但 LA 视图自身不能联网。 | 同上 |
| 深链 | 锁屏 / compact leading / compact trailing / minimal 用 **`.widgetURL(_:)`**；expanded 还可用 SwiftUI **`Link`**。compact 的 leading 与 trailing 必须指向同一页面。 | [来源](https://developer.apple.com/documentation/activitykit/launching-your-app-from-a-live-activity) |
| 点击打开 App | 默认走 `NSUserActivity`，类型 `NSUserActivityTypeLiveActivity`；可在 `onContinueUserActivity` 里处理。 | 同上 |
| CarPlay | **交互元素在 CarPlay 里被系统禁用**；点击 LA 也只有在 App 支持 CarPlay 时才打开 App。 | 同上 [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) |
| Apple Watch | 点 Watch 上的 LA：有 watchOS App 就开 Watch App，否则给「在 iPhone 上打开」按钮。 | [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) |

---

## 8. 工程与审核

| 项 | 事实 | 来源 |
|---|---|---|
| Info.plist | **`NSSupportsLiveActivities = YES`**（iOS 16.1+）；高频推送再加 **`NSSupportsLiveActivitiesFrequentUpdates = YES`** | [来源](https://developer.apple.com/documentation/bundleresources/information-property-list/nssupportsliveactivities) [来源](https://developer.apple.com/documentation/bundleresources/information-property-list/nssupportsliveactivitiesfrequentupdates) |
| Entitlement | **LA 本身没有专属 entitlement**；推送能力用 **Push Notifications capability（`aps-environment`）**，且**不能用 UserNotifications 框架注册 LA 推送**，必须用 ActivityKit 拿 token；broadcast 能力**只能在 developer.apple.com 后台开，Xcode 里开不了** | [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| time-sensitive | **`AlertConfiguration` 没有 interruption-level 参数**，LA 的 `alert` 只有 title/body/sound（据 API 签名），因此**不能把 LA 更新声明为 time-sensitive/critical**；普通通知的 time-sensitive 能力是否影响 LA 表现，**未能确认** | [来源](https://developer.apple.com/documentation/activitykit/alertconfiguration) [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| 开发者账号 | **需要 Apple Developer Program（$99/年）**：创建 APNs Auth Key（p8）在付费账号后台；TestFlight/App Store/Ad Hoc 分发同样需要会员 | [来源](https://developer.apple.com/programs/) [来源](https://developer.apple.com/documentation/usernotifications/establishing-a-token-based-connection-to-apns) |
| 分发 | **不一定非上架**：开发期可直接装真机；给别人的设备装需要 TestFlight / Ad Hoc / App Store（都要付费会员）。**但 LA 本身不要求上架**。 | [来源](https://developer.apple.com/programs/) |
| 审核红线 | ①**不得用 LA 展示广告/促销**；②必须只展示与正在进行的事件/任务相关的信息；③**不得滥用推送/LA 发垃圾或未经请求的消息**（审核指南 4.5.3 明确点名 Live Activities）；④普通推送不得成为 App 运行的必要条件（4.5.4）；⑤Apple 建议 LA 场景是「有明确开始和结束、时长不超过 8 小时」 | [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) [来源](https://developer.apple.com/app-store/review/guidelines/) |
| 平台支持 | **iPhone、iPad、Mac（菜单栏，点击进 iPhone Mirroring）、Apple Watch Smart Stack、CarPlay Dashboard 自动出现**；**visionOS 不支持**，从兼容 iPad/iPhone App 发起的请求会失败 | [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) [来源](https://developer.apple.com/documentation/activitykit) |
| 用户开关 | 用户可在「设置」里**按 App 关闭 Live Activities**；`areActivitiesEnabled` 判断，`activityEnablementUpdates` 监听 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 低电量模式 | **未能确认（截至检索时）**：官方 LA 文档未描述低电量模式对 LA 的影响；APNs 文档只说低优先级推送会按「设备电源状况」决定投递（`apns-priority` 表） | [来源](https://developer.apple.com/documentation/usernotifications/sending-notification-requests-to-apns) |
| 专注模式 / 静音 | **未能确认（截至检索时）**：LA 常驻锁屏不受通知开关影响；带 `alert` 的更新会点亮屏幕，但是否穿透专注模式、静音下是否响铃，官方文档未明确 | [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) |

---

## 9. 「做不到 / 限制」清单（每条都给原因）

| 需求（桌面卡片的做法） | iOS 能否做到 | 为什么不可能 / 限制在哪 | 来源 |
|---|---|---|---|
| 不装 App 也显示 | **不能** | LA 必须由 App 内嵌的 widget extension 渲染；push-to-start 也要求设备已装该 App 并上报 token | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 常驻桌面/主屏 | **不能** | LA 的系统位置只有锁屏、灵动岛、StandBy、Watch、Mac 菜单栏、CarPlay；桌面常驻是普通 Widget（刷新约 40–70 次/天，不能秒级） | [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) [来源](https://developer.apple.com/documentation/widgetkit/keeping-a-widget-up-to-date) |
| 全天挂着（上班 9h+） | **不能** | 活动态硬上限 8 小时，之后系统强制结束；锁屏再挂 4 小时，总计 12 小时后消失 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 自定义全屏覆盖/浮层 | **不能** | LA 只能出现在系统指定位置，尺寸由系统决定；StandBy 的「全屏」是系统把锁屏卡放大 2×，不是 App 可编程的全屏层 | [来源](https://developer.apple.com/design/human-interface-guidelines/live-activities) |
| 自定义尺寸/圆角/背景（灵动岛） | **不能** | 灵动岛背景固定黑色、尺寸由系统给；圆角用 `ContainerRelativeShape` 跟随，禁止硬编码 | [来源](https://developer.apple.com/documentation/activitykit/creating-custom-views-for-live-activities) |
| 18fps / 逐帧动画、自绘进度动画 | **不能** | 视图由 widget extension 渲染快照；动画最长 **2 秒**；iOS 16 忽略动画修饰符；AOD 完全不播动画；没有逐帧回调机制 | [来源](https://developer.apple.com/documentation/widgetkit/animating-data-updates-in-widgets-and-live-activities) [来源](https://developer.apple.com/design/human-interface-guidelines/always-on) |
| 厘秒/毫秒倒计时 | **不能** | `Text(timerInterval:…)` 只有秒级，API 无厘秒参数；自绘秒以下刷新也受「无持续刷新」限制 | [来源](<https://developer.apple.com/documentation/swiftui/text/init(timerinterval:pausetime:countsdown:showshours:)>) |
| 保证 AOD 下秒数在跳 | **不能保证 / 未确认** | AOD 以降低亮度渲染且不播动画，刷新频率 Apple 未公开 | [来源](https://developer.apple.com/design/human-interface-guidelines/always-on) |
| 每秒推送刷新倒计时 | **技术可行但不该做** | 推送有每小时预算、超预算被节流；秒级推送会迅速耗尽预算且耗电。倒计时应用系统 timer 文本 | [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| 服务端静默启动 LA | **不能** | `start` 事件的 payload **必须包含 `alert`**，即必须让用户看到提示 | [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| LA 内自己轮询/联网拿考勤 | **不能** | LA 沙盒明确不能访问网络、不能收定位更新；数据只能由 App 或推送写入 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 结束后自动留一条普通通知 | **不能自动** | 没有 LA→普通通知的自动转换；需另发一条 `apns-push-type: alert` 普通推送 | [来源](https://developer.apple.com/documentation/activitykit/displaying-live-data-with-live-activities) |
| 用 Windows 端直接改 iPhone 上的 LA | **不能直接** | Windows 无 ActivityKit；只能作为 APNs provider（需 p8 + 会员账号 + 已拿到的 LA token）。且**不要把 p8 内置进分发的 exe**：Apple 要求私钥保密，泄露即可伪造向你 App 用户推送 | [来源](https://developer.apple.com/documentation/usernotifications/establishing-a-token-based-connection-to-apns) |
| 多台设备同步同一秒 | **不能保证** | LA 更新走 APNs best-effort，可能乱序/延迟（系统靠 `timestamp` 只显示最新一条） | [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications) |
| iPad/iPhone 之外（visionOS） | **不能** | visionOS 明确不支持 Live Activities | [来源](https://developer.apple.com/documentation/activitykit) |

## 10. 最小可抄骨架

### 10.1 `ActivityAttributes` + `ContentState`

```swift
import ActivityKit
import Foundation

struct WorkShiftAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        // 倒计时起止点：用 Unix 秒（Double）而不是 Date，避免与 .NET 侧 Date 编码不一致
        // 系统 Text(timerInterval:) 用转换出的 Date 区间，无需推送更新
        var countdownStart: Double    // Unix 秒
        var countdownEnd: Double      // Unix 秒
        // 卡片上的其他动态信息
        var statusText: String        // "工作中" / "午休" / "已下班"
        var progress: Double          // 0...1，进度条用
        var shiftEndText: String      // "20:04"
    }
    // 静态信息：一个活动期间不变，且占用 4KB 预算
    var shiftName: String             // "早班"
    var cardTitle: String             // "距离下班"
}

extension WorkShiftAttributes.ContentState {
    var interval: ClosedRange<Date> {
        Date(timeIntervalSince1970: countdownStart)...Date(timeIntervalSince1970: countdownEnd)
    }
}
```

### 10.2 `ActivityConfiguration`（锁屏 + 灵动岛四区）

```swift
import WidgetKit
import SwiftUI

struct WorkShiftLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: WorkShiftAttributes.self) { context in
            // 锁屏 / 无灵动岛机型上的横幅
            HStack {
                VStack(alignment: .leading, spacing: 4) {
                    Text(context.attributes.cardTitle).font(.caption).bold()
                    Text(timerInterval: context.state.interval, countsDown: true)
                        .font(.system(size: 40, weight: .semibold, design: .rounded))
                        .monospacedDigit()
                }
                Spacer()
                VStack(alignment: .trailing, spacing: 4) {
                    Text(context.state.statusText).font(.caption)
                    Text("下班 \(context.state.shiftEndText)").font(.caption)
                }
            }
            .padding(14)   // HIG：锁屏标准边距 14pt
            .activityBackgroundTint(Color.black.opacity(0.55))
            .activitySystemActionForegroundColor(.white)

        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Label(context.attributes.shiftName, systemImage: "briefcase")
                        .font(.caption).foregroundStyle(.secondary)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    Text(context.state.statusText).font(.caption).bold()
                }
                DynamicIslandExpandedRegion(.center) {
                    // center 位于灵动岛相机正下方
                    Text(timerInterval: context.state.interval, countsDown: true)
                        .font(.system(size: 32, weight: .semibold, design: .rounded))
                        .monospacedDigit()
                        .frame(maxWidth: 170)
                }
                DynamicIslandExpandedRegion(.bottom) {
                    ProgressView(timerInterval: context.state.interval, countsDown: true) {
                        EmptyView()
                    }
                    .tint(.green)
                }
            } compactLeading: {
                Image(systemName: "clock")
            } compactTrailing: {
                Text(timerInterval: context.state.interval, countsDown: true)
                    .monospacedDigit()
                    .frame(maxWidth: 44)   // 横屏受限宽度时退化为 28pt 圆
            } minimal: {
                Image(systemName: "clock.fill")
            }
            .keylineTint(.green)
        }
    }
}
```

> 横屏/limited width 下用 `@Environment(\.isDynamicIslandLimitedInWidth)` 切简化视图（iOS 27）。 [来源](<https://developer.apple.com/documentation/swiftui/environmentvalues/isdynamicislandlimitedinwidth>)

### 10.3 ActivityKit 启动 / 更新 / 结束 / 拿 token

```swift
import ActivityKit

@MainActor
enum ShiftActivity {

    static func start(shiftName: String, title: String,
                      start: Date, end: Date) throws -> Activity<WorkShiftAttributes> {
        guard ActivityAuthorizationInfo().areActivitiesEnabled else {
            throw NSError(domain: "LA", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: "Live Activities disabled"])
        }
        let attrs = WorkShiftAttributes(shiftName: shiftName, cardTitle: title)
        let state = WorkShiftAttributes.ContentState(
            countdownStart: start.timeIntervalSince1970,
            countdownEnd: end.timeIntervalSince1970,
            statusText: "工作中", progress: 0, shiftEndText: Self.fmt(end)
        )
        let activity = try Activity.request(
            attributes: attrs,
            content: ActivityContent(state: state, staleDate: end.addingTimeInterval(300),
                                     relevanceScore: 100),
            pushType: .token          // 仅本地更新可传 nil；要 APNs 更新必须 .token
        )
        observeTokens(activity)
        return activity
    }

    static func update(_ activity: Activity<WorkShiftAttributes>,
                       state: WorkShiftAttributes.ContentState) async {
        await activity.update(ActivityContent(state: state, staleDate: Date.now + 600,
                                              relevanceScore: 100))
    }

    static func end(_ activity: Activity<WorkShiftAttributes>,
                    final: WorkShiftAttributes.ContentState,
                    dismissAfter: TimeInterval = 30 * 60) async {
        let policy: ActivityUIDismissalPolicy = dismissAfter <= 0 ? .immediate
                                                                 : .after(.now + dismissAfter)
        await activity.end(ActivityContent(state: final, staleDate: nil), dismissalPolicy: policy)
    }

    private static func observeTokens(_ activity: Activity<WorkShiftAttributes>) {
        Task {
            for await data in activity.pushTokenUpdates {          // 每个 LA 实例一个
                await report(updateToken: data.map { String(format: "%02x", $0) }.joined())
            }
        }
        Task {
            for await data in Activity<WorkShiftAttributes>.pushToStartTokenUpdates {  // iOS 17.2+
                await report(pushToStartToken: data.map { String(format: "%02x", $0) }.joined())
            }
        }
    }
    private static func report(updateToken: String) async { /* POST 到自己的服务端 */ }
    private static func report(pushToStartToken: String) async { /* POST 到自己的服务端 */ }
    private static func fmt(_ d: Date) -> String {
        let f = DateFormatter(); f.dateFormat = "HH:mm"; return f.string(from: d)
    }
}
```

### 10.4 APNs 推 Live Activity：完整 HTTP/2 请求

```
POST /3/device/<ACTIVITY_PUSH_TOKEN> HTTP/2
host: api.push.apple.com
authorization: bearer <ES256 JWT>                       # kid + iss + iat
apns-topic: com.example.workcountdown.push-type.liveactivity
apns-push-type: liveactivity
apns-priority: 10                                        # 5 = 不占预算，10 = 立即且占预算
apns-id: eabeae54-14a8-11e5-b60b-1697f925ec7b
apns-expiration: 0
content-type: application/json

{"aps":{
  "timestamp": 1763280000,
  "event": "update",
  "content-state": {
    "countdownStart": 1763251200,
    "countdownEnd": 1763280000,
    "statusText": "工作中",
    "progress": 0.5,
    "shiftEndText": "20:04"
  },
  "stale-date": 1763280300,
  "relevance-score": 100
}}
```

对应 `curl`（Apple 官方示例同构，`--http2` 必填）：

```bash
curl --http2 \
  --header "authorization: bearer $AUTH_JWT" \
  --header "apns-topic: com.example.workcountdown.push-type.liveactivity" \
  --header "apns-push-type: liveactivity" \
  --header "apns-priority: 10" \
  --data '{"aps":{"timestamp":1763280000,"event":"update","content-state":{"countdownStart":1763251200,"countdownEnd":1763280000,"statusText":"工作中","progress":0.5,"shiftEndText":"20:04"},"stale-date":1763280300,"relevance-score":100}}' \
  https://api.push.apple.com/3/device/$ACTIVITY_PUSH_TOKEN
```

**远程启动 LA（push-to-start，payload 必须带 alert）：**

```json
{"aps":{
  "timestamp": 1763251200,
  "event": "start",
  "attributes-type": "WorkShiftAttributes",
  "attributes": {"shiftName":"早班","cardTitle":"距离下班"},
  "content-state": {"countdownStart":1763251200,"countdownEnd":1763280000,"statusText":"工作中","progress":0.0,"shiftEndText":"20:04"},
  "alert": {"title":"早班已开始","body":"下班倒计时已启动","sound":"default"}
}}
```

**结束（`end`）**：同一 URL/headers，`"event":"end"` + 最终 `content-state`；`"dismissal-date": 0` 表示立即消失。 [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications)

**编码陷阱（必须知道）**：`content-state` 只能用 Swift **默认** Codable，不能用自定义 key/日期策略，否则更新失败。若字段类型写 `Date`，默认编码是 **2001-01-01 起算的秒数（Double，= Unix 秒 − 978307200）**，不是 Unix 秒；本骨架因此直接用 `Double` 存 Unix 秒，.NET 端 `DateTimeOffset.ToUnixTimeSeconds()` 原样发即可。 [来源](https://developer.apple.com/documentation/activitykit/starting-and-updating-live-activities-with-activitykit-push-notifications)

### 10.5 Windows / .NET 侧等价实现（JWT ES256 + HTTP/2）

```csharp
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static string B64Url(byte[] b) =>
    Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static string CreateProviderJwt(string keyId, string teamId, string p8Path)
{
    var header = JsonSerializer.Serialize(new { alg = "ES256", kid = keyId });
    var claims = JsonSerializer.Serialize(new
    {
        iss = teamId,
        iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    });
    var signingInput = B64Url(Encoding.UTF8.GetBytes(header)) + "." +
                       B64Url(Encoding.UTF8.GetBytes(claims));

    using var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(File.ReadAllText(p8Path));              // Apple 的 .p8 (P-256)
    var sig = ecdsa.SignData(Encoding.ASCII.GetBytes(signingInput),
                             HashAlgorithmName.SHA256,
                             DSASignatureFormat.IeeeP1363FixedFieldConcatenation); // JWT 要 r||s
    return signingInput + "." + B64Url(sig);
}

static async Task SendLiveActivityUpdateAsync(
    string bundleId, string deviceToken, string jwt, string json)
{
    using var http = new HttpClient
    {
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };
    var req = new HttpRequestMessage(
        HttpMethod.Post, $"https://api.push.apple.com/3/device/{deviceToken}")
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
    req.Headers.TryAddWithoutValidation("authorization", $"bearer {jwt}");
    req.Headers.TryAddWithoutValidation("apns-topic", $"{bundleId}.push-type.liveactivity");
    req.Headers.TryAddWithoutValidation("apns-push-type", "liveactivity");
    req.Headers.TryAddWithoutValidation("apns-priority", "10");

    using var resp = await http.SendAsync(req);
    // 200 = 成功；403 ExpiredProviderToken = JWT 过期；410 Unregistered = token 失效
    resp.EnsureSuccessStatusCode();
}
```

要点：**.NET 的 `DSASignatureFormat.IeeeP1363FixedFieldConcatenation` 才能产出 JWT 需要的 `r||s` 签名**（DER 格式 APNs 不认）；`HttpVersion.Version20 + RequestVersionExact` 才能保证走 HTTP/2；JWT 建议缓存并在 **20–60 分钟**内轮换。 [来源](https://developer.apple.com/documentation/usernotifications/establishing-a-token-based-connection-to-apns)

## 11. 未能确认 / 待实测清单

| 项 | 状态 | 建议验证方式 |
|---|---|---|
| LA 更新预算的具体数值（次/小时、次/分钟） | **未能确认（Apple 未公开）** | 真机上连续优先级 10 推送并观察延迟/日志（`liveactivitiesd`、`apsd`、`chronod`） |
| AOD 下 timer 秒位是否继续跳动 / AOD 刷新频率 | **未能确认（Apple 未公开）** | iPhone 真机 + Always-On 录屏逐帧核对 |
| LA 在低电量模式、专注模式、静音下的具体行为 | **未能确认** | 真机分场景实测 |
| LA 同时活跃数量上限 | **未能确认（文档只说「取决于多种因素」）** | 真机批量 request |
| 锁屏卡系统圆角的具体半径 | **未公开（禁止硬编码）** | 用 `ContainerRelativeShape` 自适应，不需知道数值 |
| iOS 27 是否还有其它 LA 未列出的新 API | **未能确认** | 对照 Xcode 27 SDK 的 ActivityKit/WidgetKit 头文件与更新日志 |

> 附：本次核查抓取的官方页面已全部以上方行内链接形式给出（Apple 文档为主，WWDC23/26 逐字稿与 Apple 支持页为辅），便于逐个复核。
