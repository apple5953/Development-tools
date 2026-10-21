using System;
using System.IO;
using System.Text;

namespace BEAI.QueryHost.Core
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logDirectory;

        public static void Initialize(string baseDir)
        {
            try
            {
                _logDirectory = Path.Combine(baseDir, "Logs");
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            catch
            {
                _logDirectory = DtStorage.TempDir;
            }
        }

        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public static void Warn(string message)
        {
            WriteLog("WARN", message);
        }

        public static void Error(string message)
        {
            Error(message, null);
        }

        public static void Error(string message, Exception ex)
        {
            string msg = ex != null ? string.Format("{0} | Exception: {1}\n{2}", message, ex.Message, ex.StackTrace) : message;
            WriteLog("ERROR", msg);
            RuntimeState.Instance.SetLastError(message);
        }

        public static void Query(string method, string path, long durationMs, int resultCount, string status)
        {
            WriteLog("QUERY", string.Format("{0} {1} - {2}ms - Count: {3} - Status: {4}", method, path, durationMs, resultCount, status));
            RuntimeState.Instance.RecordQuery();
        }

        private static void WriteLog(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(_logDirectory))
                    {
                        _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    }
                    if (!Directory.Exists(_logDirectory))
                    {
                        Directory.CreateDirectory(_logDirectory);
                    }

                    string logFileName = string.Format("beai_bridge_{0:yyyyMMdd}.log", DateTime.Now);
                    string fullPath = Path.Combine(_logDirectory, logFileName);
                    string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}", DateTime.Now, level, message);

                    if (File.Exists(fullPath) && new FileInfo(fullPath).Length > 10 * 1024 * 1024)
                    {
                        string rolledPath = Path.Combine(_logDirectory, string.Format("beai_bridge_{0:yyyyMMdd_HHmmss}.log", DateTime.Now));
                        File.Move(fullPath, rolledPath);
                    }

                    File.AppendAllText(fullPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
