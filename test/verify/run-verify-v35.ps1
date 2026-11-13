# v3.5.0 独立验收探针：编译 test\verify\NoteVerifyProbeV35.cs（和 src\*.cs 一起编，去掉 Program.cs）→ 跑断言 → 出截图。
#
#   .\test\verify\run-verify-v35.ps1           编译 + 跑
#   .\test\verify\run-verify-v35.ps1 -NoRun    只编译
#
# 注意：
#  - 只写 test\verify\NoteVerifyProbeV35.exe 与 test\verify\v35-shots\，绝不覆盖 test\Check.exe / test\Test.exe。
#  - 编译参数照 test\build-tests.ps1：/codepage:65001 + /win32manifest:assets\app.manifest（DPI 感知）。
#  - 探针自己带 WC_NO_SAVE=1 / WC_NO_RELAY=1，不连真实中继、不动真配置。

param(
    [switch]$NoRun
)

$ErrorActionPreference = 'Stop'

$verifyDir = $PSScriptRoot
$root      = Split-Path -Parent (Split-Path -Parent $verifyDir)
$srcDir    = Join-Path $root 'src'
$assets    = Join-Path $root 'assets'
$outDir    = Join-Path $verifyDir 'v35-shots'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 .NET Framework 的 csc.exe' }

$manifest = Join-Path $assets 'app.manifest'
if (-not (Test-Path $manifest)) {
    New-Item -ItemType Directory -Force -Path $assets | Out-Null
    $manifestXml = @'
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="WorkCountdown.app"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">system</dpiAwareness>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
'@
    [System.IO.File]::WriteAllText($manifest, $manifestXml, (New-Object System.Text.UTF8Encoding($false)))
}

$refs = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll'
)
$sources = (Get-ChildItem -Path (Join-Path $srcDir '*.cs') |
    Where-Object { $_.Name -ne 'Program.cs' } |
    Sort-Object Name).FullName
$common = @(
    '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    ('/win32manifest:' + $manifest)
)

$exe = Join-Path $verifyDir 'NoteVerifyProbeV35.exe'
$probeSrc = Join-Path $verifyDir 'NoteVerifyProbeV35.cs'

Write-Host '编译探针 -> test\verify\NoteVerifyProbeV35.exe'
$cscArgs = $common + @(('/out:' + $exe)) + $refs + $sources + @($probeSrc)
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "探针编译失败（exit $LASTEXITCODE）" -ForegroundColor Red
    exit 1
}

if ($NoRun) {
    Write-Host '编译完成，没有跑（-NoRun）'
    exit 0
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Write-Host ''
Write-Host "----- NoteVerifyProbeV35 -> $outDir -----"
$out  = @(& $exe $outDir)
$code = $LASTEXITCODE
foreach ($line in $out) { Write-Host $line }

$pass  = @($out | Where-Object { $_ -like 'PASS  *' }).Count
$fails = @($out | Where-Object { $_ -like 'FAIL  *' })
$skips = @($out | Where-Object { $_ -like 'SKIP*' }).Count

Write-Host ''
Write-Host ("  {0} 条 PASS / {1} 条 FAIL / {2} 条 SKIP / exit={3}" -f $pass, $fails.Count, $skips, $code)
if ($fails.Count -gt 0) { foreach ($f in $fails) { Write-Host "  $f" -ForegroundColor Red } }
Write-Host "  截图目录：$outDir"

# 顺手输出当前装机版本（Lead 稍后会装 3.5.0；装了就能交叉核对）
$installed = Join-Path $env:LOCALAPPDATA 'Programs\WorkCountdown\WorkCountdown.exe'
if (Test-Path $installed) {
    $fv = (Get-Item $installed).VersionInfo.FileVersion
    $sha = (Get-FileHash $installed -Algorithm SHA256).Hash.Substring(0, 16)
    Write-Host ("  已装机：FileVersion=" + $fv + "  SHA256前16=" + $sha + "  " + (Get-Item $installed).LastWriteTime)
}
exit $code
