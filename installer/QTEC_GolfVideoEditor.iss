#define MyAppName "QTEC 골프 영상 편집기"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "QTEC"
#define MyAppExeName "QTEC_GolfVideoEditor.exe"

[Setup]
AppId={{8CE5152B-7FF6-4CD0-9CE2-61B1B25050A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\QTEC Golf Video Editor
DefaultGroupName={#MyAppName}
OutputDir=..\release
OutputBaseFilename=QTEC_GolfVideoEditor_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
Source: "..\publish\QTEC_GolfVideoEditor.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\tools\ffmpeg.exe"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "바탕화면 바로가기 만들기"; GroupDescription: "추가 바로가기:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "QTEC 골프 영상 편집기 실행"; Flags: nowait postinstall skipifsilent
