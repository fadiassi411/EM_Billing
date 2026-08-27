#define MyAppName "BlackDog EM"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MicroBrain"
#define MyAppExeName "MallEnergyBilling.Web.exe"

[Setup]
AppId={{8C4952D4-CC8E-4FD1-9432-0B587F2E5D77}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\BlackDog EM
DefaultGroupName=BlackDog EM
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=BlackDog-EM-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\Black Dog.ico
UninstallDisplayIcon={app}\Black Dog.ico
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
VersionInfoDescription=BlackDog Energy - Watch Every Watt

[Files]
Source: "..\outputs\BlackDog-EM-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Launch BlackDog EM.vbs"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Black Dog.ico"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\BlackDog EM"; Permissions: users-modify
Name: "{commonappdata}\BlackDog EM\Backups"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\BlackDog EM"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch BlackDog EM.vbs"""; WorkingDir: "{app}"; IconFilename: "{app}\Black Dog.ico"
Name: "{autodesktop}\BlackDog EM"; Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch BlackDog EM.vbs"""; WorkingDir: "{app}"; IconFilename: "{app}\Black Dog.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Run]
Filename: "{sys}\wscript.exe"; Parameters: """{app}\Launch BlackDog EM.vbs"""; WorkingDir: "{app}"; Description: "Launch BlackDog EM"; Flags: nowait postinstall skipifsilent
