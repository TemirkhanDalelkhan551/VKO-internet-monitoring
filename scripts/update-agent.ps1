#Requires -RunAsAdministrator
param(
    [string]$SourceDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\agent-win-x64'),
    [string]$InstallDirectory = "$env:ProgramFiles\VkoInternetMonitoringAgent"
)

$ErrorActionPreference = 'Stop'
$serviceName = 'VkoInternetMonitoringAgent'
$sourceExecutable = Join-Path $SourceDirectory 'VkoMonitoring.Agent.exe'
$installedConfiguration = Join-Path $InstallDirectory 'appsettings.json'

if (-not (Test-Path $sourceExecutable)) {
    throw "Published agent was not found at $sourceExecutable. Run scripts\publish-agent.ps1 first."
}

if (-not (Test-Path $installedConfiguration)) {
    throw "Installed configuration was not found at $installedConfiguration. Use install-agent.ps1 for the first installation."
}

$service = Get-Service -Name $serviceName -ErrorAction Stop
if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

Get-ChildItem -Path $SourceDirectory |
    Where-Object { $_.Name -ne 'appsettings.json' } |
    Copy-Item -Destination $InstallDirectory -Recurse -Force

Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))

Write-Host "Service $serviceName updated and started. Existing configuration was preserved."
