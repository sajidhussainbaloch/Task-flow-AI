; ============================================================================
; ZayFlow AI - Inno Setup installer
; Build steps:
;   1. Run publish.ps1 from this folder
;   2. Open this file in Inno Setup Compiler
;   3. Compile to generate the installer exe in installer-output
; ============================================================================

#define MyAppName "ZayFlow AI"
#define MyAppVersion "3.0.0"
#define MyAppPublisher "ZayQen"
#define MyAppURL "https://github.com/sajidhussainbaloch/Task-flow-AI"
#define MyAppExeName "ZayFlow.App.exe"
#define MyAppDescription "Windows PC Management & Automation Assistant"
#define MyAppAssocName "ZayFlow AI"

[Setup]
AppId={{B8F2A7C3-4D5E-6F01-8A9B-CD3456789ABC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppComments={#MyAppDescription}
AppContact={#MyAppPublisher}
AppCopyright=Copyright (C) 2026 {#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=installer-output
OutputBaseFilename=ZayFlow-Setup-{#MyAppVersion}
SetupIconFile=..\ZayFlow.App\Assets\TrayIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppDescription}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
WizardSizePercent=105
DisableDirPage=no
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
LaunchApp=Launch ZayFlow AI now
StartupTaskDescription=Start ZayFlow AI when Windows starts

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "{cm:StartupTaskDescription}"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppDescription}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "{#MyAppDescription}"
Name: "{autostartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\ZayFlow"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM ZayFlow.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM ZayFlow.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;