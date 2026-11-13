<#
.SYNOPSIS
    上班工具箱：把 dist 里打好的 zip / exe 提交、推送到 GitHub，并发布成 Release。

.DESCRIPTION
    步骤：① 检查 dist\上班工具箱-v<Version>.zip 和 dist\WorkCountdown.exe 都存在，
           并校验 exe 的 FileVersion == <Version>.0.0（自动更新包必须是本次编译产物）
          ② git add -A；工作区有改动就 commit（"release: 上班工具箱 v<Version>"）
          ③ git push origin main（-SkipPush 可跳过）
          ④ 查 Release（tag=v<Version>）：存在就 PATCH 更新，不存在就 POST 创建
          ⑤ 先删同名的旧 zip / exe 资产，再上传两个资产：
               - 上班工具箱-v<Version>.zip（application/zip，完整发布包）
               - WorkCountdown.exe（application/octet-stream，自动更新用的全量 exe）
          ⑥ 打印 release 的 html_url 和两个资产的直链

    幂等：同一版本重复执行只会更新 Release / 替换资产，不会报「已存在」，也不会堆重复资产。
    失败：任意一步失败都 exit 1（package.ps1 会据此中止）。

.PARAMETER Version
    版本号，默认 3.6.0；决定 zip 名、exe 的期望 FileVersion（<Version> 后面补一个 .0，如 3.6.0 -> 3.6.0.0）、tag（v<Version>）。

.PARAMETER Notes
    Release 说明正文；不传就从 CHANGELOG.md 里抽对应版本的小节，再没有就用默认文案。

.PARAMETER SkipPush
    只 commit、不 push（tag / Release 仍会用当前远端 main 的 HEAD）。

.PARAMETER DryRun
    只打印将要执行的 git 命令 / API 调用，不 commit、不 push、不调 API 写数据。
    （DryRun 下 exe 版本不一致只警告，方便先预览；真实发布会直接 exit 1。）

.EXAMPLE
    .\tools\release.ps1 -Version 3.6.0 -DryRun

.EXAMPLE
    .\tools\release.ps1 -Version 3.6.0

.NOTES
    token 来源：$env:GITHUB_TOKEN，或 git credential fill（host=github.com）里缓存的 PAT。
    本文件必须保持 UTF-8 BOM（PowerShell 5.1 靠 BOM 保住中文字符串）。
