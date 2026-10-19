using System.Collections.Generic;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class ParameterSearchRequestDto
    {
        public ParameterSearchRequestDto()
        {
            Page = 1;
            PageSize = 100;
        }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("parameter")]
        public ParameterQueryCriterion Parameter { get; set; }

        [JsonProperty("operator")]
        public string Operator { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("pageSize")]
        public int PageSize { get; set; }
    }

    public class ParameterQueryCriterion
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("parameterId")]
        public string ParameterId { get; set; }

        [JsonProperty("builtInParameter")]
        public string BuiltInParameter { get; set; }

        [JsonProperty("sharedParameterGuid")]
        public string SharedParameterGuid { get; set; }
    }

    public class ParameterSearchResponseDto
    {
        public ParameterSearchResponseDto()
        {
            Success = true;
            Items = new List<ParameterMatchItemDto>();
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
        public List<ParameterMatchItemDto> Items { get; set; }
    }

    public class ParameterMatchItemDto
    {
        [JsonProperty("element")]
        public ElementSummaryDto Element { get; set; }

        [JsonProperty("matchedParameter")]
        public ParameterDto MatchedParameter { get; set; }
    }
}
