using System;
using System.IO;

namespace DT_WordPdfLink.Utils
{
    public static class Logger
    {
        private static string GetLogPath()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DT_WordPdfLink",
                "Logs"
            );
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            string fileName = string.Format("WordPdfLink_{0}.log", DateTime.Today.ToString("yyyy-MM-dd"));
            return Path.Combine(folder, fileName);
        }

        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public static void Error(string message, Exception ex)
        {
            string detail = message;
            if (ex != null)
            {
                detail = string.Format("{0}\nException: {1}\nStackTrace: {2}", message, ex.Message, ex.StackTrace);
            }
            WriteLog("ERROR", detail);
        }

        private static void WriteLog(string level, string content)
        {
            try
            {
                string path = GetLogPath();
                string line = string.Format("[{0}] [{1}] {2}\r\n", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), level, content);
                File.AppendAllText(path, line, System.Text.Encoding.UTF8);
            }
            catch
            {
                // Suppress logging errors to avoid crashing Revit
            }
        }
    }
}
