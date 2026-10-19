using System;
using System.Threading;
using BEAI.QueryHost.DTO;

namespace BEAI.QueryHost.Core
{
    public class RuntimeState
    {
        private static readonly Lazy<RuntimeState> _lazy = new Lazy<RuntimeState>(delegate { return new RuntimeState(); });
        public static RuntimeState Instance { get { return _lazy.Value; } }

        private long _queryCount = 0;
        private readonly object _stateLock = new object();

        public RuntimeState()
        {
            ServerRunning = false;
            ServerAddress = "http://127.0.0.1:8765/";
            RevitVersion = "2024";
            DocumentOpen = false;
            DocumentTitle = "None";
            ActiveView = "None";
            WorkerStatus = "offline";
            WorkerVersion = "none";
            RuleGeneration = 0;
            DevelopmentMode = true;
            LastQueryTime = "None";
            LastReloadTime = "None";
            LastWorkerRestartTime = "None";
            LastError = "None";
            CoreBuild = "unknown";
            CurrentCorePath = "None";
            PendingCorePath = "None";
            BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }

        public bool ServerRunning { get; set; }
        public string ServerAddress { get; set; }
        public string RevitVersion { get; set; }
        public bool DocumentOpen { get; set; }
        public string DocumentTitle { get; set; }
        public string ActiveView { get; set; }
        public string WorkerStatus { get; set; }
        public string WorkerVersion { get; set; }
        public int RuleGeneration { get; set; }
        public bool DevelopmentMode { get; set; }
        public string LastQueryTime { get; set; }
        public string LastReloadTime { get; set; }
        public string LastWorkerRestartTime { get; set; }
        public string LastError { get; set; }
        public string CoreBuild { get; set; }
        public bool CoreUpdatePending { get; set; }
        public string CurrentCorePath { get; set; }
        public string PendingCorePath { get; set; }
        public string BaseDirectory { get; set; }

        public long QueryCount { get { return Interlocked.Read(ref _queryCount); } }

        public void RecordQuery()
        {
            Interlocked.Increment(ref _queryCount);
            lock (_stateLock)
            {
                LastQueryTime = DateTime.Now.ToString("HH:mm:ss");
            }
        }

        public void SetLastReload()
        {
            lock (_stateLock)
            {
                LastReloadTime = DateTime.Now.ToString("HH:mm:ss");
            }
        }

        public void SetLastWorkerRestart()
        {
            lock (_stateLock)
            {
                LastWorkerRestartTime = DateTime.Now.ToString("HH:mm:ss");
            }
        }

        public void SetLastError(string error)
        {
            lock (_stateLock)
            {
                LastError = error;
            }
        }

        public void ClearLastError()
        {
            lock (_stateLock)
            {
                LastError = "None";
            }
        }

        public StatusDto ToStatusDto()
        {
            lock (_stateLock)
            {
                var dto = new StatusDto();
                dto.Success = true;
                dto.ServerRunning = ServerRunning;
                dto.ServerAddress = ServerAddress;
                dto.RevitVersion = RevitVersion;
                dto.DocumentOpen = DocumentOpen;
                dto.DocumentTitle = DocumentTitle;
                dto.ActiveView = ActiveView;
                dto.WorkerStatus = WorkerStatus;
                dto.WorkerVersion = WorkerVersion;
                dto.RuleGeneration = RuleGeneration;
                dto.QueryCount = QueryCount;
                dto.DevelopmentMode = DevelopmentMode;
                dto.LastQueryTime = LastQueryTime;
                dto.LastReloadTime = LastReloadTime;
                dto.LastWorkerRestartTime = LastWorkerRestartTime;
                dto.LastError = LastError;
                dto.CoreBuild = CoreBuild;
                dto.CoreUpdatePending = CoreUpdatePending;
                return dto;
            }
        }

        public string GetFormattedStatusSummary()
        {
            lock (_stateLock)
            {
                return string.Format(
                    "查詢服務：{0}\n" +
                    "位址：{1}\n" +
                    "Revit 版本：{2}\n" +
                    "模型：{3}\n" +
                    "目前視圖：{4}\n" +
                    "背景處理器：{5}\n" +
                    "處理器版本：{6}\n" +
                    "規則版本：{7}\n" +
                    "查詢次數：{8}\n" +
                    "最後查詢：{9}\n" +
                    "最後重載：{10}\n" +
                    "最後重啟處理器：{11}\n" +
                    "核心版本：{12}\n" +
                    "核心更新：{13}\n" +
                    "最後錯誤：{14}",
                    ServerRunning ? "運作中" : "已停止",
                    ServerAddress,
                    RevitVersion,
                    DocumentTitle,
                    ActiveView,
                    string.Equals(WorkerStatus, "online", StringComparison.OrdinalIgnoreCase) ? "運作中" : "離線",
                    WorkerVersion,
                    RuleGeneration,
                    QueryCount,
                    LastQueryTime,
                    LastReloadTime,
                    LastWorkerRestartTime,
                    CoreBuild,
                    CoreUpdatePending ? "等待重新啟動 Revit" : "已是最新版",
                    LastError);
            }
        }
    }
}
