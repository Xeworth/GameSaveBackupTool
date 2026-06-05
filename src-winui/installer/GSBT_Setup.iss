; GSBT — Game Save Backup Tool (WinUI 3)
; Build: installer\build_installer.bat (after scripts\publish_release.bat)
; gsbt-sandbox.exe and gsbt-sandbox.pri are hard links to gsbt.exe / gsbt.pri (zero extra disk space).
; WinUI MRT resolves resources from {exe-name}.pri — both links are required.

#define MyAppName "Game Save Backup Tool"
#define MyAppShortName "GSBT"
#define MyAppExe "gsbt.exe"
#define MyAppSandboxExe "gsbt-sandbox.exe"
#define MyAppVersion "0.0.1.250605"
#define MyAppPublisher "Xeworth"
#define MyAppURL "https://github.com/Xeworth/GameSaveBackupTool"
#define MyAppId "{{A7B3C4D5-E6F7-4890-ABCD-EF1234567890}"

#define PublishDir "..\src\GSBT.WinUI\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppComments=Self-contained WinUI backup tool for game saves. .NET 8 and Windows App SDK are bundled — no separate runtime install required.
DefaultDirName={autopf}\{#MyAppShortName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=output
OutputBaseFilename=GSBT_Setup_{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\branding\gsbt.ico
UninstallDisplayIcon={app}\{#MyAppExe}
MinVersion=10.0
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon,{#MyAppName}}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "desktopiconsandbox"; Description: "Create a &desktop icon for GSBT Sandbox"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,gsbt-sandbox.exe,gsbt-sandbox.pri"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\branding\gsbt.ico"
Name: "{group}\GSBT Sandbox"; Filename: "{app}\{#MyAppSandboxExe}"; WorkingDir: "{app}"; IconFilename: "{app}\branding\gsbt-s.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; IconFilename: "{app}\branding\gsbt.ico"; Tasks: desktopicon
Name: "{autodesktop}\GSBT Sandbox"; Filename: "{app}\{#MyAppSandboxExe}"; WorkingDir: "{app}"; IconFilename: "{app}\branding\gsbt-s.ico"; Tasks: desktopiconsandbox
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent unchecked

[Registry]
Root: HKLM; Subkey: "Software\{#MyAppShortName}"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekeyifempty
Root: HKLM; Subkey: "Software\{#MyAppShortName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"

[Code]
function TryCreateSandboxHardLink(const AppDir: String): Boolean;
var
  ResultCode: Integer;
  MainExe, SandboxExe, MainPri, SandboxPri: String;
begin
  MainExe := AppDir + '\{#MyAppExe}';
  SandboxExe := AppDir + '\{#MyAppSandboxExe}';
  MainPri := AppDir + '\gsbt.pri';
  SandboxPri := AppDir + '\gsbt-sandbox.pri';
  Result := False;
  if not FileExists(MainExe) or not FileExists(MainPri) then
    Exit;

  if FileExists(SandboxExe) then
    DeleteFile(SandboxExe);
  if FileExists(SandboxPri) then
    DeleteFile(SandboxPri);

  if not (Exec('cmd.exe',
    '/C mklink /H "' + SandboxExe + '" "' + MainExe + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) and FileExists(SandboxExe)) then
    Exit;

  if Exec('cmd.exe',
    '/C mklink /H "' + SandboxPri + '" "' + MainPri + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) and FileExists(SandboxPri) then
    Result := True
  else
  begin
    DeleteFile(SandboxExe);
    Result := False;
  end;
end;

procedure TryRemoveSandboxHardLinks(const AppDir: String);
var
  SandboxExe, SandboxPri: String;
begin
  SandboxExe := AppDir + '\{#MyAppSandboxExe}';
  SandboxPri := AppDir + '\gsbt-sandbox.pri';
  if FileExists(SandboxExe) then
    DeleteFile(SandboxExe);
  if FileExists(SandboxPri) then
    DeleteFile(SandboxPri);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppDir: String;
begin
  if CurStep = ssPostInstall then
  begin
    AppDir := ExpandConstant('{app}');
    if not TryCreateSandboxHardLink(AppDir) then
      MsgBox('GSBT installed, but gsbt-sandbox.exe could not be created.' + #13#10 +
        'You can still use gsbt.exe with the -s flag, or reinstall with admin rights.',
        mbError, MB_OK)
    else
      RegWriteStringValue(HKLM, 'Software\{#MyAppShortName}', 'SandboxInstalled', '1');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteKeyIncludingSubkeys(HKLM, 'Software\{#MyAppShortName}');
  if CurUninstallStep = usUninstall then
    TryRemoveSandboxHardLinks(ExpandConstant('{app}'));
end;
