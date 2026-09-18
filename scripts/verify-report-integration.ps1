param(
    [string]$BaseUrl = 'http://localhost:5090',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/report-tests')
)
$ErrorActionPreference = 'Stop'
if (!([uri]$BaseUrl).IsLoopback) { throw 'This test script is for isolated localhost web fixtures only.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$results = [Collections.Generic.List[object]]::new()
function Check($name, $condition) {
    $results.Add(@{name=$name;passed=[bool]$condition})
    if (!$condition) { throw "FAILED: $name" }
}
function Auth($login) {
    $auth = Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -ContentType application/json -Body (@{login=$login;password='web-test-only-2026'}|ConvertTo-Json)
    return @{Authorization='Bearer '+$auth.accessToken}
}
function Status($path, $headers, $expected) {
    try { $response=Invoke-WebRequest ($BaseUrl+$path) -Headers $headers; Check $path ($response.StatusCode -eq $expected) }
    catch { if ($_.Exception.Response) { Check $path ([int]$_.Exception.Response.StatusCode -eq $expected) } else { throw } }
}
$from=[DateTimeOffset]::UtcNow.AddDays(-7).ToString('o'); $to=[DateTimeOffset]::UtcNow.AddDays(1).ToString('o')
$period='from='+[uri]::EscapeDataString($from)+'&to='+[uri]::EscapeDataString($to)
$export='/api/reports/export?kind=measurements&format=csv&'+$period
$admin=Auth 'web-admin'
$schools=Invoke-RestMethod "$BaseUrl/api/schools" -Headers $admin
$school=@($schools|Where-Object name -eq 'Школа «Восток»')[0]
$other=@($schools|Where-Object name -eq 'Лицей № 2')[0]
$devices=Invoke-RestMethod ("$BaseUrl/api/schools/"+$school.schoolId+'/devices') -Headers $admin
$first=@($devices)[0]; $second=@($devices)[1]
Status $export @{} 401
Status ($export+'&fields=token') $admin 400
Status ($export+'&fields=') $admin 400
Status ($export+'&status=Invalid') $admin 400
Status ($export+'&deviceIds=not-a-guid&schoolId='+$school.schoolId) $admin 400
Status ($export+'&deviceIds='+$first.deviceId) $admin 400
Status '/api/reports/export?kind=measurements&format=pdf' $admin 400
Status '/api/reports/export?kind=measurements&format=csv&from=2026-09-20&to=2026-09-18' $admin 400
Status '/api/reports/export?kind=measurements&format=csv&from=invalid' $admin 400
Status '/api/reports/export?kind=measurements&format=csv&to=2025-01-01T00:00:00Z' $admin 200
foreach($login in @('web-admin','web-school','web-district','web-regional','web-provider','web-empty')) {
    $headers=if($login -eq 'web-admin'){$admin}else{Auth $login}
    $csvPath=Join-Path $OutputDirectory "$login.csv"
    $response=Invoke-WebRequest ($BaseUrl+$export) -Headers $headers -OutFile $csvPath -PassThru
    $rows=@(Import-Csv -LiteralPath $csvPath -Delimiter ';' -Encoding utf8)
    Check "$login CSV MIME" ($response.Headers['Content-Type'] -like '*text/csv*')
    Check "$login no-store" ($response.Headers['Cache-Control'] -like '*no-store*')
    Check "$login exact row count" ($rows.Count -eq [int]($response.Headers['X-Report-Measurement-Count']|Select-Object -First 1))
    if($login -in @('web-school','web-district','web-provider')){Check "$login school isolation" (@($rows|Where-Object Школа -ne 'Школа «Восток»').Count -eq 0)}
    if($login -eq 'web-provider'){
        Check 'provider only own device' (@($rows|Where-Object Компьютер -ne 'Кабинет 102').Count -eq 0)
        Status ($export+'&schoolId='+$other.schoolId) $headers 404
        Status ($export+'&schoolId='+$school.schoolId+'&deviceIds='+$first.deviceId) $headers 404
    }
    if($login -eq 'web-school'){Status ($export+'&schoolId='+$other.schoolId) $headers 404}
    if($login -eq 'web-empty'){Check 'empty scope header-only report' ($rows.Count -eq 0)}
    $xlsxPath=Join-Path $OutputDirectory "$login.xlsx"
    $xlsx=Invoke-WebRequest ($BaseUrl+$export.Replace('format=csv','format=xlsx')) -Headers $headers -OutFile $xlsxPath -PassThru
    Check "$login XLSX MIME" ($xlsx.Headers['Content-Type'] -like '*spreadsheetml.sheet*')
}
$selected=$export+'&schoolId='+$school.schoolId+'&deviceIds='+$first.deviceId+','+$second.deviceId+'&status=Offline&fields=school,computer,room,date,time,download,status'
$selectedFile=Join-Path $OutputDirectory 'selected-offline.csv'
Invoke-WebRequest ($BaseUrl+$selected) -Headers $admin -OutFile $selectedFile | Out-Null
$selectedRows=@(Import-Csv $selectedFile -Delimiter ';' -Encoding utf8)
Check 'two-device filter' ($selectedRows.Count -eq 2)
Check 'status filter' (@($selectedRows|Where-Object 'Статус соединения' -ne 'Offline').Count -eq 0)
Check 'missing metric remains empty' (@($selectedRows|Where-Object 'Download, Мбит/с' -ne '').Count -eq 0)
Check 'selected column count' (@($selectedRows[0].PSObject.Properties).Count -eq 7)
$summaryPath=Join-Path $OutputDirectory 'summary.csv'
$summaryUrl='/api/reports/export?kind=summary&format=csv&'+$period+'&schoolId='+$school.schoolId
Invoke-WebRequest ($BaseUrl+$summaryUrl) -Headers $admin -OutFile $summaryPath | Out-Null
Invoke-WebRequest ($BaseUrl+$summaryUrl.Replace('format=csv','format=xlsx')) -Headers $admin -OutFile (Join-Path $OutputDirectory 'summary.xlsx') | Out-Null
$summary=@(Import-Csv $summaryPath -Delimiter ';' -Encoding utf8)
$analytics=Invoke-RestMethod ("$BaseUrl/api/analytics?schoolId="+$school.schoolId+'&'+$period) -Headers $admin
Check 'summary school count' ($summary.Count -eq 1)
Check 'summary reconciles measurement count' ([int]$summary[0].'Количество замеров' -eq $analytics.measurementCount)
Check 'summary reconciles saved threshold problems' ([int]$summary[0].'Проблемных замеров' -eq $analytics.problemMeasurementCount)
Check 'summary reconciles download average' ([Math]::Abs([double]::Parse($summary[0].'Средний Download, Мбит/с',[Globalization.CultureInfo]::InvariantCulture)-$analytics.averageDownloadMbps) -lt 0.00001)
$results|ConvertTo-Json -Depth 4|Set-Content (Join-Path $OutputDirectory 'results.json') -Encoding utf8
Write-Output "PASS: $($results.Count) report integration checks. Files: $OutputDirectory"
