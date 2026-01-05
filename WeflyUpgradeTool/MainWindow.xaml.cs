using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace WeflyUpgradeTool
{
    public partial class MainWindow : Window
    {
        private readonly bool _mock = string.Equals(Environment.GetEnvironmentVariable("WEFLY_MOCK"), "1", StringComparison.Ordinal);
        private const int SerialBaudRate = 921600;
        private const Parity SerialParity = Parity.None;
        private const int SerialDataBits = 8;
        private const StopBits SerialStopBits = StopBits.One;

        private const int CommandTimeoutMs = 1000; // 减少超时时间从5秒到1秒
        private const int FastTransmitTimeoutMs = 200; // 数据传输时使用更短的超时
        private const string RarBasePasswordPrefix = "990401"; // 固定基数

        private SerialPort? _serial;
        private string? _tempExtractDir;
        private string? _mcsPath;
        private string? _hexPath;
        private string? _deviceType; // tx or rx
        private string? _version;    // xx_xx

        public MainWindow()
        {
            InitializeComponent();
            AppendLog("欢迎使用 Wefly 软件升级工具");
            RefreshPorts();
        }

        private void SafeUpdateUI(Action action)
        {
            try
            {
                if (Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    Dispatcher.Invoke(action);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"UI更新错误: {ex.Message}");
            }
        }

        private void AppendLog(string message)
        {
            try
            {
                // 确保在UI线程上执行
                if (LogBox != null)
                {
                    if (LogBox.Dispatcher.CheckAccess())
                    {
                        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                        LogBox.ScrollToEnd();
                    }
                    else
                    {
                        LogBox.Dispatcher.Invoke(() =>
                        {
                            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                            LogBox.ScrollToEnd();
                        });
                    }
                }
                Logging.Write(message);
            }
            catch (Exception ex)
            {
                // 防止日志记录本身导致崩溃
                System.Diagnostics.Debug.WriteLine($"日志记录错误: {ex.Message}");
            }
        }

        // 供 CI/云端在 WEFLY_MOCK=1 下做一次端到端冒烟测试
        public async Task RunMockUpgradeSmokeTestAsync()
        {
            if (!_mock) throw new InvalidOperationException("仅在模拟模式下执行");
            // 连接（模拟）
            OnConnectClicked(this, new RoutedEventArgs());
            await Task.Delay(200);
            // 加载文件（模拟）
            if (!ValidateAndExtractRar("wefly_v01_162.rar", out var extractDir, out var mcsPath, out var hexPath))
            {
                throw new InvalidOperationException("模拟解压失败");
            }
            _tempExtractDir = extractDir; _mcsPath = mcsPath; _hexPath = hexPath;
            // 升级（模拟）
            await StartUpdateHandshake();
            await TransmitFileAsync(_mcsPath!, true);
            await TransmitFileAsync(_hexPath!, false);
            await StopUpdateHandshake();
        }

        private async void OnConnectClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mock)
                {
                    _deviceType = "发射机";
                    _version = "01_02";
                    SafeUpdateUI(() =>
                    {
                        if (DeviceTypeText != null) DeviceTypeText.Text = _deviceType;
                        if (VersionText != null) VersionText.Text = _version;
                        if (LoadFileButton != null) LoadFileButton.IsEnabled = true;
                        if (StartButton != null) StartButton.IsEnabled = false;
                    });
                    AppendLog("已进入模拟模式：跳过串口握手");
                    return;
                }
                
                // 先确保之前的串口完全关闭
                await CloseSerialPortSafely();
                
                var portName = SelectSerialPort();
                if (string.IsNullOrEmpty(portName))
                {
                    AppendLog("未选择串口");
                    return;
                }

                // 添加重试机制处理端口占用问题
                const int maxRetries = 3;
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        _serial = new SerialPort(portName, SerialBaudRate, SerialParity, SerialDataBits, SerialStopBits)
                        {
                            ReadTimeout = CommandTimeoutMs,
                            WriteTimeout = CommandTimeoutMs,
                            Encoding = Encoding.ASCII
                        };
                        _serial.Open();
                        break; // 成功打开，退出重试循环
                    }
                    catch (UnauthorizedAccessException) when (retry < maxRetries - 1)
                    {
                        AppendLog($"端口 {portName} 被占用，等待后重试... ({retry + 1}/{maxRetries})");
                        await Task.Delay(1000); // 等待1秒后重试
                        try { _serial?.Close(); } catch { }
                        _serial = null;
                        continue;
                    }
                }
                
                if (_serial == null || !_serial.IsOpen)
                {
                    throw new InvalidOperationException($"无法打开串口 {portName}，可能被其他程序占用");
                }
                
                _serial.DtrEnable = true;
                _serial.RtsEnable = true;
                _serial.DiscardInBuffer();
                _serial.DiscardOutBuffer();
                await Task.Delay(100);
                AppendLog($"串口已打开: {portName}，DTR/RTS 已使能");

                var (ok, devType, ver, raw) = await TryDetectVersionAsync();
                AppendLog($"原始回应: {raw}");
                if (!ok)
                {
                    AppendLog("回应不匹配，连接失败");
                    SafeUpdateUI(() =>
                    {
                        if (DeviceTypeText != null) DeviceTypeText.Text = "未连接";
                        if (VersionText != null) VersionText.Text = "--";
                    });
                    return;
                }
                _deviceType = devType;
                _version = ver;

                SafeUpdateUI(() =>
                {
                    if (DeviceTypeText != null) DeviceTypeText.Text = _deviceType;
                    if (VersionText != null) VersionText.Text = _version;
                    if (LoadFileButton != null) LoadFileButton.IsEnabled = true;
                    if (StartButton != null) StartButton.IsEnabled = false;
                });
            }
            catch (Exception ex)
            {
                AppendLog($"连接失败: {ex.Message}");
                await CloseSerialPortSafely();
                SafeUpdateUI(() =>
                {
                    if (DeviceTypeText != null) DeviceTypeText.Text = "未连接";
                    if (VersionText != null) VersionText.Text = "--";
                    if (LoadFileButton != null) LoadFileButton.IsEnabled = false;
                    if (StartButton != null) StartButton.IsEnabled = false;
                });
            }
        }

        private string? SelectSerialPort()
        {
            if (PortCombo?.Items?.Count == 0 || PortCombo == null)
            {
                MessageBox.Show("未发现串口，请先插入 USB 转串口线并安装 CH34x 驱动", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            return PortCombo.SelectedItem?.ToString();
        }

        private void RefreshPorts()
        {
            try
            {
                if (PortCombo?.Items != null)
                {
                    PortCombo.Items.Clear();
                    foreach (var p in SerialPort.GetPortNames())
                    {
                        PortCombo.Items.Add(p);
                    }
                    if (PortCombo.Items.Count > 0) PortCombo.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"刷新串口列表失败: {ex.Message}");
            }
        }

        private void OnRefreshPorts(object sender, RoutedEventArgs e)
        {
            RefreshPorts();
        }

        private void OnInstallDriver(object sender, RoutedEventArgs e)
        {
            try
            {
                // 打开驱动安装程序目录
                var driverSetup = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CH341SER", "DRVSETUP64", "DRVSETUP64.exe");
                if (!File.Exists(driverSetup))
                {
                    // 回退到上级目录（项目根附带）
                    var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
                    driverSetup = System.IO.Path.Combine(root, "CH341SER", "DRVSETUP64", "DRVSETUP64.exe");
                }
                if (File.Exists(driverSetup))
                {
                    Process.Start(new ProcessStartInfo { FileName = driverSetup, UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("未找到驱动安装程序，请手动运行 CH341SER/DRVSETUP64/DRVSETUP64.exe", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AppendLog("启动驱动安装失败: " + ex.Message);
            }
        }

        private async Task<string> SendCommandAndReadLine(string command)
        {
            if (_mock)
            {
                // 简单模拟
                if (command.StartsWith("?version")) return "tx_ver_01_02";
                if (command.StartsWith("$update start")) return "start ok";
                if (command.StartsWith("$update stop")) return "stop ok";
                return string.Empty;
            }
            if (_serial == null) throw new InvalidOperationException("串口未打开");
            return await Task.Run(() =>
            {
                _serial.Write(command);
                return ReadAvailableWithin(CommandTimeoutMs);
            });
        }

        private void OnLoadFileClicked(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择升级压缩包 (wefly_vxx_xx.rar)",
                Filter = "RAR 压缩包 (*.rar)|*.rar|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            var selectedPath = dlg.FileName;
            SafeUpdateUI(() =>
            {
                if (FileNameText != null) FileNameText.Text = System.IO.Path.GetFileName(selectedPath);
            });
            AppendLog($"已选择: {selectedPath}");

            if (!ValidateAndExtractRar(selectedPath, out var extractDir, out var mcsPath, out var hexPath))
            {
                AppendLog("非法文件或解压失败");
                MessageBox.Show("文件校验失败：非法文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                SafeUpdateUI(() =>
                {
                    if (FileNameText != null) FileNameText.Text = "非法文件";
                    if (StartButton != null) StartButton.IsEnabled = false;
                });
                return;
            }

            _tempExtractDir = extractDir;
            _mcsPath = mcsPath;
            _hexPath = hexPath;
            AppendLog("文件合法，解压完成");
            SafeUpdateUI(() =>
            {
                if (StartButton != null) StartButton.IsEnabled = true;
            });
        }

        private static int ComputePasswordFromFilename(string fileName)
        {
            // 规则：取 .rar 之前的最后一串数字，与基数 990401 相加
            // 示例：wefly_vxx_08.rar => 990401 + 8 = 990409
            // 兼容如 wefly_v01_162.rar.rar：反复去除 .rar 后缀
            string name = System.IO.Path.GetFileName(fileName);
            while (name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }
            var nameWithoutExt = name;
            var matches = Regex.Matches(nameWithoutExt, "[0-9]+");
            int lastNumber = 0;
            if (matches.Count > 0)
            {
                // 处理前导零，如 "08" -> 8
                if (!int.TryParse(matches[matches.Count - 1].Value, out lastNumber))
                {
                    lastNumber = 0;
                }
            }
            int baseNum = int.Parse(RarBasePasswordPrefix);
            return baseNum + lastNumber;
        }

        private bool ValidateAndExtractRar(string rarPath, out string extractDir, out string mcsPath, out string hexPath)
        {
            extractDir = mcsPath = hexPath = string.Empty;
            try
            {
                string[] mcsFiles = Array.Empty<string>();
                string[] hexFiles = Array.Empty<string>();
                if (_mock)
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var sampleDir = System.IO.Path.Combine(baseDir, "the files contained after extract.rar");
                    mcsFiles = Directory.GetFiles(sampleDir, "*.mcs", SearchOption.AllDirectories);
                    hexFiles = Directory.GetFiles(sampleDir, "*.hex", SearchOption.AllDirectories);
                    if (mcsFiles.Length == 0 || hexFiles.Length == 0)
                    {
                        AppendLog("模拟模式：未找到示例 .mcs 或 .hex");
                        return false;
                    }
                    extractDir = sampleDir;
                    mcsPath = mcsFiles[0];
                    hexPath = hexFiles[0];
                    AppendLog("模拟模式：已使用示例文件作为解压结果");
                    return true;
                }
                if (!rarPath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                int password = ComputePasswordFromFilename(rarPath);
                AppendLog($"计算密码: {password}");

                // 使用内嵌 UnRAR 进行解压
                string workDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_extract");
                if (!Directory.Exists(workDir)) Directory.CreateDirectory(workDir);

                string outDir = System.IO.Path.Combine(workDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(outDir);

                bool ok = RarExtractor.ExtractWithPassword(rarPath, password.ToString(), outDir, s => AppendLog(s));
                if (!ok) return false;

                // 期望包含 .mcs 和 .hex
                mcsFiles = Directory.GetFiles(outDir, "*.mcs", SearchOption.AllDirectories);
                hexFiles = Directory.GetFiles(outDir, "*.hex", SearchOption.AllDirectories);
                if (mcsFiles.Length == 0 || hexFiles.Length == 0)
                {
                    AppendLog("解压文件不完整，未发现 .mcs 或 .hex");
                    return false;
                }

                extractDir = outDir;
                mcsPath = mcsFiles[0];
                hexPath = hexFiles[0];
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"RAR 校验/解压异常: {ex.Message}");
                return false;
            }
        }

        private async void OnStartUpgradeClicked(object sender, RoutedEventArgs e)
        {
            if (_serial == null || string.IsNullOrEmpty(_mcsPath) || string.IsNullOrEmpty(_hexPath))
            {
                MessageBox.Show("缺少设备连接或文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SafeUpdateUI(() =>
                {
                    if (StartButton != null) StartButton.IsEnabled = false;
                    if (LoadFileButton != null) LoadFileButton.IsEnabled = false;
                });

                AppendLog("开始升级: 先发送 FPGA (mcs)，再发送 MCU (hex)");
                bool success = false;
                await StartUpdateHandshake();

                // 发送 FPGA
                await TransmitFileAsync(_mcsPath!, isFpga: true);

                // 发送 MCU
                await TransmitFileAsync(_hexPath!, isFpga: false);

                await StopUpdateHandshake();

                SafeUpdateUI(() =>
                {
                    if (Progress != null) Progress.Value = 100;
                });
                AppendLog("升级成功，请重启设备");
                MessageBox.Show("升级成功，请重启设备", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                success = true;
                // 成功后删除临时目录
                try
                {
                    if (success && !string.IsNullOrEmpty(_tempExtractDir) && Directory.Exists(_tempExtractDir))
                    {
                        Directory.Delete(_tempExtractDir, true);
                        AppendLog("已清理临时文件");
                    }
                }
                catch { }
            }
            catch (TimeoutException)
            {
                AppendLog("升级超时失败，请关机检查串口连接线，然后开机重新连接");
                MessageBox.Show("升级超时失败，请检查连接", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                AppendLog($"升级失败: {ex.Message}");
                MessageBox.Show("升级失败: " + ex.Message, "失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SafeUpdateUI(() =>
                {
                    if (LoadFileButton != null) LoadFileButton.IsEnabled = true;
                    if (StartButton != null) StartButton.IsEnabled = true;
                });
            }
        }

        private async Task CloseSerialPortSafely()
        {
            if (_serial != null)
            {
                try
                {
                    if (_serial.IsOpen)
                    {
                        _serial.DiscardInBuffer();
                        _serial.DiscardOutBuffer();
                        _serial.Close();
                        AppendLog("串口已安全关闭");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"关闭串口时出错: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        _serial.Dispose();
                    }
                    catch { }
                    _serial = null;
                }
                // 给系统时间释放资源
                await Task.Delay(100);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 同步关闭串口
            Task.Run(async () => await CloseSerialPortSafely()).Wait(2000);
            base.OnClosing(e);
        }

        private async Task StartUpdateHandshake()
        {
            if (_mock)
            {
                AppendLog("模拟模式：start ok");
                return;
            }
            var resp = await SendCommandMultiEndings("$update start");
            if (!resp.Contains("start ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("设备未接受开始传输");
            }
            AppendLog("握手成功: start ok");
        }

        private async Task StopUpdateHandshake()
        {
            if (_mock)
            {
                AppendLog("模拟模式：stop ok");
                return;
            }
            var resp = await SendCommandMultiEndings("$update stop");
            if (!resp.Contains("stop ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("设备未确认停止传输");
            }
            AppendLog("停止成功: stop ok");
        }

        private async Task TransmitFileAsync(string filePath, bool isFpga)
        {
            // 解析 .mcs 或 .hex，按规范打包帧并发送
            var parser = isFpga ? (IRecordFileParser)new McsParser() : new HexParser();
            var frames = parser.ParseToFrames(filePath, isFpga);

            int total = frames.Count;
            for (int i = 0; i < total; i++)
            {
                var frame = frames[i];
                await SendFrameWithRetry(frame);
                SafeUpdateUI(() =>
                {
                    if (Progress != null) Progress.Value = (int)((i + 1) * 100.0 / total);
                });
            }
        }

        private async Task SendFrameWithRetry(DataFrame frame)
        {
            if (_mock)
            {
                await Task.CompletedTask; // 直接视为成功
                return;
            }
            const int MaxRetry = 3;
            for (int attempt = 1; attempt <= MaxRetry; attempt++)
            {
                SendSingleFrame(frame);
                // 使用更短的超时时间进行快速传输
                string resp = await ReadLineWithFastTimeout();
                if (resp.StartsWith("received", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (resp.IndexOf("cheksum error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    resp.IndexOf("checksum error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AppendLog($"校验错误，重传 第{attempt}次");
                    continue;
                }
                // 其它异常回应也重试
                AppendLog($"异常回应[{resp}]，重试 第{attempt}次");
            }
            throw new TimeoutException("多次发送未被确认");
        }

        private void SendSingleFrame(DataFrame frame)
        {
            if (_mock) return;
            if (_serial == null) throw new InvalidOperationException("串口未打开");
            // 帧格式：# [1B 类型] [2B 帧号] [4B 地址] [2B 长度] [1-256B 数据] [2B 校验]
            using var ms = new MemoryStream();
            ms.WriteByte((byte)'#');
            ms.WriteByte(frame.DataType);
            ms.WriteByte((byte)(frame.FrameNumber >> 8));
            ms.WriteByte((byte)(frame.FrameNumber & 0xFF));
            ms.WriteByte((byte)(frame.Address >> 24));
            ms.WriteByte((byte)((frame.Address >> 16) & 0xFF));
            ms.WriteByte((byte)((frame.Address >> 8) & 0xFF));
            ms.WriteByte((byte)(frame.Address & 0xFF));
            // 修正：数据长度应为2字节，高字节在前（大端格式）
            ushort payloadLen = (ushort)frame.Data.Length;
            ms.WriteByte((byte)(payloadLen >> 8));
            ms.WriteByte((byte)(payloadLen & 0xFF));
            ms.Write(frame.Data, 0, frame.Data.Length);

            // 校验从数据类型开始到最后一个数据字节相加之和（16位）
            ushort checksum = 0;
            checksum += frame.DataType;
            checksum += (ushort)(frame.FrameNumber >> 8);
            checksum += (ushort)(frame.FrameNumber & 0xFF);
            checksum += (ushort)(frame.Address >> 24);
            checksum += (ushort)((frame.Address >> 16) & 0xFF);
            checksum += (ushort)((frame.Address >> 8) & 0xFF);
            checksum += (ushort)(frame.Address & 0xFF);
            // 修正：校验和需要包含2字节长度字段
            checksum += (ushort)(payloadLen >> 8);
            checksum += (ushort)(payloadLen & 0xFF);
            foreach (var b in frame.Data) checksum += b;

            ms.WriteByte((byte)(checksum >> 8));
            ms.WriteByte((byte)(checksum & 0xFF));

            var buffer = ms.ToArray();
            _serial.Write(buffer, 0, buffer.Length);
            // 简化日志记录以提高性能 - 只记录关键信息
            try
            {
                // 只在前几帧和错误时记录详细信息
                if (frame.FrameNumber < 5 || frame.FrameNumber % 100 == 0)
                {
                    AppendLog($"TX frame #{frame.FrameNumber}: len={frame.Data.Length}, addr=0x{frame.Address:X8}");
                }
            }
            catch { }
        }

        private async Task<string> ReadLineWithTimeout()
        {
            if (_mock) return await Task.FromResult("received 00");
            if (_serial == null) throw new InvalidOperationException("串口未打开");
            return await Task.Run(() =>
            {
                var data = ReadAvailableWithin(CommandTimeoutMs);
                if (string.IsNullOrEmpty(data)) throw new TimeoutException("设备回应超时");
                return data;
            });
        }

        private async Task<string> ReadLineWithFastTimeout()
        {
            if (_mock) return await Task.FromResult("received 00");
            if (_serial == null) throw new InvalidOperationException("串口未打开");
            return await Task.Run(() =>
            {
                var data = ReadAvailableWithin(FastTransmitTimeoutMs);
                if (string.IsNullOrEmpty(data)) throw new TimeoutException("设备回应超时");
                return data;
            });
        }

        private string ReadAvailableWithin(int timeoutMs)
        {
            if (_serial == null) throw new InvalidOperationException("串口未打开");
            var startedAt = DateTime.UtcNow;
            var sb = new StringBuilder();
            int noDataCount = 0;
            const int maxNoDataIterations = 10; // 减少无数据等待次数
            
            while ((DateTime.UtcNow - startedAt).TotalMilliseconds < timeoutMs)
            {
                try
                {
                    var chunk = _serial.ReadExisting();
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        sb.Append(chunk);
                        noDataCount = 0; // 重置计数器
                        
                        // 检查常见的回应模式
                        var response = sb.ToString();
                        if (response.Contains("received") || 
                            response.Contains("start ok") || 
                            response.Contains("stop ok") ||
                            response.Contains("tx_ver") ||
                            response.Contains("rx_ver") ||
                            response.Contains("error"))
                        {
                            // 给一点时间接收剩余字符然后立即返回
                            Thread.Sleep(5);
                            var final = _serial.ReadExisting();
                            if (!string.IsNullOrEmpty(final)) sb.Append(final);
                            break;
                        }
                        Thread.Sleep(2); // 减少延时
                    }
                    else
                    {
                        noDataCount++;
                        if (noDataCount > maxNoDataIterations && sb.Length > 0) break;
                        Thread.Sleep(2); // 减少延时
                    }
                }
                catch (TimeoutException) { }
            }
            return sb.ToString();
        }

        private async Task<(bool ok, string deviceType, string version, string raw)> TryDetectVersionAsync()
        {
            if (_serial == null) throw new InvalidOperationException("串口未打开");
            // 优化：先尝试最常见的格式，避免不必要的重试
            var suffixes = new[] { "\n", "\r\n", "\r", string.Empty };
            string all = string.Empty;
            foreach (var sfx in suffixes)
            {
                _serial.DiscardInBuffer();
                var resp = await SendCommandAndReadLine("?version" + sfx);
                all += resp + "\n";
                var parsed = ParseVersion(resp);
                if (parsed.ok) return (true, parsed.deviceType, parsed.version, all.Trim());
                // 减少延时从100ms到50ms
                await Task.Delay(50);
            }
            // 减少额外等待时间从300ms到100ms
            var extra = ReadAvailableWithin(100);
            all += extra;
            var finalParsed = ParseVersion(all);
            if (finalParsed.ok) return (true, finalParsed.deviceType, finalParsed.version, all.Trim());
            return (false, string.Empty, string.Empty, all.Trim());
        }

        private (bool ok, string deviceType, string version) ParseVersion(string text)
        {
            if (string.IsNullOrEmpty(text)) return (false, string.Empty, string.Empty);
            var t = text.Trim();
            var txIdx = t.IndexOf("tx_ver", StringComparison.OrdinalIgnoreCase);
            var rxIdx = t.IndexOf("rx_ver", StringComparison.OrdinalIgnoreCase);
            if (txIdx >= 0)
            {
                var ver = ExtractVersionSuffix(t.Substring(txIdx + 6));
                return (true, "发射机", ver);
            }
            if (rxIdx >= 0)
            {
                var ver = ExtractVersionSuffix(t.Substring(rxIdx + 6));
                return (true, "接收机", ver);
            }
            return (false, string.Empty, string.Empty);
        }

        private string ExtractVersionSuffix(string s)
        {
            var m = Regex.Match(s, @"[^0-9]*([0-9]{2})[^0-9]?([0-9]{2})");
            if (m.Success) return m.Groups[1].Value + "_" + m.Groups[2].Value;
            s = s.Trim();
            return s.Length > 8 ? s.Substring(0, 8) : s;
        }

        private async Task<string> SendCommandMultiEndings(string baseCmd)
        {
            // 优化：先尝试最常见的格式
            var suffixes = new[] { "\n", "\r\n", "\r", string.Empty };
            var all = new StringBuilder();
            foreach (var s in suffixes)
            {
                var r = await SendCommandAndReadLine(baseCmd + s);
                all.AppendLine(r);
                if (!string.IsNullOrWhiteSpace(r)) return all.ToString();
                // 添加短暂延时避免命令冲突
                await Task.Delay(25);
            }
            return all.ToString();
        }
    }
}
