# 改动前 / 改动后对比图 + 数值对比（边框粗细、大卡片 5 图标包围盒）。
#   改动前：test\visual\verify-before（NoteShot.exe 旧编译 + v3.3.2 真机 shot.ps1）
#   改动后：test\visual\verify-after（NoteVerifyProbe 出图 + 真机 real-card-expanded.png）
#
#   .\test\verify\make-compare.ps1
#
# 输出（都写到 test\visual\verify-after\）：
#   compare-border-4x.png    左上角 64×64 放大 4 倍：改前 | 改后
#   compare-glyphs-3x.png    右上角图标行放大 3 倍：改前 / 改后
#   compare-metrics.txt      边框像素带行数 + 5 图标包围盒数值

param(
    [string]$Before,
    [string]$After
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$verifyDir = $PSScriptRoot
$testDir   = Split-Path -Parent $verifyDir
$root      = Split-Path -Parent $testDir
if (-not $Before) { $Before = Join-Path $root 'test\visual\verify-before' }
if (-not $After)  { $After  = Join-Path $root 'test\visual\verify-after' }
$shots = Join-Path $After 'shots'

function LoadBmp([string]$path) {
    if (-not (Test-Path $path)) { throw "缺图：$path" }
    return [System.Drawing.Bitmap]::FromFile($path)
}

# 和 NoteVerifyProbe.BorderRows 同一算法：x=宽/2，边缘往里数「与内部色差 > 12」的连续行
function BorderRows($bmp, [bool]$fromTop) {
    $x = [int]($bmp.Width / 2)
    $refY = if ($fromTop) { [Math]::Min($bmp.Height - 1, 10) } else { [Math]::Max(0, $bmp.Height - 11) }
    $inside = $bmp.GetPixel($x, $refY)
    $n = 0
    $lim = [Math]::Min(12, $bmp.Height)
    for ($i = 0; $i -lt $lim; $i++) {
        $y = if ($fromTop) { $i } else { $bmp.Height - 1 - $i }
        if ($y -lt 0 -or $y -ge $bmp.Height) { break }
        $c = $bmp.GetPixel($x, $y)
        $d = [Math]::Sqrt([Math]::Pow($c.R - $inside.R, 2) + [Math]::Pow($c.G - $inside.G, 2) + [Math]::Pow($c.B - $inside.B, 2))
        if ($d -gt 12) { $n++ } elseif ($n -gt 0) { break }
    }
    return $n
}

# 图标包围盒：与 NoteVerifyProbe.GlyphBox 同算法（和左边 3px 处背景比，色差 > 90）
function GlyphBox($bmp, [int]$x, [int]$y, [int]$w, [int]$h) {
    $x0 = [Math]::Max(0, $x); $y0 = [Math]::Max(0, $y)
    $x1 = [Math]::Min($bmp.Width, $x + $w); $y1 = [Math]::Min($bmp.Height, $y + $h)
    if ($x1 -le $x0 -or $y1 -le $y0) { return $null }
    $bg = $bmp.GetPixel([Math]::Max(0, $x0 - 3), [Math]::Min($bmp.Height - 1, $y0 + 2))
    $minX = [int]::MaxValue; $minY = [int]::MaxValue; $maxX = -1; $maxY = -1
    for ($yy = $y0; $yy -lt $y1; $yy++) {
        for ($xx = $x0; $xx -lt $x1; $xx++) {
            $c = $bmp.GetPixel($xx, $yy)
            $d = [Math]::Sqrt([Math]::Pow($c.R - $bg.R, 2) + [Math]::Pow($c.G - $bg.G, 2) + [Math]::Pow($c.B - $bg.B, 2))
            if ($d -gt 90) {
                if ($xx -lt $minX) { $minX = $xx }; if ($xx -gt $maxX) { $maxX = $xx }
                if ($yy -lt $minY) { $minY = $yy }; if ($yy -gt $maxY) { $maxY = $yy }
            }
        }
    }
    if ($maxX -lt 0) { return $null }
    return [pscustomobject]@{ X = $minX; Y = $minY; Width = $maxX - $minX + 1; Height = $maxY - $minY + 1 }
}

function GlyphSet($bmp) {
    $S = 1.5
    $dips = [ordered]@{ 'plane' = 146; '¥' = 118; 'gear' = 90; 'refresh' = 62; 'chevron' = 34 }
    $iconY = [int][Math]::Round((12 + (20 - 24) / 2) * $S)
    $size = [int][Math]::Round(24 * $S)
    $out = @()
    foreach ($k in $dips.Keys) {
        $x = [int]($bmp.Width - [Math]::Round($dips[$k] * $S))
        $b = GlyphBox $bmp $x $iconY $size $size
        $out += [pscustomobject]@{ Name = $k; X = $x; Box = $b }
    }
    return $out
}

function CropZoom($src, [System.Drawing.Rectangle]$r, [int]$zoom) {
    $r.Intersect([System.Drawing.Rectangle]::new(0, 0, $src.Width, $src.Height))
    $bmp = New-Object System.Drawing.Bitmap ([int]($r.Width * $zoom)), ([int]($r.Height * $zoom))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.DrawImage($src, [System.Drawing.Rectangle]::new(0, 0, $r.Width * $zoom, $r.Height * $zoom),
        $r, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $bmp
}

function SideBySide($left, $right, [string]$leftLabel, [string]$rightLabel, [string]$outPath) {
    $gap = 12
    $labelH = 22
    $w = $left.Width + $gap + $right.Width
    $h = [Math]::Max($left.Height, $right.Height) + $labelH
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(245, 245, 245))
    $g.DrawImageUnscaled($left, 0, $labelH)
    $g.DrawImageUnscaled($right, $left.Width + $gap, $labelH)
    $font = New-Object System.Drawing.Font('Segoe UI', 10)
    $br = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 40, 40))
    $g.DrawString($leftLabel, $font, $br, 2, 2)
    $g.DrawString($rightLabel, $font, $br, $left.Width + $gap + 2, 2)
    $g.Dispose(); $font.Dispose(); $br.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Stacked($top, $bottom, [string]$topLabel, [string]$bottomLabel, [string]$outPath) {
    $gap = 8
    $labelH = 20
    $w = [Math]::Max($top.Width, $bottom.Width)
    $h = $labelH + $top.Height + $labelH + $bottom.Height
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(245, 245, 245))
    $font = New-Object System.Drawing.Font('Segoe UI', 9)
    $br = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 40, 40))
    $g.DrawString($topLabel, $font, $br, 2, 2)
    $g.DrawImageUnscaled($top, 0, $labelH)
    $y2 = $labelH + $top.Height + 4
    $g.DrawString($bottomLabel, $font, $br, 2, $y2)
    $g.DrawImageUnscaled($bottom, 0, $y2 + $labelH)
    $g.Dispose(); $font.Dispose(); $br.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('改动前 / 改动后数值对比（150% DPI，scale=1.5）')
