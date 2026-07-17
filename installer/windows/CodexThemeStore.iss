#ifndef MyAppSource
  #define MyAppSource "..\..\publish\win-x64"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\..\artifacts"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{C3177165-DB9B-4F5E-BB64-78A6EC6A2267}
AppName=Codex Theme Store
AppVersion={#MyAppVersion}
AppPublisher=Codex Skin
DefaultDirName={localappdata}\Programs\CodexThemeStore
DefaultGroupName=Codex Theme Store
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#MyOutputDir}
OutputBaseFilename=CodexThemeStore-Setup-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Codex-Skin.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "{#SourcePath}\languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#MyAppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Codex Theme Store"; Filename: "{app}\Codex-Skin.exe"
Name: "{autodesktop}\Codex Theme Store"; Filename: "{app}\Codex-Skin.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Run]
Filename: "{app}\Codex-Skin.exe"; Description: "启动 Codex Theme Store"; Flags: nowait postinstall skipifsilent
