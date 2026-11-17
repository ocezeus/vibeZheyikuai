# 上班工具箱 → iOS 实时活动（Live Activity）技术方案

> 版本 v0.1 · 2026-11-16 · 针对 `work-countdown` v3.8.1（.NET Framework 4.x / C# 5 / WinForms 分层窗口）
> 配套文件：
> - 布局稿：`design/ios-live-activity.html` + `design/shots-ios/*.png`
> - 视觉规格提取：`docs/card-spec-extract.md`
> - iOS 事实核查：`docs/research-ios-liveactivity.md`

---

## 0. 结论先行（TL;DR）

1. **"把这个卡片搬到 iPhone 锁屏"不是一个移植问题，是一个换渲染引擎的问题。** 桌面卡片是"自己逐像素画出来的窗口"（分层窗口 + 自绘），iOS 实时活动是"你声明状态、系统渲染的系统组件"。凡是"帧级动画 / 自定义圆角 / 任意尺寸"都必须重画，凡是"状态 + 时间"都能完整保留。
2. **布局可以 1:1 对得上，但只保留 5 行。** 桌面展开卡 280×(164|188) DIP（`src/Widget.cs:364-369` 算出来的，不是常量：无午休行 164 / 有午休行 188，工资面板 +116）里的 8 个信息块 → 锁屏卡 **371×~127pt**（393 屏；430 屏 408pt）的 5 行（标题 / 大计时 / 细进度 / 午休行 / 脚注）+ 灵动岛 4 区。工资面板、设置、胶囊菜单不进锁屏（进 App）。
3. **实现的关键不是"怎么推"，是"怎么少推"。** 倒计时和进度条用 iOS 原生 `Text(timerInterval:)` / `ProgressView(timerInterval:)` 自己走秒，**IPC 主链路一天只推 8–20 条事件**（打卡、日程、午休、提醒、未读），而不是每秒推。这是整个方案省电、省预算、不被系统节流的支点。
4. **必须有一个 iOS App + Widget Extension。** 没有"Windows 直接往 iPhone 锁屏塞一个卡片"的办法。Windows 端从"画界面"退化为"状态机 + 推送器"。
5. **8 小时上限是这个需求最大的坑。** 12:00–20:30 的工作日 = 8.5h > 8h。方案默认**只在下班前 4 小时上岛**（可配 2/4/6/全天），既避开上限，也符合 Apple HIG「只展示当下重要的事」。
6. **Windows 端有个硬约束：`csc.exe v4.0.30319` + .NET Framework，`HttpClient` 只有 HTTP/1.1，而 APNs 必须 HTTP/2。** 三条路：`curl.exe` 子进程（MVP 最快）、WinHTTP P/Invoke（无依赖、推荐）、或加一个中转服务（多设备时）。
7. **建议分两步走**：先做"锁屏小组件 + 时效性通知"（1 天，零推送），验证价值和审美；确认要灵动岛再上 Live Activity（3–5 天）。
8. **事实基线（2026-11 核实）**：当前 iOS 最新正式版 **iOS 27.2.1**（灵动岛已支持横屏，新增 `isDynamicIslandLimitedInWidth`）。硬限制：活动态 **8 小时** + 结束后锁屏最多 **4 小时**（总计 12h）、payload/LA 数据合计 **4 KB**、动画最长 **2 秒**、倒计时只能到**秒**、**推送预算 Apple 从未公布具体数字**。详见 `docs/research-ios-liveactivity.md`。

---

## 1. 需求翻译：把"卡片"翻译成 iOS 的原语

| 桌面卡片的机制 | iOS 上的对应物 | 差异 |
|---|---|---|
| 置顶悬浮窗、自绘整帧、逐像素 alpha | Live Activity（锁屏卡片 + 灵动岛），系统渲染 | 尺寸/圆角/背景/刷新率全部由系统给 |
| 1 秒 `tick` + 55ms `flowT` 重绘 | `Text(timerInterval:countsDown:)`、`ProgressView(timerInterval:)` | 系统原生走秒，不需要你刷新 |
| 每 5 分钟跑一次 `lark-cli` 读考勤 | 不变（仍跑在 Windows 上）→ 状态变化时推 APNs | 手机不连飞书，PC 仍是"大脑" |
| 全屏扩散提醒 / 警戒带 / 抽屉卡 | 时效性通知（time-sensitive）+ Live Activity 切"提醒态" | 不能全屏覆盖、不能自定义动画 |
| 小纸条未读左上角每 5 秒快闪 | 未读点 + 普通通知 | 锁屏组件不能做循环高亮 |
| 39 套主题 / 六套皮肤 / 磨砂玻璃 | 一套固定「iOS quiet」+ 跟随系统深浅色 + 主题强调色透传 | 系统会做 vibrancy，颜色不能逐像素还原 |

**一句话**：桌面端是"我画什么你看什么"，iOS 端是"我报什么状态你系统自己画"。

---

## 2. 能力边界（先对齐"做不到"）

| 桌面能力 | iOS 实时活动 | 结论 |
|---|---|---|
| 常驻悬浮窗 | 锁屏 + 灵动岛，最长 8h（锁屏可到 12h），且用户可关 | ⚠️ 有条件可用 |
| 秒级倒计时 | `Text(timerInterval:)` 原生走秒 | ✅ 零更新 |
| 进度条 | `ProgressView(timerInterval:)` / `ProgressView(value:)` | ✅ 零更新（暂停时切静态值） |
| 午休行 / 下一段日程 | 推送更新 ContentState | ✅ 事件驱动 |
| 全屏提醒动画（扩散/警戒带/抽屉卡） | ❌ 不存在。只有：时效性通知 + Live Activity 换"提醒态" + 可选 `alert` 弹横幅 | ❌ 换实现 |
| 磨砂玻璃 / 呼吸柔光 / 扫边流光 | 锁屏组件背景只能 tint + 系统模糊；没有多层光晕 | ❌ 不做 |
| 55ms 逐帧展开动画 | 系统接管出入场（约 300ms 弹簧） | ❌ 不做 |
| 18fps 工资小数跳动 | 实时活动不能高频重绘；工资每秒变 | ❌ 不进锁屏 |
| 未读快闪（420ms 两下） | 系统不给你帧循环 | ❌ 改成未读点 |
| 右键胶囊菜单 | Live Activity 内 `Button(intent:)`（有限、系统样式）+ App 内设置页 | ⚠️ 部分 |
| 不装 App 就想有实时活动 | 不可能。Live Activity 属于 App 的 Widget Extension | ❌ |

**白捡的附加形态（不用额外开发）**：同一张实时活动会自动出现在 **StandBy（床头横屏，系统把锁屏卡放大 2×）**、**Apple Watch 智能叠放**、**Mac 菜单栏 / iPhone 镜像**、**CarPlay 仪表盘**（iPad 锁屏也支持，卡宽 425–500pt）。这是"实时通知"体验里最划算的一块——手机放床头就是个大号倒计时。

**两个容易踩的边界**：① 实时活动的 `alert`（`AlertConfiguration`）**没有 interruption-level 参数**，所以它不能像普通通知那样声明为"时效性"；要穿透专注模式只能另发一条普通 APNs 通知。② 系统**不会**把结束的实时活动自动转成普通通知，降级通知要单独发。

> 事实依据（限额/尺寸/API）：见 `docs/research-ios-liveactivity.md`（498 行，逐条带 Apple 官方来源）。

---

## 3. 三个候选方案与选型

| 方案 | 形态 | 成本 | 能做什么 | 不能做什么 |
|---|---|---|---|---|
| **A. 锁屏小组件**（WidgetKit accessory） | 锁屏小圆/小方块/长条 | **1 天**（1 个 iOS App + 1 个 Widget） | 倒计时（`Text(timerInterval:)` 原生走秒）、进度环、下一个日程；**零推送**，靠 timeline 刷新（系统预算约 40–70 次/天，不是分钟级保证） | 不是大卡片、没有灵动岛、不能交互按钮、靠时间线刷新
| **B. 实时活动 Live Activity** | 锁屏大卡 + 灵动岛 | **3–5 天**（App + Widget Extension + APNs 推送链路 + Windows 改造） | 上表所有 ✅ 项；打卡/日程/午休/提醒实时切态 | 8h/12h、每秒推送、全屏动画、自定义样式 |
| **C. 纯通知** | 时效性通知 | **0.5 天** | 「距下班 5 小时」「该下班啦」这类到点提醒 | 不能常驻、不能一眼看到还剩多久 |

**推荐路径**：A（当天能验证审美和"值不值得"）→ B（确认后 1 周内落地）。C 作为 B 的降级兜底（Live Activity 被关/低电量/超 8h 时）。

