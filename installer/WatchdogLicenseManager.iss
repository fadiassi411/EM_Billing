#define MyAppName "Watchdog License Manager"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MicroBrain"
#define MyAppExeName "Watchdog License Manager.exe"

[Setup]
AppId={{D590458A-329C-47A2-BC8A-7D63036220AA}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Watchdog License Manager
DefaultGroupName=Watchdog License Manager
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=Watchdog-License-Manager-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\Watch Dog.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Watchdog offline license generator

[Files]
Source: "..\private-license-authority\WatchdogLicenseManager\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Watchdog License Manager"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Watchdog License Manager"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Open Watchdog License Manager"; Flags: nowait postinstall skipifsilent
