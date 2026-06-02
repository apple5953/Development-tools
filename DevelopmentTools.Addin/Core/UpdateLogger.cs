using System;
using System.IO;

namespace DevelopmentTools.Core
{
    public static class UpdateLogger
    {
        private static readonly string LogDir = @"C:\ProgramData\DevelopmentTools\Update\Logs";
        private static readonly string LogFile = Path.Combine(LogDir, "update_log.txt");

        public static void Log(string message, Exception ex = null)
        {
            try
            {
                if (!Directory.Exists(LogDir))
                {
                    Directory.CreateDirectory(LogDir);
                }

                string logText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                if (ex != null)
                {
                    logText += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";
                }

                File.AppendAllText(LogFile, logText + Environment.NewLine);
            }
            catch
            {
                // 絕不能因為 Log 失敗而讓 Revit 崩潰
            }
        }
    }
}
