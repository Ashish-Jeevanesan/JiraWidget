using System;
using System.IO;

namespace JiraWidget
{
    public static class AppLogger
    {
        private static readonly object _sync = new();
        private static readonly string _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JiraWidget");
        private static readonly string _logPath = Path.Combine(_logDirectory, "jira-widget.log");

        public static string LogPath => _logPath;

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Error(string message, Exception? ex = null)
        {
            var finalMessage = ex == null
                ? message
                : $"{message}{Environment.NewLine}{ex}";
            Write("ERROR", finalMessage);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (_sync)
                {
                    Directory.CreateDirectory(_logDirectory);
                    var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(_logPath, line);
                }
            }
            catch
            {
                // Never throw from logger.
            }
        }
    }
}
