; KulyCopy Windows installer (Inno Setup 6.3+).
; Normally compiled by build.ps1, which passes AppVersion and PublishDir.
; Per-user install (no administrator rights), Start Menu shortcut, optional autostart, clean uninstall.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif
#define AppName "KulyCopy"
#define AppExe "kulycopy.exe"
#define AppPublisher "KulyCopy contributors"
#define AppURL "https://github.com/GuilhermeKulyk/kulycopy"

[Setup]
AppId={{A3F2C8D1-6E4B-4B7A-9D2E-1F5C7B8A9E01}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=KulyCopy-Setup-{#AppVersion}
SetupIconFile=..\src\KulyCopy\Assets\kulycopy.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=no
ShowLanguageDialog=auto
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
english.StartupGroup=Startup:
english.AutostartTask=Start %1 when I sign in to Windows
english.DeleteDataPrompt=Do you also want to delete your %1 settings and clipboard history?%n%n%2%n%3
brazilianportuguese.StartupGroup=Inicialização:
brazilianportuguese.AutostartTask=Iniciar o %1 junto com o Windows
brazilianportuguese.DeleteDataPrompt=Deseja também apagar as configurações e o histórico do %1?%n%n%2%n%3

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask,{#AppName}}"; GroupDescription: "{cm:StartupGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Clipboard manager"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the application itself manages from Preferences > "Start KulyCopy when I sign in".
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--show"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// Asks a running instance to quit (the CLI "exit" command talks to it over a named pipe).
procedure StopRunningInstance();
var
  Exe: String;
  ResultCode: Integer;
begin
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) then
  begin
    Exec(Exe, 'exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(800);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningInstance();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningInstance();
  Result := True;
end;

// User data is never removed silently: ask once, default "No".
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ConfigDir, DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    ConfigDir := ExpandConstant('{userappdata}\{#AppName}');
    DataDir := ExpandConstant('{localappdata}\{#AppName}');
    if DirExists(ConfigDir) or DirExists(DataDir) then
    begin
      if MsgBox(FmtMessage(CustomMessage('DeleteDataPrompt'), ['{#AppName}', ConfigDir, DataDir]),
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(ConfigDir, True, True, True);
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
