# 小纸条通知：实现约束与规格（v3.8.1 基线）

> 用途：5 种设计稿选定后按本文件直接实现，避免重复踩坑。行号 = 当前工作树 `src/*.cs` 实际行号（只读审计，未改任何源码）。
> 前提结论：卡片内左上角的一切效果都走 `Widget.DrawNoteCorner` 这套机制（分层窗裁剪 + flowT 重绘 + `NoteRimActive` 门控）；卡片外光晕只有 `CardGlowForm` 一条路（多一层窗口，成本中–高）。

## 1. 现状实现

### 1.1 几何（DIP，实际像素一律 `S()`）
| 项 | 数值 / 来源 | 行号 |
| --- | --- | --- |
| 卡片尺寸 | 收起 224×68；展开 280×(164 / 有午休行 188)，工资面板 +116 | `Widget.cs:18-20`、`373-378`、`203` |
| 卡片圆角 | `CardRadiusDip` = 收起 `Skin.radiusCollapsed` / 展开 `radiusExpanded`；`CardRadiusPx = S(...)`，背景填充与裁剪共用 `CardPath()` | `391-400`、`2044`、`2115-2118` |
| 角亮带圆角 r | 默认皮肤 `S(13)` / `S(17)`；其它皮肤 `S(max(2, CardRadiusDip-1))` → quiet 9 / mica 17 / paper 4 / instrument 5 / neon 13 | `4107` |
| 起点 | `inset = S(1)` | `4106` |
| 延伸 ext | `min(S(20), Width*0.14f)`，实测恒为 20 DIP（收起 224×0.14=31.4 > 20） | `4108` |
| 路径 | 弧 `(inset,inset,r*2,r*2)` 180°→90°；左侧线 `(inset, inset+r)` 向下 +ext；顶部线 `(inset+r, inset)` 向右 +ext；三段不闭合 | `4111-4115` |
| 描边趟数 | 光晕 i=3..1：alpha `a*26/i`、线宽 `max(2, S(2)*i)` = 6/4/2 DIP；芯 alpha `a*235` accent、`max(1.6, S(2.2))`；白高光 alpha `a*140`、`max(1, S(1))` | `4116-4122` |
| 柔光 | `DrawGlowEllipse` 矩形 `(-12,-12,64,64)` DIP、中心 `(2,2)`、accent、alpha `0.40*a`；被卡片路径裁掉，只露出角内那一片 | `4125`、`2285-2301` |
| 整圈 rim | 矩形内缩 1 DIP、圆角 默认13/17 或 `CardRadiusDip-1`；3 趟 `a*24/i` 线宽 `max(2,S(2)*i)` + 芯 `a*225` 宽 `max(1.5, scale*2)` + 白 `a*80` 宽 `max(1, scale)`；**无 SetClip**，只内缩 1 DIP，角上粗笔画会溢到圆角外几像素 | `4135-4146` |

### 1.2 时间
- 收到即闪一圈：`noteFlashAt=now` → `NoteRimAlpha()` 1300ms 关键帧，`RimT{0,.08,.24,.40,.60,.75,.90,1}` / `RimA{0,.95,.12,.90,.10,.50,0,0}`（峰值 0.95@104ms、0.90@520ms、0.50@975ms）— `4023-4054`。
- 未读周期闪：`NoteBlinkIntervalMs=5000` / `NoteBlinkSpanMs=420` — `4057-4058`；包络 `CornerFlashAlpha` 两下：0–150ms、250–400ms，各 `sin(u*pi)` 快进快出 — `4060-4072`。
- 触发：1 秒 `tick`（末 60 分钟倒数厘秒时改 50ms、reducedMotion 时 200ms）里调 `MaybeBlinkUnread()`，`now-noteBlinkLast>=5000` 才重设 `noteBlinkLast/noteBlinkAt` 并 `Invalidate()` — `259-276`、`275`、`1093-1099`。
- 静默：角闪在 `reducedMotion || HighContrast` 时直接 0 且不调度 — `4077`、`4085`；**整圈 rim 不判这两个开关**（`4044-4054`），只靠 flowT 的静默分支画（`309-313`）。

