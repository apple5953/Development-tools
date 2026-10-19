using System.Collections.Generic;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class SearchElementsRequestDto
    {
        public SearchElementsRequestDto()
        {
            Page = 1;
            PageSize = 100;
        }

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

        [JsonProperty("worksetId")]
        public long? WorksetId { get; set; }

        [JsonProperty("nameContains")]
        public string NameContains { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("pageSize")]
        public int PageSize { get; set; }
    }

    public class SearchElementsResponseDto
    {
        public SearchElementsResponseDto()
        {
            Success = true;
            Items = new List<ElementSummaryDto>();
        }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("pageSize")]
        public int PageSize { get; set; }

        [JsonProperty("hasMore")]
        public bool HasMore { get; set; }

        [JsonProperty("items")]
        public List<ElementSummaryDto> Items { get; set; }
    }
}
