# Render the design preview to PNGs with headless Chrome.
# Renders at 2x device scale, then downscales to the CSS pixel size so the
# screenshots get clean grayscale-ish antialiasing (no ClearType fringes).
# Usage: powershell -File render.ps1
$ErrorActionPreference = 'Stop'
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$shots = Join-Path $root 'shots'
$tmp = Join-Path $root 'shots\_2x'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$base = 'file:///' + ($root -replace '\\', '/') + '/preview.html'
$env:NODE_PATH = 'C:\Users\xlean\AppData\Local\npm-cache\_npx\1e7f6d9597241db0\node_modules'

function Shoot([string]$name, [string]$query, [int]$w, [int]$h) {
  $raw = Join-Path $tmp ($name + '@2x.png')
  $args = @(
    '--headless=new', '--disable-gpu', '--hide-scrollbars', '--disable-lcd-text',
    '--force-device-scale-factor=2',
    "--window-size=$w,$h", '--virtual-time-budget=3000',
    "--user-data-dir=$env:TEMP\wc-shoot-$name",
    "--screenshot=$raw",
    "$base$query"
  )
  $p = Start-Process -FilePath $chrome -ArgumentList $args -Wait -PassThru -NoNewWindow
  if ($p.ExitCode -ne 0) { throw "chrome failed for $name" }
  node (Join-Path $root 'downscale.cjs') $raw (Join-Path $shots ($name + '.png')) $w $h
  if ($LASTEXITCODE -ne 0) { throw "downscale failed for $name" }
  Remove-Item $raw -Force
}

Shoot 'cards' '?section=cards' 1010 900
foreach ($k in @('a', 'b', 'c', 'd', 'e')) {
  Shoot $k "?d=$k&snap=1" 1280 720
}
Get-ChildItem $shots -Filter *.png | Select-Object Name, Length | Format-Table -AutoSize
