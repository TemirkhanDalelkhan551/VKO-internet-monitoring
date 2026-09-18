param([string]$BaseUrl = 'http://localhost:5095')
$ErrorActionPreference = 'Stop'
if (-not ([uri]$BaseUrl).IsLoopback) { throw 'Use only isolated local web fixtures.' }
$taskChecks = 0
function Check($name, $condition) {
    if (-not $condition) { throw "FAILED: $name" }
    $script:taskChecks++; Write-Output "PASS: $name"
}
function Login($login) {
    $session = Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -ContentType application/json -Body (@{login=$login;password='web-test-only-2026'} | ConvertTo-Json)
    return @{Authorization="Bearer $($session.accessToken)"}
}
function WriteApi($path, $method, $body, $headers) {
    return Invoke-WebRequest "$BaseUrl$path" -Method $method -Headers $headers -ContentType application/json -Body ($body | ConvertTo-Json) -SkipHttpErrorCheck
}
$admin = Login 'web-admin'
try {
    $school = @{name=' Школа справочника ';districtCity='Тестовый район справочников';address=' Адрес ';responsibleName=' Иван ';responsiblePosition='Директор';responsiblePhone='+7 000 0000000';responsibleEmail='school@example.com'}
    Check 'anonymous create denied' ((WriteApi '/api/schools' Post $school @{}).StatusCode -eq 401)
    $created = WriteApi '/api/schools' Post $school $admin
    Check 'school created' ($created.StatusCode -eq 201)
    $id = ($created.Content | ConvertFrom-Json).schoolId
    $saved = Invoke-RestMethod "$BaseUrl/api/schools/$id" -Headers $admin
    Check 'school fields trimmed and contacts returned' ($saved.name -eq 'Школа справочника' -and $saved.responsibleName -eq 'Иван' -and $saved.responsibleEmail -eq 'school@example.com')
    Check 'school created without fake lines or coordinates' ($saved.lines.Count -eq 0 -and $null -eq $saved.latitude)
    $devices=Invoke-RestMethod "$BaseUrl/api/schools/$id/devices" -Headers $admin
    Check 'school has no fake devices' ($devices.Count -eq 0)
    Check 'location set' ((WriteApi "/api/schools/$id/location" Put @{latitude=49;longitude=82} $admin).StatusCode -eq 204)
    $school.name='Новая школа'; $school.responsiblePhone=''; $school.responsibleEmail=$null
    Check 'school updated' ((WriteApi "/api/schools/$id" Put $school $admin).StatusCode -eq 204)
    $saved = Invoke-RestMethod "$BaseUrl/api/schools/$id" -Headers $admin
    Check 'identity and coordinates preserved with contacts cleared' ($saved.schoolId -eq $id -and $saved.latitude -eq 49 -and $saved.name -eq 'Новая школа' -and $null -eq $saved.responsiblePhone -and $null -eq $saved.responsibleEmail)
    $line = @{name='Линия договора';providerName='Directory telecom';connectionType='Оптика';contractedDownloadMbps=100.125;contractedUploadMbps=50;contractNumber='Д-2026';contractDate='2026-09-18';lineStatus='Primary'}
    $createdLine = WriteApi "/api/schools/$id/lines" Post $line $admin
    Check 'line created' ($createdLine.StatusCode -eq 201)
    $lineId = ($createdLine.Content | ConvertFrom-Json).lineId
    $saved = Invoke-RestMethod "$BaseUrl/api/schools/$id" -Headers $admin
    Check 'contract and speeds returned exactly' ($saved.lines[0].contractNumber -eq 'Д-2026' -and $saved.lines[0].contractDate -eq '2026-09-18' -and $saved.lines[0].contractedDownloadMbps -eq 100.125)
    Check 'line has no fake device or measurement' ($saved.lines[0].deviceCount -eq 0 -and $null -eq $saved.lines[0].latestMeasurement)
    $line.lineStatus='Disabled'; $line.contractNumber=$null; $line.contractDate=$null; $line.contractedUploadMbps=$null
    Check 'line updated' ((WriteApi "/api/schools/$id/lines/$lineId" Put $line $admin).StatusCode -eq 204)
    $saved = Invoke-RestMethod "$BaseUrl/api/schools/$id" -Headers $admin
    Check 'line identity retained and optional contract cleared' ($saved.lines[0].lineId -eq $lineId -and $saved.lines[0].lineStatus -eq 'Disabled' -and $null -eq $saved.lines[0].contractDate -and $null -eq $saved.lines[0].contractedUploadMbps)
    $line.contractedDownloadMbps=0
    Check 'zero contractual speed rejected' ((WriteApi "/api/schools/$id/lines/$lineId" Put $line $admin).StatusCode -eq 400)
    $line.contractedDownloadMbps=100.125
    $school.responsibleEmail='bad email'
    Check 'invalid email rejected' ((WriteApi "/api/schools/$id" Put $school $admin).StatusCode -eq 400)
    $school.responsibleEmail=$null
    $unknown=[guid]::NewGuid()
    Check 'missing school cannot acquire a line' ((WriteApi "/api/schools/$unknown/lines" Post $line $admin).StatusCode -eq 404)
    $main = (Invoke-RestMethod "$BaseUrl/api/schools" -Headers $admin) | Where-Object districtCity -eq 'Усть-Каменогорск'
    Check 'line cannot move between schools' ((WriteApi "/api/schools/$($main.schoolId)/lines/$lineId" Put $line $admin).StatusCode -eq 404)
    foreach ($role in @('school','district','provider','regional')) {
        $headers=Login "web-$role"
        try {
            $expected=if($role -eq 'regional'){204}else{403}
            Check "$role school edit permissions" ((WriteApi "/api/schools/$id" Put $school $headers).StatusCode -eq $expected)
            Check "$role line edit permissions" ((WriteApi "/api/schools/$id/lines/$lineId" Put $line $headers).StatusCode -eq $expected)
            if($role -ne 'regional') {
                Check "$role school create denied" ((WriteApi '/api/schools' Post $school $headers).StatusCode -eq 403)
                Check "$role foreign contact hidden" ((Invoke-WebRequest "$BaseUrl/api/schools/$id" -Headers $headers -SkipHttpErrorCheck).StatusCode -eq 404)
            }
        } finally { Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $headers | Out-Null }
    }
    $audit=Invoke-RestMethod "$BaseUrl/api/audit?limit=100" -Headers $admin
    Check 'directory writes audited' (@($audit | Where-Object { $_.path -eq "/api/schools/$id/lines/$lineId" -and $_.statusCode -eq 204 }).Count -ge 1)
    Write-Output "Directory integration: $taskChecks checks passed."
} finally { Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $admin | Out-Null }
