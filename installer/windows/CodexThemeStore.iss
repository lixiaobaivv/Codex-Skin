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
AppName=Codex-Skin
AppVersion={#MyAppVersion}
AppPublisher=Codex Skin
DefaultDirName={localappdata}\Programs\Codex-Skin
DefaultGroupName=Codex-Skin
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#MyOutputDir}
OutputBaseFilename=Codex-Skin-Setup-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\..\assets\Codex-Skin.ico
UninstallDisplayIcon={app}\Codex-Skin.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "{#SourcePath}\languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#MyAppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\Classes\dreamskin"; ValueType: string; ValueName: ""; ValueData: "URL:Dream Skin Import Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\dreamskin"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\dreamskin\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Codex-Skin.exe,0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\dreamskin\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Codex-Skin.exe"" ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.dreamskin"; ValueType: string; ValueName: ""; ValueData: "CodexSkin.dreamskin"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CodexSkin.dreamskin"; ValueType: string; ValueName: ""; ValueData: "Codex Dream Skin Theme Package"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CodexSkin.dreamskin\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Codex-Skin.exe,0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CodexSkin.dreamskin\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Codex-Skin.exe"" ""%1"""; Flags: uninsdeletekey

[Icons]
Name: "{autoprograms}\Codex-Skin"; Filename: "{app}\Codex-Skin.exe"
Name: "{autodesktop}\Codex-Skin"; Filename: "{app}\Codex-Skin.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Run]
Filename: "{app}\Codex-Skin.exe"; Description: "启动 Codex-Skin"; Flags: nowait postinstall skipifsilent