$lines.Add('生成时间：' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))
$lines.Add('')

# ---------- 边框 ----------
$bChat = LoadBmp (Join-Path $Before 'note-drawer\note-drawer-chat.png')
$aChat = LoadBmp (Join-Path $shots 'note-drawer-chat-border.png')
$bTop = BorderRows $bChat $true;  $bBot = BorderRows $bChat $false
$aTop = BorderRows $aChat $true;  $aBot = BorderRows $aChat $false
$bEdge = $bChat.GetPixel([int]($bChat.Width / 2), 0); $bIn = $bChat.GetPixel([int]($bChat.Width / 2), 10)
$aEdge = $aChat.GetPixel([int]($aChat.Width / 2), 0); $aIn = $aChat.GetPixel([int]($aChat.Width / 2), 10)
$lines.Add('[边框] x=宽/2 处像素带行数（色差>12）')
$lines.Add(('  改动前：顶 {0} 行 / 底 {1} 行；边框={2} 内部={3}' -f $bTop, $bBot, $bEdge, $bIn))
$lines.Add(('  改动后：顶 {0} 行 / 底 {1} 行；边框={2} 内部={3}' -f $aTop, $aBot, $aEdge, $aIn))
$borderVerdict = if ($aTop -ge 3 -and $aBot -ge 3) { 'PASS 变粗' } else { 'FAIL 没变粗' }
$lines.Add('  结论：' + $borderVerdict)
$lines.Add('')

