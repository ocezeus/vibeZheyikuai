# 上班工具箱 · 桌面卡片视觉规格 + 状态机提取

> 用途：给「把这张卡做成 iOS 实时活动（锁屏卡 + 灵动岛）」提供**源码级事实**。只提取，不做设计、不提方案。
> 读取范围：`src/Widget.cs`(4790 行：1–348 / 590–1040 / 1428–1600 / 2107–2230 / 2385–2760 / 2764–3060 / 3066–3465 / 3476–3660 / 3867–4200)、`src/Models.cs`(2912 行：24–123 / 197–213 / 768–843 / 915–944 / 1140–1235 / 1398–1494 / 1820–2250 / 2283–2409 / 2411–2911)、`src/Skins.cs`(全 465 行)、`src/Program.cs`(全)、`src/Lark.cs`(110–235 / 519–549)，另 grep 了 `ScreenReminderForm.cs`/`CardGlowForm.cs`。
> 未修改 `src/` 下任何文件；本文是唯一产出。
> 标「未找到」的地方都是实际 grep 过没命中的，不是没查。

---

## 0. 一句话结论

桌面卡是一张**自绘 32bpp 分层窗口**（`UpdateLayeredWindow`，无子控件），真实形态只有 4 种：**收起 224×68 DIP**、**展开 280×(164|188) DIP**、**展开+工资面板 280×(280|304) DIP**、**提醒覆盖态**（尺寸不变，内容被色块盖住）。所有尺寸都是 96 DPI 设计坐标（DIP）× 主屏缩放系数，**没有百分比/约束布局**。

---

## 1. 尺寸与栅格

### 1.1 设计常量（全为 DIP）

| 常量 | 值 | 含义 | 出处 |
|---|---|---|---|
| `DesignExpandedW` | **280** | 展开宽 | `src/Widget.cs:18` |
| `DesignCollapsedW` | **224** | 收起宽 | `src/Widget.cs:19` |
| `DesignCollapsedH` | **68** | 收起高 | `src/Widget.cs:20` |
| `CardTopDip` / `CardBottomDip` | 12 / 12 | 首行顶部距 / 末行底部距 | `src/Widget.cs:23-24` |
| `HeadHDip` | **20** | 状态行行高 | `src/Widget.cs:25` |
| `LineDip` | **18** | 普通文字行行高 | `src/Widget.cs:26` |
| `BigDip` | **34** | 大数字行行高（`static`，可被测试探针改） | `src/Widget.cs:27` |
| `BarDip` | **4** | 进度条行盒高 | `src/Widget.cs:28` |
| `FootDip` | **16** | 脚注行高 | `src/Widget.cs:29` |
| `RowGapDip` | **6** | 统一行距（改它整卡等距重排） | `src/Widget.cs:31` |
| `SalaryPanelDip` | **116** | 工资面板增量高度 | `src/Widget.cs:203` |
| 左右内边距 pad | 展开 **18** / 收起 **14** | `S(18)`/`S(14)` | `src/Widget.cs:2766`、`3533` |
| 圆角 radiusCollapsed/Expanded | 默认 **14/18**；无垠 **10/10** | 由 SkinDef 决定 | `src/Widget.cs:382-385`、`src/Skins.cs:189-190`、`228-229` |
| 边框 borderWidth | 1 DIP（默认皮肤实际笔画 = `Math.Max(1, scale)` 像素，即 100% 时 1px、150% 时 1.5px，`accent` @ alpha 24/255） | | `src/Widget.cs:2192-2197` |

**卡片高度是算出来的，不是常量**（`CardHeightDip`，`src/Widget.cs:364-369`）：

```
rows = HeadH(20) + Line(18) + Big(34) + Bar(4) + Line(18) + Foot(16) + (有午休行 ? Line(18) : 0)
gaps = 有午休行 ? 6 : 5
高 = 12 + rows + 6×gaps + 12
```

→ **无午休行 164 DIP / 有午休行 188 DIP**；工资面板再 `+116`（`ExpandedH`，`src/Widget.cs:371-379`）。
> 任务里提到的「280×188」只在**显示午休行时**成立；README 未写死这两个数字（grep `224`/`280x188` 均未命中），以源码常量为准。

### 1.2 展开态纵向坐标（`PaintExpanded`，`src/Widget.cs:2764-2858`）

| 内容块 | y 起点 | 高 | 下一行起点 | 备注 |
|---|---|---|---|---|
| 状态行 | 12 | 20 | 38 | 状态点 6×6 圆心偏 `head/2-3` |
| 说明行 `bigLabel` | 38 | 18 | 62 | |
| 大数字 `big` | 62 | 34 | 102 | 行盒 x = `pad-2`，宽 `w+2` |
| 进度条 | **102** | 4 | 112 | x=pad，宽 `Width-2*pad` |
| 下一段 `sub` | 112 | 18 | 136 | |
| 午休行（可选） | 136 | 18 | 160 | 无午休行时 `sub` 后直接脚注 |
| 脚注 `extra` | 160 | 16 | 176 | +底部 12 = **188** |
| 工资面板 | 188 | 116 | 304 | 分隔线画在 `top` |

右上角 5 个图标（命中区 24×24，`iconY = y+(20-24)/2`）：`plane` 未读纸条 `Width-146`、`salary` ¥ `Width-118`、`gear` `Width-90`、`refresh` `Width-62`、`chevron` `Width-34`（`src/Widget.cs:2787-2792`）。图形几何外框统一 12 DIP，1.5 DIP 线宽（`DrawGlyph`，`src/Widget.cs:3550-3560`）。

### 1.3 收起态纵向坐标（`PaintCollapsed`，`src/Widget.cs:3531-3548`）

| 元素 | rect（x,y,w,h） |
|---|---|
| 状态点 5×5 | (14, 15, 5, 5) |
| 左上标签（`bigLabel`） | (24, 7, 90, 20)，左对齐 |
| 右侧槽（下班时间 / 午休） | (`Width-14-86`, 7, 86, 20)，右对齐 |
| 大数字 | (13, 27, `Width-28`, 27) |
| 进度条 | (14, **59**, `Width-28`, **3**) |
| 拉取失败 `!` | (`Width-28`, 32, 14, 20)，琥珀 `#F2B34C` |

### 1.4 工资面板坐标（相对 `top = CardHeightDip`：有午休行 188 / 无 164，`DrawSalaryPanel`，`src/Widget.cs:2864-2927`）

