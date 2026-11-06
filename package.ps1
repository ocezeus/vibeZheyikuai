param(
[string]$Version = '2.7.1'
)

# 打包成一份可以直接发给别人的压缩包：dist\下班倒计时-v1.0.zip
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$pack = Join-Path $dist 'pack'

& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw '编译失败' }

Remove-Item $pack -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $pack | Out-Null

Copy-Item (Join-Path $dist 'WorkCountdown.exe') (Join-Path $pack 'WorkCountdown.exe')

# 字体目录：把 PingFang.ttc 丢进来，程序启动会优先用它（不用装进系统）
$fontDir = Join-Path $pack "fonts"
New-Item -ItemType Directory -Force -Path $fontDir | Out-Null
Set-Content -Path (Join-Path $fontDir "放苹方字体到这里.txt") -Value @(
  "把 PingFang.ttc / PingFang.ttf 放到这个文件夹里，"
  "下班倒计时 启动时会优先用它显示中文，不需要安装到系统。"
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
$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) '下班倒计时.lnk'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $dst
$sc.IconLocation = "$exe,0"
$sc.Description = '下班倒计时悬浮窗'
$sc.Save()

Write-Host "桌面快捷方式：$lnk"
Start-Process -FilePath $exe
Write-Host ''
Write-Host '装好了。第一次打开会让你填上班时间和午休，之后右键卡片可以改。'
'@
Write-Text (Join-Path $pack 'install.ps1') $install -Bom

$uninstall = @'
$ErrorActionPreference = 'Stop'
$dst = Join-Path $env:LOCALAPPDATA 'Programs\WorkCountdown'

Get-Process WorkCountdown -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) '下班倒计时.lnk'
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
下班倒计时 $Version —— 桌面悬浮窗，显示距离下班还有多久

安装
  1. 双击「安装.cmd」
  2. 程序会装到 %LOCALAPPDATA%\Programs\WorkCountdown，并在桌面放一个快捷方式
  3. 首次打开会问你：上班时间 / 弹性工时 / 午休 —— 填完点「开始用」

不用装运行时
  只需要 Windows 自带的 .NET Framework 4.x（Win10 / Win11 都有）。

日常用
  - 双击桌面图标启动；卡片默认贴在屏幕右边，只露一条倒计时
  - 窗口默认始终置顶（右键「窗口置顶」可关）；万一被系统或别的程序压下去，2 秒内会自动补回最上层
  - 单击收起的小横条展开成卡片，右上角箭头收回；20 秒没动会自动收回
  - 右键卡片 = 菜单：设置 / 提醒预览 / 开机自启 / 退出
  - 固定日程超过三条时列表右边会出现滚动条，滚轮或拖滑块都行；新建的排在最后并自动滚过去
  - 中文用苹方：系统里装了「苹方 常规 / 苹方 粗体」就直接用（粗体也是真粗体）；没有就用 Noto Sans SC，或把 PingFang.ttc 放进安装目录的 fonts\
  - 每条固定日程能设重复（每天 / 单数周 / 双数周）、重复到哪天、配色（从主题色里挑）、到点提醒与「结束时也提醒」
- 整帧按像素 alpha 合成：圆角、字形、图标边缘都是抗锯齿的（小字走灰阶抗锯齿，不再镶 ClearType 彩边）
  - 到点会弹提醒动画；会议和日程跟随当前主题色，固定日程用这条自己选的配色。提醒可只在卡片内或全屏扩散，卡片背景有角落/进度条/内缘三种光效（可选）
  - 提醒配色在「固定日程」页逐条选：第一项「跟随当前主题」，后面就是 39 套主题本身，不新增色板；左边色带就是这套主题折算出来的两层色
  - 卡片第二行跟着一天走：距午休 → 午休中 → 距晚休 → 晚休中 → 马上下班
  - 所有提醒一响就常驻，直到这个时段过完：会议撑到日程结束、休息撑到休息结束、下班撑到打卡
  - 提醒卡片右上角有 ×，点一下立刻收掉；整张卡片上随便点哪儿也会收起（右键也有「关闭当前提醒」），关掉的那一场今天不再弹
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
  - 设置分四页：考勤（班次/工作日/节假日）、固定日程（午休/晚休/下班 + 自建）、提醒（飞书/日程，会议与日程提前量各自独立）、外观（39 套配色 + 自己调主题：背景色与进度条色分开选，已有自建主题可编辑）

接飞书打卡（可选）
  不接也能用，只是按班次时间倒计时。
  想读飞书里的真实打卡：
    1. 装 Node.js，然后 npm i -g lark-cli
    2. 在命令行执行 lark-cli auth login 完成授权
    3. 右键 → 设置 → 飞书对话 → 点「自动找」，找到你和考勤机器人的会话
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
$zip = Join-Path $dist ("下班倒计时-v$Version.zip")
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($pack, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host ''
Write-Host "pack  -> $pack"
Write-Host "zip   -> $zip  ($([math]::Round((Get-Item $zip).Length / 1KB, 1)) KB)"
Get-ChildItem $pack | ForEach-Object { Write-Host ("  " + $_.Name + "  " + $_.Length + " B") }