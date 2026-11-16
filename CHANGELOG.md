# 变更记录（CHANGELOG）

> **记录约定（2026-11-12 定）**：以后每次改动都往这里追加一条，写清日期、改了哪些文件、怎么验证的、装机状态。
> - **平时不升版本号**：`src/Models.cs` 的 `AppInfo.Version` / `src/AssemblyInfo.cs` 停在 `v3.7.0`（2026-11-13 发版时从 3.3.1 升上来）；只有用户说「发版」时才 +1。
> - **平时不打包、不改一图说明书**：`dist\下班倒计时-*.zip` 和 `manual-poster.png` 只在发版时重出。
> - **改完仍然编译 + 跑测试 + 装机**：`.\test\build-tests.ps1` 两套全绿，再用 `.\tools\install-dev.ps1` 替换正在跑的悬浮窗。
> - 每次收尾都要把这一轮改动补进本文件，避免「改了但没人记得」。

## 当前版本：v3.7.0

已装机（FileVersion 3.7.0.0）；日常改动**不升版本号、不出 zip、不动一图说明书**，改动全部追加在下面；用户说「发版」才走下面的发版流程。

### 2026-11-16 · v3.7.0 六套整体界面主题（默认 + 无垠/云母/宣纸/仪表/霓虹）

用户从 `design/theme-packs/` 的 5 套方向稿里选定后，要求做成**能切换的整体界面主题**。落实为 **6 套「界面风格」**：默认（保留原来的 39 套配色主题 + 背景风格）+ 无垠 Quiet / 云母 Mica / 宣纸 Ink / 仪表 Instrument / 霓虹 Neon，在 **设置 →「外观」→「界面风格」** 切换；换的不只是配色，卡片 / 设置窗口 / 小纸条 / 右键胶囊 / 提醒整套一起换。

**① 皮肤引擎（`src/Skins.cs`（新）/ `src/Models.cs`）**
- `SkinDef` 定义每套皮肤：配色（windowBg / windowInk / cardBg / cardEdge / rail / panel / bubble…）、圆角（卡片收起 / 展开 / 小纸条 / 胶囊）、描边 + 顶部高光、进度条画法（原有 / Hairline / Pill / Brush / Segmented / Neon 五种）、数字发光、标题字体，以及玻璃 / 纸纹 / 螺丝 / 霓虹描边 / 朱砂印章 / 无垠 hairline 等令牌。
- `Skins.Current(ui)` / `Skins.ResolveTheme(ui)` / `Skins.UsesGlass(ui)` / `Skins.CardBorderColor` / `Skins.GlowColor`：默认皮肤全部回落到 v3.6 的原取值，五套皮肤返回自带配色。
- `UiSettings.skin`（默认 `"default"`；`Normalize()` 里 null / 空 / 未知 / 大小写错一律归 default，老配置零迁移）。

**② 各窗体换肤（默认皮肤逐像素不变）**
- `src/Widget.cs`：卡片圆角、描边、顶部高光、5 种进度条、数字发光、卡片外光色、仪表螺丝全部走 skin；默认皮肤的现有画法一行没改。
- `src/CardGlowForm.cs`：卡片外柔光 / 扫边颜色改取 `Skins.GlowColor`。
- `src/SettingsForm.cs`：设置窗口从顶部七标签改成**左栏导航 + 内容卡片**（690×640 DIP，七项不变）；外观页新增「界面风格」卡（6 块，点一下立即预览，保存写 `ui.skin`）；换肤后控件树 / 自绘卡片 / 色卡统一刷新；非默认皮肤时 39 套主题色卡置灰 + 提示「该风格自带配色，切回《默认》可选 39 套」；宣纸朱砂印章、仪表螺丝、无垠细线卡等细节。
- `src/NotePanelForm.cs` / `src/BubbleMenuForm.cs`：小纸条面板（气泡 / 输入框 / 昵称胶囊 / 圆角 / 字体）和右键胶囊（124×40 + 246 面板尺寸不变，只换配色 / 圆角 / 字体）跟着皮肤；云母仍走磨砂玻璃。
- `src/Frost.cs`：`Frost.On` 改由 `Skins.UsesGlass(ui)` 驱动；新增 `PaintPaperGrain`（宣纸颗粒，复用噪点 tile）。
- `build.ps1`：编译列表加 `src/Skins.cs`。
- 版本：`src/Models.cs`（AppInfo.Version）/ `src/AssemblyInfo.cs` / `package.ps1` 默认版本 / README 顶部 → **3.7.0**。

**③ 测试与探针**
- `test/Test.cs` / `test/Check.cs`：新增六套皮肤断言（皮肤表 / Normalize / 序列化往返 / ResolveTheme / UsesGlass / 形态令牌 / 默认皮肤回落 / 设置窗口左栏布局 / 色卡置灰提示 / 保存字段 / 小纸条与胶囊换肤），两套全绿：Test 334 / Check 316（原 299 / 299，新增 35 + 17 条，全部 ALL PASS）。
- 新增探针：`test/SkinCardShot.cs`（六套卡片并排 `visual/skins/sheet-expanded.png`、收起态 `sheet-collapsed.png`、工资、提醒；默认皮肤与 v3.6 基线逐像素比）、`test/SkinSettingsShot.cs`（六套设置页 `settings-*.png` + 切肤联动自检）、`test/SkinNoteShot.cs`（小纸条 / 胶囊六套 `visual/skins/after/`）。

**④ 验证**
- 默认皮肤**逐像素比对**：与 v3.6 基线 16 张探针图，11 张 0 像素差、5 张仅 1 像素差（倒计时秒位 / 输入光标相位），等于没变。
- 六套卡片并排（1820×254）、六套设置页（1041×1000）、小纸条 / 胶囊六套并排全部出图并逐张看过版式。
- 两套测试全绿：Test 334 / Check 316。
- 一图说明书新增第 06 节「界面风格 · 六套」并重出 `manual-poster.png`（2400×11386；版本 v3.7.0 / 页脚 2026-11-16）。
- 文档：README 新增「## 界面风格（v3.7.0，六套）」一节、设置窗口改左栏导航描述、更新一图说明书尺寸与测试条数；`package.ps1`「使用说明」补一句界面风格。

**⑤ 修复**
- `UiSettings.skin` 归一化：老配置 / 手改配置里为空、未知、大小写错、带空白的 `skin` 值，会在 `Settings.Normalize()` 里规范成合法的小写 id（`Skins.Get` 先 `Trim()` 再忽略大小写匹配，未命中回退 `default`），避免坏值落盘；断言覆盖 null / 空串 / 未知 / 大小写错 / 首尾空格 / 六个合法 id 往返。

**⑥ 打包 / 发布 / 装机**
- **打包**：`dist\上班工具箱-v3.7.0.zip`（4,158,566 B / 4061.1 KB）：`WorkCountdown.exe`（452,608 B / FileVersion 3.7.0.0）、`一图说明书.png`（2400×11386）、`使用说明.txt`、`install.ps1` / `uninstall.ps1`、`安装.cmd` / `卸载.cmd`、`fonts\`。
- **发布**：commit `e6f773b`（50 文件，+6318/−377）→ push `origin main` + tag `v3.7.0` → GitHub Release **https://github.com/ocezeus/vibeZheyikuai/releases/tag/v3.7.0**（id 446683706），两个资产：`WorkCountdown-v3.7.0.zip`（4061.1 KB）+ `WorkCountdown.exe`（442 KB）。
- **装机**：`.\tools\install-dev.ps1` → **FileVersion 3.7.0.0，PID 9528（14:27）**；装机后把配置里 `ui.skin` 依次改成 paper / neon / instrument 重启并真机截图（`test\visual\real\v370-*-expanded.png`），确认五套皮肤在真分层窗上生效；随后已把配置还原成发版前那份（无 `skin` 字段 → 默认皮肤）。
- **更新链路实测**：新增 `test/UpdateProbe.cs`（发布后跑）→ `UpdateService.Check()` 拿到最新 `v3.7.0` / `WorkCountdown.exe`（452,608 B）/ 直链正确，`IsNewer("3.7.0","3.7.0") = false`（已经是最新）。「检查更新」全链路可用。
- **发版脚本两个坑（本轮踩到并修掉）**：
  1. `tools\release.ps1` 的 `Invoke-Git` 直接 `& git ...`，git 的 warning（如 `LF will be replaced by CRLF`）在 `$ErrorActionPreference='Stop'` 下被当成异常，发布在 `git add -A` 一步假失败（改动已 stage、没 commit）。修法：git 调用统一 `2>&1 | ForEach-Object { "$_" }` 并临时 `Continue`，退出码仍由 `$LASTEXITCODE` 检查。
  2. `package.ps1` 里 `& powershell ... release.ps1` 同样会被子进程 stderr 打断：改成临时 `Continue` + 只认 `$exitCode`；`.gitignore` 行尾统一 CRLF（消除那条 warning 源头）；`package.ps1` 补回 UTF-8 **BOM**（`write` 工具会掉 BOM 的历史坑）。

### 2026-11-16 13:15 · 主题方向稿：5 套整体界面皮肤（设计稿，源码 0 改动）

用户原话：「继续工具箱开发，基于已有的功能，开发更换主题功能；渲染五个主题，用html呈现，我挑选一下」；
确认范围＝**「5 个版本的整体工具箱界面（含卡片 + 设置页 + 小纸条）重设计」**，先出可交互 HTML 让用户挑，挑完再实现。

- 新增 `design\theme-packs\`（全部是新文件，未碰 `src\`）：
  - `index.html` + `packs.css` + `themes.css` + `packs.js`：可交互预览页，顶部五个标签 / `←→` 切换 / 底部「收起态并排」可点；`#paper` 等 hash 直达；`?only=stage` 只截桌面场景。
  - `palettes.js`：39 套内置主题真实配色（从 `design\theme-cards.html` 的 `THEMES` JSON 提取，与 `src\Models.cs` 同源）。
  - `ui-spec.md`（762 行）：现有 v3.6 界面几何 / 字号 / 颜色的 brief，含 9 条「源码里发现、README 没写」的细节（图标顺序 ✈ ¥ ⚙ ↻ ›、设置页主题网格 10×4、提醒扩散已迁到日程页、文字混色公式、设置页真实裁切等）。
  - `render.ps1`（整页 1.25x → `shots\final\`）、`compose.ps1`（场景 → `shots\stage-*.png` + `shots\theme-compare.png` 2×3 对比图）。
  - `README.md`：怎么打开 / 文件说明 / 五套定位 / 出图命令 / 待确认项。
- 五套方向（都画了卡片 + 设置窗口 + 小纸条 + 右键胶囊四块）：01 无垠 Quiet（近黑 + 1px 细线 + 等宽数字，成本低）、02 云母 Mica（磨砂玻璃全套，成本中，`Frost.cs` 已有基础）、03 宣纸 Ink（浅色 + 宋体 + 朱砂印章，成本中）、04 仪表 Instrument（金属 + 琥珀数码 + 24 格分刻 + 螺丝，成本中高）、05 霓虹 Neon（品红 × 青辉光 + 网格，成本高）。
- 五套共用**同一个新结构**：设置窗口从顶部七标签改成左栏导航 + 内容卡片（680×720，现在是 524×640）；卡片 224×68 / 280×188、小纸条 392×282、胶囊 124×40 + 246 长开面板都不变，换的只是皮肤。
- 验证：Chrome 无头出图 5 套 × 2 种（整页 1.25x / 场景 1:1），`theme-compare.png` 1298×1217；`#paper` 等 hash 直达、切标签、色卡填充、时钟均正常；页面无外部依赖，`file://` 双击即可打开。
- 源码 / 版本 / 装机：**0 改动**；版本仍 v3.6.0；未打包、未装机、未动一图说明书。
- 下一步：等用户挑 01–05（或组合），再按选中皮肤改 `Widget.cs` / `SettingsForm.cs` / `NotePanelForm.cs` / `BubbleMenuForm.cs`，跑两套测试 + 装机。
### 2026-11-13 18:40 · 发版 v3.6.0（磨砂玻璃 / 卡片页昵称 / 两排联系人 / 预览通知 / 检查更新 + 自动发版）

