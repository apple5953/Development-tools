using System;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class ElementSummaryDto
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("uniqueId")]
        public string UniqueId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("categoryId")]
        public long? CategoryId { get; set; }

        [JsonProperty("categoryName")]
        public string CategoryName { get; set; }

        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("typeId")]
        public long? TypeId { get; set; }

        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        [JsonProperty("levelId")]
        public long? LevelId { get; set; }

        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        [JsonProperty("worksetId")]
        public long? WorksetId { get; set; }

        [JsonProperty("worksetName")]
        public string WorksetName { get; set; }

        [JsonProperty("hostElementId")]
        public long? HostElementId { get; set; }

        [JsonProperty("isPinned")]
        public bool IsPinned { get; set; }
    }
}
