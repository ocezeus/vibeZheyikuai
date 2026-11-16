# 编译并运行两套测试（test\Test.cs 逻辑断言、test\Check.cs 界面与规则验收）。
#
# 为什么要这个脚本：两套测试都必须带 /win32manifest:assets\app.manifest。
# 不带的话 Check 在 150% 缩放下会假失败 3 条（"上面的色块是落下来的"、"退场时下面的先走"、
# "展开时时间先出来"），看起来像动画 bug，其实是 DPI 感知没开。
#
#   .\test\build-tests.ps1           编译 + 跑，两套都 ALL PASS 才算成功
#   .\test\build-tests.ps1 -NoRun    只编译，不跑
#
# 跑之前请确认没有 WorkCountdown 正在写配置；探针都带 WC_NO_SAVE=1，不会回写用户设置。
# 截图 / 交互 / 历史验收探针不在这两套套件里：用 .\test\build-probes.ps1 逐个编译验证。

param(
    [switch]$NoRun
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$srcDir  = Join-Path $root 'src'
$testDir = Join-Path $root 'test'
$assets  = Join-Path $root 'assets'

# ---------- 编译器 ----------
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 .NET Framework 的 csc.exe（这台机器不需要装 SDK，但要有 4.x 运行时）' }

# ---------- DPI 感知清单 ----------
# 平时由 build.ps1 生成；没跑过 build 的干净仓库里补一份，内容和 build.ps1 里的完全一致
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
    Write-Host "已生成 DPI 清单 -> $manifest"
}

# ---------- 公共编译参数 ----------
$refs = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll'
)
# Program.cs 自带 Main，测试程序不要它
$sources = (Get-ChildItem -Path (Join-Path $srcDir '*.cs') |
    Where-Object { $_.Name -ne 'Program.cs' } |
    Sort-Object Name).FullName

$common = @(
    '/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    ('/win32manifest:' + $manifest)
)

# 两套测试共用的可控同步 adapter / 调度器
$testKit = @((Join-Path $testDir 'SyncFakes.cs'), (Join-Path $testDir 'SettingsStoreFakes.cs'), (Join-Path $testDir 'SettingsApplyFakes.cs'))

$suites = @(
    [pscustomobject]@{ Name = 'Test';  Src = 'Test.cs';  Exe = 'Test.exe'  },
    [pscustomobject]@{ Name = 'Check'; Src = 'Check.cs'; Exe = 'Check.exe' }
)

# ---------- 编译 ----------
foreach ($s in $suites) {
    $exe = Join-Path $testDir $s.Exe
    $cscArgs = $common + @(('/out:' + $exe)) + $refs + $sources + $testKit + @((Join-Path $testDir $s.Src))
    Write-Host "编译 -> $($s.Exe)"
    & $csc $cscArgs
    if ($LASTEXITCODE -ne 0) { throw "编译失败：$($s.Exe)（exit $LASTEXITCODE）" }
}

if ($NoRun) {
    Write-Host '编译完成，没有跑（-NoRun）'
    exit 0
}

# ---------- 跑 ----------
$failed = 0
foreach ($s in $suites) {
    $exe = Join-Path $testDir $s.Exe
    Write-Host ''
    Write-Host "----- $($s.Name) -----"
    $out   = @(& $exe)
    $code  = $LASTEXITCODE
    $pass  = @($out | Where-Object { $_ -like 'PASS *' }).Count
    $fails = @($out | Where-Object { $_ -like 'FAIL*' })

    foreach ($line in $out | Where-Object { $_ -like 'ALL PASS*' }) { Write-Host "  $line" -ForegroundColor Green }
    foreach ($line in $fails) { Write-Host "  $line" -ForegroundColor Red }
    Write-Host "  $pass 条通过 / $($fails.Count) 条失败 / exit=$code"

    if ($code -ne 0 -or $fails.Count -gt 0) { $failed++ }
}

if ($failed -gt 0) {
    Write-Host ''
    Write-Host "$failed 套测试没过" -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '两套测试全绿' -ForegroundColor Green
exit 0