### 1.3 绘制顺序
- `OnPaint`(`2014-2036`) → 全窗口 `frame` 位图（`Format32bppArgb`，`Clear(Transparent)`）→ `RenderCard`(`2039-2107`)。
- 顺序：底色/玻璃(`2046-2050`) → 质感(`2052-2054`) → `PaintCardGlow`(`2055`) → 内容 `PaintCollapsed/PaintExpanded`(`2058`) → 描边 `DrawCardBorder`(`2102`) → 螺丝(`2103`) → `DrawNoteRim`(`2104`) → `DrawNoteCorner`(`2105`)；后两者都在内容**之上**、都只在 `if (!reminding)` 里画。
- `DrawNoteCorner` 用 `g.SetClip(cardPath, Intersect)` 裁到卡片圆角内（`4104`），`DrawNoteRim` 不裁；提醒动画期间两者都不画，提醒层走另一张离屏位图（`2069-2088`）。

### 1.4 重绘驱动
- `flowT` 55ms（≈18fps）— `302-317`；门控 `NoteRimActive() = NoteRimAlpha()>0.02 || NoteCornerAlpha()>0.02` — `4129`。
- 静默分支：`salaryTick || NoteRimActive()` 才 `Invalidate()`（`309-313`）；正常分支：`!BarAnimated && !salaryTick && !NoteRimActive()` 直接 return（`315`）。
- `BarAnimated` 仅 default/mica/neon 为真（`3169-3176`）→ quiet / paper / instrument 三套皮肤平时不重绘，角闪那 420ms 是唯一空转窗口。
- 每帧成本 = 整卡重绘（玻璃皮肤还要 `Frost.PaintSurface` 全套：渐变 + 2 团雾 + 噪点 tile + 斜高光，`Frost.cs:60-103`）+ `PushFrame`（GetHbitmap + `UpdateLayeredWindow`，`1974-2007`）。**效果本身几乎不占成本，成本在「让 flowT 跑多久」。**

## 2. 硬约束（违反任一条都会被用户看见或耗电）

1. **不能画出卡片窗口外**：窗口尺寸就是卡片（`335`），帧位图 `Width×Height`（`2019-2023`），只推这个矩形（`1997`），`ApplyRegion()` 是空实现（`1634-1637`，靠像素 alpha 收圆角）。超出部分直接被裁；在圆角外画还会露出「方角」。要画到卡片外，只能走 `CardGlowForm`（见 §4）。
2. **reducedMotion / HighContrast 不闪**：新动效必须同时判这两个条件（照 `4077`/`4085` 的写法），并给一份静态替代——常驻未读标记在这两种模式下**仍要看得见**。
3. **圆角跟皮肤**：`Skins.cs:42-43` 默认 14/18，`quiet` 10/10（`228-229`）、`mica` 18/18（`273-274`）、`paper` 5/5（`317-318`）、`instrument` 6/6（`363-364`）、`neon` 14/14（`409-410`）。角上元素的尺寸/位置不能写死，按 `CardRadiusDip` 推导（如 `min(22, CardRadiusDip+8)`）；22×22 折角在 paper/instrument 上会显得过大。
4. **DPI 走 `S()`**：`S(v)=(int)Round(v*scale)`（`341-344`），`scale=DpiX/96` 钳在 0.75–3（`234-237`），清单是 system DPI aware（`build.ps1:89-90`）。线宽写 `Math.Max(1f, S(x))` 这类下限，别用裸 float 常量。
5. **1 秒调度，不在绘制里按 `TickCount % 5000` 算**：v3.8.1 之前就这么干，每秒只重绘一帧，肉眼等于没有（`CHANGELOG.md:81`）。正确做法：tick 里记时间戳 → flowT 按时间戳插值持续重画。
6. **新增/修改的闪光必须挂进 `NoteRimActive()`**（`4129`），否则 flowT 不重绘，只有 1 帧。
7. 动画里不要重建大位图/tile；要缓存就照 `CardGlowForm` 的 key+bucket 做法（`283-287`）。
8. 常驻动画 = 常驻 18fps 重绘（quiet/paper/instrument 现在完全空转，加常驻动效会新开一路耗电）；常驻标记优先做**静态**的。

## 3. 未读状态来源与「读完即停」

