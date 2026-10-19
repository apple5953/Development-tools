using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class LevelDto
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("elevationMm")]
        public double ElevationMm { get; set; }
    }
}

