param([string]$BaseUrl='http://localhost:5098')
$ErrorActionPreference='Stop'
if(-not ([uri]$BaseUrl).IsLoopback){throw 'Use isolated local fixtures only.'}
$taskChecks=0
function Check($name,$condition){if(-not $condition){throw "FAILED: $name"};$script:taskChecks++;Write-Output "PASS: $name"}
function Login($login){$session=Invoke-RestMethod "$BaseUrl/api/auth/login" -Method Post -ContentType application/json -Body (@{login=$login;password='web-test-only-2026'}|ConvertTo-Json);return @{Authorization="Bearer $($session.accessToken)"}}
function WriteApi($path,$method,$body,$headers){return Invoke-WebRequest "$BaseUrl$path" -Method $method -Headers $headers -ContentType application/json -Body ($body|ConvertTo-Json) -SkipHttpErrorCheck}
$admin=Login 'web-admin'
try{
 $schools=Invoke-RestMethod "$BaseUrl/api/schools" -Headers $admin
 $main=$schools|Where-Object districtCity -eq 'Усть-Каменогорск'
 $other=$schools|Where-Object districtCity -eq 'Риддер'
 $lineBody=@{name='Линия сценария инцидентов';providerName='Reserve telecom';lineStatus='Backup'}
 $line=(WriteApi "/api/schools/$($main.schoolId)/lines" Post $lineBody $admin).Content|ConvertFrom-Json
 $foreignLine=(WriteApi "/api/schools/$($other.schoolId)/lines" Post @{name='Чужая тестовая линия';providerName='Other telecom';lineStatus='Backup'} $admin).Content|ConvertFrom-Json
 $body=@{schoolId=$main.schoolId;lineId=$line.lineId;problemType='Потеря соединения';title='Тестовый инцидент';description='Описание для проверки веб-панели';actor='spoofed-actor'}
 Check 'anonymous incident creation denied' ((WriteApi '/api/incidents' Post $body @{}).StatusCode -eq 401)
 $foreignBody=$body.Clone();$foreignBody.schoolId=$other.schoolId;$foreignBody.lineId=$foreignLine.lineId
 $foreign=(WriteApi '/api/incidents' Post $foreignBody $admin).Content|ConvertFrom-Json
 $school=Login 'web-school'
 try{
  $response=WriteApi '/api/incidents' Post $body $school
  Check 'school creates own incident' ($response.StatusCode -eq 201)
  $id=($response.Content|ConvertFrom-Json).incidentId
  Check 'duplicate open incident rejected' ((WriteApi '/api/incidents' Post $body $school).StatusCode -eq 409)
  Check 'school cannot create foreign incident' ((WriteApi '/api/incidents' Post $foreignBody $school).StatusCode -eq 403)
  Check 'school cannot change status' ((WriteApi "/api/incidents/$id/status" Put @{status='SentToProvider';actor='test'} $school).StatusCode -eq 403)
  Check 'school cannot comment' ((WriteApi "/api/incidents/$id/comments" Post @{comment='test';actor='test'} $school).StatusCode -eq 403)
  Check 'school cannot view foreign incident' ((Invoke-WebRequest "$BaseUrl/api/incidents/$($foreign.incidentId)" -Headers $school -SkipHttpErrorCheck).StatusCode -eq 404)
 }finally{Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $school|Out-Null}
 Check 'invalid status transition rejected' ((WriteApi "/api/incidents/$id/status" Put @{status='Closed';actor='test'} $admin).StatusCode -eq 409)
 Check 'administrator sends to provider' ((WriteApi "/api/incidents/$id/status" Put @{status='SentToProvider';actor='spoofed-actor'} $admin).StatusCode -eq 204)
 $provider=Login 'web-provider'
 try{
  Check 'provider starts work' ((WriteApi "/api/incidents/$id/status" Put @{status='InProgress';actor='spoofed-actor';comment='Принято в работу'} $provider).StatusCode -eq 204)
  Check 'provider adds comment' ((WriteApi "/api/incidents/$id/comments" Post @{comment='Проверка линии';actor='spoofed-actor'} $provider).StatusCode -eq 204)
  Check 'provider cannot assign responsible' ((WriteApi "/api/incidents/$id/assignment" Put @{assignedTo='test';actor='test'} $provider).StatusCode -eq 403)
  Check 'provider cannot modify foreign incident' ((WriteApi "/api/incidents/$($foreign.incidentId)/status" Put @{status='SentToProvider';actor='test'} $provider).StatusCode -eq 404)
  $visible=Invoke-RestMethod "$BaseUrl/api/incidents?schoolId=$($main.schoolId)&status=InProgress" -Headers $provider
  Check 'provider filtered list includes own incident' ($visible.incidentId -contains $id)
 }finally{Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $provider|Out-Null}
 $district=Login 'web-district'
 try{
  Check 'district reads own incident' ((Invoke-WebRequest "$BaseUrl/api/incidents/$id" -Headers $district).StatusCode -eq 200)
  Check 'district cannot modify incident' ((WriteApi "/api/incidents/$id/status" Put @{status='Resolved';actor='test'} $district).StatusCode -eq 403)
 }finally{Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $district|Out-Null}
 $regional=Login 'web-regional'
 try{Check 'regional assigns responsible' ((WriteApi "/api/incidents/$id/assignment" Put @{assignedTo='Тестовый инженер';actor='spoofed-actor'} $regional).StatusCode -eq 204)}finally{Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $regional|Out-Null}
 foreach($status in @('WaitingForInformation','InProgress','Resolved','Closed')){Check "administrator transition $status" ((WriteApi "/api/incidents/$id/status" Put @{status=$status;actor='spoofed-actor'} $admin).StatusCode -eq 204)}
 $details=Invoke-RestMethod "$BaseUrl/api/incidents/$id" -Headers $admin
 Check 'final status and lifecycle timestamps returned' ($details.incident.status -eq 'Closed' -and $null -ne $details.incident.sentToProviderAtUtc -and $null -ne $details.incident.recoveredAtUtc -and $null -ne $details.incident.closedAtUtc)
 Check 'assignment and comment appear in history' ($details.incident.assignedTo -eq 'Тестовый инженер' -and $details.history.comment -contains 'Проверка линии')
 Check 'history actor cannot be spoofed by client' ($details.history.actor -notcontains 'spoofed-actor')
 Check 'invalid period rejected' ((Invoke-WebRequest "$BaseUrl/api/incidents?from=2026-09-19T00:00:00Z&to=2026-09-18T00:00:00Z" -Headers $admin -SkipHttpErrorCheck).StatusCode -eq 400)
 Write-Output "Web incidents: $taskChecks checks passed."
}finally{Invoke-RestMethod "$BaseUrl/api/auth/logout" -Method Post -Headers $admin|Out-Null}
