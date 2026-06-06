; GSBT — Game Save Backup Tool (WinUI 3)
; Build: installer\build_installer.bat (after scripts\publish_release.bat)
; Requires Inno Setup 6.5.4+ for WizardStyle=modern dynamic (system light/dark).
; gsbt-sandbox.exe is a separate publish with gsbt-s.ico embedded (not a hard link).

#define MyAppName "Game Save Backup Tool"
#define MyAppShortName "GSBT"
#define MyAppExe "gsbt.exe"
#define MyAppSandboxExe "gsbt-sandbox.exe"
#define MyAppVersion "0.0.2.260606"
#define MyAppPublisher "Xeworth"
#define MyAppURL "https://github.com/Xeworth/GameSaveBackupTool"
#define MyAppId "{{A7B3C4D5-E6F7-4890-ABCD-EF1234567890}"

#define PublishDir "..\src\GSBT.WinUI\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
UninstallDisplayName={#MyAppName}
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
WizardStyle=modern dynamic includetitlebar
ShowLanguageDialog=no
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
Name: "desktopiconsandbox"; Description: "Create a &desktop shortcut for the Sandbox tool"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

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
function SandboxEntryInstalled(const AppDir: String): Boolean;
begin
  Result := FileExists(AppDir + '\{#MyAppSandboxExe}')
    and FileExists(AppDir + '\gsbt-sandbox.pri');
end;

procedure TryRemoveSandboxEntryFiles(const AppDir: String);
var
  F: String;
begin
  F := AppDir + '\{#MyAppSandboxExe}';
  if FileExists(F) then DeleteFile(F);
  F := AppDir + '\gsbt-sandbox.pri';
  if FileExists(F) then DeleteFile(F);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppDir: String;
begin
  if CurStep = ssPostInstall then
  begin
    AppDir := ExpandConstant('{app}');
    if not SandboxEntryInstalled(AppDir) then
      MsgBox('GSBT installed, but gsbt-sandbox.exe is missing or incomplete.' + #13#10 +
        'You can still use gsbt.exe with the -s flag.',
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
    TryRemoveSandboxEntryFiles(ExpandConstant('{app}'));
end;
