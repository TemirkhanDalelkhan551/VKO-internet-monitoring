param(
    [ValidateRange(1, 20)]
    [int]$MaximumLogFiles = 5,
    [ValidateRange(10, 5000)]
    [int]$MaximumLogLines = 500,
    [string]$OutputDirectory = "$env:USERPROFILE\Desktop"
)

$ErrorActionPreference = 'Stop'
$serviceName = 'VkoInternetMonitoringAgent'
$installDirectory = Join-Path $env:ProgramFiles 'VkoInternetMonitoringAgent'
$dataDirectory = Join-Path $env:ProgramData 'VkoInternetMonitoringAgent'
$configurationPath = Join-Path $installDirectory 'appsettings.json'
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$staging = Join-Path $temporaryRoot "vko-agent-diagnostics-$([guid]::NewGuid().ToString('N'))"
$stagingFullPath = [IO.Path]::GetFullPath($staging)

if (-not $stagingFullPath.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Could not create a safe temporary diagnostics directory.'
}

function Protect-Text([string]$Text) {
    if ($null -eq $Text) { return $Text }
    $Text = $Text -replace '(?i)(authorization\s*[:=]\s*bearer\s+)\S+', '$1[REDACTED]'
    $Text = $Text -replace '(?i)(x-device-token\s*[:=]\s*)\S+', '$1[REDACTED]'
    $Text = $Text -replace '(?i)("?(?:device)?token|password|secret|activationcode"?\s*[:=]\s*"?)[^",\s]+', '$1[REDACTED]'
    return $Text
}

function Protect-JsonValue($Value, [string]$PropertyName = '') {
    if ($PropertyName -match '(?i)token|password|secret|activationcode') {
        return '[REDACTED]'
    }
    if ($null -eq $Value -or $Value -is [string] -or $Value -is [ValueType]) {
        return $Value
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [pscustomobject]) {
        return @($Value | ForEach-Object { Protect-JsonValue $_ })
    }
    $result = [ordered]@{}
    foreach ($property in $Value.PSObject.Properties) {
        $result[$property.Name] = Protect-JsonValue $property.Value $property.Name
    }
    return [PSCustomObject]$result
}

try {
    New-Item -ItemType Directory -Force -Path $stagingFullPath | Out-Null
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    $agentExecutable = Join-Path $installDirectory 'VkoMonitoring.Agent.exe'
    $agentVersion = if (Test-Path -LiteralPath $agentExecutable) {
        (Get-Item -LiteralPath $agentExecutable).VersionInfo.ProductVersion.Trim()
    } else { $null }
    $queueFiles = @(Get-ChildItem -LiteralPath (Join-Path $dataDirectory 'queue') -File -ErrorAction SilentlyContinue)
    $logFiles = @(Get-ChildItem -LiteralPath (Join-Path $dataDirectory 'logs') -File -ErrorAction SilentlyContinue)
    $os = Get-CimInstance Win32_OperatingSystem
    $drive = Get-PSDrive -Name ([IO.Path]::GetPathRoot($installDirectory).TrimEnd('\').TrimEnd(':'))

    [PSCustomObject]@{
        GeneratedAt = [DateTimeOffset]::Now
        ComputerName = $env:COMPUTERNAME
        Windows = $os.Caption
        WindowsVersion = $os.Version
        Architecture = $env:PROCESSOR_ARCHITECTURE
        AgentVersion = $agentVersion
        ServiceStatus = $service.State
        ServiceStartMode = $service.StartMode
        ServiceProcessId = $service.ProcessId
        FreeDiskGb = [Math]::Round($drive.Free / 1GB, 2)
        QueueFiles = $queueFiles.Count
        QueueSizeMb = [Math]::Round(($queueFiles | Measure-Object Length -Sum).Sum / 1MB, 3)
        LogFiles = $logFiles.Count
        LogSizeMb = [Math]::Round(($logFiles | Measure-Object Length -Sum).Sum / 1MB, 3)
        SecretsIncluded = $false
    } | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $stagingFullPath 'system-summary.json') `
        -Encoding utf8

    if (Test-Path -LiteralPath $configurationPath) {
        $configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
        Protect-JsonValue $configuration | ConvertTo-Json -Depth 20 | Set-Content `
            -LiteralPath (Join-Path $stagingFullPath 'appsettings.sanitized.json') `
            -Encoding utf8
    }

    $logsDirectory = Join-Path $stagingFullPath 'logs'
    New-Item -ItemType Directory -Force -Path $logsDirectory | Out-Null
    foreach ($log in $logFiles | Sort-Object LastWriteTime -Descending | Select-Object -First $MaximumLogFiles) {
        $safeLines = Get-Content -LiteralPath $log.FullName -Tail $MaximumLogLines |
            ForEach-Object { Protect-Text $_ }
        $safeLines | Set-Content `
            -LiteralPath (Join-Path $logsDirectory $log.Name) `
            -Encoding utf8
    }

    @(
        'The archive intentionally excludes device-token.dat and queued measurement contents.',
        'Configuration and log lines are sanitized for common token/password/secret patterns.',
        'Review the archive before sending it outside the organization.'
    ) | Set-Content -LiteralPath (Join-Path $stagingFullPath 'README.txt') -Encoding utf8

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $archivePath = Join-Path $OutputDirectory "vko-agent-diagnostics-$(Get-Date -Format 'yyyyMMdd-HHmmss').zip"
    Compress-Archive -Path (Join-Path $stagingFullPath '*') -DestinationPath $archivePath -Force
    $archive = Get-Item -LiteralPath $archivePath
    [PSCustomObject]@{
        Path = $archive.FullName
        SizeBytes = $archive.Length
        Sha256 = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash
        SecretsIncluded = $false
    }
}
finally {
    if ((Test-Path -LiteralPath $stagingFullPath) -and
        $stagingFullPath.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $stagingFullPath -Recurse -Force
    }
}