---

## 4. 布局方案

### 4.1 信息分层：桌面 8 块 → 锁屏 5 行 + 1 个"溢出区"

桌面展开卡的纵向顺序（`src/Widget.cs:2764-2858`）：状态行 y=12 h=20（headline 9.5pt + 右上 5 个图标）→ 说明行 y=38 h=18（`bigLabel`，如「距离下班」）→ 大数字 y=62 h=34（28pt）→ 进度条 y=102 h=4 → 下一段 y=112 h=18（`sub`，如「上班 09:12 · 预计下班 19:30」）→ 午休行 y=136 h=18（可选）→ 脚注 y=160 h=16（`extra`，如「已工作 3h20m · 应勤 8h00m」）。全部 9.5pt / 9pt，行距统一 6 DIP，上下留白 12。

按"锁屏上 3 秒能不能读完"重新分级（文案模板全部照 `docs/card-spec-extract.md` §4.7 抄）：

| 级别 | 内容（真实文案） | 锁屏卡 | 灵动岛 expanded | 灵动岛 compact | AOD |
|---|---|---|---|---|---|
| **P0 必须** | 状态点 + `headline`（`工作中`/`午休中`/`已达标`/`还没打卡`） | 第 1 行左（17pt） | leading | leading 小点 | 保留 |
| **P0 必须** | 大数字（`05:30:56` / `00:42:18` / `今天休息`） | 第 2 行（34pt 等宽） | center | trailing（`5h30` / `42:18`） | 只到分钟 |
| **P0 必须** | 进度（`(now-start)/(target-start)`，Done/Overtime = 1） | 第 3 行（3pt 细线） | bottom 上方 | — | 隐藏 |
| **P1 重要** | `bigLabel` + 右侧 `targetText`（桌面收起态右槽：`下班 20:04`；午休计数时 `休 1h05m`、晚休 `晚 45m`） | 第 1 行（`● 工作中 · 距离下班` ｜ 右 `下班 20:04`） | trailing | — | 保留 |
| **P1 重要** | 午休行（`距午休 1h05m` / `午休中 还剩 1h05m` / `距晚休 3h27m` / `晚休中 还剩 40m` / `马上下班` / `距下班 2h10m`） | 第 4 行左（13pt） | bottom | — | 只留剩余 |
| **P2 可选** | `sub`（`上班 09:00 · 预计下班 20:04`） | 第 4 行右（13pt 灰） | leading 第二行 | — | 隐藏 |
| **P2 可选** | `extra`（`已工作 5h33m · 应勤 8h30m`）+ 未读点 | 第 5 行（10pt 灰）+ 右上小点 | trailing 角标 | minimal 小点 | 隐藏 |
| **溢出区** | 工资面板（+116 DIP）、设置、主题、胶囊菜单、纸条对话 | ❌ 不进锁屏 | ❌ | ❌ | ❌ 打开 App |

> 桌面卡在"显示午休行"时会自己长高 24 DIP（188 vs 164）；iOS 端不用跟着长高——把午休行固定在锁屏第 4 行，没内容时**留空而不是重排**，避免推送前后卡片跳动。

### 4.2 锁屏实时活动卡（371 × ~127 pt；430 宽机型 408pt）

> HIG 2026-10 规格表：**锁屏卡宽 = 屏宽 − 22pt**（393 屏 → 371、430 屏 → 408），左右各 11pt；**高 84–160pt，超过 160 会被系统截断**；标准内容边距 **14pt**。圆角由系统画（禁止硬编码，内部用 `ContainerRelativeShape` 跟随）。

```
┌──────────────────────────────────────────────────────┐  371pt 宽（393 屏 − 22）
│  ● 工作中 · 距离下班                      下班 20:04  │  17pt headline + 13pt bigLabel / 13pt targetText
│                                                      │
│  05:30:56                                            │  34pt semibold monospaced，等价 #F1F3F7
│                                                      │
│  ▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔         │  3pt 细进度（quiet 皮肤 2 DIP hairline → 直角）
│                                                      │
│  ● 距晚休 3h27m                          上班 09:00  │  13pt；左=午休行，右=sub 首段
│                                                      │
│  已工作 5h33m · 应勤 8h30m                    ● 2    │  10pt dim + 未读点（accent）
└──────────────────────────────────────────────────────┘
   内容内边距 14pt（HIG），卡内左对齐；右侧状态列右对齐
```

> 示例口径：上班 09:00、现在 14:33、下班 20:04（布局稿 `design/shots-ios/lock-activity.png` 按 371×154pt 出图，比 §4.2 文字预算的 ~127pt 略高，因为多了 14pt 内边距与 5 行间距，仍在系统允许的 84–160pt 内）。你的实际班次是 12:00–20:30，把示例数字换成 14:33/20:30/5h57m 即可。

纵向预算：上 12 + 第 1 行 17 + 6 + 计时 34 + 6 + 进度 3 + 8 + 第 4 行 13 + 6 + 第 5 行 10 + 下 12 ≈ **127pt**（系统锁屏活动最佳区间 100–160pt）。

- **不跟着桌面卡的高度变化**：桌面"显示午休行 → 卡长高 24 DIP"（188 vs 164），iOS 上第 4 行留空不重排，避免推送前后跳动。
- **不要自己画圆角/投影/边框**：容器是系统的，内部只用 1px `Color.white.opacity(0.08)`（对应 `#13FFFFFF`）或通栏 3pt 进度线表达 quiet 皮肤的"极细线"气质。
- **1/100 秒必须降级**：桌面 `<60min` 显示 `mm:ss.ff`（`src/Models.cs:2232-2236`），Live Activity 计时文本只到秒 → `<1h` 用 `mm:ss`，再短就用「剩 3 分」文案。这是唯一一处"桌面更精细"的地方，接受。

### 4.3 灵动岛四态

| 区域 | 内容 | 尺寸（393 屏 / 430 屏） | 设计取舍 |
|---|---|---|---|
| `compactLeading` | 状态点 + `下班` | **52.33×36.67 / 62.33×36.67** | 只放"状态点 + 2 个汉字"，超过就只剩点 |
| `compactTrailing` | 倒计时简写 | 同上（与 leading 等宽） | **≥1h 显示 `5h30`；<1h 显示 `42:18`；<1min 显示 `42s`** |
| `minimal` | 一个点，或进度环 | **36.67–45 × 36.67** | quiet 皮肤：小圆点 + 进度环（`Circle().trim(to:)`），不放文字 |
| `expanded` | leading 状态/距下班；trailing 预计下班/未读；center 大计时 + 细进度；bottom 下一段日程 + 一颗按钮 | **371×84–160 / 408×84–160**，>160 截断 | bottom 只放 **1 个** 按钮；center 宽建议 ≤170pt |
| limited width（iOS 27） | 横屏/受限宽度下的紧凑版 | **必须塞进直径 28pt 圆** | 用 `@Environment(\.isDynamicIslandLimitedInWidth)` 切极简视图 |

> iOS 27 起灵动岛**横屏也显示**；受限宽度下 leading/trailing 会被压到 28pt 圆内，所以 compact 视图必须有一个"只剩图/只剩数字"的降级分支，否则会截字。

```
expanded（岛宽 371pt，高上限 160pt；布局稿按 160 画）
┌──────────────────────────────────────────────┐
│ ● 距离下班                          下班 20:04│  leading / trailing
│                                              │
│              05:30:56                        │  center 34pt
│         ▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁                │
│                                              │
│ ● 距晚休 3h27m                    [ 打卡 ]   │  bottom（1 颗按钮）
└──────────────────────────────────────────────┘
```

### 4.4 常亮（Always-On Display）与深浅色

- `isLuminanceReduced == true` 时：**不显示秒**（只 `5h30`）、藏进度条、去掉所有描边、文字降到 60–70% 不透明度、背景几乎全黑。AOD 会降亮度且**不播放任何动画**；刷新频率 Apple 未公开（秒位是否继续跳需真机验证），所以按"AOD 里不承诺秒"设计。
- 锁屏实时活动背景是系统的半透明材质；`quiet` 皮肤不能给纯黑（会和壁纸糊在一起）→ 用 `.activityBackgroundTint(Color(hex: "#12151A").opacity(0.82))`，前景文字用 `Color.primary`（系统会自动在浅色壁纸下反色）或显式 token。
- 提供**两套前景色**（深壁纸 / 浅壁纸），由 `colorScheme` 环境值切换；出图对照见 `design/shots-ios/lock-activity.png` 的 H 画板。

