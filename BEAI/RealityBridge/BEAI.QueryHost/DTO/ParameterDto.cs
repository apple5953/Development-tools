using System;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class ParameterDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("parameterId")]
        public string ParameterId { get; set; }

        [JsonProperty("builtInParameter", NullValueHandling = NullValueHandling.Include)]
        public string BuiltInParameter { get; set; }

        [JsonProperty("sharedParameterGuid", NullValueHandling = NullValueHandling.Include)]
        public string SharedParameterGuid { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; } // "instance" or "type"

        [JsonProperty("storageType")]
        public string StorageType { get; set; } // String, Integer, Double, ElementId, None

        [JsonProperty("hasValue")]
        public bool HasValue { get; set; }

        [JsonProperty("isReadOnly")]
        public bool IsReadOnly { get; set; }

        [JsonProperty("rawValue", NullValueHandling = NullValueHandling.Include)]
        public object RawValue { get; set; }

        [JsonProperty("displayValue", NullValueHandling = NullValueHandling.Include)]
        public string DisplayValue { get; set; }

        [JsonProperty("unit", NullValueHandling = NullValueHandling.Include)]
        public string Unit { get; set; }

        [JsonProperty("referencedElementId", NullValueHandling = NullValueHandling.Ignore)]
        public long? ReferencedElementId { get; set; }

        [JsonProperty("referencedElementName", NullValueHandling = NullValueHandling.Ignore)]
        public string ReferencedElementName { get; set; }
    }
}