- `Widget.NoteUnread` = `settings.note != null && settings.note.TotalUnread() > 0`（`3800-3803`）；`TotalUnread()` 遍历 `threads[i].unread`（`Models.cs:1056-1061`）；未读在 `Append(m, incoming:true)` 里 `t.unread++`（`1052`），随配置持久化（`NoteThread.unread`，`872`），Normalize 钳负（`990`），`MarkRead` 清 0（`1063-1067`）。
- 卡片上现有标记：**只有展开态**——飞机图标 `rectNote = (Width-146, iconY, 24, 24)` DIP（`2721`）+ 未读时右上角 **5×5 DIP 圆点** `(rectNote.Right-5, rectNote.Top+1)`（`2728`）。收起态 `PaintCollapsed`（`3463-3480`）**没有任何未读标记**——这是「没有常驻标记」的根因。
- 面板内：头部「N 条未读」文字（`NotePanelForm.cs:1455-1461`）；联系人胶囊未读徽标 `DrawBadge`（`1616-1623`，15×14 DIP、圆角 7、「9+」），竖排落点 `1594-1595`、横排 `1606-1609`。
- 读完即停：面板选中/进入会话 → `t.unread=0` + `onRead` 回调（`NotePanelForm.cs:545-552`、`986-991`）→ `Widget.ReadNoteThread` 调 `MarkRead` + `Invalidate`（`3874-3880`）；面板开着且正看该会话时收到消息，直接 `MarkRead`（`4007-4013`）。打开面板只清 `noteFlashAt`（`3842`，停整圈闪），角闪靠 unread 归 0 自然停。

## 4. 现成的卡片外发光：`CardGlowForm`

- 是什么：垫在卡片**下面**的独立分层窗，四边各留 `PadDip=56`（`CardGlowForm.cs:18`）；两层：呼吸柔光（1/2 尺寸 3 次盒模糊烘图，outset 12 / radius 24 / sigma 18 / alpha 0.34–0.62，周期 4200ms）＋沿圆角边框 5.5s 转一圈的 conic 扫边（宽 3 DIP，5500ms）；帧率 33ms（`20-40`、`103-123`）。
- 位置/尺寸：`Follow()` 每帧贴 `owner.Bounds` 外扩 pad（`209-220`），窗口 = 卡片 + 112 DIP：1:1 收起 336×180、展开 392×276（无午休），150% 时 504×270 / 588×414；压在卡片下（`OrderBelow` `223-227`）；整窗 `WS_EX_LAYERED|TRANSPARENT|NOACTIVATE|TOOLWINDOW`，点击全穿透（`125-133`）。
- 刷新：33ms `Step`→`Invalidate`→`Render`；底图只在尺寸/主题/accent/呼吸档位（0..63）变时重烘（`283-287`、`300-314`），每帧 = 1 次整窗贴底图 + 1 次裁到圆环里旋转贴 conic + `Push`。测试钩子 `Frozen/FixedClock/HaloAlpha`（`78-82`）。
- 开关：只在 `!reducedMotion && !HighContrast` 且首次 `OnShown` 时创建（`Widget.cs:463-467`）；运行中切换「减少动态效果」**不会**销毁它。`settings.ui.cardGlow` 取值 `classic|corner|bar|all`（`Models.cs:749`/`781`/`1492-1494`，`SettingsForm.cs:2143-2147`/`2323-2324`/`2448-2449`）**只管卡片内** `PaintCardGlow`（`Widget.cs:2253-2283`），不管 `CardGlowForm`。即卡外柔光/扫边目前一直开着、没有用户开关。
- 能否在卡外左上角画通知光晕/角标：**能，且是唯一位置**。左上多出 56 DIP 可用区；做法是给 `CardGlowForm` 加一个 `SetNoteNotify(alpha,count)` 之类入口（照 `SetGlow` 的缓存失效写法 `158-169`），在 `DrawHalo`/`DrawSweep` 之后画（`291-297`）。**成本判断：中等**——已有 30fps 全窗合成，多画一个约 80×80 DIP 的 `PathGradientBrush` 圆斑每帧 <0.3ms 可忽略；真实代价是：① 要新增 Widget→Glow 状态通道并在未读变化时算 alpha；② 它画在卡片下面，卡片矩形内的像素被卡片盖住，只能做「卡外那一圈」，做不了盖在卡片上的角标；③ 点击穿透，不能承载可点元素；④ 常驻光晕 = 常驻 30fps（本来在跑，增量小），但 reducedMotion 用户完全看不到，需要静态替代。备选「另开一层置顶透明窗画角标」成本高（跟位 + 置顶 + 多一次 ULW 上传 + hover 命中冲突），不推荐。

