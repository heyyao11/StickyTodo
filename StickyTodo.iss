; Inno Setup script: builds dist\StickyTodo-Setup.exe, a normal Windows installer.
; Build: run build.cmd first, then:  ISCC.exe StickyTodo.iss
; Installs per user (no admin prompt), adds Start menu entry, optional desktop icon,
; optional "start at sign-in" (Task Manager > Startup apps), and an uninstaller in Settings > Apps.

#define AppName "Sticky Todo"
; CI passes /DAppVersion=x.y.z from the git tag; local builds use this default
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#define AppExe "StickyTodo.exe"

[Setup]
AppId={{74FAA91D-E4AE-4DC1-B6CC-67AD2A0B24D9}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={localappdata}\Programs\StickyTodo
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=StickyTodo-Setup
SetupIconFile=StickyTodo.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
; Windows 10/11 (they ship .NET Framework 4.8, which the exe runs on)
MinVersion=10.0

[Tasks]
Name: "startup"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "StickyTodo.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "StickyTodo"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "KillApp"