### 4.5 视觉令牌映射（quiet → iOS）

| 令牌 | 桌面值（源码） | iOS 写法 |
|---|---|---|
| 卡片底 | `#12151A` → `#171B21` 90° 渐变（`src/Skins.cs:225`） | `.activityBackgroundTint(Color(hex: "#12151A").opacity(0.82))`（不能渐变，系统会叠材质） |
| 细线 | 1 DIP `ARGB(19,255,255,255)`（= `#13FFFFFF`） | `Color.white.opacity(0.075)`，`Rectangle().frame(height: 1)` |
| 主文字 | `#E9ECF1` | `Color(hex: "#E9ECF1")` |
| 二级（实际用的） | **`#C3C6CC`** = `Mix(textMain, bgBottom, 0.18)`（注意：不是 `theme.textMuted` #929AA7） | `Color(hex: "#C3C6CC")` |
| 三级（实际用的） | **`#AAADB3`** = `Mix(textMain, bgBottom, 0.30)`（不是 `#666E7B`） | `Color(hex: "#AAADB3")` |
| 状态点 / 强调 | `#96A9FF` | `Color(hex: "#96A9FF")` |
| 成功（已达标/已下班/未读点） | `#46D39A` | `Color(hex: "#46D39A")` |
| 告警（还没打卡） | **`#F2B34C`**（硬编码在 `Models.cs:2663`，不在皮肤里） | `Color(hex: "#F2B34C")` |
| 进度轨道 | `#2E3237`（实际画白 11%×0.9） | `Color.white.opacity(0.10)` |
| 进度已走段（quiet） | `#D7DDE8` = `Mix(accent2, textMain, 0.3)` | `Color(hex: "#D7DDE8")`；强调主题时用 `accentHex` |
| 大数字 | `#F1F3F7`，28pt Bold Consolas，无发光 | `.system(size: 34, weight: .semibold, design: .monospaced)` + `#F1F3F7` |
| 中文 | PingFang SC → Noto Sans SC → 微软雅黑 | 系统默认（iOS 自带 PingFang SC） |
| 圆角 | 收起 10 / 展开 10 DIP（quiet） | 系统容器固定，不可改；内部元素 `RoundedRectangle(cornerRadius: 2)` 等 |
| 进度条 | **2 DIP 细线**（quiet `SkinProgress.Hairline`），不发光不流动 | `Capsule().frame(height: 2.5)` + 已走段 |
| 最后 30 分钟彩色 | `0 < 剩余 ≤ 30min` → 四色渐变 `rgb(255,212,122)` / `rgb(255,214,61)` / `rgb(255,138,61)` / `rgb(255,92,122)` | 由 ContentState 传 `tone = "soon"`，固定用其中 2 色（系统里不做渐变），30 分钟一到推一次

> 注意：39 套主题不搬。只把当前主题的 `accent` 以 hex 传进 ContentState，供灵动岛/进度条使用；系统会做 vibrancy，颜色比桌面钝一点，这是可接受的。

### 4.6 不可移植动效 → 替代

| 桌面 | iOS 替代 |
|---|---|
| 全屏扩散（圆形柔光/进度条拉伸/警戒带） | 时效性通知（`interruptionLevel: .timeSensitive`）+ Live Activity 提醒态；重要会议再加一次 `alert` 弹横幅 |
| 常驻"该下班啦"整卡覆盖 | Live Activity 提醒态（标题变色 + 大数字变成"该下班啦"） + 每 5 分钟一条通知（可配） |
| 未读左上角快闪 | 未读点 + 收到时一条通知；Live Activity 的 `relevanceScore` 提升 |
| 卡片外呼吸柔光 / 扫边 | 不做（系统禁止，也没必要） |
| 展开/收起 55ms 错峰 | 系统弹簧动画，接受 |
| 自定义入场/退场动画 | 系统接管；自绘动画最长 **2 秒**，iOS 16 上 Live Activity 直接忽略 `withAnimation` / `animation(_:value:)`；只有 `.contentTransition(.numericText())` 这类系统转场有效 |

### 4.7 状态 × 布局矩阵

| 状态（`CardState`/`LunchState`，`src/Models.cs:2411`） | 第 1 行左 headline · bigLabel | 第 1 行右 targetText | 大数字 `big` | 第 4 行左 午休行 | 第 4 行右 `sub` | 第 5 行 `extra` | DI L/T · minimal | expanded bottom |
|---|---|---|---|---|---|---|---|---|
| `Working` 工作中 | 工作中 · 距离下班 | 下班 20:04 | ↓ `05:30:56` 34pt | 距晚休 3h27m | 上班 09:00 · 预计下班 20:04 | 已工作 5h33m · 应勤 8h30m | 点/下班 · `5h30` · 进度环 | 下一段日程 + 打卡按钮 |
| `Working`+`During` 午休中 | 午休中 · 还剩 | 休 1h05m | ↓ `01:05:00`（强调色） | 午休中 还剩 1h05m | 上班 09:00 · 预计下班 20:04 | 已工作 3h25m · 应勤 8h30m | 点/午休 · `1h05` · 实心点 | 午休到 13:30 |
| `Working`+`EveningDuring` 晚休中 | 晚休中 · 还剩 | 晚 35m | ↓ `00:35:00` | 晚休中 还剩 35m | 同上 | 同上 | 点/晚休 · `35m` | 晚休到 18:45 |
| `Working`+`OffSoon` 马上下班 | 工作中 · 距离下班 | 下班 20:04 | ↓ `00:04:12` | 马上下班 | 上班 09:00 · 预计下班 20:04 | 已工作 8h26m · 应勤 8h30m | 点/下班 · `4m` · 进度环 | 打卡按钮 |
| `Overtime` 已达标 | 已达标 · 可下班 · 已超 | 下班 20:04 | ↑ `00:12:30`（success） | 午休行照旧 | 上班 09:00 · 预计下班 20:04 | 在岗 8h42m · 下班后发「2」记录 | 点/已达标 · `12m` · success 点 | 打卡（下班） |
| `Done` 已下班 | 已下班 · 今日在岗 | — | `8h42m`（17pt Bold） | 隐藏 | 09:00 → 20:04 | 工时已达标 · 应勤 8h30m | 点/已下班 · success 点 | —（10 分钟后 end） |
| `NotClockedIn` 还没打卡 | 还没打卡 · 上班已过 | 上班时间 09:00 | ↑ `00:25:00`（**琥珀 #F2B34C**） | 距午休 1h05m | 上班时间 09:00 · 周五 | 在飞书给「考勤大王」发「1」完成打卡 | 点/迟到 · `25m` · 琥珀点 | 打开 App 打卡 |
| `BeforeWork` 未上班 | 未上班 · 距离上班 | 上班时间 09:00 | ↓ `00:45:00` | 距午休 1h05m | 上班时间 09:00 · 周五 | 飞书里发「1」即完成上班打卡 | 点/上班 · `45m` · 空心点 | —（默认不上岛） |
| `Rest` 休息日 | 休息日 · 周六 | — | `今天休息`（17pt Bold） | 隐藏 | 下次上班 11月17日 09:00（周一） | 右键可临时设为上班日 | 不上岛 | 不上岛 |
| `offAlert` 该下班啦（常驻） | 注意 · 该下班啦 | 下班 20:04 | `该下班啦`（琥珀） | — | 下班 20:04 | 在飞书发「2」打卡后自动消失 | 点/该下班 · 琥珀点 | 打卡按钮 |
| 提醒（`source=lark/local/outlook/break`） | 会议提醒 · 需求对齐会 | — | `还剩 12 分钟` | — | 11:34 会议室 A | — | 点/会议 · `12m` · 会议色点 | 打开详情 |

---

## 5. 状态模型与更新机制（本方案的核心）

### 5.1 为什么"不能推秒"

APNs 的实时活动更新是**事件型**通道：系统有预算/节流，且每秒一条既费电又会被系统降频。桌面端 1 秒 tick 全部丢弃，只把**不变量**（什么时候开始、什么时候结束、现在处于哪个阶段）交给 iOS，让系统自己算时间。

结论：**桌面端从"每秒重画"改成"状态变化时写一条 JSON"。**

### 5.2 ContentState 设计：CardStatus 的 iOS 化

桌面端已经有现成的 `CardStatus`（`Models.cs:2415`），它几乎是天然的 ContentState；唯一要改的是：**不要把 `big` 字符串推过去，推时间戳 + 模式**。