分隔线 y=top；`headY=top+8`（「今日收入」60×16 + 「实时」胶囊 48×13 r6 + 右「单击收起」60×16）；`moneyY=top+32`（`¥` 槽 18×24，金额 rect 高 36）；细分隔线 `top+68`；`statY=top+74`（本月收入 60×14 / 值 190×18 / 右侧「发薪日 MM/dd」150×14 与「N 天后」150×18）。

### 1.5 提醒覆盖态（`DrawReminderContent`，`src/Widget.cs:1497-1552`）

- 收起：标题 (14, 9, w-28, 25) 13pt Bold；meta (14, 40, w-28, 20) 9.5pt；常驻「该下班啦」上下分界 `seam = S(37)`（`src/Widget.cs:1471`）。
- 展开：类型行 (18, 12, w-28, 18) 9.5pt；标题 (18, 30, w-28, **32**) 17pt Bold；信息行从 y=66 起，行宽 -28（右上角关闭按钮），行高依次 22(时间)/18(地点)/16(备注)/20(剩余)，剩余空间均分作行距（clamp 2–16 DIP，放不下先砍「备注」行，整块垂直居中）；`seam = S(64)`。
- 全屏提醒（另一路）：独立全屏分层窗，按 **1280×720 设计坐标** × `screenLayout = min(rw/1280, rh/720)` 渲染（`src/Widget.cs:1148`、`337-346`）。

### 1.6 DPI 换算

`scale = 主屏.DpiX / 96f`，clamp `[0.75, 3]`（`src/Widget.cs:218-223`）；`AutoScaleMode.None`；一切坐标 `S(v) = (int)Math.Round(v * scale)`（`src/Widget.cs:332-335`，`Math.Round(double)` 默认是**银行家舍入**，.5 走偶数）。

| 设计值(DIP) | 100% | 150% | 200% |
|---|---|---|---|
| 收起卡 | 224×68 | 336×102 | 448×136 |
| 展开卡（含午休） | 280×188 | 420×282 | 560×376 |
| 展开卡（无午休） | 280×164 | 420×246 | 560×328 |
| 加工资面板 | +116 | +174 | +232 |
| 进度条 | 4（收起 3） | 6（4.5→4） | 8（6） |
| 圆角 | 18 | 27 | 36 |
| 行距 | 6 | 9 | 12 |

**iOS 映射**：iOS 的 point 就是 DIP，**直接把 DIP 值当 pt 用**，别再乘屏幕缩放。字体是 `pt`（`GraphicsUnit.Point`），pt→DIP 需 **×4/3**（96/72），见 §2.2。
**未找到**：逐显示器 DPI 处理（grep `DpiChanged`/`OnDpiChanged` 无命中）——只在启动时取一次主屏 DPI，多屏不同缩放会拉伸，是桌面端已有缺陷，不必在 iOS 复刻。

---

## 2. 排版

### 2.1 字体族解析（`src/Models.cs:2703-2910` `UiFont`）

- 中文候选顺序：`PingFang SC / PingFang HK / PingFang TC / 苹方 常规 / 苹方` → `Noto Sans SC` → `Microsoft YaHei UI`；粗体另有 `苹方 粗体` 族（有独立粗体族时用它并去掉 Bold 标志，避免二次加粗，`src/Models.cs:2830-2839`）。优先读 exe 旁 `fonts\` 私有字体；用户 `ui.textFont` 可覆盖。
- **数字等宽的实现方式**：所有数字字符串（倒计时、工资）都走 `UiFont.MakeNumber(size, style)` → `new Font(numberFamily, size, style, GraphicsUnit.Point)`，`numberFamily = ui.numberFont`（默认 `Consolas`），失败退 `GenericMonospace`，再退系统默认（`src/Models.cs:2783-2795`）。
- iOS 对应：中文用系统 `PingFang SC`（iOS 自带），数字用 `.monospacedDigit()` 或 SF Mono。**不要假设 Consolas 存在**。

### 2.2 字号表（`CreateFonts`，`src/Widget.cs:394-415`）

| 角色 | 字段 | 构造 | pt | ≈DIP(px@96dpi) |
|---|---|---|---|---|
| 大号倒计时（时钟格式） | `fontBig` | `MakeNumber(28, Bold)` | **28** | 37.3 |
| 大号非时钟（"今天休息"/"8h30m"） | `fontBigText` | `Make(17, Bold)` | 17 | 22.7 |
| 状态行 headline / 提醒类型、剩余 | `fontStatus` | `Make(TypeStatusPt=9.5)` | **9.5** | 12.7 |
| 说明行 / 下一段 / 午休行 | `fontSmall` / `fontSmallBold` | `Make(9.5)` / Bold | 9.5 | 12.7 |
| 脚注 | `fontTiny` | `Make(TypeTinyPt=9)` | **9** | 12.0 |
| 收起大数字（时钟） | `fontCollapsedNum` | `MakeNumber(17, Bold)` | 17 | 22.7 |
| 收起大数字（非时钟） | `fontCollapsedText` | `Make(13, Bold)` | **13** | 17.3 |
| 收起标签 / 右侧槽 | `fontCollapsedLabel` | `Make(9.5)` | 9.5 | 12.7 |
| 提醒标题（卡内） | `fontRemindTitle` | `Make(17, Bold)` | 17 | 22.7 |
| 提醒时间 / 地点 | `fontRemindTime` / `fontRemindMeta` | `Make(12, Bold)` / `Make(10)` | 12 / 10 | 16 / 13.3 |
| 工资大数字 | `fontSalary` | `MakeNumber(22, Bold)` | 22 | 29.3 |
| 工资 ¥ 图标 | `fontGlyphSalary` | `MakeNumber(13, Regular)`，只取轮廓缩放到 13.4 DIP 高 | 13 | — |
| 全屏提醒（1280×720 设计系） | `screenTitleFont` / `screenWhenFont` / `screenCountFont` / `screenPlaceFont` / `screenDetailFont` / `screenHintFont` / `screenKindFont` | 46/28/19/16/14/11.5/12 × `screenLayout` | — | `src/Widget.cs:1200-1210` |

字号旋钮：`TypeStatusPt=9.5`、`TypeSmallPt=9.5`、`TypeTinyPt=9`、`MutedMix=0.18`、`DimMix=0.30`（`src/Widget.cs:355-359`）。

### 2.3 颜色角色 / 对齐 / 截断

- 派生色：`muted = Mix(textMain, bgBottom, 0.18)`；`dim = Mix(textMain, bgBottom, 0.30)`（`src/Widget.cs:2767-2768`）。**卡片正文不用 `theme.textMuted/textDim`**，用的是这两个混合色——iOS 要按这个公式算，别直接抄 `textMuted`。
- 对齐：全部 `Near` + 垂直居中；只有「右上图标行」「收起态右侧槽」「工资·单击收起」「发薪日」用 `Far`（`Line(..., far)`，`src/Widget.cs:1487-1495`）。
- 截断：单行 `NoWrap + EllipsisCharacter`；换行/制表符替换成空格。大数字额外 `FitFont` 按宽度缩字号，最小 8pt（`src/Widget.cs:1567-1573`），所以**极长文案会变小而不是截断**。

---

## 3. 颜色令牌

### 3.1 「无垠 quiet」（`Skins.cs:218-259`，色值由 `MakeTheme` 拼装，`src/Skins.cs:446-463`）

| 角色 | SkinDef/ThemeDef 字段 | 值 | 备注 |
|---|---|---|---|
| 卡片底 | `colors.bgTop` / `colors.bgBottom` | **#12151A** / **#171B21** | 画的是 90° 线性渐变（`src/Widget.cs:2117`） |
| 1px 描边 | `borderColor`（=`theme.edge`） | **ARGB(19,255,255,255)** = `#13FFFFFF` | `borderWidth=1`，画法见 `src/Widget.cs:2206-2214` |
| 一级文字 | `colors.textMain` | **#E9ECF1** | |
| 二级文字（名义） | `colors.textMuted` | #929AA7 | 卡片实际用下面的 muted |
| 三级文字（名义） | `colors.textDim` | #666E7B | 卡片实际用下面的 dim |
| 实际二级 `muted` | 计算：Mix(textMain,bgBottom,.18) | **#C3C6CC** | `src/Widget.cs:2767` |
| 实际三级 `dim` | 计算：Mix(textMain,bgBottom,.30) | **#AAADB3** | `src/Widget.cs:2768` |
| 强调色 | `colors.accent` | **#96A9FF** | 状态点 / 大数字（默认）/ 渐变端 |
| 强调第二色 | `accent2` | **#CFD6E4** | 无垠进度条已走段用它 |
| 成功色 | `colors.success` | **#46D39A** | 已下班 / 已达标 |
| 告警色 | 硬编码（非皮肤） | **#F2B34C** | 「还没打卡」`src/Models.cs:2663` |
| 进度轨道 | `colors.track` | **#2E3237**（= Mix(#171B21,#FFF,.10)） | 无垠实际轨道 = 白 11%×0.9 alpha（`BarTrackInk`，`src/Widget.cs:3251-3259`） |
| 进度已走段 | 计算 | **#D7DDE8**（= Mix(accent2,textMain,.3)） | `src/Widget.cs:3282-3286` |
| 大数字色 | `numberColor` | **#F1F3F7**，无发光 | |
| 顶高光 | `sheenAlpha` | 0.045 | `PaintSkinSheen` |
| 进度条画法 | `progress=Hairline` | **2 DIP 细线，不发光、不流动** | `ProgressBarDip`，`src/Widget.cs:3224` |
| 圆角 | `radiusCollapsed=radiusExpanded` | **10 / 10** | |
| 卡片外辉光 | `glow=null` → 取 `theme.accent` | #96A9FF | `Skins.GlowColor`，`src/Skins.cs:139-142` |

