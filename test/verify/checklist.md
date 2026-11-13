# 独立验收清单 — 用户 4 条反馈（task-2）

被测对象：`src/` 当前实现（Lead 本轮改动）。探针：`test/verify/NoteVerifyProbe.cs`（独立 exe，不覆盖 `test/Check.exe` / `test/Test.exe`）。
运行：`.\test\verify\run-verify.ps1`（编译探针 → 跑断言 → 出改动后截图 → 打印 PASS/FAIL 汇总）。
证据目录：改动前 `test/visual/verify-before/`，改动后 `test/visual/verify-after/`。

约定：断言里的数值都按 150% DPI（scale=1.5）计；像素断言用离屏 `OnPaint` 渲染，不依赖真鼠标（锁屏也能跑）。

---

## 需求 1：添加联系人 → 添加对话（任意 5 位字符 = 对话码；5 位纯数字 = 1:1；保留个人 ID）

| ID | 断言（可机械验证） | 证据获取方式 | 预期（PASS 条件） |
| --- | --- | --- | --- |
| R1-01 | `NoteSettings.ValidCode("12345")` 为 true（5 位数字也是合法码，只是按个人 ID 处理） | 反射调用静态 `ValidCode` | true |
| R1-02 | 5 位非数字字符（字母/中文/字母+中文混合）是合法对话码：`ValidCode("abcde")`、`ValidCode("讨论组ab")` | 同上 | 都为 true |
| R1-03 | 非法长度/空白被拒：`"1234"`、`"123456"`、`"12 45"`、`"     "`、`""` | 同上 | 都为 false |
| R1-04 | 5 位字符不是个人 ID：`ValidId("abcde")=false`；`ValidId("12345")=true`；`ValidId("01234")=false` | 反射调用静态 `ValidId` | 与左述一致 |
| R1-05 | `IsRoom` 判定：`IsRoom("abcde")=true`、`IsRoom("12345")=false`（数字走 1:1） | 反射（静态或实例均可） | 与左述一致 |
| R1-06 | `AddConversation("abcde","")` 建成对话：返回 true、`Contact("abcde")!=null`、`Thread("abcde")` 存在 | 反射调用 `AddConversation`；`IsRoom` 辅助 | 返回 true 且联系人与线程都在 |
| R1-07 | `AddConversation("12345","林总")` 建的是 1:1：`Contact("12345").IsRoom=false`、`id` 原样保留 5 位数字 | 同上 | IsRoom=false 且 id=="12345" |
| R1-08 | 对话码没备注时显示「对话 abcde」；数字 ID 没备注时显示「ID 12345」 | `NoteContact.Display()` | 字符串相等 |
| R1-09 | 非法输入不建对话：`AddConversation("1234")`、`AddConversation("12 45")`、`AddConversation("123456")` 全 false，且没新增联系人 | 反射调用 | 全 false，联系人数量不变 |
| R1-10 | 同一个码重复添加：返回 false（已存在），联系人不重复（仍 1 条），备注被更新 | 反射调用两次 + 计数 | 1 条联系人 |
| R1-11 | 房间主题 = `xzt/v1/room/<hex(UTF8(code))>`：`NoteRelay.RoomTopic("abcde")=="xzt/v1/room/6162636465"`（"abcde" 的 UTF-8 十六进制） | 反射调用 `NoteRelay.RoomTopic`（internal 也可，探针与 src 同程序集） | 字符串完全相等 |
| R1-12 | 码与主题一一对应：不同码 → 不同主题；同码 → 同主题；中文码按 UTF-8 而不是 ASCII/GBK 编码 | 反射调用 + 探针内自算 hex | 3 组比较全等 |
| R1-13 | 房间消息进对应线程：`note.id="46040"`，`Append(new NoteMessage("11111","abcde","hi",now), true)` 后消息进 `threads["abcde"]`；再来一条不同发送者 `"22222"→"abcde"` 也进**同一个**线程 | 直接调用 `NoteSettings.Append` 读 `threads` | 线程数 1，消息 2 条 |
| R1-14 | 房间消息保留发送者 5 位 ID：上述两条消息的 `from` 分别是 `"11111"` / `"22222"`，`to` 是码 `"abcde"` | 读 `NoteThread.messages` | from 原样保留 |
| R1-15 | 自己从房间发出：`Append(new NoteMessage("46040","abcde","yo",now), false)` 的 `from=="46040"`、进 `"abcde"` 线程 | 直接调用 | 与左述一致 |
| R1-16 | 房间对话里**显示了发送者 ID**：同一房间对话分别用发送者 `11111`、`22222` 渲染两张图，气泡以外的「发送者 ID 标注」区域像素必须有差异（同文同时间下唯一变量是发送者 ID） | 离屏渲染两张 `note-room-chat.png` + 逐像素 diff（排除气泡外接矩形），并保存截图人工复核 | diff 像素数 ≥ 20 |
| R1-17 | 真实中继发布路径用房间主题（端到端）：本地起一个假 MQTT broker（127.0.0.1 随机端口），收 CONNECT 回 CONNACK，再让 `NoteRelay` 往房间码发一条，抓到 PUBLISH 包的 topic | 探针内 TcpListener 假 broker + 读 MQTT 包 | topic == `xzt/v1/room/<hex>`；payload 的 `from` 是本机 5 位 ID |
| R1-18 | 设置页「纸条」页的添加对话框改成对话码：`NoteContactDialog` 的 ID 输入框标签含「对话码」/「5 位」，添加按钮文案是「添加对话」一类；对话框校验接受 abcde、拒绝 1234 | 反射实例化 `SettingsForm.NoteContactDialog`（不 ShowDialog），读控件文案；对 `ValidCode` 断言 | 文案含「对话码」；abcde 合法、1234 非法 |

