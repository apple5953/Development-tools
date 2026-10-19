using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using BEAI.QueryHost.DTO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BEAI.QueryHost.Core
{
    public class WorkerManager : IDisposable
    {
        private static readonly Lazy<WorkerManager> _lazy = new Lazy<WorkerManager>(delegate { return new WorkerManager(); });
        public static WorkerManager Instance { get { return _lazy.Value; } }

        private const string PipeName = "BEAI_QueryWorker_Pipe";
        private Process _workerProcess;
        private readonly object _processLock = new object();
        private string _baseDirectory;
        private string _workerDirectory;
        private bool _isDisposed;

        public void Initialize(string baseDirectory)
        {
            _baseDirectory = baseDirectory;
            _workerDirectory = Path.Combine(baseDirectory, "Runtime", "Worker");

            if (!Directory.Exists(_workerDirectory))
            {
                Directory.CreateDirectory(_workerDirectory);
            }

            EnsureStateFiles();
            StartWorker(true);
        }

        private void EnsureStateFiles()
        {
            string currentJsonPath = Path.Combine(_workerDirectory, "current.json");
            if (!File.Exists(currentJsonPath))
            {
                File.WriteAllText(currentJsonPath, JsonConvert.SerializeObject(new { version = "v0001" }, Formatting.Indented), Encoding.UTF8);
            }

            string stateJsonPath = Path.Combine(_workerDirectory, "worker-state.json");
            if (!File.Exists(stateJsonPath))
            {
                File.WriteAllText(stateJsonPath, JsonConvert.SerializeObject(new { current = "v0001", lastKnownGood = "v0001" }, Formatting.Indented), Encoding.UTF8);
            }
        }

        public bool StartWorker(bool isInitial)
        {
            lock (_processLock)
            {
                try
                {
                    StopWorkerInternal();

                    string targetVersion = GetConfiguredVersion();
                    string workerExePath = GetWorkerExePath(targetVersion);

                    if (!File.Exists(workerExePath))
                    {
                        string fallbackExe = Path.Combine(_baseDirectory, "BEAI.QueryWorker.exe");
                        string workerSubDirExe = Path.Combine(_baseDirectory, "Worker", "BEAI.QueryWorker.exe");
                        if (File.Exists(fallbackExe))
                        {
                            workerExePath = fallbackExe;
                            targetVersion = "dev";
                        }
                        else if (File.Exists(workerSubDirExe))
                        {
                            workerExePath = workerSubDirExe;
                            targetVersion = "dev";
                        }
                        else
                        {
                            Logger.Warn(string.Format("[WorkerManager] Worker exe not found at: {0}. Worker offline.", workerExePath));
                            RuntimeState.Instance.WorkerStatus = "offline";
                            RuntimeState.Instance.WorkerVersion = targetVersion;
                            return false;
                        }
                    }

                    Logger.Info(string.Format("[WorkerManager] Launching Worker: {0} (Version: {1})", workerExePath, targetVersion));

                    var startInfo = new ProcessStartInfo();
                    startInfo.FileName = workerExePath;
                    startInfo.Arguments = string.Format("--pipe {0} --version {1}", PipeName, targetVersion);
                    startInfo.UseShellExecute = false;
                    startInfo.CreateNoWindow = true;
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                    startInfo.WorkingDirectory = Path.GetDirectoryName(workerExePath);

                    _workerProcess = new Process();
                    _workerProcess.StartInfo = startInfo;
                    _workerProcess.EnableRaisingEvents = true;
                    _workerProcess.Exited += OnWorkerExited;

                    if (!_workerProcess.Start())
                    {
                        throw new InvalidOperationException("Failed to start worker process.");
                    }

                    _workerProcess.BeginOutputReadLine();
                    _workerProcess.BeginErrorReadLine();

                    Thread.Sleep(300);
                    bool isHealthy = HealthCheck(targetVersion);

                    if (isHealthy)
                    {
                        RuntimeState.Instance.WorkerStatus = "online";
                        RuntimeState.Instance.WorkerVersion = targetVersion;
                        RuntimeState.Instance.SetLastWorkerRestart();
                        SaveLastKnownGood(targetVersion);
                        Logger.Info(string.Format("[WorkerManager] Worker {0} is ONLINE and healthy.", targetVersion));
                        return true;
                    }
                    else
                    {
                        Logger.Warn(string.Format("[WorkerManager] Worker {0} failed health check. Attempting rollback...", targetVersion));
                        return RollbackWorker();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(string.Format("[WorkerManager] Error starting worker: {0}", ex.Message), ex);
                    RuntimeState.Instance.WorkerStatus = "error";
                    RuntimeState.Instance.SetLastError(string.Format("WORKER_START_FAILED: {0}", ex.Message));
                    return false;
                }
            }
        }

        public bool RestartWorker()
        {
            Logger.Info("[WorkerManager] Restart Worker requested from Ribbon/API.");
            return StartWorker(false);
        }

        public bool RollbackWorker()
        {
            lock (_processLock)
            {
                string stateJsonPath = Path.Combine(_workerDirectory, "worker-state.json");
                if (!File.Exists(stateJsonPath))
                {
                    Logger.Error("[WorkerManager] worker-state.json not found. Cannot rollback.");
                    RuntimeState.Instance.WorkerStatus = "offline";
                    return false;
                }

                try
                {
                    string content = File.ReadAllText(stateJsonPath, Encoding.UTF8);
                    var obj = JObject.Parse(content);
                    string lastKnownGood = obj["lastKnownGood"] != null ? obj["lastKnownGood"].ToString() : "";
                    string current = obj["current"] != null ? obj["current"].ToString() : "";

                    if (string.IsNullOrEmpty(lastKnownGood) || lastKnownGood == current)
                    {
                        Logger.Warn("[WorkerManager] No alternate lastKnownGood version to rollback to.");
                        RuntimeState.Instance.WorkerStatus = "offline";
                        return false;
                    }

                    Logger.Warn(string.Format("[WorkerManager] Performing WORKER_ROLLBACK from {0} to {1}", current, lastKnownGood));
                    StopWorkerInternal();

                    string currentJsonPath = Path.Combine(_workerDirectory, "current.json");
                    File.WriteAllText(currentJsonPath, JsonConvert.SerializeObject(new { version = lastKnownGood }, Formatting.Indented), Encoding.UTF8);

                    string workerExe = GetWorkerExePath(lastKnownGood);
                    if (!File.Exists(workerExe))
                    {
                        Logger.Error(string.Format("[WorkerManager] Rollback target exe {0} does not exist.", workerExe));
                        RuntimeState.Instance.WorkerStatus = "offline";
                        return false;
                    }

                    var startInfo = new ProcessStartInfo();
                    startInfo.FileName = workerExe;
                    startInfo.Arguments = string.Format("--pipe {0} --version {1}", PipeName, lastKnownGood);
                    startInfo.UseShellExecute = false;
                    startInfo.CreateNoWindow = true;
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                    startInfo.WorkingDirectory = Path.GetDirectoryName(workerExe);

                    _workerProcess = new Process();
                    _workerProcess.StartInfo = startInfo;
                    _workerProcess.EnableRaisingEvents = true;
                    _workerProcess.Exited += OnWorkerExited;
                    _workerProcess.Start();
                    _workerProcess.BeginOutputReadLine();
                    _workerProcess.BeginErrorReadLine();

                    Thread.Sleep(300);
                    if (HealthCheck(lastKnownGood))
                    {
                        RuntimeState.Instance.WorkerStatus = "online";
                        RuntimeState.Instance.WorkerVersion = lastKnownGood;
                        RuntimeState.Instance.SetLastWorkerRestart();
                        Logger.Info(string.Format("[WorkerManager] WORKER_ROLLBACK to {0} succeeded.", lastKnownGood));
                        return true;
                    }
                    else
                    {
                        RuntimeState.Instance.WorkerStatus = "offline";
                        Logger.Error(string.Format("[WorkerManager] Rollback version {0} also failed health check.", lastKnownGood));
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(string.Format("[WorkerManager] Rollback error: {0}", ex.Message), ex);
                    RuntimeState.Instance.WorkerStatus = "offline";
                    return false;
                }
            }
        }

        private bool HealthCheck(string expectedVersion)
        {
            try
            {
                var msg = new WorkerIpcMessage();
                msg.Id = Guid.NewGuid().ToString("N");
                msg.Action = "health";
                msg.Payload = "";

                var response = SendMessage(msg, 2000);
                return response != null && response.Success;
            }
            catch (Exception ex)
            {
                Logger.Warn(string.Format("[WorkerManager] HealthCheck failed: {0}", ex.Message));
                return false;
            }
        }

        public WorkerIpcResponse SendMessage(WorkerIpcMessage message, int timeoutMs)
        {
            if (_isDisposed || _workerProcess == null || _workerProcess.HasExited)
            {
                var r = new WorkerIpcResponse();
                r.Success = false;
                r.Error = "WORKER_OFFLINE";
                return r;
            }

            try
            {
                using (var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipeClient.Connect(timeoutMs);

                    string json = JsonConvert.SerializeObject(message);
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    byte[] lengthPrefix = BitConverter.GetBytes(bytes.Length);

                    pipeClient.Write(lengthPrefix, 0, 4);
                    pipeClient.Write(bytes, 0, bytes.Length);
                    pipeClient.Flush();

                    byte[] respLengthPrefix = new byte[4];
                    int read = pipeClient.Read(respLengthPrefix, 0, 4);
                    if (read < 4)
                    {
                        var r = new WorkerIpcResponse();
                        r.Success = false;
                        r.Error = "INVALID_WORKER_RESPONSE";
                        return r;
                    }

                    int respLen = BitConverter.ToInt32(respLengthPrefix, 0);
                    byte[] respBytes = new byte[respLen];
                    int totalRead = 0;
                    while (totalRead < respLen)
                    {
                        int res = pipeClient.Read(respBytes, totalRead, respLen - totalRead);
                        if (res <= 0) break;
                        totalRead += res;
                    }

                    string respJson = Encoding.UTF8.GetString(respBytes, 0, totalRead);
                    return JsonConvert.DeserializeObject<WorkerIpcResponse>(respJson);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(string.Format("[WorkerManager] IPC message failed: {0}", ex.Message));
                var r = new WorkerIpcResponse();
                r.Success = false;
                r.Error = string.Format("WORKER_IPC_ERROR: {0}", ex.Message);
                return r;
            }
        }

        private void OnWorkerExited(object sender, EventArgs e)
        {
            if (!_isDisposed)
            {
                Logger.Warn("[WorkerManager] Worker process exited unexpectedly. Setting status to WORKER_OFFLINE.");
                RuntimeState.Instance.WorkerStatus = "offline";
            }
        }

        private void StopWorkerInternal()
        {
            try
            {
                if (_workerProcess != null && !_workerProcess.HasExited)
                {
                    try
                    {
                        var msg = new WorkerIpcMessage();
                        msg.Id = "shutdown";
                        msg.Action = "shutdown";
                        SendMessage(msg, 500);

                        if (!_workerProcess.WaitForExit(1000))
                        {
                            _workerProcess.Kill();
                        }
                    }
                    catch
                    {
                        _workerProcess.Kill();
                    }
                }
            }
            catch { }
            finally
            {
                if (_workerProcess != null)
                {
                    _workerProcess.Dispose();
                    _workerProcess = null;
                }
            }
        }

        private string GetConfiguredVersion()
        {
            string currentJsonPath = Path.Combine(_workerDirectory, "current.json");
            if (File.Exists(currentJsonPath))
            {
                try
                {
                    var obj = JObject.Parse(File.ReadAllText(currentJsonPath, Encoding.UTF8));
                    if (obj["version"] != null)
                    {
                        return obj["version"].ToString();
                    }
                }
                catch { }
            }
            return "v0001";
        }

        private string GetWorkerExePath(string version)
        {
            return Path.Combine(_workerDirectory, version, "BEAI.QueryWorker.exe");
        }

        private void SaveLastKnownGood(string version)
        {
            try
            {
                string stateJsonPath = Path.Combine(_workerDirectory, "worker-state.json");
                var state = new { current = version, lastKnownGood = version };
                File.WriteAllText(stateJsonPath, JsonConvert.SerializeObject(state, Formatting.Indented), Encoding.UTF8);
            }
            catch { }
        }

        public void Dispose()
        {
            _isDisposed = true;
            StopWorkerInternal();
        }
    }
}
