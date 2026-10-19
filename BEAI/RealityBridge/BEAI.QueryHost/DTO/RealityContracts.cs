using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BEAI.QueryHost.DTO
{
    public static class RealityTools
    {
        public const string SystemHealth = "system.health";
        public const string SystemVersion = "system.version";
        public const string SystemCapabilities = "system.capabilities";
        public const string DocumentGetActive = "document.get_active";
        public const string LevelList = "level.list";
        public const string ElementQuery = "element.query";
        public const string ElementGetParameters = "element.get_parameters";
    }

    public class RealityRequest
    {
        public RealityRequest()
        {
            RequestId = Guid.NewGuid().ToString("N");
            Version = "1.0";
            Timestamp = DateTime.UtcNow;
            Args = new JObject();
            Context = new RealityRequestContext();
        }

        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("tool")]
        public string Tool { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("args")]
        public JObject Args { get; set; }

        [JsonProperty("context")]
        public RealityRequestContext Context { get; set; }
    }

    public class RealityRequestContext
    {
        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("client")]
        public string Client { get; set; }
    }

    public class RealityError
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class RealityResponse
    {
        public RealityResponse()
        {
            Warnings = new List<string>();
            Errors = new List<RealityError>();
        }

        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("data")]
        public object Data { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; }

        [JsonProperty("errors")]
        public List<RealityError> Errors { get; set; }
    }
}