# 边框放大对比图
$bl = CropZoom $bChat ([System.Drawing.Rectangle]::new(0, 0, 64, 64)) 4
$al = CropZoom $aChat ([System.Drawing.Rectangle]::new(0, 0, 64, 64)) 4
SideBySide $bl $al 'before (1 DIP, 2 rows)' 'after (1.8 DIP, 4 rows)' (Join-Path $After 'compare-border-4x.png')
$bl.Dispose(); $al.Dispose()

# ---------- 图标 ----------
$bCard = LoadBmp (Join-Path $Before 'card-expanded.png')
$afterCardPath = Join-Path $After 'real-card-expanded.png'
if (-not (Test-Path $afterCardPath)) { $afterCardPath = Join-Path $shots 'card-expanded.png' }
$aCard = LoadBmp $afterCardPath
$bSet = GlyphSet $bCard
$aSet = GlyphSet $aCard
$lines.Add('[大卡片 5 图标] 可见包围盒（px @1.5）')
$lines.Add(('  {0,-8} {1,-22} {2,-22}' -f '图标', '改动前', '改动后'))
for ($i = 0; $i -lt $bSet.Count; $i++) {
    $bb = $bSet[$i].Box; $ab = $aSet[$i].Box
    $bf = if ($bb) { '{0}x{1} @({2},{3})' -f $bb.Width, $bb.Height, $bb.X, $bb.Y } else { 'missing' }
    $af = if ($ab) { '{0}x{1} @({2},{3})' -f $ab.Width, $ab.Height, $ab.X, $ab.Y } else { 'missing' }
    $lines.Add(('  {0,-8} {1,-22} {2,-22}' -f $bSet[$i].Name, $bf, $af))
}
$bw = @($bSet | Where-Object { $_.Box } | ForEach-Object { $_.Box.Width })
$bh = @($bSet | Where-Object { $_.Box } | ForEach-Object { $_.Box.Height })
$aw = @($aSet | Where-Object { $_.Box } | ForEach-Object { $_.Box.Width })
$ah = @($aSet | Where-Object { $_.Box } | ForEach-Object { $_.Box.Height })
$lines.Add(('  改动前：宽 {0}…{1}（极差 {2}），高 {3}…{4}（极差 {5}）' -f ($bw | Measure-Object -Minimum).Minimum, ($bw | Measure-Object -Maximum).Maximum, (($bw | Measure-Object -Maximum).Maximum - ($bw | Measure-Object -Minimum).Minimum), ($bh | Measure-Object -Minimum).Minimum, ($bh | Measure-Object -Maximum).Maximum, (($bh | Measure-Object -Maximum).Maximum - ($bh | Measure-Object -Minimum).Minimum)))
$lines.Add(('  改动后：宽 {0}…{1}（极差 {2}），高 {3}…{4}（极差 {5}）' -f ($aw | Measure-Object -Minimum).Minimum, ($aw | Measure-Object -Maximum).Maximum, (($aw | Measure-Object -Maximum).Maximum - ($aw | Measure-Object -Minimum).Minimum), ($ah | Measure-Object -Minimum).Minimum, ($ah | Measure-Object -Maximum).Maximum, (($ah | Measure-Object -Maximum).Maximum - ($ah | Measure-Object -Minimum).Minimum)))
$lines.Add('  对比图：compare-glyphs-3x.png（上=改动前，下=改动后；改动后取真机 real-card-expanded.png）')
$lines.Add('')

# 图标放大对比图（两图宽相同 420，图标行同一坐标）
$br = [System.Drawing.Rectangle]::new(195, 8, 200, 52)
$bG = CropZoom $bCard $br 3
$aG = CropZoom $aCard $br 3
Stacked $bG $aG 'before' 'after (real app)' (Join-Path $After 'compare-glyphs-3x.png')
$bG.Dispose(); $aG.Dispose()

$metricsPath = Join-Path $After 'compare-metrics.txt'
[System.IO.File]::WriteAllLines($metricsPath, $lines, (New-Object System.Text.UTF8Encoding($false)))

$bChat.Dispose(); $aChat.Dispose(); $bCard.Dispose(); $aCard.Dispose()
Write-Host ("对比图 -> " + $After)
Write-Host ("数值 -> " + $metricsPath)
Get-Content $metricsPath | ForEach-Object { Write-Host $_ }
