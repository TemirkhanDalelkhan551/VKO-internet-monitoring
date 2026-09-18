[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repositoryRoot "src\VkoMonitoring.Api\VkoMonitoring.Api.csproj"
$apiAssembly = Join-Path $repositoryRoot "src\VkoMonitoring.Api\bin\Release\net10.0\VkoMonitoring.Api.dll"
$apiAssemblyArgument = ".\src\VkoMonitoring.Api\bin\Release\net10.0\VkoMonitoring.Api.dll"
$dataDirectory = Join-Path $repositoryRoot "data"
$apiStandardOutput = Join-Path $dataDirectory "api-demo.stdout.log"
$apiStandardError = Join-Path $dataDirectory "api-demo.stderr.log"
$cloudflaredCommand = Get-Command cloudflared -ErrorAction SilentlyContinue
$cloudflaredPath = if ($cloudflaredCommand) {
    $cloudflaredCommand.Source
}
else {
    "C:\Program Files (x86)\cloudflared\cloudflared.exe"
}

if (-not (Test-Path -LiteralPath $cloudflaredPath)) {
    throw "cloudflared is not installed. Install Cloudflare.cloudflared with winget first."
}

$adminToken = [Environment]::GetEnvironmentVariable("MonitoringApi__AdminToken", "User")
if ([string]::IsNullOrWhiteSpace($adminToken) -or
    $adminToken.StartsWith("CHANGE_ME", [StringComparison]::OrdinalIgnoreCase)) {
    $adminToken = [Convert]::ToBase64String(
        [Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    [Environment]::SetEnvironmentVariable("MonitoringApi__AdminToken", $adminToken, "User")
}

Push-Location $repositoryRoot
$apiProcess = $null
try {
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    docker compose -p vko-monitoring up -d postgres
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL could not be started. Make sure Docker Desktop is running."
    }

    dotnet build $apiProject --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "The API build failed."
    }

    $env:ASPNETCORE_URLS = "http://localhost:5080"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:MonitoringApi__StorageProvider = "PostgreSql"
    $env:MonitoringApi__MaximumSpeedTestBytes = "5000000"
    $env:MonitoringApi__AdminToken = $adminToken
    $legacyAdminSetting = [Environment]::GetEnvironmentVariable("MonitoringApi__EnableLegacyAdminToken", "User")
    $env:MonitoringApi__EnableLegacyAdminToken = if ([string]::IsNullOrWhiteSpace($legacyAdminSetting)) { "true" } else { $legacyAdminSetting }
    $env:ConnectionStrings__MonitoringDatabase =
        "Host=localhost;Port=55432;Database=vko_monitoring;Username=vko_monitoring;Password=vko_dev_password"

    $apiProcess = Start-Process `
        -FilePath "C:\Program Files\dotnet\dotnet.exe" `
        -ArgumentList @($apiAssemblyArgument) `
        -WorkingDirectory $repositoryRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $apiStandardOutput `
        -RedirectStandardError $apiStandardError `
        -PassThru

    $apiReady = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if ($apiProcess.HasExited) {
            $apiError = Get-Content -LiteralPath $apiStandardError -Raw -ErrorAction SilentlyContinue
            throw "The local API stopped during startup. $apiError"
        }

        try {
            $health = Invoke-RestMethod -Uri "http://localhost:5080/health/ready" -TimeoutSec 2
            if ($health.status -in @("Healthy", "Degraded")) {
                $apiReady = $true
                break
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    if (-not $apiReady) {
        throw "The local API did not become ready within 30 seconds."
    }

    Write-Host "API and PostgreSQL are ready. Starting Cloudflare Quick Tunnel..."
    Write-Host "Keep this window open and use the displayed https://...trycloudflare.com address."
    & $cloudflaredPath tunnel --url "http://localhost:5080" --no-autoupdate
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id
    }

    Pop-Location
}
