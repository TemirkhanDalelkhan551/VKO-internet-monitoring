param(
    [string]$BaseUrl = 'https://vko-internet-monitoring-api.onrender.com'
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
$roles = @('Administrator', 'Regional', 'District', 'School', 'Provider')
$checks = [Collections.Generic.List[object]]::new()

function Add-Check([string]$Role, [string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([pscustomobject]@{ Role = $Role; Check = $Name; Passed = $Passed; Detail = $Detail })
    if (-not $Passed) { throw "$Role / $Name failed: $Detail" }
}

function Invoke-Api([string]$Path, [string]$Token) {
    $headers = @{}
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    return Invoke-WebRequest -Uri "$BaseUrl$Path" -Headers $headers `
        -SkipHttpErrorCheck -TimeoutSec 90
}

$ready = Invoke-WebRequest -Uri "$BaseUrl/health/ready" -TimeoutSec 90
Add-Check 'Server' 'readiness' ($ready.StatusCode -eq 200 -and $ready.Content -match 'Healthy') "HTTP $($ready.StatusCode)"

foreach ($role in $roles) {
    $prefix = "VKO_RENDER_$($role.ToUpperInvariant())"
    $login = [Environment]::GetEnvironmentVariable("${prefix}_LOGIN")
    $password = [Environment]::GetEnvironmentVariable("${prefix}_PASSWORD")
    if ([string]::IsNullOrWhiteSpace($login) -or [string]::IsNullOrEmpty($password)) {
        throw "Set ${prefix}_LOGIN and ${prefix}_PASSWORD for the read-only role acceptance."
    }

    $session = $null
    try {
        $session = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method Post `
            -ContentType 'application/json' -Body (@{ login = $login; password = $password } | ConvertTo-Json -Compress) `
            -TimeoutSec 90
        Add-Check $role 'login' ([bool]$session.accessToken) 'Bearer session issued'
        $me = Invoke-RestMethod -Uri "$BaseUrl/api/auth/me" -Headers @{ Authorization = "Bearer $($session.accessToken)" } -TimeoutSec 90
        Add-Check $role 'identity' ($me.role -eq $role) "role=$($me.role)"

        $schools = Invoke-Api '/api/schools' $session.accessToken
        Add-Check $role 'schools scope' ($schools.StatusCode -eq 200) "HTTP $($schools.StatusCode)"
        $incidents = Invoke-Api '/api/incidents?limit=1' $session.accessToken
        Add-Check $role 'incidents scope' ($incidents.StatusCode -eq 200) "HTTP $($incidents.StatusCode)"

        foreach ($adminPath in '/api/users', '/api/audit?limit=1', '/api/settings/operations') {
            $response = Invoke-Api $adminPath $session.accessToken
            $expected = if ($role -eq 'Administrator') { 200 } else { 403 }
            Add-Check $role "access $adminPath" ($response.StatusCode -eq $expected) "HTTP $($response.StatusCode), expected $expected"
        }
    }
    finally {
        if ($session?.accessToken) {
            try { Invoke-WebRequest -Uri "$BaseUrl/api/auth/logout" -Method Post `
                    -Headers @{ Authorization = "Bearer $($session.accessToken)" } `
                    -SkipHttpErrorCheck -TimeoutSec 30 | Out-Null } catch { }
        }
    }
}

$checks | Format-Table -AutoSize
"Role acceptance passed: $(@($checks | Where-Object Passed).Count) checks."
