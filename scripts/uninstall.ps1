[CmdletBinding()]
param([switch]$RemoveData)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw '请使用 PowerShell 7 或更高版本运行此脚本。' }

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($RemoveData) { $arguments += '-RemoveData' }
    $hostExecutable = (Get-Process -Id $PID).Path
    Start-Process -FilePath $hostExecutable -ArgumentList $arguments -Verb RunAs -Wait
    exit
}

$serviceName = 'YiShuHelper'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(40))
    }
    & sc.exe delete $serviceName | Out-Null
}

Get-Process -Name 'YiShuHelper.Tray' -ErrorAction SilentlyContinue | Stop-Process
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Startup')) '翼枢分流助手.lnk') -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'YiShuHelper' -Force -ErrorAction SilentlyContinue

$installDirectory = Join-Path $env:ProgramFiles 'YiShuHelper'
if ((Split-Path -Leaf $installDirectory) -eq 'YiShuHelper' -and (Test-Path -LiteralPath $installDirectory)) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

if ($RemoveData) {
    $dataDirectory = Join-Path $env:ProgramData 'YiShuHelper'
    if ((Split-Path -Leaf $dataDirectory) -eq 'YiShuHelper' -and (Test-Path -LiteralPath $dataDirectory)) {
        Remove-Item -LiteralPath $dataDirectory -Recurse -Force
    }
}

Write-Host '卸载完成。默认保留配置、状态和日志；使用 -RemoveData 可一并删除。' -ForegroundColor Green
