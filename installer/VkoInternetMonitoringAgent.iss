#define ProductName "Мониторинг интернета ВКО"
#define ProductVersion "1.1.0"
#define ProductPublisher "Команда хакатона ВКО"
#define ServiceName "VkoInternetMonitoringAgent"

[Setup]
AppId={{8E736269-BED7-4C68-BEAA-4057DE96CF6D}
AppName={#ProductName}
AppVersion={#ProductVersion}
AppPublisher={#ProductPublisher}
DefaultDirName={autopf}\VkoInternetMonitoringAgent
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=VkoInternetMonitoringAgent-Setup-{#ProductVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\vko-monitoring.ico
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#ProductName}
UninstallDisplayIcon={app}\VkoMonitoring.Agent.Setup.exe
SetupLogging=yes
CloseApplications=no
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\artifacts\agent-win-x64\*"; DestDir: "{app}"; Excludes: "appsettings.json"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\agent-win-x64\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\Состояние и настройка агента"; Filename: "{app}\VkoMonitoring.Agent.Setup.exe"
Name: "{group}\Журнал работы"; Filename: "{sys}\explorer.exe"; Parameters: "{commonappdata}\VkoInternetMonitoringAgent\logs"
Name: "{group}\Удалить агент"; Filename: "{uninstallexe}"

[InstallDelete]
Type: files; Name: "{group}\Настроить агент.lnk"

[Run]
Filename: "{app}\VkoMonitoring.Agent.Setup.exe"; Description: "Настроить и активировать компьютер"; Flags: postinstall nowait skipifsilent; Check: not IsActivated

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopMonitoringService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteMonitoringService"

[Code]
function IsActivated(): Boolean;
begin
  Result := FileExists(
    ExpandConstant('{commonappdata}\VkoInternetMonitoringAgent\device-token.dat'));
end;

procedure StopServiceIfInstalled();
var
  ResultCode: Integer;
  PowerShellPath: String;
  PowerShellArguments: String;
begin
  PowerShellPath := ExpandConstant(
    '{sys}\WindowsPowerShell\v1.0\powershell.exe');
  PowerShellArguments :=
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ' +
    '"$service = Get-Service -Name ''{#ServiceName}'' -ErrorAction SilentlyContinue; ' +
    'if ($null -ne $service -and $service.Status -ne ''Stopped'') { ' +
    'Stop-Service -Name ''{#ServiceName}'' -Force; ' +
    '$service.WaitForStatus(''Stopped'', [TimeSpan]::FromSeconds(30)) }"';

  if not Exec(
    PowerShellPath,
    PowerShellArguments,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) or (ResultCode <> 0) then
    RaiseException(
      'Не удалось безопасно остановить службу мониторинга перед обновлением.');
end;

procedure ConfigureService();
var
  ResultCode: Integer;
begin
  if not Exec(
    ExpandConstant('{app}\VkoMonitoring.Agent.Setup.exe'),
    '--install-service',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) or (ResultCode <> 0) then
    RaiseException('Не удалось установить или обновить службу мониторинга.');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopServiceIfInstalled();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ConfigureService();
end;
