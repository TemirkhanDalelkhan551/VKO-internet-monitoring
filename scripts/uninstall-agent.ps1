#Requires -RunAsAdministrator
param(
    [string]$InstallDirectory = "$env:ProgramFiles\VkoInternetMonitoringAgent"
)

$ErrorActionPreference = 'Stop'
$serviceName = 'VkoInternetMonitoringAgent'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}

Write-Host "Service $serviceName removed. Data in ProgramData was preserved."
