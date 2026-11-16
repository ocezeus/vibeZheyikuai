param(
    [switch]$Run,
    [switch]$Shortcut
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$assets = Join-Path $root 'assets'

New-Item -ItemType Directory -Force -Path $dist | Out-Null
New-Item -ItemType Directory -Force -Path $assets | Out-Null

# ---------- 生成图标 ----------
$iconPath = Join-Path $assets 'app.ico'
function New-AppIcon([string]$path) {
    Add-Type -AssemblyName System.Drawing
    $size = 256
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $radius = 56
    $d = $radius * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc(0, 0, $d, $d, 180, 90)
    $p.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $p.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
    $p.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $p.CloseFigure()

    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255,26,32,46), [System.Drawing.Color]::FromArgb(255,38,46,64), 90.0)
    $g.FillPath($bg, $p)

    $ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255,76,141,255), 14.0)
    $g.DrawEllipse($ringPen, 52, 52, 152, 152)

    $trackPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120,255,255,255), 8.0)
    $g.DrawLine($trackPen, 128, 70, 128, 60)

    $handPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255,236,241,250), 12.0)
    $handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($handPen, 128, 128, 128, 82)
    $g.DrawLine($handPen, 128, 128, 166, 146)

    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,76,141,255))
    $g.FillEllipse($dotBrush, 116, 116, 24, 24)

    $g.Dispose()

    $png = Join-Path $env:TEMP ('wc-icon-' + [guid]::NewGuid().ToString('N') + '.png')
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bytes = [System.IO.File]::ReadAllBytes($png)
    Remove-Item $png -Force

    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0)      # reserved
    $bw.Write([uint16]1)      # type = icon
    $bw.Write([uint16]1)      # count
    $bw.Write([byte]0)        # width 256
    $bw.Write([byte]0)        # height 256
    $bw.Write([byte]0)        # colors
    $bw.Write([byte]0)        # reserved
    $bw.Write([uint16]1)      # planes
    $bw.Write([uint16]32)     # bpp
    $bw.Write([uint32]$bytes.Length)
    $bw.Write([uint32]22)     # offset
    $bw.Write($bytes)
    $bw.Flush(); $bw.Close()
}

New-AppIcon $iconPath
Write-Host "icon  -> $iconPath"

# ---------- 生成 DPI 感知清单 ----------
$manifestPath = Join-Path $assets 'app.manifest'
$manifest = @'
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="WorkCountdown.app"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">system</dpiAwareness>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
'@
[System.IO.File]::WriteAllText($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "mani  -> $manifestPath"

# ---------- 编译 ----------
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "找不到 .NET Framework 编译器：$csc" }

$exe = Join-Path $dist 'WorkCountdown.exe'
# 生产编译需要显式清单（顺序可读、打包审计也看它）。清单和 src\*.cs 不一致时直接失败，
# 避免新增源码只在 build-tests 的 glob 里编过、正式构建却漏掉。
$sources = @(
    (Join-Path $src 'Program.cs'),
    (Join-Path $src 'AssemblyInfo.cs'),
    (Join-Path $src 'Models.cs'),
    (Join-Path $src 'SettingsStore.cs'),
    (Join-Path $src 'SettingsApplyPlan.cs'),
    (Join-Path $src 'ReminderEngine.cs'),
    (Join-Path $src 'SyncEngine.cs'),
    (Join-Path $src 'SyncAdapters.cs'),
    (Join-Path $src 'Skins.cs'),
    (Join-Path $src 'Frost.cs'),
    (Join-Path $src 'Updates.cs'),
    (Join-Path $src 'UpdateChangelog.cs'),
    (Join-Path $src 'Lark.cs'),
    (Join-Path $src 'Ics.cs'),
    (Join-Path $src 'OutlookGraph.cs'),
    (Join-Path $src 'Widget.cs'),
    (Join-Path $src 'ScreenReminderForm.cs'),
    (Join-Path $src 'DrawerForm.cs'),
    (Join-Path $src 'BubbleMenuForm.cs'),
    (Join-Path $src 'NotePanelForm.cs'),
    (Join-Path $src 'NoteRelay.cs'),
    (Join-Path $src 'CardGlowForm.cs'),
    (Join-Path $src 'SettingsForm.cs'),
    (Join-Path $src 'FirstRunForm.cs')
)

$listed = @($sources | ForEach-Object { Split-Path -Leaf $_ })
$unlisted = @(Get-ChildItem -Path (Join-Path $src '*.cs') | Where-Object { $listed -notcontains $_.Name })
if ($unlisted.Count -gt 0) {
    $names = ($unlisted | ForEach-Object { $_.Name }) -join ', '
    throw ('build.ps1 的源码清单漏了：' + $names + '（加进 $sources，或从 src 移除）')
}

$cscArgs = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    ('/win32icon:' + $iconPath),
    ('/win32manifest:' + $manifestPath),
    ('/out:' + $exe),
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:System.Security.dll'
) + $sources

# 正在运行的旧实例会锁住 exe，先按路径精确关掉它（只关这个 dist 里的）
Get-Process -Name WorkCountdown -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | ForEach-Object {
    Write-Host "close -> 正在运行的旧实例 (pid $($_.Id))"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 600

& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }
Write-Host "exe   -> $exe"

if ($Shortcut) {
    $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) '上班工具箱.lnk'
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($lnk)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = $dist
    $sc.IconLocation = "$exe,0"
    $sc.Description = '上班工具箱悬浮窗'
    $sc.Save()
    Write-Host "link  -> $lnk"
}

if ($Run) {
    Start-Process -FilePath $exe
    Write-Host "started"
}
