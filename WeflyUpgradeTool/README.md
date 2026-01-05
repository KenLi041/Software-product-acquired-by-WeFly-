# Wefly 软件升级工具

本项目依据 `WF1000 无线图传系统软件升级工具设计说明书` 开发，实现：
- 通过 USB 串口 (CH34x) 与设备通信，波特率 921600，8N1
- UI 按钮：连接设备、加载文件、开始升级；显示设备类型、版本号、文件名、进度与日志
- 文件验证与解压：wefly_vxx_xx.rar → 密码 = 990401 + 最后一段数字（如 08）
- 解压后包含 `.mcs` 与 `.hex`，先传 FPGA，再传 MCU
- 协议：?version / $update start / 数据帧 / $update stop

## 构建

需要 .NET SDK 8.0（Windows 上构建和运行）。

```bash
dotnet build -c Release
dotnet run --project WeflyUpgradeTool
```

## 运行

1. 安装 `CH341SER` 驱动（随项目提供），插入 USB 转串口线。
2. 打开应用，点击“连接设备”。
3. 点击“加载文件”，选择 wefly_vxx_xx.rar。
4. 点击“开始升级”，等待进度完成并重启设备。

## 目录

- `CH341SER/` 驱动与安装程序
- `the files contained after extract.rar/` 示例解压后文件
- `WeflyUpgradeTool/` 源码目录

## 协议补充（数据帧）
- 帧格式：`# + 1B类型 + 2B帧号 + 4B地址 + 2B长度 + 数据(1-256B) + 2B校验`
- 校验范围：从“类型”开始到“最后一个数据字节”逐字节求和的低16位（包含2字节长度）
- 长度字段为大端（高字节在前），应与实际发送的数据字节数一致







