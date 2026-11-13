# v3.5.0 独立验收报告 — ① 固定班次工作日勾选错位修复 ② 个人昵称 ③（增量）旧配置工作日迁移

验收人：note-verify（独立探针，不依赖 Lead 的测试）
验收对象：`src/` 最终代码（Models 17:28 / NoteRelay 17:07 / NotePanelForm 17:06 / Widget 17:06 / SettingsForm 17:13）
验收时间：2026-11-13 17:25 初验 + 17:34 迁移增量（150% DPI，会话锁屏 → 离屏渲染 + 反射 + 本地假 MQTT broker，不依赖真鼠标）
复跑命令：`.\test\verify\run-verify-v35.ps1`（编译 `test/verify/NoteVerifyProbeV35.exe` → **50 条断言** → 出图；只写 `test/verify/**`，不覆盖 `test\Check.exe` / `test\Test.exe`）

## 结论（一句话）

**两条需求 + 数据迁移全部 ✅ 通过。** 独立探针 **ALL PASS (50 checks)**，退出码 0。没有发现需要修改 `src/` 的缺陷；文末列了 5 条不阻塞的观察。

| 需求 | 结论 | 关键数字 |
| --- | --- | --- |
| 1. 固定班次工作日勾选错位 | ✅ | 映射 `WorkdayArrayIndex(0..6)=1,2,3,4,5,6,0`；UI 勾周一~周五 → `workdays=0,1,1,1,1,1,0`；`Policy.Build(2026-11-13 周五).isWorkday = **True**`（修复前为 False）；7 天逐项往返、3 个快捷按钮全过 |
| 2. 个人昵称 | ✅ | 模型/设置页/面板/Widget 全链路；假 MQTT broker 抓到「自己的 retained presence 带昵称」「订阅所有联系人 presence」「Profile 事件（网络线程）」「消息 payload 带昵称」「keepalive 重播」；群聊气泡/历史行显示昵称、无昵称退回 `ID xxxxx：`；面板头部显示「我的 ID 46040 · 阿哲」 |
| 3.（增量）旧配置工作日迁移 | ✅ | 旧错位配置 `version=1, workdays=1,1,1,1,1,0,0` → Normalize 后 `workdays=0,1,1,1,1,1,0`、`version=2`、周五 `isWorkday=True`；`周一~周六` 形态迁移正确；正常配置/version=2/其它形态不误迁；幂等；`SettingsStore.Load` 在 `WC_NO_SAVE=1` 下不写盘、放开后立刻落盘 v2（实测磁盘 version=2 + 周一~周五） |

## 独立证据 / 可追溯信息

- 源码哈希（SHA256 前 16 位）：
  - `src/Models.cs` `7A3CD965AFC837C6`（132,194 B，17:28:27，含迁移代码）
  - `src/NoteRelay.cs` `2E481F1FA3889799`（22,293 B，17:07:24）
  - `src/NotePanelForm.cs` `2CCB085E0D757DF2`（88,817 B，17:06:04）
  - `src/Widget.cs` `FBF0F6C75922523C`（191,768 B，17:06:38）
  - `src/SettingsForm.cs` `329E78C05E331216`（163,062 B，17:13:31）
- 装机版（Lead 17:29 重新打包装机，已交叉核对）：`%LOCALAPPDATA%\Programs\WorkCountdown\WorkCountdown.exe` **FileVersion 3.5.0.0**，**SHA256 = `878D008E8898A24AD60965B5A4460FD97476B39B3074F5B4F37D75019C40155B`**（385,024 B，17:29:12），与 `dist\WorkCountdown.exe` 哈希**一致**；验收时 **PID 12712（17:29:13）**。
  - 说明：这份 3.5.0 是在初验之后补了迁移代码的重新打包，所以 hash 与初验报告里的 `AAA6FBBFAD9B14E4`（380,928 B）不同；旧 hash 已作废，以本条为准。