用户 5 条需求（原话）：
1. 主题背景增加磨砂玻璃风格；
2. 昵称可以直接在卡片页设置，添加对话旁边，双击设置；
3. 小纸条抽屉页联系人可以显示两行；
4. 右键气泡增加一个按钮，预览通知，点击后可以选择预览的通知类型；
5. 右键气泡增加一个按钮，检查更新，同步完成软件更新功能，全量更新；如果需要上传到 github，则每次打包后需要自动更新 github 代码，发布最新 release。

**① 主题背景 · 磨砂玻璃（`src/Models.cs` / `src/Frost.cs`（新）/ `src/Widget.cs` / `src/NotePanelForm.cs` / `src/BubbleMenuForm.cs` / `src/SettingsForm.cs`）**
- 模型：`UiSettings.bgStyle`（`"solid"` 默认 / `"glass"`），`Normalize()` 里非 glass 一律归 solid（老配置零迁移）。
- 新增 `src/Frost.cs`：`Frost.On(ui)` / `Frost.PaintSurface(g, path, bounds, theme, alpha, strength)`。做法是**半透明主题底色 + 两团柔光雾气 + 模糊过的磨砂颗粒（128×128 tile 缓存，2×2 块噪点 + 2 遍盒式模糊，alpha 只有 5~12）+ 25° 斜向反光带 + 顶部白色高光 / 底部暗边**。分层窗逐像素 alpha，桌面会真的从卡片后面透出来。
  - 不做真实抓屏模糊：每次重绘 BitBlt + 模糊整屏会掉帧（秒/厘秒动画），而且分层窗会拍到自己形成反馈。
  - 应用范围：悬浮卡片（`RenderCard`）、小纸条面板（`Render` + `DrawAddSheet` 两处重填）、右键胶囊气泡（`DrawItem` 的胶囊与长开面板）。
- 设置页：外观页「主题配色」卡片 196→232 高，底部新增 **背景风格：纯色 / 磨砂玻璃** 两个单选 + 说明；「外观与提醒」卡片下移到 248（配置备份仍在 444，页面不溢出）。
- 出图：`test\visual\v36\card-dark-glass.png` / `card-light-glass.png`（对比 solid 同名文件），浅色下透出背景圆、文字不发飘；深色下仍是深玻璃，可读性 OK。

**② 卡片页直接改昵称（`src/NotePanelForm.cs` / `src/Widget.cs`）**
- 小纸条面板头部「＋ 新建对话」**左边**新增一颗 **昵称胶囊**（`NickRect()`，96×28 DIP）：有昵称显示「昵称 小明」（实线），没有显示「＋ 昵称」（虚线）；**双击**在原位开小编辑窗（`NoteField`，MaxLength=`NoteSettings.MaxNick`=12），Esc 取消。
- 单击只提示「双击「昵称」可以改本机昵称」，不弹窗（防手滑）；`HitKind.Nickname` 命中区优先于「＋ 新建对话」，不重叠。
- 落盘链路：`ApplyNickname` → `NoteSettings.CleanNick` → Widget.`SetNoteNickname` → `store.Save` + `RestartNoteRelay()`（presence 立刻带新昵称，已有联系人不用等 15s keepalive）。
- 头部副标题改成「我的 ID xxxxx」（有昵称才追加「 · 昵称」），未读数移到昵称胶囊左边，长 ID/长昵称不会互相压住。

**③ 抽屉页联系人两排（`src/NotePanelForm.cs`）**
- `DrawerRows`：只在「对话列表页」且 `contacts.Count > 4` 时铺 **两排**（4 列 × 2 行 = 一屏 8 个），进了聊天 / 历史页还是保留一排，不挤聊天区。
- 面板高度：未选对话时 124 DIP（一排）/ **158 DIP（两排）**；对话条带 36 / 70 DIP；空列表提示行随之 104→138。`ContactRectAt` 改为 `col=i%4 / row=i/4`、行步进 34 DIP。
- `RefreshData()` 发现对话数量跨过 4 个的分界（来新消息自动建对话 / 删对话）会重新算高度。
- 出图：`test\visual\v36\note-drawer-two-rows-{light,dark}.png`、`note-drawer-one-row-*`（只剩 3 个对话收回一排）、`note-drawer-glass-*`。

**④ 右键气泡 · 预览通知（`src/BubbleMenuForm.cs` / `src/Widget.cs`）**
- 胶囊从 4 颗变 **6 颗**：新增日程 / 修改主题 / 触发上下班 / 窗口置顶 / **预览通知** / **检查更新**；`ColumnHDip` 184→280，`ExtraHDip` 新增 Preview 208 / Update 216；新增铃铛、循环箭头两个矢量图标。
- 「预览通知」长开的卡片是 **2 列 × 3 行**的通知类型：上班提醒（固定日程）/ 午休提醒 / 飞书会议 / Outlook / 普通日程 / 下班提醒（打卡）。点一下 → `Widget.PreviewNotification(kind)` 造一条 `id="preview"` 的临时日程，走**真实提醒动画**（用该类日程自己的配色 / 范围 / 扩散样式），所以看到的就是真提醒的样子。
- 预览只放一遍：`ComputeRemindUntil` 对 preview 返回最短停留，`CheckSticky` / `StepReminder` 对 preview 特判（含「下班提醒」这种本来常驻的样式），到点自动退场；不写配置、不弹托盘气泡、不改 `notified`。预览前若卡片是收起的会先展开。
- 出图：`test\visual\v36\bubble-preview-{light,dark}.png`、`preview-{fixed,lunch,lark,outlook,local,off}.png`（六类真提醒帧）。

