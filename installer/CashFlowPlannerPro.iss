; CashFlow Planner Pro - Inno Setup Script
; Erstellt am 25.03.2026

#define MyAppName "CashFlow Planner Pro"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "Thomas Tian"
#define MyAppURL "https://cashflowplannerpro.de"
#define MyAppExeName "CashFlowPlannerPro.exe"
#define MyAppSourceDir "..\publish\win-x64"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Ausgabedatei
OutputDir=..\installer\output
OutputBaseFilename=CashFlowPlannerPro_Setup_{#MyAppVersion}
; Icon
SetupIconFile=..\CashFlowPlannerPro\Resources\CashFlowIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
; Komprimierung
Compression=lzma2/ultra64
SolidCompression=yes
; UI
WizardStyle=modern
WizardResizable=no
; Rechte
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Mindestversion Windows 10
MinVersion=10.0
; Architektur
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checked
Name: "startmenuicon"; Description: "Startmen&uuml;-Eintrag erstellen"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checked

[Files]
; Alle Dateien aus dem Publish-Ordner
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\{#MyAppName} deinstallieren"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} starten"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; AppData-Ordner beim Deinstallieren NICHT loeschen (Benutzerdaten behalten)
; Falls gewuenscht, auskommentieren:
; Type: filesandirs; Name: "{localappdata}\CashFlowPlannerPro"

[Code]
// Pruefen ob die App laeuft und anbieten zu schliessen
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if CheckForMutexes('{#MyAppName}_Mutex') then
  begin
    if MsgBox('{#MyAppName} laeuft noch. Soll die Anwendung geschlossen werden?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      Exec('taskkill', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(1000);
    end
    else
      Result := False;
  end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  Exec('taskkill', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;
