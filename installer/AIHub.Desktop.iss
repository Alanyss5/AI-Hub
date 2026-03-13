#define MyAppName "AI-Hub Desktop"
#define MyAppPublisher "AI-Hub"
#define MyAppExeName "AIHub.Desktop.exe"
#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif
#ifndef PublishDir
  #error PublishDir is required.
#endif
#ifndef OutputDir
  #define OutputDir AddBackslash(SourcePath) + "out"
#endif
#ifndef RepoRoot
  #define RepoRoot ExtractFileDir(SourcePath)
#endif

[Setup]
AppId={{9E54F5EC-3D4F-4D35-9D90-67E97F91303A}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\AI-Hub
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=aihub-desktop-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
ChangesEnvironment=no

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\scripts\windows\backup-hub-state.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 AI-Hub Desktop"; Flags: nowait postinstall skipifsilent

[Code]
function BackupHubRoot(): Boolean;
var
  HubRoot: string;
  BackupRoot: string;
  ScriptPath: string;
  Parameters: string;
  ResultCode: Integer;
begin
  Result := True;
  HubRoot := GetEnv('AIHUB_HUBROOT');
  if HubRoot = '' then
    exit;

  ScriptPath := ExpandConstant('{tmp}\backup-hub-state.ps1');
  BackupRoot := ExpandConstant('{localappdata}\AIHub\installer-backups');
  ForceDirectories(BackupRoot);

  Parameters := '-ExecutionPolicy Bypass -File "' + ScriptPath + '" -HubRoot "' + HubRoot + '" -OutputRoot "' + BackupRoot + '" -Label "installer-upgrade" -CreateZip';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := False;
    exit;
  end;

  Result := ResultCode = 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if BackupHubRoot() then
    Result := ''
  else
    Result := 'AI-Hub Hub root backup failed. Set AIHUB_HUBROOT correctly or run scripts\\windows\\backup-hub-state.ps1 manually before retrying.';
end;