[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw '请使用 PowerShell 7 或更高版本运行此脚本。' }
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'

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
Write-Host "发布完成：$artifacts" -ForegroundColor Green
