<#
.SYNOPSIS
手动构建并（可选）发布 OpenSteam Kitten release（壳/小猫更新用）。

.DESCRIPTION
保证手动发版产物与 GitHub Action 自动发版布局一致：
zip 根含 OpenSteamKitten.exe、VERSION、version.json、Resources/dlls/{3 DLL + VERSION.txt}。

发布到 GitHub 的 version.json 是权威更新清单，包含 zip 与关键文件的 SHA256/size。
默认只本地构建出 zip + 打印发版命令；加 -Publish 才真正执行 gh release create。

.PARAMETER Version
壳（小猫）版本号，如 1.4.1。必填。

.PARAMETER CoreVersion
内核版本号，如 1.4.9。默认读取当前 Resources/dlls/VERSION.txt（纯壳更新通常不改内核）。

.PARAMETER Title
release 标题。默认 "v<Version> - 小猫更新"。

.PARAMETER Notes
release 说明（可多行）。

.PARAMETER Publish
开关：加上才会执行 gh release create（默认只本地构建，便于先测试）。

.EXAMPLE
.\build-release.ps1 -Version 1.4.1 -Notes "修复拖拽 bug"
.\build-release.ps1 -Version 1.4.1 -CoreVersion 1.4.9 -Publish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [Alias("Version")]
    [string]$ShellVersion,
    [string]$CoreVersion,
    [string]$Title,
    [string]$Notes = "",
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

$Root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

$VersionFile     = Join-Path $Root "VERSION"
$CoreVersionFile = Join-Path $Root "OpenSteamKitten\Resources\dlls\VERSION.txt"
$SrcVersionJson  = Join-Path $Root "OpenSteamKitten\version.json"
$Csproj          = Join-Path $Root "OpenSteamKitten\OpenSteamKitten.csproj"
$PublishDir      = Join-Path $Root "dist\publish"
$CoreDllNames    = @("OpenSteamTool.dll", "dwmapi.dll", "xinput1_4.dll")
$Utf8NoBom       = New-Object System.Text.UTF8Encoding($false)

function Set-TextFileNoBom {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Value
    )

    [System.IO.File]::WriteAllText($Path, $Value, $Utf8NoBom)
}

function Get-FileDigestObject {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path
    $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256
    return [ordered]@{
        sha256 = $hash.Hash.ToLowerInvariant()
        size = [int64]$item.Length
    }
}

function Add-ManifestFile {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Files,
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$ActualPath
    )

    if (Test-Path -LiteralPath $ActualPath) {
        $Files[$RelativePath.Replace("\", "/")] = Get-FileDigestObject -Path $ActualPath
    }
}

function New-VersionManifestJson {
    param(
        [Parameter(Mandatory)][string]$ShellVersion,
        [Parameter(Mandatory)][string]$KernelVersion,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Files,
        [string]$PackageName,
        [string]$PackagePath
    )

    $package = $null
    if ($PackageName -and $PackagePath -and (Test-Path -LiteralPath $PackagePath)) {
        $digest = Get-FileDigestObject -Path $PackagePath
        $package = [ordered]@{
            name = $PackageName
            sha256 = $digest.sha256
            size = $digest.size
        }
    }

    $manifest = [ordered]@{
        schema = 1
        shell = $ShellVersion
        core = $KernelVersion
        package = $package
        files = $Files
    }

    return ($manifest | ConvertTo-Json -Depth 8)
}

function New-SourceManifestJson {
    param(
        [Parameter(Mandatory)][string]$ShellVersion,
        [Parameter(Mandatory)][string]$KernelVersion
    )

    $files = [ordered]@{}
    Add-ManifestFile -Files $files -RelativePath "VERSION" -ActualPath $VersionFile
    foreach ($dll in $CoreDllNames) {
        Add-ManifestFile -Files $files -RelativePath "Resources/dlls/$dll" -ActualPath (Join-Path $Root "OpenSteamKitten\Resources\dlls\$dll")
    }
    Add-ManifestFile -Files $files -RelativePath "Resources/dlls/VERSION.txt" -ActualPath $CoreVersionFile

    return New-VersionManifestJson -ShellVersion $ShellVersion -KernelVersion $KernelVersion -Files $files
}

function New-PublishManifestJson {
    param(
        [Parameter(Mandatory)][string]$ShellVersion,
        [Parameter(Mandatory)][string]$KernelVersion,
        [string]$PackageName,
        [string]$PackagePath
    )

    $files = [ordered]@{}
    Add-ManifestFile -Files $files -RelativePath "OpenSteamKitten.exe" -ActualPath (Join-Path $PublishDir "OpenSteamKitten.exe")
    Add-ManifestFile -Files $files -RelativePath "VERSION" -ActualPath (Join-Path $PublishDir "VERSION")
    foreach ($dll in $CoreDllNames) {
        Add-ManifestFile -Files $files -RelativePath "Resources/dlls/$dll" -ActualPath (Join-Path $PublishDir "Resources\dlls\$dll")
    }
    Add-ManifestFile -Files $files -RelativePath "Resources/dlls/VERSION.txt" -ActualPath (Join-Path $PublishDir "Resources\dlls\VERSION.txt")

    return New-VersionManifestJson -ShellVersion $ShellVersion -KernelVersion $KernelVersion -Files $files -PackageName $PackageName -PackagePath $PackagePath
}

if (-not $CoreVersion) {
    $CoreVersion = (Get-Content $CoreVersionFile -Raw).Trim()
}

Write-Host "==> 壳版本: $ShellVersion" -ForegroundColor Cyan
Write-Host "==> 内核版本: $CoreVersion" -ForegroundColor Cyan

# 1) 写版本文件 + 源码清单（build 会拷贝到 publish，稍后用发布清单覆盖）
Set-TextFileNoBom -Path $VersionFile     -Value $ShellVersion
Set-TextFileNoBom -Path $CoreVersionFile -Value $CoreVersion
Set-TextFileNoBom -Path $SrcVersionJson  -Value (New-SourceManifestJson -ShellVersion $ShellVersion -KernelVersion $CoreVersion)

# 2) 发布（lite，单文件，依赖 .NET 6 Desktop Runtime）—— 与 Action 一致
Write-Host "==> dotnet publish (lite, single-file)..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
dotnet publish $Csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }

# 3) 写入发布包内部清单（包含 exe/dll hash；zip hash 需要打包后才能生成）
$ZipName = "OpenSteamKitten-${ShellVersion}-Release.zip"
$ZipPath = Join-Path $Root "dist\$ZipName"
Set-TextFileNoBom -Path (Join-Path $PublishDir "version.json") -Value (New-PublishManifestJson -ShellVersion $ShellVersion -KernelVersion $CoreVersion)

# 4) 打包 zip（布局与 Action 完全一致）
if ([System.IO.File]::Exists($ZipPath)) { [System.IO.File]::Delete($ZipPath) }
Push-Location $PublishDir
try {
    Compress-Archive -Path "OpenSteamKitten.exe","VERSION","version.json","Resources" -DestinationPath $ZipPath -Force
} finally {
    Pop-Location
}

# 5) 打包后生成权威 release 清单（额外包含 zip hash），作为独立资产上传
Set-TextFileNoBom -Path (Join-Path $PublishDir "version.json") -Value (New-PublishManifestJson -ShellVersion $ShellVersion -KernelVersion $CoreVersion -PackageName $ZipName -PackagePath $ZipPath)

Write-Host "==> 已生成: $ZipPath" -ForegroundColor Green
Write-Host "==> publish 内容:" -ForegroundColor Cyan
Get-ChildItem $PublishDir -Recurse -File | ForEach-Object {
    Write-Host ("   " + $_.FullName.Substring($PublishDir.Length + 1))
}

# 6) 发版（默认只打印命令）
if (-not $Title) { $Title = "v$ShellVersion - 小猫更新" }
if ($Publish) {
    Write-Host "==> gh release create..." -ForegroundColor Cyan
    $ghArgs = @("release", "create", "v$ShellVersion", $ZipPath, (Join-Path $PublishDir "version.json"), "--title", $Title)
    if ($Notes) { $ghArgs += @("--notes", $Notes) }
    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create 失败 (exit $LASTEXITCODE)" }
    Write-Host "==> 已发布 v$ShellVersion" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "==> 测试无误后，手动执行以下命令发版（或加 -Publish 开关一步到位）:" -ForegroundColor Yellow
    $cmd = "gh release create v$ShellVersion `"$ZipPath`" `"$($PublishDir)\version.json`" --title `"$Title`""
    if ($Notes) { $cmd += " --notes `"$Notes`"" }
    Write-Host "    $cmd" -ForegroundColor Yellow
}
