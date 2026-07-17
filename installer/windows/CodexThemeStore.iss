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
UninstallDisplayIcon={app}\Codex-Skin.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "{#SourcePath}\languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#MyAppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Codex-Skin"; Filename: "{app}\Codex-Skin.exe"
Name: "{autodesktop}\Codex-Skin"; Filename: "{app}\Codex-Skin.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Run]
Filename: "{app}\Codex-Skin.exe"; Parameters: "protocol register"; StatusMsg: "正在注册网页主题导入协议..."; Flags: runhidden waituntilterminated
Filename: "{app}\Codex-Skin.exe"; Description: "启动 Codex-Skin"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Codex-Skin.exe"; Parameters: "protocol unregister"; Flags: runhidden waituntilterminated skipifdoesntexist
