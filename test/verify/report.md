# 独立验收报告 —「上班工具箱」用户 4 条反馈（task-2）

验收人：note-verify（独立探针，不依赖 Lead 的测试）
验收对象：`src/` 最终代码（Models 15:42 / NoteRelay 15:44 / NotePanelForm 16:23 / Widget 16:03 / SettingsForm 15:53）
验收时间：2026-11-13 16:27（150% DPI，会话锁屏 → 全部用离屏渲染 + 反射，不依赖真鼠标）
复跑命令：`.\test\verify\run-verify.ps1`（编译探针 → 71 条断言 → 出图 → before/after 对比；不覆盖 `test\Check.exe` / `test\Test.exe`）

## 结论（一句话）

**4 条需求全部 ✅ 通过。** 独立探针 **ALL PASS (71 checks)**，退出码 0；另外用真机安装版抓图交叉验证了大卡片图标。没有发现需要修改 `src/` 的问题；文末 2 条小观察均已核实/已按复核调整（对话码「查在线」UI 实际是正确分流；双栏行数已从 4 调到 5）。

| 用户需求 | 结论 | 关键数字 |
| --- | --- | --- |
| 1. 添加联系人 → 添加对话（任意 5 位字符 = 对话码；5 位数字 = 1:1；保留个人 ID） | ✅ | 模型/面板/设置页/真实中继 31 条断言全过；房间主题 `xzt/v1/room/6162636465` 收发均抓到；房间气泡与历史都显示发送者 ID |
| 2. 小纸条卡片边框稍微粗一点 | ✅ | `CardBorderDip = 1.80`（原 1 DIP）；150% 下顶/底像素带 **2 行 → 4 行**，聊天/添加/历史/双栏四种页面都变粗 |
| 3. 多对话可滚轮滚动 + 右键删除 | ✅ | 8 个对话：抽屉每屏 4 个、双栏每屏 5 个（历史态 10 行）；滚轮 1 格 = ±1，clamp 到 0…4 / 0…3，末屏可见第 8 个；右键出红色「删除」，点击后联系人 + 本机线程一起删并通知 Widget |
| 4. 大卡片 5 个按钮大小/高度一致 | ✅ | 可见包围盒宽 18…20px（极差 **2**，原 15）、高 18…20px（极差 **2**，原 8）、中心 y 极差 **1px**；真机截图同样满足 |

## 独立证据 / 可追溯信息

- 最终源码哈希（SHA256 前 16 位）：
  - `src/Models.cs` `856C7B60C632C092`、`src/NoteRelay.cs` `0A411EED495225C5`、`src/NotePanelForm.cs` `6773BD34A47F2085`（87,821 B，16:23:08）、`src/Widget.cs` `20660FAE61DAFEA2`、`src/SettingsForm.cs` `6251722470B27AE0`
- 装机版：`%LOCALAPPDATA%\Programs\WorkCountdown\WorkCountdown.exe` SHA256 前 16 位 `8FA235C7DAD048AD`（380,928 B，16:23:40，FileVersion 3.3.2.0），与 `dist\WorkCountdown.exe` 哈希一致；验收时 PID **35852**。
- Lead 侧同版本测试：`.\test\build-tests.ps1` → **Test 223 / Check 268 两套全绿**；`test\NoteClick.exe` **ALL PASS（28 项）**；`test\CardGlyphProbe.exe` 17 PASS（我未复跑这三套，只作为同版本状态记录；独立验收以我的探针为准）。
- 探针：`test/verify/NoteVerifyProbe.exe`（416,768 B，独立 exe；`test/Check.exe`、`test/Test.exe` 仍是 Lead 的 16:23:25 版本，未被覆盖）。
- 完整断言日志：`test/verify/run-final.log`（71 PASS / 0 FAIL / 0 SKIP / exit=0）。
- 改动前基线：`test/verify/baseline-metrics.txt`（旧 `NoteShot.exe` 14:37 + 旧装机版真机 `shot.ps1`，早于本轮改动）。
- 改动后截图目录：`test/visual/verify-after/`（含 `shots/` 子目录）；改动前对照：`test/visual/verify-before/`。

---

## 需求 1：添加联系人 → 添加对话（任意 5 位字符 = 对话码）

**结论：✅ 通过。** 5 位纯数字仍是和该 ID 的 1:1；其它任意 5 位字符（字母/中文/混合）是对话码，同码进同一对话；个人 5 位 ID 全程保留并显示。

