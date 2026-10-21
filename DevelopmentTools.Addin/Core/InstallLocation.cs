using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace DevelopmentTools.Core
{
    /// <summary>
    /// 決定 Development Tools 的安裝根目錄。
    ///
    /// 公司防毒會攔截寫到系統碟（C:）使用者設定檔下（%LOCALAPPDATA%、%TEMP%）的下載檔與執行檔，
    /// 因此更新包的下載、解壓、Updater 與安裝目錄一律優先放到非系統碟，例如 D:\DevelopmentTools。
    ///
    /// 解析順序：
    ///   1. 環境變數 DEVELOPMENTTOOLS_ROOT（IT 可統一指定）
    ///   2. Config\appsettings.json 的 "InstallRoot"
    ///   3. 目前已安裝在非系統碟 → 沿用目前位置
    ///   4. 第一個可寫入的本機固定非系統碟 → X:\DevelopmentTools
    ///   5. 都不可用時才退回目前位置（通常是 %LOCALAPPDATA%\DevelopmentTools）
    /// </summary>
    public static class InstallLocation
    {
        public const string FolderName = "DevelopmentTools";
        public const string RootOverrideEnvironmentVariable = "DEVELOPMENTTOOLS_ROOT";

        public static string LegacyBaseDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName); }
        }

        /// <summary>目前正在執行的外掛所屬的根目錄（App 資料夾的上一層）。</summary>
        public static string CurrentBaseDir
        {
            get
            {
                string appDir = CurrentAppDir;
                if (!string.IsNullOrEmpty(appDir)
                    && string.Equals(Path.GetFileName(appDir.TrimEnd('\\', '/')), "App", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Directory.GetParent(appDir.TrimEnd('\\', '/'));
                    if (parent != null) return parent.FullName;
                }
                return LegacyBaseDir;
            }
        }

        /// <summary>目前正在執行的 DevelopmentTools.Addin.dll 所在資料夾。</summary>
        public static string CurrentAppDir
        {
            get
            {
                string location = null;
                try { location = Assembly.GetExecutingAssembly().Location; } catch { }
                if (string.IsNullOrEmpty(location))
                {
                    try { location = new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath; } catch { }
                }
                if (string.IsNullOrEmpty(location)) return Path.Combine(LegacyBaseDir, "App");
                return Path.GetDirectoryName(location);
            }
        }

        /// <summary>更新／安裝要使用的目標根目錄（優先非系統碟）。</summary>
        public static string ResolvePreferredBaseDir()
        {
            string current = CurrentBaseDir;

            string fromEnvironment = NormalizeRoot(Environment.GetEnvironmentVariable(RootOverrideEnvironmentVariable));
            if (!string.IsNullOrEmpty(fromEnvironment) && IsWritableDirectory(fromEnvironment))
            {
                return fromEnvironment;
            }

            string fromSettings = NormalizeRoot(ReadInstallRootSetting(current));
            if (!string.IsNullOrEmpty(fromSettings) && IsWritableDirectory(fromSettings))
            {
                return fromSettings;
            }

            if (!IsOnSystemDrive(current) && IsWritableDirectory(current))
            {
                return current;
            }

            foreach (string drive in CandidateDriveRoots())
            {
                string candidate = Path.Combine(drive, FolderName);
                if (IsWritableDirectory(candidate))
                {
                    return candidate;
                }
            }

            UpdateLogger.Log("找不到可寫入的非系統碟，更新仍使用目前安裝位置：" + current);
            return current;
        }

        public static bool IsOnSystemDrive(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                string systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
                return string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd('\\', '/'),
                    Path.GetFullPath(b).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>目前註冊此外掛的 Revit .addin 檔案（使用者層級）。</summary>
        public static List<string> FindAddinManifests()
        {
            var results = new List<string>();
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            foreach (string revitVersion in new[] { "2024", "2025", "2026", "2027" })
            {
                string path = Path.Combine(roaming, "Autodesk", "Revit", "Addins", revitVersion, "DevelopmentTools.addin");
                if (File.Exists(path)) results.Add(path);
            }
            return results;
        }

        private static IEnumerable<string> CandidateDriveRoots()
        {
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch { yield break; }

            foreach (var drive in drives.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                bool usable;
                try
                {
                    usable = drive.DriveType == DriveType.Fixed
                        && drive.IsReady
                        && !IsOnSystemDrive(drive.RootDirectory.FullName)
                        && drive.AvailableFreeSpace > 500L * 1024 * 1024;
                }
                catch
                {
                    usable = false;
                }
                if (usable) yield return drive.RootDirectory.FullName;
            }
        }

        private static string ReadInstallRootSetting(string baseDir)
        {
            try
            {
                string settingsPath = Path.Combine(baseDir, "Config", "appsettings.json");
                if (!File.Exists(settingsPath)) return null;
                using (var doc = JsonDocument.Parse(File.ReadAllText(settingsPath)))
                {
                    JsonElement prop;
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("InstallRoot", out prop)
                        && prop.ValueKind == JsonValueKind.String)
                    {
                        return prop.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("讀取 appsettings.json 的 InstallRoot 失敗", ex);
            }
            return null;
        }

        private static string NormalizeRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                if (!Path.IsPathRooted(expanded)) return null;
                return Path.GetFullPath(expanded).TrimEnd('\\', '/');
            }
            catch
            {
                return null;
            }
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
            catch
            {
                return false;
            }
        }
    }
}
