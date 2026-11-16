param(
[string]$Version = '3.8.0',
[switch]$NoPublish
)

# 打包成一份可以直接发给别人的压缩包：dist\上班工具箱-v1.0.zip
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$pack = Join-Path $dist 'pack'

& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw '编译失败' }

Remove-Item $pack -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $pack | Out-Null

Copy-Item (Join-Path $dist 'WorkCountdown.exe') (Join-Path $pack 'WorkCountdown.exe')
# 一图说明书一起放进压缩包
if (Test-Path (Join-Path $root 'manual-poster.png')) {
    Copy-Item (Join-Path $root 'manual-poster.png') (Join-Path $pack '一图说明书.png')
}

# 字体目录：把 PingFang.ttc 丢进来，程序启动会优先用它（不用装进系统）
$fontDir = Join-Path $pack "fonts"
New-Item -ItemType Directory -Force -Path $fontDir | Out-Null
Set-Content -Path (Join-Path $fontDir "放苹方字体到这里.txt") -Value @(
  "把 PingFang.ttc / PingFang.ttf 放到这个文件夹里，"
  "上班工具箱 启动时会优先用它显示中文，不需要安装到系统。"
  "没有这个文件就自动用系统里的 Noto Sans SC 或微软雅黑。"
) -Encoding UTF8

function Write-Text([string]$path, [string]$text, [switch]$Bom) {
    $enc = New-Object System.Text.UTF8Encoding($Bom.IsPresent)
    [System.IO.File]::WriteAllText($path, $text, $enc)
}

# ---------- 安装 / 卸载脚本 ----------
$install = @'
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$dst = Join-Path $env:LOCALAPPDATA 'Programs\WorkCountdown'

Write-Host "安装到：$dst"
Get-Process WorkCountdown -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item (Join-Path $src 'WorkCountdown.exe') (Join-Path $dst 'WorkCountdown.exe') -Force

if (Test-Path (Join-Path $src 'fonts')) { Copy-Item (Join-Path $src 'fonts') (Join-Path $dst 'fonts') -Recurse -Force }

$exe = Join-Path $dst 'WorkCountdown.exe'
$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) '上班工具箱.lnk'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $dst
$sc.IconLocation = "$exe,0"
$sc.Description = '上班工具箱悬浮窗'
$sc.Save()

Write-Host "桌面快捷方式：$lnk"
Start-Process -FilePath $exe
Write-Host ''
Write-Host '装好了。第一次打开会让你填上班时间和午休，之后右键卡片能加日程、换主题、开关置顶。'
'@
Write-Text (Join-Path $pack 'install.ps1') $install -Bom

$uninstall = @'
$ErrorActionPreference = 'Stop'
$dst = Join-Path $env:LOCALAPPDATA 'Programs\WorkCountdown'

Get-Process WorkCountdown -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) '上班工具箱.lnk'
if (Test-Path $lnk) { Remove-Item $lnk -Force; Write-Host "已删除快捷方式" }

try {
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Get-ItemProperty -Path $run -Name WorkCountdown -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $run -Name WorkCountdown -Force
        Write-Host '已取消开机自启'
    }
} catch { }

if (Test-Path $dst) { Remove-Item $dst -Recurse -Force; Write-Host "已删除程序目录：$dst" }

$cfg = Join-Path $env:APPDATA 'WorkCountdown'
if (Test-Path $cfg) {
    Write-Host ''
    Write-Host "配置和日志还留着：$cfg"
    Write-Host '不想要了就手动删掉这个文件夹。'
}
'@
Write-Text (Join-Path $pack 'uninstall.ps1') $uninstall -Bom

# cmd 外壳保持纯 ASCII，避免代码页问题
$cmdInstall = @'
@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 echo.
echo Press any key to close...
pause >nul
'@
Write-Text (Join-Path $pack '安装.cmd') ($cmdInstall -replace "`r?`n", "`r`n")

$cmdUninstall = @'
@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
if errorlevel 1 echo.
echo Press any key to close...
pause >nul
'@
Write-Text (Join-Path $pack '卸载.cmd') ($cmdUninstall -replace "`r?`n", "`r`n")

# ---------- 使用说明 ----------
$readme = @"
上班工具箱 $Version —— 桌面悬浮窗，显示距离下班还有多久

安装
  1. 双击「安装.cmd」
  2. 程序会装到 %LOCALAPPDATA%\Programs\WorkCountdown，并在桌面放一个快捷方式
  3. 首次打开会问你：上班时间 / 弹性工时 / 午休 —— 填完点「开始用」

不用装运行时
  只需要 Windows 自带的 .NET Framework 4.x（Win10 / Win11 都有）。

