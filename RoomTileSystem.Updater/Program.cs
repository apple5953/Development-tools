using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace RoomTileSystem.Updater
{
    class Program
    {
        static int Main(string[] args)
        {
            string pendingJsonPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--pending" && i + 1 < args.Length)
                {
                    pendingJsonPath = args[i + 1];
                }
            }

            if (string.IsNullOrEmpty(pendingJsonPath) || !File.Exists(pendingJsonPath))
            {
                Console.WriteLine("Error: Missing --pending update configuration path.");
                Thread.Sleep(3000);
                return 1;
            }

            Console.WriteLine("=== Room Tile System Update Tool ===");
            Console.WriteLine($"Reading configuration from: {pendingJsonPath}");

            PendingUpdateInfo info = null;
            try
            {
                string rawJson = File.ReadAllText(pendingJsonPath);
                info = JsonSerializer.Deserialize<PendingUpdateInfo>(rawJson);
            }
            catch (Exception ex)
            {
                LogError("Failed to parse pending update configuration.", ex);
                return 1;
            }

            // 1. 等待 Revit 關閉
            Console.WriteLine("Waiting for Revit to close (Timeout: 60s)...");
            bool revitClosed = WaitForProcessToExit("Revit", 60000);
            if (!revitClosed)
            {
                LogError("Update aborted: Revit.exe did not exit within 60 seconds.", null);
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return 2;
            }

            // 2. 校驗 ZIP 的 SHA256 雜湊值
            Console.WriteLine("Verifying package hash...");
            if (!VerifySha256(info.zip_path, info.sha256))
            {
                LogError("Update aborted: Staged ZIP SHA256 hash mismatch.", null);
                return 3;
            }

            // 3. 備份
            string tempUnzipDir = Path.Combine(Path.GetTempPath(), "RTS_Update_" + Guid.NewGuid().ToString("N"));
            try
            {
                if (Directory.Exists(info.backup_dir))
                {
                    Directory.Delete(info.backup_dir, true);
                }
                Directory.CreateDirectory(info.backup_dir);

                Console.WriteLine("Backing up current installation...");
                if (Directory.Exists(info.install_dir))
                {
                    CopyDirectory(info.install_dir, info.backup_dir);
                }
            }
            catch (Exception ex)
            {
                LogError("Failed to create backups. Aborting update.", ex);
                return 4;
            }

            // 4. 解壓縮並安全複製
            try
            {
                Console.WriteLine("Extracting update package...");
                Directory.CreateDirectory(tempUnzipDir);
                
                using (ZipArchive archive = ZipFile.OpenRead(info.zip_path))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string destinationPath = Path.GetFullPath(Path.Combine(tempUnzipDir, entry.FullName));
                        if (!destinationPath.StartsWith(Path.GetFullPath(tempUnzipDir), StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("Zip Slip Vulnerability Detected! Aborting unzip.");
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            string dir = Path.GetDirectoryName(destinationPath);
                            if (!Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            entry.ExtractToFile(destinationPath, true);
                        }
                    }
                }

                // 檢查是否有核心 DLL 存在
                string coreDllPath = Path.Combine(tempUnzipDir, "RoomTileSystem.Addin.dll");
                if (!File.Exists(coreDllPath))
                {
                    throw new FileNotFoundException("Invalid update package: Missing RoomTileSystem.Addin.dll.");
                }

                Console.WriteLine("Installing new files...");
                CopyDirectory(tempUnzipDir, info.install_dir);

                // 5. 更新本機 version.json 中的版本與時間
                Console.WriteLine("Updating version meta...");
                if (File.Exists(info.version_file_path))
                {
                    string localJson = File.ReadAllText(info.version_file_path);
                    var versionInfo = JsonSerializer.Deserialize<LocalVersionInfo>(localJson);
                    versionInfo.current_version = info.new_version;
                    versionInfo.updated_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

                    string updatedJson = JsonSerializer.Serialize(versionInfo, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(info.version_file_path, updatedJson);
                }

                // 6. 清理 Staging 壓縮包與 pending_update.json
                Console.WriteLine("Cleaning up temporary update configuration...");
                if (File.Exists(info.zip_path)) File.Delete(info.zip_path);
                if (File.Exists(pendingJsonPath)) File.Delete(pendingJsonPath);

                LogSuccess($"Successfully upgraded to version {info.new_version}!");
                Console.WriteLine("Update process completed successfully!");
                Thread.Sleep(2000);
                return 0;
            }
            catch (Exception ex)
            {
                LogError("Failed to apply update. Attempting rollback...", ex);
                Rollback(info.backup_dir, info.install_dir);
                if (File.Exists(pendingJsonPath)) File.Delete(pendingJsonPath);
                return 5;
            }
            finally
            {
                if (Directory.Exists(tempUnzipDir))
                {
                    Directory.Delete(tempUnzipDir, true);
                }
            }
        }

        private static bool WaitForProcessToExit(string name, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length == 0)
                {
                    return true;
                }
                Thread.Sleep(1000);
            }
            return false;
        }

        private static bool VerifySha256(string filePath, string expectedHash)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                using (var sha256 = SHA256.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        byte[] hash = sha256.ComputeHash(stream);
                        string computed = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        return computed.Equals(expectedHash.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        private static void Rollback(string backupDir, string installDir)
        {
            try
            {
                Console.WriteLine("Performing Rollback...");
                if (Directory.Exists(backupDir))
                {
                    CopyDirectory(backupDir, installDir);
                    Console.WriteLine("Rollback successful.");
                }
            }
            catch (Exception ex)
            {
                LogError("Critical Error: Rollback failed! System state might be corrupted.", ex);
            }
        }

        private static void LogError(string message, Exception ex)
        {
            string logText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}";
            if (ex != null)
            {
                logText += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";
            }
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(logText);
            Console.ResetColor();

            try
            {
                string logDir = @"C:\ProgramData\RoomTileSystem\Update\Logs";
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "update_log.txt"), logText + Environment.NewLine);
            }
            catch { }
        }

        private static void LogSuccess(string message)
        {
            string logText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SUCCESS: {message}";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(logText);
            Console.ResetColor();

            try
            {
                string logDir = @"C:\ProgramData\RoomTileSystem\Update\Logs";
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "update_log.txt"), logText + Environment.NewLine);
            }
            catch { }
        }
    }

    public class LocalVersionInfo
    {
        public string app_id { get; set; }
        public string product_name { get; set; }
        public string current_version { get; set; }
        public string channel { get; set; }
        public string main_dll { get; set; }
        public string install_folder { get; set; }
        public string updater_path { get; set; }
        public string manifest_url { get; set; }
        public string updated_at { get; set; }
    }

    public class PendingUpdateInfo
    {
        public string zip_path { get; set; }
        public string sha256 { get; set; }
        public string install_dir { get; set; }
        public string backup_dir { get; set; }
        public string version_file_path { get; set; }
        public string new_version { get; set; }
        public string updated_at { get; set; }
    }
}
