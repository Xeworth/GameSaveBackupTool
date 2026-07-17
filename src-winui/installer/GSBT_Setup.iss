; GSBT — Game Save Backup Tool (WinUI 3 + CLI)
; Build: installer\build_installer.bat (after scripts\publish_release.bat)
; Requires Inno Setup 6.5.4+ for WizardStyle=modern dynamic (system light/dark).
; Self-contained publish (.NET 10 + Windows App SDK bundled beside gsbt-main.exe).
; gsbt.exe in the same folder is the terminal CLI (optional PATH task).
; Per-user install under %LocalAppData% (no admin) — same pattern as PowerToys-style tools.

#define MyAppName "Game Save Backup Tool"
#define MyAppFolderName "Game Save Backup Tool"
#define MyAppRegKey "Game Save Backup Tool"
#define MyAppGuiExe "gsbt-main.exe"
#define MyAppCliExe "gsbt.exe"
#define MyAppSandboxExe "gsbt-sandbox.exe"
#ifndef MyAppVersion
  #define MyAppVersion GetEnv("GSBT_VERSION")
#endif
#if MyAppVersion == ""
  #error GSBT_VERSION is required. Run installer\build_installer.bat.
#endif
#define MyAppPublisher "Xeworth"
#define MyAppURL "https://github.com/Xeworth/GameSaveBackupTool"
#define MyAppId "{{A7B3C4D5-E6F7-4890-ABCD-EF1234567890}"

#define PublishDir "..\src\GSBT.WinUI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

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
AppComments=Self-contained WinUI backup tool for game saves plus gsbt terminal CLI. .NET 10 and Windows App SDK are bundled — no separate runtime install required.
DefaultDirName={localappdata}\{#MyAppFolderName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=output
OutputBaseFilename=GSBT_Setup_{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic includetitlebar
ShowLanguageDialog=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\branding\gsbt.ico
UninstallDisplayIcon={app}\{#MyAppGuiExe}
MinVersion=10.0
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "compact"; Description: "Compact installation (without compression screen saver media)"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "core"; Description: "GSBT GUI, CLI, and sandbox tools"; Types: full compact custom; Flags: fixed
Name: "screensaver"; Description: "Compression screen saver media"; Types: full

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon,{#MyAppName}}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "desktopiconsandbox"; Description: "Create a &desktop shortcut for the Sandbox tool"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addpath"; Description: "Add &gsbt to PATH (run gsbt in Command Prompt / PowerShell)"; GroupDescription: "Terminal:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Components: core; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,data\screensaver.7z"
Source: "{#PublishDir}\data\screensaver.7z"; DestDir: "{app}\data"; Components: screensaver; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppGuiExe}"; WorkingDir: "{app}"; IconFilename: "{app}\branding\gsbt.ico"
Name: "{group}\GSBT Sandbox"; Filename: "{app}\{#MyAppSandboxExe}"; WorkingDir: "{app}"; IconFilename: "{app}\branding\gsbt-s.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppGuiExe}"; WorkingDir: "{app}"; IconFilename: "{app}\branding\gsbt.ico"; Tasks: desktopicon
Name: "{autodesktop}\GSBT Sandbox"; Filename: "{app}\{#MyAppSandboxExe}"; WorkingDir: "{app}"; IconFilename: "{app}\branding\gsbt-s.ico"; Tasks: desktopiconsandbox
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppGuiExe}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent unchecked

[Registry]
Root: HKCU; Subkey: "Software\{#MyAppRegKey}"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\{#MyAppRegKey}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"

[Code]
function NeedsAddPath(Param: string): Boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(OrigPath) + ';') = 0;
end;

procedure AddDirToPath(const Dir: string);
var
  OrigPath: string;
  NewPath: string;
begin
  if not NeedsAddPath(Dir) then
    exit;
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', OrigPath) then
    NewPath := Dir
  else if (Length(OrigPath) = 0) or (OrigPath[Length(OrigPath)] = ';') then
    NewPath := OrigPath + Dir
  else
    NewPath := OrigPath + ';' + Dir;
  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', NewPath);
end;

procedure RemoveDirFromPath(const Dir: string);
var
  OrigPath, NewPath: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', OrigPath) then
    exit;
  NewPath := OrigPath;
  StringChangeEx(NewPath, Dir + ';', '', True);
  StringChangeEx(NewPath, ';' + Dir, '', True);
  if CompareText(NewPath, Dir) = 0 then
    NewPath := '';
  if NewPath <> OrigPath then
    RegWriteExpandStringValue(HKCU, 'Environment', 'Path', NewPath);
end;

function SandboxEntryInstalled(const AppDir: String): Boolean;
begin
  Result := FileExists(AppDir + '\{#MyAppSandboxExe}')
    and FileExists(AppDir + '\gsbt-sandbox.pri');
end;

function CliEntryInstalled(const AppDir: String): Boolean;
begin
  Result := FileExists(AppDir + '\{#MyAppCliExe}');
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
    if WizardIsTaskSelected('addpath') then
      AddDirToPath(AppDir);
    if not CliEntryInstalled(AppDir) then
      MsgBox('GSBT installed, but gsbt.exe (terminal CLI) is missing from the install folder.',
        mbError, MB_OK);
    if not SandboxEntryInstalled(AppDir) then
      MsgBox('GSBT installed, but gsbt-sandbox.exe is missing or incomplete.' + #13#10 +
        'You can still use gsbt-main.exe with the -s flag.',
        mbError, MB_OK)
    else
      RegWriteStringValue(HKCU, 'Software\{#MyAppRegKey}', 'SandboxInstalled', '1');
    if not WizardIsComponentSelected('screensaver') then
      DeleteFile(AppDir + '\data\screensaver.7z');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDir: String;
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\{#MyAppRegKey}');
  if CurUninstallStep = usUninstall then
  begin
    AppDir := ExpandConstant('{app}');
    RemoveDirFromPath(AppDir);
    TryRemoveSandboxEntryFiles(AppDir);
  end;
end;
