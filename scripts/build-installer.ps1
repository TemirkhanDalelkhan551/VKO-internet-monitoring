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

& $compilerPath $definitionPath
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed with exit code $LASTEXITCODE."
}

$installer = Get-ChildItem `
    -LiteralPath (Join-Path $projectRoot 'artifacts\installer') `
    -Filter 'VkoInternetMonitoringAgent-Setup-*-win-x64.exe' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Write-Host "Installer built: $($installer.FullName)"