### 1.1 输入规则（模型层，R1-01…R1-10）

| 断言 | 实测 |
| --- | --- |
| 5 位数字同时是合法「码」 | `ValidCode("12345")=true` |
| 5 位字母/中文是合法对话码 | `ValidCode("abcde")=true`、`ValidCode("讨论组ab")=true` |
| 4 位 / 6 位 / 含空格 / 空串 / 全空格被拒 | 全 `false` |
| 5 位字符不是个人 ID；0 开头不是 ID | `ValidId("abcde")=false`、`ValidId("01234")=false`、`ValidId("12345")=true` |
| `IsRoom`：非数字 5 位=true，5 位数字=false | `abcde=true`、`12345=false` |
| `AddConversation` 建对话码对话（联系人与线程都建） | `ret=true`、联系人与线程都在 |
| 5 位数字建 1:1，ID 原样保留 | `IsRoom=false`、`id=="12345"` |
| 显示名 | 对话码无备注=`对话 abcde`；数字无备注=`ID 23456`；有备注=`林总`（备注优先） |
| 非法输入不建对话 | 联系人数不变（3→3） |
| 同码重复添加 | 不重复建、备注更新（count=1） |

### 1.2 对话码 → 中继主题（R1-11/12/21）

- `NoteRelay.RoomTopic("abcde")` = `xzt/v1/room/6162636465`（`abcde` 的 UTF-8 十六进制）；
  `RoomTopic("讨论组ab")` = `xzt/v1/room/e8aea8e8aebae7bb846162`（中文按 UTF-8，不是 GBK/ASCII）。
- `ParseRoomTopic` 能反解回原码（中英文都行）；`xzt/v1/msg/...` 主题、坏十六进制都被拒；`HexEncode/HexDecode` 往返一致。
- 改动前：`NoteRelay.cs` 只有 `xzt/v1/msg/<id>` 和 presence，没有 `room` 主题（基线记录在 checklist/基线说明）。

### 1.3 真实中继端到端（R1-17/18/31，本地假 MQTT broker 抓包）

探针在 `127.0.0.1:<随机端口>` 起一个假 broker，收 CONNECT → 回 CONNACK，然后抓 NoteRelay 真实发出的 MQTT 包：

- 连接后订阅：`[xzt/v1/msg/46040, xzt/v1/room/6162636465, xzt/v1/room/e8aea8e8aebae7bb846162]` —— 本机收件箱 + 两个对话码房间主题都订阅了（同码的人才能进同一对话）。
- 往 `abcde` 发一条：抓到 PUBLISH `topic=xzt/v1/room/6162636465`，
  `payload={"v":1,"from":"46040","to":"abcde","text":"房间测试","ts":...}` —— 发布走房间主题，payload 带本机 5 位 ID。
- 已连接状态下**新增**对话码 `新码abc` → 立刻补订阅 `xzt/v1/room/e696b0e7a081616263`（不用重连）。

### 1.4 消息路由与「保留每个人的 ID」

- `Append` 路由：`11111→abcde`、`22222→abcde`、`46040→abcde` 三条消息都进**同一个** `abcde` 线程；`from` 原样保留 `11111/22222/46040`，`to` 是码 `abcde`（R1-13/14/15）。
- 收包解码：`DecodeMessage(BuildMessageJson("11111","abcde",...))` 正常解出 `from=11111, to=abcde`（R1-16）。
- Widget 收消息接线（反射真调 `OnNoteMessage` + 消息泵）：两个不同发送者进同一线程；已被删掉/不存在的对话码收到消息**不会**重建对话；1:1 陌生人消息会自动按对方 ID 建对话（R1-25/26/27）。

### 1.5 房间对话里显示发送者 ID（用户「保留每个人 ID 方便直接建对话」）

- 气泡文字（R1-22）：别人发进对话码的消息 = `ID 11111：你好`（带发送者 5 位 ID 前缀）；自己发的不带前缀；1:1 消息不带。
- 历史行「谁」（R1-23）：对话码对话显示 `11111`，自己=`你`，1:1=`对方`。
- 抽屉式补了「名字 + 对话码」头行（R1-24，见截图）。
- 像素级：同一房间、同文同时间、只换发送者 `11111`→`22222`，两张渲染图差异 760 px（集中在气泡文字区）——证明发送者 ID 真的画出来了（R1-19）。

### 1.6 添加对话 UI（面板 + 设置页）

- 面板「＋ 新建对话」：标题「＋ 新建对话」、字段「对方的 5 位 ID / 5 位对话码」、提示「5 位数字 = 和这个人单独聊；其它 5 位字符 = 对话码，别人输入同一个码就进同一个对话」、按钮「创建对话」（截图 `note-drawer-add-room.png`）。
  - 输 `abcde` → 回调 + 建对话码对话 + 状态「已创建对话 abcde（同码的人都会进来）」（R1-28）。
  - 输 `12345` → 1:1、`IsRoom=false`、备注保留（R1-29）。
  - 输 `1234` → 不建、状态提示「要 5 位字符：…」（R1-30）。
- 设置页「纸条」页对话框（R1-20）：控件文案实测为 `5 位 ID / 对话码`、`名字（≤8 字）`，提示「5 位数字 = 和这个 ID 的人单独对话；其它任意 5 位字符 = 对话码，别人输入同一个码就进同一个对话。」；`abcde` 合法、`1234` 非法。列表行显示 `对话码 <code>` / `ID <id>`（源码 `SettingsForm.cs` 的 `FillNoteContacts` 已按 `IsRoom` 分支，代码审查 + 对话框实测）。

**截图**：`test/visual/verify-after/shots/note-drawer-add-room.png`、`test/visual/verify-after/shots/note-drawer-room-chat.png`、`test/visual/verify-after/shots/note-room-sender-11111.png`、`test/visual/verify-after/shots/note-room-sender-22222.png`

---

## 需求 2：小纸条卡片边框稍微粗一点

**结论：✅ 通过。** 从 1 DIP 加粗到 1.8 DIP，肉眼与像素测量都确认变粗，且没有把整卡提亮来「假装」边框。

| 断言 | 改动前 | 改动后 |
| --- | --- | --- |
| `CardBorderDip` | 无此常量（1 DIP） | **1.80**（在 1.4–2.0 合理区间） |
| x=面板宽/2 顶部像素带（色差 >12 的连续行数） | **2 行** | **4 行** |
| 同上，底部 | **2 行** | **4 行** |
| 边框亮度 − 内部亮度 | 101−39 = 62 | 101−39 = 62（仍然清晰，不靠提亮） |
| 聊天页 / 添加对话页 / 历史页 / 双栏式 | — | 顶部、底部均 **4 行**（4 个页面都验证） |

**改动后**（`notes`：150% DPI 下 1 DIP=1.5px，1.8 DIP 笔迹 + 抗锯齿正好落 4 行像素带；用户观感约粗一倍但仍是细线）。

**截图**：`test/visual/verify-after/compare-border-4x.png`（左=改前 2 行 / 右=改后 4 行，4 倍放大）、`test/visual/verify-after/shots/note-drawer-chat-border.png`、`note-drawer-add-border.png`、`note-drawer-history-border.png`、`note-columns-border.png`

---

## 需求 3：多对话可滚轮滚动 + 右键删除

**结论：✅ 通过。** 8 个对话时抽屉式/双栏式都能滚动看全；右键对话出现红色「删除」按钮，点删除同时删联系人与本机这段 8 条消息，并通知 Widget 落盘。

### 3.1 滚动（R3-01…R3-05、R3-16、R3-17/17b、R3-18/18b）

以 8 个对话构造面板（150% DPI，drawer 392×282 DIP / columns 470×298 DIP）：

| 断言 | 抽屉式 | 双栏式 |
| --- | --- | --- |
| 有 `ContactScroll` / `VisibleContactCount` | 有，可见 4 个 | 有，**可见 5 个**（正常态；历史态 10 行） |
| 初始 window | scroll=0，只画 `10001…10004` | scroll=0，只画 `10001…10005` |
| `ScrollContacts(+1)` | scroll 0→1，首个从 10001→10002 | 同左 |
| 滚轮一格（`OnMouseWheel -120/+120`） | scroll +1 / −1 | 同左 |
| `ScrollContacts(±999)` clamp | 最大 4（8−4），末屏看得到 `10008`；反向回 0 | 最大 3（8−5），末屏看得到 `10008`；反向回 0 |
| 选中窗口外对话（`EnsureContactVisible`） | 自动 scroll=4、window=`10005…10008`；再选 10001 回 0 | 自动 scroll=3、window=`10004…10008`；再选 10001 回 0 |

- 双栏一屏行数（2026-11-13 16:23 `ColumnsVisibleCount()` 调整后）：**正常态 5 行**（`VisibleContactCount=5`，画出 `10001…10005`，截图 `note-columns-contacts-5rows.png`）、**历史态 10 行**（512 DIP 高，8 个对话全画、`scroll=0` 不用滚，截图 `note-columns-history-10rows.png`）。
- 底部「＋ 新建对话」：命中区与绘制位置**逐像素一致**（实测都是 `{X=15,Y=390,195×45}`），左键点它真的进入新建对话页（`state=Add`）——这条验证了本轮「命中区和绘制位置对齐」的修正（R3-18/18b）。
- 滚动提示也画了：双栏左侧竖轨+滑块（`note-columns-scroll-max.png` 右侧可见）、抽屉式横排右侧三个点（`note-drawer-scroll-max.png`）。
- 滚轮有两条路径：面板 `OnMouseWheel` 兜底 + 全局 `IMessageFilter`（`HandleWheelMessage`）；探针直接验证了前者，后者做了代码审查（需要面板可见/有焦点时才吃消息，锁屏会话无法真滚，已如实说明）。

### 3.2 右键删除（R3-07…R3-15）

| 断言 | 实测 |
| --- | --- |
| 右键命中对话 → 出现删除按钮 | `ShowDelete` 后 `DeleteId=10002`；真实 `OnMouseDown(Right)` 点胶囊中心同样得到 `DeleteId=10002` |
| 删除命中区位置 | `{X=218,Y=88,64×36}`，落在该对话胶囊 `{X=159,Y=84,129×45}` 内/旁 |
| 删除按钮画出来 | ShowDelete 前后渲染差异 **2196 px**（红色「删除」胶囊） |
| 左键点删除按钮 | 通过 `OnMouseUp(Left)` 点命中区中心：联系人没了、本机线程没了、`onDeleteContact` 回调收到 `10002` |
| Widget 落盘接线 | `Widget.DeleteNoteContact("10001")` 后该对话联系人与线程都没了，别的对话（10002）保持 |
| `HideDelete()` | `DeleteId` 清空、`Delete` 命中区消失 |
| 左键点对话 | 仍然正常选中、不误删 |

**截图**：`test/visual/verify-after/shots/note-drawer-delete.png`（红色「删除」盖在该对话上）、`note-drawer-before-delete.png`、`note-drawer-scroll-max.png`、`note-columns-scroll-max.png`、`note-columns-contacts-5rows.png`（双栏 5 行）、`note-columns-history-10rows.png`（历史态 10 行）

---

## 需求 4：大卡片 5 个按钮大小和高度一致

**结论：✅ 通过。** 5 个图标的可见墨迹外框已统一到约 18…20px（@1.5，即 12…13.3 DIP），纵向中心只差 1px；命中区仍是原来的 24 DIP，没有靠缩小点击区域「假装统一」。

| 图标 | 改动前 可见包围盒 | 改动后 可见包围盒（真机截图实测） |
| --- | --- | --- |
| ✈ plane | 25×25 @(206,21) | **20×20** @(209,23) |
| ¥ salary | 13×17 @(254,23) | **18×20** @(252,23) |
| ⚙ gear | 20×18 @(293,24) | **20×20** @(293,23) |
| ↻ refresh | 19×18 @(336,24) | **19×18** @(335,25) |
| › chevron | 11×18 @(382,24) | **18×20** @(378,23) |
| 宽极差 / 高极差 | **14 / 8** | **2 / 2** |
| 纵向中心 y 极差 | 2.0 | **1.0** |
| 命中矩形 | 5×36×36 @1.5 | 5×36×36 @1.5（不变） |

- 数据来源：把**正在运行的新装机版**大卡片用 `tools\shot.ps1 -Expand` 抓成 `real-card-expanded.png`（420×282），按同一算法量包围盒；探针自己离屏渲染的 Widget 量出来同样是 20/18/20/19/18（与真机差 ≤1px，抗锯齿阈值）。
- 放大对比图（上=改动前、下=改动后）：`test/visual/verify-after/compare-glyphs-3x.png`。

**截图**：`test/visual/verify-after/compare-glyphs-3x.png`、`test/visual/verify-after/real-card-expanded.png`、`test/visual/verify-after/shots/card-expanded.png`、`card-glyphs-row.png`

---

## 如何复跑（不依赖真机、锁屏也能跑）

```powershell
cd D:\DSH\RWZ0\work-countdown
.\test\verify\run-verify.ps1
#   -> 编译 test\verify\NoteVerifyProbe.exe（src\*.cs 去掉 Program.cs + 探针）
#   -> 跑 71 条断言；ALL PASS (71 checks) / exit 0
#   -> 出图到 test\visual\verify-after\（含 before/after 对比图与 compare-metrics.txt）
```

单独出对比图/数值：`.\test\verify\make-compare.ps1`（读 `verify-before` 与 `verify-after`）。

## 剩余风险 / 小观察（都不影响本次 4 条需求结论）

1. **（已核实，不是问题）对话码的「查在线」**：面板 `NotePanelForm.ProbeAdd()`（`src/NotePanelForm.cs:941-961`）对「5 位非数字码」会**提前 return**，只显示「对话码 ✓ 对方输入同一个码就能进这个对话」，**不会**调用 `onProbe`；`NoteRelay.Probe()`（`NoteRelay.cs:291`）只在目标是 5 位数字个人 ID 时才用（API 语义 by design）。上一版报告里「对话码点查在线永远没在线」是我只看 `NoteRelay.Probe` API、没核 UI 路径的误判，**撤回**。
2. **删除对话后订阅只增不减**：`NoteRelay.SubscribeRooms` 设计为「订阅只增不减」（`NoteRelay.cs:242-245` 注释）；删掉的对话本次连接期间还会继续收该房间主题的包，但 `Widget.OnNoteMessage` 会用 `Contact==null` 丢掉（探针 R1-26 已验证不会重建对话）。下次重连就不再订阅。代价只是一点无用流量。
3. **（已按复核调整，不再算风险）双栏行数**：复核后 Lead 已把 `ColumnsVisibleCount()` 从 4 行调到 **5 行**（正常态 298 DIP；历史态 10 行；`src/NotePanelForm.cs:452-456`），底部「＋ 新建对话」命中区同步与绘制位置对齐。探针 R3-17/17b/18/18b 实测：正常态 `VisibleContactCount=5`、历史态 `=10`、命中区与绘制矩形逐像素一致（`{X=15,Y=390,195×45}`）。现状比改动前的 6 行少 1 行，是给「＋ 新建对话」留位 + 40 DIP 行距的取舍，可滚动看全，不再是问题。
4. **锁屏会话限制**：真鼠标滚轮/右键没法在锁屏下真滚，本次滚动/右键用离屏渲染 + 反射调用 `OnMouseWheel`/`OnMouseDown(Right)`/`OnMouseUp(Left)` 验证；全局滚轮过滤器只做了代码审查。解锁后建议顺手用真鼠标滚一下、右键点一次删除（真机数据 93183/10825 请先备份再试）。
5. **真机数据未动**：验收只读/离屏/假数据；没有碰用户真机的小纸条联系人（93183 / 10825）。Lead 说真机只右键看过没点删除，我这边删除路径是在离屏 + 假数据上验证的。
6. 图标是 card-glyph 重绘版本：形状与旧版略有差异（plane 略小、chevron 略大、gear/refresh 线条更粗一点），但仍是线稿 + ¥，风格一致（见 `compare-glyphs-3x.png`）。

## 附：断言 ↔ 需求对照

| 需求 | 断言编号 | 条数 |
| --- | --- | --- |
| 1 添加对话/对话码 | R1-01…R1-31（含 1 条往返解析、1 条真实中继补订阅） | 31 |
| 2 边框加粗 | R2-01…R2-07 | 7 |
| 3 滚动 + 右键删除 | R3-01…R3-18b（含两种样式、双栏 5 行/历史 10 行、新建对话命中区、选中自动滚动、真实右键/点击、Widget 落盘） | 27 |
| 4 图标一致 | R4-01…R4-06 | 6 |
| **合计** | | **71** |

## 附：日志抓取方式

run-verify.ps1 里用 Write-Host 给 FAIL 上色（美化终端），所以普通 `>` / `|` 抓不到；抓完整日志要合并信息流：
```powershell
& .\test\verify\run-verify.ps1 *>&1 | Set-Content test\verify\run-final.log -Encoding UTF8
```

本次 `test/verify/run-final.log`：11,682 B，71 行 PASS、0 行 FAIL、`ALL PASS  (71 checks)`。
