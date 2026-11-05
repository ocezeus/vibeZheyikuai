param([string]$Out = 'dlg.png', [string]$TabName = '外观')
# 用 UI Automation 选中设置对话框里的标签页，再 PrintWindow 抓图
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
try { Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public class DpiQ2 { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }' } catch { }
[DpiQ2]::SetProcessDPIAware() | Out-Null

$code = @'
using System;
using System.Runtime.InteropServices;
public class DW2 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
'@
Add-Type -TypeDefinition $code

$AE = [System.Windows.Automation.AutomationElement]
$procId = (Get-Process WorkCountdown -ErrorAction Stop | Select-Object -First 1).Id

$dlg = [IntPtr]::Zero
[DW2]::EnumWindows({ param($h, $l)
    $p = 0
    [DW2]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
    if ($p -eq $procId -and [DW2]::IsWindowVisible($h)) {
        $el = $AE::FromHandle($h)
        if ($el -ne $null -and $el.Current.Name -eq '下班倒计时 · 设置') { $script:dlg = $h }
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
if ($dlg -eq [IntPtr]::Zero) { throw '设置窗口没开' }

$root = $AE::FromHandle($dlg)
$items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)))
"tabs: " + (($items | ForEach-Object { $_.Current.Name }) -join ' | ')

$target = $null
foreach ($it in $items) { if ($it.Current.Name -eq $TabName) { $target = $it } }
if ($target -eq $null -and $items.Count -ge 3) { $target = $items[[int]$TabIndex] }
if ($target -eq $null) { throw "没有标签页：$TabName" }
$sel = $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$sel.Select()
Start-Sleep -Milliseconds 700

$r = New-Object DW2+RECT
[DW2]::GetWindowRect($dlg, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[DW2]::PrintWindow($dlg, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"captured ${w}x${h} -> $Out"