**⑤ 右键气泡 · 检查更新 + 全量更新 + 自动发版（`src/Updates.cs`（新）/ `src/BubbleMenuForm.cs` / `src/Widget.cs` / `tools/release.ps1`（新）/ `package.ps1`）**
- 新增 `src/Updates.cs`：`UpdateService`（Repo `ocezeus/vibeZheyikuai`）+ `UpdateRelease` + `UpdatePhase`（Idle/Checking/UpToDate/Available/Downloading/Ready/Error）。
  - 检查：`api.github.com/repos/<repo>/releases/latest`（`JavaScriptSerializer` 解析，挑 `.exe` asset）；被限流 / 不通时退回 `releases/latest` 的 HTML 跳转（只能拿版本号，界面就只给「打开发布页」）。
  - **TLS 1.2**：本机 .NET Framework 默认协议连 GitHub 会报「未能创建 SSL/TLS 安全通道」，静态构造里 `SecurityProtocol |= (SecurityProtocolType)3072` 修好（已实测能拿到 404/200）。
  - 下载：后台线程 + 进度回调（64KB 一块），下到 `%TEMP%\WorkCountdown-update\`；`VerifyDownloaded` 校验大小与 FileVersion 不低于目标版本。
  - **全量更新**：`LaunchUpdater` 写一个临时 `.cmd` —— 等当前 PID 退出 → 备份旧 exe 为 `.bak` → 覆盖 → 重新启动 → 自删；失败自动回滚并 `pause`。配置（%APPDATA%）和 fonts 目录不动。
- 气泡 UI：展开「检查更新」自动查一次；面板显示「当前 v3.6.0 / 最新 vX」、状态行（正在检查 / 已是最新 / 有新版本 / 正在下载 46% / 下载完成 / 失败原因）、Release 摘要一行、主按钮（检查更新 / 再检查一次 / 立即更新（全量）/ 立即重启并更新 / 打开发布页下载）+「打开发布页 →」。下载完成后即使气泡关了，再打开还是「下载完成」状态（Widget 持有状态）。
- 网络 / 下载全在后台线程，结果 `BeginInvoke` 回 UI 线程推进气泡；下载完成弹托盘气泡提示。
- 版本号：`AppInfo.Version` / `AssemblyInfo`（3.6.0.0）/ `package.ps1` 默认 `-Version` / `README.md` 全部 3.6.0。
- **自动发版**：`package.ps1` 打包完自动调 `tools/release.ps1`（新增 `-NoPublish` 可只打包）。release.ps1 六步：校验 zip + exe（FileVersion 必须 = `<Version>.0`）→ `git add -A` + 有改动才 commit → `git push origin main` + tag → 建 / 更新 Release（draft=false / prerelease=false，body 取 `-Notes` > CHANGELOG 对应小节 > 默认文案）→ 先删同名旧 asset 再上传 **zip（完整包）+ WorkCountdown.exe（自动更新用全量 exe）** 两个资产 → 打印 html_url 与直链。token 从 `$env:GITHUB_TOKEN` 或 `git credential fill` 取，不写死；幂等可重复；`-DryRun` 只打印不执行。
- 出图：`test\visual\v36\bubble-update-{available,downloading,latest}-{light,dark}.png`。

**其他 / 发版**
- 测试：`.\test\build-tests.ps1` → **Test 299 / Check 299 两套 ALL PASS**（v3.5 是 252 / 280；连续跑 3 次全绿）；本轮新增 `test\V36Shot.cs` 出图探针（`test\V36Shot.exe test\visual\v36`，含真打 GitHub API 的更新检查与「锁着 → 放开 → 覆盖」的更新脚本实测）。
- 装机：`.\tools\install-dev.ps1` → **PID 36496 / 18:44 / FileVersion 3.6.0.0**；真机卡片截图 `test\visual\v36\real-card-v360.png`，真机右键 6 胶囊截图 `test\visual\bubble-live-bubbles-graphite.png`。
- 发布（2026-11-13 18:46 完成）：`.\package.ps1` → `dist\上班工具箱-v3.6.0.zip`（1,965.9 KB / 2,013,107 B）；自动 commit + push（`618361a` → `c1b8fb9`）+ tag `v3.6.0` + GitHub Release **https://github.com/ocezeus/vibeZheyikuai/releases/tag/v3.6.0**，两个资产：**`WorkCountdown-v3.6.0.zip`**（GitHub 资产名只能用 ASCII，见下）+ **`WorkCountdown.exe`**（自动更新用）。发布后实测：`UpdateService.Check()` 拿到 v3.6.0 / WorkCountdown.exe，下载 399.5 KB 校验 ok、FileVersion 3.6.0.0 → `test\V36Shot.exe` 输出可复现。
- 发版过程中踩到并修掉的 3 个坑（都已写进 `tools/release.ps1` 注释）：
  1. **资产上传必须走 Release 的 `upload_url`（uploads.github.com）**：直接 POST `api.github.com/repos/.../releases/<id>/assets` 会 404。release.ps1 改成从 Release 的 `upload_url` 去掉 `{?name,label}` 再拼参数。
  2. **GitHub Release 资产名会抹掉非 ASCII 字符**（实测 `上班工具箱-v3.6.0.zip` 变成 `-v3.6.0.zip`，`中文.txt` 甚至变 `default.txt`，PATCH 改名也一样）：资产名改 ASCII `WorkCountdown-v<Version>.zip`，本地 zip 名字不变；每次发布会顺带删掉「不该存在」的旧资产（清掉中文名残留）。
  3. release.ps1 末尾显式 `exit 0`（否则子进程 stderr 里的 git 进度会让 package.ps1 误判发布失败）。
- 发布后真机链路实测（`test\V36Shot.exe update-live`）：真 WidgetForm + 真右键胶囊菜单，展开「检查更新」→ 500ms 后状态变成 **UpToDate**（远端 latest = 当前版本），说明 API / 后台线程 / BeginInvoke 推状态 / 气泡渲染整条链路通。
- Release 最终状态（发布后核对）：latest tag=`v3.6.0`、draft=false、prerelease=false、body 6266 字符；资产 `WorkCountdown-v3.6.0.zip` 1965.9 KB + `WorkCountdown.exe` 399.5 KB；本地 `git status` 干净、与 origin/main 同步。
- 已知边界：磨砂玻璃是「拟态」不是真实背景模糊（见 ①）；自动更新只下载独立 exe 资产，`dist\pack\fonts` 与配置保留；Release 里没有 exe 资产时按钮会退化成「打开发布页下载」；GitHub 资产名只能 ASCII（zip 资产叫 `WorkCountdown-v3.6.0.zip`）。
- 一图说明书本轮**已重出**：2400×7426（v3.5 是 7222），07 页 …（见下）；根目录 `note-drawer.png` / `note-columns.png` / `note-delete.png` / `note-flash.png` 已按 v3.6 面板（两排 + 昵称胶囊）重出。

### 2026-11-13 17:35 · 发版 v3.5.0（工作日显示修复 + 个人昵称）

用户 2 条反馈：
1. 设置为固定班次后，在应上班的时间会显示休息，比如周五是上班日，会直接显示休息，修复；
2. 可以设置个人昵称，对方添加对话后会直接显示，并且在群聊中直接展示；
3. 完成后直接打包 3.5 版本。

**① 工作日显示错乱（根因 + 修复 + 旧数据迁移）**
- **根因**：设置页「工作日」复选框顺序是「周一..周日」，内部 `Settings.workdays` 下标是 `0=周日..6=周六`（DayOfWeek），旧代码在两处都直接按下标读写（`cbDays[i].Checked = s.workdays[i]` / `s.workdays[i] = cbDays[i].Checked`），**差一天**。用户勾「周一到周五」保存后实际变成「周日 + 周一到周四上班、周五和周六休息」→ 周五应上班却显示「休息日」，`下次上班` 还会算到周日。
- **修复**（`src/SettingsForm.cs`）：新增 `WorkdayArrayIndex(uiIndex) = (uiIndex + 1) % 7`，`LoadValues` / `Save` 都走它；「工作日」卡片加高、补说明，并新增快捷按钮 **「周一~周五 / 周一~周六 / 每天」**（`SetWorkdayPreset`）。
- **旧配置迁移**（`src/Models.cs`）：只修 UI 不够（用户真机里已经存坏了），新增 `SettingsExtensions.MigrateLegacyWorkdays()`：`version<2` 且形态为「周日上班 + 周一到周四上班 + 周六休息」时把数组右移一位还原成真实意图，`Normalize()` 里调用并把 `version` 升到 2（只做一次）；`SettingsStore.Load()` 在 version 变化时立刻落盘。真机验证：配置 `workdays=[1,1,1,1,1,0,0]`（日一二三四上班）→ 迁移成 `[0,1,1,1,1,1,0]`（周一~周五）、设置页勾选正确、卡片「工作中」。
- **注意**：用户机器上 `mode` 仍保存为 `flex`（历史上没真正保存过「固定班次」），设置页点「固定班次」再保存即可；固定班次下 16:45 会显示「工作中 / 预计下班 18:00」（已用探针验证）。

**② 个人昵称（`src/Models.cs` / `src/NoteRelay.cs` / `src/NotePanelForm.cs` / `src/Widget.cs` / `src/SettingsForm.cs`）**
- **设置**：设置 → 纸条 →「本机 ID 与昵称」里填昵称（`NoteSettings.nickname`，最多 12 字，`Normalize` 去空格/截断）；保存后 `ApplySettings → RestartNoteRelay` 让新昵称立刻随在线状态广播。
- **对方添加就显示**：中继连接时订阅**所有联系人的 presence retained 主题**；presence payload 带 `nick`，`NoteRelay.Profile` 事件（id, nick, online）把昵称交给 Widget → `NoteSettings.SetContactNick()` 写进联系人；`NoteContact.Display()` 优先级 **本机备注 > 对方昵称 > ID / 对话码**。
- **群聊直接显示**：消息 payload 带发送者 `nick`，群聊（对话码）气泡前缀直接是 `昵称：内容`（`NotePanelForm.BubbleText` / `WhoLabel`），没有昵称才退回 `ID 12345：`；`NoteMessage.fromNick` 落线程，历史行「谁」同规则；自己的消息仍显示「你」。
- **兼容**：老消息 / 老配置缺 nick 字段时为 `""`，行为与以前一致；设置页「对话」列表也显示「备注/昵称 · ID」。

**其它 / 发版**
- **版本号 3.4.0 → 3.5.0**：`src/Models.cs`、`src/AssemblyInfo.cs`（3.5.0.0）、`package.ps1` 默认、`README.md` 当前版本、`design/manual-poster.html` 头部 + 页脚。
- **一图说明书 2400×7222**（上一版 6954）：03 小纸条新增「昵称 / 消息前缀 / 备注优先」并换新图（图内可见「我的 ID 48372 · 小明」「小满：…」），补一张「收到时边框闪光」图；05 设置总表「考勤」写清「周一到周日逐日勾 + 三个快捷按钮 + 节假日调休覆盖」，「纸条」加昵称；版本页脚 v3.5.0 / 2026-11-13。
- **使用说明**（package.ps1）：加昵称说明（对方加你 / 群聊直接显示，没昵称才显示 ID），考勤页补「工作日按勾选的周一到周日算」。
- **测试**：`.\test\build-tests.ps1` → **Test 252 / Check 280 两套 ALL PASS**（v3.4 是 248 / 280；新增工作日映射 7 天逐项、旧配置迁移、固定班次 18:00、昵称存取/显示/群聊气泡等断言）；`test\NoteClick.exe` **ALL PASS（28 项）**；独立验收（note-verify）**50 项 ALL PASS**（39 项主验收：工作日往返、假 MQTT broker 下标昵称 / Profile / 消息 nick / 设置页与面板昵称；+11 项旧配置迁移增量：精确形态迁移、边界不误迁、幂等、`Load()` 落盘与不重复写盘，真机 settings.json 已确认迁移落盘）。
- **打包**：`.\package.ps1` → `dist\上班工具箱-v3.5.0.zip`（**1,924,031 B / 1879.1 KB**）：`WorkCountdown.exe`（385,024 B / FileVersion 3.5.0.0）、`一图说明书.png`（2400×7222 / 1,931,638 B）、`使用说明.txt`（7,684 B，首行 3.5.0）、`install.ps1` / `uninstall.ps1`、`安装.cmd` / `卸载.cmd`、`fonts\`；`package.ps1` 仍 UTF-8 BOM。
- **装机**：`dist\pack\install.ps1` → **PID 12712 / 17:29 / FileVersion 3.5.0.0**；装机 exe 与 `dist\WorkCountdown.exe` SHA256 一致（`878D008E8898A24AD60965B5A4460FD97476B39B3074F5B4F37D75019C40155B`）；设置窗口标题 `上班工具箱 3.5.0 · 设置`。
- **真机截图**：`test\visual\real\card-v350-fixed.png`（迁移后周五「工作中」）、`settings-workdays-fixed.png`（周一~周五勾选 + 快捷按钮）、`settings-attendance-v350.png`（迁移前的错位状态，对照用）。
- **已知/提醒**：昵称随 retained presence 走公共 MQTT 明文，任何订阅者能看到「ID + 昵称 + 最后在线时间」（ID 本来公开）；v3.5 之前的老消息没有 `fromNick`，群聊里先显示 `ID xxxxx：`，对方再发一条就变昵称。
- **下一版**：3.5.1 / 3.6.0 按需。

### 2026-11-13 16:45 · 发版 v3.4.0（小纸条「添加对话 / 对话码」+ 边框加粗 + 滚动 & 右键删除 + 大卡片图标统一）

用户原话：「打包更新版本3.4」。上一轮 16:15 的 4 条改动随这一版正式发出。

- **版本号 3.3.2 → 3.4.0**（四处一起）：`src/Models.cs`（`AppInfo.Version`）、`src/AssemblyInfo.cs`（AssemblyVersion / FileVersion 3.4.0.0）、`package.ps1` 默认 `-Version`、`README.md` 当前版本；另外 `design/manual-poster.html` 头部 `.v` + 页脚同步 v3.4.0（日期保持 2026-11-13）。`package.ps1` 保持 **UTF-8 BOM**（前 3 字节 239,187,191）。
- **一图说明书 2400×6954**（v3.3.2 是 2400×6619）：
  - 03 小纸条整节重写：标题「不装账号：5 位数字找人，5 位字符开一个对话」；怎么用 10 条（本机 ID / 新建对话 / 保留 ID / 改名 / 看消息 / 对话多了（滚轮翻）/ 右键删除 / 发消息 / 收到时 / 两套样式）；尺寸与传输补「边框 1.8 DIP（比之前粗一点）」「主题：1:1 走对方 ID，对话码走 `xzt/v1/room/<hex>`」；底部 note 改成「对话码没有密码、公开可进，别用公司名/工号这类码；中继明文、不存离线消息」。
  - 05 设置总表「7 纸条」、07 已知边界同步从「联系人」改成「对话」；全文无「3.3.x」旧版本号。
  - 根目录 `note-drawer.png`（对话码房间，气泡带 `ID 11788：`）588×423、`note-columns.png`（双栏历史）705×768、`note-flash.png` 420×282 用新面板重出；**新增 `note-delete.png`**（588×186，滚轮 + 右键红色「删除」）。
  - 出图：`.\test\NoteShot.exe drawer|columns test\visual\note-poster huang-liuli` → `.\design\render-poster.ps1 -CssHeight 5200` → `manual-poster.png`（宽度必须 2400）。
- **打包**：`.\package.ps1` → `dist\上班工具箱-v3.4.0.zip`（**1,760 KB / 1,802,306 B**）：`WorkCountdown.exe`（380,928 B / FileVersion 3.4.0.0）、`一图说明书.png`（2400×6954 / 1,805,598 B）、`使用说明.txt`（首行 3.4.0；小纸条段落重写成「对话 / 对话码 / 滚轮 / 右键删除」，设置七页里「纸条」也改成对话）、`install.ps1` / `uninstall.ps1`、`安装.cmd` / `卸载.cmd`、`fonts\`。
- **测试**：`.\test\build-tests.ps1` → **Test 223 / Check 268 两套全绿**；`test\NoteClick.exe` → **ALL PASS（28 项；锁屏 27 PASS + 1 skip）**；`test\CardGlyphProbe.exe` → 17 PASS；独立验收探针 **71 项 ALL PASS**（`test\verify\report.md`）。
- **装机**（走最终用户路径 `dist\pack\install.ps1`）：**PID 18064 / 16:42 / FileVersion 3.4.0.0**；装机 exe 与 `dist\WorkCountdown.exe` SHA256 一致（`E3F2E4FD897126FE82AEF6F60B150139218B1F4650D2C6D3B91E8152A876CA84`）；设置窗口标题实测 `上班工具箱 3.4.0 · 设置`；桌面快捷方式 `上班工具箱.lnk` 指向装机 exe。
- **真机截图**（装机后 16:43）：`test\visual\real\card-expanded-real.png`（展开态，右上角 5 个图标一条水平线）、`note-panel-contacts.png`、`note-panel-rightclick-delete.png`（右键红色「删除」，**只看未点**）。截图时用户正在真机使用（主题 graphite，4 个对话：93183 祥源 / 10825 林总 / 自己 / 东联），只开面板截图，没删任何对话。
- **交付**：`dist\上班工具箱-v3.4.0.zip`；下一次发版从 3.4.1 / 3.5.0 起算（老 `下班倒计时-v3.3.2.zip` 是最后一个旧名包）。

### 2026-11-13 16:15 · 小纸条「添加对话」（任意 5 位字符 / 对话码）+ 边框加粗 + 滚动 & 右键删除 + 大卡片图标统一

用户 4 条反馈（原话）：
1. 添加联系人改为添加对话，可以输入任意五位字符，如果不同用户输入字符相同，则完成对话创建，同时保留每个人的ID方便直接创建对话；
2. 小纸条卡片边框稍微粗一点；
3. 保存有多个对话的情况下，可以鼠标滚动查看更多对话，同时右键对话，会出现删除按钮；
4. 大卡片的几个按钮大小和高度不一致，调整一下。

**① 添加联系人 → 添加对话（`src/Models.cs` / `src/NoteRelay.cs` / `src/NotePanelForm.cs` / `src/Widget.cs` / `src/SettingsForm.cs`）**
- 规则：`5 位纯数字` = 和这个 ID 的人 **1:1 对话**（沿用原来的收件箱主题 `xzt/v1/msg/<ID>`，查在线照旧）；`任意其它 5 位字符`（中文 / 字母 / 数字 / 混合）= **对话码**，收发主题 `xzt/v1/room/<hex(UTF8(码))>`，**不同的人输入同一个码就进同一个对话**；中继只增订阅、删掉的对话不会因为还收着消息又冒出来。
- 模型：`NoteSettings.ValidCode()`（正好 5 个字符、不含空白/控制字符）、`IsRoom()`、`AddConversation(code, remark)`；`NoteContact.IsRoom` / `Display()`（没名字的对话码显示「对话 xxxxx」）；`Append()` 支持对话码线程（`to` 是码、`from` 仍是发送者个人 ID）；`Normalize()` 用 `ValidCode` 校验联系人和线程。`AddContact()` 保留为「只接受 5 位数字」的兼容入口（老探针 / 老代码不受影响）。
- ID 不丢：对话码对话里收到的气泡前面带 `ID 12345：`，历史行左边直接显示发送者 5 位 ID —— 看到就能用「＋ 新建对话」按 ID 直接建一对一；抽屉式房间对话补一行「名字 + 对话码」头（双栏本来就有），不然不知道自己在哪个对话里。
- 面板：标题「＋ 新建对话」；新建页字段变成「对方的 5 位 ID / 5 位对话码」（5 位数字 → 查在线；对话码 → 提示"对方输入同一个码就能进"），按钮「创建对话」；校验失败提示「要 5 位字符」；建完自动进这个对话并滚到可见。
- 设置页「纸条」页：卡片「联系人」→「对话」，列表显示「名字 · 对话码 xxxxx / ID xxxxx」，新建对话框文案同步，标题「新建对话」。
- 中继：`RoomTopic` / `ParseRoomTopic` / `HexEncode` / `HexDecode`（任意字符含 `# + /` 都能安全进主题）；`SubscribePacket(topic, packetId)` 加了包 ID 计数；`RefreshSubscriptions()` 在新建对话后补订阅，断线重连时按联系人全量补订阅。
- 安全性：对话码没有密码，**知道码的人都能进**；公共中继明文，README 里已写明别用公司名 / 工号 / 生日这类码。

