using System;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class StatusDto
    {
        public StatusDto()
        {
            Success = true;
            ServerAddress = "http://127.0.0.1:8765/";
            RevitVersion = "2024";
            WorkerStatus = "offline";
            WorkerVersion = "none";
            LastQueryTime = "None";
            LastReloadTime = "None";
            LastWorkerRestartTime = "None";
            LastError = "None";
        }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("serverRunning")]
        public bool ServerRunning { get; set; }

        [JsonProperty("serverAddress")]
        public string ServerAddress { get; set; }

        [JsonProperty("revitVersion")]
        public string RevitVersion { get; set; }

        [JsonProperty("documentOpen")]
        public bool DocumentOpen { get; set; }

        [JsonProperty("documentTitle")]
        public string DocumentTitle { get; set; }

        [JsonProperty("activeView")]
        public string ActiveView { get; set; }

        [JsonProperty("workerStatus")]
        public string WorkerStatus { get; set; }

        [JsonProperty("workerVersion")]
        public string WorkerVersion { get; set; }

        [JsonProperty("ruleGeneration")]
        public int RuleGeneration { get; set; }

        [JsonProperty("queryCount")]
        public long QueryCount { get; set; }

        [JsonProperty("developmentMode")]
        public bool DevelopmentMode { get; set; }

        [JsonProperty("lastQueryTime")]
        public string LastQueryTime { get; set; }

        [JsonProperty("lastReloadTime")]
        public string LastReloadTime { get; set; }

        [JsonProperty("lastWorkerRestartTime")]
        public string LastWorkerRestartTime { get; set; }

        [JsonProperty("lastError")]
        public string LastError { get; set; }

        [JsonProperty("coreBuild")]
        public string CoreBuild { get; set; }

        [JsonProperty("coreUpdatePending")]
        public bool CoreUpdatePending { get; set; }
    }
}
