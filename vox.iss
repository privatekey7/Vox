; Inno Setup script for Vox — Voice Input
; Builds a Windows installer with optional desktop shortcut and autostart.
;
; Paths are resolved relative to this .iss file (SourcePath), so the script
; works from any ASCII-only build directory. Expected layout next to vox.iss:
;   ./vox.ico
;   ./dist/vox/vox.exe
;   ./dist/vox/_internal/...
; Output:
;   ./installer/vox-setup.exe

#define MyAppName "Vox"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Vox"
#define MyAppExeName "vox.exe"

[Setup]
AppId={{8D2A1B3F-4E5C-4D7A-9F8B-1E2C3D4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
OutputDir={#SourcePath}\installer
OutputBaseFilename=vox-setup
SetupIconFile={#SourcePath}\vox.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Запускать Vox при старте Windows"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "{#SourcePath}\dist\vox\vox.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\dist\vox\_internal\*"; DestDir: "{app}\_internal"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Autostart entry (only if user checked the autostart task)
; HKA = HKLM for admin install (all users), HKCU for per-user install
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Vox"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName} сейчас"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill any running vox.exe before uninstalling
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM vox.exe"; Flags: runhidden; RunOnceId: "KillVox"
