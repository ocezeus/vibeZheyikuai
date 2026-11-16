# 五套主题「桌面场景」出图 + 拼成一张对比图（发给用户看的那张）
#   .\design\theme-packs\compose.ps1                 # 默认 1.25x，输出 design\theme-packs\shots\
#   .\design\theme-packs\compose.ps1 -Scale 1
param([double]$Scale = 1.25)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
$html = Join-Path $PSScriptRoot 'index.html'
$outDir = Join-Path $PSScriptRoot 'shots'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$themes = @(
    @{ key = 'quiet';      label = '01  无垠 Quiet' },
    @{ key = 'mica';       label = '02  云母 Mica' },
    @{ key = 'paper';      label = '03  宣纸 Ink' },
    @{ key = 'instrument'; label = '04  仪表 Instrument' },
    @{ key = 'neon';       label = '05  霓虹 Neon' }
)

# 1) 只截桌面场景（1500×840 CSS，隐藏页面外壳）
foreach ($t in $themes) {
    $ud = Join-Path $env:TEMP ('wc-tp-' + [guid]::NewGuid().ToString('N'))
    $png = Join-Path $outDir ("stage-" + $t.key + ".png")
    $url = 'file:///' + ($html -replace '\\', '/') + '?only=stage#' + $t.key
    $args = @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars',
        "--force-device-scale-factor=$Scale",
        '--window-size=1502,842',
        '--virtual-time-budget=4000',
        '--default-background-color=00000000',
        "--user-data-dir=$ud",
        "--screenshot=$png",
        $url
    )
    $p = Start-Process -FilePath $chrome -ArgumentList $args -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0 -or -not (Test-Path $png)) { throw "Chrome 出图失败：$($t.key)（exit $($p.ExitCode)）" }
    Remove-Item $ud -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host ("stage-{0}.png  {1:N0} KB" -f $t.key, ((Get-Item $png).Length / 1KB))
}

# 2) 拼成 2 列 × 3 行 的对比图
$cardW = 616
$cardH = [int]($cardW * 840 / 1500)
$padX = 24; $padY = 22; $gap = 18; $labelH = 34
$W = $padX * 2 + $cardW * 2 + $gap
$H = $padY * 2 + ($cardH + $labelH) * 3 + $gap * 2

$canvas = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.SmoothingMode = 'AntiAlias'
$g.InterpolationMode = 'HighQualityBicubic'
$g.PixelOffsetMode = 'HighQuality'
$g.Clear([System.Drawing.Color]::FromArgb(255, 8, 10, 14))
$font = New-Object System.Drawing.Font('Microsoft YaHei UI', 13.5, [System.Drawing.FontStyle]::Bold)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 226, 231, 240))
$subFont = New-Object System.Drawing.Font('Consolas', 9.5)
$subBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 110, 119, 133))

for ($i = 0; $i -lt $themes.Count; $i++) {
    $col = $i % 2
    $row = [Math]::Floor($i / 2)
    $x = $padX + $col * ($cardW + $gap)
    $y = $padY + $row * ($cardH + $labelH + $gap)
    $src = [System.Drawing.Bitmap]::FromFile((Join-Path $outDir ("stage-" + $themes[$i].key + ".png")))
    $dy = $y + $labelH
    $dst = New-Object System.Drawing.Rectangle -ArgumentList @([int]$x, [int]$dy, [int]$cardW, [int]$cardH)
    $g.DrawImage($src, $dst)
    $src.Dispose()
    $g.DrawString($themes[$i].label, $font, $brush, [float]$x, [float]($y + 6))
    $tx = $x + $cardW - 132
    # （右侧比例标签去掉：会压到下一格标题；比例写在 README 里）
}
# 3) 最后一格放"怎么挑"图例（5 套 + 1 格说明正好填满 2×3）
$lx = $padX + ($cardW + $gap)
$ly = $padY + 2 * ($cardH + $labelH + $gap)
$g.DrawString('怎么挑', $font, $brush, [float]$lx, [float]($ly + 6))
$legend = New-Object System.Drawing.Font('Microsoft YaHei UI', 11)
$legendB = New-Object System.Drawing.Font('Microsoft YaHei UI', 11, [System.Drawing.FontStyle]::Bold)
$muBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 150, 160, 175))
$lines = @(
    '壁纸：浅色 → 宣纸；照片 / 深色 → 云母、霓虹；纯黑 → 无垠、仪表',
    '气质：几乎看不见 → 无垠；有质感 → 云母；有个性 → 仪表、霓虹',
    '白天用得多、旁边就是飞书 → 宣纸',
    '',
    '共同结构：设置窗口改成「左栏导航 + 内容卡片」；',
    '卡片 / 小纸条 / 右键胶囊的布局和功能与 v3.6 一致，',
    '五套换的只是皮肤（颜色 / 字体 / 圆角 / 质感 / 进度条写法）。',
    '',
    '挑中编号后：按皮肤实现到 src/（GDI+ 自绘），跑两套测试 + 装机；',
    '其它四套先不实现。'
)
$yy = $ly + 40
foreach ($ln in $lines) {
    $g.DrawString($ln, $legend, $muBrush, [float]$lx, [float]$yy)
    $yy += 21
}
$g.Dispose()
$outPng = Join-Path $outDir 'theme-compare.png'
$canvas.Save($outPng, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Write-Host ("compare -> {0}  {1}x{2}  {3:N0} KB" -f $outPng, $W, $H, ((Get-Item $outPng).Length / 1KB))
