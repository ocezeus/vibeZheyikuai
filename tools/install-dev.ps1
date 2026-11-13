# 开发装机：编译当前源码 → 替换到 %LOCALAPPDATA%\Programs\WorkCountdown → 重启悬浮窗。
#
# 日常改动就走这条，一条命令搞定，不再跑 package.ps1（打包 zip / 重出一图说明书只在发版时做）。
#
#   .\tools\install-dev.ps1            编译 + 替换 + 重启
#   .\tools\install-dev.ps1 -NoBuild   直接用 dist 里已有的 exe 替换 + 重启
#
# 注意：会先强制停掉正在跑的 WorkCountdown（配置在改动时已经落盘，强制结束不会丢设置）。

param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $root 'dist\WorkCountdown.exe'
$dst  = Join-Path $env:LOCALAPPDATA 'Programs\WorkCountdown'
$dstExe = Join-Path $dst 'WorkCountdown.exe'

if (-not $NoBuild) {
    & (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw '编译失败' }
    if (-not (Test-Path $exe)) { throw "编译后没找到 $exe" }
}
elseif (-not (Test-Path $exe)) {
    throw "没有 $exe，先跑一次 build.ps1（或去掉 -NoBuild）"
}

Get-Process WorkCountdown -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item $exe $dstExe -Force

Start-Process -FilePath $dstExe
Start-Sleep -Milliseconds 1500

$p = Get-Process WorkCountdown -ErrorAction SilentlyContinue | Select-Object -First 1
$v = (Get-Item $dstExe).VersionInfo.FileVersion
if ($p -eq $null) { throw "装好了，但 WorkCountdown 没起来：$dstExe" }

Write-Host "已装机：$dstExe"
Write-Host ("  FileVersion {0}  PID {1}  启动于 {2:HH:mm:ss}" -f $v, $p.Id, $p.StartTime)
