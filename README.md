# 软件系统

本目录包含：

- `WF1000无线图传系统软件升级工具设计说明书.pdf` 与提取的 `WF1000_spec.txt`
- `CH341SER/` 串口驱动（需在 Windows 上安装）
- `the files contained after extract.rar/` 示例解压后文件（`.mcs` & `.hex`）
- `Tools/7zip/` 目录（放置 7z.exe/7z.dll 以支持 RAR 解压）
- `WeflyUpgradeTool/` Windows WPF 升级工具源码
- `build_windows.ps1` Windows 一键发布脚本
 - `build_installer.ps1` Windows 一键制作安装包（Inno Setup 6，需要已安装）
 - `installer/installer.iss` 安装器配置（自动创建桌面图标、可选安装驱动）

## 使用

1. 在 Windows 上安装 `CH341SER/DRVSETUP64/DRVSETUP64.exe`。
2.（macOS 用户）如果无法在本机构建，请使用 GitHub Actions：
   - 将本目录放入 Git 仓库并推送到 GitHub
   - 进入 GitHub 仓库 → Actions → 选择“Build Windows Single EXE”→ Run workflow
   - 等待完成后在“Artifacts”下载 `WeflyUpgradeTool.exe`（单文件）或 `publish.zip`
3.（本地 Windows 构建）用 PowerShell 运行：
   - `build_windows.ps1`（发布目录：`WeflyUpgradeTool/bin/Release/net8.0-windows/win-x64/publish`）
   - `build_installer.ps1`（安装包：`installer/Output/*.exe`，可一键安装和桌面图标）
4. 运行应用：
   - 点击“连接设备”识别发射机/接收机与版本号
   - 点击“加载文件”选择 `wefly_vxx_xx.rar`
   - 程序将按“990401 + 文件名最后一串数字”计算密码并解压；成功后先传 `.mcs` 再传 `.hex`
   - 升级完成提示“升级成功，请重启设备”

所有生成与源码均在本目录内，便于查找与交付。

