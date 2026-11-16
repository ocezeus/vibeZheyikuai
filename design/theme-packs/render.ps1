# 主题方向稿出图：design\theme-packs\index.html → test\visual\theme-packs\<theme>.png
#   .\design\theme-packs\render.ps1                    # 五套各一张，1.25x
#   .\design\theme-packs\render.ps1 -Scale 1 -Theme quiet
param(
    [double]$Scale = 1.25,
    [string[]]$Theme = @('quiet', 'mica', 'paper', 'instrument', 'neon'),
    [int]$Width = 1560,
    [int]$Height = 1400,
    [string]$Out = 'test\visual\theme-packs'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path $chrome)) { throw "找不到 Chrome：$chrome" }
$html = Join-Path $PSScriptRoot 'index.html'
if (-not (Test-Path $html)) { throw "找不到 $html" }
$outDir = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

foreach ($t in $Theme) {
    $ud = Join-Path $env:TEMP ('wc-tp-' + [guid]::NewGuid().ToString('N'))
    $png = Join-Path $outDir ("theme-$t.png")
    $url = 'file:///' + ($html -replace '\\', '/') + '#' + $t
    $args = @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars',
        "--force-device-scale-factor=$Scale",
        "--window-size=$Width,$Height",
        '--virtual-time-budget=4000',
        '--default-background-color=00000000',
        "--user-data-dir=$ud",
        "--screenshot=$png",
        $url
    )
    $p = Start-Process -FilePath $chrome -ArgumentList $args -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0 -or -not (Test-Path $png)) { throw "Chrome 出图失败：$t（exit $($p.ExitCode)）" }
    Remove-Item $ud -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host ("theme-{0}.png  {1:N0} KB" -f $t, ((Get-Item $png).Length / 1KB))
}
