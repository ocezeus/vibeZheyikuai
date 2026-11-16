# 真机验证「未读时左上角每 5 秒快闪两下」：连续抓卡片窗口帧，统计左上角蓝色像素变化。
# 输出：test\visual\real-flash\frames-*.png + 控制台打印每帧左上角蓝色像素数。
param(
    [string]$OutDir = 'D:\DSH\RWZ0\work-countdown\test\visual\real-flash',
    [int]$Seconds = 7,
    [int]$StepMs = 80
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
try { Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public class DpiFixF { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }' } catch { }
[DpiFixF]::SetProcessDPIAware() | Out-Null
$code = @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WinF {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static List<IntPtr> Windows(uint pid) {
    var list = new List<IntPtr>();
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) list.Add(h);
      return true;
    }, IntPtr.Zero);
    return list;
  }
  public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
}
'@
Add-Type -TypeDefinition $code
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$procId = (Get-Process WorkCountdown -ErrorAction Stop | Select-Object -First 1).Id
$card = [IntPtr]::Zero
foreach ($h in [WinF]::Windows($procId)) { if ([WinF]::Title($h) -eq '上班工具箱') { $card = $h; break } }
if ($card -eq [IntPtr]::Zero) { throw '没找到悬浮窗' }

function Get-Rect([IntPtr]$h) {
    $r = New-Object WinF+RECT
    [WinF]::GetWindowRect($h, [ref]$r) | Out-Null
    return $r
}
function Click([IntPtr]$h, [int]$x, [int]$y) {
    $lp = [IntPtr](($y -shl 16) -bor $x)
    [WinF]::PostMessage($h, 0x0200, [IntPtr]0, $lp) | Out-Null
    [WinF]::PostMessage($h, 0x0201, [IntPtr]1, $lp) | Out-Null
    [WinF]::PostMessage($h, 0x0202, [IntPtr]0, $lp) | Out-Null
}
function Count-CornerBlue([System.Drawing.Bitmap]$bmp) {
    $n = 0
    $maxX = [Math]::Min($bmp.Width - 1, [int](110 * 1.5))
    $maxY = [Math]::Min($bmp.Height - 1, [int](110 * 1.5))
    for ($y = 6; $y -lt $maxY; $y += 2) {
        for ($x = 6; $x -lt $maxX; $x += 2) {
            $c = $bmp.GetPixel($x, $y)
            if ($c.B -gt 150 -and $c.B -gt ($c.R + 35) -and $c.B -gt ($c.G + 30)) { $n++ }
        }
    }
    return $n
}
function Capture([IntPtr]$h, [string]$tag) {
    $r = Get-Rect $h
    $w = $r.R - $r.L; $hh = $r.B - $r.T
    $bmp = New-Object System.Drawing.Bitmap($w, $hh)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [WinF]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $n = Count-CornerBlue $bmp
    $g.Dispose()
    return @{ bmp = $bmp; blue = $n; w = $w; h = $hh }
}

$r = Get-Rect $card
$w = $r.R - $r.L
$label = if ($w -lt 500) { 'collapsed' } else { 'expanded' }
if ($label -eq 'collapsed') {
    Click $card ([int]($w / 2)) ([int](($r.B - $r.T) / 2))
    Start-Sleep -Milliseconds 900
    $r = Get-Rect $card
}

$label = if ($r.R - $r.L -lt 500) { 'collapsed' } else { 'expanded' }
$results = @()
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$i = 0
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    $cap = Capture $card $label
    $results += ("{0,5} ms  blue={1,6}  size={2}x{3}" -f [int]$sw.ElapsedMilliseconds, $cap.blue, $cap.w, $cap.h)
    $cap.bmp.Save((Join-Path $OutDir ("frames-{0}-{1:D3}.png" -f $label, $i)), [System.Drawing.Imaging.ImageFormat]::Png)
    $cap.bmp.Dispose()
    $i++
    Start-Sleep -Milliseconds $StepMs
}
$results | ForEach-Object { Write-Host $_ }
"frames -> $OutDir"