## 5. 与角闪相关的测试与探针（改动后必须同步）

| 位置 | 断言 / 作用 | 改动后要改什么 |
| --- | --- | --- |
| `test/Test.cs:1087-1098` | 9 条：常量 5000/420；`CornerFlashAlpha` 在 60/75/120/200/300/420/421/-5 的取值（亮/暗隙/熄灭/越界） | 改常量或包络算法必改；`NoteBlink*` 要保持 `internal` 可见 |
| `test/Check.cs:3674-3678` | 1 条合并断言：间隔 5000 + 420ms + 60 亮 / 200 灭 / 300 亮 / 600 灭 | UI 侧唯一角闪断言，同上 |
| `test/Check.cs:3517-3527` | 飞机图标几何（在 ¥ 左边、不出窗口）、`TotalUnread()>0`、`noteFlashAt=now-40` 时 `NoteRimAlpha()>0.3` | 挪飞机图标/改 rim 关键帧必改 |
| `test/NoteShot.cs:113-131` | 离屏渲染整圈 rim，出 `test/visual/note-widget-flash.png`（设 `noteFlashAt`） | 改 rim 画法/包络同步改 |
| `test/CornerFlashShot.cs` | 离屏 6 帧（40/120/200/300/380/600ms）× 收起/展开，反射设 `noteBlinkLast/noteBlinkAt`，出 `test/visual/skins/cornerflash-*.png` + `sheet-cornerflash.png` | 改包络/时长要改 `offsets`（`55`）；反射字段名不能改 |
| `test/FlashLiveProbe.cs` | 真 `WidgetForm` + 消息循环跑 8s，每 4 拍打印 `noteBlinkAt` 与 `CornerFlashAlpha`，存 `live-NNNN.png` | 同上；用来证明「1 秒 tick 真的调度到了」 |
| `tools/flash-probe.ps1` | 真机（已装 `WorkCountdown.exe` 运行中）`PrintWindow` 连拍卡片窗，统计左上 110×110 DIP 内「蓝色像素」数（B>150 且 B>R+35 且 B>G+30），存 `test/visual/real-flash/frames-*.png` | 新效果若不在左上/不是蓝色/超出 110 DIP，判定口径（`57-68`）要跟着改；现在绑定了 accent=蓝 |
| `test/build-tests.ps1` / `build-probes.ps1` | 前者编译跑 Test+Check（必须带 DPI 清单）；后者自动发现所有含 `static Main` 的 .cs 逐个编译 | 新增探针不用登记；但探针大量用反射，改私有字段名**不会编译报错**、只在运行时炸 |

## 6. 五种候选效果的 GDI+ 可行性

> 卡片内可画面积：收起 224×68 / 展开 280×188 DIP。每帧已是整卡重绘 + 一次 ULW 上传，角上再多几次描边/填充不改变量级；「会不会卡」取决于它让 flowT 跑多久（18fps 上限）。