### 3.2 默认皮肤 + graphite 主题（`src/Models.cs:1933`；`Make` 参数顺序见 `src/Models.cs:2075`）

`Make(id, name, accent, success, bgTop, bgBottom, track, textMain, textMuted, textDim, dark)`：

| 令牌 | 值 |
|---|---|
| `accent` | **#B9C2D0** |
| `success` | **#8FD3A8** |
| `bgTop` / `bgBottom` | **#141619** / **#1F2228** |
| `track` | **#2E323A** |
| `textMain` / `textMuted` / `textDim` | **#F0F2F6** / #929AA8 / #6F7684 |
| 派生 `muted` / `dim` | **#CACDD1** / **#B1B4B8**（计算值） |
| 卡片描边 | `ARGB(24, accent)`，1 物理像素（`src/Widget.cs:2196`） |
| 渐变进度条已走段 | `Mix(accent,bgBottom,.22)`=**#979FAB** → `accent` **#B9C2D0** + 白 90 高光 |
| 圆角 | 收起 14 / 展开 18 |

其它皮肤差异（`src/Skins.cs`）：`mica` 玻璃 r18 + Pill 5 DIP 渐变 + 数字发光；`paper` 浅色 `#FDFAF2` r5 + Brush 3 DIP + 宋体标题；`instrument` `#1E1F1A` r6 + Segmented 8 DIP + 琥珀数字 `#FFC24A`；`neon` `#0D0A1A` r14 + Neon 4 DIP + 品红→青描边。

### 3.3 最后 30 分钟「hot」

- 判定：`0 < (target - now).TotalMinutes <= 30`（`HotBar`，`src/Widget.cs:3038-3041`）。
- 进度条渐变四色（位置 0 / .42 / .74 / 1）：`rgb(255,212,122)`、`rgb(255,214,61)`、`rgb(255,138,61)`、`rgb(255,92,122)`（`src/Widget.cs:3158-3168`）。
- 外发光两圈 alpha 32 / 70（`src/Widget.cs:3114`）。

---

## 4. 内容块 × 显示条件

### 4.0 内容块 × 显示条件总表

| 内容块 | 展开态 | 收起态 | 显示条件（源码） | 数据来源 |
|---|---|---|---|---|
| 状态点 6/5 DIP | ✓ | ✓ | 无条件 | `status.accent` |
| headline（一句话状态） | ✓ 9.5pt | ✗ | 无条件 | `status.headline` |
| bigLabel（说明行） | ✓ 9.5pt | ✓ 左上 90×20 | 无条件 | `status.bigLabel` |
| 大数字 | ✓ 28pt 时钟 / 17pt Bold | ✓ 17pt 时钟 / 13pt Bold | 无条件 | `status.big` + `bigIsClock` |
| 进度条 | ✓ 4 DIP @102 | ✓ 3 DIP @59 | `barAlpha > 0.01`（展开动画期间淡入） | `status.progress` |
| sub（下一段） | ✓ | ✗ | 无条件（Rest/Done 也有值） | `status.sub` |
| 午休行 | ✓ | ✗ | `ui.showLunch && status.lunch != None`（`src/Widget.cs:349-352`） | `lunchLabel/lunchValue/lunchCounting` |
| 脚注 extra | ✓ | ✗ | 无条件 | `status.extra` |
| 收起态右槽 | ✗ | ✓ 86×20 右对齐 | 有 `targetText`，或午休计数中 | `targetText` / `lunchValue` |
| 右上 5 图标 | ✓ 24×24 | ✗ | 图标无条件；未读圆点 `NoteUnread` | `settings.note.TotalUnread()` |
| 工资面板 | ✓ +116 高 | ✗ | `salaryOpen && salary.monthly > 0` | `SalaryCalc.Compute` |
| 拉取失败 `!` | ✗ | ✓ | `lastFetchFailed` | 飞书拉取状态 |
| 大数字发光 | 视皮肤 | 视皮肤 | `skin.numberGlow`（mica/instrument/neon） | `accent2`/`textMain` |
| 未读左上角快闪 | ✓ | ✓ | `NoteUnread && !reducedMotion && !HighContrast` | 定时器 + `sin` 包络 |

