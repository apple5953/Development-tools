using System;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class CategoryDto
    {
        [JsonProperty("categoryId")]
        public long CategoryId { get; set; }

        [JsonProperty("categoryName")]
        public string CategoryName { get; set; }

        [JsonProperty("categoryType")]
        public string CategoryType { get; set; } // Model / Annotation / Analytical / Internal

        [JsonProperty("elementCount")]
        public int ElementCount { get; set; }

        [JsonProperty("allowsBoundParameters")]
        public bool AllowsBoundParameters { get; set; }
    }
}
