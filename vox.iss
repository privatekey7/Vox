; Inno Setup script for Vox — Voice Input
; Builds a Windows installer with optional desktop shortcut and autostart.
;
; Paths are relative to this .iss file (SourcePath). Expected layout:
;   ./publish/...                    dotnet publish output of src/Vox.App (Vox.exe + DLLs)
;   ./assets/icons/vox.ico
;   ./assets/installer/wizard-*.bmp
; Output:
;   ./installer/vox-setup.exe
;
; Vox 2.0 keeps the AppId of 1.x, so it installs over the Python version as an upgrade.

#define MyAppName "Vox"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "Vox"
#define MyAppExeName "Vox.exe"

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
SetupIconFile={#SourcePath}\assets\icons\vox.ico
WizardImageFile={#SourcePath}\assets\installer\wizard-100.bmp,{#SourcePath}\assets\installer\wizard-125.bmp,{#SourcePath}\assets\installer\wizard-150.bmp,{#SourcePath}\assets\installer\wizard-175.bmp,{#SourcePath}\assets\installer\wizard-200.bmp,{#SourcePath}\assets\installer\wizard-250.bmp
WizardSmallImageFile={#SourcePath}\assets\installer\wizard-small-100.bmp,{#SourcePath}\assets\installer\wizard-small-125.bmp,{#SourcePath}\assets\installer\wizard-small-150.bmp,{#SourcePath}\assets\installer\wizard-small-175.bmp,{#SourcePath}\assets\installer\wizard-small-200.bmp,{#SourcePath}\assets\installer\wizard-small-250.bmp
CloseApplications=force
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Always show the "Select Setup Language" dialog so the user can pick English
; (or any language) on every install. Without these, Inno auto-selects the
; system language and, on re-install, silently reuses the previous one
; (UsePreviousLanguage defaults to yes) — which hid English on Russian systems.
ShowLanguageDialog=yes
UsePreviousLanguage=no

; ---------------------------------------------------------------------------
; Languages — all standard Inno Setup 6 translations.
; English first = default fallback for CustomMessages.
; ---------------------------------------------------------------------------
[Languages]
Name: "english";    MessagesFile: "compiler:Default.isl"
Name: "russian";    MessagesFile: "compiler:Languages\Russian.isl"
Name: "ukrainian";  MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "german";     MessagesFile: "compiler:Languages\German.isl"
Name: "french";     MessagesFile: "compiler:Languages\French.isl"
Name: "spanish";    MessagesFile: "compiler:Languages\Spanish.isl"
Name: "italian";    MessagesFile: "compiler:Languages\Italian.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "brazilian";  MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "dutch";      MessagesFile: "compiler:Languages\Dutch.isl"
Name: "polish";     MessagesFile: "compiler:Languages\Polish.isl"
Name: "czech";      MessagesFile: "compiler:Languages\Czech.isl"
Name: "turkish";    MessagesFile: "compiler:Languages\Turkish.isl"
Name: "japanese";   MessagesFile: "compiler:Languages\Japanese.isl"
Name: "finnish";    MessagesFile: "compiler:Languages\Finnish.isl"
Name: "danish";     MessagesFile: "compiler:Languages\Danish.isl"
Name: "norwegian";  MessagesFile: "compiler:Languages\Norwegian.isl"
Name: "slovenian";  MessagesFile: "compiler:Languages\Slovenian.isl"
Name: "catalan";    MessagesFile: "compiler:Languages\Catalan.isl"
Name: "bulgarian";  MessagesFile: "compiler:Languages\Bulgarian.isl"
Name: "hebrew";     MessagesFile: "compiler:Languages\Hebrew.isl"

; ---------------------------------------------------------------------------
; CustomMessages — translated strings used in [Tasks] and [Run].
; Languages without explicit entries inherit from English (first in list).
; ---------------------------------------------------------------------------
[CustomMessages]
english.AutostartDescription=Start Vox with Windows
english.AutostartGroup=Additional options:
english.LaunchAfterInstall=Launch {#MyAppName} now
russian.AutostartDescription=Запускать Vox при старте Windows
russian.AutostartGroup=Дополнительно:
russian.LaunchAfterInstall=Запустить {#MyAppName} сейчас
ukrainian.AutostartDescription=Запускати Vox разом з Windows
ukrainian.AutostartGroup=Додатково:
ukrainian.LaunchAfterInstall=Запустити {#MyAppName} зараз
german.AutostartDescription=Vox mit Windows starten
german.AutostartGroup=Weitere Optionen:
german.LaunchAfterInstall={#MyAppName} jetzt starten
french.AutostartDescription=Lancer Vox au démarrage de Windows
french.AutostartGroup=Options supplémentaires :
french.LaunchAfterInstall=Lancer {#MyAppName} maintenant
spanish.AutostartDescription=Iniciar Vox con Windows
spanish.AutostartGroup=Opciones adicionales:
spanish.LaunchAfterInstall=Ejecutar {#MyAppName} ahora
italian.AutostartDescription=Avvia Vox con Windows
italian.AutostartGroup=Opzioni aggiuntive:
italian.LaunchAfterInstall=Avvia {#MyAppName} adesso
portuguese.AutostartDescription=Iniciar o Vox com o Windows
portuguese.AutostartGroup=Opções adicionais:
portuguese.LaunchAfterInstall=Iniciar o {#MyAppName} agora
brazilian.AutostartDescription=Iniciar o Vox com o Windows
brazilian.AutostartGroup=Opções adicionais:
brazilian.LaunchAfterInstall=Iniciar o {#MyAppName} agora
dutch.AutostartDescription=Vox starten met Windows
dutch.AutostartGroup=Extra opties:
dutch.LaunchAfterInstall={#MyAppName} nu starten
polish.AutostartDescription=Uruchamiaj Vox przy starcie Windows
polish.AutostartGroup=Opcje dodatkowe:
polish.LaunchAfterInstall=Uruchom {#MyAppName} teraz
czech.AutostartDescription=Spouštět Vox při startu Windows
czech.AutostartGroup=Další možnosti:
czech.LaunchAfterInstall=Spustit {#MyAppName} nyní
turkish.AutostartDescription=Vox'u Windows ile başlat
turkish.AutostartGroup=Ek seçenekler:
turkish.LaunchAfterInstall={#MyAppName} şimdi başlat
japanese.AutostartDescription=Windows 起動時に Vox を開始
japanese.AutostartGroup=追加オプション:
japanese.LaunchAfterInstall={#MyAppName} を今すぐ起動
finnish.AutostartDescription=Käynnistä Vox Windowsin kanssa
finnish.AutostartGroup=Lisäasetukset:
finnish.LaunchAfterInstall=Käynnistä {#MyAppName} nyt
danish.AutostartDescription=Start Vox med Windows
danish.AutostartGroup=Yderligere indstillinger:
danish.LaunchAfterInstall=Start {#MyAppName} nu
norwegian.AutostartDescription=Start Vox med Windows
norwegian.AutostartGroup=Tilleggsalternativer:
norwegian.LaunchAfterInstall=Start {#MyAppName} nå
slovenian.AutostartDescription=Zaženi Vox ob zagonu Windows
slovenian.AutostartGroup=Dodatne možnosti:
slovenian.LaunchAfterInstall=Zaženi {#MyAppName} zdaj
catalan.AutostartDescription=Iniciar Vox amb Windows
catalan.AutostartGroup=Opcions addicionals:
catalan.LaunchAfterInstall=Executar {#MyAppName} ara
bulgarian.AutostartDescription=Стартиране на Vox с Windows
bulgarian.AutostartGroup=Допълнителни опции:
bulgarian.LaunchAfterInstall=Стартирай {#MyAppName} сега
hebrew.AutostartDescription=הפעל את Vox עם Windows
hebrew.AutostartGroup=:אפשרויות נוספות
hebrew.LaunchAfterInstall=הפעל את {#MyAppName} עכשיו

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartDescription}"; GroupDescription: "{cm:AutostartGroup}"; Flags: unchecked

[InstallDelete]
; Vox 1.x (Python/PyInstaller) files
Type: filesandordirs; Name: "{app}\_internal"
Type: files; Name: "{app}\vox.exe"

[Files]
Source: "{#SourcePath}\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Vox 1.x wrote autostart for all users (HKLM); 2.0 keeps it per user (HKCU) so the
; "Запускать вместе с Windows" switch in the app can turn it on and off.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Vox"; Flags: deletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Vox"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart
; Crash dumps for native crashes (speech engine) that the app cannot log itself: Windows Error
; Reporting writes up to 3 minidumps of Vox.exe to %LOCALAPPDATA%\vox\crashes.
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Vox.exe"; ValueType: expandsz; ValueName: "DumpFolder"; ValueData: "%LOCALAPPDATA%\vox\crashes"; Flags: uninsdeletekey; Check: IsAdminInstallMode
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Vox.exe"; ValueType: dword; ValueName: "DumpCount"; ValueData: "3"; Check: IsAdminInstallMode
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Vox.exe"; ValueType: dword; ValueName: "DumpType"; ValueData: "1"; Check: IsAdminInstallMode

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill any running Vox before uninstalling
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM vox.exe"; Flags: runhidden; RunOnceId: "KillVox"

[Code]
// Close a running Vox (1.x or 2.0) so its files can be replaced.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM vox.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