```swift
// 共享给 App 与 Widget Extension（Target Membership 两边都勾上）
struct WorkActivityAttributes: ActivityAttributes {
    struct ContentState: Codable, Hashable {
        // 阶段与语义
        var phase: String          // beforeWork | working | lunch | evening | offSoon | overtime | done | offAlert | reminder
        var headline: String       // "工作中" / "午休中" / "该下班啦"
        var detail: String?        // "上班 12:00 · 弹性 8h30m"

        // 时间（关键：给时间戳，不给字符串）
        var startAt: Date?         // 上班打卡时间 / 当前时段开始
        var endAt: Date?           // 目标时间（下班/午休结束/会议开始）→ 用于 timerInterval
        var mode: String           // "down" | "up" | "paused"

        // 进度（paused 时用静态值；其余由 interval 计算）
        var progress: Double?      // 0...1
        var progressStartAt: Date?
        var progressEndAt: Date?

        // 副行与角标
        var secondaryLabel: String?  // "距晚休"
        var secondaryValue: String?  // "4h30m"
        var targetText: String?      // "20:04" 或 "休 1h05m"
        var unread: Int

        // 视觉
        var tone: String           // normal | soon | critical | success | alert | meeting
        var accentHex: String      // 主题强调色，供进度条/状态点用

        // 新鲜度（系统 staleDate 兜底）
        var staleAt: Date?
    }
    // 整个工作日不变的部分
    var workDate: String           // "2026-11-16"
    var id: String                 // 设备/实例标识，防串台
}
```

> **两个编码陷阱**（研究底稿 §10.4）：① `content-state` **只能用 Swift 默认 Codable 策略**，自定义 key/日期策略会导致更新失败；② 若字段类型写 `Date`，默认编码是 **2001-01-01 起算的秒数**（= Unix 秒 − 978307200），不是 Unix 秒。所以本方案**直接用 `Double` 存 Unix 秒**，Windows 端 `DateTimeOffset.ToUnixTimeSeconds()` 原样发。
> 另外：**静态 attributes + 动态 content-state 合计也受 4 KB 限制**（不是只有推送 payload 4 KB），所以 attributes 里只放 `workDate` / `id` 这种极小字段，别把主题、日程列表塞进去。

映射表（桌面字段 → iOS 字段）：

| `CardStatus`（C#） | ContentState | 说明 |
|---|---|---|
| `state` | `phase` | 枚举转字符串 |
| `headline` | `headline` | 直接搬 |
| `sub` | `detail` | 直接搬 |
| `big`（"05:30:56"） | **丢弃** → `endAt` | 手机自己算 |
| `targetIso` | `endAt` | 已存在，正好复用 |
| `progress`（double） | `progress` + `progressStartAt/EndAt` | 正常走 interval，暂停用静态值 |
| `lunch` / `lunchLabel` / `lunchValue` | `phase` + `secondaryLabel` + `secondaryValue` | 合并 |
| `targetText` | `targetText` | 直接搬 |
| `bigIsClock` | `mode`（down/up） | 倒计时/正计时 |
| `accent`（Color） | `accentHex` | 转 `#RRGGBB` |
| `offAlert` | `phase = offAlert` | |
| 未读数（`Notes`） | `unread` | 桌面已有 |
| — | `staleAt` | 新增，见 5.6 |

### 5.3 零刷新倒计时 / 进度（关键代码）

```swift
// 倒计时：系统自己走秒，不依赖你推送
if let endAt = state.endAt {
    Text(timerInterval: Date.now...endAt, countsDown: state.mode == "down")
        .font(.system(size: 34, weight: .semibold, design: .monospaced))
        .monospacedDigit()
        .contentTransition(.numericText())
}

// 正常态进度：系统按 interval 自己长
if let s = state.progressStartAt, let e = state.progressEndAt {
    ProgressView(timerInterval: s...e, countsDown: false)
        .progressViewStyle(.linear)
        .tint(Color(hex: state.accentHex))
        .scaleEffect(y: 3, anchor: .center)   // 4pt 细线
} else {
    // 午休/等待/暂停：静态进度，避免"暂停了条还在走"
    ProgressView(value: state.progress ?? 0)
        .tint(Color(hex: state.accentHex))
}

// 常亮态：不显示秒
Text(timerInterval: Date.now...endAt, countsDown: true)
    .format { $0 == .luminanceReduced ? .minuteOnly : .hourMinuteSecond } // 伪代码，实际用环境值分支
```

> 具体修饰符以研究底稿的 API 核查为准；**设计意图不变：秒数由系统渲染，一次推送管一段**。

### 5.4 更新触发矩阵（一天推什么）

| 事件 | 桌面现状 | 需要推？ | 通道 | 频率 |
|---|---|---|---|---|
| 1 秒 tick | 重画 | ❌ | 本地系统计时 | 0 |
| 55ms 流动亮带 | 重画 | ❌ | 不做 | 0 |
| 飞书同步到上班打卡 | 状态工作 | ✅ start/update | APNs `event: start` | 1 次/天 |
| 飞书同步到下班打卡 | 已下班 | ✅ update → 10 分钟后 end | APNs | 1 次/天 |
| 午休开始 / 结束 | 状态行切换 | ✅ update | APNs | 4 次/天（午/晚各 2） |
| 日程出现 / 改时间 / 取消 | 提醒列表变 | ✅ update（只改 secondary/提醒态） | APNs | 0–10 次/天 |
| 日程提醒触发 / 结束 | 全屏动画 | ✅ update（提醒态）+ 可选通知 | APNs（含 `alert`） | 0–10 次/天 |
| 到下班点还没打卡 | 常驻"该下班啦" | ✅ update（offAlert 态） | APNs | 1 次/天 |
| 收到小纸条 | 闪光 + 未读 | ✅ update（unread）+ 普通通知 | APNs | 少量 |
| 读完纸条 | 未读清零 | ✅ update | APNs | 少量 |
| 跨天 | 重置 | ✅ end + 次日 start | APNs | 2 次/天 |
| 主题/皮肤切换 | 换配色 | ⚠️ 可选（accent 变小才推） | APNs | 罕见 |
| 工资数字 | 18fps | ❌ | 不进锁屏 | 0 |

**合计：正常一天 8–20 条推送，远低于系统预算。** 落地时加 1.5s debounce + 合并（比如 09:27:00 打卡 + 09:27:00 日程刷新一起），并给每条推 `timestamp`（秒级）用于丢弃乱序。

推送优先级策略（官方口径，别自己编数字）：

- **默认用 `apns-priority: 5`** —— 低优先级、**不计入每小时的推送预算**，适合"午休开始""未读 +1"这类可等的更新；
- **只有"打卡成功 / 提醒到点 / 结束"用 `10`** —— 立即投递但计入预算，一天个位数；
- **Apple 从未公布预算具体数值**（"每小时一定预算，取决于设备状态，超预算会被节流"）→ 方案里不承诺"每分钟几次/每秒几次"，验收只看"一天 8–20 条时是否被节流"；
- 本方案**不需要** `NSSupportsLiveActivitiesFrequentUpdates`（高频推送要更高预算，但用户可以在设置里单独关掉，还要用 `frequentPushesEnabled` 感知并同步给 Windows，增加复杂度而无收益）。

### 5.5 8 小时上限 vs 8.5 小时工作日（必须处理）

系统对单个实时活动的限制（已核实）：**活动态硬上限 8 小时**，到时系统自动结束并**立即从灵动岛移除**；结束后锁屏最多再留 **4 小时**，**总计 12 小时**后彻底消失；`dismissalPolicy.after(Date)` 也受 4 小时封顶。而当前配置 12:00–20:30 = 8.5h，恰好越线。且**系统不会把结束的实时活动自动变成普通通知**，要兜底必须另发一条 `apns-push-type: alert`。

三档对策（推荐 ①）：

| 档 | 策略 | 效果 |
|---|---|---|
| **① 下班前 4 小时才启动**（默认） | 16:30 才第一次 `start`，20:04 结束 → 3.5h，远离上限，也更符合 HIG「只展示此刻重要的事」 | 上午没有灵动岛，但上午本来也不焦虑 |
| ② 分段重启 | 午休时 `end()`，13:30 用 `push-to-start` 重新开一段 | 全天都有：新活动是**独立实例**，各自有自己的 8h/12h 窗口，所以"上午场 + 下午场"每段 <8h 就安全；代价是中午会收到一次启动横幅（`start` 事件**必须带 `alert`**，不能静默启动） |
| ③ 全天模式 + 兜底 | 超过 8h 后靠普通通知接力（"还剩 2 小时"） | 体验有断层，不推荐 |

配置项：`ios.armWindow = "4h" | "2h" | "6h" | "all"`（设置页可选），默认 4h。

### 5.6 离线、陈旧与幂等

