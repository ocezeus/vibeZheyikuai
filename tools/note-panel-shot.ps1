# 真机截图：展开大卡片 → 打开小纸条面板 → 截面板（联系人态 / 右键删除按钮）→ 关面板
# 注意：只做「右键弹出删除按钮」的截图，绝不点删除（用户真机里有 93183/10825 两个真对话）
param([string]$OutDir = 'D:\DSH\RWZ0\work-countdown\test\visual\real')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

try { Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public class DpiFix2 { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }' } catch { }
[DpiFix2]::SetProcessDPIAware() | Out-Null

$code = @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Win2 {
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
  public static string Title(IntPtr h) {
    var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString();
  }
}
'@
Add-Type -TypeDefinition $code

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$procId = (Get-Process WorkCountdown -ErrorAction Stop | Select-Object -First 1).Id

function Find-Titled([string]$title) {
    foreach ($h in [Win2]::Windows($procId)) {
        if ([Win2]::Title($h) -eq $title) { return $h }
    }
    return [IntPtr]::Zero
}

function Shot([IntPtr]$h, [string]$path) {
    $r = New-Object Win2+RECT
    [Win2]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $hh = $r.B - $r.T
    $bmp = New-Object System.Drawing.Bitmap($w, $hh)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [Win2]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "shot ${w}x${hh} -> $path"
}

$card = Find-Titled '上班工具箱'
if ($card -eq [IntPtr]::Zero) { throw '没找到悬浮窗' }

# 1. 展开（折叠时先点一下箭头）
$r = New-Object Win2+RECT
[Win2]::GetWindowRect($card, [ref]$r) | Out-Null
$w = $r.R - $r.L
if ($w -lt 380) {
    $lp = [IntPtr](((132 / 2) -shl 16) -bor ($w / 2))
    [Win2]::PostMessage($card, 0x0201, [IntPtr]1, $lp) | Out-Null
    Start-Sleep -Milliseconds 60
    [Win2]::PostMessage($card, 0x0202, [IntPtr]0, $lp) | Out-Null
    Start-Sleep -Milliseconds 900
}
[Win2]::GetWindowRect($card, [ref]$r) | Out-Null
$w = $r.R - $r.L
Shot $card (Join-Path $OutDir 'card-expanded-real.png')

# 2. 点小飞机（展开态：x = W - S(146) + S(12)，y = S(12) + 12）
$nx = [int]($w - 134 * 1.5)
$ny = [int]((12 + 12) * 1.5)
$lp = [IntPtr](($ny -shl 16) -bor $nx)
[Win2]::PostMessage($card, 0x0200, [IntPtr]0, $lp) | Out-Null
Start-Sleep -Milliseconds 60
[Win2]::PostMessage($card, 0x0201, [IntPtr]1, $lp) | Out-Null
Start-Sleep -Milliseconds 60
[Win2]::PostMessage($card, 0x0202, [IntPtr]0, $lp) | Out-Null
Start-Sleep -Milliseconds 900

# 3. 面板是另一个窗口：挑「标题为空 + 宽度 > 400」的那个
$panel = [IntPtr]::Zero
foreach ($h in [Win2]::Windows($procId)) {
    if ($h -eq $card) { continue }
    $rr = New-Object Win2+RECT
    [Win2]::GetWindowRect($h, [ref]$rr) | Out-Null
    $pw = $rr.R - $rr.L; $ph = $rr.B - $rr.T
    if ($pw -gt 400 -and $ph -gt 100) { $panel = $h; break }
}
if ($panel -eq [IntPtr]::Zero) { throw '面板没打开' }
Start-Sleep -Milliseconds 300
Shot $panel (Join-Path $OutDir 'note-panel-contacts.png')

# 4. 右键第一个对话 → 出现「删除」按钮（只看不点，别删用户真数据）
$lp = [IntPtr](((106) -shl 16) -bor (88))
[Win2]::PostMessage($panel, 0x0200, [IntPtr]0, $lp) | Out-Null
Start-Sleep -Milliseconds 60
[Win2]::PostMessage($panel, 0x0204, [IntPtr]2, $lp) | Out-Null
Start-Sleep -Milliseconds 60
[Win2]::PostMessage($panel, 0x0205, [IntPtr]0, $lp) | Out-Null
Start-Sleep -Milliseconds 500
Shot $panel (Join-Path $OutDir 'note-panel-rightclick-delete.png')

# 5. 左键点面板空白处收掉删除按钮（不点删除本身）
$lp = [IntPtr](((300) -shl 16) -bor (200))
[Win2]::PostMessage($panel, 0x0201, [IntPtr]1, $lp) | Out-Null
Start-Sleep -Milliseconds 60
[Win2]::PostMessage($panel, 0x0202, [IntPtr]0, $lp) | Out-Null
Start-Sleep -Milliseconds 300

"done -> $OutDir"
