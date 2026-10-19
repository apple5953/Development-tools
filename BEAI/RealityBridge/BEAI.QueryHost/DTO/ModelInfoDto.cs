using System;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class ModelInfoDto
    {
        [JsonProperty("documentTitle")]
        public string DocumentTitle { get; set; }

        [JsonProperty("documentPath")]
        public string DocumentPath { get; set; }

        [JsonProperty("projectName")]
        public string ProjectName { get; set; }

        [JsonProperty("projectNumber")]
        public string ProjectNumber { get; set; }

        [JsonProperty("isWorkshared")]
        public bool IsWorkshared { get; set; }

        [JsonProperty("isModified")]
        public bool IsModified { get; set; }

        [JsonProperty("isFamilyDocument")]
        public bool IsFamilyDocument { get; set; }

        [JsonProperty("activeViewId")]
        public long ActiveViewId { get; set; }

        [JsonProperty("activeView")]
        public string ActiveView { get; set; }

        [JsonProperty("activeViewType")]
        public string ActiveViewType { get; set; }

        [JsonProperty("revitVersion")]
        public string RevitVersion { get; set; }
    }
}