- **iOS 端本地继续走**：只要 `endAt` 还在，断网/PC 关机时倒计时仍然准（系统计时），只是副行/未读会旧。
- `staleAt`（= `endAt` + 5 分钟，或最后一次心跳 + 30 分钟）交给 `staleDate`：系统会把卡片置灰并显示"已过期"感，避免用户被旧状态骗。
- 每次推送带 `timestamp`（epoch 秒）；iOS 端 ContentState 里存 `lastTs`，**小于已应用值就丢弃**（APNs 不保证顺序）。
- 推送失败：Windows 端退避重试（1s/5s/30s），410/BadDeviceToken 时清 token 并提示"请在 App 里重新配对"。
- APNs token 变化：App 每次启动/`pushTokenUpdates` 都上报，Windows 端保留最近 3 个做灰度切换。

### 5.7 结束与降级

- 下班打卡后 `event: end`，`dismissal-date = now + 10min`（让用户看完"今日 10h32m"），10 分钟后从锁屏移除。
- 用户关闭实时活动权限（`ActivityAuthorizationInfo().areActivitiesEnabled == false`）/ 超 8h / 主动降级：全部改用**普通 APNs 通知**（`apns-push-type: alert`），内容与卡片一致（标题 + 剩余时间）。注意：**Live Activity 自己的 `alert` 没有 interruption-level**，只有普通通知能声明 `.timeSensitive`（需 `com.apple.developer.usernotifications.time-sensitive` entitlement）。
- 用户手动划掉实时活动：**不要立刻重开**。官方没有承诺"记住用户偏好"，但反复 start 属于滥用，容易触发审核/系统限制；当天以通知为主。
- **状态陈旧**：`staleDate` 到点后系统把活动标成 `.stale`（UI 里 `context.isStale`），卡片该置灰/弱化，不要假装数据新鲜。

---

## 6. 端到端架构

### 6.1 拓扑

```
┌──────────────────────────── Windows（仍是大盘）────────────────────────────┐
│  Widget (1s tick / 5min Lark / 15min 日程)                                    │
│     ├─ 状态机 StatusBuilder → CardStatus                                     │
│     ├─ IosBridge：快照 → diff → 事件 → 节流/合并                              │
│     └─ ApnsClient（curl / WinHTTP / relay）→ APNs                            │
│  设置页新增「iPhone」页：配对码、设备列表、测试推送、启动窗口(2/4/6/全天)        │
└───────────────┬──────────────────────────────────────────────────────────────┘
                │ HTTPS（不开放入站端口）
                ▼
        ┌───────────────┐        ┌──────────────┐
        │  APNs (HTTP/2)│───────▶│ iPhone        │
        │  api.push.apple.com │   │  Widget Extension（系统渲染）│
        └───────────────┘        │  App（设置/配对/按钮回传）    │
                                 └──────┬───────┘
                                        │ 打卡按钮 → 回传
                                        ▼
                              现有 MQTT 中继（复用 NoteRelay）或 relay
```

### 6.2 通道选型（含 Windows 侧的硬约束）

**约束**：项目用 `csc.exe v4.0.30319` 编译、目标 .NET Framework 4.x，`HttpClient` **只有 HTTP/1.1**；APNs 必须 HTTP/2。

| 选项 | 做法 | 优点 | 缺点 | 适用 |
|---|---|---|---|---|
| **A. `curl.exe` 子进程** | Windows 10 1803+ 自带 `curl.exe`（支持 `--http2`）；`--config` 文件传 header，避免 p8/参数出现在命令行 | 0 依赖、1 小时能打通 | 进程开销（一天几十次无所谓）、依赖系统 curl | **MVP / 打样** |
| **B. WinHTTP P/Invoke** | `WinHttpOpen` + `WINHTTP_OPTION_ENABLE_HTTP_PROTOCOL = HTTP/2`，同步 API 放后台线程 | 无外部依赖、可靠、可复用连接 | 约 250–350 行 P/Invoke，需处理 TLS/重试 | **正式版推荐** |
| **C. 中转服务** | Windows POST 到一个 Serverless（CF Worker/腾讯云函数），它负责 APNs JWT 与扇出 | Windows 端最简单；可多设备/多用户、可观测 | 多一套运维、p8 要放云端、隐私面变大 | 多设备/多人时 |
| C'. 复用 MQTT 传状态 | Windows publish → App 后台收 → 本地 `Activity.update` | 复用已有中继 | **iOS 不能常驻后台接收**，App 不在前台就收不到；不可靠 | ❌ 不可作为主通道，只用于配对/控制信号 |

**结论**：MVP 走 A，正式版走 B，配对与控制复用现有 MQTT（`xzt/v1/...`），状态更新永远走 APNs。

### 6.3 配对与 token 流（复用现有 5 位 ID / MQTT）

```
① Windows 设置页生成一次性配对码（6 位）+ 二维码
② iPhone App 扫码/输码 → App 请求通知权限 → 拿到 pushToken 与 pushToStartToken
③ App 发到 MQTT 主题 xzt/v1/pair/<pairCode>（一次性、TTL 5 分钟、payload 加密）
④ Windows 订阅该主题拿到 token → DPAPI 加密存 settings.json → 回推一条测试活动
⑤ 之后两端只在状态变化时通信；对端不在线也不影响计时（本地 interval）
```

- 配对码一次性 + 5 分钟过期 + 只能成功一次；token 存 DPAPI（`ProtectedData.Protect`），和现有 Outlook refresh token 同一套保护（`src/OutlookGraph.cs` 已有先例）。
- 公共 broker 明文传输 → **必须**换成自建/云 MQTT（TLS 8883 + 账号密码）再上生产；否则 token 泄露可被伪造推送（骚扰，不能读数据）。
- **两类 token 别搞混**（已核实）：`push-to-start` token 是**每 App 每设备一个**，App 装好并运行、观察 `pushToStartTokenUpdates` 即可拿到，**不需要先启动过一次活动**，用于远程创建；`pushTokenUpdates` 是**每个活动实例一个**，同一活动期间可能变化，变了要作废旧 token。首次配对只需让用户打开一次 App。
- 但**远程启动不是静默的**：`event:"start"` 的 payload **必须带 `alert`**，用户会看到一条横幅——这反而正好当"上岛提示"，设计上要接受它。

### 6.4 控制回传（打卡按钮）

Live Activity 里的按钮**必须用 `LiveActivityIntent`**（不是普通 `AppIntent`）：

- `LiveActivityIntent.perform()` 在 **App 进程**里执行（普通 AppIntent 在 widget extension 进程），因此**可以直接发网络请求**、写数据库；LA 视图本身是沙盒，不能联网。
- 按钮只在 **expanded 与锁屏**两种呈现里可用；**锁屏未解锁时按钮无效**，系统要求先认证；**CarPlay 里交互元素被系统禁用**。
- 动作：写 App Group 标记 + 发一个 HTTP/MQTT 请求（"请 Windows 代发 1/2"），然后本地 `Activity.update` 一条"打卡中…"；真正的打卡仍由 Windows 调 `lark-cli im +messages-send`（`src/Lark.cs:280`），因为飞书凭证在 PC 上。
- PC 不在线时：Intent 里检测不到确认 → 文案"电脑未在线，稍后自动补发" + 本地保底通知。
- 备选：按钮只做深链 `workcountdown://punch/out`，打开 App 再点一次（更可靠，多一次点击）。

### 6.5 安全

| 面 | 措施 |
|---|---|
| p8 私钥 | 只存 Windows；DPAPI 加密；不写日志；支持"重新生成 p8"引导 |
| device token | DPAPI 存；配对码一次性；410 即失效 |
| MQTT | 生产换 TLS + 认证；配对 payload 加密 + TTL |
| 隐私 | 不传飞书消息原文；只传时间戳、状态词、日程标题（≤40 字）；纸条内容走通知时截断 |
| 越权 | `apns-topic` 必须 `<bundleid>.push-type.liveactivity`，防止拿普通通知 topic 刷实时活动 |
| 审核 | Live Activity 必须与应用核心功能相关，不能当广告位/不能无意义常驻；TestFlight 自用不受此限 |

---

## 7. iOS 端实现

### 7.1 工程结构（新增 `ios/`）