> 说明：R1-17 如果 Lead 的实现没有独立的房间发送入口（例如直接在 `SendMessage` 里按 `IsRoom` 分支），探针按实际方法名适配后仍做端到端抓包；完全无发送路径则 FAIL。

## 需求 2：小纸条卡片边框稍微粗一点

| ID | 断言 | 证据获取方式 | 预期 |
| --- | --- | --- | --- |
| R2-01 | 代码里有边框宽度常量/字段 `CardBorderDip`，值在 1.4–2.0 DIP 之间 | 反射取 `NotePanelForm.CardBorderDip`（字段或属性） | 1.4 ≤ 值 ≤ 2.0 |
| R2-02 | 顶部边框像素带比改动前粗：在 x=面板宽/2 从 y=0 往下扫，统计与内部底色的色差 > 25 的连续行数 | 离屏渲染（huang-liuli）读像素 | 改动前 = **2 行**（已实测）；改动后 ≥ **3 行** |
| R2-03 | 底部边框同步变粗：x=面板宽/2，从 y=H-1 往上扫同样的行数 | 同上 | 改动后 ≥ 3 行 |
| R2-04 | 边框依然清晰可见（不是靠把整卡调亮）：边框亮度 − 内部亮度 ≥ 25 | 同上 | ≥ 25 |
| R2-05 | 聊天页 / 添加页 / 历史页三个状态边框都画出来（不能被内容盖掉） | 分别渲染 `note-*-chat/add/history.png`，各自做 R2-02 | 三张都 ≥ 3 行 |

## 需求 3：多对话可滚轮滚动 + 右键删除

| ID | 断言 | 证据获取方式 | 预期 |
| --- | --- | --- | --- |
| R3-01 | 有滚动状态字段：`ContactScroll`（int）与 `VisibleContactCount`（int，可为属性/方法）存在 | 反射 | 存在 |
| R3-02 | 初始滚动位置 0，且 `drawnContacts` 只画窗口内的一屏（不是全部 8 个） | 8 个联系人构造面板，读 `ContactScroll` / `VisibleContactCount` / `drawnContacts` | scroll=0；drawnContacts.Count 精确等于可见行数：**抽屉 4 / 双栏 5**（双栏历史态 10） |
| R3-03 | `ScrollContacts(+1)` 后滚动位置 +1，`drawnContacts` 首个 id 变成第 2 个联系人（窗口真的动了） | 反射调用 `ScrollContacts(1)` 后读字段 | scroll=1 且首项 id 变化 |
| R3-04 | 滚轮滚 1 格：`OnMouseWheel(delta=-120)` 后 scroll +1；`delta=+120` 后 −1 | 反射调用 `OnMouseWheel`（protected） | ±1 |
| R3-05 | 边界 clamp：`ScrollContacts(999)` 后 scroll = 最大合法值（最后一屏能看到最后一个联系人）；再 `ScrollContacts(-999)` 后 scroll = 0 | 反射调用 + 读字段 | 不越界；末屏 `drawnContacts` 包含最后一个 id |
| R3-06 | 抽屉式与双栏式都能滚（两种 style 各跑一遍 R3-02..05） | 两个面板实例 | 都满足 |
| R3-07 | 右键某个对话 → 出现删除按钮：`ShowDelete(id)` 后 `DeleteId==id`，`hits` 里有 `Delete` 命中区，矩形落在该对话矩形内 | 反射调用 + 遍历 `hits` 的 `kind.ToString()=="Delete"` | 命中区存在且位置合理 |
| R3-08 | 删除按钮画出来了（像素）：ShowDelete 前后的截图在联系人胶囊右侧有差异，保存 `note-*-delete.png` | 离屏渲染 diff + 截图 | diff ≥ 10 像素 |
| R3-09 | 点删除真的删：`DeleteConversation(id)` 后 `note.Contact(id)==null`，且 `note.threads` 里没有 contact==id 的线程（本机 8 条对话一起删） | 反射调用 + 读集合 | 联系人和线程都没了 |
| R3-10 | 删除会通知 Widget 落盘：构造时传入的 `onDeleteContact` 回调被调用，参数 == 被删 id | 构造参数末尾新增 `Action<string>`；探针记录回调 | 收到 id |
| R3-11 | 右键位置隐藏删除按钮：`HideDelete()` 后 `DeleteId` 为空/null，删除命中区消失 | 反射调用 | 清空 |
| R3-12 | 右键菜单不影响原有左键选中（左键点联系人仍然选中会话，不误删） | 点 Left 命中后 `selectedId` 正确、联系人数不变 | 不误删 |

