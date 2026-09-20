param(
    [string]$InstallerPath,
    [switch]$RequireValidSignature,
    [switch]$RequireTimestamp
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Get-ChildItem `
        -LiteralPath (Join-Path $projectRoot 'artifacts\installer') `
        -Filter 'VkoInternetMonitoringAgent-Setup-*-win-x64.exe' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
elseif (-not [IO.Path]::IsPathRooted($InstallerPath)) {
    $InstallerPath = Join-Path $projectRoot $InstallerPath
}

if (-not $InstallerPath -or -not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw 'Installer was not found. Build it with scripts\build-installer.ps1.'
}

[xml]$buildProps = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props')
$expectedVersion = [string]$buildProps.Project.PropertyGroup.Version
$installer = Get-Item -LiteralPath $InstallerPath
$expectedName = "VkoInternetMonitoringAgent-Setup-$expectedVersion-win-x64.exe"

if ($installer.Name -ne $expectedName) {
    throw "Installer name '$($installer.Name)' does not match project version $expectedVersion."
}

if ($installer.Length -lt 1MB) {
    throw "Installer is unexpectedly small: $($installer.Length) bytes."
}

$productVersion = $installer.VersionInfo.ProductVersion.Trim()
if ($productVersion -ne $expectedVersion) {
    throw "Installer product version '$productVersion' does not match $expectedVersion."
}

$hash = Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256
$signature = Get-AuthenticodeSignature -LiteralPath $installer.FullName
if ($RequireValidSignature -and $signature.Status -ne 'Valid') {
    throw "Installer signature is $($signature.Status); a valid signature is required."
}
if ($RequireTimestamp -and -not $signature.TimeStamperCertificate) {
    throw 'Installer signature has no trusted timestamp.'
}

[PSCustomObject]@{
    Path = $installer.FullName
    Version = $productVersion
    SizeBytes = $installer.Length
    Sha256 = $hash.Hash
    SignatureStatus = $signature.Status.ToString()
    SignerSubject = $signature.SignerCertificate?.Subject
    TimestampSubject = $signature.TimeStamperCertificate?.Subject
    VerifiedAt = [DateTimeOffset]::Now
}
