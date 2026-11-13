# 渲染「小纸条」设计稿的状态截图（Chrome 无头，1:1 1280×720）。
#
#   .\render-note.ps1                 三版全渲染
#   .\render-note.ps1 -Variant a      只渲染 A
#   .\render-note.ps1 -Theme midnight 用午夜蓝主题
#
# 输出：.\shots\v-{a,b,c}-{state}.png；对比长图 sheet-*.png 另行用 Python + PIL 拼。

param(
    [string]$Variant = 'all',
    [string]$Theme = 'gold'
)

$ErrorActionPreference = 'Stop'

$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path $chrome)) { $chrome = 'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe' }
if (-not (Test-Path $chrome)) { $chrome = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe" }
if (-not (Test-Path $chrome)) { throw '找不到 Chrome / Edge，没法出图' }

$base = $PSScriptRoot
$shots = Join-Path $base 'shots'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$page = 'file:///' + ((Join-Path $base 'note.html') -replace '\\', '/')

$variants = if ($Variant -eq 'all') { @('a', 'b', 'c') } else { @($Variant) }
$states = @('select', 'chat', 'history', 'add', 'flash')

foreach ($v in $variants) {
    foreach ($s in $states) {
        $extra = ''
        if ($s -eq 'history' -and $v -eq 'c') { $extra = '&scroll=bottom' }
        $url = "$page?v=$v&state=$s&clean=1&still=1&theme=$Theme$extra"
        $out = Join-Path $shots ("v-{0}-{1}.png" -f $v, $s)
        # Chrome 会在 stderr 打一行 LOG ERROR（无害），用 Continue 吞掉，别被 Stop 炸掉
        $eap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $chrome --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=1280,720 --screenshot="$out" $url 2>$null | Out-Null
        $ErrorActionPreference = $eap
        if (-not (Test-Path $out)) { throw "出图失败：$out" }
        Write-Host ("ok  v-{0}-{1}.png" -f $v, $s)
    }
}

Write-Host ''
Write-Host "全部输出在：$shots"

# 闪光三种贴边样式（A 版闪态）
foreach ($f in @('1','2','3')) {
    $url = "$page?v=a&state=flash&clean=1&still=1&theme=$Theme&flash=$f"
    $out = Join-Path $shots ("flash-{0}.png" -f $f)
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $chrome --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=1280,720 --screenshot="$out" $url 2>$null | Out-Null
    $ErrorActionPreference = $eap
    if (Test-Path $out) { Write-Host ("ok  flash-{0}.png" -f $f) }
}

# 设置 → 纸条 页（第 7 个页签）
$setOut = Join-Path $shots 'settings-note.png'
$eap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& $chrome --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --window-size=900,1040 --screenshot="$setOut" ('file:///' + ((Join-Path $base 'settings-note.html') -replace '\\','/')) 2>$null | Out-Null
$ErrorActionPreference = $eap
if (Test-Path $setOut) { Write-Host 'ok  settings-note.png' }