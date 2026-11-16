# 真机验证：小纸条 → 点输入框长高到历史页 → 截图（确认一屏 8 条、最新一条能看见、滚动条在）
# 用法：.\test\note-history-shot.ps1 -OutDir test\visual\real-note-live
param([string]$OutDir = 'D:\DSH\RWZ0\work-countdown\test\visual\real-note-live')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

try { Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public class DpiFixH { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }' } catch { }
[DpiFixH]::SetProcessDPIAware() | Out-Null

$code = @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WinH {
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
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

function Find-Titled([string]$title) {
    foreach ($h in [WinH]::Windows($procId)) { if ([WinH]::Title($h) -eq $title) { return $h } }
    return [IntPtr]::Zero
}
function Shot([IntPtr]$h, [string]$path) {
    $r = New-Object WinH+RECT
    [WinH]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $hh = $r.B - $r.T
    $bmp = New-Object System.Drawing.Bitmap($w, $hh)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [WinH]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "shot ${w}x${hh} -> $path"
}
function Click([IntPtr]$h, [int]$x, [int]$y) {
    $lp = [IntPtr](($y -shl 16) -bor $x)
    [WinH]::PostMessage($h, 0x0200, [IntPtr]0, $lp) | Out-Null
    Start-Sleep -Milliseconds 40
    [WinH]::PostMessage($h, 0x0201, [IntPtr]1, $lp) | Out-Null
    Start-Sleep -Milliseconds 40
    [WinH]::PostMessage($h, 0x0202, [IntPtr]0, $lp) | Out-Null
}

$card = Find-Titled '上班工具箱'
if ($card -eq [IntPtr]::Zero) { throw '没找到悬浮窗' }

# 1) 展开卡片（收起时点一下横条）
$r = New-Object WinH+RECT
[WinH]::GetWindowRect($card, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
if ($w -lt 500) {
    Click $card ([int]($w / 2)) ([int]($h / 2))
    Start-Sleep -Milliseconds 900
    [WinH]::GetWindowRect($card, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $h = $r.B - $r.T
}

# 2) 点小飞机打开小纸条
Click $card ([int]($w - 134 * 1.5)) ([int]((12 + 12) * 1.5))
Start-Sleep -Milliseconds 1000

# 3) 找面板窗口（标题空 + 宽 > 400）
$panel = [IntPtr]::Zero
foreach ($hh in [WinH]::Windows($procId)) {
    if ($hh -eq $card) { continue }
    $rr = New-Object WinH+RECT
    [WinH]::GetWindowRect($hh, [ref]$rr) | Out-Null
    $pw = $rr.R - $rr.L; $ph = $rr.B - $rr.T
    if ($pw -gt 400 -and $ph -gt 100) { $panel = $hh; break }
}
if ($panel -eq [IntPtr]::Zero) { throw '面板没打开' }
Start-Sleep -Milliseconds 500
Shot $panel (Join-Path $OutDir 'panel-before.png')

# 4) 先点第一个对话胶囊（面板停在对话列表时），再点输入框 → 长高到历史页
$c = New-Object WinH+RECT
[WinH]::GetClientRect($panel, [ref]$c) | Out-Null
$cw = $c.R - $c.L; $ch = $c.B - $c.T
Click $panel ([int](57 * 1.5)) ([int](71 * 1.5))
Start-Sleep -Milliseconds 1000
[WinH]::GetClientRect($panel, [ref]$c) | Out-Null
$ch = $c.B - $c.T
Shot $panel (Join-Path $OutDir 'panel-chat.png')
Click $panel ([int]($cw / 2)) ([int]($ch - 24 * 1.5))
Start-Sleep -Milliseconds 1400
Shot $panel (Join-Path $OutDir 'panel-history-newest.png')

# 5) 鼠标滚轮往上翻 4 格（看更早的）
foreach ($i in 0..3) {
    $lp = [IntPtr](((240 -shl 16) -bor 300))
    [WinH]::PostMessage($panel, 0x020A, [IntPtr]((120 -shl 16)), $lp) | Out-Null
    Start-Sleep -Milliseconds 120
}
Start-Sleep -Milliseconds 300
Shot $panel (Join-Path $OutDir 'panel-history-scrolled.png')

# 6) 关面板：发 WM_CLOSE（面板会自己淡出关闭）
[WinH]::PostMessage($panel, 0x0010, [IntPtr]0, [IntPtr]0) | Out-Null
Start-Sleep -Milliseconds 600
"done -> $OutDir"