**② 小纸条卡片边框稍微粗一点（`src/NotePanelForm.cs`）**
- 新增 `CardBorderDip = 1.8f`，外描边从 `max(1f, scale)`（1 DIP）改成 `max(2f, S(CardBorderDip))`；150% 下从「顶部 2 行像素」变成「4 行像素」，浅色 / 深色主题都明显能看出来，但仍是细边框、不抢内容。

**③ 对话多了能滚动 + 右键删除（`src/NotePanelForm.cs`）**
- 列表改成滚动窗口：抽屉式横排一屏 4 个、双栏竖排按栏高算（正常高度 5 行），`ContactScroll` / `ScrollContacts(±1)`（clamp 到首尾）、选中 / 新建对话自动 `EnsureContactVisible` 滚到看得见；抽屉式右下角三个点、双栏右侧细轨道 + 滑块提示还能滚。
- 滚轮：面板 `OnShown` 挂 `IMessageFilter`，**鼠标在面板上就能滚**（面板不一定拿焦点），输入编辑窗打开 / 新建对话页时不抢滚轮；`OnMouseWheel` 作为焦点在面板上的兜底。
- 右键某个对话：该对话右半边盖上红色「删除」按钮（`ShowDelete` / `DeleteId` / `Delete` 命中区插在最前优先命中），点一下 `DeleteConversation()` 同时删联系人和本机这段 8 条消息，并回调 `Widget.DeleteNoteContact` 落盘；点别处 / 滚动会自动收起删除按钮。

