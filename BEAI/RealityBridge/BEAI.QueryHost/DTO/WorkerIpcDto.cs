using System.Collections.Generic;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class WorkerIpcMessage
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; } // "ping", "health", "evaluate_rules", "filter_elements", "aggregate"

        [JsonProperty("payload")]
        public string Payload { get; set; }

        [JsonProperty("rulesJson")]
        public string RulesJson { get; set; }
    }

    public class WorkerIpcResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("payload")]
        public string Payload { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }
}