### 4.1 六种状态全表（`StatusBuilder.Build`，`src/Models.cs:2576-2697`）

| state | headline | bigLabel | big | sub | extra | accent | bigIsClock | 午休行 | progress | targetText |
|---|---|---|---|---|---|---|---|---|---|---|
| `Rest` 休息日 | 休息日 | `dayLabel`（如「周六」/「周六 · 调休上班」） | **今天休息** | 下次上班 M月D日 HH:mm（周X） | 右键可临时设为上班日 | theme.accent | false | 隐藏 | 0 | — |
| `BeforeWork` 未上班 | 未上班 | 距离上班 | `Clock(start-now)` | 上班时间 09:00 · 周五 | 飞书里发「1」即完成上班打卡 / 未接入飞书，按班次时间计算 | theme.accent | true | 有 | 0 | — |
| `NotClockedIn` 还没打卡 | 还没打卡 | 上班已过 | `Clock(now-start)` | 同上 | 「在飞书给『考勤』发『1』完成打卡」或「飞书读取失败：…」 | **#F2B34C** | true | 有 | 0 | — |
| `Working` 工作中 | 工作中 | 距离下班 | `Clock(target-now)` | 上班 09:12 · 预计下班 19:30 | 已工作 3h20m · 应勤 8h00m | theme.accent | true | 有 | (now-start)/(target-start) | 19:30 |
| `Overtime` 已达标 | 已达标 | 可下班 · 已超 | `Clock(now-target)` | 同上 | 在岗 8h30m · 下班后发「2」记录 | **success** | true | 有 | 1 | 19:30 |
| `Done` 已下班 | 已下班 | 今日在岗 | `CompactDuration(worked)` | 09:12 → 18:40 | 工时已达标 · 应勤 8h / 工时 X / 应勤 Y | **success** | false | 隐藏 | 1 | — |

### 4.2 大倒计时格式（`Policy.Clock`，`src/Models.cs:2228-2239`）

| 条件 | 格式 | 例子 |
|---|---|---|
| 剩余 **< 60 分钟** | `{mm}:{ss}.{厘秒}`（毫秒/10） | `03:12.45` |
| 剩余 ≥ 60 分钟 | `{HH}:{mm}:{ss}`（总小时，>99 变 3 位） | `08:31:12` |

注：源码注释说厘秒「每 50ms 跳一次」，实际写法是每 10ms 变化（`ts.Milliseconds / 10`），靠 55ms 重绘看到跳动。**iOS 上 1/100 秒不可行**（Live Activity 计时文本到秒），这属于降级点，见 §7。
紧凑时长 `CompactDuration`：`h>0 → "3h20m"`；只有分 → `"45m"`；<1 分 → `"30s"`（`src/Models.cs:2218-2226`）。`Hm`：`"8 小时 30 分"` / `"8 小时"` / `"45 分钟"`。

### 4.3 预计下班 / 「休 1h05m」

- **收起态右槽**（`src/Widget.cs:3537-3543`）：
  1. 默认 `"下班 " + targetText`（如「下班 19:30」）；
  2. 若 `ShowLunchRow && lunchCounting && lunchValue` 非空 → `"休 " + lunchValue.Replace("还剩 ","")`（晚休用 `"晚 "`）。所以 **`休 1h05m` 是收起态右侧的短标签**，不是展开态。
- **展开态午休行**（`FillLunch`，`src/Models.cs:2443-2527`）：`距午休  1h05m` / `午休中  还剩 1h05m` / `距晚休  3h20m` / `晚休中  还剩 40m` / `马上下班` / 午休后无晚休时改 `距下班  2h10m`。文案颜色：计数中 `textMain`，否则 `muted`；行用 `fontSmallBold`。
- 显示开关：`ui.showLunch` && `status.lunch != None`（`ShowLunchRow`，`src/Widget.cs:349-352`），并且会改变卡片高度（§1.1）。

### 4.4 进度条（`DrawProgress`，`src/Widget.cs:3066-3215`）

- `progress = clamp01((now - start) / (target - start))`，`start = fixed ? 班次上班时间 : 打卡时间`，`target = TargetEnd`（`src/Models.cs:2668-2672`）；`Done` 与 `Overtime` 都是 1（不超 100%）。
- 默认画法：4 DIP 圆角胶囊，轨道 `theme.track`，已走段 `Mix(accent,bgBottom,.22)→accent` 渐变 + 白 90 高光 + 流动亮带（2600ms 一轮，带宽 `max(S(24), filled/3)`，峰值 alpha 150，两端 smoothstep 淡入淡出，`src/Widget.cs:3182-3213`、`3038-3055`）。
- 收起态条高 3 DIP @ y=59；展开 4 DIP @ y=102；皮肤可换 2/5/3/8/4 DIP 并在行盒内垂直居中（`ProgressBarDip`，`src/Widget.cs:3220-3231`）。

### 4.5 未读角标 / 右上图标

- 未读判定：`settings.note.TotalUnread() > 0`（`src/Widget.cs:3869-3872`）。
- 展开态：`plane` 图标改 `status.accent` 色 + 5×5 圆点画在 `rectNote.Right-5, rectNote.Top+1`，色 `theme.accent`（`src/Widget.cs:2793-2796`）。
- 收到消息：整圈贴边闪光 1300ms；未读期间左上角每 5s 快闪两下（0–150ms、250–400ms 两段 `sin` 包络，总 420ms）（`src/Widget.cs:4103-4152`）。
- **收起态没有未读角标**（`PaintCollapsed` 里没有这块）。

### 4.6 其它零碎