日常用
  - 双击桌面图标启动；卡片默认贴在屏幕右边，只露一条倒计时
  - 窗口默认始终置顶（设置里的「窗口置顶」可以关）；万一被系统或别的程序压下去，2 秒内会自动补回最上层
  - 单击收起的小横条展开成卡片，右上角箭头收回；20 秒没动会自动收回
  - 换整套外观：设置 →「外观」→「界面风格」六选一（默认 / 无垠 / 云母 / 宣纸 / 仪表 / 霓虹），点一下就预览、点「保存」后悬浮窗立即换。默认皮肤保留 39 套主题 + 背景风格（纯色 / 磨砂玻璃），其它五套自带固定配色、39 套色卡会置灰
  - 卡片背面有一圈跟主题强调色走的呼吸柔光 + 一道沿边缘跑的流光；减少动效/高对比度时自动关掉
  - 右键卡片 = 六颗胶囊：新增日程 / 修改主题 / 触发上下班 / 窗口置顶 / 预览通知 / 检查更新；点开的那颗原地长成一张大卡片、下面胶囊被顶下去，颜色跟主题走；新日程的标题和地点可以直接改。托盘右键才是原来的长菜单（设置 / 提醒预览 / 开机自启 / 退出）
  - 「预览通知」：选上班 / 午休 / 飞书会议 / Outlook / 普通日程 / 下班提醒，立刻在卡片上真播一遍那类提醒的动画，只放一遍，不改日程
  - 「检查更新」：展开自动查 GitHub 最新版本，有新版本可以全量更新（下载新 exe → 校验 → 替换 → 自动重启，配置和对话都保留）；已是最新会直接告诉你
  - 固定日程超过三条时列表右边会出现滚动条，滚轮或拖滑块都行；新建的排在最后并自动滚过去
  - 中文用苹方：系统里装了「苹方 常规 / 苹方 粗体」就直接用（粗体也是真粗体）；没有就用 Noto Sans SC，或把 PingFang.ttc 放进安装目录的 fonts\
  - 每条固定日程能设重复（每天 / 单数周 / 双数周）、重复到哪天、配色（从主题色里挑）、到点提醒与「结束时也提醒」
- 整帧按像素 alpha 合成：圆角、字形、图标边缘都是抗锯齿的（小字走灰阶抗锯齿，不再镶 ClearType 彩边）
  - 到点会弹提醒动画；会议和日程跟随当前主题色，固定日程用这条自己选的配色。提醒可只在卡片内或全屏扩散，扩散样式可选圆形柔光 / 进度条拉伸 / 警戒带（横贯全屏的黄黑长条，斜纹滚动、背景淡入淡出），卡片背景可选角落光/进度条光（默认原样式，不额外加光）；外观页还能把主题背景切成「磨砂玻璃」（半透明 + 磨砂颗粒，桌面从卡片后面透出来，小纸条面板和胶囊气泡一起变）
  - 警戒带会先让卡片展开，再横贯全屏扫过；警戒带期间卡片不铺色块。黄条走完 0.3 秒，从卡片贴边的那一侧反向拉出一张抽屉卡（实心工业黄、和卡片同高），写着标题、时间段、地点和剩余时间，一直留到日程结束，点一下收回
  - 提醒配色在「固定日程」页逐条选：第一项「跟随当前主题」，后面就是 39 套主题本身，不新增色板；左边色带就是这套主题折算出来的两层色
  - 卡片第二行跟着一天走：距午休 → 午休中 → 距晚休 → 晚休中 → 马上下班
  - 所有提醒一响就常驻，直到这个时段过完：会议撑到日程结束、休息撑到休息结束、下班撑到打卡
  - 提醒卡片右上角有 ×，点一下立刻收掉；整张卡片上随便点哪儿也会收起（托盘菜单也有「关闭当前提醒」），关掉的那一场今天不再弹
  - 到下班点还没打卡，会常驻「注意 / 该下班啦」，每 5 分钟重播一次，飞书同步到打卡就自动消失
- 加日程：日历选日期 + 15 分钟一档时间 + 选时长 + 位置；懒得拨就点「快速填入」：3 / 5 / 15 / 30 分钟后、1 小时后
- 进度条走过的一段带发光，上面还有一道流动的光带（收起的小卡片也有）；光带两头淡入淡出，走到尽头是渐渐变淡，不会突然消失；距离下班不足 30 分钟时整条变成彩色（绿→黄→橙→粉），午休那一行用粗体
- 剩下不到一小时自动换计时方式：分:秒.厘秒（50ms 跳一次）；一小时以上仍是 时:分:秒
- 提醒卡上不带任何氛围特效（光晕 / 粒子 / 极光 / 扫光都去掉了），只有两张卡片叠上来的动作；提醒停留 3–20 秒可调
- 提醒卡的颜色：会议、日程跟着当前主题走；午休、晚休、下班等固定日程用各自选的配色
- 展开卡片时先出时间和进度条这两样主视觉，其余文字行错峰一行行浮上来（每行隔 55ms，同一行里的元素一起出现）
- 提醒入场：上面的色块先落下来，下面的再顶上来（两张卡片叠上去的感觉），文字最后浮出来；退场反过来
  - 提醒文字独立淡入淡出，色块先后进入，不裁切字形；悬停可查看最近提醒全文