```
ios/
├─ project.yml                      # XcodeGen（避免手改 pbxproj）
├─ WorkCountdown/
│  ├─ WorkCountdownApp.swift
│  ├─ RootView.swift                # 今日状态 + 配对页 + 设置（SwiftUI）
│  ├─ PushRegistrar.swift           # 请求权限、APNs 注册、token 上报
│  ├─ ActivityManager.swift         # start/update/end、push-to-start token
│  ├─ PunchIntents.swift            # 打卡 AppIntent
│  ├─ DeepLink.swift                # workcountdown://punch/out 等
│  └─ Assets.xcassets
├─ WorkCountdownWidgets/
│  ├─ WorkActivityAttributes.swift  # ★ App 与 Widget 共享
│  ├─ WorkLiveActivity.swift        # ActivityConfiguration + DynamicIsland
│  ├─ LockScreenWidget.swift        # 方案 A：accessory 小组件（可选）
│  └─ Info.plist                    # NSSupportsLiveActivities = YES
└─ WorkCountdownTests/
```

### 7.2 实时活动本体（骨架）

```swift
import ActivityKit
import WidgetKit
import SwiftUI

@main
struct WorkCountdownWidgets: WidgetBundle {
    var body: some Widget {
        WorkLiveActivity()
    }
}

struct WorkLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: WorkActivityAttributes.self) { context in
            LockScreenView(state: context.state, attrs: context.attributes)
                .activityBackgroundTint(Color(hex: "#12151A").opacity(0.82))
                .activitySystemActionForegroundColor(Color(hex: "#96A9FF"))
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading)  { StatusPill(state: context.state) }
                DynamicIslandExpandedRegion(.trailing) { TargetTime(state: context.state) }
                DynamicIslandExpandedRegion(.center)   { BigTimer(state: context.state) }
                DynamicIslandExpandedRegion(.bottom)   { BottomRow(state: context.state) }
            } compactLeading: {
                HStack(spacing: 4) { StatusDot(state: context.state); Text("下班").font(.caption2) }
            } compactTrailing: {
                CompactTimer(state: context.state)   // 5h30 / 42:18
            } minimal: {
                ProgressRing(state: context.state)
            }
            .keylineTint(Color(hex: context.state.accentHex))
        }
    }
}
```

### 7.3 生命周期

```swift
actor ActivityManager {
    static let shared = ActivityManager()
    private var current: Activity<WorkActivityAttributes>?

    // 1) 本地启动（App 在前台/用户点了"开始"）
    func start(attrs: WorkActivityAttributes, state: WorkActivityAttributes.ContentState) async throws {
        current = try Activity.request(
            attributes: attrs,
            content: ActivityContent(state: state, staleDate: state.staleAt),
            pushType: .token          // 关键：拿到 token 才能被 Windows 远端更新
        )
        for await token in current!.pushTokenUpdates { await PushRegistrar.shared.upload(token) }
    }

    // 2) 本地更新（用户点按钮后即时反馈）
    func update(_ state: WorkActivityAttributes.ContentState) async {
        await current?.update(ActivityContent(state: state, staleDate: state.staleAt))
    }

    // 3) 结束
    func end(_ state: WorkActivityAttributes.ContentState, after: TimeInterval = 600) async {
        await current?.end(
            ActivityContent(state: state, staleDate: nil),
            dismissalPolicy: .after(.now + after)
        )
        current = nil
    }

    // 4) push-to-start token（iOS 17.2+）：让 Windows 能在 App 没运行时直接开一张卡
    func observePushToStart() {
        Task {
            for await token in Activity<WorkActivityAttributes>.pushToStartTokenUpdates {
                await PushRegistrar.shared.uploadPushToStart(token)
            }
        }
    }
}
```

### 7.4 token 注册与权限

```swift
final class PushRegistrar: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    func request() async {
        let center = UNUserNotificationCenter.current()
        _ = try? await center.requestAuthorization(options: [.alert, .sound, .badge])
        UIApplication.shared.registerForRemoteNotifications()
    }
    func application(_ app: UIApplication, didRegisterForRemoteNotificationsWithDeviceToken token: Data) {
        let hex = token.map { String(format: "%02x", $0) }.joined()
        Task { await Pairing.upload(deviceToken: hex) }
    }
    func userNotificationCenter(_:willPresent:) async -> UNNotificationPresentationOptions {
        [.banner, .sound]
    }   // App 在前台也弹
}
```

Info.plist / capability 清单：

- `NSSupportsLiveActivities = YES`（必须，iOS 16.1+）
- `NSSupportsLiveActivitiesFrequentUpdates = YES`（**本方案不需要**；要更高推送预算时才加，且用户可单独关掉）
- Push Notifications capability（`aps-environment`）——**Live Activity 没有专属 entitlement**，用这个即可；但拿 APNs token 必须走 ActivityKit，不能用 UserNotifications 注册
- `com.apple.developer.usernotifications.time-sensitive`（只对**普通通知**降级链路有用，LA 的 alert 无法声明时效性）
- 深链 scheme `workcountdown`
- 最低版本：**iOS 17.2**（push-to-start 必需；16.1 只能本地 start，覆盖率已足够低，不值得为此做降级分支）。Xcode 用当前 iOS 27 SDK 编译，天然支持 iOS 27 的横屏灵动岛/`isDynamicIslandLimitedInWidth`。
- 平台：**iPhone / iPad / Mac（菜单栏）/ Apple Watch 智能叠放 / CarPlay 仪表盘**自动出现，StandBy 下锁屏卡被放大 2× 铺满；**visionOS 不支持**（从兼容 iPad/iPhone 的 App 发起会失败）。
- 用户可在「设置 → 通知 → 上班工具箱」按 App 关闭实时活动；代码里用 `areActivitiesEnabled` / `activityEnablementUpdates` 感知，关闭时走通知降级。

### 7.5 分发

- **付费 Apple Developer Program（$99/年）是硬性前提**，不是可选项：APNs 的 p8 Auth Key **只能在付费账号后台生成**。
- 自用：Xcode → 真机 → TestFlight（内部测试不需要审核）。Live Activity 本身**不要求上架 App Store**。
- 公开上架：审核红线（审核指南 4.5.3 点名 Live Activities）——不得展示广告/促销；必须只展示与**正在进行的事件/任务**相关的信息；不得滥用推送发未经请求的内容；Apple 建议"有明确开始和结束、不超过 8 小时"。倒计时类要么说清"这是当日工作的实时活动"，要么只做 TestFlight 自用。
- 免费账号 7 天签名过期 → 必须 TestFlight 或付费账号。

---

## 8. Windows 端改造

### 8.1 文件级改动

> **语法约束**：项目用 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 直接编译（`build.ps1:104`），默认 **C# 5**。新文件不能用 `?.` / `$"..."` / `nameof` / 表达式体成员；`async/await` 可用。若想用新语法，先把 `build.ps1` 换成 Roslyn（`/langversion:latest` + `Microsoft.CodeAnalysis.CSharp` 单文件包），那是另一件事。

| 文件 | 改动 | 说明 |
|---|---|---|
| **新增** `src/IosBridge.cs` | 快照 + diff + 事件队列 + 节流 + 重试 | 核心，约 400–500 行 |
| **新增** `src/ApnsClient.cs` | JWT(ES256) + HTTP/2 发送（curl 或 WinHTTP） | 约 200–350 行 |
| **新增** `src/IosPairing.cs` | 配对码、token 存储（DPAPI）、设备列表 | 约 150 行 |
| `src/Models.cs` | `Settings` 增加 `ios` 段；`CardStatus` 增加 `progressStartAt/progressEndAt`、`phase` 字符串（或直接由 IosBridge 计算）；`AttendanceSnapshot` 不变 | 小改 |
| `src/Widget.cs` | 在状态计算完成后调用 `iosBridge.OnStatus(status, plan)`；在打卡/日程/纸条/跨天事件处发 `OnEvent(...)`；`tick` 不需要接 iOS | 挂钩点 6–8 处 |
| `src/SettingsForm.cs` | 左栏加「iPhone」页：开关、启动窗口、配对码/二维码、设备列表、测试推送、日志 | 约 300 行 UI |
| `src/Program.cs` | 启动时初始化 IosBridge（读配置、起后台线程） | 10 行 |
| `build.ps1` | `$sources` 加 3 个新文件 | 3 行 |
| `package.ps1` | 版本号/说明（可选） | 不改逻辑 |

### 8.2 快照与 diff