- 拉取失败：收起态右下琥珀 `!`（`src/Widget.cs:3547`）。
- 大数字发光：仅 `numberGlow=true` 的皮肤（mica/instrument/neon），8 方向 × 2 圈偏移（1.5 / 3.4 DIP）（`NumberHaloDip`，`src/Widget.cs:3469`、`3476-3521`）。
- 工资面板：`SalaryCalc.Compute` 每帧重算；「实时」胶囊（绿 `rgb(79,209,161)`，bg alpha 24，点 5 DIP）；今日收入整数位走主题色渐变 + 常驻柔光 + 6.5s 流动高光，小数位 `textMain`；`Money3` 保留 3 位小数且封顶到日薪（`src/Widget.cs:2864-3021`、`src/Models.cs:2283-2347`）。

### 4.7 文案模板（可直接照抄成 iOS 字符串）

| 位置 | 模板 | 出处 |
|---|---|---|
| headline | `休息日` / `未上班` / `还没打卡` / `工作中` / `已达标` / `已下班`（固定串） | `src/Models.cs:2588/2647/2656/2681/2690/2624` |
| bigLabel | `{dayLabel}` / `距离上班` / `上班已过` / `距离下班` / `可下班 · 已超` / `今日在岗` | 同上 |
| big | `Clock()` 见 §4.2 / `今天休息` / `CompactDuration(worked)` | 同上 |
| sub · Rest | `下次上班 {M月d日 HH:mm}（{周X}）` | `src/Models.cs:2596` |
| sub · 未打卡 | `上班时间 {HH:mm} · {dayLabel}` | `2650`、`2659` |
| sub · 工作中/超标 | `上班 {HH:mm} · 预计下班 {HH:mm}` | `2673` |
| sub · 已下班 | `{HH:mm} → {HH:mm}`，无上班卡时 `下班 {HH:mm}` | `2629-2631` |
| extra · 工作中 | `已工作 {CompactDuration} · 应勤 {CompactDuration}` | `2685` |
| extra · 已达标 | `在岗 {CompactDuration} · 下班后发「2」记录` | `2693` |
| extra · 已下班 | `工时已达标 · 应勤 {Hm}` 或 `工时 {X} / 应勤 {Y}` | `2633` |
| extra · 未打卡 | `飞书读取失败：{error}` / `在飞书给「{botName}」发「1」完成打卡` / `未接入飞书，按班次时间计算` | `2660-2662`、`2651` |
| 午休行 | `{label}` + 两个空格 + `{value}`（`距午休` / `午休中` / `距晚休` / `晚休中` / `马上下班` / `距下班`；值为 `CompactDuration`，进行中前缀 `还剩 `） | `2843-2844`、`2466-2509` |
| 收起右槽 | `下班 {HH:mm}` / `休 {dur}` / `晚 {dur}` | `src/Widget.cs:3537-3543` |
| 提醒首行 kind | `会议提醒` / `日程提醒` / `Outlook 日程` / `{固定日程名}提醒` / `注意` | `src/Widget.cs:1437-1444` |
| 提醒剩余 | `CountdownText(...)` / `还剩 N 分钟` / `还剩 {Hm}` / `正在休息` / `已结束` | `src/Widget.cs:1555-1563` |

---

## 5. 状态机

### 5.1 枚举（`src/Models.cs:2411-2413`）

```
enum CardState  { Rest, BeforeWork, NotClockedIn, Working, Overtime, Done }
enum LunchState { None, Before, During, After, EveningBefore, EveningDuring, OffSoon }
```
提醒另有 `source` 字符串：`lark | local | outlook | break | alert`（`src/Models.cs:32`、`61-63`）。**没有独立的"提醒态"枚举**，是 `WidgetForm.reminding` bool + `remindEvent`。

### 5.2 判定顺序（`StatusBuilder.Build`，`src/Models.cs:2576-2697`）

```
1. plan = Policy.Build(今天)；FillLunch() 先填午休行
2. !plan.isWorkday                → Rest（提前返回）
3. 未接飞书（!lark.enabled 或 chatId 空）→ inTime = plan.suggestStart, outTime = null
4. 跨天/未来时间清洗：clockIn/clockOut 不是今天 → 丢弃；> now → 丢弃
5. clockOut 有值                  → Done（提前返回）
6. clockIn 为空：
      now < suggestStart          → BeforeWork
      否则                         → NotClockedIn（accent 琥珀）
7. target = DayPlan.TargetEnd(clockIn)；start = fixed ? suggestStart : clockIn
   progress = clamp01((now-start)/(target-start))；写 sub/targetText/targetIso
8. ResolveAfterLunch()  // 午休结束后改「距下班」/「马上下班」
9. now < target                   → Working
10. 否则                          → Overtime（accent = success，offAlert = true）
```

`offAlert` 之后由 `CheckOffWork` 每秒判：`offwork` 固定日程启用且没被「忽略今天」→ 常驻「该下班啦」，每 **5 分钟**重播一次入场（`src/Widget.cs:768-813`，`OffAlertRepeatMs = 5*60*1000`）；飞书同步到下班或跨天自动收掉。

### 5.3 关键时间点算法

| 算法 | 公式 | 出处 |
|---|---|---|
| 参考上班 `suggestStart` | `date + startTime`（`"HH:mm"`） | `src/Models.cs:2172` |
| 固定班次下班 `fixedEnd` | `date + endTime` | `src/Models.cs:2173` |
| 是否工作日 | `workdays[(int)DayOfWeek]`（0=周日），`exceptions` 的 `off`/`work` 覆盖；周六日上班 label 加「· 周末加班」，调休加「· 调休上班」 | `src/Models.cs:2177-2188` |
| **弹性下班** `TargetEnd` | `fixed → fixedEnd`；`flex → 打卡 + flexMinutes`；若 `lunchDeduct` 再 `+ 与[lunchStart,lunchEnd]的重叠分钟`（= 扣掉午休） | `src/Models.cs:2137-2153` |
| 应勤分钟 | `fixed → round((target - start)/1min)`（`Done` 态用 `fixedEnd - suggestStart`）；`flex → flexMinutes`；工资侧 `DailyMinutes` 再扣午休/晚休 | `src/Models.cs:2632`、`2684`、`2372-2382` |
| 跨天 | 只认当天的打卡记录；下班 `> now` 视为脏数据 | `src/Models.cs:2611-2619` |
| 休息日 → 下次上班 | 从明天起逐日扫最多 14 天取第一个工作日 `suggestStart` | `src/Models.cs:2192-2202` |
| 午休窗口兜底 | 内置 `lunchStart/End` 无效时，从普通日程里找 **10:30–14:30** 开始且有结束时间的那条 | `src/Models.cs:2533-2555` |
| 午休/晚休/下班提醒 | 每日一次，秒级 tick 里比对「时:分」相等（`now.Hour*60+now.Minute == at.Hour*60+at.Minute`），开机时若已处于时段内则 `ResumeBreak` 直接立常驻卡 | `src/Widget.cs:863-931`、`891-908` |
| 飞书打卡读取 | 拉会话当天消息：用户消息 `Normalize` 后 `"1"/"１"/"打卡"/"打卡了"/"上班打卡"`/含「上班」≤12 字 → 上班；`"2"/"２"`/含「下班」≤12 字/`收工`/`走了` → 下班；取**最早上班 + 最晚下班**。机器人消息正则兜底 `上班时间：HH:mm`、`预计下班：HH:mm`、`收工 HH:mm→HH:mm` | `src/Lark.cs:172-232`、`519-549` |
| 提醒常驻时长 | `max(remindHoldSeconds(3..20, 默认 6s), 日程结束时间)`；`break` 结束提醒只放最短时长；`alert` 常驻 | `src/Widget.cs:977-994` |

