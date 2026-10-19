using System.Collections.Generic;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class ElementParametersDto
    {
        public ElementParametersDto()
        {
            Success = true;
            InstanceParameters = new List<ParameterDto>();
            TypeParameters = new List<ParameterDto>();
        }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("element")]
        public ElementHeaderDto Element { get; set; }

        [JsonProperty("instanceParameters")]
        public List<ParameterDto> InstanceParameters { get; set; }

        [JsonProperty("type", NullValueHandling = NullValueHandling.Include)]
        public TypeHeaderDto Type { get; set; }

        [JsonProperty("typeParameters")]
        public List<ParameterDto> TypeParameters { get; set; }
    }

    public class ElementHeaderDto
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("uniqueId")]
        public string UniqueId { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("typeName")]
        public string TypeName { get; set; }
    }

    public class TypeHeaderDto
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }
}
