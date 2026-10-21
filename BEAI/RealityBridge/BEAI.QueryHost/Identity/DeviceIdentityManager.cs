using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BEAI.QueryHost.Identity
{
    public class DeviceIdentityManager
    {
        private static readonly Lazy<DeviceIdentityManager> _lazy = new Lazy<DeviceIdentityManager>(delegate { return new DeviceIdentityManager(); });
        public static DeviceIdentityManager Instance { get { return _lazy.Value; } }

        private readonly string _configDirectory;
        private readonly string _configFilePath;
        private string _deviceId;
        private string _deviceName;

        public DeviceIdentityManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            // 裝置識別移到非系統碟資料目錄；第一次會從舊 %APPDATA%\BEAI\device.json 複製，識別碼不變。
            _configFilePath = BEAI.QueryHost.Core.DtStorage.File(@"BEAI\device.json", Path.Combine(appData, "BEAI", "device.json"));
            _configDirectory = Path.GetDirectoryName(_configFilePath);
            _deviceName = Environment.MachineName;

            InitializeDeviceIdentity();
        }

        public string DeviceId { get { return _deviceId; } }
        public string DeviceName { get { return _deviceName; } }

        private void InitializeDeviceIdentity()
        {
            try
            {
                if (!Directory.Exists(_configDirectory))
                {
                    Directory.CreateDirectory(_configDirectory);
                }

                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath, Encoding.UTF8);
                    var data = JsonConvert.DeserializeObject<DeviceConfigData>(json);
                    if (data != null && !string.IsNullOrEmpty(data.DeviceId))
                    {
                        _deviceId = data.DeviceId;
                        return;
                    }
                }

                // Generate new DeviceId (e.g., 01JBEAI + 16 random hex chars)
                byte[] idBytes = new byte[8];
                using (var rng = new RNGCryptoServiceProvider())
                {
                    rng.GetBytes(idBytes);
                }
                _deviceId = string.Format("01JBEAI{0}", BitConverter.ToString(idBytes).Replace("-", "").ToUpperInvariant());

                // Generate 256-bit random DeviceSecret
                byte[] secretBytes = new byte[32];
                using (var rng = new RNGCryptoServiceProvider())
                {
                    rng.GetBytes(secretBytes);
                }

                // Encrypt using Windows DPAPI (ProtectedData)
                byte[] encryptedSecret = ProtectedData.Protect(secretBytes, null, DataProtectionScope.CurrentUser);
                string encryptedSecretBase64 = Convert.ToBase64String(encryptedSecret);

                var newConfig = new DeviceConfigData();
                newConfig.DeviceId = _deviceId;
                newConfig.DeviceName = _deviceName;
                newConfig.EncryptedSecret = encryptedSecretBase64;
                newConfig.CreatedAt = DateTime.UtcNow;

                File.WriteAllText(_configFilePath, JsonConvert.SerializeObject(newConfig, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Fallback in memory
                if (string.IsNullOrEmpty(_deviceId))
                {
                    _deviceId = string.Format("01JBEAI{0}", Guid.NewGuid().ToString("N").Substring(0, 16).ToUpperInvariant());
                }
            }
        }

        public byte[] GetDecryptedDeviceSecret()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath, Encoding.UTF8);
                    var data = JsonConvert.DeserializeObject<DeviceConfigData>(json);
                    if (data != null && !string.IsNullOrEmpty(data.EncryptedSecret))
                    {
                        byte[] encrypted = Convert.FromBase64String(data.EncryptedSecret);
                        return ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    }
                }
            }
            catch { }
            return new byte[32];
        }

        private class DeviceConfigData
        {
            [JsonProperty("deviceId")]
            public string DeviceId { get; set; }

            [JsonProperty("deviceName")]
            public string DeviceName { get; set; }

            [JsonProperty("encryptedSecret")]
            public string EncryptedSecret { get; set; }

            [JsonProperty("createdAt")]
            public DateTime CreatedAt { get; set; }
        }
    }
}
