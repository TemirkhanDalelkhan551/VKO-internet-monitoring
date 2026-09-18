[CmdletBinding()]
param(
    [string]$LocalApiUrl = "http://localhost:5080"
)

$ErrorActionPreference = "Stop"

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

$healthUrl = "$($LocalApiUrl.TrimEnd('/'))/health/ready"
try {
    $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 5
}
catch {
    throw "Local API is not ready at $healthUrl. Start the API and PostgreSQL first."
}

if ($health.status -notin @("Healthy", "Degraded")) {
    throw "Local API returned readiness status '$($health.status)'."
}

Write-Host "Local API is ready. Starting a temporary Cloudflare Quick Tunnel..."
Write-Host "Keep this window open. Copy the https://...trycloudflare.com address from the output."
Write-Host "The address changes whenever the tunnel is restarted."

& $cloudflaredPath tunnel --url $LocalApiUrl --no-autoupdate
