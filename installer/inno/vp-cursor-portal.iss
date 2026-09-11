#define MyAppName "vp-cursor-portal"
#ifndef MyAppVersion
#define MyAppVersion "0.1.8"
#endif
#define MyAppPublisher "vp-cursor-portal contributors"
#define MyAppExeName "vp-cursor-portal.exe"
#ifndef PublishDir
#define PublishDir "..\..\artifacts\vp-cursor-portal-win-x64"
#endif

[Setup]
AppId={{FA47277E-8B64-4CE7-B26A-B45168613CC6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\vp-cursor-portal
DefaultGroupName=vp-cursor-portal
DisableProgramGroupPage=yes
OutputDir=..\..\artifacts\installer
OutputBaseFilename=vp-cursor-portal-setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
CloseApplications=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\..\src\H2CursorRouter.App\Assets\app.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\vp-cursor-portal"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\vp-cursor-portal"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch vp-cursor-portal"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := True;

  if MsgBox(
    'Remove user configuration and logs too?'#13#10#13#10 +
    'This deletes profiles, layouts, H2 settings, cached presets, and logs from:'#13#10 +
    ExpandConstant('{userappdata}\vp-cursor-portal'),
    mbConfirmation,
    MB_YESNO
  ) = IDYES then
  begin
    DelTree(ExpandConstant('{userappdata}\vp-cursor-portal'), True, True, True);
  end;
end;
