; Fluent for Windows installer (Inno Setup 6, free). Built in CI by scripts/build.ps1:
;   ISCC /DAppVersion=1.0.0 /DSourceExe=..\dist\win-x64\Fluent.exe installer\Fluent.iss
;
; Installs for the current user only (no admin prompt), into %LOCALAPPDATA%\Programs\Fluent, like
; VS Code, Slack and Discord do. Adds Start menu (and optionally desktop) shortcuts, a normal entry in
; Settings > Apps for uninstalling, and optionally "open when I sign in".

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\dist\win-x64\Fluent.exe"
#endif

[Setup]
AppId={{7C2A5E1D-4F7B-4B8E-9E0A-F1C3D2B6A901}
AppName=Fluent
AppVersion={#AppVersion}
AppVerName=Fluent {#AppVersion}
AppPublisher=Fluent
AppPublisherURL=https://fluent-voice-v2.vercel.app
AppSupportURL=https://fluent-voice-v2.vercel.app
AppUpdatesURL=https://fluent-voice-v2.vercel.app
AppCopyright=© 2026 Fluent
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=Fluent
VersionInfoDescription=Fluent setup
DefaultDirName={localappdata}\Programs\Fluent
DefaultGroupName=Fluent
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=Fluent-Setup-{#AppVersion}
SetupIconFile=..\src\Fluent\Assets\Fluent.ico
UninstallDisplayIcon={app}\Fluent.exe
UninstallDisplayName=Fluent
WizardStyle=modern
WizardImageFile=wizard-side.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=force
RestartApplications=no
SetupMutex=FluentSetupMutex

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install Fluent {#AppVersion} on your computer.%n%nFluent lets you speak in any app and writes it for you. It is free.%n%nIt installs for your Windows account only and does not need administrator rights.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Open Fluent when I sign in (it waits quietly in the system tray)"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Fluent.exe"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Fluent"; Filename: "{app}\Fluent.exe"; Comment: "Speak in any app"
Name: "{userdesktop}\Fluent"; Filename: "{app}\Fluent.exe"; Tasks: desktopicon; Comment: "Speak in any app"

[Registry]
; Same value Fluent's own "Open Fluent when I sign in" switch writes, so either can turn it off.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Fluent"; \
  ValueData: """{app}\Fluent.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Fluent.exe"; Description: "Open Fluent now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM Fluent.exe /F"; Flags: runhidden; RunOnceId: "StopFluent"

[UninstallDelete]
; Settings, history and the encrypted key live in %APPDATA%\Fluent; the log in %LOCALAPPDATA%\Fluent.
Type: filesandordirs; Name: "{localappdata}\Fluent"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Fluent');
    if MsgBox('Also delete your Fluent settings, history and saved Gemini key from this PC?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Fluent'), True, True, True);
  end;
end;
