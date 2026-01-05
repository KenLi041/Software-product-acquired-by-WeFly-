using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WeflyUpgradeTool
{
    public static class RarExtractor
    {
        private enum RarToolType { Unrar, WinRAR, SevenZip, None }

        // 优先使用内嵌 UnRAR 工具；若不存在，则尝试系统 Path 中的 unrar
        public static bool ExtractWithPassword(string rarPath, string password, string outputDir, Action<string>? log = null)
        {
            try
            {
                Directory.CreateDirectory(outputDir);

                // 1) 内嵌 UnRAR
                string embeddedUnrar = ExtractEmbeddedUnrarIfNeeded(log);
                if (!string.IsNullOrEmpty(embeddedUnrar))
                {
                    return RunTool(embeddedUnrar, RarToolType.Unrar, rarPath, password, outputDir, log);
                }

                // 2) 检测系统中已安装的工具（优先 WinRAR/UnRAR，再尝试 7z）
                var (exe, type) = DetectExistingTool(log);
                if (type != RarToolType.None && !string.IsNullOrEmpty(exe))
                {
                    return RunTool(exe, type, rarPath, password, outputDir, log);
                }

                // 3) 未检测到任何可用工具，提示下载
                log?.Invoke("未检测到可用的 RAR 解压工具，将打开下载页面");
                TryOpenUrl("https://www.win-rar.com/download.html", log);
                return false;
            }
            catch (Exception ex)
            {
                log?.Invoke("UnRAR 异常: " + ex.Message);
                return false;
            }
        }

        public static bool EnsureToolAvailableOrPrompt(Action<string>? log = null)
        {
            // 如果内嵌或本机存在解压工具则返回 true，否则打开下载页面并返回 false
            string embeddedUnrar = ExtractEmbeddedUnrarIfNeeded(log);
            if (!string.IsNullOrEmpty(embeddedUnrar)) return true;
            var (exe, type) = DetectExistingTool(log);
            if (type != RarToolType.None && !string.IsNullOrEmpty(exe)) return true;
            log?.Invoke("未检测到解压工具，将打开 WinRAR 下载页面");
            TryOpenUrl("https://www.win-rar.com/download.html", log);
            return false;
        }

        private static (string exePath, RarToolType type) DetectExistingTool(Action<string>? log)
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return (string.Empty, RarToolType.None);
                }

                // 常见安装路径
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinRAR", "UnRAR.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinRAR", "UnRAR.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinRAR", "WinRAR.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinRAR", "WinRAR.exe"),
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        if (string.Equals(Path.GetFileName(c), "UnRAR.exe", StringComparison.OrdinalIgnoreCase))
                            return (c, RarToolType.Unrar);
                        if (string.Equals(Path.GetFileName(c), "WinRAR.exe", StringComparison.OrdinalIgnoreCase))
                            return (c, RarToolType.WinRAR);
                    }
                }

                // PATH 中查找 unrar / winrar / rar / 7z
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in pathEnv.Split(';').Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    string Try(string name) => Path.Combine(dir.Trim(), name);
                    if (File.Exists(Try("unrar.exe"))) return (Try("unrar.exe"), RarToolType.Unrar);
                    if (File.Exists(Try("winrar.exe"))) return (Try("winrar.exe"), RarToolType.WinRAR);
                    if (File.Exists(Try("rar.exe"))) return (Try("rar.exe"), RarToolType.Unrar);
                    if (File.Exists(Try("7z.exe"))) return (Try("7z.exe"), RarToolType.SevenZip);
                }

                return (string.Empty, RarToolType.None);
            }
            catch (Exception ex)
            {
                log?.Invoke("检测解压工具失败: " + ex.Message);
                return (string.Empty, RarToolType.None);
            }
        }

        private static bool RunTool(string exe, RarToolType type, string rarPath, string password, string outputDir, Action<string>? log)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            switch (type)
            {
                case RarToolType.Unrar:
                    psi.Arguments = $"x -y -p{password} \"{rarPath}\" \"{outputDir}\"";
                    break;
                case RarToolType.WinRAR:
                    // WinRAR.exe 也支持 x -y -p
                    psi.Arguments = $"x -y -p{password} \"{rarPath}\" \"{outputDir}\"";
                    break;
                case RarToolType.SevenZip:
                    // 一些 7z 版本支持 RAR/RAR5 解压；若用户 PATH 中有 7z，则作为兜底尝试
                    psi.Arguments = $"x -p{password} -y -o\"{outputDir}\" \"{rarPath}\"";
                    break;
                default:
                    return false;
            }
            var proc = Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            log?.Invoke(Path.GetFileName(exe) + " 输出:\n" + stdout + "\n" + stderr);
            return proc.ExitCode == 0;
        }

        private static string ExtractEmbeddedUnrarIfNeeded(Action<string>? log)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var names = asm.GetManifestResourceNames();
                var unrarRes = names.FirstOrDefault(n => n.Contains("Embedded.Unrar", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                if (unrarRes == null) return string.Empty;

                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_bin");
                Directory.CreateDirectory(tempDir);
                string outPath = Path.Combine(tempDir, "unrar.exe");
                using (var s = asm.GetManifestResourceStream(unrarRes))
                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                {
                    s!.CopyTo(fs);
                }
                return outPath;
            }
            catch (Exception ex)
            {
                log?.Invoke("解出内嵌 UnRAR 失败: " + ex.Message);
                return string.Empty;
            }
        }

        private static void TryOpenUrl(string url, Action<string>? log)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                log?.Invoke("打开浏览器失败: " + ex.Message);
            }
        }
    }
}


