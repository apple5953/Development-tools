using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Core
{
    public class UpdateManager
    {
        private static readonly string BaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevelopmentTools");
        private static readonly string LocalVersionPath = Path.Combine(BaseDir, "App", "version.json");
        private static readonly string StagingDir = Path.Combine(BaseDir, "Update", "Staging");
        private static readonly string BackupDir = Path.Combine(BaseDir, "Update", "Backup");
        private static readonly string PendingUpdatePath = Path.Combine(BaseDir, "Update", "pending_update.json");
        private static readonly string AppSettingsPath = Path.Combine(BaseDir, "Config", "appsettings.json");

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
                string zipName = Path.GetFileName(new Uri(manifest.release_url).LocalPath);
                string destinationZip = Path.Combine(StagingDir, zipName);

                UpdateLogger.Log($"開始下載新版壓縮包: {manifest.release_url}");
                bool downloadSuccess = await DownloadManager.DownloadFileAsync(manifest.release_url, destinationZip).ConfigureAwait(false);
                if (!downloadSuccess)
                {
                    _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TaskDialog.Show("更新提示", "下載新版更新檔案失敗。詳細資料請見 Logs。");
                    }));
                    return false;
                }

                UpdateLogger.Log("驗證新版壓縮包的 SHA256 哈希值...");
                if (!DownloadManager.VerifySha256(destinationZip, manifest.sha256))
                {
                    UpdateLogger.Log($"SHA256 驗證失敗。檔案: {destinationZip}");
                    _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TaskDialog.Show("更新提示", "下載的檔案損壞（SHA256 校驗失敗），已取消更新。");
                    }));
                    return false;
                }

                var pendingInfo = new PendingUpdateInfo
                {
                    zip_path = destinationZip,
                    sha256 = manifest.sha256,
                    install_dir = Path.Combine(BaseDir, "App"),
                    backup_dir = BackupDir,
                    version_file_path = LocalVersionPath,
                    new_version = manifest.latest_version,
                    updated_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
                };

                string pendingJson = JsonSerializer.Serialize(pendingInfo, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PendingUpdatePath, pendingJson);

                string localJson = File.ReadAllText(LocalVersionPath);
                var localVersion = JsonSerializer.Deserialize<LocalVersionInfo>(localJson);
                string updaterExe = Environment.ExpandEnvironmentVariables(localVersion.updater_path);

                if (!File.Exists(updaterExe))
                {
                    _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        TaskDialog.Show("更新提示", $"找不到更新主程式：{updaterExe}");
                    }));
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = updaterExe,
                    Arguments = $"--pending \"{PendingUpdatePath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                Process.Start(startInfo);
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    TaskDialog.Show("更新提示", "更新程式已啟動。請關閉 Revit 以完成更新安裝。");
                }));
                return true;
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("啟動更新程序失敗", ex);
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    TaskDialog.Show("更新錯誤", "啟動更新流程時發生錯誤: " + ex.Message);
                }));
                return false;
            }
        }
    }
}