- 真机配置交叉核对（只读）：`%APPDATA%\WorkCountdown\settings.json` 实测 `"version":2`、`workdays=[false,true,true,true,true,true,false]`（周日休、周一~周五上班、周六休），文件 17:29:14 被写入 → **真实机器上迁移已生效并落盘**。
- 探针：`test/verify/NoteVerifyProbeV35.exe`；完整断言日志：`test/verify/run-v35.log`（50 PASS / 0 FAIL / exit 0）。
- 截图：`test/verify/v35-shots/note-header-nick.png`、`note-header-empty.png`、`note-room-nick.png`。
- 迁移测试用 `SettingsStore` 便携模式（临时 `test/verify/portable.txt` + `test/verify/data/`），跑完自动删除，不碰用户配置；`WC_NO_SAVE=1` 全程保证默认不写盘。
- 全程只读 `src/`，只写 `test/verify/**`；`test\Check.exe`、`test\Test.exe` 未被覆盖（当前是 Lead 17:28:56/57 重编译的版本）；未跑 build-tests / install / package。

---

## 需求 1：固定班次工作日勾选错位

**结论：✅ 修复正确。** UI 顺序「周一..周日」与 `Settings.workdays`（0=周日..6=周六）的映射统一走 `WorkdayArrayIndex`，读写双向都对齐；「周一~周五上班」保存后周五=上班、周日=休息，`Policy.Build` 判定正确。

### 1.1 映射函数（A1）

`SettingsForm.WorkdayArrayIndex(0..6)` 实测 = `1,2,3,4,5,6,0`，即 UI 周一→数组 1（周一）、UI 周日→数组 0（周日）。

### 1.2 打开设置页回填（A2）

构造 `Settings.workdays = {false,true,true,true,true,true,false}`（周日休、周一~周五上班、周六休）→ 打开 `SettingsForm`：

- 7 个 `cbDays` 文本顺序实测 `周一,周二,周三,周四,周五,周六,周日`；
- 勾选状态实测 **`1,1,1,1,1,0,0`**（周一~周五勾、周六周日不勾）。

### 1.3 保存方向（A3 / A4）

- 勾选 `cbDays` 周一~周五 → `Save(false)` → `s.workdays = **0,1,1,1,1,1,0**`（[0]=周日 false、[1..5]=周一~周五 true、[6]=周六 false），`Save` 返回 true。
- 7 天逐项往返（每天单独勾上再保存）全部正确：`UI0→arr1 ✓ UI1→arr2 ✓ UI2→arr3 ✓ UI3→arr4 ✓ UI4→arr5 ✓ UI5→arr6 ✓ UI6→arr0 ✓`。
  - 修复前的 bug 正是这个映射：旧代码 `s.workdays[i] = cbDays[i]`，勾周一~周五会写成 `workdays[0..4]=true` → **周日、周一~周四上班，周五、周六休息**；所以修复前 `Policy.Build(周五).isWorkday=false`。现在 A4 的 7 项逐项断言可以防止再错位。

### 1.4 快捷按钮（A5）

- 设置页实测存在三个按钮：「周一~周五」「周一~周六」「每天」。
- `SetWorkdayPreset(5)` → 复选框 5 勾，保存后 `workdays=0,1,1,1,1,1,0`；
- `SetWorkdayPreset(6)` → `workdays=0,1,1,1,1,1,1`（周日休）；
- `SetWorkdayPreset(7)` → `workdays=1,1,1,1,1,1,1`。

### 1.5 端到端判定（A6，用户原话场景）

走和用户相同的路径（打开设置 → 勾周一~周五 → 保存），然后 `Policy.Build`：

- `2026-11-13`（**周五**）`isWorkday = **True**` ← 修复前是 False（用户看到「周五该上班却显示休息」）
- `2026-11-16`（周一）`isWorkday = True`
- `2026-11-14`（周六）`isWorkday = False`
- `2026-11-15`（周日）`isWorkday = False`

> 证据截图：本轮没有真机设置页截图（设置窗是模态窗，未做 UI 自动化）；上述结论来自对 `SettingsForm` 真实控件（`cbDays`）的读写 + 真实 `Save(false)` + 真实 `Policy.Build`，用的是生产同一套代码路径。

---

## 需求 2：个人昵称

**结论：✅ 通过。** 昵称字段、显示优先级、presence 广播/订阅、Profile 事件、消息携带、设置页输入框、面板头部/群聊显示、Widget 落盘全部验证通过。

### 2.1 模型层（B1…B9）

