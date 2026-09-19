param(
    [string]$Container = 'vko-monitoring-postgres-1',
    [string]$Database = 'vko_monitoring',
    [string]$User = 'vko_monitoring',
    [string]$OutputDirectory = 'artifacts\backups',
    [ValidateRange(1, 3650)]
    [int]$RetentionDays = 30
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot $OutputDirectory
}

foreach ($identifier in @($Container, $Database, $User)) {
    if ($identifier -notmatch '^[A-Za-z0-9_.-]+$') {
        throw "Unsafe container or database identifier: $identifier"
    }
}

& docker inspect $Container | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "PostgreSQL container '$Container' is unavailable."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$fileName = "${Database}-${stamp}.dump"
$destination = Join-Path $OutputDirectory $fileName
$temporaryPath = "/tmp/$([guid]::NewGuid().ToString('N')).dump"

try {
    & docker exec $Container pg_dump `
        --username=$User `
        --dbname=$Database `
        --format=custom `
        --no-owner `
        --no-privileges `
        --file=$temporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed.' }

    & docker exec $Container pg_restore --list $temporaryPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The created backup failed pg_restore validation.' }

    & docker cp "${Container}:$temporaryPath" $destination
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy the backup from the container.' }
}
finally {
    & docker exec $Container rm -f $temporaryPath 2>$null
}

$backup = Get-Item -LiteralPath $destination
$hash = Get-FileHash -LiteralPath $destination -Algorithm SHA256
$expired = @(Get-ChildItem -LiteralPath $OutputDirectory -Filter "${Database}-*.dump" -File |
    Where-Object {
        $_.FullName -ne $backup.FullName -and
        $_.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddDays(-$RetentionDays)
    })
foreach ($expiredBackup in $expired) {
    Remove-Item -LiteralPath $expiredBackup.FullName -Force
}
[PSCustomObject]@{
    Path = $backup.FullName
    Database = $Database
    SizeBytes = $backup.Length
    Sha256 = $hash.Hash
    RetentionDays = $RetentionDays
    RemovedExpiredBackups = $expired.Count
    CreatedAt = [DateTimeOffset]::Now
}
