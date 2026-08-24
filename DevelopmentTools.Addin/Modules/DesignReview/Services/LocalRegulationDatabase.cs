using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using DevelopmentTools.Core;

namespace DevelopmentTools.Modules.DesignReview.Services
{
    public static class LocalRegulationDatabase
    {
        private static Dictionary<string, double> _regulations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static readonly string ConfigFileName = "RegulationsConfig.json";
        private static readonly string GitHubUrl = "https://raw.githubusercontent.com/h30190/HJPLUS_Taiwan_Architect_KB/main/RegulationsConfig.json";
        private static readonly object _lock = new object();

        static LocalRegulationDatabase()
        {
            LoadLocalConfig();
        }

        private static string GetConfigPath()
        {
            string assemblyFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string resourcesDir = Path.Combine(assemblyFolder, "Resources");
            if (!Directory.Exists(resourcesDir))
            {
                Directory.CreateDirectory(resourcesDir);
            }
            return Path.Combine(resourcesDir, ConfigFileName);
        }

        public static void LoadLocalConfig()
        {
            lock (_lock)
            {
                string path = GetConfigPath();
                if (!File.Exists(path))
                {
                    // Create default config file if it does not exist (Fallback values)
                    var defaults = new Dictionary<string, double>
                    {
                        { "BCR-CorridorWidth-Single", 1200.0 },
                        { "BCR-CorridorWidth-Double", 1600.0 },
                        { "BCR-CorridorWidth-Public-Single", 1800.0 },
                        { "BCR-CorridorWidth-Public-Double", 2400.0 },
                        { "BCR-StairWidth-Default", 1200.0 },
                        { "BCR-StairWidth-Public", 1400.0 },
                        { "BCR-StairWidth-Other", 750.0 },
                        { "BCR-StairRiser-Default", 180.0 },
                        { "BCR-StairRiser-Public", 160.0 },
                        { "BCR-StairRiser-Other", 200.0 },
                        { "BCR-StairTread-Default", 240.0 },
                        { "BCR-StairTread-Public", 260.0 },
                        { "BCR-StairTread-Other", 220.0 },
                        { "BCR-DoorWidth-Escape", 900.0 },
                        { "BCR-DoorWidth-Egress", 1200.0 },
                        { "BCR-DoorWidth-Accessible", 900.0 },
                        { "BCR-DoorThreshold-Accessible", 20.0 },
                        { "BCR-RampSlope-Max", 0.0833 },
                        { "BCR-RampWidth-Min", 1200.0 },
                        { "BCR-AccessibleToiletWidth", 1500.0 },
                        { "BCR-AccessibleToiletLength", 1500.0 },
                        { "BCR-AccessibleToiletArea", 2.25 },
                        { "BCR-AccessiblePassageWidth", 1300.0 }
                    };
                    try
                    {
                        string json = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(path, json);
                    }
                    catch (Exception ex)
                    {
                        UpdateLogger.Log("寫入預設法規配置失敗", ex);
                    }
                }

                if (File.Exists(path))
                {
                    try
                    {
                        string json = File.ReadAllText(path);
                        var data = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
                        if (data != null)
                        {
                            _regulations = new Dictionary<string, double>(data, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                    catch (Exception ex)
                    {
                        UpdateLogger.Log("讀取本地法規配置失敗", ex);
                    }
                }
            }
        }

        public static double GetLimit(string key, double fallbackValue)
        {
            lock (_lock)
            {
                if (_regulations.TryGetValue(key, out double val))
                {
                    return val;
                }
                return fallbackValue;
            }
        }

        public static async Task SyncWithGitHubAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    // Add User-Agent header which is required by GitHub API/Raw services
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RevitDesignReviewBot/1.0)");
                    
                    var response = await client.GetAsync(GitHubUrl).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var remoteData = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
                        if (remoteData != null && remoteData.Count > 0)
                        {
                            lock (_lock)
                            {
                                foreach (var kvp in remoteData)
                                {
                                    _regulations[kvp.Key] = kvp.Value;
                                }
                                // Save back to local file
                                string path = GetConfigPath();
                                string savedJson = JsonSerializer.Serialize(_regulations, new JsonSerializerOptions { WriteIndented = true });
                                File.WriteAllText(path, savedJson);
                            }
                            UpdateLogger.Log("與 GitHub 台灣建築師法規庫同步成功。");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateLogger.Log("與 GitHub 台灣建築師法規庫同步失敗，將繼續使用本地緩存法規值。", ex);
            }
        }
    }
}