#>
param(
    [string]$Version = '3.6.0',
    [string]$Repo = 'ocezeus/vibeZheyikuai',
    [string]$Branch = 'main',
    [string]$Notes = '',
    [switch]$SkipPush,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dist = Join-Path $root 'dist'
$zipName = "上班工具箱-v$Version.zip"
$zip = Join-Path $dist $zipName
$exeName = 'WorkCountdown.exe'
$exe = Join-Path $dist $exeName
$tag = "v$Version"
$releaseName = "上班工具箱 v$Version"
$expectedFileVersion = "$($Version).0"
$api = 'https://api.github.com'
$script:Headers = $null

function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Step([string]$text) { Write-Host ''; Write-Host $text -ForegroundColor Cyan }
function Plan([string]$text) { Write-Host "  [DryRun] 将执行：$text" -ForegroundColor Yellow }
function Invoke-Git { & git -C $root @args }

# ---------- token / API ----------
function Get-GitHubToken {
    if ($env:GITHUB_TOKEN -and $env:GITHUB_TOKEN.Trim()) { return $env:GITHUB_TOKEN.Trim() }
    # 本机凭据管理器里 git push 用过的 PAT（owner=ocezeus，40 字符）
    $old = $env:GIT_TERMINAL_PROMPT
    $env:GIT_TERMINAL_PROMPT = '0'
    try {
        $cred = ("protocol=https`r`nhost=github.com`r`n`r`n" | & git credential fill 2>$null) -join "`n"
    } finally {
        if ($null -eq $old) { Remove-Item Env:GIT_TERMINAL_PROMPT -ErrorAction SilentlyContinue }
        else { $env:GIT_TERMINAL_PROMPT = $old }
    }
    $m = [regex]::Match($cred, 'password=(.+)')
    if ($m.Success -and $m.Groups[1].Value.Trim()) { return $m.Groups[1].Value.Trim() }
    throw '取不到 GitHub token：请设置 $env:GITHUB_TOKEN，或先手动 git push 一次让凭据管理器记住 PAT'
}

function Invoke-GitHub([string]$Method, [string]$Path, $Body = $null) {
    $p = @{
        Uri         = "$api$Path"
        Method      = $Method
        Headers     = $script:Headers
        TimeoutSec  = 180
        ErrorAction = 'Stop'
    }
    if ($null -ne $Body) {
        # 显式转成 UTF-8 字节，避免 PS 5.1 按 ANSI 编码提交中文 body 变成乱码
        $json = ConvertTo-Json -InputObject $Body -Depth 6 -Compress
        $p['Body'] = [System.Text.Encoding]::UTF8.GetBytes($json)
        $p['ContentType'] = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @p
}

function Get-HttpStatus($err) {
    try { return [int]$err.Exception.Response.StatusCode } catch { return 0 }
}

# ---------- Release 说明 ----------
function Get-ChangelogSection([string]$ver) {
    $f = Join-Path $root 'CHANGELOG.md'
    if (-not (Test-Path -LiteralPath $f)) { return '' }
    $text = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
    $pattern = '(?ms)^###[^\n]*v' + [regex]::Escape($ver) + '(?![\d.])[^\n]*\n(.*?)(?=^### |\z)'
    $m = [regex]::Match($text, $pattern)
    if (-not $m.Success) { return '' }
    return $m.Groups[1].Value.Trim()
}

# ==================== 主流程 ====================
try {
    Write-Host ''
    Write-Host "=== 上班工具箱 发布 v$Version ===" -ForegroundColor Cyan
    Write-Host "  仓库：$Repo   分支：$Branch   tag：$tag"
    Write-Host "  发布包：dist\$zipName"
    Write-Host "  自动更新 exe：dist\$exeName（要求 FileVersion $expectedFileVersion）"
    if ($DryRun) { Write-Host '  模式：-DryRun（只打印，不 commit / 不 push / 不调写 API）' -ForegroundColor Yellow }

    # ---------- ① 检查发布资产 ----------
    Step '[1/6] 检查发布资产（zip + 自动更新 exe）'
    if (-not (Test-Path -LiteralPath $zip)) {
        throw "找不到发布包：$zip`n先跑 .\package.ps1 -Version $Version（或用 -Version 指定已有 zip 的版本）"
    }
    $zipItem = Get-Item -LiteralPath $zip
    if ($zipItem.Length -le 0) { throw "发布包是空文件：$zip" }
    $zipKB = [math]::Round($zipItem.Length / 1KB, 1)
    Say "  OK  $zip" 'Green'
    Say "      大小 $($zipItem.Length) B / $zipKB KB"

    if (-not (Test-Path -LiteralPath $exe)) {
        throw "找不到自动更新用的 exe：$exe`n先跑 .\build.ps1（或 .\package.ps1）用 v$Version 的源码编译出 dist\$exeName"
    }
    $exeItem = Get-Item -LiteralPath $exe
    if ($exeItem.Length -le 0) { throw "exe 是空文件：$exe" }
    $exeKB = [math]::Round($exeItem.Length / 1KB, 1)
    $fv = $null
    try { $fv = $exeItem.VersionInfo.FileVersionRaw } catch { }
    if (-not $fv) { $fv = $exeItem.VersionInfo.FileVersion }
    $fvText = ("$fv").Trim()
    if (-not $fvText) { $fvText = '(读不到)' }
    if ($fvText -ne $expectedFileVersion) {
        $msg = "dist\$exeName 的 FileVersion 是 $fvText，和本次版本 $expectedFileVersion 不一致；先用 v$Version 的源码重编（.\build.ps1 或 .\package.ps1），别拿旧 exe 发版"
        if ($DryRun) { Say "  [DryRun] 警告：$msg（DryRun 继续，真实发布会 exit 1）" 'Yellow' }
        else { throw $msg }
    } else {
        Say "  OK  $exe" 'Green'
        Say "      大小 $($exeItem.Length) B / $exeKB KB  FileVersion=$fvText"
    }

    # 本次要上传的资产（顺序 = 上传顺序；同名旧资产会先删）
    $uploadList = @(
        @{ Path = $zip; Name = $zipName; Label = '完整压缩包（含说明书/安装脚本/字体说明）'; ContentType = 'application/zip'; SizeKB = $zipKB },
        @{ Path = $exe; Name = $exeName; Label = '自动更新用全量 exe'; ContentType = 'application/octet-stream'; SizeKB = $exeKB }
    )
    Say '  本次会上传 2 个资产：'
    foreach ($u in $uploadList) {
        Say ("    - {0}  {1}  {2} KB  ->  {3}" -f $u.Name, $u.ContentType, $u.SizeKB, $u.Label) 'DarkGray'
    }

    # ---------- ② git add + commit ----------
    Step '[2/6] 提交本地改动（git add -A + commit）'
    $branchNow = (Invoke-Git rev-parse --abbrev-ref HEAD | Out-String).Trim()
    Say "  当前分支：$branchNow"
    if ($branchNow -ne $Branch) { Say "  注意：当前分支不是 $Branch，push 仍会推到 origin $Branch" 'Yellow' }
    $pending = @(Invoke-Git status --porcelain | Where-Object { $_ })

    if ($DryRun) {
        Plan 'git add -A'
        if ($pending.Count -gt 0) {
            Plan "git commit -F <临时文件>（消息：release: 上班工具箱 v$Version）"
            Say "  当前有 $($pending.Count) 个文件改动（前 10 个）：" 'Yellow'
            $pending | Select-Object -First 10 | ForEach-Object { Say "    $_" 'DarkGray' }
        } else {
            Say '  当前工作区干净，真实执行时会跳过 commit'
        }
    } else {
        Invoke-Git add -A
        if ($LASTEXITCODE -ne 0) { throw 'git add -A 失败' }
        $pending = @(Invoke-Git status --porcelain | Where-Object { $_ })
        if ($pending.Count -gt 0) {
            $msgFile = Join-Path ([System.IO.Path]::GetTempPath()) ("workcountdown-release-" + [guid]::NewGuid().ToString('N') + '.txt')
            $msg = "release: 上班工具箱 v$Version`n`n发布包：dist\$zipName`n自动更新 exe：dist\$exeName`n"
            [System.IO.File]::WriteAllText($msgFile, $msg, (New-Object System.Text.UTF8Encoding($false)))
            try {
                Invoke-Git commit -F $msgFile
                if ($LASTEXITCODE -ne 0) { throw 'git commit 失败' }
            } finally {
                Remove-Item -LiteralPath $msgFile -Force -ErrorAction SilentlyContinue
            }
            $short = (Invoke-Git rev-parse --short HEAD | Out-String).Trim()
            Say "  已提交 $($pending.Count) 个文件：$short" 'Green'
        } else {
            Say '  没有需要提交的改动，跳过 commit'
        }
    }

    # ---------- ③ push ----------
    Step '[3/6] 推送代码到 GitHub'
    if ($SkipPush) {
        Say '  -SkipPush：跳过 git push（Release 的 tag 会落在远端 main 当前 HEAD）' 'Yellow'
    } elseif ($DryRun) {
        Plan "git push origin $Branch"
        Plan "git tag $tag（本地没有就建）+ git push origin refs/tags/$tag（远端没有就推）"
    } else {
        Invoke-Git push origin $Branch
        if ($LASTEXITCODE -ne 0) { throw "git push origin $Branch 失败（检查网络 / 凭据）" }
        Say "  已推送 origin $Branch" 'Green'

        # tag：Release 用 tag 定位。GitHub 也能自动建 tag，这里显式建一下更可控；失败不致命。
        try {
            $remoteTag = (& git -C $root ls-remote --tags origin "refs/tags/$tag" 2>$null | Out-String).Trim()
            if ($remoteTag) {
                Say "  远端已有 tag $tag，跳过建 tag"
            } else {
                $localTag = (& git -C $root tag --list $tag | Out-String).Trim()
                if (-not $localTag) {
                    Invoke-Git tag -a $tag -m "WorkCountdown $tag"
                    if ($LASTEXITCODE -ne 0) { throw "git tag $tag 失败" }
                }
                Invoke-Git push origin "refs/tags/$tag"
                if ($LASTEXITCODE -ne 0) { throw "git push tag $tag 失败" }
                Say "  已建并推送 tag $tag" 'Green'
            }
        } catch {
            Say "  tag 处理失败：$($_.Exception.Message)（Release 仍会用 tag_name 自动建 tag，继续）" 'Yellow'
        }
    }

    # ---------- ④ Release 说明 ----------
    Step '[4/6] 准备 Release 说明'
    $defaultBody = "**上班工具箱 v$Version**（Windows，只需系统自带的 .NET Framework 4.x）`n`n下载下面的 ``$zipName``，解压后双击「安装.cmd」。`n`n- ``$zipName``：完整发布包（含一图说明书 / 安装脚本 / 字体说明）`n- ``$exeName``：自动更新用的全量 exe，程序「检查更新」会下载它并替换自身`n`n也可以用便携版：把 WorkCountdown.exe 放到任意文件夹，同目录建一个空的 ``portable.txt``。"
    if ($Notes -and $Notes.Trim()) {
        $body = $Notes.Trim()
        Say '  来源：-Notes 参数'
    } else {
        $section = Get-ChangelogSection $Version
        if ($section) {
            $body = "**上班工具箱 v$Version** · Windows（.NET Framework 4.x 自带即可运行）`n`n下载 ``$zipName``，解压后双击「安装.cmd」。`n`n程序里「检查更新」会自动下载 ``$exeName`` 并替换自身。`n`n---`n`n## 本次更新`n`n$section"
            Say '  来源：CHANGELOG.md 里对应版本的小节'
        } else {
            $body = $defaultBody
            Say "  来源：默认文案（CHANGELOG.md 里没找到 v$Version 小节）" 'Yellow'
        }
    }
    if ($body.Length -gt 120000) {
        $body = $body.Substring(0, 120000) + "`n`n…（太长截断，完整内容见 CHANGELOG.md）"
        Say '  body 超过 120000 字符，已截断' 'Yellow'
    }
    Say "  正文 $($body.Length) 字符"
    if ($DryRun) {
        $preview = $body
        if ($preview.Length -gt 1500) { $preview = $preview.Substring(0, 1500) + "`n...（截断预览）" }
        Say '  ---- body 预览 ----' 'DarkGray'
        Write-Host $preview
        Say '  -------------------' 'DarkGray'
    }

    # ---------- ⑤ Release + 资产 ----------
    Step '[5/6] 创建 / 更新 GitHub Release，并替换 zip / exe 两个资产'
    $uploaded = @()
    if ($DryRun) {
        # 只验证凭据能不能取到，不调 API
        try {
            $tok = Get-GitHubToken
            Write-Host "  GitHub 凭据：已取到（长度 $($tok.Length)）" -ForegroundColor Green
        } catch {
            Write-Host "  GitHub 凭据：没取到 —— $($_.Exception.Message)" -ForegroundColor Yellow
        }
        Plan "GET  $api/repos/$Repo/releases/tags/$tag   （200=已存在走 PATCH，404=新建）"
        Plan "     已存在：PATCH $api/repos/$Repo/releases/<id>   （name/tag/body 重写）"
        Plan "     不存在：POST  $api/repos/$Repo/releases   （tag_name=$tag, target_commitish=$Branch, name=$releaseName, draft=false, prerelease=false）"
        Plan "GET  $api/repos/$Repo/releases/<id>/assets"
        Plan "     先删同名旧资产：$zipName（存在就 DELETE）"
        Plan "     先删同名旧资产：$exeName（存在就 DELETE）"
        Plan "POST $api/repos/$Repo/releases/<id>/assets?name=$zipName   （application/zip, $zipKB KB, -InFile $zip）"
        Plan "POST $api/repos/$Repo/releases/<id>/assets?name=$exeName&label=自动更新用全量 exe   （application/octet-stream, $exeKB KB, -InFile $exe）"
        Say '  [DryRun] 以上 API 调用都没有真正执行（2 个资产都没上传）' 'Yellow'
    } else {
        $tok = Get-GitHubToken
        $script:Headers = @{
            'Authorization' = "token $tok"
            'User-Agent'    = 'WorkCountdown-release'
            'Accept'        = 'application/vnd.github+json'
        }
        Say "  GitHub 凭据：已取到（长度 $($tok.Length)）"

        $rel = $null
        try {
            $rel = Invoke-GitHub -Method Get -Path "/repos/$Repo/releases/tags/$tag"
        } catch {
            if ((Get-HttpStatus $_) -eq 404) { $rel = $null }
            else { throw "查询 Release 失败：$($_.Exception.Message)" }
        }

        if ($rel) {
            Say "  已存在 Release id=$($rel.id)，PATCH 更新 name / tag / body" 'Yellow'
            $rel = Invoke-GitHub -Method Patch -Path "/repos/$Repo/releases/$($rel.id)" -Body @{
                name       = $releaseName
                tag_name   = $tag
                body       = $body
                draft      = $false
                prerelease = $false
            }
        } else {
            Say '  没有这个 tag 的 Release，POST 新建'
            $rel = Invoke-GitHub -Method Post -Path "/repos/$Repo/releases" -Body @{
                tag_name         = $tag
                target_commitish = $Branch
                name             = $releaseName
                body             = $body
                draft            = $false
                prerelease       = $false
            }
        }
        if (-not $rel -or -not $rel.id) { throw '创建 / 更新 Release 后没拿到 id' }
        $relId = $rel.id
        Say "  Release id=$relId  $($rel.html_url)" 'Green'

        # 删同名旧资产（幂等：重复发同一版本不会堆两个同名资产）
        $existingAssets = Invoke-GitHub -Method Get -Path "/repos/$Repo/releases/$relId/assets"
        foreach ($u in $uploadList) {
            foreach ($a in @($existingAssets)) {
                if ($a -and $a.name -eq $u.Name) {
                    Say "  删除同名旧资产：$($a.name)  id=$($a.id)" 'Yellow'
                    Invoke-GitHub -Method Delete -Path "/repos/$Repo/releases/assets/$($a.id)" | Out-Null
                }
            }
        }

        # 上传两个资产：完整 zip + 自动更新用 exe
        # 注意：资产上传必须走 Release 的 upload_url（uploads.github.com 主机）；直接打
        # api.github.com/repos/.../releases/<id>/assets 会 404（本轮实测踩过）。
        $uploadBase = ($rel.upload_url -replace '\{\?[^}]*\}', '')
        if (-not $uploadBase) { $uploadBase = "https://uploads.github.com/repos/$Repo/releases/$relId/assets" }
        foreach ($u in $uploadList) {
            $uploadUri = $uploadBase + "?name=" + [uri]::EscapeDataString($u.Name) + "&label=" + [uri]::EscapeDataString($u.Label)
            Say "  上传资产：$($u.Name)  ($($u.SizeKB) KB, $($u.ContentType)) …"
            $asset = Invoke-RestMethod -Uri $uploadUri -Method Post -Headers $script:Headers `
                -ContentType $u.ContentType -InFile $u.Path -TimeoutSec 600 -ErrorAction Stop
            if (-not $asset -or -not $asset.id) { throw "上传资产失败：$($u.Name)（没拿到 asset id）" }
            Say "  已上传：$($asset.name)  $($asset.browser_download_url)" 'Green'
            $uploaded += $asset
        }
        if ($uploaded.Count -ne 2) { throw "应该有 2 个资产，实际上传成功 $($uploaded.Count) 个" }
    }

    # ---------- ⑥ 完成 ----------
    Step '[6/6] 完成'
    if ($DryRun) {
        Say '  -DryRun 结束：没有 commit、没有 push、没有调 GitHub 写接口，2 个资产都没上传。' 'Yellow'
        Say '  去掉 -DryRun 就会真的发布（前提：凭据可用 + dist 里的 zip/exe 都是 package.ps1 本次新出的）。'
    } else {
        Say "  Release 页面：$($rel.html_url)" 'Green'
        foreach ($a in $uploaded) {
            Say "  资产：$($a.name)  ($([math]::Round($a.size / 1KB, 1)) KB)  ->  $($a.browser_download_url)" 'Green'
        }
    }
} catch {
    Write-Host ''
    Say "发布失败：$($_.Exception.Message)" 'Red'
    if ($_.ScriptStackTrace) { Say $_.ScriptStackTrace 'DarkGray' }
    exit 1
}
