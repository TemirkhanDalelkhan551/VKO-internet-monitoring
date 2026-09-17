#Requires -RunAsAdministrator
param(
    [string]$SourceDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\agent-win-x64'),
    [string]$InstallDirectory = "$env:ProgramFiles\VkoInternetMonitoringAgent"
)

$ErrorActionPreference = 'Stop'
$serviceName = 'VkoInternetMonitoringAgent'
$agentExecutablePath = Join-Path $InstallDirectory 'VkoMonitoring.Agent.exe'

if ([Environment]::OSVersion.Version.Major -lt 10) {
    throw 'The agent requires Windows 10 or newer. Windows 7 and 8.1 are not supported by .NET 10.'
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'This package supports only 64-bit Windows. Build a win-x86 package for 32-bit computers.'
}

if (-not (Test-Path (Join-Path $SourceDirectory 'VkoMonitoring.Agent.exe'))) {
    throw "Published agent was not found. Run scripts\publish-agent.ps1 first."
}

$configurationPath = Join-Path $SourceDirectory 'appsettings.json'
$configuration = Get-Content -Path $configurationPath -Raw | ConvertFrom-Json
$plainDeviceToken = [string]$configuration.Agent.DeviceToken
$activationRequired = [string]::IsNullOrWhiteSpace($plainDeviceToken) -or $plainDeviceToken -like 'CHANGE_ME*'

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $SourceDirectory '*') -Destination $InstallDirectory -Recurse -Force

$dataDirectory = [Environment]::ExpandEnvironmentVariables([string]$configuration.Agent.DataDirectory)
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$tokenFile = Join-Path $dataDirectory 'device-token.dat'
if (-not $activationRequired) {
    Add-Type -AssemblyName System.Security
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
}

$configuration.Agent.DeviceToken = ''
$configuration.Agent | Add-Member `
    -NotePropertyName DeviceTokenFile `
    -NotePropertyValue $(if ($activationRequired) { '' } else { $tokenFile }) `
    -Force
$configuration |
    ConvertTo-Json -Depth 10 |
    Set-Content -Path (Join-Path $InstallDirectory 'appsettings.json') -Encoding UTF8

$serviceCommand = "`"$agentExecutablePath`""
$serviceStartMode = if ($activationRequired) { 'demand' } else { 'auto' }
sc.exe create $serviceName binPath= $serviceCommand start= $serviceStartMode DisplayName= "VKO Internet Monitoring Agent" | Out-Null
sc.exe description $serviceName "Autonomous internet quality monitoring agent for educational organizations." | Out-Null
sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

if ($activationRequired) {
    Write-Host "Agent installed. Run VkoMonitoring.Agent.Setup.exe as administrator to activate and start the service."
}
else {
    Start-Service -Name $serviceName
    Write-Host "Service $serviceName installed and started."
}