| 候选 | 可行性 | 关键 API | 每帧重画量 | 注意事项 |
| --- | --- | --- | --- | --- |
| (a) 柔光呼吸：角亮带 + 径向柔光上下呼吸 | **低**（最稳） | 复用 `DrawGlowEllipse`（`PathGradientBrush`，`2285-2301`）+ 现有 4 趟 `DrawPath`；alpha 用 `sin` 曲线 | +1 次 64×64 DIP 径向填充 + 4 次 DrawPath ≈ 0.2ms | 直接扩 `DrawNoteCorner` 最省；呼吸周期 1200–1600ms 要让 420ms 窗口变长（或「收到后呼吸 2–3 轮再转静态」）；常驻呼吸会永久占 18fps |
| (b) 斜扫光：45° 亮光沿顶部掠过 | **低–中** | `LinearGradientBrush(45f)` + 3–5 段 `ColorBlend` + `SetClip(cardPath)`；或先烘一条 56–72 DIP 宽斜带位图，逐帧 `DrawImage` 平移 | 1 次裁剪填充或 1 次 DrawImage | 600–800ms 从 `x=-72` 扫到 `Width`；45° 矩形要按对角线留余量；比多次描边平滑，推荐烘位图 |
| (c) 涟漪：2–3 圈圆角矩形放大淡出 | **中** | `GraphicsPath` + 现成 `RoundPath`（`1639-1652`）+ 2–3 趟递减 alpha `DrawPath`（GDI+ 无模糊，只能多趟假柔边） | 3 圈 × 2–3 趟 = 6–9 次 DrawPath（24→96 DIP 小矩形） | 错峰 180ms、每圈 500–600ms、线宽 1.2–2 DIP、alpha 0.5→0；总时长 ≥1.2s，必须延长重绘窗口；裁到 cardPath 后不会出角 |
| (d) 霓虹灯管：顶部/左侧短灯管，白芯 + 双层色光，不规则闪 | **中** | 多趟 `DrawPath/DrawLine`（白芯 1.5 DIP + accent 3 DIP + accent 7–9 DIP@0.1）+ 预烘闪烁关键帧表（仿 `RimT/RimA`，`4023-4041`） | 2 条灯管 × 4–5 趟 ≈ 8–10 次描边 | 不能每帧 `new Random()`：用查表或 `(elapsed/50)` 的确定性伪随机；不规则闪一轮至少 8–10 帧，55ms 下略顿；霓虹皮肤本身已有 `CardGlowForm` 外辉光，注意叠加过亮 |
| (e) 折角信：左上 22×22 折角三角 + 未读数 | **低**（但要改布局） | `GraphicsPath` 多边形 + `FillPath` + 1 DIP 描边 + `DrawString`（`fontTiny` 9pt 或数字字体）+ 偏移 1–2 DIP 的暗色多边形当投影 | 静态：只在 Invalidate 时画，0 额外帧 | 最省的常驻标记，reducedMotion 下也能留；但左上角已被占：展开态状态点在 `(18,19,6×6)`（`2717`）、收起态 `(14,15,5×5)`（`3467`），22×22 折角会压住它 → 收起 16–18 DIP、展开 22 DIP，或把状态点右移；计数 >9 显示 `9+` |

**推荐顺序（成本从低到高）**：(a) 柔光呼吸 → (b) 斜扫光 → (e) 折角信 → (c) 涟漪 → (d) 霓虹灯管。
若目标是「常驻标记 + 轻提示」而不是更炫地闪：**(e) 静态折角 + (a) 短呼吸** 性价比最高，且天然满足 reducedMotion；纯炫技选 (b)/(d)。

## 7. 整圈贴边闪与角闪两条链路的现有关系

- 收到消息：`OnNoteMessage`(`3972-4021`) `BeginInvoke` 回 UI 线程 → `note.Append(m, incoming:true)` 让 unread+1（`4000`）→ 同一处连写三个时间戳 `noteFlashAt / noteBlinkLast / noteBlinkAt = TickCount`（`4004-4006`）→ `SaveSettingsQuietly` → `Invalidate`(`4016`)。
- 于是两条链路**同时起跑**：① 整圈 rim 1300ms（`NoteRimAlpha`，`4044-4054`）；② 左上角 420ms 两下（`CornerFlashAlpha`，`4061-4065`）。同一帧里先画 rim（`2104`）再画 corner（`2105`），角闪叠在整圈闪上面。
- 未读还在时：tick 每 5s 只重设 `noteBlinkAt`（`MaybeBlinkUnread`，`4083-4093`），rim 不再参与；`unread` 归 0 后两条一起停（`NoteUnread` 是唯一开关，`3800-3803`）。
- 改动建议：新效果若要「收到时更明显、之后收敛」，扩一个 `noteEffectAt` 复用这三个时间戳的语义，不要新增定时器；任何新字段都要进 `NoteRimActive()` 与 reducedMotion 判定。