**④ 大卡片 5 个图标大小 / 高度统一（`src/Widget.cs`，由 teammate card-glyph 完成）**
- `DrawGlyph()`：五个图标统一 12 DIP 几何外框 + 1.5f*scale 圆头线宽 + Round 拐角（消掉飞机尖角的 miter 长刺）；齿轮 6→8 齿；飞机 / 刷新等比缩到 12 DIP；折叠箭头宽度 5.4→11 DIP；¥ 改成取字体轮廓量**真实墨迹框**再缩放到 12.4×13.4 DIP 并光学居中（没有 ¥ 字形时退回 DrawString）；`fontGlyphSalary` Bold→Regular 让笔画粗细和线条图标一致。命中区（24 DIP）、间距、颜色、顺序都没动。
- 结果：五个图标墨迹外框 12.7~14.0 DIP、宽/高彼此相差 ≤5.3%、中心偏差 ≤1px（改前宽 11~26px 差 15px），探针 17 PASS / 0 FAIL。

**改动文件（本轮）**
| 文件 | 改了什么 |
| --- | --- |
| `src/Models.cs` | `ValidCode` / `NormalizeCode` / `IsRoom` / `AddConversation`；`NoteContact.IsRoom` + `Display()`；`Append` 支持对话码；`Normalize` 用 `ValidCode` |
| `src/NoteRelay.cs` | 对话码主题 `xzt/v1/room/<hex>`、订阅管理（`RefreshSubscriptions` / `SubscribeRooms` / `SubscribePacket(topic,id)`）、`SendMessage` 支持对话码、`DecodeMessage` 允许 `to` 为码 |
| `src/NotePanelForm.cs` | 新建对话页文案 / 校验 / 路由；抽屉式房间头；气泡 / 历史行带发送者 ID（`BubbleText` / `WhoLabel`）；`CardBorderDip=1.8`；滚动窗口 + `ScrollContacts` + `EnsureContactVisible` + 滚轮消息过滤器；`ShowDelete` / `DeleteConversation` / `Delete` 命中区；`onDeleteContact` 构造参数 |
| `src/Widget.cs` | `AddNoteContact` 走 `AddConversation` + 补订阅；新增 `DeleteNoteContact`；`OnNoteMessage` 区分个人 / 对话码（忽略自己发布的回显、删掉的对话不再重建）；`DrawGlyph` 五图标统一 + `fontGlyphSalary` Regular |
| `src/SettingsForm.cs` | 纸条页「对话」、新建对话框文案 / 校验 / 列表显示、删除提示 |
| `test/Test.cs` | +16 条：`ValidCode` / `IsRoom` / 对话码建对话 / 对话码线程往来（自己 + 别人）/ 主题编码解回 / 通配符码 / hex 可逆 / JSON 解码 / 存读 |
| `test/Check.cs` | +21 条：边框 1.8 DIP + 像素厚度 / 空码 & 4 位码校验 / 任意 5 位字符建对话 / 5 位数字仍走个人 ID / 房间气泡与历史行发送者 ID / 抽屉一屏 4 个 + 滚动 ±1 & clamp / 右键 Delete 命中区 / 点删除连线程一起删并回调 / 双栏滚动 / 设置页对话码行 |
| `test/NoteShot.cs` | 6 个对话 + 1 个对话码房间的 fixture；新增「滚动 + 右键删除」「对话码房间」两张图；主题参数照旧 |
| `test/NoteClick.cs` | 无需改（重建即可，坐标 / 行为保持兼容） |
| `README.md` | 小纸条章节改成「对话 / 对话码 / 滚动 / 右键删除 / 1.8 DIP 边框」，测试条数与文件说明同步 |