---

## 6. 数据字段 → 显示映射（settings.json，C# 属性名）

`Settings`（`src/Models.cs:1140-1234`）/ `UiSettings`（`768-830`）/ `LarkSettings`（`197-213`）/ `SalarySettings`（`833-843`）/ `FixedSchedule`（`1398-1484`）/ `ReminderEvent`（`24-34`）。

| 字段 | 类型/默认 | 映射到卡面 | 刷新性质 |
|---|---|---|---|
| `mode` | fixed/flex | 状态机分支、target 算法 | 事件（设置变更） |
| `startTime` / `endTime` | "09:00"/"18:00" | `suggestStart`、固定班次 `fixedEnd`、sub/extra 文案 | 事件 |
| `flexMinutes` | 480 | 弹性应勤 → target | 事件 |
| `lunchDeduct` / `lunchStart` / `lunchEnd` | false / 11:50 / 13:30 | 下班时间是否加回午休、午休行 | 事件 |
| `eveningEnabled` / `eveningStart` / `eveningEnd` | true / 18:00 / 18:45 | 晚休行序列 | 事件 |
| `breakNotify` | true | 午休/晚休开始结束是否弹提醒 | 事件 |
| `workdays[]`、`exceptions[]` | 周一~五 / 空 | Rest 判定、`dayLabel`、下次上班 | 事件（跨天也算） |
| `schedules[]`（`id/name/role/start/end/repeat/until/enabled/notify/endNotify/deduct/styleId`） | 3 条（lunch/evening/offwork） | 午休行、晚休行、下班提醒、提醒配色 | 事件 |
| `lark.enabled` / `lark.chatId` / `lark.botName` / `lark.pollMinutes`（默认 5） | | 是否读打卡、指纹文案、同步频率 | 事件 |
| `salary.monthly` / `salary.payday` | 0 / 15 | 工资面板；`monthly<=0` 时面板只显示「还没设置月工资…」且不允许展开 | 事件 |
| `note.*`（`id/nickname/threads/contacts/relayHost`） | | 未读角标、纸条面板 | 事件（收到消息推） |
| `calendarEnabled` / `calendarLeadMinutes`(5) / `eventLeadMinutes`(10) / `calendarPollMinutes`(15) / `events[]` | | 提醒触发时刻 | 事件 + 时间 |
| `ui.theme` / `ui.skin` | midnight / default | 全部颜色 + 圆角 + 进度条画法 | 事件 |
| `ui.showLunch` | true | 午休行显隐 → **改变卡片高度** | 事件 |
| `ui.textFont` / `ui.numberFont` | "" / Consolas | 字体族 | 事件 |
| `ui.opacity`(94) / `ui.edge` / `ui.x/ui.y` / `ui.alwaysOnTop` / `ui.autoCollapseSeconds`(20) | | 窗口层，非卡面 | 不做 |
| `ui.reducedMotion` | false | 关掉所有逐帧动画（含流光/快闪/全屏扩散） | 事件 |
| `ui.reminderHoldSeconds`(6) / `reminderSpread`(card|screen) / `reminderSpreadStyle`(circle|bar|tape) / `styleFixed|styleLark|styleLocal|styleOutlook` | | 提醒覆盖时长与形态 | 事件 |
| `snapshot.clockIn` / `clockOut` | 运行时 | 状态机输入 | **事件驱动**（飞书同步） |

**每秒重算（iOS 端本地算，无需推送）**：`big` 倒计时、`progress`、`sub`/`extra` 里的时长与「已工作」、午休行剩余、`MinutesToOffWork`/`HotBar`、跨天、`Overtime` 迁移、`offAlert` 重播动画。
**事件驱动（必须有外部输入）**：打卡（开始/结束）、日程列表变化、纸条未读、主题/皮肤/字体/工资/午休开关、调休与班次设置。

---

## 7. 更新触发矩阵

