using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BEAI.QueryHost.Core
{
    public class HotReloadManager : IDisposable
    {
        private static readonly Lazy<HotReloadManager> _lazy = new Lazy<HotReloadManager>(delegate { return new HotReloadManager(); });
        public static HotReloadManager Instance { get { return _lazy.Value; } }

        private FileSystemWatcher _watcher;
        private System.Threading.Timer _debounceTimer;
        private readonly object _lock = new object();
        private string _rulesDirectory;

        private ConcurrentDictionary<string, JToken> _activeRules = new ConcurrentDictionary<string, JToken>();
        private ConcurrentDictionary<string, JToken> _lastKnownGoodRules = new ConcurrentDictionary<string, JToken>();

        public int CurrentGeneration { get { return RuntimeState.Instance.RuleGeneration; } }

        public void Initialize(string baseDirectory)
        {
            _rulesDirectory = Path.Combine(baseDirectory, "Runtime", "Rules");
            if (!Directory.Exists(_rulesDirectory))
            {
                Directory.CreateDirectory(_rulesDirectory);
                CreateDefaultRuleFiles(_rulesDirectory);
            }

            ReloadAllRules(true);
            StartWatcher();
        }

        private void StartWatcher()
        {
            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                }

                _watcher = new FileSystemWatcher(_rulesDirectory, "*.json");
                _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                _watcher.IncludeSubdirectories = false;
                _watcher.EnableRaisingEvents = RuntimeState.Instance.DevelopmentMode;

                _watcher.Changed += OnRulesFolderChanged;
                _watcher.Created += OnRulesFolderChanged;
                _watcher.Renamed += OnRulesFolderChanged;
                _watcher.Deleted += OnRulesFolderChanged;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to start Rules FileSystemWatcher", ex);
            }
        }

        public void SetWatcherEnabled(bool enabled)
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = enabled;
            }
        }

        private void OnRulesFolderChanged(object sender, FileSystemEventArgs e)
        {
            if (!RuntimeState.Instance.DevelopmentMode) return;

            lock (_lock)
            {
                if (_debounceTimer != null)
                {
                    _debounceTimer.Dispose();
                }
                _debounceTimer = new System.Threading.Timer(delegate
                {
                    ReloadAllRules(false);
                }, null, 500, Timeout.Infinite);
            }
        }

        public bool ReloadAllRules(bool isInitial)
        {
            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(_rulesDirectory))
                    {
                        Directory.CreateDirectory(_rulesDirectory);
                    }

                    var newRules = new ConcurrentDictionary<string, JToken>();
                    string[] ruleFiles = Directory.GetFiles(_rulesDirectory, "*.json");

                    foreach (var file in ruleFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        string content = File.ReadAllText(file, Encoding.UTF8);

                        if (string.IsNullOrWhiteSpace(content))
                        {
                            throw new InvalidOperationException(string.Format("Rule file '{0}' is empty.", fileName));
                        }

                        var token = JToken.Parse(content);
                        newRules[fileName] = token;
                    }

                    _lastKnownGoodRules = _activeRules;
                    _activeRules = newRules;

                    RuntimeState.Instance.RuleGeneration++;
                    RuntimeState.Instance.SetLastReload();
                    RuntimeState.Instance.ClearLastError();

                    Logger.Info(string.Format("[HotReloadManager] Successfully loaded {0} rule files. Rule Generation: {1}", newRules.Count, RuntimeState.Instance.RuleGeneration));
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Error(string.Format("[HotReloadManager] Rule reload failed: {0}. Preserving last known good rules.", ex.Message), ex);
                    RuntimeState.Instance.SetLastError(string.Format("RULE_RELOAD_FAILED: {0}", ex.Message));
                    return false;
                }
            }
        }

        public JToken GetRule(string ruleFileName)
        {
            JToken rule;
            if (_activeRules.TryGetValue(ruleFileName, out rule))
            {
                return rule;
            }
            return null;
        }

        public string GetAllRulesJson()
        {
            var dict = new Dictionary<string, object>();
            foreach (var kvp in _activeRules)
            {
                dict[kvp.Key] = kvp.Value;
            }
            return JsonConvert.SerializeObject(dict);
        }

        private void CreateDefaultRuleFiles(string dir)
        {
            string doorsJson = "{\n  \"category\": \"Doors\",\n  \"requiredParameters\": [\n    \"Mark\",\n    \"Fire Rating\",\n    \"Comments\"\n  ],\n  \"rules\": [\n    {\n      \"parameter\": \"Fire Rating\",\n      \"required\": true,\n      \"description\": \"Doors must have fire rating specified\"\n    }\n  ]\n}";
            File.WriteAllText(Path.Combine(dir, "Doors.json"), doorsJson, Encoding.UTF8);

            string wallsJson = "{\n  \"category\": \"Walls\",\n  \"requiredParameters\": [\n    \"Type Name\",\n    \"Unconnected Height\"\n  ],\n  \"rules\": [\n    {\n      \"parameter\": \"Unconnected Height\",\n      \"minValue\": 1.0,\n      \"description\": \"Wall unconnected height must be greater than 1 meter\"\n    }\n  ]\n}";
            File.WriteAllText(Path.Combine(dir, "Walls.json"), wallsJson, Encoding.UTF8);

            string parametersJson = "{\n  \"standardParameters\": [\n    \"Mark\",\n    \"Comments\",\n    \"Fire Rating\",\n    \"Type Mark\",\n    \"Model\"\n  ]\n}";
            File.WriteAllText(Path.Combine(dir, "Parameters.json"), parametersJson, Encoding.UTF8);

            string generalRulesJson = "{\n  \"version\": \"1.0\",\n  \"strictLevelValidation\": true,\n  \"allowUnpinnedElements\": true\n}";
            File.WriteAllText(Path.Combine(dir, "GeneralRules.json"), generalRulesJson, Encoding.UTF8);
        }

        public void Dispose()
        {
            if (_debounceTimer != null)
            {
                _debounceTimer.Dispose();
            }
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
        }
    }
}
