#define MyAppName "Watch Dog EM"
#define MyAppVersion "1.8.0"
#define MyAppPublisher "MicroBrain"
#define MyAppExeName "MallEnergyBilling.Web.exe"

[Setup]
AppId={{8C4952D4-CC8E-4FD1-9432-0B587F2E5D77}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Watch Dog EM
DefaultGroupName=Watch Dog EM
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=Watch-Dog-EM-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\Watch Dog.ico
UninstallDisplayIcon={app}\Watch Dog.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Watch Dog EM - Watch Every Watt

[Files]
Source: "..\outputs\Watch-Dog-EM-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Launch Watch Dog EM.vbs"; DestDir: "{app}"; Flags: ignoreversion
Source: "Install Watch Dog EM Service.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Remove Watch Dog EM Service.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Watch Dog.ico"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\Watch Dog EM"; Permissions: users-modify
Name: "{commonappdata}\Watch Dog EM\Backups"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\Watch Dog EM"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch Watch Dog EM.vbs"""; WorkingDir: "{app}"; IconFilename: "{app}\Watch Dog.ico"
Name: "{autodesktop}\Watch Dog EM"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch Watch Dog EM.vbs"""; WorkingDir: "{app}"; IconFilename: "{app}\Watch Dog.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install Watch Dog EM Service.ps1"" -InstallDirectory ""{app}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch Watch Dog EM.vbs"""; WorkingDir: "{app}"; Description: "Launch Watch Dog EM"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Remove Watch Dog EM Service.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveWatchDogEMService"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop WatchDogEM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