**验证 / 装机**
- `.\test\build-tests.ps1` → **Test 223 / Check 268 两套 ALL PASS**（上一轮 207 / 247；本轮 +16 / +21）。
- `test\NoteClick.exe` 重编译后 **ALL PASS（28 项；锁屏走 PostMessage，27 PASS + 1 skip）**。
- `test\NoteShot.exe` 出图：`test\visual\new-drawer\` / `new-columns\`（ivory）、`new-drawer-dark\` / `new-columns-dark\`（huang-liuli），含截图 / 滚动 + 右键删除 / 新建对话 / 对话码房间。
- 真机截图（装机后 16:08，`test\visual\real\`；之后修了双栏「＋ 新建对话」命中区、把双栏一屏行数从 4 调到 5，16:23 再次装机 PID 35852）：大卡片 5 图标已等高同大小（`card-expanded-real.png`）；小纸条面板边框变粗、右上角「＋ 新建对话」、右键弹出红色「删除」按钮（`note-panel-rightclick-delete.png`，**只看未点，用户真数据 93183 / 10825 未动**）。
- `.\tools\install-dev.ps1` 装机 → **PID 35852 / FileVersion 3.3.2.0**（16:23）。
- 独立验收（teammate note-verify，`test\verify\`）：自己的探针 **71 项断言 ALL PASS / exit 0**，4 条需求逐条 ✅（对话码模型 + 本地假 MQTT broker 端到端抓包、边框 2 行→4 行、滚动 ±1/clamp/末屏、右键红色删除命中区 64×36 与删除回调、5 图标包围盒极差 15→2px）；报告 `test\verify\report.md`，对比图 `test\visual\verify-after\compare-*.png`。
- **版本号仍是 3.3.2**，本轮**没有打包、没动一图说明书**；下次发版按 3.3.3 走（zip 名 `上班工具箱-v3.3.3.zip`）。

### 2026-11-13 15:05 · 项目改名：下班倒计时 → **上班工具箱**

用户原话：「以后这个项目叫 上班工具箱」。

- **改的是产品 / 项目名（用户可见部分）**，内部标识保持不变、老配置和老快捷方式不会失效：
  - 窗口标题 / 托盘 tooltip / 气泡提醒标题 / 首次使用窗口：`上班工具箱`；设置窗口：`上班工具箱 3.3.2 · 设置`；
  - exe 文件属性：`FileDescription` / `ProductName` = 上班工具箱（`src/AssemblyInfo.cs` 的 Title / Product / Description）；
  - 桌面快捷方式：`上班工具箱.lnk`（Description「上班工具箱悬浮窗」），旧的 `下班倒计时.lnk` 已删除；
  - 导出配置默认文件名：`上班工具箱配置.json`；
  - 打包：`package.ps1` 的 zip 名改成 `dist\上班工具箱-v<Version>.zip`，安装 / 卸载脚本里的快捷方式名同步；
  - README / HANDOFF / 根 HANDOFF / 设计稿（`manual-poster.html`、`feature-sheet.html`、`manual.html`、`salary-*`、`design/note/settings-note.html`）/ `tools` 里按窗口标题找窗口的脚本（`shot.ps1` / `preview.ps1` / `dlg.ps1` / `tab.ps1`，tab 改成 `-like '上班工具箱*设置'`）全部同步。
- **保持不变（历史兼容）**：`WorkCountdown.exe` 文件名、`%APPDATA%\WorkCountdown` 配置目录、`%LOCALAPPDATA%\Programs\WorkCountdown` 安装目录、注册表 Run 项名、命名空间 / 类名、`work-countdown` 项目目录名——改这些会让老配置、老快捷方式和单实例判断失效；要彻底改名应单独做一次迁移。
- **版本号没动**：仍是 v3.3.2，本轮**没有重新打包**；`dist\下班倒计时-v3.3.2.zip` 是改名前的最后一包（包内 exe 的 FileDescription 还是旧名）。下次用户说「发版」时按新名出 `上班工具箱-v3.3.3.zip`。`manual-poster.png` 已按新名重出（2400×6619）。
- **验证**：`.\test\build-tests.ps1` → **Test 207 / Check 247 全绿**；`.\tools\install-dev.ps1` 装机 → PID 13800 / FileVersion 3.3.2.0；枚举窗口标题 = `上班工具箱`；exe `FileDescription` / `ProductName` = 上班工具箱；桌面快捷方式只剩 `上班工具箱.lnk` 且指向装机 exe。

### 2026-11-13 14:45 · 发版 v3.3.2（联系人名字居中/大一号 + 去掉进对话的白雾 + 抽屉未选对话收窄）

用户反馈与澄清：
1. 刚点开对话，聊天内容上有白色遮罩，去掉；
2. 抽屉式未选中聊天时，空白太多，修复下；
3. 上一轮的第 4 条澄清：「联系人名字**本身**需要居中、字号大一个，而不是双击后字号和输入框变大」；
4. 改完打包新版本。

- **版本号 3.3.1 → 3.3.2**：`src/Models.cs`（`AppInfo.Version`）、`src/AssemblyInfo.cs`（AssemblyVersion / FileVersion）、`package.ps1` 默认 `-Version`、`README.md` 当前版本、`design/manual-poster.html` 头部 `.v` + 页脚，全部 3.3.2 / 2026-11-13。
- **① 联系人名字居中 + 字号大一号**（`src/NotePanelForm.cs`）：新增 `fName = F(12.5f)`（≈9.375pt，原 `fContact` 11.5px≈8.625pt），`DrawTextCenter()` 水平垂直居中画；抽屉式横排胶囊按剩余宽度居中（未读点单独占位），双栏竖排胶囊右侧给未读点/时间留位后居中；双栏聊天头部名字同字号。
- **② 双击改备注的编辑窗回到普通样式**：不再放大、不再居中（用户明确要放大/居中的是名字本身）。编辑窗直接盖在联系人名字上、和名字**一样大**（抽屉式 129×45 物理像素）；为此 `NoteField.WndProc` 处理 `WM_GETMINMAXINFO` 把最小跟踪尺寸降到 1×1——否则系统会把无边框窗口钳到约 202×56，看起来像「输入框变大了」。圆角 / 回车提交 / 点别处提交 / Esc 取消 / 8 字上限都保留。
- **③ 去掉进对话的「白雾」**：进聊天态不再做 180ms 整体淡入（`st = 1`），内容立刻不透明；浅色主题下不再用半透明白填充——收到的气泡改淡墨色（`Mix(bgBottom, textMain, 0.07)`）、未选中的联系人胶囊和次级按钮同理，避免白底上像盖了一层白纱。发新消息的「旧消息上移、新消息淡入」保留（只动那两行）。
- **④ 抽屉式未选对话收窄**：新增 `DrawerContactsHDip = 124`（150% 下 588×186），标题 + 联系人 + 一句提示就够，点开对话长回 392×282；联系人还没选时不再空一大片。`SetState` / 构造函数 / `DrawBody` 的提示行位置一起改。
- **测试**：`test/Check.cs` 新增断言：联系人名字居中（像素）、名字字号 ≥9.3pt、改备注编辑窗普通样式且与胶囊同尺寸、刚点开对话内容立刻不透明（无白雾）、抽屉式未选对话 392×124 并点开后长回 282、浅色主题气泡/胶囊不再用白色。`.\test\build-tests.ps1` → **Test 207 / Check 247 全绿**（原 229，本轮 +18）。
  `test/NoteClick.cs` 扩到 **28 项**（锁屏走 PostMessage 兜底 27 PASS + 1 skip；解锁后 28 项全跑）：新增「备注编辑窗和联系人名字一样大（129×45）」「不放大不居中」「名字字号大一号」。本轮真机复核时系统已锁屏，真实鼠标路径仍需解锁后重跑一遍。
  `test/NoteShot.cs` 支持第 3 个参数选主题（`ivory` 出浅色图，`huang-liuli` 出深色图）；离屏渲染前手动把 `heightNow` 落到 `heightTo`，避免抽屉式收窄后聊天态仍画成 186 高。
- **出一图说明书**：`design/manual-poster.html` 更新 03 节（新增「改备注」、「发完可以接着打」、抽屉「未选对话收成 124」），根目录 `note-drawer.png` / `note-columns.png` / `note-flash.png` 用新面板重出（深色 huang-liuli），`.\design\render-poster.ps1` → `manual-poster.png` **2400×6619**（2400×6485 → 6619）。
- **打包**：`.\package.ps1` → `dist\下班倒计时-v3.3.2.zip`（**1,648 KB**）：`WorkCountdown.exe`（372,736 B / FileVersion 3.3.2.0）、`一图说明书.png`（2400×6619）、`使用说明.txt`（首行 3.3.2）、`install.ps1` / `uninstall.ps1`、`安装.cmd` / `卸载.cmd`、`fonts\`。
- **装机**：`dist\pack\install.ps1` 实跑（最终用户路径）→ FileVersion 3.3.2.0，PID 37880（14:40），装机 exe 与 `dist\WorkCountdown.exe` SHA256 一致（`FFCE6F6A…8F1616C`），桌面快捷方式已更新。截图：`test\visual\note-new\`（ivory 浅色六张）、`test\visual\note-poster\`（深色，供说明书）。

### 2026-11-13 12:05 · 小纸条 6 条体验修复（输入不断 / 不整块刷新 / 圆角编辑窗 / 双击改备注 / 双栏气泡对齐 / 卡片细边框）

用户原话（6 条）：
1. 每次输入后，输入框保持可以继续输入，不应该需要再点击
2. 发送新消息后，不应该整体刷新，而是原有信息上移、新消息进入，不用显示已发出
3. 点击输入框后，文本框不要直角
4. 双击联系人名字，可以修改备注，文本需要居中，字号再大一个
5. 极简双栏的聊天气泡和文本有错位
6. 小纸条整个卡片加上细边框

**改动（`src/NotePanelForm.cs` 为主，`src/Widget.cs` 补一个改备注回调）**

- **① 发完继续输入**：`NoteField` 新增 `SubmitKeepsOpen`（回车不关窗，返回 true 就清空文字、保持焦点）和 `ClearText()`；聊天输入框回车发送后编辑窗**留在原地**，可以直接打下一句；点发送按钮那条路径编辑窗会被点掉，`SendCompose` 成功后 `EnsureComposeEditor()` 立刻补开并聚焦。发失败（中继没连 / 对方不在线）时不清空，文字留着可以重发。
- **② 不整块刷新**：`SendCompose` 不再 `SetState(History, true)`、不重置 `stateAt`、不再显示「已发出」。只记 `sentAt / sentPrevCount`，用 240ms 的 `EnterProgress()` 做两件事：聊天态原来最下面那条**往上挪一格**、新消息从下往上淡入；历史态固定行高（抽屉 36 / 双栏 38 设计 px），新消息从底部进入，老消息整体上移一位，其它行不再重新淡入。
- **③ 圆角编辑窗**：`NoteField` 用圆角 `Region` + 自绘 1 DIP 强调色圆角边框（以前是直角矩形 + 1px 实心边），TextBox 在窗内**垂直居中**；聊天输入字号 12.5 设计 px（≈9.375pt，和面板正文同级）。
- **④ 双击名字改备注**：同一个联系人名字两次 MouseUp（间隔 ≤ `SystemInformation.DoubleClickTime`）→ `EditRemark()`，在名字上盖编辑窗：**文字居中**、字号 13.5 设计 px（≈10.125pt，比列表名字 8.625pt 大一号）、最多 8 字；回车提交、点别处也提交（`CommitOnBlur`）、Esc 取消；`ApplyRemark()` 先改面板再回调 Widget（`RenameNoteContact` 落盘），空备注恢复显示 ID。
- **⑤ 双栏气泡对齐**：气泡统一走 `DrawBubble()`——**文字在气泡里垂直居中**（以前画在 y+12，34 高的气泡偏上），**时间放到气泡外面**（自己发的在左、收到的在右，以前塞在气泡右下角，长文字会和时间撞一起）；历史气泡也用同一套；双栏聊天头部的名字和 ID 改成跟着名字实际宽度排，长备注不会压住 ID。
- **⑥ 卡片细边框**：整卡圆角路径**内缩半笔宽**画 1 DIP 强调色细边（alpha 0.62）。以前笔迹正好画在窗口边上，一半被裁掉，150% 下只剩 0.75px，几乎看不见。
- 顺手：`CursorOnPanel()`——锁屏 / 无输入桌面时 `Cursor.Position` 恒为 (0,0)，编辑窗“点到别处收起”改用最后一次面板内鼠标坐标兜底，避免面板被误关。

**测试**

- `test/Check.cs`：新增 13 条（发送后停在原状态、stateAt 不重置 / 不再「已发出」/ 记了进入动画 / 聊天编辑窗圆角 + 圆角 Region 与实际窗口一致 + ≥9pt / 备注编辑窗居中 + 字号比列表大 / 改备注去空格、8 字截断、清空恢复 ID / 整卡细边框像素断言（聊天页 + 添加页）/ 气泡文字垂直居中像素断言）。`.\test\build-tests.ps1` → **Test 207 / Check 242 全绿**。
- `test/NoteClick.cs`：交互回归扩到 **26 项 PASS + 1 项按环境 skip**（连发第二句不用再点输入框、发完编辑窗还在且清空、点发送按钮发完留空输入框、双击改备注居中/字号/带出原备注/提交落盘）。当前会话**锁屏、拿不到前台**，真实鼠标事件到不了窗口——探针会自动检测并退化成 `PostMessage` 点按（点按钮前手动补“编辑窗失焦收起”这一步，顺序和真机一致），结果 **ALL PASS**；解锁后需要再跑一次真鼠标版本（见 HANDOFF）。
- `test/NoteShot.cs`：加 `SetProcessDPIAware()`，现在出的是 150% 真实尺寸图（抽屉 588×423 / 历史 588×714，双栏 705×447 / 705×768）。新图在 `test\visual\note-new\`。
- `test/NoteEditorShot.cs`（新）：`PrintWindow` 拍编辑窗（锁屏也能拍），见证圆角 + 居中，图在 `test\visual\note-fix\`（`editor-compose.png` 366×56、`editor-remark.png` 222×56、`panel-border.png` 705×447）。补修后重出：四边描边齐全（顶/底都在），Add 页也有整卡边框。

**复核后补修（12:25，独立复核发现 3 处）**

- **「＋ 添加联系人」页没有细边框**：`Render()` 先画边框，紧接着 `DrawAddSheet()` 用不透明渐变把整卡重填一遍 → 边框被完全盖掉（chat/history 页有，Add 页没有）。**把边框绘制挪到所有内容之后**。
- **圆角编辑窗底边缺描边**：系统对无边框窗口有最小窗高（150% 下 56px），申请的 51 高会被钳到 56，而圆角 `Region` 是按 51 建的 → 底部 5px 被裁。`NoteField` 新增 `RebuildShape()`，在 `OnShown` / `OnResize` 里按**实际 ClientSize** 重建 Region。
- **气泡文字视觉偏上**：像素量出来文字中心比气泡中心高约 6.5px（150%）。`DrawBubble()` 给文字补 `S(4)`、时间补 `S(3)` 视觉居中偏移；修后实测差 1px（Check 里有像素断言，容差 5px）。
- 顺手：`SelectContact()` 换联系人时清掉上一人的草稿；Esc 取消输入时面板缩回聊天态；双栏聊天头部的名字也能双击改备注（和左栏一致）。
- Check 相应补到 **242** 条（新增圆角 Region 尺寸、Add 页边框、气泡文字居中等像素断言）；NoteClick 仍 26 PASS + 1 skip。

**装机**：`.\tools\install-dev.ps1` → FileVersion 3.3.1.0，PID 23960（12:23），`dist\WorkCountdown.exe` 371,712 B，装机 exe 与 dist SHA256 一致（`A59603D8…42F3EE933`）。**版本号仍冻结在 3.3.1、未打包、未动一图说明书**（用户没说发版）。

### 2026-11-13 11:13 · 发版 v3.3.1（小纸条交互修复 + 重新打包 / 说明书）

用户原话：「然后打包更新版本」。3.3.0 的 zip 里是带小纸条三个交互 bug 的旧 exe，这次把 11:10 的修复一起发出去。

- **版本号 3.3.0 → 3.3.1**：`src/Models.cs`（`AppInfo.Version`）、`src/AssemblyInfo.cs`（AssemblyVersion / FileVersion）、`package.ps1` 默认 `-Version`、`README.md` 当前版本、`design/manual-poster.html` 头部 + 页脚，全部 3.3.1 / 2026-11-13。
- **本版修复**：见下面 11:10 条目 —— 编辑窗改非模态（不再禁用面板）、输入实时同步 + Enter 提交（不再丢输入）、长高后重建命中区（底部输入框 / 发送按钮能点）、查在线 / 空消息提示。3.3.0 的 zip 不含这些。
- **打包**：`.\package.ps1` → `dist\下班倒计时-v3.3.1.zip`（1,627 KB）。包内 7 个文件 + `fonts\`：`WorkCountdown.exe`（368,128 B / FileVersion 3.3.1.0）、`一图说明书.png`（2400×6485，v3.3.1）、`使用说明.txt`（首行 3.3.1）、`install.ps1` / `uninstall.ps1`、`安装.cmd` / `卸载.cmd`。
- **一图说明书**：内容与 v3.3.0 发布版一致（第 03 节小纸条、七页设置、界面 ✈），只更新版本号 / 页脚日期，重出 2400×6485 PNG（`design\render-poster.ps1`）。
- **验证**：`.\test\build-tests.ps1` → **Test 207 / Check 229 全绿**；`.\test\NoteClick.exe` 13 项交互回归 **ALL PASS**；`dist\pack\install.ps1` 实跑安装 → FileVersion 3.3.1.0、exe 哈希与 `dist\WorkCountdown.exe` 一致、桌面快捷方式已更新、应用正常启动。
- **装机**：PID 36012（11:13），FileVersion 3.3.1.0；配置里的验证联系人 93183 / 祥源保留。

### 2026-11-13 11:10 · 修小纸条「添加联系人点不动 / 查不了 ID / 发不出消息」

用户反馈：添加联系人里很多按钮点不了、无法搜索好友、发不出去消息。查出来是 3 个独立 bug 叠在一起（3.3.0 的 zip 里都带着，下次发版必须重新打包）：

- **① 编辑窗是模态的（`ShowDialog`），打开后整个面板被禁用**：点一下 ID / 备注 / 输入框，弹出来的小编辑窗会让面板 `Enabled=false`，之后「查在线 / 添加为联系人 / 发送」全点不动；而且点外面不会关，人就卡死在那一屏。——改成**非模态编辑窗**（`Show(this)`）：面板不再禁用，点到面板其它按钮时编辑窗自动收起、这次点击继续落到按钮上。
- **② 编辑窗按 Enter 不保存，输入被丢掉**：`NoteField` 的 Enter 只 `Close()`，没设 `DialogResult`，`ShowDialog` 返回 `Cancel`，调用方 `if (== DialogResult.OK)` 永远不成立 —— 所以 ID 永远是空的（查在线静默无反应、添加提示「ID 要 5 位数字」）、消息永远是空的（发不出去）。——改成输入**实时同步**回面板（`TextChanged`），Enter 收起并提交、Esc 还原原值，不再依赖 `DialogResult`。
- **③ 面板长高以后命中区没跟着走**：点输入框 → 面板从 423 长到 714，但 `BuildHits()` 只在 `SetState` 时算过一次（那时还是旧高度），底部输入框 / 发送按钮的命中区还停在 y≈342，用户点底部输入框 / 发送按钮全部点空。——`StepAnim` 每次改高度后重建命中区；并且**等长高动画结束再开编辑窗**（以前编辑窗开在旧位置，离输入框差 291px）。
- 顺手补的提示：ID 不合法点「查在线」提示「先填 5 位数字的 ID」；查完显示「● 在线，可以加 / ○ 没在线，也能先加」；空输入点发送提示「写一句再发」；空 ID 点「添加为联系人」会直接打开 ID 编辑窗。按钮文案「查找」→「查在线」，底部提示改成「点 ID / 备注框直接输入，回车确认」。
- **验证**：`.\test\build-tests.ps1` → **Test 207 / Check 229 全绿**（新增 3 条：空 ID 查在线提示、空 ID 添加提示 + 自动开编辑窗、空消息提示）。新增 `test/NoteClick.cs` 交互回归探针（真实鼠标 + 真实点击顺序，13 项全 PASS）：非模态编辑窗、输入实时同步、查在线、回车提交备注、添加成功、长高后编辑窗贴输入框、回车发送、历史态发送按钮 —— `.\test\NoteClick.exe`。
- **真机**：装机的 exe 上用真实鼠标走了一遍完整流程：＋联系人 → 点 ID 框输入 **93183** → 回车 → 「● 在线，可以加」→ 备注 **祥源** → 添加为联系人 → 面板切到和 93183 的聊天、联系人列表里显示「祥源」（`%APPDATA%\WorkCountdown\settings.json` 已落库）；截图 `test\visual\live-fix\`。**没有给 93183 发消息**，留给用户自己发。
- **装机**：`.\tools\install-dev.ps1` 编译替换并重启，PID 1880（11:09），FileVersion 3.3.0.0（版本号仍冻结，未打包、未改一图说明书）。
- **注意**：`dist\下班倒计时-v3.3.0.zip` 是 10:02 打的，里面还是带上面 3 个 bug 的 exe；用户下次说「发版」时要 +1 版本号、重新 `package.ps1` 并重出一图说明书。

### 2026-11-13 10:00 · 发版 v3.3.0（版本号 +1 / 打包 / 一图说明书）

用户原话：「打包更新版本号，以及说明书」。按发版流程把 3.2.0 之后攒下的改动一次发出去（**小纸条** + 小纸条面板放大 + 3.2 的气泡边线 / 无投影 / 退场 + 取消工资双击固定）。

- **版本号 3.2.0 → 3.3.0**：`src/Models.cs`（`AppInfo.Version`）、`src/AssemblyInfo.cs`（AssemblyVersion / FileVersion）、`package.ps1` 默认 `-Version`、`README.md` 当前版本、`design/manual-poster.html` 头部 + 页脚，全部改成 3.3.0 / 2026-11-13。日常约定不变：下次说「发版」再 +1。
- **打包**：`.\package.ps1` → `dist\下班倒计时-v3.3.0.zip`（1,627 KB）。包内 7 个文件 + `fonts\`：`WorkCountdown.exe`（366,592 B / FileVersion 3.3.0.0）、`一图说明书.png`（2400×6485）、`使用说明.txt`、`install.ps1` / `uninstall.ps1`、`安装.cmd` / `卸载.cmd`。`使用说明.txt` 补了「小纸条」一节、设置页数改七页、删掉早已不存在的「双击固定」。
- **一图说明书**：`design/manual-poster.html` 新增第 03 节「小纸条」（A 抽屉 / C 双栏两张实机图、本机 ID / 加联系人 / 8 条历史 / 边框闪光 / 尺寸字号 / 中继说明），设置节 六页 → **七页**（补第 7 页「纸条」），界面节工具栏补 `✈`、已知边界补小纸条中继；重出 **2400×6485** PNG（上一版 2400×5070）。顺手新建 `design/render-poster.ps1`：Chrome 无头 1.5x 截图 + 自动裁底部空白，以后不用再手敲命令、手动量高度。
- **验证**：`.\test\build-tests.ps1` → **Test 207 / Check 226 全绿**；`dist\pack\install.ps1` 实跑安装 → FileVersion 3.3.0.0、exe 哈希与 `dist\WorkCountdown.exe` 一致、桌面快捷方式已更新；真机 PostMessage 点 ✈ → 小纸条面板 588×423 正常出图（`test/visual/release-330/`）；卡片右上角五颗图标 `✈ ¥ ⚙ ↻ ›` 都在。
- **装机**：`.\tools\install-dev.ps1 -NoBuild` 重启后 PID 32952（10:02），FileVersion 3.3.0.0。
- 未提交 git（HEAD 仍 `fd48196`，工作区未提交是现状）；`.handoff/` 归档见 HANDOFF。

### 2026-11-12 20:52 · 小纸条面板放大（聊天字太小 → 尺寸 / 字号按 DIP 走）

用户反馈：小纸条面板里聊天文字太小看不清，要适当放大面板和字号。

- **根因**（`src/NotePanelForm.cs`）：`u = card.Width / 420f` 把 96 DPI 设计稿（392×282 DIP）当成「卡片 420 宽」再缩到实际卡片 280 DIP，整体缩到 2/3；字号 `Pt(px)` 又除以 DPI，正文只剩 **6.25pt**（150% 下 12.5px），比卡片正文 9.5pt 小一半，所以看不清。
- **改法**：`u = scale`，面板尺寸和字号都按 DIP 走、和卡片（280 DIP 宽）同一个坐标系。150% 下：抽屉式 **588×423 物理像素**（392×282 DIP，历史态 476 DIP = 714px），双栏 **705×447**（历史态 512 DIP = 768px）；聊天正文 9.375pt（18.75px）、标题 10.5pt、时间/说明 7.1–8.6pt，和卡片正文 9.5pt 同级。
- **两个健壮性修正**（面板变宽后小屏会碰到）：① 左右空间都不够时不再固定开右边，改成「右边够→右边，否则左边够→左边，都不够→选空间大的一侧」再 clamp 回屏内；② 长高到历史态会顶出屏幕底边时，新增 `TopForHeight()` 把窗口整体上移，历史 8 条不会被裁。
- **测试**：`.\test\build-tests.ps1` → **Test 207 / Check 226 全绿**。Check 新增 2 条（面板尺寸只跟 DPI 走、聊天正文 ≥ 9pt），原「按卡片比例」两条改成 DIP 断言。
- **出图 / 真机**：`NoteShot.exe` 重出 A/C 四状态（588×423 / 705×768）；装机的 exe 上 PostMessage 点 ✈ → 面板窗口实测 **588×423**（改前 392×282），PrintWindow 抓图文字清晰（`test/visual/after-note/live-*.png`）。
- **文档 / 图**：README「小纸条」补面板尺寸 / 字号说明、断言数改 226；`note-drawer.png` / `note-columns.png` / `note-flash.png` 按新尺寸重出。
- **装机**：`.\tools\install-dev.ps1` 已替换并重启，FileVersion 3.2.0.0，PID 4504（20:52）；未升版本、未打包、未改一图说明书。

### 2026-11-12 · 开发流程调整（不算产品改动）

- 新增 `tools/install-dev.ps1`：编译 → 停掉旧进程 → 覆盖 `%LOCALAPPDATA%\Programs\WorkCountdown\WorkCountdown.exe` → 重启，一条命令完成日常装机；日常不再跑 `package.ps1`。
- 新增本文件 `CHANGELOG.md`；`HANDOFF.md` 增加「工作约定」一节。
- 验证：`tools\install-dev.ps1` 跑通，编译 → 覆盖 exe → 重启，FileVersion 3.2.0.0、PID 15248 正常起来（11:02）。

### 2026-11-12 20:30 · 小纸条（会话功能）实现完成（版本仍 v3.2.0，未发版）

用户确认后按「A/C 两套 + 设置里切换 + 贴边闪光 + 公共中继 + 8 条消息」实现：

- 新增 `src/NoteRelay.cs`：手写 MQTT 3.1.1（TCP，不引第三方库），默认 `broker.hivemq.com:1883`；按 5 位 ID 订阅收件箱 `xzt/v1/msg/<id>`、retained 在线状态 `xzt/v1/presence/<id>`（按 ID 探在线）、15s 心跳、断线 10s 重连。
- 新增 `src/NotePanelForm.cs`：分层窗小纸条面板，**A 抽屉式 / C 极简双栏**两种布局；联系人胶囊（未读徽标）、hover/点选 → 最后一条 + 输入框、点输入框长高展开 **8 条历史**、加联系人页（5 位 ID + 查在线 + 备注）、行内编辑器回车发送；打开/高度/逐行淡入/收起动画；尺寸与字号按卡片宽度等比（面板高 = 卡片高 282 设计像素）。
- `src/Models.cs`：`NoteSettings / NoteContact / NoteMessage / NoteThread`；本机 5 位 ID 生成与校验、每对 ID 只留 **8 条**、未读计数、备注 ≤8 字、会话缺联系人自动补（显示 ID）、`Settings.Normalize` 迁移老配置。
- `src/Widget.cs`：卡片右上角第 5 颗图标 **✈（¥ 左边）** + 未读点；**贴边闪光**（沿卡片圆角边框 1.5px 亮边 + 外发光，1.3s 两次脉冲；未读每 5s 呼吸）；打开/关闭面板；收到消息落库、置未读、闪边框、自动加陌生人联系人；`GlyphRect` 改成动画途中也能点中图标。
- `src/SettingsForm.cs`：第 7 页「纸条」（样式 A/C、本机 ID 复制/重新生成、公共中继地址+端口+测试连接、联系人增删改）；页签宽度 84→64。
- 测试：**Test 207 / Check 224 全绿**；新增 `test/NoteShot.cs`（A/C 四状态 + 闪光出图）、`test/RelayProbe.cs`（两 ID 互发）、`test/NoteSend.cs`（给装机实例发一条）。
- 真机验证：公共 MQTT 两个 ID 互发成功；给装机实例发纸条 → 收到、卡片边框闪光、面板未读徽标/已读清零、A 抽屉面板可打开并查看消息；设置 → 纸条 页离屏渲染正常；测试产生的临时会话已从用户配置里清掉。
- 未打包 zip、未改一图说明书、未升版本号；README 已补「小纸条」章节。
### 2026-11-12 19:20 · 小纸条设计稿 v2（按用户确认改，仍未改产品代码）

用户确认：**A 抽屉式 + C 极简双栏都做、在设置里切换**；**闪光贴着圆角边框**；**公共中继**；**8 轮 = 一共 8 条消息**。

- `design/note/note.html`：A/C 两套都保留（B 便签纸标为备选）；历史从「8 轮 = 16 条」改为 **8 条消息**；闪光从角落圆点改成 **贴边亮边**，并做了 3 种贴边样式（①整圈亮边 / ②边框扫光 / ③四角弧光，默认①），顶部可切。
- 新增 `design/note/settings-note.html`：设置第 7 个页签「纸条」设计稿（样式 A/C、本机 ID、公共中继、联系人）。
- 新增对比图：`shots/sheet-a.png`、`sheet-c.png`、`shots/flash-options.png`、`shots/settings-note.png`；`render-note.ps1` 同步支持闪光三样式与设置页出图。
- 中继已验证：本机实测 `broker.hivemq.com:1883`、`test.mosquitto.org:1883`、`broker.emqx.io:1883` 均可连（MQTT CONNACK `00 00`），默认用 hivemq 公共 broker，设置里可改。
- `design/note/NOTES.md` 重写：决策记录、A/C 对照、贴边闪光时序、公共 MQTT 协议（主题 / presence / 离线 / 隐私）、代码落点。
- 状态：未改 `src/`、未编译、未装机，版本仍 `v3.2.0`；下一步开始写代码。

### 2026-11-12 · 小纸条（会话功能）· 设计稿 v1（未改产品代码）

- 新增 `design/note/note.html`：三版可交互设计稿（**A 抽屉式 / B 便签纸 / C 极简双栏**），六个状态（入口 / 未读闪光 / 会话选择 / 最后一轮 + 输入框 / 展开 8 轮历史 / 加联系人），打开就能点、能 hover、能发消息、能加联系人，动画是真实 CSS 过渡。
- 新增 `design/note/NOTES.md`：需求 → 设计落点对照、三版取舍、尺寸与时长、**待确认**（传输方案 / 8 轮定义 / ID 规则 / 离线 / 隐私）、实现时的代码落点。
- 新增 `design/note/render-note.ps1`（Chrome 无头出状态图）与 `design/note/shots/`（`v-{a,b,c}-{state}.png` + 三张对比长图 `sheet-*.png` + 闪光 `flash.png`）。
- 状态：**设计稿待用户确认**；未改 `src/`、未重新编译、未装机，版本仍 `v3.2.0`。

### 2026-11-12 · v3.2.0 产品改动（已随 v3.2.0 装机）

- **取消工资卡片双击固定**：删除 `UiSettings.salaryPinned`、`Widget.salaryPinned` / `salaryClickHandled` / `salaryPinBeforeClick` / `OnDoubleClick` / `ToggleSalaryPin` / `SetSalaryPinned`；`¥` 只剩单击展开 / 收起，面板右边改「单击收起」，启动不再恢复展开态，老配置里的 `salaryPinned` 被忽略。（`src/Widget.cs`、`src/Models.cs`）
- **右键胶囊菜单加主题色细边框**：四颗胶囊 + 长开卡片统一 `accent` 1 DIP 细线，常态 / 悬停 / 已置顶 / 长开透明度 0.46 / 0.58 / 0.66 / 0.72。（`src/BubbleMenuForm.cs`）
- **去掉气泡投影**：删除 `DrawShadow` 与投影位图缓存，`ShadowPadDip=20` 收紧为 `PadDip=14`（留 12 DIP 滑入余量）。（`src/BubbleMenuForm.cs`）
- **气泡消失动画**：`ExitMs=180` 淡出 + 以贴卡片那条边为锚点缩回，关窗后再执行点击动作；`IsClosing` 让淡出期间的右键不再被吞。（`src/BubbleMenuForm.cs`、`src/Widget.cs`）
- 测试 / 出图：Test 191、Check 210 全绿；新增 `test/BubbleExitShot.cs`；`README.md`、`HANDOFF.md` 已同步；一图说明书与 zip 为发版时产物（本次已按旧流程出过 v3.2.0）。
