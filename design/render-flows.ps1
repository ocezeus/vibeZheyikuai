# Render the reminder-flow and drawer previews to PNGs with headless Chrome.
# 2x device scale, downscaled to CSS pixels (same pipeline as render.ps1).
# Usage: powershell -File render-flows.ps1
$ErrorActionPreference = 'Stop'
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$shots = Join-Path $root 'shots'
$tmp = Join-Path $root 'shots\_2x'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$env:NODE_PATH = 'C:\Users\xlean\AppData\Local\npm-cache\_npx\1e7f6d9597241db0\node_modules'
$base = 'file:///' + ($root -replace '\\', '/')

function Shoot([string]$page, [string]$name, [string]$query, [int]$w = 1280, [int]$h = 720) {
  $raw = Join-Path $tmp ($name + '@2x.png')
  $log = Join-Path $tmp ($name + '.log')
  $cargs = @(
    '--headless=new', '--disable-gpu', '--hide-scrollbars', '--disable-lcd-text',
    '--force-device-scale-factor=2', "--window-size=$w,$h",
    '--virtual-time-budget=4000',
    "--user-data-dir=$env:TEMP\wc-shoot-$name",
    "--screenshot=$raw",
    "$base/$page$query"
  )
  if (Test-Path $raw) { Remove-Item $raw -Force }
  & $chrome @cargs *> $log
  # headless chrome detaches; wait until the png exists and stops growing
  for ($i = 0; $i -lt 150 -and -not (Test-Path $raw); $i++) { Start-Sleep -Milliseconds 100 }
  if (Test-Path $raw) {
    $last = -1
    for ($i = 0; $i -lt 50; $i++) {
      $len = (Get-Item $raw).Length
      if ($len -gt 0 -and $len -eq $last) { break }
      $last = $len
      Start-Sleep -Milliseconds 120
    }
  }
  if (-not (Test-Path $raw)) { throw "chrome failed for $name`n" + (Get-Content $log -Raw) }
  node (Join-Path $root 'downscale.cjs') $raw (Join-Path $shots ($name + '.png')) $w $h
  if ($LASTEXITCODE -ne 0) { throw "downscale failed for $name" }
  Remove-Item $raw -Force
}

# flow: band on screen / drawer sliding out / drawer settled
Shoot 'flow-preview.html' 'flow-band'       '?snap=1&t=3.6'
Shoot 'flow-preview.html' 'flow-drawer-mid' '?snap=1&t=7.7'
Shoot 'flow-preview.html' 'flow-drawer'     '?snap=1&t=10'

# drawer: scheme A (tone-graded text), settled + one mid-slide + 2x text detail
Shoot 'drawer-preview.html' 'drawer-a'        '?snap=1&t=4.2'
Shoot 'drawer-preview.html' 'drawer-a-mid'    '?snap=1&t=0.95'
Shoot 'drawer-preview.html' 'drawer-a-detail' '?detail=1' 764 136

Get-ChildItem $shots -Filter '*.png' | Select-Object Name, Length | Format-Table -AutoSize




