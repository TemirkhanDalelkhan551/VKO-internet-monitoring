param(
    [string]$BackupPath,
    [string]$Container = 'vko-monitoring-postgres-1',
    [string]$SourceDatabase = 'vko_monitoring',
    [string]$User = 'vko_monitoring'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BackupPath)) {
    $BackupPath = Get-ChildItem `
        -LiteralPath (Join-Path $projectRoot 'artifacts\backups') `
        -Filter '*.dump' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
elseif (-not [IO.Path]::IsPathRooted($BackupPath)) {
    $BackupPath = Join-Path $projectRoot $BackupPath
}

if (-not $BackupPath -or -not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) {
    throw 'Backup file was not found. Run scripts\backup-postgres.ps1 first.'
}

foreach ($identifier in @($Container, $SourceDatabase, $User)) {
    if ($identifier -notmatch '^[A-Za-z0-9_.-]+$') {
        throw "Unsafe container or database identifier: $identifier"
    }
}

& docker inspect $Container | Out-Null
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL container '$Container' is unavailable." }

$targetDatabase = "vko_restore_check_$([guid]::NewGuid().ToString('N').Substring(0, 12))"
$temporaryPath = "/tmp/$([guid]::NewGuid().ToString('N')).dump"
$databaseCreated = $false
$tables = @(
    'schools',
    'internet_lines',
    'devices',
    'device_activation_codes',
    'measurements',
    'incidents',
    'incident_history',
    'monitoring_users',
    'user_sessions',
    'audit_events'
)

try {
    & docker cp $BackupPath "${Container}:$temporaryPath" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy the backup into the container.' }

    & docker exec $Container createdb --username=$User $targetDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the isolated restore database.' }
    $databaseCreated = $true

    & docker exec $Container pg_restore `
        --username=$User `
        --dbname=$targetDatabase `
        --exit-on-error `
        --no-owner `
        --no-privileges `
        $temporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Backup restore failed.' }

    $comparison = foreach ($table in $tables) {
        $sourceCount = & docker exec $Container psql `
            --username=$User `
            --dbname=$SourceDatabase `
            --tuples-only `
            --no-align `
            --command="SELECT count(*) FROM public.$table;"
        if ($LASTEXITCODE -ne 0) { throw "Could not count source table $table." }

        $restoredCount = & docker exec $Container psql `
            --username=$User `
            --dbname=$targetDatabase `
            --tuples-only `
            --no-align `
            --command="SELECT count(*) FROM public.$table;"
        if ($LASTEXITCODE -ne 0) { throw "Could not count restored table $table." }

        [PSCustomObject]@{
            Table = $table
            SourceRows = [long]$sourceCount.Trim()
            RestoredRows = [long]$restoredCount.Trim()
            Matches = $sourceCount.Trim() -eq $restoredCount.Trim()
        }
    }

    $mismatches = @($comparison | Where-Object { -not $_.Matches })
    if ($mismatches.Count -gt 0) {
        $mismatches | Format-Table -AutoSize | Out-String | Write-Error
        throw 'Restored table counts do not match the source database.'
    }

    [PSCustomObject]@{
        BackupPath = (Get-Item -LiteralPath $BackupPath).FullName
        SourceDatabase = $SourceDatabase
        TablesChecked = $comparison.Count
        RowsChecked = ($comparison | Measure-Object SourceRows -Sum).Sum
        VerifiedAt = [DateTimeOffset]::Now
    }
}
finally {
    if ($databaseCreated -and $targetDatabase -like 'vko_restore_check_*') {
        & docker exec $Container dropdb `
            --username=$User `
            --if-exists `
            --force `
            $targetDatabase 2>$null
    }
    & docker exec $Container rm -f $temporaryPath 2>$null
}