## 需求 4：大卡片右上角 5 个图标（✈ / ¥ / ⚙ / ↻ / ›）大小与高度统一

| ID | 断言 | 证据获取方式 | 预期 |
| --- | --- | --- | --- |
| R4-01 | 5 个图标命中矩形仍然都是 24×24 DIP（不靠缩小命中区冒充统一） | 离屏渲染 Widget，读 `rectNote/rectSalary/rectGear/rectRefresh/rectChevron` | 5 个都是 36×36 px @1.5 |
| R4-02 | 图标可见包围盒宽度一致：5 个包围盒 max(width) − min(width) ≤ 8 px（改动前 = 26−11 = 15 px） | 渲染后逐图标扫描非背景像素 | 极差 ≤ 8 |
| R4-03 | 图标可见包围盒高度一致：max(height) − min(height) ≤ 5 px（改动前 = 25−17 = 8 px） | 同上 | 极差 ≤ 5 |
| R4-04 | 5 个图标纵向中心对齐：包围盒中心 y 的极差 ≤ 3 px（改动前 31…33） | 同上 | 极差 ≤ 3 |
| R4-05 | 5 个图标横向中心与各自 24 DIP 矩形的中心偏差 ≤ 4 px | 同上 | 每个偏差 ≤ 4 |
| R4-06 | 图标整体视觉风格没被改坏（还是线稿/¥ 字），截图对比 `verify-before/card-expanded.png` 与 `verify-after/card-expanded.png` | 人眼 + 保存截图 | 无异常 |

> 改动前实测（`verify-before/card-expanded.png`，420×456，scale=1.5）：
> plane 26×25、¥ 13×17、gear 20×18、refresh 19×19、chevron 11×18；中心 y = 33 / 31 / 32.5 / 32 / 32.5。
> 这组数字就是 R4-02/03 的「改动前」基线。
> （统一用 `make-compare.ps1` 的欧氏色差 >90 算法复量：plane 25×25，宽 11…25 极差 14、高 17…25 极差 8；差 1px 只是抗锯齿边缘阈值。）

---

## 第 2 阶段补充断言（最终 src 实测后新增，均已并入探针）

