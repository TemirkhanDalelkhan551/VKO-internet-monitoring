#Requires -RunAsAdministrator
param(
    [string]$InstallDirectory = "$env:ProgramFiles\VkoInternetMonitoringAgent"
)

$ErrorActionPreference = 'Stop'
$serviceName = 'VkoInternetMonitoringAgent'
$configurationPath = Join-Path $InstallDirectory 'appsettings.json'

if (-not (Test-Path $configurationPath)) {
    throw "Installed configuration was not found at $configurationPath."
}

$configuration = Get-Content -Path $configurationPath -Raw | ConvertFrom-Json
$plainDeviceToken = [string]$configuration.Agent.DeviceToken
if ([string]::IsNullOrWhiteSpace($plainDeviceToken)) {
    throw 'The installed configuration does not contain a plain device token to protect.'
}
Add-Type -AssemblyName System.Security

$service = Get-Service -Name $serviceName -ErrorAction Stop
if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

$dataDirectory = [Environment]::ExpandEnvironmentVariables([string]$configuration.Agent.DataDirectory)
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$tokenFile = Join-Path $dataDirectory 'device-token.dat'
$protectedToken = [System.Security.Cryptography.ProtectedData]::Protect(
    [System.Text.Encoding]::UTF8.GetBytes($plainDeviceToken),
    $null,
    [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
[System.IO.File]::WriteAllText(
    $tokenFile,
    [Convert]::ToBase64String($protectedToken),
    [System.Text.UTF8Encoding]::new($false))
& icacls.exe $tokenFile '/inheritance:r' '/grant:r' '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Failed to restrict access to $tokenFile."
}

$configuration.Agent.DeviceToken = ''
$configuration.Agent | Add-Member -NotePropertyName DeviceTokenFile -NotePropertyValue $tokenFile -Force
$configuration |
    ConvertTo-Json -Depth 10 |
    Set-Content -Path $configurationPath -Encoding UTF8

Start-Service -Name $serviceName
$service = Get-Service -Name $serviceName
$service.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))

Write-Host "Device token protected with Windows DPAPI and service $serviceName restarted."
