param([string]$PublishDirectory = 'artifacts/api-test', [int]$Port = 5090)
$ErrorActionPreference='Stop'
$taskRepo=Split-Path $PSScriptRoot -Parent
if(![IO.Path]::IsPathRooted($PublishDirectory)){$PublishDirectory=Join-Path $taskRepo $PublishDirectory}
$taskAssembly=Join-Path $PublishDirectory 'VkoMonitoring.Api.dll'
if(!(Test-Path -LiteralPath $taskAssembly)){throw 'Publish API to PublishDirectory before starting fixtures.'}
if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw 'Fixture port is already occupied. Choose another test port.'}
$taskWork=Join-Path $taskRepo 'artifacts/web-fixtures'
New-Item -ItemType Directory -Force -Path $taskWork|Out-Null
$db='vko_web_'+(Get-Date -Format yyyyMMddHHmmss)
docker exec vko-monitoring-postgres-1 psql -U vko_monitoring -d postgres -c "CREATE DATABASE $db;"|Out-Null
if($LASTEXITCODE -ne 0){throw 'Fixture database creation failed'}
$db|Set-Content "$taskWork/web-fixture-db.txt"
$env:ASPNETCORE_URLS=("http://localhost:"+$Port)
$env:ASPNETCORE_ENVIRONMENT='Production'
$env:MonitoringApi__StorageProvider='PostgreSql'
$env:MonitoringApi__EnableLegacyAdminToken='true'
$env:MonitoringApi__AdminToken=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:ConnectionStrings__MonitoringDatabase="Host=localhost;Port=55432;Database=$db;Username=vko_monitoring;Password=vko_dev_password"
function Start-FixtureApi {
 $script:taskApi=Start-Process 'C:/Program Files/dotnet/dotnet.exe' -ArgumentList @('"'+$taskAssembly+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$taskWork/web-fixture.stdout.log" -RedirectStandardError "$taskWork/web-fixture.stderr.log"
 for($i=0;$i -lt 30;$i++){if($taskApi.HasExited){throw 'Fixture API exited before readiness'};try{if((Invoke-RestMethod ("http://localhost:"+$Port+"/health/ready") -TimeoutSec 2).status -eq 'Healthy'){return}}catch{};Start-Sleep -Milliseconds 300}
 throw 'Fixture API failed readiness'
}
function FixturePost($path,$body,$headers) {Invoke-RestMethod (("http://localhost:"+$Port)+$path) -Method Post -Headers $headers -Body ($body|ConvertTo-Json -Depth 8) -ContentType application/json}
Start-FixtureApi
$admin=@{'X-Admin-Token'=$env:MonitoringApi__AdminToken}
$main=FixturePost '/api/devices/register' @{schoolName='Школа «Восток»';districtCity='Усть-Каменогорск';lineName='Основной канал';providerName='Main telecom';lineStatus='Primary';deviceName='Кабинет 101';deviceIdentifier=[guid]::NewGuid().ToString()} $admin
$backup=FixturePost '/api/devices/register' @{schoolId=$main.schoolId;schoolName='Школа «Восток»';districtCity='Усть-Каменогорск';lineId=[guid]::NewGuid().ToString();lineName='Резервный канал';providerName='Reserve telecom';lineStatus='Backup';deviceName='Кабинет 102';deviceIdentifier=[guid]::NewGuid().ToString()} $admin
$other=FixturePost '/api/devices/register' @{schoolName='Лицей № 2';districtCity='Риддер';lineName='Основной канал';providerName='Other telecom';lineStatus='Primary';deviceName='Кабинет 201';deviceIdentifier=[guid]::NewGuid().ToString()} $admin
foreach($pair in @(@{binding=$main;age=2880;down=50},@{binding=$backup;age=1;down=50},@{binding=$other;age=1;down=8})) {
 $binding=$pair.binding;$headers=@{'X-Device-Token'=$binding.deviceToken}
 $null=FixturePost '/api/devices/heartbeat' @{schoolId=$binding.schoolId;lineId=$binding.lineId;deviceId=$binding.deviceId;sentAtUtc=[DateTimeOffset]::UtcNow.ToString('o');agentVersion='web-fixture'} $headers
 $null=FixturePost '/api/measurements' @{eventId=[guid]::NewGuid().ToString();schoolId=$binding.schoolId;lineId=$binding.lineId;deviceId=$binding.deviceId;measuredAtUtc=[DateTimeOffset]::UtcNow.AddMinutes(-$pair.age).ToString('o');downloadMbps=$pair.down;uploadMbps=50;pingMilliseconds=20;jitterMilliseconds=1;packetLossPercent=0;connectionStatus='Online';agentVersion='web-fixture';durationMilliseconds=1000;networkConnectionType='Ethernet'} $headers
}
foreach($user in @(
 @{login='web-admin';displayName='Тестовый администратор';role='Administrator'},
 @{login='web-school';displayName='Представитель школы';role='School';schoolId=$main.schoolId},
 @{login='web-provider';displayName='Представитель поставщика';role='Provider';providerName='Reserve telecom'},
 @{login='web-district';displayName='Отдел образования';role='District';districtCity='Усть-Каменогорск'},
 @{login='web-regional';displayName='Областной оператор';role='Regional'},
 @{login='web-empty';displayName='Нет назначенных школ';role='District';districtCity='Пустой район'}
)) {$user.password='web-test-only-2026';$null=FixturePost '/api/users' $user $admin}
$fixtureSql=@'
INSERT INTO measurements(event_id,school_id,device_id,line_id,measured_at_utc,download_mbps,upload_mbps,ping_milliseconds,jitter_milliseconds,packet_loss_percent,connection_status,failure_kind,failure_reason,agent_version,duration_milliseconds,threshold_download_mbps,threshold_upload_mbps,threshold_ping_milliseconds,threshold_jitter_milliseconds,threshold_packet_loss_percent)
SELECT gen_random_uuid(),school_id,device_id,line_id,now()-make_interval(hours=>s.n),CASE WHEN s.n=4 THEN NULL ELSE 20+s.n END,CASE WHEN s.n=4 THEN NULL ELSE 15 END,CASE WHEN s.n=4 THEN NULL ELSE 35+s.n END,CASE WHEN s.n=4 THEN NULL ELSE 2 END,CASE WHEN s.n=4 THEN NULL ELSE 0 END,CASE WHEN s.n=4 THEN 'Offline' ELSE 'Online' END,'None',CASE WHEN s.n=4 THEN 'Тестовая потеря соединения' ELSE NULL END,'web-fixture',1000,CASE WHEN s.n=2 THEN 10 ELSE threshold_download_mbps END,CASE WHEN s.n=2 THEN 10 ELSE threshold_upload_mbps END,threshold_ping_milliseconds,threshold_jitter_milliseconds,threshold_packet_loss_percent
FROM measurements m CROSS JOIN generate_series(2,8) s(n) WHERE m.agent_version='web-fixture';
UPDATE devices SET room='101' WHERE name='Кабинет 101';
'@
$fixtureSql|docker exec -i vko-monitoring-postgres-1 psql -U vko_monitoring -d $db -v ON_ERROR_STOP=1|Out-Null
if($LASTEXITCODE -ne 0){throw 'Fixture measurement seed failed'}
Stop-Process -Id $taskApi.Id
$env:MonitoringApi__EnableLegacyAdminToken='false'
Start-FixtureApi
$taskApi.Id|Set-Content "$taskWork/web-fixture.pid"
Write-Output "Web fixtures ready at http://localhost:$Port; login web-admin / web-test-only-2026 (test database only). PID: $($taskApi.Id)"
