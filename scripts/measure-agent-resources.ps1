param(
    [ValidateRange(1, 3600)]
    [int]$DurationSeconds = 60,
    [ValidateRange(1, 60)]
    [int]$IntervalSeconds = 5,
    [string]$OutputDirectory = "$env:USERPROFILE\Desktop"
)

$ErrorActionPreference = 'Stop'
$serviceName = 'VkoInternetMonitoringAgent'
$dataDirectory = Join-Path $env:ProgramData 'VkoInternetMonitoringAgent'
$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"

if (-not $service) {
    throw "Service $serviceName is not installed."
}

if ($service.State -ne 'Running' -or $service.ProcessId -le 0) {
    throw "Service $serviceName is not running. Start it before measuring resources."
}

$process = Get-Process -Id $service.ProcessId -ErrorAction Stop
$startedAt = Get-Date
$previousCpu = $process.TotalProcessorTime.TotalSeconds
$previousTime = $startedAt
$samples = [Collections.Generic.List[object]]::new()

while (((Get-Date) - $startedAt).TotalSeconds -lt $DurationSeconds) {
    Start-Sleep -Seconds $IntervalSeconds
    $now = Get-Date
    $process.Refresh()
    $cpu = $process.TotalProcessorTime.TotalSeconds
    $elapsed = ($now - $previousTime).TotalSeconds
    $cpuPercent = if ($elapsed -gt 0) {
        [Math]::Round((($cpu - $previousCpu) / $elapsed / [Environment]::ProcessorCount) * 100, 2)
    } else { 0 }

    $queueDirectory = Join-Path $dataDirectory 'queue'
    $logDirectory = Join-Path $dataDirectory 'logs'
    $queueFiles = @(Get-ChildItem -LiteralPath $queueDirectory -File -ErrorAction SilentlyContinue)
    $logFiles = @(Get-ChildItem -LiteralPath $logDirectory -File -ErrorAction SilentlyContinue)
    $network = @(Get-CimInstance `
        Win32_PerfFormattedData_Tcpip_NetworkInterface `
        -ErrorAction SilentlyContinue)
    $systemNetworkBytesPerSecond = ($network | Measure-Object BytesTotalPersec -Sum).Sum
    $samples.Add([PSCustomObject]@{
        Timestamp = [DateTimeOffset]$now
        CpuPercent = $cpuPercent
        WorkingSetMb = [Math]::Round($process.WorkingSet64 / 1MB, 2)
        PrivateMemoryMb = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        ThreadCount = $process.Threads.Count
        HandleCount = $process.HandleCount
        SystemNetworkMbps = [Math]::Round(($systemNetworkBytesPerSecond * 8) / 1MB, 3)
        QueueFiles = $queueFiles.Count
        QueueSizeMb = [Math]::Round(($queueFiles | Measure-Object Length -Sum).Sum / 1MB, 3)
        LogSizeMb = [Math]::Round(($logFiles | Measure-Object Length -Sum).Sum / 1MB, 3)
    })
    $previousCpu = $cpu
    $previousTime = $now
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$csvPath = Join-Path $OutputDirectory "vko-agent-resources-$stamp.csv"
$samples | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8

[PSCustomObject]@{
    ComputerName = $env:COMPUTERNAME
    Service = $serviceName
    ProcessId = $service.ProcessId
    Samples = $samples.Count
    AverageCpuPercent = [Math]::Round(($samples | Measure-Object CpuPercent -Average).Average, 2)
    MaximumWorkingSetMb = [Math]::Round(($samples | Measure-Object WorkingSetMb -Maximum).Maximum, 2)
    MaximumPrivateMemoryMb = [Math]::Round(($samples | Measure-Object PrivateMemoryMb -Maximum).Maximum, 2)
    CsvPath = $csvPath
}
