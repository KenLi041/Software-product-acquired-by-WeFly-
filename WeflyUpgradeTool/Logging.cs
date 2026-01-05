using System;
using System.IO;

namespace WeflyUpgradeTool
{
    public static class Logging
    {
        private static readonly object _lock = new object();
        private static readonly string _logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WeflyUpgradeTool", "upgrade.log");

        static Logging()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
            }
            catch { }
        }

        public static void Write(string message)
        {
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
                }
                catch { }
            }
        }

        public static string LogFilePath => _logFile;
    }
}






