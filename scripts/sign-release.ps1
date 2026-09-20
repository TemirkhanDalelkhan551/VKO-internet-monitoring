param(
    [ValidateSet('Payload', 'Installer', 'All')]
    [string]$Stage = 'All',
    [string]$CertificateThumbprint,
    [string]$PfxPath,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [string]$PfxPasswordEnvironmentVariable = 'VKO_CODE_SIGNING_PFX_PASSWORD'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Get-CodeSigningCertificate {
    if ($CertificateThumbprint) {
        $normalized = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
        $certificate = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
            Where-Object Thumbprint -eq $normalized |
            Select-Object -First 1
        if (-not $certificate) { throw "Code-signing certificate $normalized was not found." }
        return $certificate
    }

    if (-not $PfxPath) {
        throw 'Specify -CertificateThumbprint or -PfxPath. Never commit the PFX or its password.'
    }
    if (-not [IO.Path]::IsPathRooted($PfxPath)) { $PfxPath = Join-Path $projectRoot $PfxPath }
    if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) { throw "PFX was not found: $PfxPath" }
    $password = [Environment]::GetEnvironmentVariable($PfxPasswordEnvironmentVariable)
    if ([string]::IsNullOrEmpty($password)) { throw "Environment variable $PfxPasswordEnvironmentVariable is required." }
    return [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        [IO.File]::ReadAllBytes($PfxPath),
        $password,
        [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
}

function Test-CodeSigningCertificate($Certificate) {
    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    if (-not $Certificate.HasPrivateKey) { throw 'The certificate has no private key.' }
    if ([DateTime]::UtcNow -lt $Certificate.NotBefore.ToUniversalTime() -or
        [DateTime]::UtcNow -gt $Certificate.NotAfter.ToUniversalTime()) {
        throw 'The code-signing certificate is not currently valid.'
    }
    $eku = $Certificate.Extensions |
        Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        ForEach-Object EnhancedKeyUsages |
        Where-Object Value -eq $codeSigningOid
    if (-not $eku) { throw 'The certificate is not valid for code signing.' }
}

function Get-ReleaseFiles {
    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    if ($Stage -in @('Payload', 'All')) {
        foreach ($name in 'VkoMonitoring.Agent.exe', 'VkoMonitoring.Agent.Setup.exe') {
            $path = Join-Path $projectRoot "artifacts\agent-win-x64\$name"
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release payload was not found: $path" }
            $files.Add((Get-Item -LiteralPath $path))
        }
    }
    if ($Stage -in @('Installer', 'All')) {
        $installer = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'artifacts\installer') `
            -Filter 'VkoInternetMonitoringAgent-Setup-*-win-x64.exe' |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $installer) { throw 'Installer was not found.' }
        $files.Add($installer)
    }
    return $files
}

$certificate = Get-CodeSigningCertificate
Test-CodeSigningCertificate $certificate
foreach ($file in Get-ReleaseFiles) {
    $signature = Set-AuthenticodeSignature -LiteralPath $file.FullName `
        -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer
    if ($signature.Status -ne 'Valid') { throw "Signing failed for $($file.Name): $($signature.StatusMessage)" }
    $verified = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($verified.Status -ne 'Valid' -or -not $verified.TimeStamperCertificate) {
        throw "A valid timestamped signature was not found on $($file.Name)."
    }
    Write-Host "Signed: $($file.FullName)"
}
