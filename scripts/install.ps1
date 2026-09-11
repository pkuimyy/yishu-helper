[CmdletBinding()]
param(
    [string]$ArtifactsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw '请使用 PowerShell 7 或更高版本运行此脚本。' }

if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $coLocatedArtifacts = $PSScriptRoot
    $repositoryArtifacts = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'
    if (Test-Path -LiteralPath (Join-Path $coLocatedArtifacts 'service')) {
        $ArtifactsDirectory = $coLocatedArtifacts
    } else {
        $ArtifactsDirectory = $repositoryArtifacts
    }
}
$ArtifactsDirectory = [IO.Path]::GetFullPath($ArtifactsDirectory)

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-ArtifactsDirectory', ('"{0}"' -f $ArtifactsDirectory)
    )
    $hostExecutable = (Get-Process -Id $PID).Path
    $elevatedProcess = Start-Process -FilePath $hostExecutable -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    exit $elevatedProcess.ExitCode
}

$serviceName = 'YiShuHelper'
$installDirectory = Join-Path $env:ProgramFiles 'YiShuHelper'
$dataDirectory = Join-Path $env:ProgramData 'YiShuHelper'
$serviceSource = Join-Path $ArtifactsDirectory 'service'
$traySource = Join-Path $ArtifactsDirectory 'tray'
$configurationSource = Join-Path $ArtifactsDirectory 'yishu-split-config.json'

foreach ($path in @($serviceSource, $traySource, $configurationSource)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺少发布产物：$path" }
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(40))
    }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

New-Item -ItemType Directory -Path (Join-Path $installDirectory 'service'),(Join-Path $installDirectory 'tray'),$dataDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $serviceSource '*') -Destination (Join-Path $installDirectory 'service') -Recurse -Force
Copy-Item -Path (Join-Path $traySource '*') -Destination (Join-Path $installDirectory 'tray') -Recurse -Force
if (-not (Test-Path -LiteralPath (Join-Path $dataDirectory 'yishu-split-config.json'))) {
    Copy-Item -LiteralPath $configurationSource -Destination (Join-Path $dataDirectory 'yishu-split-config.json')
}

$account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls.exe $dataDirectory /grant "${account}:(OI)(CI)M" /T /Q | Out-Null

$serviceExecutable = Join-Path $installDirectory 'service\YiShuHelper.Service.exe'
New-Service -Name $serviceName -BinaryPathName ('"{0}"' -f $serviceExecutable) `
    -DisplayName '翼枢分流助手' -Description '持续维护翼枢 WireGuard 窄分流。' -StartupType Automatic | Out-Null
& sc.exe config $serviceName start= delayed-auto | Out-Null
if ($LASTEXITCODE -ne 0) { throw '设置服务延迟启动失败。' }
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/none/0 | Out-Null
if ($LASTEXITCODE -ne 0) { throw '设置服务恢复策略失败。' }

Start-Service -Name $serviceName
$trayExecutable = Join-Path $installDirectory 'tray\YiShuHelper.Tray.exe'
Write-Host "安装完成。应用安装目录：$installDirectory" -ForegroundColor Green
Write-Host "后台服务已启动。如需启用托盘，请手动运行：$trayExecutable" -ForegroundColor Yellow
