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

            // 舊版外掛（3.2.13 以前）產生的 pending 檔沒有遷移資訊；由 Updater 自行決定是否移出系統碟。
            // 只有在所有 Revit 註冊檔都能改寫時才搬移，否則照舊原地更新，確保外掛不會因此無法載入。
            PlanRelocation(info!);

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

                // 5.2 每次更新都修復 Revit 註冊。舊版曾以 DevelopmentTools.Addin.App
                // 註冊獨立的「BEAI Runtime」，即使原地更新 DLL，殘留 manifest 仍會讓 Revit
                // 在啟動時拋出 TypeLoadException。
                RepairAddinRegistrations(info.install_dir);

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

        private const string RootOverrideEnvironmentVariable = "DEVELOPMENTTOOLS_ROOT";
        private static readonly Regex AddinAssemblyPattern =
            new Regex(@"<Assembly>[^<]*DevelopmentTools\.Addin\.dll\s*</Assembly>", RegexOptions.IgnoreCase);

        internal static void PlanRelocation(PendingUpdateInfo info)
        {
            try
            {
                bool legacyPending = string.IsNullOrWhiteSpace(info.source_install_dir);
                if (legacyPending)
                {
                    info.source_install_dir = info.install_dir;
                    info.source_updater_dir = info.updater_dir;
                }

                string sourceInstall = info.source_install_dir!;
                if (legacyPending)
                {
                    if (!IsOnSystemDrive(sourceInstall)) return;
                    string? sourceBase = Directory.GetParent(sourceInstall.TrimEnd('\\', '/'))?.FullName;
                    string? root = ResolveTargetRoot(sourceBase);
                    if (root == null || (sourceBase != null && SamePath(root, sourceBase))) return;

                    info.install_dir = Path.Combine(root, "App");
                    info.updater_dir = Path.Combine(root, "Updater");
                    info.backup_dir = Path.Combine(root, "Update", "Backup");
                    info.version_file_path = Path.Combine(root, "App", "version.json");
                    info.extract_dir = Path.Combine(root, "Update", "Extract");
                    info.log_dir = Path.Combine(root, "Update", "Logs");
                    _logDir = info.log_dir;
                    if (sourceBase != null) CopyMissingFiles(Path.Combine(sourceBase, "Config"), Path.Combine(root, "Config"));
                }

                if (SamePath(sourceInstall, info.install_dir)) return;

                // 收集所有指向 DevelopmentTools.Addin.dll 的 Revit 註冊檔（使用者層級與 ProgramData）。
                var manifests = new List<string>(info.addin_manifest_paths ?? new List<string>());
                foreach (string found in FindAddinRegistrations())
                {
                    if (!manifests.Any(m => SamePath(m, found))) manifests.Add(found);
                }
                manifests = manifests.Where(File.Exists).ToList();

                string? blocked = manifests.FirstOrDefault(m => !CanRewrite(m));
                if (manifests.Count == 0 || blocked != null)
                {
                    LogError(blocked != null
                        ? "Relocation skipped: cannot write Revit registration " + blocked + ". Updating in place."
                        : "Relocation skipped: no Revit registration found. Updating in place.", null);
                    CancelRelocation(info);
                    return;
                }
                info.addin_manifest_paths = manifests;
            }
            catch (Exception ex)
            {
                LogError("Relocation planning failed. Updating in place.", ex);
                CancelRelocation(info);
            }
        }

        private static void CancelRelocation(PendingUpdateInfo info)
        {
            // 安裝位置維持原處；下載、解壓、備份與記錄仍可留在非系統碟。
            if (string.IsNullOrWhiteSpace(info.source_install_dir)) return;
            info.install_dir = info.source_install_dir!;
            info.updater_dir = info.source_updater_dir ?? info.updater_dir;
            info.version_file_path = Path.Combine(info.install_dir, "version.json");
            info.addin_manifest_paths = new List<string>();
        }

        private static string? ResolveTargetRoot(string? currentBase)
        {
            string? fromEnvironment = NormalizeRoot(Environment.GetEnvironmentVariable(RootOverrideEnvironmentVariable));
            if (fromEnvironment != null && IsWritableDirectory(fromEnvironment)) return fromEnvironment;

            if (currentBase != null)
            {
                string? fromSettings = NormalizeRoot(ReadInstallRootSetting(Path.Combine(currentBase, "Config", "appsettings.json")));
                if (fromSettings != null && IsWritableDirectory(fromSettings)) return fromSettings;
            }

            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch { return null; }
            foreach (var drive in drives.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    if (IsOnSystemDrive(drive.RootDirectory.FullName)) continue;
                    if (drive.AvailableFreeSpace <= 500L * 1024 * 1024) continue;
                    string candidate = Path.Combine(drive.RootDirectory.FullName, "DevelopmentTools");
                    if (IsWritableDirectory(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        private static string? ReadInstallRootSetting(string settingsPath)
        {
            try
            {
                if (!File.Exists(settingsPath)) return null;
                using (var doc = JsonDocument.Parse(File.ReadAllText(settingsPath)))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("InstallRoot", out var prop)
                        && prop.ValueKind == JsonValueKind.String)
                    {
                        return prop.GetString();
                    }
                }
            }
            catch { }
            return null;
        }

        private static string? NormalizeRoot(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(value!.Trim().Trim('"'));
                if (!Path.IsPathRooted(expanded)) return null;
                return Path.GetFullPath(expanded).TrimEnd('\\', '/');
            }
            catch { return null; }
        }

        internal static bool IsOnSystemDrive(string path)
        {
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(path));
                string? systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(systemRoot)) return false;
                return string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        private static bool IsWritableDirectory(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write_probe_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        private static IEnumerable<string> FindAddinRegistrations()
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "Revit", "Addins")
            };
            foreach (string root in roots)
            {
                string[] files;
                try
                {
                    if (!Directory.Exists(root)) continue;
                    files = Directory.GetFiles(root, "*.addin", SearchOption.AllDirectories);
                }
                catch { continue; }
                foreach (string file in files)
                {
                    bool match = false;
                    try { match = AddinAssemblyPattern.IsMatch(File.ReadAllText(file)); } catch { }
                    if (match) yield return file;
                }
            }
        }

        private static readonly Regex LegacyBeaiManifestPattern =
            new Regex(@"<FullClassName>\s*DevelopmentTools\.Addin\.App\s*</FullClassName>|<Name>\s*BEAI Runtime \(DevelopmentTools\)\s*</Name>", RegexOptions.IgnoreCase);

        private static void RepairAddinRegistrations(string installDir)
        {
            string userRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "Revit", "Addins");
            string commonRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Autodesk", "Revit", "Addins");

            foreach (string root in new[] { userRoot, commonRoot })
            {
                string[] manifests;
                try
                {
                    manifests = Directory.Exists(root)
                        ? Directory.GetFiles(root, "*.addin", SearchOption.AllDirectories)
                        : Array.Empty<string>();
                }
                catch (Exception ex)
                {
                    LogError("Warning: could not inspect Revit add-in registrations under " + root, ex);
                    continue;
                }

                foreach (string manifest in manifests)
                {
                    try
                    {
                        string xml = File.ReadAllText(manifest);
                        bool legacyBeai = LegacyBeaiManifestPattern.IsMatch(xml)
                            || string.Equals(Path.GetFileName(manifest), "BEAI.QueryHost.addin", StringComparison.OrdinalIgnoreCase);
                        bool duplicateCommonRegistration = SamePath(root, commonRoot)
                            && AddinAssemblyPattern.IsMatch(xml);
                        if (!legacyBeai && !duplicateCommonRegistration) continue;

                        File.Delete(manifest);
                        Console.WriteLine("Removed stale Revit add-in registration: " + manifest);
                    }
                    catch (Exception ex)
                    {
                        // ProgramData 通常需要系統管理員權限。不要因此回滾已完成的安全更新，
                        // 但必須留下可供 IT 排查的明確記錄。
                        LogError("Warning: could not remove stale Revit add-in registration: " + manifest, ex);
                    }
                }
            }

            foreach (string unsupportedVersion in new[] { "2025", "2026", "2027" })
            {
                TryDeleteManifest(Path.Combine(userRoot, unsupportedVersion, "DevelopmentTools.addin"));
            }

            string revit2024Dir = Path.Combine(userRoot, "2024");
            try
            {
                Directory.CreateDirectory(revit2024Dir);
                string canonicalPath = Path.Combine(revit2024Dir, "DevelopmentTools.addin");
                File.WriteAllText(
                    canonicalPath,
                    BuildCanonicalAddinManifest(Path.Combine(installDir, "DevelopmentTools.Addin.dll")),
                    new UTF8Encoding(false));
                Console.WriteLine("Repaired Revit 2024 add-in registration: " + canonicalPath);
            }
            catch (Exception ex)
            {
                LogError("Warning: could not repair the Revit 2024 add-in registration. Re-run DevelopmentTools_Setup.exe.", ex);
            }
        }

        private static void TryDeleteManifest(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                LogError("Warning: could not remove unsupported Revit registration: " + path, ex);
            }
        }

        internal static string BuildCanonicalAddinManifest(string assemblyPath)
        {
            string escaped = System.Security.SecurityElement.Escape(assemblyPath) ?? assemblyPath;
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                + "<RevitAddIns>\r\n"
                + "  <AddIn Type=\"Application\">\r\n"
                + "    <Name>Development Tools</Name>\r\n"
                + "    <Assembly>" + escaped + "</Assembly>\r\n"
                + "    <FullClassName>DevelopmentTools.App</FullClassName>\r\n"
                + "    <ClientId>c2d5d85c-4d33-4f9e-a89e-21ef1ea3b361</ClientId>\r\n"
                + "    <VendorId>RoomTileSystem</VendorId>\r\n"
                + "    <VendorDescription>Room Tile System v3</VendorDescription>\r\n"
                + "  </AddIn>\r\n"
                + "</RevitAddIns>\r\n";
        }

        private static bool CanRewrite(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
                return true;
            }
            catch { return false; }
        }

        private static void CopyMissingFiles(string sourceDir, string targetDir)
        {
            try
            {
                if (!Directory.Exists(sourceDir)) return;
                Directory.CreateDirectory(targetDir);
                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    string destination = Path.Combine(targetDir, Path.GetFileName(file));
                    if (!File.Exists(destination)) File.Copy(file, destination, false);
                }
            }
            catch { }
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
            var pattern = AddinAssemblyPattern;
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