```csharp
// IosBridge.cs（C# 5，不能用 ?. 和字符串插值）
internal class LiveSnapshot
{
    public string phase;            // working / lunch / ...
    public string headline;
    public string detail;
    public long startAt, endAt;     // Unix 秒
    public string mode;             // down / up / paused
    public double progress;
    public long progressStartAt, progressEndAt;
    public string secondaryLabel, secondaryValue, targetText;
    public int unread;
    public string tone;             // normal / soon / critical / success / alert / meeting
    public string accentHex;
    public long ts;                 // 单调递增，用于幂等
}

// 只在"语义变化"时才推，倒计时数字变化不推
bool Changed(LiveSnapshot a, LiveSnapshot b)
{
    if (a == null || b == null) return true;
    return a.phase != b.phase || a.headline != b.headline || a.endAt != b.endAt
        || a.mode != b.mode || a.tertiaryKey != b.tertiaryKey
        || Math.Abs(a.progress - b.progress) > 0.0015   // 只在进度跳超过 0.15% 时（约每 3–4 分钟一次）才考虑
        || a.unread != b.unread || a.targetText != b.targetText;
}

// 统一入口：状态变了 → 1.5s 合并窗口 → 发一条
void OnStatus(CardStatus st, WorkPlan plan) {
    var snap = Build(st, plan);
    if (!Changed(last, snap)) return;
    last = snap;
    queue.Post(snap);                 // 合并/节流在后台线程做
}
```

关键：**进度条不需要推送**（`ProgressView(timerInterval:)` 自己长），所以 `progress` 差异不应该触发推送；只有"模式/阶段/目标时间"变化才推。上面 `Changed` 里那一行进度判断仅用于"没有 interval 的暂停态"。

### 8.3 APNs 客户端（WinHTTP 版要点）

```csharp
// 1) JWT ES256：header {alg:ES256, kid:KEY_ID}，payload {iss:TEAM_ID, iat:now}
//    用 .NET Framework 的 ECDsaCng（CngKey.Import(p8, CngKeyBlobFormat.Pkcs8PrivateBlob)）签名
//    每 30–50 分钟重签（APNs 允许 token 最长 1 小时）
// 2) WinHTTP：WINHTTP_OPTION_ENABLE_HTTP_PROTOCOL = HTTP/2(0x2)，WinHttpOpen → Connect(sni) → OpenRequest
//    headers: :method POST / :path /3/device/<token> / apns-topic / apns-push-type /
//             apns-priority / apns-expiration / authorization: bearer <jwt>
// 3) body = UTF-8 JSON（≤ 4 KB），失败按 400/403/410 分类处理
```

若走 curl：`curl.exe --http2 -s -o NUL -w "%{http_code}" -X POST https://api.push.apple.com/3/device/<token> -H "authorization: bearer <jwt>" ... --data-binary @payload.json`，header 走 `--config` 文件，避免出现在命令行里。

> **⚠️ 别直接抄 .NET 的 `HttpClient` 示例**：研究底稿 §10.5 的 `DefaultRequestVersion = HttpVersion.Version20` 只在 **.NET 8+**（`SocketsHttpHandler`）上成立；本项目是 `csc.exe v4.0.30319` + .NET Framework 4.x，`HttpClient` 会**静默降级到 HTTP/1.1**，APNs 直接拒绝。要么上 WinHTTP P/Invoke / curl，要么把运行时换成 .NET 8（exe 从 445 KB 变成几十 MB，和"单文件小工具"的定位冲突）。
> **p8 私钥绝对不要打进分发的 exe**：泄露即可向你的 App 用户伪造推送；只放本机 `%APPDATA%`，用 DPAPI 加密，和现有 Outlook refresh token 同一套（`src/OutlookGraph.cs` 有先例）。

### 8.4 设置页「iPhone」页

```
┌ iPhone ─────────────────────────────────────────┐
│ 实时活动          [ 开 ]                         │
│ 上岛时机          [ 下班前 4 小时 ▾ ]             │
│ 桌面卡继续显示    [ 开 ]（关掉=只给手机看）        │
│ ─────────────────────────────────────────────── │
│ 设备：iPhone 15 Pro · 最近推送 2 分钟前 ✅        │
│ 配对码： 4 8 2 1 9 3        [ 重新生成 ]         │
│ [ 发送测试推送 ]  [ 打开推送日志 ]                │
│ 提示：需要 iOS App + TestFlight 包               │
└─────────────────────────────────────────────────┘
```

### 8.5 与现有功能的挂钩点

| 现有代码位置 | 事件 | 触发 |
|---|---|---|
| `Widget.cs` 状态计算后（`status = StatusBuilder.Build(...)`） | 任意状态变化 | `IosBridge.OnStatus` |
| `Widget.cs` `Refresh()` 打卡同步成功 | 上班/下班打卡 | `OnEvent("clockin"/"clockout")` |
| `Widget.cs` 日程提醒 `StepReminder` | 提醒开始/结束 | `OnEvent("reminder")` |
| `Widget.cs` 跨天检测 `lastDay` | 跨天 | `OnEvent("newday")` |
| `NotePanelForm.cs` / `Widget.cs` `OnNoteMessage` | 收到纸条 / 读完 | `OnEvent("note")` |
| `SettingsForm.cs` 保存皮肤/主题 | accent 变化 | `OnEvent("theme")`（节流） |

---

## 9. 各功能模块的 iOS 适配

| 模块 | 桌面做法 | iOS 做法 | 工作量 |
|---|---|---|---|
| 倒计时 + 进度 | 自绘每帧 | `timerInterval` 原生；阶段变化才推 | 小 |
| 午休 / 晚休行 | 第二行 | `phase = lunch/evening` + 副行 + 静态进度（暂停） | 小 |
| 下一段日程 | 一行文字 | ContentState 副行；日程变化推一次 | 小 |
| 会议/日程提醒 | 全屏扩散/警戒带/抽屉卡 | 时效性通知（标题/时间/地点/还剩）+ Live Activity 提醒态；不再做"覆盖桌面" | 中 |
| 该下班啦（常驻） | 整卡覆盖 + 每 5 分钟重播 | offAlert 态 + 每 5 分钟一条通知（可配，避免扰民） | 小 |
| 小纸条 | 面板 + 未读快闪 + MQTT | App 内聊天页（复用同一套 MQTT 协议与 5 位 ID）；锁屏只显示未读数 + 通知内容 | **大**（约等于重写面板） |
| 工资面板 | 18fps 小数跳动 | App 内页面；**不进锁屏** | 中 |
| 39 套主题 / 6 皮肤 | 全自绘 | 只透传 accent + 固定 quiet 深色皮肤；App 内可选主题 | 小 |
| 设置页 | WinForms 七页 | SwiftUI 简化版（配对/通知/启动时机/安静模式），其余配置仍回 Windows 改 | 中 |
| 右键胶囊 | 自绘菜单 | 深链进 App 对应页 | 小 |
| 托盘菜单 | WinForms | App 内按钮 + App Intents（快捷指令） | 小 |

> 小纸条是唯一"必须重做"的模块，建议二期。一期 iOS 端只做"状态展示 + 打卡按钮"，纸条继续只在 Windows 上看。

---

## 10. 分期实施计划

| 阶段 | 内容 | 产出 | 估时 | 验收 |
|---|---|---|---|---|
| **S0 打样** | 开发者账号 + 真机；一个静态 Activity（不接 Windows）；验证：锁屏卡尺寸/灵动岛四区/`timerInterval` AOD 行为/8h 行为/推送一条 | 1 张真机截图 | 0.5–1 天 | 能看到 iPhone 锁屏上"05:30:56"自己走 |
| **S1 MVP** | Windows `IosBridge` + `curl` 发 APNs；iOS App 配对 + `Activity.request`；上班打卡 start、下班 end、每小时对时；锁屏卡 4 行 + 灵动岛 compact/expanded | 可日常用 | 3–4 天 | 打卡后 5 秒内锁屏变化；断网 30 分钟时间仍准 |
| **S2 状态全覆盖** | 6 状态 + 午休/晚休 + 下一段日程 + 提醒态 + 未读 + AOD/深浅色 + 8h 分段策略 + 幂等/重试 | 完整对标桌面卡 | 3–4 天 | 见 §10.1 |
| **S3 体验与稳定性** | 提醒通知体系、持久化日志、token 失效处理、TestFlight 分发、电量实测 | 可交给家人用 | 2–3 天 | 24h 后台耗电 < 1%，30 天无人工干预 |
| **S4 可选** | 小纸条 iOS 版、工资页、主题同步、relay 多设备 | — | 1–2 周 | — |

### 10.1 验收清单（S2 结束）

