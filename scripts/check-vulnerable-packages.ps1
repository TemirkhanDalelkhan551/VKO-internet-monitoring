param([string]$Solution = 'VkoInternetMonitoring.sln')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Solution)) {
    $Solution = Join-Path $projectRoot $Solution
}

$output = & dotnet list $Solution package `
    --vulnerable `
    --include-transitive `
    --format json
if ($LASTEXITCODE -ne 0) {
    throw 'NuGet vulnerability audit failed to run.'
}

$report = $output | ConvertFrom-Json -Depth 20
$vulnerable = @(
    foreach ($project in $report.projects) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($null -ne $package -and @($package.vulnerabilities).Count -gt 0) {
                    [PSCustomObject]@{
                        Project = $project.path
                        Framework = $framework.framework
                        Package = $package.id
                        ResolvedVersion = $package.resolvedVersion
                        Vulnerabilities = @($package.vulnerabilities).Count
                    }
                }
            }
        }
    }
)

if ($vulnerable.Count -gt 0) {
    $vulnerable | Format-Table -AutoSize | Out-String | Write-Error
    throw "$($vulnerable.Count) vulnerable NuGet package entries were found."
}

Write-Host "NuGet vulnerability audit passed for $(@($report.projects).Count) projects."