| # | 事件 | 源码触发 | 频率 | 卡片变化 | iOS 建议承载 |
|---|---|---|---|---|---|
| 1 | 1s tick | `tick.Interval=1000` + `Recompute()`（`src/Widget.cs:250-267`） | 1 Hz | 大倒计时、进度、午休剩余、extra | **端上本地计算**（`Text(timerInterval:)`/`ProgressView(timerInterval:)`），不推送 |
| 2 | 逐帧重绘 | `flowT.Interval=55`（≈**18.2 fps**，`src/Widget.cs:293-308`） | 55 ms | 进度条流光、工资小数、未读闪光 | **不做** |
| 3 | 飞书打卡同步 | `poll.Interval = lark.pollMinutes`（默认 5 min，`src/Widget.cs:269-270`、`1617-1620`） | 5 min | BeforeWork→Working、Working→Done | **事件驱动**：状态迁移时更新 Live Activity（起/止各一次） |
| 4 | 飞书日程同步 | `calPoll`（`calendarPollMinutes`，默认 15 min，`src/Widget.cs:279-281`） | 15 min | 提醒池 | 排**本地通知**；列表不变则不推 |
| 5 | Outlook/ICS 同步 | `olPoll` 同上（`src/Widget.cs:284-286`） | 15 min | 提醒池 | 同上 |
| 6 | 提醒触发 | `CheckReminders()` 每秒查 `Reminders.IsDue`（`src/Widget.cs:738-762`） | 1 Hz 检查 | 卡片被提醒态覆盖 | 更新 Live Activity（提醒卡）+ 本地通知（时间敏感） |
| 7 | 提醒结束 / 手动关闭 | `EndReminder()`（`src/Widget.cs:1016-1030`） | 事件 | 恢复普通卡 | 更新 Live Activity |
| 8 | 午休 / 晚休 开始·结束 | `CheckBreaks()`→`TryBreak()`（`src/Widget.cs:863-931`） | 事件（每天各一次，`notified` 去重） | 午休行状态 + 提醒卡 | 更新 Live Activity（+ 可选本地通知） |
| 9 | 下班到点（Working→Overtime） | tick 里 `Recompute`（`src/Models.cs:2689-2696`） | 事件（时间驱动） | headline/文案/accent/`offAlert` | 端上本地计算；到点更新一次 |
| 10 | 「该下班啦」重播 | `CheckOffWork`，`OffAlertRepeatMs=5min`（`src/Widget.cs:768-813`） | 5 min 重播动画 | 卡片覆盖层 | 只推**一次**通知；不要每 5 分钟推 |
| 11 | 收到小纸条 | `NoteRelay.Message`→`OnNoteMessage`（`src/Widget.cs:4031`） | 事件（推送） | 未读角标 + 下沿闪光 | 更新 Live Activity（角标）+ 普通通知 |
| 12 | 未读清零 | 打开纸条面板已读（`TotalUnread()`→0） | 事件 | 角标消失 | 更新 Live Activity |
| 13 | 跨天 | tick 里 `lastDay != Today`（`src/Widget.cs:254-260`） | 每天 1 次 | 清 snapshot、重算、Rest/BeforeWork | 端上本地计算；午夜更新一次 |
| 14 | 主题/皮肤/字体/透明度/工资配置变更 | 设置窗口 → `ApplySettings()`（`src/Widget.cs:1597-1645`） | 事件 | 全量重绘 | 不同步到 Live Activity（端上读配置即可） |
| 15 | 工资面板展开/收起 | `ToggleSalary()`（`src/Widget.cs:1813`）、`SalaryPanelDip` | 用户交互 | 高度 +116、18fps 数字 | 不做（锁屏上没有这块） |
| 16 | 展开/收起 / 自动收起（20s 无操作） | `StartAnim`/`MaybeAutoCollapse`（`src/Widget.cs:1728-1830`、`1996`） | 用户/空闲 | 尺寸 224×68 ↔ 280×188 | 不做（锁屏用固定紧凑布局） |
| 17 | 置顶看门狗 | `topT.Interval=2000`（`src/Widget.cs:314-316`） | 2 s | 无（仅 z 序） | 不做 |
| 18 | 全屏提醒动画 / 警戒带 | `remindT` 33ms、`tapeT` 50ms、`ScreenReminderForm` 33/8ms | 30–125 fps | 全屏覆盖 | **不做**（见 §8） |

**最值得推的 4 个**：① 上班打卡成功（BeforeWork→Working，Live Activity 起始）② 日程提醒触发（Live Activity 提醒卡 + 时间敏感通知）③ 下班打卡（Working→Done，Live Activity 收尾）④ 午休/晚休开始（午休行切换 + 通知）。第 5 位可选「收到小纸条未读」。

---

## 8. 不可移植 / 需改写清单

| 特性 | 桌面实现 | iOS 上为什么不成立 | 改写方向 |
|---|---|---|---|
| **18fps 工资数字** | `flowT` 55ms 重绘 + `SalaryCalc.Compute` 每帧重算 + `Money3` 三位小数；整数位渐变 + 8 方向柔光 + 6.5s 流动高光（`src/Widget.cs:293-308`、`2864-3021`、`2994`） | Live Activity 不能 18fps 刷新（更新有节流与预算），且金额每秒 3 位小数滚动无信息价值 | 降到「今日收入」整数/1 位小数，端上按 `workedSec` 算；只有跨过整元时才更新一次 |
| **55ms 逐帧展开** | `StaggerGapMs=55`、每行 190ms 淡入、主视觉 40ms 提前、`AnimMs=150` 尺寸补间、`animT` 15ms（`src/Widget.cs:147-148`、`132-133`、`2774-2776`） | 锁屏/灵动岛没有自定义补间动画，Live Activity 只能整块替换内容 | 只保留紧凑布局；需要「展开」时用 App 内页面或深链 |
| **全屏扩散提醒** | 独立 `ScreenReminderForm` 铺满整屏分层窗、截屏垫底、1280×720 设计坐标、30fps（`src/Widget.cs:2561-2700`、`ScreenReminderForm.cs:15/75/119-121`） | iOS 应用无法覆盖全屏、无法在锁屏上画任意色块；Live Activity 最大只有锁屏卡/灵动岛区域 | 提醒交给 **时间敏感本地通知 + Live Activity 提醒卡**（`AlertConfiguration`） |
| **警戒带** | 卡片先展开（`TapeExpandMs=400`），再推全屏色带，消失 0.3s 后拉出抽屉卡 `DrawerForm`（`src/Widget.cs:948-964`、`DrawerForm.cs`） | 同上：跨应用/跨屏幕的持续覆盖不成立 | 拆成「通知横幅 + 卡片内一行提示」 |
| **卡片外呼吸柔光 / 扫边流光** | 独立 `CardGlowForm` 分层窗，`BreatheMs=4200` 余弦呼吸，双圈描边 `EdgeAlpha=0.45`，`UpdateLayeredWindow` 合成（`CardGlowForm.cs:21/40/278-336`） | Live Activity 容器不给卡外发光区；锁屏背景不可控 | 用卡片内描边/渐变模拟；或直接放弃 |
| **磨砂玻璃拟态** | `Frost.PaintSurface` 抓背景做模糊 + `WS_EX_LAYERED` 逐像素 alpha（`src/Widget.cs:2114-2115`、`Frost.cs`）；mica 皮肤强制 `glass` | Live Activity 背景是系统给的材质，不能采样桌面/壁纸 | 用 `.activityBackgroundTint` 深色半透明近似 |
| **顶部高光 / 纸纹 / 螺丝 / 霓虹描边** | `PaintSkinSheen`、`Frost.PaintPaperGrain(…,2.4)`、`DrawScrews`、`DrawNeonEdge`（13/8.5/5.2 DIP 三层内侧辉光）（`src/Widget.cs:2120-2122`、`2218-2250`、`2279`） | 都是像素级拟物，成本高、收益低，且需要逐帧合成 | 保留色板，去掉质感层（quiet 皮肤本身就只有 1px 细线 + 4.5% 高光，最适合移植） |
| **未读左上角快闪** | 每 5s 一次、420ms 内两段 `sin` 闪光；收到消息另有 1300ms 整圈贴边闪光（`src/Widget.cs:4103-4152`、`NoteBlinkIntervalMs=5000`） | Live Activity 不能跑这种高频短动画；锁屏也不适合闪烁（会打扰/被系统降频） | 静态未读圆点 + 通知 |
| **大数字发光** | 8 方向 × 2 圈偏移文字叠透明度（`LineNumber`，`src/Widget.cs:3476-3521`） | 逐像素多遍绘制；Live Activity 排版由 SwiftUI 承担，多层描边会糊 | 用 `shadow` 一档，或去掉 |
| **右键胶囊菜单 / 抽屉 / 纸条面板** | 自定义 `BubbleMenuForm`、`DrawerForm`、`NotePanelForm`（含 MQTT 中继 `NoteRelay`，`src/Widget.cs:3821`、`3895`） | 这些是窗口级交互，Live Activity 不可点击区域内做菜单；灵动岛点击只能深链 | 深链进 App 处理；卡片上只留 1 个入口 |
| **贴边停靠/拖动/置顶/托盘/单实例** | 自绘无边框窗、`WS_EX_TOOLWINDOW`、2s 置顶看门狗、`Mutex` 单实例（`src/Widget.cs:427-435`、`314-316`、`src/Program.cs:12/30-50`） | 桌面窗口模型；iOS 没有自由悬浮窗 | 不适用 |
| **逐像素透明 + 关 ClearType** | 32bpp 分层位图 + `TextRenderingHint.AntiAliasGridFit`（`src/Widget.cs:360-362`、`2143`） | 系统接管文字渲染 | 不适用 |
| **1/100 秒倒计时** | `<60min` 显示 `mm:ss.ff`（`src/Models.cs:2232-2236`） | Live Activity 计时文本精度到秒 | 显示到秒；或干脆 `<1h` 用「剩 3 分」文案 |

