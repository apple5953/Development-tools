using System.Collections.Generic;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class QueryPlanDto
    {
        public QueryPlanDto()
        {
            Categories = new List<string>();
            Include = new QueryPlanIncludeDto();
            ParameterNames = new List<string>();
            Page = 1;
            PageSize = 100;
        }

        [JsonProperty("categories")]
        public List<string> Categories { get; set; }

        [JsonProperty("include")]
        public QueryPlanIncludeDto Include { get; set; }

        [JsonProperty("parameterNames")]
        public List<string> ParameterNames { get; set; }

        [JsonProperty("ruleFile")]
        public string RuleFile { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("pageSize")]
        public int PageSize { get; set; }
    }

    public class QueryPlanIncludeDto
    {
        public QueryPlanIncludeDto()
        {
            Metadata = true;
            InstanceParameters = true;
            TypeParameters = true;
        }

        [JsonProperty("metadata")]
        public bool Metadata { get; set; }

        [JsonProperty("instanceParameters")]
        public bool InstanceParameters { get; set; }

        [JsonProperty("typeParameters")]
        public bool TypeParameters { get; set; }
    }
}