| 断言 | 实测 |
| --- | --- |
| `NoteSettings.MaxNick` | = 12 |
| `CleanNick` | null→`""`；`"  阿哲  "`→`"阿哲"`；超 12 字截断到 12 |
| `Normalize` | `nickname="  阿哲  "` → `"阿哲"`（超长同样截 12） |
| 新联系人 `nick` 默认 | `""`，`Display()` 退回 `ID 10001` |
| Display 优先级 | 无备注有昵称 → `小满`；有备注 → `满哥`（备注优先）；对话码无昵称 → `对话 abcde` |
| `SetContactNick` | trim 后写入；同值返回 false；联系人不存在返回 false；超 12 截断（实测存 `一二三四五六七八九十甲乙`，12 字） |
| `NoteMessage.fromNick` | 构造函数带昵称可写入（`小甲`） |

### 2.2 中继端到端（C1…C8，本地假 MQTT broker 抓包）

探针在 `127.0.0.1:<随机端口>` 起假 broker，走真实 `NoteRelay` 连接/收发：

| 断言 | 实测 |
| --- | --- |
| C1 连上后订阅 | `[xzt/v1/msg/46040, xzt/v1/room/6162636465, xzt/v1/presence/10001, xzt/v1/presence/10002]` —— 所有个人联系人 presence retained 主题都订阅了（房间主题仍正常） |
| C2 CONNACK 广播自己 | 抓到 PUBLISH `xzt/v1/presence/46040`，`retain=True`，payload `{"v":1,"id":"46040","ts":...,"nick":"阿哲"}` |
| C3 收到对方 presence → Profile | 事件参数 `(id=10001, nick=小满, online=True)`；**在子线程 raise**（实测 thread=3，主线程=1）；`online` 按 120s 窗口判定 |
| C4 过期 presence | 600 秒前的 presence → `online=False`，昵称仍解出 `老周` |
| C5 改昵称立刻广播 | `RefreshSubscriptions()` 后抓到新 presence payload 带 `"nick":"阿哲2"` |
| C6 消息带昵称 | `SendMessage("10001","你好")` → PUBLISH topic `xzt/v1/msg/10001`，payload `{...,"nick":"阿哲2"}` |
| C7 解码带昵称 | `DecodeMessage(... "nick":"小甲")` → `NoteIncoming.nick == "小甲"` |
| C8 keepalive | 连接后约 **15.0s** 抓到 keepalive 重播的 retained presence，payload 仍带当前昵称 `阿哲2` |

### 2.3 设置页 / 面板 UI（D1…D7）

- `D1`：设置 → 纸条页有 `tbNoteNick` 输入框，能读出当前昵称 `阿哲`，`MaxLength=12`。
- `D2`：把输入框改成 `"  小满  "` → `Save(false)` → `s.note.nickname == "小满"`（保存会 trim）。
- `D3`：面板头部「我的 ID xxxxx · 昵称」：有昵称（阿哲）/ 无昵称两版渲染，副标题区域像素差 **332 px**；截图 `note-header-nick.png`（可见「我的 ID 46040 · 阿哲」）与 `note-header-empty.png`。
- `D4`：群聊气泡实测：
  - 消息带 `fromNick="小甲"` → `小甲：你好`
  - 无 fromNick、但本机该 ID 联系人有 nick `小满` → `小满：在吗`
  - 都没有 → `ID 22222：没有昵称的人`
  - 自己发的 → 原样 `我自己`（不加前缀）
- `D5`：历史行「谁」：`小甲` / `小满` / `ID 22222` / 自己=`你`。
- `D6`：本机备注优先于对方昵称：备注改成 `满哥` 后气泡 `满哥：在吗`、历史行 `满哥`。
- `D7`：个人对话没备注时列表显示对方昵称（`Display()=="小满"`）。
- 截图 `note-room-nick.png`：群聊里一条无昵称消息显示 `ID 22222：没有昵称的人`，自己发的不带前缀；左侧联系人胶囊显示「小满」「小甲」（昵称）。

### 2.4 Widget 接线（E1…E5）

