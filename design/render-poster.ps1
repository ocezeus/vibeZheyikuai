# 一图说明书出图：design\manual-poster.html（1600 CSS 宽）→ 根目录 manual-poster.png（2400 px 宽）。
#
#   .\design\render-poster.ps1
#
# 做法：Chrome 无头模式按 1.5x 设备缩放截 1600 CSS 宽的长图（2400 px），
# 再把底部超出内容的纯背景色裁掉、补回 56 CSS 的底部留白。
# 改了 manual-poster.html 的高度 / 加了章节后不用手动调：CssHeight 给一个足够大的值就行。
param(
    [int]$CssHeight = 8000
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path $chrome)) { throw "找不到 Chrome：$chrome" }

$html = Join-Path $root 'design\manual-poster.html'
$out = Join-Path $root 'manual-poster.png'
$raw = Join-Path $env:TEMP ('wc-poster-' + [guid]::NewGuid().ToString('N') + '.png')
$ud = Join-Path $env:TEMP ('wc-poster-profile-' + [guid]::NewGuid().ToString('N'))
$url = 'file:///' + ($html -replace '\\', '/')

$args = @(
    '--headless=new', '--disable-gpu', '--hide-scrollbars',
    '--force-device-scale-factor=1.5',
    "--window-size=1600,$CssHeight",
    '--virtual-time-budget=6000',
    "--user-data-dir=$ud",
    "--screenshot=$raw",
    $url
)
$p = Start-Process -FilePath $chrome -ArgumentList $args -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0 -or -not (Test-Path $raw)) { throw "Chrome 出图失败（exit $($p.ExitCode)）" }

Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile($raw)
$bg = [System.Drawing.Color]::FromArgb(255, 11, 15, 23)   # body 背景 #0b0f17
$w = $bmp.Width
$h = $bmp.Height
$xs = @([int]($w * 0.1), [int]($w * 0.3), [int]($w * 0.5), [int]($w * 0.7), [int]($w * 0.9))
$last = -1
for ($y = $h - 1; $y -ge 0; $y--) {
    $diff = $false
    foreach ($x in $xs) {
        if ($bmp.GetPixel($x, $y).ToArgb() -ne $bg.ToArgb()) { $diff = $true; break }
    }
    if ($diff) { $last = $y; break }
}
if ($last -lt 0) { throw '整张图都是背景色，检查 manual-poster.html 是不是没渲染出来' }
$targetH = [Math]::Min($h, $last + 1 + 84)                 # 84 px = 56 CSS × 1.5 底部留白
$outBmp = New-Object System.Drawing.Bitmap($w, $targetH)
$g = [System.Drawing.Graphics]::FromImage($outBmp)
$g.DrawImage($bmp, (New-Object System.Drawing.Rectangle(0, 0, $w, $targetH)),
    (New-Object System.Drawing.Rectangle(0, 0, $w, $targetH)), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$outBmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$outBmp.Dispose()
$bmp.Dispose()

Remove-Item $raw -Force -ErrorAction SilentlyContinue
Remove-Item $ud -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ("poster -> {0}  {1}x{2}  {3:N0} KB" -f $out, $w, $targetH, ((Get-Item $out).Length / 1KB))
