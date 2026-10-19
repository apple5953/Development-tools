using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class CountRequestDto
    {
        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("categoryId")]
        public long? CategoryId { get; set; }

        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        [JsonProperty("level")]
        public string Level { get; set; }

        [JsonProperty("levelId")]
        public long? LevelId { get; set; }

        [JsonProperty("workset")]
        public string Workset { get; set; }

        [JsonProperty("nameContains")]
        public string NameContains { get; set; }
    }

    public class CountResponseDto
    {
        public CountResponseDto()
        {
            Success = true;
        }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)]
        public string Category { get; set; }
    }
}