| 断言 | 实测 |
| --- | --- |
| E1 `OnNoteProfile` | 调用后 `contact.nick` 写入 `小满`（BeginInvoke 消息泵真跑） |
| E2 陌生 ID profile | 不会凭空建联系人（`contacts 2→2`）——只更新已有对话的昵称，符合设计 |
| E3 收到消息补昵称 | `msg.nick="小满2"` → `contact.nick="小满2"` 且消息 `fromNick="小满2"` |
| E4 群聊消息昵称 | 房间消息 `from=22222, nick=乙` → 线程消息 `fromNick="乙"` |
| E5 设置保存后重启 relay | `Widget` 有 `ApplySettings → RestartNoteRelay` 接线（`ApplySettings` 内调用，见 `src/Widget.cs:1588`；设置窗 OK 后 `OpenSettings` 调 `ApplySettings`，见 `Widget.cs:3284-3290`）→ 新 relay 用最新 `cfg.nickname` 在 CONNACK 广播（C2 已验证）。注：探针环境带 `WC_NO_RELAY=1`，`RestartNoteRelay` 会提前返回，所以这条是「代码路径 + relay 层行为」合并证据。 |

---

## 增量：v3.5 旧配置工作日迁移

**结论：✅ 正确。** 只修 UI 映射无法改变用户已被旧 bug 写坏的本机配置（真机原状：`workdays=[日,一,二,三,四]=上班、周五周六休`）。`SettingsExtensions.MigrateLegacyWorkdays` + `Normalize` 的 `version<2` 一次性迁移能把这类错位配置还原成真实意图；正常配置不误伤。

### 迁移规则（以实际代码为准，`src/Models.cs:1525-1554`）

- 触发条件：`version < 2` **且** 数组长度 7 **且** 形态为「周日上班 + 周一~周四都上班 + 周六休息」= `w[0]&&w[1]&&w[2]&&w[3]&&w[4]&&!w[6]`；
- 迁移：`fixedDays[(i+1)%7] = w[i]`（整体右移一位，周日→周六、周一→周日、…）；
- `Normalize()` 里调用并把 `version` 升到 2（只迁一次）；`SettingsStore.Load()` 发现 `version` 变了立刻 `Save` 落盘（`WC_NO_SAVE=1` 时 `Save` 直接 return）。

### 断言与实测（M1…M11）

| 断言 | 实测 |
| --- | --- |
| M1 迁移函数：`version=1` + 旧错位形态 → 右移一位 | 入参 `1,1,1,1,1,0,0` → 输出 `0,1,1,1,1,1,0`（居中：周日休 / 周一~周五上班 / 周六休），且不改入参数组 |
| M2 迁移函数边界 | 正常 `0,1,1,1,1,1,0`（version=1）**原样返回**；`version=2` 的旧形态原样返回；`null` 原样返回 `null`；长度 6 原样返回 |
| M3 用户真机旧形态 → Normalize | `version=1, workdays=1,1,1,1,1,0,0` → `workdays=**0,1,1,1,1,1,0**`、`version=**2**`、`Policy.Build(2026-11-13 周五).isWorkday=**True**` |
| M4 旧「周一~周六」形态 | `1,1,1,1,1,1,0` → `0,1,1,1,1,1,1`；周六 `isWorkday=True`、周日 `False` |
| M5 幂等 | 迁移后再 `Normalize()` 一次：`workdays` 不变（`0,1,1,1,1,1,0`）、`version` 仍 2 |
| M6 默认配置不误迁 | `version=1, workdays=0,1,1,1,1,1,0` → 不变 |
| M7 已是 version=2 的旧形态不再迁移 | `version=2, workdays=1,1,1,1,1,0,0` → 不变（只迁一次） |
| M8 其它形态不误迁 | 「每天」「全休」「周二~周四」「周一+周三~周四」4 种形态 Normalize 前后完全一致 |
| M9 `SettingsStore.Load()` + `WC_NO_SAVE=1` | 便携模式临时目录：读到 v1 旧配置 → 内存里迁移成 `workdays=0,1,1,1,1,1,0`/v2，但**磁盘仍是 v1**（不写盘、不报错） |
| M10 `SettingsStore.Load()` 允许写盘 | 同一份旧配置再 Load：内存 v2，**磁盘立刻变 version=2 + workdays=0,1,1,1,1,1,0**（落盘逻辑真实执行） |
| M11 version=2 不重复写盘 | 再 Load 一次，文件 mtime 完全不变（`09:34:44.301 → 09:34:44.301`），不会每次启动都重写 |

### 真实机器交叉核对（只读）

