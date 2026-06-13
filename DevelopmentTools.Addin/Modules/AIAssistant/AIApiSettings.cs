using System;
using System.IO;
using System.Text.Json;

namespace DevelopmentTools.Modules.AIAssistant
{
    public class AIApiSettings
    {
        public string ApiKey { get; set; } = "";
        public string ApiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string ModelName { get; set; } = "gpt-4o";
        public bool IsApiReady { get; set; } = false;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevelopmentTools", "App", "ai_settings.json"
        );

        public static AIApiSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AIApiSettings>(json);
                    if (settings != null)
                    {
                        // 嘗試做簡單的 Base64 解碼保護 API Key
                        if (!string.IsNullOrEmpty(settings.ApiKey))
                        {
                            try
                            {
                                byte[] data = Convert.FromBase64String(settings.ApiKey);
                                settings.ApiKey = System.Text.Encoding.UTF8.GetString(data);
                            }
                            catch { }
                        }
                        return settings;
                    }
                }
            }
            catch { }
            return new AIApiSettings();
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 備份並做 Base64 編碼保護
                string tempKey = this.ApiKey;
                if (!string.IsNullOrEmpty(this.ApiKey))
                {
                    byte[] data = System.Text.Encoding.UTF8.GetBytes(this.ApiKey);
                    this.ApiKey = Convert.ToBase64String(data);
                }

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);

                // 還原記憶體中的 key
                this.ApiKey = tempKey;
            }
            catch { }
        }
    }
}
