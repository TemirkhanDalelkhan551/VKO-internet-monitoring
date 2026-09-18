param([string]$BaseUrl = 'http://localhost:5094')
$ErrorActionPreference = 'Stop'
if (-not ([uri]$BaseUrl).IsLoopback) { throw 'Run this script only against the isolated local web fixture.' }
$taskChecks = 0
function Check($name, $condition) {
    if (-not $condition) { throw "FAILED: $name" }
    $script:taskChecks++
    Write-Output "PASS: $name"
}
function Login($login) {
    $body = @{ login=$login; password='web-test-only-2026' } | ConvertTo-Json
    $session = Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -ContentType application/json -Body $body
    return @{ Authorization="Bearer $($session.accessToken)" }
}
function PutLocation($id, $body, $headers) {
    $response = Invoke-WebRequest "$BaseUrl/api/schools/$id/location" -Method Put -Headers $headers `
        -ContentType application/json -Body ($body | ConvertTo-Json) -SkipHttpErrorCheck
    return [int]$response.StatusCode
}
$admin = Login 'web-admin'
try {
    $schools = Invoke-RestMethod "$BaseUrl/api/schools" -Headers $admin
    $main = $schools | Where-Object districtCity -eq 'Усть-Каменогорск'
    $other = $schools | Where-Object districtCity -eq 'Риддер'
    Check 'fixture schools' ($null -ne $main -and $null -ne $other)
    Check 'reset fixture location' ((PutLocation $main.schoolId @{latitude=$null;longitude=$null} $admin) -eq 204)
    $main = Invoke-RestMethod "$BaseUrl/api/schools/$($main.schoolId)" -Headers $admin
    Check 'coordinates initially missing' ($null -eq $main.latitude -and $null -eq $main.longitude)
    $location = @{ latitude=49.97; longitude=82.61 }
    Check 'anonymous write rejected' ((PutLocation $main.schoolId $location @{}) -eq 401)
    Check 'administrator saves coordinates' ((PutLocation $main.schoolId $location $admin) -eq 204)
    $saved = Invoke-RestMethod "$BaseUrl/api/schools/$($main.schoolId)" -Headers $admin
    Check 'card returns exact coordinate pair' ($saved.latitude -eq 49.97 -and $saved.longitude -eq 82.61)
    $list = Invoke-RestMethod "$BaseUrl/api/schools" -Headers $admin
    Check 'overview retains coordinates after line aggregation' (($list | Where-Object schoolId -eq $main.schoolId).latitude -eq 49.97)
    Check 'partial coordinate rejected' ((PutLocation $main.schoolId @{latitude=49.97;longitude=$null} $admin) -eq 400)
    Check 'out of range rejected' ((PutLocation $main.schoolId @{latitude=91;longitude=82} $admin) -eq 400)
    Check 'unknown school rejected' ((PutLocation ([guid]::NewGuid()) $location $admin) -eq 404)
    foreach ($role in @('school','district','provider','regional','empty')) {
        $headers = Login "web-$role"
        try {
            $visible = Invoke-RestMethod "$BaseUrl/api/schools" -Headers $headers
            if ($role -eq 'regional') {
                Check 'regional sees all fixture schools' ($visible.Count -eq $schools.Count -and $visible.schoolId -contains $main.schoolId -and $visible.schoolId -contains $other.schoolId)
                Check 'regional can update location' ((PutLocation $main.schoolId $location $headers) -eq 204)
            } elseif ($role -eq 'empty') {
                Check 'empty district exposes no coordinates' ($visible.Count -eq 0)
            } else {
                Check "$role sees only assigned school" ($visible.Count -eq 1 -and $visible[0].schoolId -eq $main.schoolId)
                Check "$role reads assigned coordinates" ($visible[0].latitude -eq 49.97)
                $foreign = Invoke-WebRequest "$BaseUrl/api/schools/$($other.schoolId)" -Headers $headers -SkipHttpErrorCheck
                Check "$role foreign school hidden" ($foreign.StatusCode -eq 404)
            }
            if ($role -ne 'regional') {
                Check "$role cannot write coordinates" ((PutLocation $main.schoolId $location $headers) -eq 403)
            }
            if ($role -eq 'provider') {
                Check 'provider map payload contains only own lines' ($visible[0].lines.Count -eq 1 -and $visible[0].lines[0].providerName -eq 'Reserve telecom')
                Check 'backup provider receives no foreign primary metric' ($null -eq $visible[0].primaryLineId -and $null -eq $visible[0].latestMeasurement)
            }
        } finally { Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $headers | Out-Null }
    }
    $unchanged = Invoke-RestMethod "$BaseUrl/api/schools/$($main.schoolId)" -Headers $admin
    Check 'rejected edits do not change location' ($unchanged.latitude -eq 49.97 -and $unchanged.longitude -eq 82.61)
    $audit = Invoke-RestMethod "$BaseUrl/api/audit?limit=100" -Headers $admin
    Check 'coordinate updates audited' (@($audit | Where-Object { $_.path -eq "/api/schools/$($main.schoolId)/location" -and $_.statusCode -eq 204 }).Count -ge 2)
    Check 'coordinates can be removed' ((PutLocation $main.schoolId @{latitude=$null;longitude=$null} $admin) -eq 204)
    $removed = Invoke-RestMethod "$BaseUrl/api/schools/$($main.schoolId)" -Headers $admin
    Check 'removal returns null pair' ($null -eq $removed.latitude -and $null -eq $removed.longitude)
    # Restore a synthetic test location for browser QA; this does not describe a real school.
    Check 'restore synthetic fixture marker' ((PutLocation $main.schoolId $location $admin) -eq 204)
    Write-Output "Map integration: $taskChecks checks passed."
} finally { Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $admin | Out-Null }
