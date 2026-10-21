using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Core
{
    public class UpdateManager
    {
        // 目前執行中的安裝位置（舊版固定在 %LOCALAPPDATA%\DevelopmentTools）。
        private static string BaseDir { get { return InstallLocation.CurrentBaseDir; } }
        private static string LocalVersionPath { get { return Path.Combine(BaseDir, "App", "version.json"); } }
        private static string AppSettingsPath { get { return Path.Combine(BaseDir, "Config", "appsettings.json"); } }

        public class UpdateCheckResult
        {
            public bool HasUpdate { get; set; }
            public string CurrentVersion { get; set; }
            public string LatestVersion { get; set; }
            public string ReleaseNotes { get; set; }
            public UpdateManifest Manifest { get; set; }
        }

        public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            var result = new UpdateCheckResult { HasUpdate = false };
            try
            {
                if (!File.Exists(LocalVersionPath))
                {
                    UpdateLogger.Log("找不到本機 version.json 檔案。");
                    return result;
                }

                string localJson = File.ReadAllText(LocalVersionPath);
                var localVersion = JsonSerializer.Deserialize<LocalVersionInfo>(localJson);
                result.CurrentVersion = localVersion.current_version;

                string manifestUrl = localVersion.manifest_url;
                
                if (File.Exists(AppSettingsPath))
                {
                    try
                    {
                        using (var doc = JsonDocument.Parse(File.ReadAllText(AppSettingsPath)))
                        {
                            if (doc.RootElement.TryGetProperty("ManifestUrl", out var prop))
                            {
                                string customUrl = prop.GetString();
                                if (!string.IsNullOrEmpty(customUrl))
                                {
                                    manifestUrl = customUrl;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateLogger.Log("讀取 appsettings.json 自訂 ManifestUrl 失敗", ex);
                    }
                }

                string remoteJson = await DownloadManager.DownloadJsonAsync(manifestUrl).ConfigureAwait(false);
                if (string.IsNullOrEmpty(remoteJson))
                {
                    return result;
                }

                var remoteManifest = JsonSerializer.Deserialize<UpdateManifest>(remoteJson);
                result.LatestVersion = remoteManifest.latest_version;
                result.ReleaseNotes = remoteManifest.release_note;
                result.Manifest = remoteManifest;

                if (IsNewerVersion(localVersion.current_version, remoteManifest.latest_version))
                {
                    result.HasUpdate = true;
                }
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("檢查更新流程異常", ex);
            }
            return result;
        }

        private static bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            try
            {
                var cur = new Version(currentVersion);
                var lat = new Version(latestVersion);
                return lat > cur;
            }
            catch
            {
                return string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
            }
        }

        public static async Task<bool> StartUpdateProcessAsync(UpdateManifest manifest)
        {
            try
            {
                // 下載、解壓、Updater 與安裝全部放在非系統碟，避免公司防毒攔截 C: 使用者目錄中的下載檔。
                string targetBaseDir = InstallLocation.ResolvePreferredBaseDir();
                string updateDir = Path.Combine(targetBaseDir, "Update");
                string stagingDir = Path.Combine(updateDir, "Staging");
                string backupDir = Path.Combine(updateDir, "Backup");
                string updaterStagingDir = Path.Combine(updateDir, "UpdaterStaging");
                string pendingUpdatePath = Path.Combine(updateDir, "pending_update.json");
                string targetAppDir = Path.Combine(targetBaseDir, "App");
                string targetUpdaterDir = Path.Combine(targetBaseDir, "Updater");
                string sourceAppDir = Path.Combine(BaseDir, "App");
                string sourceUpdaterDir = Path.Combine(BaseDir, "Updater");
                bool relocating = !InstallLocation.SamePath(targetBaseDir, BaseDir);

                UpdateLogger.Log($"更新目標位置: {targetBaseDir}（目前安裝位置: {BaseDir}）");
                if (relocating)
                {
                    UpdateLogger.Log("本次更新會將安裝位置移出系統碟，並改寫 Revit .addin 註冊路徑。");
                    CopyConfigIfMissing(Path.Combine(BaseDir, "Config"), Path.Combine(targetBaseDir, "Config"));
                }

                Directory.CreateDirectory(stagingDir);
                string zipName = Path.GetFileName(new Uri(manifest.release_url).LocalPath);
                string destinationZip = Path.Combine(stagingDir, zipName);

                UpdateLogger.Log($"開始下載新版壓縮包: {manifest.release_url}");
                bool downloadSuccess = await DownloadManager.DownloadFileAsync(manifest.release_url, destinationZip).ConfigureAwait(false);
                if (!downloadSuccess)
                {
                    App.ShowDialogOnUi("更新提示", "下載新版更新檔案失敗。詳細資料請見 Logs。");
                    return false;
                }

                UpdateLogger.Log("驗證新版壓縮包的 SHA256 哈希值...");
                if (!DownloadManager.VerifySha256(destinationZip, manifest.sha256))
                {
                    UpdateLogger.Log($"SHA256 驗證失敗。檔案: {destinationZip}");
                    App.ShowDialogOnUi("更新提示", "下載的檔案損壞（SHA256 校驗失敗），已取消更新。");
                    return false;
                }

                var pendingInfo = new PendingUpdateInfo
                {
                    zip_path = destinationZip,
                    sha256 = manifest.sha256,
                    install_dir = targetAppDir,
                    updater_dir = targetUpdaterDir,
                    backup_dir = backupDir,
                    version_file_path = Path.Combine(targetAppDir, "version.json"),
                    new_version = manifest.latest_version,
                    updated_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    revit_process_id = Process.GetCurrentProcess().Id,
                    source_install_dir = sourceAppDir,
                    source_updater_dir = sourceUpdaterDir,
                    extract_dir = Path.Combine(updateDir, "Extract"),
                    log_dir = Path.Combine(updateDir, "Logs"),
                    addin_manifest_paths = relocating ? InstallLocation.FindAddinManifests() : new System.Collections.Generic.List<string>()
                };

                string pendingJson = JsonSerializer.Serialize(pendingInfo, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(pendingUpdatePath, pendingJson);

                // Launch the updater shipped inside the already SHA-256 verified
                // package. This repairs missing updater dependencies and permits
                // the updater itself to be replaced after Revit exits.
                string updaterExe = ExtractPackagedUpdater(destinationZip, updaterStagingDir);
                if (string.IsNullOrEmpty(updaterExe))
                {
                    updaterExe = Path.Combine(sourceUpdaterDir, "DevelopmentTools.Updater.exe");
                    if (!File.Exists(updaterExe))
                    {
                        string localJson = File.ReadAllText(LocalVersionPath);
                        var localVersion = JsonSerializer.Deserialize<LocalVersionInfo>(localJson);
                        updaterExe = Environment.ExpandEnvironmentVariables(localVersion.updater_path);
                    }
                    UpdateLogger.Log("更新包不含 UpdaterPayload，改用已安裝的相容更新器。");
                }

                if (!File.Exists(updaterExe))
                {
                    App.ShowDialogOnUi("更新提示", $"找不到更新主程式：{updaterExe}");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = updaterExe,
                    Arguments = $"--pending \"{pendingUpdatePath}\"",
                    WorkingDirectory = Path.GetDirectoryName(updaterExe),
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                Process.Start(startInfo);
                App.ShowDialogOnUi("更新提示", "更新程式已啟動。請關閉 Revit 以完成更新安裝。");
                return true;
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("啟動更新程序失敗", ex);
                App.ShowDialogOnUi("更新錯誤", "啟動更新流程時發生錯誤: " + ex.Message);
                return false;
            }
        }

        private static void CopyConfigIfMissing(string sourceDir, string targetDir)
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
            catch (Exception ex)
            {
                UpdateLogger.Log("複製 Config 至新安裝位置失敗（將使用預設設定）", ex);
            }
        }

        private static string ExtractPackagedUpdater(string zipPath, string updaterStagingDir)
        {
            if (Directory.Exists(updaterStagingDir))
            {
                Directory.Delete(updaterStagingDir, true);
            }
            Directory.CreateDirectory(updaterStagingDir);

            string stagingRoot = Path.GetFullPath(updaterStagingDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            bool foundPayload = false;

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    const string prefix = "UpdaterPayload/";
                    string normalized = entry.FullName.Replace('\\', '/');
                    if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                    foundPayload = true;
                    string relativePath = normalized.Substring(prefix.Length);
                    if (string.IsNullOrEmpty(relativePath)) continue;

                    string destinationPath = Path.GetFullPath(Path.Combine(updaterStagingDir, relativePath));
                    if (!destinationPath.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("UpdaterPayload contains an unsafe path.");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        entry.ExtractToFile(destinationPath, true);
                    }
                }
            }

            if (!foundPayload) return null;

            string updaterExe = Path.Combine(updaterStagingDir, "DevelopmentTools.Updater.exe");
            if (!File.Exists(updaterExe))
            {
                throw new FileNotFoundException("更新包的 UpdaterPayload 缺少 DevelopmentTools.Updater.exe。", updaterExe);
            }
            return updaterExe;
        }
    }
}
