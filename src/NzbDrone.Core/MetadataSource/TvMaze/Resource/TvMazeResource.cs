using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.TvMaze.Resource
{
    public class TvMazeShowResource
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("premiered")]
        public string Premiered { get; set; }

        [JsonProperty("ended")]
        public string Ended { get; set; }

        [JsonProperty("runtime")]
        public int? Runtime { get; set; }

        [JsonProperty("averageRuntime")]
        public int? AverageRuntime { get; set; }

        [JsonProperty("genres")]
        public List<string> Genres { get; set; }

        [JsonProperty("externals")]
        public TvMazeExternalsResource Externals { get; set; }

        [JsonProperty("image")]
        public TvMazeImageResource Image { get; set; }

        [JsonProperty("network")]
        public TvMazeNetworkResource Network { get; set; }

        [JsonProperty("webChannel")]
        public TvMazeNetworkResource WebChannel { get; set; }

        [JsonProperty("rating")]
        public TvMazeRatingResource Rating { get; set; }
    }

    public class TvMazeSearchResource
    {
        [JsonProperty("show")]
        public TvMazeShowResource Show { get; set; }
    }

    public class TvMazeEpisodeResource
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("season")]
        public int? Season { get; set; }

        [JsonProperty("number")]
        public int? Number { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("airdate")]
        public string Airdate { get; set; }

        [JsonProperty("airstamp")]
        public string Airstamp { get; set; }

        [JsonProperty("runtime")]
        public int? Runtime { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }
    }

    public class TvMazeExternalsResource
    {
        [JsonProperty("thetvdb")]
        public int? Tvdb { get; set; }

        [JsonProperty("tvrage")]
        public int? TvRage { get; set; }

        [JsonProperty("imdb")]
        public string Imdb { get; set; }
    }

    public class TvMazeImageResource
    {
        [JsonProperty("original")]
        public string Original { get; set; }

        [JsonProperty("medium")]
        public string Medium { get; set; }
    }

    public class TvMazeNetworkResource
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class TvMazeRatingResource
    {
        [JsonProperty("average")]
        public decimal? Average { get; set; }
    }
}
