#define MyAppVersion "1.1.0"

[Setup]
AppId={{3AF9E86A-05CA-48D8-800E-1C71AA496C5E}
AppName=System Pulse
AppVersion={#MyAppVersion}
AppPublisher=System Pulse
AppPublisherURL=https://github.com/bellerswang/system_pulse
DefaultDirName={localappdata}\Programs\System Pulse
DefaultGroupName=System Pulse
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
AppMutex=Local\SystemPulse
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
SetupIconFile=..\src\XinweiManager\Assets\Brand\SystemPulse.ico
UninstallDisplayIcon={app}\SystemPulse.exe
OutputDir=..\artifacts\installer
OutputBaseFilename=SystemPulseSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\System Pulse"; Filename: "{app}\SystemPulse.exe"
Name: "{userdesktop}\System Pulse"; Filename: "{app}\SystemPulse.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SystemPulse"; ValueData: """{app}\SystemPulse.exe"" --background"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\SystemPulse.exe"; Description: "Launch System Pulse"; Flags: nowait postinstall skipifsilent
