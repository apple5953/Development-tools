using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BEAI.QueryHost.Identity;
using Newtonsoft.Json;

namespace BEAI.QueryHost.Core
{
    public class SessionManager : IDisposable
    {
        private static readonly Lazy<SessionManager> _lazy = new Lazy<SessionManager>(delegate { return new SessionManager(); });
        public static SessionManager Instance { get { return _lazy.Value; } }

        private readonly string _sessionsDirectory;
        private string _sessionFilePath;
        private RevitSessionInfo _currentSession;
        private UIControlledApplication _application;

        public SessionManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _sessionsDirectory = DtStorage.DataDir != null
                ? Path.Combine(DtStorage.DataDir, "BEAI", "sessions")
                : Path.Combine(appData, "BEAI", "sessions");
            EnsureDirectory();
            InitializeSession();
        }

        public RevitSessionInfo CurrentSession { get { return _currentSession; } }

        public void Initialize(UIControlledApplication app)
        {
            _application = app;
            if (_application != null)
            {
                _application.ViewActivated += OnViewActivated;
                _application.ControlledApplication.DocumentOpened += OnDocumentOpened;
                _application.ControlledApplication.DocumentClosing += OnDocumentClosing;
                _application.ControlledApplication.DocumentSaved += OnDocumentSaved;
            }
            PublishSession();
        }

        private void EnsureDirectory()
        {
            try
            {
                if (!Directory.Exists(_sessionsDirectory))
                {
                    Directory.CreateDirectory(_sessionsDirectory);
                }
            }
            catch { }
        }

        private void InitializeSession()
        {
            _currentSession = new RevitSessionInfo();
            _currentSession.SessionId = Guid.NewGuid().ToString("N");
            _currentSession.DeviceId = DeviceIdentityManager.Instance.DeviceId;
            _currentSession.DeviceName = DeviceIdentityManager.Instance.DeviceName;
            _currentSession.ProcessId = Process.GetCurrentProcess().Id;
            _currentSession.RevitVersion = "2024";
            _currentSession.AdapterVersion = "0.1.0";
            _currentSession.DocumentTitle = "None";
            _currentSession.DocumentPath = "";
            _currentSession.ActiveView = "None";
            _currentSession.Status = "online";
            _currentSession.ConnectedAt = DateTime.UtcNow;
            _currentSession.LastSeenAt = DateTime.UtcNow;
            _currentSession.Port = 8765;

            _sessionFilePath = Path.Combine(_sessionsDirectory, string.Format("session_{0}.json", _currentSession.SessionId));
        }

        public void UpdateDocument(Document doc)
        {
            if (_currentSession == null) return;

            if (doc != null)
            {
                _currentSession.DocumentTitle = doc.Title;
                _currentSession.DocumentPath = doc.PathName;
                _currentSession.ActiveView = doc.ActiveView != null ? doc.ActiveView.Name : "None";
                _currentSession.IsModified = doc.IsModified;
            }
            else
            {
                _currentSession.DocumentTitle = "None";
                _currentSession.DocumentPath = "";
                _currentSession.ActiveView = "None";
                _currentSession.IsModified = false;
            }

            _currentSession.LastSeenAt = DateTime.UtcNow;
            PublishSession();
        }

        private void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            UpdateDocument(e.Document);
            Logger.Info(string.Format("[SessionManager] Document opened: {0}", e.Document != null ? e.Document.Title : "Unknown"));
        }

        private void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            UpdateDocument(null);
            Logger.Info("[SessionManager] Document closed.");
        }

        private void OnDocumentSaved(object sender, DocumentSavedEventArgs e)
        {
            UpdateDocument(e.Document);
        }

        private void OnViewActivated(object sender, ViewActivatedEventArgs e)
        {
            if (e.CurrentActiveView != null && _currentSession != null)
            {
                _currentSession.ActiveView = e.CurrentActiveView.Name;
                _currentSession.LastSeenAt = DateTime.UtcNow;
                PublishSession();
            }
        }

        public void PublishSession()
        {
            try
            {
                EnsureDirectory();
                if (!string.IsNullOrEmpty(_sessionFilePath) && _currentSession != null)
                {
                    _currentSession.LastSeenAt = DateTime.UtcNow;
                    string json = JsonConvert.SerializeObject(_currentSession, Formatting.Indented);
                    File.WriteAllText(_sessionFilePath, json, Encoding.UTF8);
                }
            }
            catch { }
        }

        public void RemoveSession()
        {
            try
            {
                if (!string.IsNullOrEmpty(_sessionFilePath) && File.Exists(_sessionFilePath))
                {
                    File.Delete(_sessionFilePath);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_application != null)
            {
                try
                {
                    _application.ViewActivated -= OnViewActivated;
                    _application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
                    _application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
                    _application.ControlledApplication.DocumentSaved -= OnDocumentSaved;
                }
                catch { }
            }
            RemoveSession();
        }
    }

    public class RevitSessionInfo
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("deviceName")]
        public string DeviceName { get; set; }

        [JsonProperty("processId")]
        public int ProcessId { get; set; }

        [JsonProperty("revitVersion")]
        public string RevitVersion { get; set; }

        [JsonProperty("adapterVersion")]
        public string AdapterVersion { get; set; }

        [JsonProperty("documentTitle")]
        public string DocumentTitle { get; set; }

        [JsonProperty("documentPath")]
        public string DocumentPath { get; set; }

        [JsonProperty("activeView")]
        public string ActiveView { get; set; }

        [JsonProperty("isModified")]
        public bool IsModified { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("connectedAt")]
        public DateTime ConnectedAt { get; set; }

        [JsonProperty("lastSeenAt")]
        public DateTime LastSeenAt { get; set; }
    }
}