- 卡片每行间距是均等的（6 DIP 一个行距走到底），小字用 9–9.5pt 灰阶抗锯齿绘制，150% 缩放下不发虚
  - 设置分七页：考勤（班次/工作日/节假日；工作日按勾选的周一到周日算，调休用节假日栏覆盖）、固定（午休/晚休/下班 + 自建）、提醒（飞书考勤与日程）、外观（界面风格 6 套 + 39 套配色 + 自己调主题）、日程（四类通知样式 + 多份 Outlook 日历订阅）、工资（月薪 / 发薪日）、纸条（小纸条样式 A/C + 本机 ID + 昵称 + 公共中继 + 对话 / 对话码）
  - 一图说明书：压缩包里的「一图说明书.png」，所有界面、功能和设置项都在一张图里
  - 日程页可以订阅多份日历：每份单独启停、单独设提前量；一份拉失败不影响其它；Outlook 在会开始后才同步下来也会补一次「正在进行」提醒
  - 工资页填月工资和发薪日后，卡片右上角出现 ¥：单击展开 / 收起；今日收入按打卡时间实时累计、保留 3 位小数，本月收入 = 日薪 × 已完成工作日 + 今日收入
  - 小纸条（对话）：卡片右上角 ¥ 左边的小飞机 ✈ 打开面板。本机第一次启动生成 5 位 ID（设置 → 纸条 可复制 / 重新生成）。点「＋ 新建对话」：填 5 位数字 = 和这个 ID 的人单独聊（自动查对方在不在线）；填任意其它 5 位字符 = 对话码，别人输入同一个码就进同一个对话。点对话看最后一条 + 输入框，点输入框长高看历史（一屏 8 条，每个对话最多保留 16 条，更早的用鼠标滚轮往上翻），回车发送。自己的「昵称」可以直接在卡片页「＋ 新建对话」旁边那颗昵称胶囊上双击改（也能在 设置 → 纸条 里填）：对方添加你的对话会直接显示昵称，对话码群聊里也直接显示发送者昵称（没昵称才显示 ID）；每条消息都保留发送者 ID，看到就能按 ID 建一对一。对话多了：抽屉式列表页一屏铺两排（最多 8 个）、也能用鼠标滚轮翻；右键某个对话 → 红色「删除」，连本机这段 16 条一起删。面板有两套样式：抽屉式 / 极简双栏，在 设置 → 纸条 里切；收到纸条时卡片圆角边框连闪 2 次，没读时每 5 秒轻闪。消息走公共 MQTT 中继（默认 broker.hivemq.com:1883，可在设置里改），明文转发、不存离线消息；对话码没有密码，知道码的人都能进，别发密码等敏感内容

接飞书打卡（可选）
  不接也能用，只是按班次时间倒计时。
  想读飞书里的真实打卡：
    1. 装 Node.js，然后 npm i -g lark-cli
    2. 在命令行执行 lark-cli auth login 完成授权
    3. 托盘右键 → 设置 → 飞书对话 → 点「自动找」，找到你和考勤机器人的会话
  找不到就看看命令行里 lark-cli 有没有报错。

卸载
  双击「卸载.cmd」：删程序、删快捷方式、关掉开机自启。
  配置和日志在 %APPDATA%\WorkCountdown，不想要了自己删。

便携版
  把 WorkCountdown.exe 拷到任意文件夹，在同目录建一个空文件 portable.txt，
  配置就会存到同目录的 data 文件夹里（U 盘、无权限的电脑都能用）。

出问题
  看 %APPDATA%\WorkCountdown\widget.log。
"@
Write-Text (Join-Path $pack '使用说明.txt') ($readme -replace "`r?`n", "`r`n") -Bom

# ---------- 压缩 ----------
$zip = Join-Path $dist ("上班工具箱-v$Version.zip")
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($pack, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host ''
Write-Host "pack  -> $pack"
Write-Host "zip   -> $zip  ($([math]::Round((Get-Item $zip).Length / 1KB, 1)) KB)"
Get-ChildItem $pack | ForEach-Object { Write-Host ("  " + $_.Name + "  " + $_.Length + " B") }

# ---------- 发布到 GitHub（tools\release.ps1：commit -> push -> Release + 上传 zip） ----------
# 只想打包不发布：.\package.ps1 -NoPublish
if ($NoPublish) {
    Write-Host ''
    Write-Host '-NoPublish：只打包，跳过 GitHub 发布'
} else {
    $releaseScript = Join-Path $root 'tools\release.ps1'
    if (-not (Test-Path -LiteralPath $releaseScript)) { throw "找不到发布脚本：$releaseScript" }
    Write-Host ''
    Write-Host '---------- 发布到 GitHub ----------'
    # 子进程跑发布脚本：失败时退出码直接透传，不会污染 package.ps1 自己的流程
    # 子进程可能把 git 的 warning / 进度写到 stderr；Stop 下会被当成异常，这里只认退出码
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $releaseScript -Version $Version
        $releaseCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $oldEap }
    if ($releaseCode -ne 0) { throw "发布失败（exit $releaseCode）：$releaseScript" }
}
