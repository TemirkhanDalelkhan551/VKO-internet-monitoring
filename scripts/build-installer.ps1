param(
    [switch]$Sign,
    [string]$CertificateThumbprint,
    [string]$PfxPath,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$definitionPath = Join-Path $projectRoot 'installer\VkoInternetMonitoringAgent.iss'
$publishedAgent = Join-Path $projectRoot 'artifacts\agent-win-x64\VkoMonitoring.Agent.exe'
$compilerCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$compilerPath = $compilerCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1

if (-not (Test-Path -LiteralPath $publishedAgent)) {
    throw 'Published agent was not found. Run scripts\publish-agent.ps1 first.'
}

if (-not $compilerPath) {
    throw 'Inno Setup 6 was not found. Install JRSoftware.InnoSetup with winget.'
}

if ($Sign) {
    & (Join-Path $PSScriptRoot 'sign-release.ps1') -Stage Payload `
        -CertificateThumbprint $CertificateThumbprint -PfxPath $PfxPath `
        -TimestampServer $TimestampServer
}

& $compilerPath $definitionPath
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed with exit code $LASTEXITCODE."
}

$installer = Get-ChildItem `
    -LiteralPath (Join-Path $projectRoot 'artifacts\installer') `
    -Filter 'VkoInternetMonitoringAgent-Setup-*-win-x64.exe' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($Sign) {
    & (Join-Path $PSScriptRoot 'sign-release.ps1') -Stage Installer `
        -CertificateThumbprint $CertificateThumbprint -PfxPath $PfxPath `
        -TimestampServer $TimestampServer
}

$verification = & (Join-Path $PSScriptRoot 'verify-installer.ps1') `
    -InstallerPath $installer.FullName -RequireValidSignature:$Sign -RequireTimestamp:$Sign
$checksumPath = "$($installer.FullName).sha256"
"$($verification.Sha256)  $($installer.Name)" | Set-Content `
    -LiteralPath $checksumPath `
    -Encoding ascii

Write-Host "Installer built and verified: $($installer.FullName)"
Write-Host "SHA-256 manifest: $checksumPath"
