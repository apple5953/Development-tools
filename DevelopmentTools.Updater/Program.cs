using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace DevelopmentTools.Updater
{
    class Program
    {
        private const int RevitExitTimeoutMs = 15 * 60 * 1000;
        private static string? _logDir;

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
                
                // 展開環境變數以支援免管理員 LocalAppData 路徑
                if (info != null)
                {
                    info.zip_path = Environment.ExpandEnvironmentVariables(info.zip_path ?? "");
                    info.install_dir = Environment.ExpandEnvironmentVariables(info.install_dir ?? "");
                    info.updater_dir = Environment.ExpandEnvironmentVariables(info.updater_dir ?? "");
                    info.backup_dir = Environment.ExpandEnvironmentVariables(info.backup_dir ?? "");
                    info.version_file_path = Environment.ExpandEnvironmentVariables(info.version_file_path ?? "");
                    info.source_install_dir = ExpandOrNull(info.source_install_dir);
                    info.source_updater_dir = ExpandOrNull(info.source_updater_dir);
                    info.extract_dir = ExpandOrNull(info.extract_dir);
                    info.log_dir = ExpandOrNull(info.log_dir);
                    if (!string.IsNullOrWhiteSpace(info.log_dir)) _logDir = info.log_dir;
                    if (string.IsNullOrWhiteSpace(info.updater_dir) && !string.IsNullOrWhiteSpace(info.install_dir))
                    {
                        var baseDir = Directory.GetParent(info.install_dir);
                        if (baseDir != null) info.updater_dir = Path.Combine(baseDir.FullName, "Updater");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Failed to parse pending update configuration.", ex);
                return 1;
            }

            if (!ValidatePendingInfo(info))
            {
                LogError("Update aborted: pending update configuration is incomplete.", null);
                return 1;
            }

            // 1. 等待 Revit 關閉
            Console.WriteLine("Waiting for the Revit session that started this update to close (Timeout: 15 minutes)...");
            bool revitClosed = WaitForRevitToExit(info.revit_process_id, RevitExitTimeoutMs);
            if (!revitClosed)
            {
                LogError("Update aborted: Revit did not exit within 15 minutes.", null);
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
            // 解壓縮目錄放在更新包旁（非系統碟），不使用 %TEMP%，避免公司防毒攔截 C: 暫存區中的執行檔。
            string extractBase = !string.IsNullOrWhiteSpace(info.extract_dir)
                ? info.extract_dir!
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(info.zip_path)) ?? Path.GetTempPath(), "Extract");
            string tempUnzipDir = Path.Combine(extractBase, "RTS_Update_" + Guid.NewGuid().ToString("N"));

            string sourceInstallDir = string.IsNullOrWhiteSpace(info.source_install_dir) ? info.install_dir : info.source_install_dir!;
            string sourceUpdaterDir = string.IsNullOrWhiteSpace(info.source_updater_dir) ? info.updater_dir : info.source_updater_dir!;
            bool relocating = !SamePath(sourceInstallDir, info.install_dir);
            var manifestBackups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (relocating)
            {
                Console.WriteLine($"Relocating installation: {sourceInstallDir} -> {info.install_dir}");
            }

            try
            {
                if (Directory.Exists(info.backup_dir))
                {
                    Directory.Delete(info.backup_dir, true);
                }
                Directory.CreateDirectory(info.backup_dir);

                Console.WriteLine("Backing up current installation...");
                if (Directory.Exists(sourceInstallDir))
                {
                    CopyDirectory(sourceInstallDir, Path.Combine(info.backup_dir, "App"));
                }
                if (Directory.Exists(sourceUpdaterDir))
                {
                    CopyDirectory(sourceUpdaterDir, Path.Combine(info.backup_dir, "Updater"));
                }
                if (relocating && info.addin_manifest_paths != null)
                {
                    foreach (string manifestPath in info.addin_manifest_paths)
                    {
                        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
                        {
                            manifestBackups[manifestPath] = File.ReadAllText(manifestPath);
                        }
                    }
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
                        string extractRoot = Path.GetFullPath(tempUnzipDir)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        string destinationPath = Path.GetFullPath(Path.Combine(tempUnzipDir, entry.FullName));
                        if (!destinationPath.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase))
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
                string coreDllPath = Path.Combine(tempUnzipDir, "DevelopmentTools.Addin.dll");
                if (!File.Exists(coreDllPath))
                {
                    throw new FileNotFoundException("Invalid update package: Missing DevelopmentTools.Addin.dll.");
                }

                Console.WriteLine("Installing new files...");
                if (Directory.Exists(info.install_dir))
                {
                    Directory.Delete(info.install_dir, true);
                }
                CopyDirectory(tempUnzipDir, info.install_dir, "UpdaterPayload");

                string updaterPayloadDir = Path.Combine(tempUnzipDir, "UpdaterPayload");
                if (Directory.Exists(updaterPayloadDir))
                {
                    Console.WriteLine("Updating updater files...");
                    if (Directory.Exists(info.updater_dir))
                    {
                        Directory.Delete(info.updater_dir, true);
                    }
                    CopyDirectory(updaterPayloadDir, info.updater_dir);
                }

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

                // 5.1 安裝位置已移出系統碟：改寫 Revit .addin 指向新的 DLL。
                if (relocating)
                {
                    string newAssembly = Path.Combine(info.install_dir, "DevelopmentTools.Addin.dll");
                    foreach (var pair in manifestBackups)
                    {
                        Console.WriteLine($"Updating Revit add-in registration: {pair.Key}");
                        File.WriteAllText(pair.Key, RewriteAddinAssembly(pair.Value, newAssembly), new UTF8Encoding(false));
                    }
                    if (manifestBackups.Count == 0)
                    {
                        LogError("Warning: no Revit .addin registration was found to relocate. Re-run DevelopmentTools_Setup.exe if Revit does not load the add-in.", null);
                    }
                }

                // 6. 清理 Staging 壓縮包與 pending_update.json
                Console.WriteLine("Cleaning up temporary update configuration...");
                if (File.Exists(info.zip_path)) File.Delete(info.zip_path);
                if (File.Exists(pendingJsonPath)) File.Delete(pendingJsonPath);
                if (relocating) CleanupLegacyStaging(sourceInstallDir);

                LogSuccess($"Successfully upgraded to version {info.new_version}!");
                Console.WriteLine("Update process completed successfully!");
                Thread.Sleep(2000);
                return 0;
            }
            catch (Exception ex)
            {
                LogError("Failed to apply update. Attempting rollback...", ex);
                if (relocating)
                {
                    RollbackRelocation(manifestBackups, info.install_dir, info.updater_dir);
                }
                else
                {
                    Rollback(info.backup_dir, info.install_dir, info.updater_dir);
                }
                return 5;
            }
            finally
            {
                if (Directory.Exists(tempUnzipDir))
                {
                    try { Directory.Delete(tempUnzipDir, true); } catch { }
                }
            }
        }

        private static bool ValidatePendingInfo(PendingUpdateInfo info)
        {
            return info != null
                && !string.IsNullOrWhiteSpace(info.zip_path)
                && !string.IsNullOrWhiteSpace(info.sha256)
                && !string.IsNullOrWhiteSpace(info.install_dir)
                && !string.IsNullOrWhiteSpace(info.updater_dir)
                && !string.IsNullOrWhiteSpace(info.backup_dir)
                && !string.IsNullOrWhiteSpace(info.version_file_path)
                && !string.IsNullOrWhiteSpace(info.new_version);
        }

        private static bool WaitForRevitToExit(int revitProcessId, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (revitProcessId > 0)
                {
                    try
                    {
                        using (var process = Process.GetProcessById(revitProcessId))
                        {
                            if (process.HasExited) return true;
                        }
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                }
                else if (Process.GetProcessesByName("Revit").Length == 0)
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

        private static string? ExpandOrNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : Environment.ExpandEnvironmentVariables(value);
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd('\\', '/'),
                    Path.GetFullPath(b).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static string RewriteAddinAssembly(string manifestXml, string newAssemblyPath)
        {
            string escaped = System.Security.SecurityElement.Escape(newAssemblyPath) ?? newAssemblyPath;
            var pattern = new Regex(@"<Assembly>[^<]*DevelopmentTools\.Addin\.dll\s*</Assembly>", RegexOptions.IgnoreCase);
            if (!pattern.IsMatch(manifestXml))
            {
                throw new InvalidDataException("Revit .addin registration does not reference DevelopmentTools.Addin.dll.");
            }
            return pattern.Replace(manifestXml, "<Assembly>" + escaped.Replace("$", "$$") + "</Assembly>");
        }

        private static void RollbackRelocation(Dictionary<string, string> manifestBackups, string newInstallDir, string newUpdaterDir)
        {
            // 舊安裝位置在遷移時完全沒有被修改，只要還原 .addin 並移除未完成的新目錄即可。
            Console.WriteLine("Performing Rollback (relocation)...");
            foreach (var pair in manifestBackups)
            {
                try { File.WriteAllText(pair.Key, pair.Value, new UTF8Encoding(false)); }
                catch (Exception ex) { LogError("Critical Error: failed to restore " + pair.Key, ex); }
            }
            try { if (Directory.Exists(newInstallDir)) Directory.Delete(newInstallDir, true); } catch { }
            try { if (Directory.Exists(newUpdaterDir)) Directory.Delete(newUpdaterDir, true); } catch { }
            Console.WriteLine("Rollback finished. The previous installation remains active.");
        }

        private static void CleanupLegacyStaging(string sourceInstallDir)
        {
            try
            {
                var legacyBase = Directory.GetParent(sourceInstallDir.TrimEnd('\\', '/'));
                if (legacyBase == null) return;
                foreach (string name in new[] { "Staging", "UpdaterStaging" })
                {
                    string dir = Path.Combine(legacyBase.FullName, "Update", name);
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
            }
            catch { }
        }

        private static string ResolveLogDir()
        {
            if (!string.IsNullOrWhiteSpace(_logDir)) return _logDir!;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevelopmentTools", "Update", "Logs");
        }

        private static void CopyDirectory(string sourceDir, string destDir, string excludedTopLevelDirectory = null)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                if (!string.IsNullOrEmpty(excludedTopLevelDirectory)
                    && string.Equals(Path.GetFileName(subDir), excludedTopLevelDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        private static void Rollback(string backupDir, string installDir, string updaterDir)
        {
            try
            {
                Console.WriteLine("Performing Rollback...");
                if (Directory.Exists(backupDir))
                {
                    string appBackup = Path.Combine(backupDir, "App");
                    string updaterBackup = Path.Combine(backupDir, "Updater");
                    if (Directory.Exists(appBackup))
                    {
                        if (Directory.Exists(installDir)) Directory.Delete(installDir, true);
                        CopyDirectory(appBackup, installDir);
                    }
                    if (Directory.Exists(updaterBackup))
                    {
                        if (Directory.Exists(updaterDir)) Directory.Delete(updaterDir, true);
                        CopyDirectory(updaterBackup, updaterDir);
                    }
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
                string logDir = ResolveLogDir();
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
                string logDir = ResolveLogDir();
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
        public string updater_dir { get; set; }
        public string backup_dir { get; set; }
        public string version_file_path { get; set; }
        public string new_version { get; set; }
        public string updated_at { get; set; }
        public int revit_process_id { get; set; }
        public string? source_install_dir { get; set; }
        public string? source_updater_dir { get; set; }
        public string? extract_dir { get; set; }
        public string? log_dir { get; set; }
        public List<string>? addin_manifest_paths { get; set; }
    }
}
