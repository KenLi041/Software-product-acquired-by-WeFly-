#define MyAppName "Wefly 软件升级工具"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Wefly"
#define MyAppExeName "WeflyUpgradeTool.exe"

; 构建前请先运行根目录的 build_installer.ps1，会生成 publish 目录并调用本脚本

[Setup]
AppId={{1F8F8F52-7A89-4E38-A23C-9D0F9D5C8A01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Wefly\WeflyUpgradeTool
DefaultGroupName=Wefly
DisableDirPage=no
DisableProgramGroupPage=no
OutputDir=Output
OutputBaseFilename=Wefly_Upgrade_Setup_{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
SetupLogging=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "在桌面创建图标"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
; 将发布目录下的所有文件打包到安装目录
Source: "..\WeflyUpgradeTool\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\\CH341SER\\DRVSETUP64\\DRVSETUP64.exe"; Description: "安装 CH34x 串口驱动 (可选)"; Flags: postinstall nowait shellexec skipifsilent
Filename: "{app}\\{#MyAppExeName}"; Description: "运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent







