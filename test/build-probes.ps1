# 逐个编译 test 下的探针入口（含 static Main 的文件），包含 test\verify\NoteVerifyProbe*.cs。
#
# 为什么需要：build-tests.ps1 只编译 Test / Check 两套正式套件；截图、交互、历史验收探针
# 历史上没有编译入口，接口一改就可能悄悄编不过。#4 曾机械改过 27 个探针的 Load().Settings，
# 直到本脚本才被逐个验证。它不改变正式两套套件的编译与运行。
#
#   .\test\build-probes.ps1            逐个编译到临时目录，全部 OK 才 exit 0
#   .\test\build-probes.ps1 -Keep     保留临时 exe，便于手工跑某个探针
#
# 约定同 build-tests.ps1：带 DPI manifest、只读配置由探针自己设 WC_NO_SAVE=1。

param(
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$srcDir  = Join-Path $root 'src'
$testDir = Join-Path $root 'test'
$assets  = Join-Path $root 'assets'
$manifest = Join-Path $assets 'app.manifest'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 .NET Framework 的 csc.exe' }
if (-not (Test-Path $manifest)) { throw "缺少 DPI 清单：$manifest（先跑一次 build.ps1 或 build-tests.ps1）" }

$refs = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:System.Security.dll'
)
# Program.cs 自带 Main；探针共用同一份 src
$sources = (Get-ChildItem -Path (Join-Path $srcDir '*.cs') |
    Where-Object { $_.Name -ne 'Program.cs' } |
    Sort-Object Name).FullName
$testKit = @((Join-Path $testDir 'SyncFakes.cs'), (Join-Path $testDir 'SettingsStoreFakes.cs'), (Join-Path $testDir 'SettingsApplyFakes.cs'))

$entries = @()
foreach ($dir in @($testDir, (Join-Path $testDir 'verify'))) {
    $entries += Get-ChildItem -Path (Join-Path $dir '*.cs') |
        Where-Object { Select-String -Path $_.FullName -Pattern 'static\s+(void|int)\s+Main\s*\(' -Quiet } |
        Sort-Object Name
}
if ($entries.Count -eq 0) { throw '没找到任何探针入口（含 static Main 的 .cs）' }

$outDir = Join-Path $env:TEMP ('wc-probes-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$failed = 0
foreach ($file in $entries) {
    $exe = Join-Path $outDir ($file.BaseName + '.exe')
    $cscArgs = @('/nologo', '/target:exe', '/platform:anycpu', '/codepage:65001',
        ('/win32manifest:' + $manifest), ('/out:' + $exe)) + $refs + $sources + $testKit + @($file.FullName)
    $out = & $csc $cscArgs 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host ('  OK    ' + $file.Name)
    } else {
        $failed++
        Write-Host ('  FAIL  ' + $file.Name) -ForegroundColor Red
        $out | Where-Object { $_ -match 'error CS' } | ForEach-Object { Write-Host ('        ' + $_) -ForegroundColor Red }
    }
}

Write-Host ''
Write-Host ('探针编译：' + $entries.Count + ' 个入口 / ' + ($entries.Count - $failed) + ' OK / ' + $failed + ' FAIL') -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Red' })
if ($Keep) { Write-Host ('临时目录：' + $outDir) } else { Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue }
if ($failed -gt 0) { exit 1 }
exit 0
