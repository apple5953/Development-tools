using System;
using System.IO;

namespace DevelopmentTools.Core
{
    public static class UpdateLogger
    {
        // 解析出非系統碟根目錄後寫到 <根目錄>\Update\Logs；解析前（或找不到非系統碟）沿用舊位置。
        private static string LogDir
        {
            get
            {
                string root = InstallLocation.ResolvedRootOrNull;
                if (!string.IsNullOrEmpty(root) && !InstallLocation.IsOnSystemDrive(root))
                {
                    return Path.Combine(root, "Update", "Logs");
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevelopmentTools", "Update", "Logs");
            }
        }
        private static string LogFile { get { return Path.Combine(LogDir, "update_log.txt"); } }

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