- [ ] 12 小时内 6 个状态在锁屏/灵动岛都正确显示，文案与桌面一致
- [ ] 打卡后 ≤5s 切到"工作中"；下班打卡后 ≤5s 切到"已下班"并在 10 分钟后消失
- [ ] 午休开始/结束自动切态，进度条暂停不前进
- [ ] AOD 下不显示秒、不刺眼；浅色壁纸可读
- [ ] 断网 2 小时 + PC 关机 2 小时，倒计时仍然准（只副行旧）
- [ ] `stale-date` 生效后卡片置灰
- [ ] 推送乱序（人为重放旧时间戳）不会回退状态
- [ ] 关闭实时活动权限 → 自动降级为通知，不报错
- [ ] 一天推送条数 ≤30，电量无异常
- [ ] 8h 内无"卡片消失"：4h 启动窗口下 20:04 正常结束

---

## 11. 风险与对策

| # | 风险 | 影响 | 对策 |
|---|---|---|---|
| R1 | **8h/12h 上限** | 傍晚看不到卡 | §5.5 四小时启动窗口；分段重启（待验证重置行为） |
| R2 | **推送频率被节流** | 状态更新延迟 | 事件驱动 + 合并窗口 + 不推秒；失败本地补偿 |
| R3 | **Windows 无法 HTTP/2** | 直连 APNs 不通 | curl 子进程 → WinHTTP P/Invoke → relay 三级方案 |
| R4 | **开发者账号/证书** | 装不上机 | 付费账号（99 USD/年）+ TestFlight；免费账号 7 天到期 |
| R5 | **用户关权限/低电量** | 卡片不出现 | 通知降级；App 内明示"锁屏实时活动已关闭" |
| R6 | **App Store 审核** | 上不了架 | 自用走 TestFlight；公开版需强调"与工作日程强相关" |
| R7 | **PC 关机 = 大脑离线** | 状态不再更新 | 卡片仍按 endAt 走；长期做 relay 接管 |
| R8 | **公共 MQTT 明文** | 配对 token 泄露 | 生产换 TLS broker；配对码一次性 + 加密 |
| R9 | **颜色/动效还原度低** | 审美落差 | 接受"系统感"；把差异写进设计稿评审（见 mockup） |
| R10 | **维护成本翻倍** | 长期负担 | iOS 端只做"展示 + 打卡"，设置/日程/纸条仍回 Windows |
| R11 | **推送预算数值未公开** | 高频更新可能被节流，界面停在旧数据 | 默认 `priority 5`（不计预算）、事件驱动、一天 8–20 条；验收时观察真机是否被节流 |
| R12 | **AOD 下秒位行为未公开** | 常亮屏可能只显示到分钟 | 设计上 AOD 不展示秒；真机录屏核对后再决定 |
| R13 | **p8 私钥泄露** | 可向你的 App 用户伪造推送 | 只存本机 + DPAPI、不进 exe、泄露即吊销重签；token 失效靠 410 清理 |

---

## 12. 附：一天的时间线（12:00–20:30 班次，4h 启动窗口）

| 时刻 | 桌面事件 | iPhone 动作 | 推送 |
|---|---|---|---|
| 11:57 | 飞书读到上班卡（12:00 上班） | — | 无（未到启动窗口） |
| 12:00 | 工作中 | — | 无 |
| 13:30 | 午休结束 | — | 无 |
| 16:30 | 距下班 **4h00m** | **push-to-start**：锁屏出现卡片（start 必带 alert，用户收到一条横幅） | 1（start） |
| 18:00 | 晚休开始 | 切"晚休中"，进度暂停 | 1 |
| 18:45 | 晚休结束 | 切"工作中"，endAt 不变 | 1 |
| 19:30 | 距下班 1h | 大数字切 `分:秒`（端上本地算，无推送） | 0 |
| 20:00 | 距下班 30m，`HotBar` 触发 | 进度条 tone 变 soon（暖色） | 1 |
| 20:30 | 应下班 | 切"已达标"（正计时） | 1 |
| 20:30+ | 未打卡 | 切 offAlert + 通知 | 1–2 |
| 20:36 | 飞书读到下班卡 | 切"已下班 · 今日 8h36m" | 1 |
| 20:46 | — | `end` + 10 分钟后从锁屏消失 | 1 |
| **合计** | | | **≈8 条/天（不含提醒）** |

> 这段一天 8 条，就是"不推秒"直接带来的结果；桌面端光 1s tick 一天就要重绘约 8.6 万次（8 小时工作时段再叠 18fps 流光就是几十万帧），iOS 端只收到 8 条消息，其余全由系统计时器渲染。

---

## 13. 最小验证路径（不用等整个方案定稿）

| # | 今天就能做 | 验证什么 | 通过标准 |
|---|---|---|---|
| 1 | Xcode 新建工程 → Widget Extension → 50 行静态 Live Activity（写死 `endAt`），装到真机 | 锁屏卡/灵动岛四区真实尺寸、`Text(timerInterval:)` 是否自己走、AOD 表现 | 真机锁屏能看到 `05:30:56` 自己在走 |
| 2 | 打印 push token → 手写 `curl` 推一条 `event: update` | APNs 链路、`apns-topic` 后缀、payload 结构 | 锁屏卡内容变了 |
| 3 | 桌面卡加一个隐藏开关 `--ios-dump`，把 `CardStatus` 转成 ContentState JSON 打印 | 桌面状态机与 iOS 字段是否一一对应、文案能否直接搬 | JSON 能被 iOS 端解码并渲染 |
| 4 | 手工改系统时间 / 断网 / 关 PC | `timerInterval` 是否独立于网络、`staleDate` 是否置灰 | 时间仍准，卡片变灰 |

第 1 步半天内应有结论。**如果第 1 步的真机观感不能接受（太挤/太小/太丑），整个方案就不用往下做了** —— 这也是先做方案 A（锁屏小组件）再上 Live Activity 的原因。

---

## 附录 A：ContentState JSON 实例（真实文案，直接可对拍）

```json
{
  "phase": "working",
  "headline": "工作中",
  "detail": "距离下班",
  "startAt": 1763260920,
  "endAt": 1763280240,
  "mode": "down",
  "progress": 0.62,
  "progressStartAt": 1763260920,
  "progressEndAt": 1763280240,
  "secondaryLabel": "距晚休",
  "secondaryValue": "3h20m",
  "targetText": "下班 20:04",
  "unread": 3,
  "tone": "normal",
  "accentHex": "#96A9FF",
  "staleAt": 1763280540,
  "ts": 1763267400
}
```

对应 APNs 请求（更新）：

```
POST /3/device/<64位hex token>
:method: POST
:path: /3/device/<token>
authorization: bearer <ES256 JWT>
apns-topic: com.ocezeus.workcountdown.push-type.liveactivity
apns-push-type: liveactivity
apns-priority: 10
apns-expiration: 0
content-type: application/json

{
  "aps": {
    "timestamp": 1763267400,
    "event": "update",
    "content-state": { ...上面的对象... },
    "stale-date": 1763280540,
    "relevance-score": 60
  }
}
```

启动（`push-to-start`，iOS 17.2+）与结束：

```json
// event: start —— 带 attributes-type / attributes
{ "aps": { "timestamp": 1763267400, "event": "start",
  "attributes-type": "WorkActivityAttributes",
  "attributes": { "workDate": "2026-11-16", "id": "wc-20261116" },
  "content-state": { ... }, "alert": { "title": "上班工具箱", "body": "开始记录今天的工作" } } }

// event: end —— 带 dismissal-date
{ "aps": { "timestamp": 1763282400, "event": "end",
  "content-state": { "phase": "done", "headline": "已下班" },
  "dismissal-date": 1763282700 } }
```

---

## 附录 B：为什么"不推秒"在代码上成立

桌面端每秒算一次 `big`/`progress`，iOS 端一次都不算推送，靠三件套：

| 桌面每帧算的量 | iOS 端谁算 | 何时需要推送 |
|---|---|---|
| `target - now` 倒计时 | 系统 `Text(timerInterval:)` | 目标时间变了（打卡/改班次） |
| `(now-start)/(target-start)` 进度 | 系统 `ProgressView(timerInterval:)` | 时段切换（午休/晚休）|
| 午休/晚休剩余 | 系统计时 + 副行文案 | 时段开始/结束 |
| `已工作 XhYm` | iOS 端按 `startAt` 算（每秒重算但不推送） | 不需要 |
| 超时/迟到时长 | 系统计时（`countsDown: false`） | 状态迁移 |
| 到点"该下班啦" | iOS 端本地判断 `now > endAt`（可本地切 UI） | 只推一次确认 |

**唯一必须精确到秒的边界是"到点"和"阶段迁移"**，这些都是一天个位数事件。

---

*本方案的所有 iOS 侧限额、尺寸、API 名称以 `docs/research-ios-liveactivity.md` 的核查结果为准；视觉规格以 `docs/card-spec-extract.md` 为准；布局以 `design/ios-live-activity.html` 出图为准。*