- `%APPDATA%\WorkCountdown\settings.json`：`"version":2`、`workdays=[false,true,true,true,true,true,false]`（周一~周五上班、周六周日休），文件写入时间 17:29:14（新装机启动后）——**迁移确实在用户机器上生效并落盘**。
- 与 Lead 描述一致：真机设置页现在「周一~周五」勾选、卡片显示「工作中」。

---

```powershell
cd D:\DSH\RWZ0\work-countdown
.\test\verify\run-verify-v35.ps1
#   -> 编译 test\verify\NoteVerifyProbeV35.exe（src\*.cs 去掉 Program.cs + 探针）
#   -> 跑 50 条断言；ALL PASS (50 checks) / exit 0（含 ~15s keepalive 等待 + 迁移便携模式测试，整轮约 20–25s）
#   -> 截图到 test\verify\v35-shots\；日志抓法：
#      & .\test\verify\run-verify-v35.ps1 *>&1 | Set-Content test\verify\run-v35.log -Encoding UTF8
```

## 剩余风险 / 小观察（都不阻塞）

1. **昵称走公共 MQTT 明文**：`broker.hivemq.com` 是公共中继，`xzt/v1/presence/<id>` 是 retained 主题——任何人订阅都能看到「5 位 ID + 昵称 + 最后在线时间」。ID 本来也是公开的，昵称只是多了一点可见信息；介意的话可在设置页提示「昵称会随在线状态公开」。
2. **老消息不会补昵称**：`fromNick` 是 v3.5 起随消息写入的；升级前收到的消息 `fromNick` 为空，群聊里会退回「ID xxxxx：」，要等对方再发一条才会显示昵称。这是数据兼容的自然结果。
3. **昵称只在本机已有该对话时写入**（E2）：陌生 ID 的 presence 被丢弃，避免列表凭空冒人；符合「先有对话再显示昵称」的预期。若以后想「按 ID 搜人时顺便看到昵称」，需要在搜索路径单独处理。
4. **`SettingsForm.Save` 用 `.Trim()` 写 nickname**，不是 `CleanNick`：UI 输入框有 `MaxLength=12`，正常路径不会超长；即便配置被手工改超长，`NoteSettings.Normalize` 也会截断（B3 已验证），使用端安全，不算缺陷。
5. **迁移只认「精确的旧错位形态」**：触发条件是 `version<2` 且 `[周日上班、周一~周四都上班、周六休]`。如果用户旧配置是更奇怪的组合（例如只勾了周日 + 周三，或周五也勾了），不会被迁移、仍按原值使用。这是有意的保守策略（避免把刻意的特殊排班改掉）；用户真机命中的是标准形态，已还原成功。
6. 装机交叉核对：**FileVersion 3.5.0.0**、装机 exe 与 `dist\WorkCountdown.exe` SHA256 一致（`878D008E8898A24AD60965B5A4460FD97476B39B3074F5B4F37D75019C40155B`，385,024 B，PID 12712 / 17:29:13）。探针本身不依赖装机，装没装结果一致。

## 附：本轮探针自身修过 2 处（非实现缺陷，如实记录）

- `B` 段一开始用 `params object[]` 传 `null` 触发 NRE → 改成 `new object[] { null }`。
- `D5` 预期把历史行回退写成 `22222`，而实现是 `ID 22222`（与气泡 `ID xxxxx：` 一致）→ 按实际行为修正预期。
- 迁移段 `M9/M10` 用 `SettingsStore` 的便携模式（临时 `portable.txt` + `data/settings.json`）真实走 `Load()` 落盘逻辑，测试目录只在 `test/verify/**` 内、跑完自动删除；不是靠 mock。

## 附：断言 ↔ 需求对照

| 需求 | 断言编号 | 条数 |
| --- | --- | --- |
| 1 工作日勾选错位 | A1、A2、A2b、A3、A4、A5、A5b、A5c、A5d、A6 | 10 |
| 2 个人昵称（模型） | B1…B9 | 9 |
| 2 个人昵称（中继端到端） | C1…C8 | 8 |
| 2 个人昵称（UI） | D1…D7 | 7 |
| 2 个人昵称（Widget） | E1…E5 | 5 |
| 3 旧配置工作日迁移 | M1…M11 | 11 |
| **合计** | | **50** |
