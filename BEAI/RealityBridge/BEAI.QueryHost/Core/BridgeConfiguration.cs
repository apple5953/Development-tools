using System;
using System.IO;
using Newtonsoft.Json;

namespace BEAI.QueryHost.Core
{
    public class BridgeConfiguration
    {
        private static readonly Lazy<BridgeConfiguration> LazyInstance =
            new Lazy<BridgeConfiguration>(delegate { return new BridgeConfiguration(); });
        private readonly object _syncRoot = new object();
        private DateTime _lastLoadUtc = DateTime.MinValue;
        private FeatureFlags _flags = FeatureFlags.CreateDefault();

        public static BridgeConfiguration Instance { get { return LazyInstance.Value; } }

        public FeatureFlags GetFlags()
        {
            lock (_syncRoot)
            {
                if ((DateTime.UtcNow - _lastLoadUtc).TotalSeconds >= 2) Reload();
                return _flags;
            }
        }

        private void Reload()
        {
            _lastLoadUtc = DateTime.UtcNow;
            try
            {
                string path = Path.Combine(RuntimeState.Instance.BaseDirectory, "Config", "feature-flags.json");
                if (!File.Exists(path))
                {
                    _flags = FeatureFlags.CreateDefault();
                    return;
                }
                FeatureFlags loaded = JsonConvert.DeserializeObject<FeatureFlags>(File.ReadAllText(path));
                _flags = loaded ?? FeatureFlags.CreateDefault();
            }
            catch (Exception ex)
            {
                Logger.Warn(string.Format("[BridgeConfiguration] Using safe defaults: {0}", ex.Message));
                _flags = FeatureFlags.CreateDefault();
            }
        }
    }

    public class FeatureFlags
    {
        [JsonProperty("externalAccess")]
        public bool ExternalAccess { get; set; }
        [JsonProperty("system")]
        public bool System { get; set; }
        [JsonProperty("read")]
        public bool Read { get; set; }
        [JsonProperty("analysis")]
        public bool Analysis { get; set; }
        [JsonProperty("navigation")]
        public bool Navigation { get; set; }
        [JsonProperty("actions")]
        public bool Actions { get; set; }
        [JsonProperty("clash")]
        public bool Clash { get; set; }
        [JsonProperty("remoteTransport")]
        public bool RemoteTransport { get; set; }

        public static FeatureFlags CreateDefault()
        {
            return new FeatureFlags { ExternalAccess = true, System = true, Read = true };
        }
    }
}
