using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;
using BEAI.QueryHost.Core;

namespace BEAI.QueryHost
{
    public static class EmbeddedBridge
    {
        private static readonly object SyncRoot = new object();
        private static bool _started;

        public static bool IsStarted
        {
            get { lock (SyncRoot) { return _started; } }
        }

        public static Result Start(UIControlledApplication application, string hostDirectory)
        {
            lock (SyncRoot)
            {
                if (_started) return Result.Succeeded;

                try
                {
                    string baseDirectory = string.IsNullOrWhiteSpace(hostDirectory)
                        ? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                        : Path.GetFullPath(hostDirectory);

                    RuntimeState.Instance.BaseDirectory = baseDirectory;
                    RuntimeState.Instance.CurrentCorePath = Assembly.GetExecutingAssembly().Location;
                    RuntimeState.Instance.CoreBuild = Assembly.GetExecutingAssembly().GetName().Version.ToString();

                    Logger.Initialize(baseDirectory);
                    Logger.Info("BEAI embedded bridge starting.");

                    var eventHandler = new BeaiQueryExternalEventHandler();
                    BeaiQueryExternalEventHandler.Initialize(eventHandler);
                    SessionManager.Instance.Initialize(application);
                    HotReloadManager.Instance.Initialize(baseDirectory);
                    WorkerManager.Instance.Initialize(baseDirectory);

                    if (!RevitServerHost.Instance.Start())
                    {
                        throw new InvalidOperationException(RuntimeState.Instance.LastError);
                    }

                    _started = true;
                    Logger.Info("BEAI embedded bridge started at http://127.0.0.1:8765/.");
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    Logger.Error("BEAI embedded bridge startup failed.", ex);
                    StopInternal();
                    return Result.Failed;
                }
            }
        }

        public static Result Stop()
        {
            lock (SyncRoot)
            {
                StopInternal();
                return Result.Succeeded;
            }
        }

        private static void StopInternal()
        {
            try { RevitServerHost.Instance.Stop(); } catch { }
            try { WorkerManager.Instance.Dispose(); } catch { }
            try { HotReloadManager.Instance.Dispose(); } catch { }
            try { SessionManager.Instance.Dispose(); } catch { }
            _started = false;
        }
    }
}
