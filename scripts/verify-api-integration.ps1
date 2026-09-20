[CmdletBinding()]
param([int]$ApiPort=5084)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$work=Join-Path $root 'data/integration-tests'
New-Item -ItemType Directory -Path $work -Force | Out-Null
if(Get-NetTCPConnection -LocalPort $ApiPort -State Listen -ErrorAction SilentlyContinue){throw "Integration port is already in use."}
$buildArtifacts=Join-Path $root 'artifacts/integration-build'
dotnet build (Join-Path $root 'src/VkoMonitoring.Api/VkoMonitoring.Api.csproj') --configuration Release --artifacts-path $buildArtifacts
if($LASTEXITCODE -ne 0){throw 'Integration API build failed.'}
$apiPath=Join-Path $buildArtifacts 'bin/VkoMonitoring.Api/release/VkoMonitoring.Api.dll'
$results=[Collections.Generic.List[object]]::new()
function Check($name,$ok,$detail='') { $results.Add([pscustomobject]@{name=$name;passed=[bool]$ok;detail=$detail}); Write-Output "$name : $ok $detail" }
function Req($method,$path,$body=$null,$headers=@{}) {
 $args=@{Uri="http://localhost:$ApiPort$path";Method=$method;Headers=$headers;SkipHttpErrorCheck=$true;TimeoutSec=15}
 if($null -ne $body){$args.Body=ConvertTo-Json -InputObject $body -Depth 8;$args.ContentType='application/json'}
 $r=Invoke-WebRequest @args
 $data=$null;try{$data=$r.Content|ConvertFrom-Json}catch{}
 return [pscustomobject]@{code=[int]$r.StatusCode;data=$data;content=$r.Content}
}
$db='vko_verify_'+(Get-Date -Format 'yyyyMMddHHmmss')+'_'+[guid]::NewGuid().ToString('N').Substring(0,8)
docker exec vko-monitoring-postgres-1 psql -U vko_monitoring -d postgres -c "CREATE DATABASE $db;" | Out-Null
if($LASTEXITCODE -ne 0){throw 'Test database creation failed'}
$env:MonitoringApi__EnableLegacyAdminToken='true'
$env:MonitoringApi__StorageProvider='PostgreSql'
$env:MonitoringApi__AdminToken=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$admin=@{'X-Admin-Token'=$env:MonitoringApi__AdminToken}
$env:ConnectionStrings__MonitoringDatabase="Host=localhost;Port=55432;Database=$db;Username=vko_monitoring;Password=vko_dev_password"
$env:ASPNETCORE_URLS="http://localhost:$ApiPort"
$env:ASPNETCORE_ENVIRONMENT='Production'
$api=$null;$agent=$null
try {
 $api=Start-Process 'C:/Program Files/dotnet/dotnet.exe' -ArgumentList @('"'+$apiPath+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$work/users-test.stdout.log" -RedirectStandardError "$work/users-test.stderr.log"
 for($i=0;$i -lt 30;$i++){try{if((Req GET '/health/ready').code -eq 200){break}}catch{};Start-Sleep -Milliseconds 300}
 Check 'readiness' ((Req GET '/health/ready').data.status -eq 'Healthy')
 Check 'admin_missing_token' ((Req GET '/api/schools').code -eq 401)
 Check 'admin_wrong_token' ((Req GET '/api/schools' $null @{'X-Admin-Token'='invalid'}).code -eq 401)
 $reg=Req POST '/api/devices/register' @{schoolName='Verification school';districtCity='Verification';lineName='Primary';providerName='Test provider';contractedDownloadMbps=50;contractedUploadMbps=50;lineStatus='Primary';deviceName='Test device';deviceIdentifier=[guid]::NewGuid().ToString();room='TEST'} $admin
 Check 'register_device' ($reg.code -eq 201)
 $binding=$reg.data;$dh=@{'X-Device-Token'=$binding.deviceToken}
 $hb=@{schoolId=$binding.schoolId;deviceId=$binding.deviceId;lineId=$binding.lineId;sentAtUtc=[DateTimeOffset]::UtcNow.ToString('o');agentVersion='verification'}
 Check 'heartbeat' ((Req POST '/api/devices/heartbeat' $hb $dh).code -eq 204)
 $settings=Req GET '/api/settings/operations' $null $admin
 Check 'operational_settings_read' ($settings.code -eq 200 -and @($settings.data.measurementWindows).Count -eq 4)
 Check 'operational_settings_invalid_rejected' ((Req PUT '/api/settings/operations' @{measurementWindows=@('bad');minimumDownloadMbps=20;minimumUploadMbps=20;maximumPingMilliseconds=100;maximumJitterMilliseconds=30;maximumPacketLossPercent=2;minimumAvailabilityPercent=99} $admin).code -eq 400)
 $updatedSettings=Req PUT '/api/settings/operations' @{measurementWindows=@('09:00-10:00','15:00-16:00');minimumDownloadMbps=25;minimumUploadMbps=15;maximumPingMilliseconds=90;maximumJitterMilliseconds=25;maximumPacketLossPercent=1.5;minimumAvailabilityPercent=98.5} $admin
 Check 'operational_settings_update' ($updatedSettings.code -eq 200 -and $updatedSettings.data.minimumDownloadMbps -eq 25)
 $configuration=Req GET "/api/devices/configuration?schoolId=$($binding.schoolId)&deviceId=$($binding.deviceId)&lineId=$($binding.lineId)" $null $dh
 Check 'agent_receives_remote_schedule' ($configuration.code -eq 200 -and @($configuration.data.measurementWindows).Count -eq 2 -and $configuration.data.measurementWindows[0] -eq '09:00-10:00')
 $bad=$hb.Clone();$bad.schoolId=[guid]::NewGuid().ToString()
 Check 'school_spoof_rejected' ((Req POST '/api/devices/heartbeat' $bad $dh).code -eq 401)
 $bad=$hb.Clone();$bad.lineId=[guid]::NewGuid().ToString()
 Check 'line_spoof_rejected' ((Req POST '/api/devices/heartbeat' $bad $dh).code -eq 401)
 $statusPath="/api/devices/$($binding.deviceId)/status?schoolId=$($binding.schoolId)&lineId=$($binding.lineId)"
 Check 'own_status' ((Req GET $statusPath $null $dh).code -eq 200)
 Check 'foreign_device_rejected' ((Req GET ($statusPath.Replace($binding.deviceId,[guid]::NewGuid().ToString())) $null $dh).code -eq 401)
 $activation=Req POST '/api/activation-codes' @{schoolId=$binding.schoolId;lineId=$binding.lineId;lifetimeMinutes=5} $admin
 Check 'create_activation_code' ($activation.code -eq 201)
 $ab=@{activationCode=$activation.data.activationCode;deviceName='Activation test';deviceIdentifier=[guid]::NewGuid().ToString();connectionType='Ethernet'}
 $activated=Req POST '/api/devices/activate' $ab
 Check 'activate_once' ($activated.code -eq 201 -and -not $activated.data.recovered)
 $recoveryPreview=Req POST '/api/devices/activation-preview' @{activationCode=$ab.activationCode;deviceIdentifier=$ab.deviceIdentifier}
 Check 'activation_recovery_preview' ($recoveryPreview.code -eq 200 -and $recoveryPreview.data.isRecovery)
 $recovered=Req POST '/api/devices/activate' $ab
 Check 'activation_recovery_same_machine' ($recovered.code -eq 201 -and $recovered.data.recovered -and $recovered.data.deviceId -eq $activated.data.deviceId -and $recovered.data.deviceToken -ne $activated.data.deviceToken)
 $foreignRecovery=$ab.Clone();$foreignRecovery.deviceIdentifier=[guid]::NewGuid().ToString()
 Check 'activation_recovery_foreign_machine_rejected' ((Req POST '/api/devices/activate' $foreignRecovery).code -eq 401)
 $start=[DateTimeOffset]::UtcNow.AddMinutes(-10)
 function New-TestMeasurement($offset,$download=50,$state='Online') {
  @{eventId=[guid]::NewGuid().ToString();schoolId=$binding.schoolId;deviceId=$binding.deviceId;lineId=$binding.lineId;measuredAtUtc=$start.AddSeconds($offset).ToString('o');downloadMbps=$download;uploadMbps=50;pingMilliseconds=20;jitterMilliseconds=1;packetLossPercent=0;connectionStatus=$state;agentVersion='verification';durationMilliseconds=1000;networkConnectionType='Ethernet';externalIpAddress='198.51.100.99'}
 }
 $m=New-TestMeasurement 0
 Check 'measurement_accepted' ((Req POST '/api/measurements' $m $dh).code -eq 201)
 Check 'duplicate_acknowledged' ((Req POST '/api/measurements' $m $dh).data.duplicate -eq $true)
 $hist=Req GET "/api/devices/$($binding.deviceId)/measurements" $null $admin
 Check 'duplicate_not_stored' (@($hist.data).Count -eq 1)
 Check 'external_ip_overridden' (@($hist.data)[0].externalIpAddress -eq '127.0.0.1')
 $invalid=New-TestMeasurement 1;$invalid.downloadMbps=-1
 Check 'negative_metric_rejected' ((Req POST '/api/measurements' $invalid $dh).code -eq 400)
 $invalid=New-TestMeasurement 1;$invalid.connectionStatus='Online';$invalid.downloadMbps=$null;$invalid.uploadMbps=$null;$invalid.pingMilliseconds=$null;$invalid.jitterMilliseconds=$null;$invalid.packetLossPercent=$null
 $missing=Req POST '/api/measurements' $invalid $dh
 Check 'online_missing_metrics_rejected' ($missing.code -eq 400) "HTTP $($missing.code)"
 $invalid=New-TestMeasurement 2;$invalid.measuredAtUtc=[DateTimeOffset]::UtcNow.AddDays(10).ToString('o')
 $future=Req POST '/api/measurements' $invalid $dh
 Check 'future_timestamp_rejected' ($future.code -eq 400) "HTTP $($future.code)"
 # Use another line so negative-input probes do not affect incident sequences.
 $reg2=Req POST '/api/devices/register' @{schoolName='Incident test';lineName='Primary';lineStatus='Primary';deviceName='Incident test';deviceIdentifier=[guid]::NewGuid().ToString()} $admin
 $binding=$reg2.data;$dh=@{'X-Device-Token'=$binding.deviceToken}
 $null=Req POST '/api/measurements' (New-TestMeasurement 10 10) $dh
 Check 'single_failure_no_incident' (@((Req GET "/api/incidents?schoolId=$($binding.schoolId)" $null $admin).data).Count -eq 0)
 $null=Req POST '/api/measurements' (New-TestMeasurement 20 10) $dh
 $incidents=Req GET "/api/incidents?schoolId=$($binding.schoolId)" $null $admin
 Check 'two_failures_open_incident' (@($incidents.data).Count -eq 1 -and @($incidents.data)[0].status -eq 'New')
 $iid=@($incidents.data)[0].incidentId
 $null=Req POST '/api/measurements' (New-TestMeasurement 30) $dh
 $null=Req POST '/api/measurements' (New-TestMeasurement 40) $dh
 Check 'two_recoveries_resolve' ((Req GET "/api/incidents/$iid" $null $admin).data.incident.status -eq 'Resolved')
 Check 'close_incident' ((Req PUT "/api/incidents/$iid/status" @{status='Closed';actor='verification';comment='closed'} $admin).code -eq 204)
 $manual=Req POST '/api/incidents' @{schoolId=$binding.schoolId;lineId=$binding.lineId;problemType='ManualTest';title='Test';description='Verification';actor='verification'} $admin
 Check 'manual_incident' ($manual.code -eq 201)
 $mid=$manual.data.incidentId
 Check 'invalid_status_transition' ((Req PUT "/api/incidents/$mid/status" @{status='Closed';actor='verification'} $admin).code -eq 409)
 Check 'assign_incident' ((Req PUT "/api/incidents/$mid/assignment" @{assignedTo='Test operator';actor='verification'} $admin).code -eq 204)
 Check 'comment_incident' ((Req POST "/api/incidents/$mid/comments" @{comment='Test comment';actor='verification'} $admin).code -eq 204)
 foreach($state in @('SentToProvider','InProgress','WaitingForInformation','InProgress','Resolved','Closed')) {Check "manual_status_$state" ((Req PUT "/api/incidents/$mid/status" @{status=$state;actor='verification'} $admin).code -eq 204)}
 Check 'incident_history' (@((Req GET "/api/incidents/$mid" $null $admin).data.history).Count -ge 9)
 Check 'invalid_period_rejected' ((Req GET '/api/analytics?from=2026-09-19&to=2026-09-18' $null $admin).code -eq 400)
 Check 'analytics' ((Req GET "/api/analytics?schoolId=$($binding.schoolId)" $null $admin).data.measurementCount -eq 4)
 Check 'school_read' ((Req GET "/api/schools/$($binding.schoolId)" $null $admin).code -eq 200)
 Check 'devices_read' (@((Req GET "/api/schools/$($binding.schoolId)/devices" $null $admin).data).Count -eq 1)
 $null=Req PUT "/api/devices/$($binding.deviceId)/block-state" @{isBlocked=$true} $admin
 Check 'blocked_measurement_rejected' ((Req POST '/api/measurements' (New-TestMeasurement 50) $dh).code -eq 401)
 $rot=Req POST "/api/devices/$($binding.deviceId)/token/rotate" @{} $admin
 $newDh=@{'X-Device-Token'=$rot.data.deviceToken}
 Check 'rotate_keeps_block' ((Req POST '/api/measurements' (New-TestMeasurement 50) $newDh).code -eq 401)
 $null=Req PUT "/api/devices/$($binding.deviceId)/block-state" @{isBlocked=$false} $admin
 Check 'old_token_revoked' ((Req POST '/api/measurements' (New-TestMeasurement 50) $dh).code -eq 401)
 Check 'new_token_accepted' ((Req POST '/api/measurements' (New-TestMeasurement 50) $newDh).code -eq 201)
 # Regression: separate primary and backup; use a dedicated school and explicit IDs.
 $primary=Req POST '/api/devices/register' @{schoolName='Line status regression';lineName='Main';providerName='Main provider';lineStatus='Primary';deviceName='Main device';deviceIdentifier=[guid]::NewGuid().ToString()} $admin
 $binding=$primary.data;$dh=@{'X-Device-Token'=$binding.deviceToken}
 $mainBinding=$binding;$mainDh=$dh
 $old=New-TestMeasurement 0;$old.measuredAtUtc=[DateTimeOffset]::UtcNow.AddDays(-2).ToString('o')
 Check 'old_queue_measurement_accepted' ((Req POST '/api/measurements' $old $dh).code -eq 201)
 $backup=Req POST '/api/devices/register' @{schoolId=$binding.schoolId;schoolName='Line status regression';lineId=[guid]::NewGuid().ToString();lineName='Reserve';providerName='Backup provider';lineStatus='Backup';deviceName='Backup device';deviceIdentifier=[guid]::NewGuid().ToString()} $admin
 Check 'backup_registration' ($backup.code -eq 201)
 $lifecycleOld=Req POST '/api/devices/register' @{schoolName='Lifecycle verification';districtCity='Lifecycle';lineName='Lifecycle main';providerName='Lifecycle ISP';lineStatus='Primary';deviceName='Lifecycle old';deviceIdentifier=[guid]::NewGuid().ToString()} $admin
 $lifecycleNew=Req POST '/api/devices/register' @{schoolId=$lifecycleOld.data.schoolId;schoolName='Lifecycle verification';lineId=$lifecycleOld.data.lineId;lineName='Lifecycle main';providerName='Lifecycle ISP';lineStatus='Primary';deviceName='Lifecycle new';deviceIdentifier=[guid]::NewGuid().ToString()} $admin
 $lifecycleReserve=Req POST '/api/devices/register' @{schoolId=$lifecycleOld.data.schoolId;schoolName='Lifecycle verification';lineId=[guid]::NewGuid().ToString();lineName='Lifecycle reserve';providerName='Lifecycle ISP';lineStatus='Backup';deviceName='Lifecycle reserve seed';deviceIdentifier=[guid]::NewGuid().ToString()} $admin
 $rebound=Req PUT "/api/devices/$($lifecycleOld.data.deviceId)/binding" @{schoolId=$lifecycleOld.data.schoolId;lineId=$lifecycleReserve.data.lineId;reason='Moved to reserve line'} $admin
 Check 'device_rebind' ($rebound.code -eq 200 -and $rebound.data.lineId -eq $lifecycleReserve.data.lineId)
 $replaced=Req POST "/api/devices/$($lifecycleOld.data.deviceId)/replace" @{replacementDeviceId=$lifecycleNew.data.deviceId;reason='Planned workstation replacement'} $admin
 Check 'device_replace' ($replaced.code -eq 200 -and $replaced.data.lifecycleStatus -eq 'Replaced' -and $replaced.data.replacedByDeviceId -eq $lifecycleNew.data.deviceId)
 $oldLifecycleHeaders=@{'X-Device-Token'=$lifecycleOld.data.deviceToken}
 $oldLifecycleHeartbeat=@{schoolId=$lifecycleOld.data.schoolId;deviceId=$lifecycleOld.data.deviceId;lineId=$lifecycleReserve.data.lineId;sentAtUtc=[DateTimeOffset]::UtcNow.ToString('o');agentVersion='verification'}
 Check 'replaced_device_token_revoked' ((Req POST '/api/devices/heartbeat' $oldLifecycleHeartbeat $oldLifecycleHeaders).code -eq 401)
 $decommissioned=Req POST "/api/devices/$($lifecycleNew.data.deviceId)/decommission" @{reason='Device removed from service'} $admin
 Check 'device_decommission' ($decommissioned.code -eq 200 -and $decommissioned.data.lifecycleStatus -eq 'Decommissioned')
 $lifecycleHistory=Req GET "/api/devices/$($lifecycleOld.data.deviceId)/lifecycle" $null $admin
 Check 'device_lifecycle_history' ($lifecycleHistory.code -eq 200 -and @($lifecycleHistory.data).Count -eq 2)
 $binding=$backup.data;$dh=@{'X-Device-Token'=$binding.deviceToken}
 $fresh=New-TestMeasurement 0;$fresh.measuredAtUtc=[DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')
 Check 'backup_measurement' ((Req POST '/api/measurements' $fresh $dh).code -eq 201)
 $school=(Req GET "/api/schools/$($mainBinding.schoolId)" $null $admin).data
 Check 'school_has_two_lines' (@($school.lines).Count -eq 2)
 Check 'primary_provider_not_mixed' ($school.providerName -eq 'Main provider' -and $school.primaryLineId -eq $mainBinding.lineId)
 Check 'stale_primary_not_healthy' ($school.status -eq 'Unknown' -and $school.qualityStatus -eq 'Normal' -and $school.measurementFreshness -eq 'Stale')
 Check 'fresh_backup_independent' (@($school.lines|Where-Object lineStatus -eq 'Backup')[0].status -eq 'Normal')
 $devices=(Req GET "/api/schools/$($mainBinding.schoolId)/devices" $null $admin).data
 $mainDevice=@($devices|Where-Object deviceId -eq $mainBinding.deviceId)[0]
 Check 'device_stale_state' ($mainDevice.status -eq 'Unknown' -and $mainDevice.measurementFreshness -eq 'Stale')
 $binding=$mainBinding;$dh=$mainDh
 $offline=New-TestMeasurement 0 0 'Offline';$offline.measuredAtUtc=[DateTimeOffset]::UtcNow.AddSeconds(-10).ToString('o')
 Check 'offline_primary_accepted' ((Req POST '/api/measurements' $offline $dh).code -eq 201)
 $school=(Req GET "/api/schools/$($mainBinding.schoolId)" $null $admin).data
 Check 'backup_cannot_hide_primary_offline' ($school.status -eq 'NoConnection' -and $school.measurementFreshness -eq 'Fresh')
 $null=Req PUT "/api/devices/$($binding.deviceId)/block-state" @{isBlocked=$true} $admin
 $devices=(Req GET "/api/schools/$($binding.schoolId)/devices" $null $admin).data
 Check 'blocked_presence_separate' (@($devices|Where-Object deviceId -eq $binding.deviceId)[0].agentPresence -eq 'Blocked')
 Check 'school_list_matches_card' (@((Req GET '/api/schools' $null $admin).data|Where-Object schoolId -eq $binding.schoolId)[0].status -eq 'NoConnection')
 $testPassword='Verification-only-'+[guid]::NewGuid().ToString()
 $firstAdmin=Req POST '/api/users' @{login='test-admin';displayName='Test administrator';password=$testPassword;role='Administrator'} $admin
 Check 'bootstrap_create_admin' ($firstAdmin.code -eq 201)
 Stop-Process -Id $api.Id
 $env:MonitoringApi__EnableLegacyAdminToken='false'
 $api=Start-Process 'C:/Program Files/dotnet/dotnet.exe' -ArgumentList @('"'+$apiPath+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$work/users-restart.stdout.log" -RedirectStandardError "$work/users-restart.stderr.log"
 for($i=0;$i -lt 30;$i++){try{if((Req GET '/health/ready').code -eq 200){break}}catch{};Start-Sleep -Milliseconds 300}
 Check 'legacy_admin_disabled' ((Req GET '/api/schools' $null $admin).code -eq 401)
 $login=Req POST '/api/auth/login' @{login='TEST-ADMIN';password=$testPassword}
 Check 'admin_login_case_insensitive' ($login.code -eq 200)
 $admin=@{Authorization="Bearer $($login.data.accessToken)"}
 Check 'auth_me' ((Req GET '/api/auth/me' $null $admin).data.login -eq 'test-admin')
 Check 'invalid_password' ((Req POST '/api/auth/login' @{login='test-admin';password='incorrect-password'}).code -eq 401)
 Check 'users_no_password_hash' ((Req GET '/api/users' $null $admin).content -notmatch 'passwordHash|password_hash')
 $users=@{}
 foreach($u in @(
   @{login='school-user';displayName='School user';role='School';schoolId=$mainBinding.schoolId},
   @{login='district-user';displayName='District user';role='District';districtCity='Verification'},
   @{login='regional-user';displayName='Regional user';role='Regional'},
   @{login='provider-user';displayName='Provider user';role='Provider';providerName='Backup provider'},
   @{login='empty-user';displayName='Empty district';role='District';districtCity='Nonexistent district'}
 )) {
  $u.password=$testPassword
  $created=Req POST '/api/users' $u $admin
  Check ("create_"+$u.login) ($created.code -eq 201)
  $signed=Req POST '/api/auth/login' @{login=$u.login;password=$testPassword}
  Check ("login_"+$u.login) ($signed.code -eq 200)
  $users[$u.login]=@{record=$created.data;headers=@{Authorization="Bearer $($signed.data.accessToken)"};token=$signed.data.accessToken}
 }
 $schoolHeaders=$users['school-user'].headers;$providerHeaders=$users['provider-user'].headers
 $list=Req GET '/api/schools' $null $schoolHeaders
 Check 'school_only_own_school' (@($list.data).Count -eq 1 -and @($list.data)[0].schoolId -eq $mainBinding.schoolId)
 Check 'school_sees_both_own_lines' (@($list.data)[0].lines.Count -eq 2)
 Check 'school_foreign_card_hidden' ((Req GET "/api/schools/$($reg.data.schoolId)" $null $schoolHeaders).code -eq 404)
 Check 'school_foreign_device_history_hidden' ((Req GET "/api/devices/$($reg.data.deviceId)/measurements" $null $schoolHeaders).code -eq 404)
 Check 'school_cannot_register_device' ((Req POST '/api/devices/register' @{} $schoolHeaders).code -eq 403)
 Check 'district_only_own_district' (@((Req GET '/api/schools' $null $users['district-user'].headers).data).Count -eq 1)
 Check 'district_cannot_mutate_incidents' ((Req POST '/api/incidents' @{} $users['district-user'].headers).code -eq 403)
 Check 'empty_scope_no_schools' (@((Req GET '/api/schools' $null $users['empty-user'].headers).data).Count -eq 0)
 Check 'empty_scope_no_measurements' (@((Req GET '/api/measurements/recent' $null $users['empty-user'].headers).data).Count -eq 0)
 Check 'empty_scope_no_analytics' ((Req GET '/api/analytics' $null $users['empty-user'].headers).data.measurementCount -eq 0)
 Check 'regional_sees_all_schools' (@((Req GET '/api/schools' $null $users['regional-user'].headers).data).Count -eq @((Req GET '/api/schools' $null $admin).data).Count)
 foreach($key in @('school-user','district-user','regional-user','provider-user')) {
  Check ("users_denied_"+$key) ((Req GET '/api/users' $null $users[$key].headers).code -eq 403)
  Check ("audit_denied_"+$key) ((Req GET '/api/audit' $null $users[$key].headers).code -eq 403)
  Check ("settings_denied_"+$key) ((Req GET '/api/settings/operations' $null $users[$key].headers).code -eq 403)
 }
 Check 'regional_lifecycle_write_denied' ((Req POST ("/api/devices/"+[guid]::NewGuid().ToString()+"/decommission") @{reason='Permission verification'} $users['regional-user'].headers).code -eq 403)
 $providerSchool=(Req GET "/api/schools/$($mainBinding.schoolId)" $null $providerHeaders).data
 Check 'provider_only_own_line' (@($providerSchool.lines).Count -eq 1 -and $providerSchool.lines[0].providerName -eq 'Backup provider')
 Check 'provider_no_other_line_summary' ($null -eq $providerSchool.primaryLineId -and $null -eq $providerSchool.latestMeasurement -and $null -eq $providerSchool.providerName)
 Check 'provider_device_count_scoped' ($providerSchool.deviceCount -eq 1)
 Check 'provider_device_list_scoped' (@((Req GET "/api/schools/$($mainBinding.schoolId)/devices" $null $providerHeaders).data).Count -eq 1)
 Check 'provider_other_line_history_hidden' ((Req GET "/api/devices/$($mainBinding.deviceId)/measurements" $null $providerHeaders).code -eq 404)
 $providerMeasurements=(Req GET '/api/measurements/recent' $null $providerHeaders).data
 Check 'provider_recent_scoped_before_limit' (@($providerMeasurements).Count -eq 1 -and @($providerMeasurements)[0].lineId -eq $backup.data.lineId)
 Check 'provider_analytics_scoped' ((Req GET '/api/analytics' $null $providerHeaders).data.measurementCount -eq 1)
 $manual=Req POST '/api/incidents' @{schoolId=$mainBinding.schoolId;lineId=$mainBinding.lineId;problemType='RoleTest';title='Role test';description='Scoped issue';actor='forged administrator'} $schoolHeaders
 Check 'school_can_create_own_incident' ($manual.code -eq 201)
 $incidentDetail=(Req GET "/api/incidents/$($manual.data.incidentId)" $null $admin).data
 Check 'incident_actor_from_session' (@($incidentDetail.history)[0].actor -eq 'school-user')
 Check 'provider_other_incident_hidden' ((Req GET "/api/incidents/$($manual.data.incidentId)" $null $providerHeaders).code -eq 404)
 Check 'provider_other_incident_write_hidden' ((Req PUT "/api/incidents/$($manual.data.incidentId)/status" @{status='InProgress'} $providerHeaders).code -eq 404)
 Check 'school_foreign_incident_create_denied' ((Req POST '/api/incidents' @{schoolId=$reg.data.schoolId;lineId=$reg.data.lineId;problemType='Bad';title='Bad';description='Bad';actor='spoof'} $schoolHeaders).code -eq 403)
 $adminUpdate=@{displayName='Test administrator';role='Administrator';isBlocked=$true}
 Check 'last_admin_cannot_be_blocked' ((Req PUT "/api/users/$($firstAdmin.data.userId)" $adminUpdate $admin).code -eq 409)
 $adminUpdate.isBlocked=$false;$adminUpdate.role='Regional'
 Check 'last_admin_cannot_be_demoted' ((Req PUT "/api/users/$($firstAdmin.data.userId)" $adminUpdate $admin).code -eq 409)
 $providerRecord=$users['provider-user'].record
 $update=@{displayName=$providerRecord.displayName;role='Provider';providerName=$providerRecord.providerName;isBlocked=$true}
 Check 'block_account' ((Req PUT "/api/users/$($providerRecord.userId)" $update $admin).code -eq 204)
 Check 'blocked_session_revoked_immediately' ((Req GET '/api/schools' $null $providerHeaders).code -eq 401)
 Check 'blocked_login_denied' ((Req POST '/api/auth/login' @{login='provider-user';password=$testPassword} $(@{'CF-Connecting-IP'='198.51.100.201'})).code -eq 401)
 $update.isBlocked=$false
 Check 'unblock_account' ((Req PUT "/api/users/$($providerRecord.userId)" $update $admin).code -eq 204)
 Check 'unblock_does_not_restore_old_session' ((Req GET '/api/schools' $null $providerHeaders).code -eq 401)
 Check 'logout' ((Req POST '/api/auth/logout' @{} $schoolHeaders).code -eq 204)
 Check 'logout_revokes_session' ((Req GET '/api/auth/me' $null $schoolHeaders).code -eq 401)
 $newSchoolLogin=Req POST '/api/auth/login' @{login='school-user';password=$testPassword} @{'CF-Connecting-IP'='198.51.100.202'}
 $schoolHeaders=@{Authorization="Bearer $($newSchoolLogin.data.accessToken)"}
 Check 'change_password_wrong_current_rejected' ((Req POST '/api/auth/password' @{currentPassword='wrong-password';newPassword='new-verification-password'} $schoolHeaders).code -eq 401)
 Check 'change_password' ((Req POST '/api/auth/password' @{currentPassword=$testPassword;newPassword='new-verification-password'} $schoolHeaders).code -eq 204)
 Check 'password_change_revokes_sessions' ((Req GET '/api/auth/me' $null $schoolHeaders).code -eq 401)
 Check 'old_password_rejected' ((Req POST '/api/auth/login' @{login='school-user';password=$testPassword} @{'CF-Connecting-IP'='198.51.100.202'}).code -eq 401)
 Check 'new_password_works' ((Req POST '/api/auth/login' @{login='school-user';password='new-verification-password'} @{'CF-Connecting-IP'='198.51.100.202'}).code -eq 200)
 Check 'admin_password_reset' ((Req POST "/api/users/$($users['district-user'].record.userId)/password" @{newPassword='reset-verification-password'} $admin).code -eq 204)
 Check 'reset_revokes_old_session' ((Req GET '/api/auth/me' $null $users['district-user'].headers).code -eq 401)
 $lockHeaders=@{'CF-Connecting-IP'='198.51.100.203'}
 for($i=0;$i -lt 5;$i++){$null=Req POST '/api/auth/login' @{login='empty-user';password='wrong-password'} $lockHeaders}
 Check 'account_lockout_after_five_failures' ((Req POST '/api/auth/login' @{login='empty-user';password=$testPassword} $lockHeaders).code -eq 401)
 $null=docker exec vko-monitoring-postgres-1 psql -U vko_monitoring -d $db -c "UPDATE user_sessions SET expires_at_utc=now()-interval '1 minute' WHERE user_id='$($users['regional-user'].record.userId)';"
 Check 'expired_session_denied' ((Req GET '/api/auth/me' $null $users['regional-user'].headers).code -eq 401)
 $audit=Req GET '/api/audit?limit=1000' $null $admin
 Check 'audit_contains_server_actor' (@($audit.data|Where-Object {$_.actor -eq 'school-user' -and $_.action -eq 'POST' -and $_.path -eq '/api/incidents' -and $_.statusCode -eq 201}).Count -eq 1)
 Check 'audit_login_success_and_failure' (@($audit.data|Where-Object {$_.action -eq 'Login' -and $_.statusCode -eq 200}).Count -gt 0 -and @($audit.data|Where-Object {$_.action -eq 'Login' -and $_.statusCode -eq 401}).Count -gt 0)
 Check 'audit_denied_action' (@($audit.data|Where-Object {$_.actor -eq 'school-user' -and $_.statusCode -eq 403}).Count -gt 0)
 Check 'audit_no_passwords_or_tokens' ($audit.content -notmatch [regex]::Escape($testPassword) -and $audit.content -notmatch [regex]::Escape($login.data.accessToken))
 Check 'users_invalid_scope_rejected' ((Req POST '/api/users' @{login='invalid-user';displayName='Invalid';password=$testPassword;role='School'} $admin).code -eq 400)
 Check 'users_duplicate_login_rejected' ((Req POST '/api/users' @{login='TEST-ADMIN';displayName='Duplicate';password=$testPassword;role='Administrator'} $admin).code -eq 409)
 $rateHeaders=@{'CF-Connecting-IP'='198.51.100.204'}
 for($i=0;$i -lt 10;$i++){$null=Req POST '/api/auth/login' @{login='not-existing';password='wrong-password'} $rateHeaders}
 Check 'login_rate_limit' ((Req POST '/api/auth/login' @{login='not-existing';password='wrong-password'} $rateHeaders).code -eq 429)
 Check 'mixed_case_users_route_denied' ((Req GET '/API/USERS/' $null $users['regional-user'].headers).code -eq 401)
 # Regional session has expired above; use a fresh login to test the role boundary rather than expiry.
 $freshRegional=Req POST '/api/auth/login' @{login='regional-user';password=$testPassword} @{'CF-Connecting-IP'='198.51.100.205'}
 $regionalHeaders=@{Authorization="Bearer $($freshRegional.data.accessToken)"}
 Check 'mixed_case_users_role_denied' ((Req GET '/API/USERS/' $null $regionalHeaders).code -eq 403)
 Check 'mixed_case_audit_role_denied' ((Req GET '/API/AUDIT/' $null $regionalHeaders).code -eq 403)
 $regionalUpdate=@{displayName='Now district';role='District';districtCity='Nonexistent district';isBlocked=$false}
 Check 'change_role' ((Req PUT "/api/users/$($users['regional-user'].record.userId)" $regionalUpdate $admin).code -eq 204)
 Check 'role_change_revokes_old_session' ((Req GET '/api/schools' $null $regionalHeaders).code -eq 401)
 $downgraded=Req POST '/api/auth/login' @{login='regional-user';password=$testPassword} @{'CF-Connecting-IP'='198.51.100.205'}
 Check 'new_role_new_scope' (@((Req GET '/api/schools' $null @{Authorization="Bearer $($downgraded.data.accessToken)"}).data).Count -eq 0)
 $ownProvider=Req POST '/api/incidents' @{schoolId=$backup.data.schoolId;lineId=$backup.data.lineId;problemType='OwnProvider';title='Own provider issue';description='Test';actor='forged'} $admin
 $freshProvider=Req POST '/api/auth/login' @{login='provider-user';password=$testPassword} @{'CF-Connecting-IP'='198.51.100.206'}
 $providerHeaders=@{Authorization="Bearer $($freshProvider.data.accessToken)"}
 Check 'provider_can_comment_own_incident' ((Req POST "/api/incidents/$($ownProvider.data.incidentId)/comments" @{comment='Own line comment';actor='forged'} $providerHeaders).code -eq 204)
 Check 'provider_can_update_own_incident' ((Req PUT "/api/incidents/$($ownProvider.data.incidentId)/status" @{status='SentToProvider';actor='forged'} $providerHeaders).code -eq 204)
 $ownDetails=(Req GET "/api/incidents/$($ownProvider.data.incidentId)" $null $admin).data
 Check 'provider_history_actor_from_session' (@($ownDetails.history|Where-Object actor -eq 'provider-user').Count -eq 2)
 $null=docker exec vko-monitoring-postgres-1 psql -U vko_monitoring -d $db -c 'ALTER TABLE audit_events RENAME TO audit_events_unavailable;'
 try {
  Check 'audit_unavailable_mutation_fails_closed' ((Req PUT "/api/devices/$($backup.data.deviceId)/block-state" @{isBlocked=$true} $admin).code -eq 500)
 } finally {
  $null=docker exec vko-monitoring-postgres-1 psql -U vko_monitoring -d $db -c 'ALTER TABLE audit_events_unavailable RENAME TO audit_events;'
 }
 $deviceAfterAuditFailure=@((Req GET "/api/schools/$($backup.data.schoolId)/devices" $null $admin).data|Where-Object deviceId -eq $backup.data.deviceId)[0]
 Check 'failed_audit_prevents_device_change' (-not $deviceAfterAuditFailure.isBlocked)
 $audit=Req GET '/api/audit?limit=1000' $null $admin
 Check 'audit_records_rate_limit' (@($audit.data|Where-Object {$_.action -eq 'RateLimit' -and $_.statusCode -eq 429}).Count -gt 0)
} catch { Check 'harness_exception' $false $_.Exception.Message }
finally {
 if($agent -and -not $agent.HasExited){Stop-Process -Id $agent.Id}
 if($api -and -not $api.HasExited){Stop-Process -Id $api.Id}
 $results|ConvertTo-Json -Depth 5|Set-Content -LiteralPath "$work/users-api-results.json"
 # Keep the isolated database for reproducible investigation; no production rows are modified.
 Write-Output "Isolated test database: $db"
}










Write-Output "Checks: $($results.Count)"
if(@($results | Where-Object passed -eq $false).Count -gt 0){throw 'Integration verification failed. See data/integration-tests/users-api-results.json'}
