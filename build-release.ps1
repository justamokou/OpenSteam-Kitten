<#
.SYNOPSIS
手动构建并（可选）发布 OpenSteam Kitten release（壳/小猫更新用）。

.DESCRIPTION
保证手动发版产物与 GitHub Action 自动发版布局完全一致：
zip 根含 OpenSteamKitten.exe、VERSION、version.json、Resources/dlls/{3 DLL + VERSION.txt}。

默认只本地构建出 zip + 打印发版命令；加 -Publish 才真正执行 gh release create。

.PARAMETER Version
壳（小猫）版本号，如 1.2.0。必填。

.PARAMETER CoreVersion
内核版本号，如 1.4.9。默认读取当前 Resources/dlls/VERSION.txt（纯壳更新通常不改内核）。

.PARAMETER Title
release 标题。默认 "v<Version> - 小猫更新"。

.PARAMETER Notes
release 说明（可多行）。

.PARAMETER Publish
开关：加上才会执行 gh release create（默认只本地构建，便于先测试）。

.EXAMPLE
.\build-release.ps1 -Version 1.2.0 -Notes "修复拖拽 bug"
.\build-release.ps1 -Version 1.2.0 -CoreVersion 1.4.9 -Publish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$CoreVersion,
    [string]$Title,
    [string]$Notes = "",
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

# 仓库根 = 脚本所在目录
$Root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

$VersionFile     = Join-Path $Root "VERSION"
$CoreVersionFile = Join-Path $Root "OpenSteamKitten\Resources\dlls\VERSION.txt"
$SrcVersionJson  = Join-Path $Root "OpenSteamKitten\version.json"
$Csproj          = Join-Path $Root "OpenSteamKitten\OpenSteamKitten.csproj"
$PublishDir      = Join-Path $Root "dist\publish"

if (-not $CoreVersion) {
    $CoreVersion = (Get-Content $CoreVersionFile -Raw).Trim()
}

Write-Host "==> 壳版本: $Version" -ForegroundColor Cyan
Write-Host "==> 内核版本: $CoreVersion" -ForegroundColor Cyan

# 1) 写版本文件 + version.json 源文件（build 会拷贝到 publish）
$manifest = "{`"shell`":`"$Version`",`"core`":`"$CoreVersion`"}"
Set-Content -Path $VersionFile     -Value $Version  -NoNewline -Encoding UTF8
Set-Content -Path $CoreVersionFile -Value $CoreVersion -NoNewline -Encoding UTF8
Set-Content -Path $SrcVersionJson  -Value $manifest -NoNewline -Encoding UTF8

# 2) 发布（lite，单文件，依赖 .NET 6 Desktop Runtime）—— 与 Action 一致
Write-Host "==> dotnet publish (lite, single-file)..." -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
dotnet publish $Csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }

# 3) 再次确保 publish/version.json 是最新版本（防御性，覆盖 build 拷贝的旧值）
Set-Content -Path (Join-Path $PublishDir "version.json") -Value $manifest -NoNewline -Encoding UTF8

# 4) 打包 zip（布局与 Action 完全一致）
$ZipName = "OpenSteamKitten-$Version-Release.zip"
$ZipPath = Join-Path $Root "dist\$ZipName"
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
Push-Location $PublishDir
try {
    Compress-Archive -Path "OpenSteamKitten.exe","VERSION","version.json","Resources" -DestinationPath $ZipPath -Force
} finally {
    Pop-Location
}

Write-Host "==> 已生成: $ZipPath" -ForegroundColor Green
Write-Host "==> publish 内容:" -ForegroundColor Cyan
Get-ChildItem $PublishDir -Recurse -File | ForEach-Object {
    Write-Host ("   " + $_.FullName.Substring($PublishDir.Length + 1))
}

# 5) 发版（默认只打印命令）
if (-not $Title) { $Title = "v$Version - 小猫更新" }
if ($Publish) {
    Write-Host "==> gh release create..." -ForegroundColor Cyan
    $ghArgs = @("release", "create", "v$Version", $ZipPath, (Join-Path $PublishDir "version.json"), "--title", $Title)
    if ($Notes) { $ghArgs += @("--notes", $Notes) }
    & gh @ghArgs
    if ($LASTEXITCODE -ne 0) { throw "gh release create 失败 (exit $LASTEXITCODE)" }
    Write-Host "==> 已发布 v$Version" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "==> 测试无误后，手动执行以下命令发版（或加 -Publish 开关一步到位）:" -ForegroundColor Yellow
    $cmd = "gh release create v$Version `"$ZipPath`" `"$($PublishDir)\version.json`" --title `"$Title`""
    if ($Notes) { $cmd += " --notes `"$Notes`"" }
    Write-Host "    $cmd" -ForegroundColor Yellow
}