---

## 9. 未找到 / 需确认

1. **README 里没有 224×68 / 280×188 这两个数字**（grep `224`、`280x188` 均未命中）。任务描述里的 280×188 只在**显示午休行**时成立，无午休行是 280×164；工资面板 +116。
2. **没有独立的"提醒态"枚举或布局常量表**；提醒态是 `reminding` + `remindEvent` 在同一个卡片窗口里覆盖绘制，几何散落在 `DrawReminderContent`（`src/Widget.cs:1497-1552`）与 `PaintOffAlert`（`1464-1483`）里。
3. **没有逐显示器 DPI 适配**（grep `DpiChanged`/`OnDpiChanged` 无命中），只在构造时取一次 `DpiX/96`，clamp 0.75–3。
4. **皮肤/主题未覆盖的状态色**：`Rest`、`BeforeWork`、`Working` 都用 `theme.accent`（没有单独令牌）；`NotClockedIn` 的琥珀 `#F2B34C` 是硬编码在 `Models.cs:2663`，不在任何皮肤里。
5. **`theme.textMuted` / `theme.textDim` 在卡片上实际未被使用**（卡片用 `MutedMix/DimMix` 现算）；设置窗口/纸条面板才用它们。
6. **大数字 28pt 在 280 DIP 宽内是否会被 `FitFont` 缩小**：按 Consolas 每字约 0.6em 估算，`08:31:12` 8 字符 ≈ 179 DIP < 244 DIP 可用宽，**推测不缩**（未实测，属估算）。
7. **`DrawCardFlood` 里展开态 `barY = S(102)`** 与 `PaintExpanded` 的进度条 y=102 一致（已核对）；但 `barY` 是写死的，若 `BigDip` 被改会发生错位 —— 属于桌面端耦合，iOS 不必复刻。
8. **iOS 侧的承载约束（系统，不在源码里）**：Live Activity 只能整块更新、更新频率与时长受系统预算限制，且不能自定义逐帧动画；本文件的「建议承载」列基于这一前提。

---

## 10. 附录：绘制/逻辑函数速查（便于回查行号）

| 想看什么 | 函数 | 位置 |
|---|---|---|
| 卡片整体合成（底色/皮肤质感/提醒层/描边） | `RenderCard` | `src/Widget.cs:2107-2176` |
| 圆角路径（圆角取 `S(CardRadiusDip)`） | `CardPath` / `RoundPath` | `src/Widget.cs:2183-2186`、`1707` |
| 展开态全部行布局 + 错峰动画 | `PaintExpanded` | `src/Widget.cs:2764-2858` |
| 收起态全部元素 | `PaintCollapsed` | `src/Widget.cs:3531-3548` |
| 工资面板 | `DrawSalaryPanel` / `DrawSalaryAmount` | `src/Widget.cs:2864-2927`、`2933-3021` |
| 进度条（默认 + 5 种皮肤） | `DrawProgress` / `DrawSkinProgress` / `ProgressBarDip` | `src/Widget.cs:3066-3215`、`3261-3464` |
| 大数字（含发光） | `LineNumber` | `src/Widget.cs:3476-3521` |
| 单行文本（对齐/渐变 alpha/省略号） | `Line` | `src/Widget.cs:1487-1495` |
| 字号自适应/宽度截断 | `FitFont` / `FitWidth` | `src/Widget.cs:1567-1583` |
| 5 个右上图标几何 | `DrawGlyph` | `src/Widget.cs:3550-3652` |
| 提醒覆盖（卡片模式） | `PaintCardReminder` / `DrawCardFlood` / `DrawCardReveal` | `src/Widget.cs:2385-2500` |
| 提醒覆盖（全屏模式） | `RenderScreenReminder` / `RenderCard` 分支 | `src/Widget.cs:2561-2700` |
| 提醒内容排版 | `DrawReminderContent` | `src/Widget.cs:1497-1552` |
| 状态机 | `StatusBuilder.Build` / `FillLunch` / `ResolveAfterLunch` | `src/Models.cs:2576-2697`、`2443-2527`、`2558-2574` |
| 时间算法 | `DayPlan.TargetEnd` / `Policy.Build` / `Policy.Clock` | `src/Models.cs:2137-2153`、`2158-2190`、`2228-2239` |
| 工资计算 | `SalaryCalc.Compute` / `DailyMinutes` / `BreakMinutes` | `src/Models.cs:2290-2347`、`2372-2400` |
| 皮肤/主题令牌 | `Skins.BuildAll` / `Themes.BuildAll` / `ThemeDef` | `src/Skins.cs:177-443`、`src/Models.cs:1923-2075`、`1838-1851` |
| 定时器与 tick 编排 | 构造函数 | `src/Widget.cs:250-329` |
| 打卡解析 | `LarkClient.ReadAttendance` / `LooksClockIn` / `LooksClockOut` | `src/Lark.cs:110-235`、`532-549` |
