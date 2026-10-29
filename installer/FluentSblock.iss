; FluentSblock per-user installer (Inno Setup 6/7).
; Payload: self-contained single-file exe from ..\dist (no .NET check needed).
; Build: iscc installer\FluentSblock.iss  (or installer\build-setup.bat)

#define AppVersion "0.1.5"

[Setup]
AppId={{9fcbdfc9-81db-487f-87d8-f3ff59af0ae1}
AppName=FluentSblock
AppVersion={#AppVersion}
AppPublisher=FluentSblock
DefaultDirName={localappdata}\FluentSblock
DefaultGroupName=FluentSblock
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=FluentSblock-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion=0.1.5.0
VersionInfoProductName=FluentSblock
VersionInfoDescription=FluentSblock setup
SetupIconFile=..\src\FluentSblock\icon.ico
UninstallDisplayIcon={app}\FluentSblock.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\dist\FluentSblock.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\LEGGIMI.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\VERSION.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\FluentSblock"; Filename: "{app}\FluentSblock.exe"
Name: "{autodesktop}\FluentSblock"; Filename: "{app}\FluentSblock.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FluentSblock.exe"; Description: "Launch FluentSblock"; Flags: nowait postinstall skipifsilent
