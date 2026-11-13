# 独立验收探针：编译 test\verify\NoteVerifyProbe.cs（和 src\*.cs 一起编，去掉 Program.cs）→ 跑断言 → 出改动后截图。
#
#   .\test\verify\run-verify.ps1              编译 + 跑 + 截图（默认写到 test\visual\verify-after）
#   .\test\verify\run-verify.ps1 -NoRun       只编译
#
# 注意：
#  - 只写 test\verify\NoteVerifyProbe.exe，绝不覆盖 test\Check.exe / test\Test.exe。
#  - 编译参数照 test\build-tests.ps1：/codepage:65001 + /win32manifest:assets\app.manifest（DPI 感知）。
#  - 探针自己带 WC_NO_SAVE=1 / WC_NO_RELAY=1，不连真实中继、不动真配置。

param(
    [switch]$NoRun
)

$ErrorActionPreference = 'Stop'

$verifyDir = $PSScriptRoot
$testDir   = Split-Path -Parent $verifyDir
$root      = Split-Path -Parent $testDir
$srcDir    = Join-Path $root 'src'
$assets    = Join-Path $root 'assets'
$outDir    = Join-Path $root 'test\visual\verify-after'

# ---------- 编译器 ----------
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 .NET Framework 的 csc.exe' }

# ---------- DPI 感知清单（和 build-tests.ps1 一致；没有就补一份） ----------
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

$exe = Join-Path $verifyDir 'NoteVerifyProbe.exe'
$probeSrc = Join-Path $verifyDir 'NoteVerifyProbe.cs'

Write-Host "编译探针 -> test\verify\NoteVerifyProbe.exe"
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
Write-Host "----- NoteVerifyProbe -> $outDir -----"
$out  = @(& $exe $outDir)
$code = $LASTEXITCODE
foreach ($line in $out) { Write-Host $line }

$pass  = @($out | Where-Object { $_ -like 'PASS *' }).Count
$fails = @($out | Where-Object { $_ -like 'FAIL  *' })
$skips = @($out | Where-Object { $_ -like 'SKIP*' }).Count

Write-Host ''
Write-Host ("  {0} 条 PASS / {1} 条 FAIL / {2} 条 SKIP / exit={3}" -f $pass, $fails.Count, $skips, $code)
if ($fails.Count -gt 0) { foreach ($f in $fails) { Write-Host "  $f" -ForegroundColor Red } }
Write-Host "  截图目录：$outDir"

# 有改动前基线就顺手出 before/after 对比图与数值（边框粗细、5 图标包围盒）
$beforeChat = Join-Path $root 'test\visual\verify-before\note-drawer\note-drawer-chat.png'
if (Test-Path $beforeChat) {
    $cmp = Join-Path $verifyDir 'make-compare.ps1'
    if (Test-Path $cmp) {
        Write-Host ''
        & $cmp | Out-Host
    }
}
exit $code
