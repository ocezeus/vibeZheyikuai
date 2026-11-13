# 右键胶囊菜单原型：用本机 Chrome 无头 2× 渲染再降采样。
#   .\render-bubbles.ps1
# 单看一帧：bubble-menu.html?v=u&snap=1&static=1&state=theme
$ErrorActionPreference = 'Stop'
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$shots = Join-Path $root 'shots'
$tmp = Join-Path $root 'shots\_2x'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$base = 'file:///' + ($root -replace '\\', '/') + '/bubble-menu.html'
$env:NODE_PATH = 'C:\Users\xlean\AppData\Local\npm-cache\_npx\1e7f6d9597241db0\node_modules'

function Shoot([string]$name, [string]$query, [int]$w = 1280, [int]$h = 720) {
  $raw = Join-Path $tmp ($name + '@2x.png')
  $args = @(
    '--headless=new', '--disable-gpu', '--hide-scrollbars', '--disable-lcd-text',
    '--force-device-scale-factor=2',
    "--window-size=$w,$h", '--virtual-time-budget=3500',
    "--user-data-dir=$env:TEMP\wc-capsule-$name",
    "--screenshot=$raw",
    "$base$query"
  )
  $p = Start-Process -FilePath $chrome -ArgumentList $args -Wait -PassThru -NoNewWindow
  if ($p.ExitCode -ne 0) { throw "chrome failed for $name" }
  node (Join-Path $root 'downscale.cjs') $raw (Join-Path $shots ($name + '.png')) $w $h
  if ($LASTEXITCODE -ne 0) { throw "downscale failed for $name" }
  Remove-Item $raw -Force
}

Shoot 'capsule-bubbles'       '?v=u&snap=1&static=1&state=bubbles'
Shoot 'capsule-bubbles-big'   '?v=u&snap=1&static=1&state=bubbles&card=big'
Shoot 'capsule-theme'         '?v=u&snap=1&static=1&state=theme'
Shoot 'capsule-theme-picked'  '?v=u&snap=1&static=1&state=theme&pick=3'
Shoot 'capsule-light'         '?v=u&snap=1&static=1&state=theme&pick=7'
Shoot 'capsule-clock'         '?v=u&snap=1&static=1&state=clock'
Shoot 'capsule-schedule'      '?v=u&snap=1&static=1&state=schedule&card=big'
Shoot 'capsule-pin'           '?v=u&snap=1&static=1&state=pin'
Shoot 'capsule-hug'           '?v=h&snap=1&static=1&state=bubbles'

Get-ChildItem $shots -Filter 'capsule-*.png' | ForEach-Object { Write-Host ("{0}  {1:N0} bytes" -f $_.Name, $_.Length) }
