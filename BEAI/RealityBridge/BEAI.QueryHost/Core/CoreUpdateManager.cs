using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Xml;

namespace BEAI.QueryHost.Core
{
    public sealed class CoreUpdateManager : IDisposable
    {
        private static readonly Lazy<CoreUpdateManager> _lazy = new Lazy<CoreUpdateManager>(delegate { return new CoreUpdateManager(); });
        public static CoreUpdateManager Instance { get { return _lazy.Value; } }

        private readonly object _sync = new object();
        private FileSystemWatcher _watcher;
        private Timer _debounceTimer;
        private string _currentAssemblyPath;
        private string _manifestPath;

        public void Initialize(string currentAssemblyPath)
        {
            _currentAssemblyPath = Path.GetFullPath(currentAssemblyPath);
            _manifestPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "Revit", "Addins", "2024", "BEAI.QueryHost.addin");

            RuntimeState.Instance.CurrentCorePath = _currentAssemblyPath;
            RuntimeState.Instance.CoreBuild = GetBuildId(_currentAssemblyPath);
            RefreshStatus();
            StartWatcher();
        }

        public void RefreshStatus()
        {
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(_currentAssemblyPath))
                {
                    _currentAssemblyPath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                }
                string configuredPath = ReadConfiguredAssemblyPath();
                bool pending = !string.IsNullOrWhiteSpace(configuredPath)
                    && !string.Equals(Path.GetFullPath(configuredPath), _currentAssemblyPath, StringComparison.OrdinalIgnoreCase);

                RuntimeState.Instance.CoreUpdatePending = pending;
                RuntimeState.Instance.PendingCorePath = pending ? configuredPath : "None";
                if (pending)
                {
                    Logger.Info(string.Format("[CoreUpdateManager] Core update pending: {0}", configuredPath));
                }
            }
        }

        private void StartWatcher()
        {
            try
            {
                string directory = Path.GetDirectoryName(_manifestPath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

                _watcher = new FileSystemWatcher(directory, Path.GetFileName(_manifestPath));
                _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                _watcher.Changed += OnManifestChanged;
                _watcher.Created += OnManifestChanged;
                _watcher.Renamed += OnManifestChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Logger.Warn(string.Format("[CoreUpdateManager] Manifest watcher unavailable: {0}", ex.Message));
            }
        }

        private void OnManifestChanged(object sender, FileSystemEventArgs e)
        {
            lock (_sync)
            {
                if (_debounceTimer != null) _debounceTimer.Dispose();
                _debounceTimer = new Timer(delegate { RefreshStatus(); }, null, 500, Timeout.Infinite);
            }
        }

        private string ReadConfiguredAssemblyPath()
        {
            try
            {
                if (!File.Exists(_manifestPath)) return null;
                var document = new XmlDocument();
                document.Load(_manifestPath);
                XmlNode node = document.SelectSingleNode("/RevitAddIns/AddIn/Assembly");
                return node == null ? null : node.InnerText.Trim();
            }
            catch (Exception ex)
            {
                Logger.Warn(string.Format("[CoreUpdateManager] Cannot read add-in manifest: {0}", ex.Message));
                return null;
            }
        }

        private static string GetBuildId(string assemblyPath)
        {
            string name = Path.GetFileNameWithoutExtension(assemblyPath);
            const string prefix = "BEAI.QueryHost.";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(prefix.Length);
            }

            return Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        public void Dispose()
        {
            if (_debounceTimer != null) _debounceTimer.Dispose();
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
        }
    }
}