| ID | 断言 | 证据获取方式 | 预期 / 最终实测 |
| --- | --- | --- | --- |
| R1-21 | `RoomTopic` ↔ `ParseRoomTopic` 往返：中英文码都能反解；`msg/*` 主题和坏十六进制被拒；`HexEncode/HexDecode` 往返 | 直接调用 internal static | 全 true |
| R1-22 | 对话码气泡：别人发的 = `"ID 11111：xxx"`，自己发的不带前缀；1:1 不带 | `NotePanelForm.BubbleText(m, room)` | 字符串全等 |
| R1-23 | 历史行「谁」：对话码显示发送者 5 位 ID、自己=「你」、1:1=「对方」 | `NotePanelForm.WhoLabel(m)` | 全等 |
| R1-24 | 抽屉式房间对话补「名字 + 对话码」头行 | 渲染截图 + SelectContact | `note-drawer-room-chat.png` |
| R1-25 | Widget 收到房间消息：不同发送者同一码进同一线程（BeginInvoke 真跑） | 反射调 `Widget.OnNoteMessage` + DoEvents | 2 条消息、from 11111/22222 |
| R1-26 | 已删/不存在的对话码收到消息不会重建对话 | 同上 | 联系人/线程数不变 |
| R1-27 | 1:1 陌生人消息自动建对话、保留对方 ID | 同上 | contact 99999、1 条消息 |
| R1-28 | 面板「新建对话」任意 5 位字符 → 对话码对话（回调 + 线程 + 状态文案） | 反射 `ConfirmAdd` | 状态含「已创建对话」 |
| R1-29 | 面板「新建对话」5 位数字 → 1:1 | 同上 | `IsRoom=false`、备注保留 |
| R1-30 | 面板「新建对话」4 位被拒并提示 | 同上 | 联系人不变、状态含「5 位」 |
| R1-31 | 已连接状态下新增对话码会立刻补订阅新房间主题（不用重连） | 假 broker 收 SUBSCRIBE | 抓到 `xzt/v1/room/<hex>` |
| R2-06 | 历史页边框同样加粗 | 渲染 History 页扫描 | 顶/底各 4 行 |
| R2-07 | 双栏式边框同样加粗 | 渲染 columns 聊天页 | 顶/底各 4 行 |
| R3-13 | 真实右键 `OnMouseDown(Right)` 命中对话 → `DeleteId` 指向它 | 反射调 protected 方法 | DeleteId=10002 |
| R3-14 | 左键点「删除」按钮命中区真的删掉（面板 + 回调 + 线程） | 反射调 `OnMouseUp(Left)` | 全删、回调=id |
| R3-15 | `Widget.DeleteNoteContact` 接线：联系人与本机线程一起删、别的对话不动 | 反射调私有方法 | gone=true、otherKept=true |
| R3-16 | 选中窗口外的对话自动滚到可见（`EnsureContactVisible`），再选第一个滚回顶部 | `SelectContact` | scroll=max / 0 |
| R3-16b | 同上第二条：滚回顶部 | 同上 | scroll=0 |
| R3-17 | 双栏正常态一屏 5 行（298 DIP 高、`ColumnsVisibleCount()` 调整后） | 8 个对话，读 `VisibleContactCount` + `drawnContacts` + 截图 | =5、画出 10001…10005 |
| R3-17b | 双栏历史态一屏 10 行（512 DIP 高），8 个对话全画、不用滚 | 切 History 后读字段 | =10、drawn=8、scroll=0 |
| R3-18 | 双栏底部「＋ 新建对话」命中区与绘制位置一致（本轮同时修正的 bug） | 反射 `ColumnsAddRect()` 与 `hits` 里 AddContact 矩形比对 | 两矩形逐像素相等、在面板内 |
| R3-18b | 左键点底部「＋ 新建对话」真的进入新建对话页 | 反射 `OnMouseUp(Left)` 点命中区中心 | `state=Add` |

> 最终探针结果：**ALL PASS (71 checks)**，日志 `test/verify/run-final.log`（71 行 PASS / 0 FAIL / exit 0）。
> 边框最终实测：`CardBorderDip = 1.80`，顶/底像素带 **2 行 → 4 行**（150% DPI）。
> 图标最终实测：宽 18…20（极差 2）、高 18…20（极差 2）、中心 y 极差 1px（改动前极差 14 / 8）。
> 双栏行数最终实测：正常态 5 行、历史态 10 行；底部新建对话命中区 `{X=15,Y=390,195×45}` == 绘制矩形。
> 同版本状态（Lead 侧）：Test 223 / Check 268 全绿、NoteClick 28 项 ALL PASS、CardGlyphProbe 17 PASS；装机 PID 35852 / 3.3.2.0。

---

## 汇总规则

- 每条 `PASS` / `FAIL` 一行，FAIL 必须带数值证据（实际值 vs 期望）。
- 最后 `ALL PASS (n checks)` 才代表 4 条需求全部满足；`FAILED x of n` 时按上面 ID 定位。
- 截图：`note-drawer-*.png` / `note-columns-*.png` / `note-room-chat*.png` / `card-expanded.png` / `note-scroll-*.png` / `note-*-delete.png`。
- 已知基线：改动前 `NotePanelForm` 每屏固定最多画 4 个（抽屉）/ 6 个（双栏）联系人、没有 `ContactScroll`/`DeleteId` 字段（`BuildHits` 里 `if (idx >= (columns ? 6 : 4)) break;`）；边框 `float bw = Math.Max(1f, scale);`（1 DIP）；5 个图标包围盒尺寸见上。
