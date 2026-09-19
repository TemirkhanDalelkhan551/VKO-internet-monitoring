param([string]$BaseUrl='http://localhost:5097')
$ErrorActionPreference='Stop'
if(-not ([uri]$BaseUrl).IsLoopback){throw 'Use isolated local web fixtures only.'}
$taskChecks=0
function Check($name,$condition){if(-not $condition){throw "FAILED: $name"};$script:taskChecks++;Write-Output "PASS: $name"}
function Login($login){$session=Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -ContentType application/json -Body (@{login=$login;password='web-test-only-2026'}|ConvertTo-Json);return @{Authorization="Bearer $($session.accessToken)"}}
function Post($path,$body,$headers){return Invoke-WebRequest "$BaseUrl$path" -Method Post -Headers $headers -ContentType application/json -Body ($body|ConvertTo-Json) -SkipHttpErrorCheck}
$admin=Login 'web-admin'
try{
 $schools=Invoke-RestMethod "$BaseUrl/api/schools" -Headers $admin
 $school=$schools|Where-Object districtCity -eq 'Усть-Каменогорск'
 $foreign=$schools|Where-Object districtCity -eq 'Риддер'
 $line=$school.lines|Where-Object lineStatus -eq 'Primary'
 $body=@{schoolId=$school.schoolId;lineId=$line.lineId;lifetimeMinutes=30}
 Check 'anonymous issuance denied' ((Post '/api/activation-codes' $body @{}).StatusCode -eq 401)
 foreach($role in @('school','district','provider','empty','regional')){
  $headers=Login "web-$role"
  try{$response=Post '/api/activation-codes' $body $headers;$expected=if($role -eq 'regional'){201}else{403};Check "$role issuance permissions" ($response.StatusCode -eq $expected)}finally{Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $headers|Out-Null}
 }
 $response=Post '/api/activation-codes' $body $admin
 Check 'administrator issues code' ($response.StatusCode -eq 201)
 Check 'code response cannot be cached' ($response.Headers['Cache-Control'] -contains 'no-store')
 $issued=$response.Content|ConvertFrom-Json -DateKind String
 Check 'code and future expiry returned' ($issued.activationCode.Length -gt 10 -and [DateTimeOffset]::Parse($issued.expiresAtUtc) -gt [DateTimeOffset]::UtcNow)
 Check 'malformed preview denied' ((Post '/api/devices/activation-preview' @{activationCode='bad'} @{}).StatusCode -eq 400)
 $preview=Post '/api/devices/activation-preview' @{activationCode=$issued.activationCode} @{}
 Check 'valid code can be previewed without consumption' ($preview.StatusCode -eq 200)
 $previewData=$preview.Content|ConvertFrom-Json -DateKind String
 Check 'preview identifies selected school and line' ($previewData.schoolId -eq $school.schoolId -and $previewData.lineId -eq $line.lineId -and $previewData.schoolName -eq $school.name -and $previewData.lineName -eq $line.name)
 $activate=@{activationCode=$issued.activationCode;deviceIdentifier=[guid]::NewGuid().ToString();deviceName='Тест веб-активации';room='Тестовый кабинет';connectionType='Ethernet'}
 $activated=Post '/api/devices/activate' $activate @{}
 Check 'issued code activates installer API' ($activated.StatusCode -eq 201)
 $device=$activated.Content|ConvertFrom-Json
 Check 'device belongs to selected school and line' ($device.schoolId -eq $school.schoolId -and $device.lineId -eq $line.lineId -and $device.deviceToken.Length -gt 10)
 Check 'code cannot be reused' ((Post '/api/devices/activate' $activate @{}).StatusCode -eq 401)
 Check 'used code cannot be previewed' ((Post '/api/devices/activation-preview' @{activationCode=$issued.activationCode} @{}).StatusCode -eq 401)
 foreach($minutes in @(4,1441)){$body.lifetimeMinutes=$minutes;Check "invalid lifetime $minutes denied" ((Post '/api/activation-codes' $body $admin).StatusCode -eq 400)}
 $body.lifetimeMinutes=5;$body.lineId=$foreign.lines[0].lineId
 Check 'foreign school-line pairing rejected' ((Post '/api/activation-codes' $body $admin).StatusCode -eq 404)
 $audit=Invoke-RestMethod "$BaseUrl/api/audit?limit=100" -Headers $admin
 Check 'issuance is audited' (@($audit|Where-Object{$_.path -eq '/api/activation-codes' -and $_.statusCode -eq 201}).Count -ge 2)
 Check 'audit does not contain issued code' (($audit|ConvertTo-Json -Depth 8) -notlike "*$($issued.activationCode)*")
 Write-Output "Web activation: $taskChecks checks passed."
}finally{Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $admin|Out-Null}
