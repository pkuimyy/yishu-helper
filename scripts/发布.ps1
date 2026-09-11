[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw '请使用 PowerShell 7 或更高版本运行此脚本。' }
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$packageName = 'YiShuHelper-win-x64'
$packageDirectory = Join-Path $artifacts $packageName
$archivePath = Join-Path $artifacts "$packageName.zip"
$checksumPath = "$archivePath.sha256"

foreach ($path in @(
    (Join-Path $artifacts 'service'),
    (Join-Path $artifacts 'tray'),
    $packageDirectory
)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
foreach ($path in @($archivePath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

dotnet build (Join-Path $root 'YiShuHelper.slnx') -c Release
if ($LASTEXITCODE -ne 0) { throw '解决方案构建失败。' }

dotnet run --project (Join-Path $root 'tests\YiShuHelper.SelfTests\YiShuHelper.SelfTests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw '离线自检失败。' }

dotnet publish (Join-Path $root 'src\YiShuHelper.Service\YiShuHelper.Service.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -o (Join-Path $artifacts 'service')
if ($LASTEXITCODE -ne 0) { throw '后台服务发布失败。' }

dotnet publish (Join-Path $root 'src\YiShuHelper.Tray\YiShuHelper.Tray.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -o (Join-Path $artifacts 'tray')
if ($LASTEXITCODE -ne 0) { throw '托盘程序发布失败。' }

Copy-Item -LiteralPath (Join-Path $root 'config\yishu-split-config.example.json') `
    -Destination (Join-Path $artifacts 'yishu-split-config.json') -Force

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $artifacts 'service') -Destination $packageDirectory -Recurse
Copy-Item -LiteralPath (Join-Path $artifacts 'tray') -Destination $packageDirectory -Recurse
Copy-Item -LiteralPath (Join-Path $artifacts 'yishu-split-config.json') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $root 'scripts\install.ps1') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $root 'scripts\uninstall.ps1') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $root 'scripts\install.cmd') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $root 'scripts\uninstall.cmd') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $root 'README.zh-CN.md') -Destination $packageDirectory

Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$archiveHash  $packageName.zip" -Encoding utf8NoBOM

Write-Host "发布完成：$archivePath" -ForegroundColor Green
