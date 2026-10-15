using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.Omdb
{
    public class OmdbResource
    {
        public string Title { get; set; }
        public string Year { get; set; }
        public string Type { get; set; }
        public string Released { get; set; }
        public string Runtime { get; set; }
        public string Plot { get; set; }
        public string Genre { get; set; }
        public string Rated { get; set; }
        public string Poster { get; set; }
        public string Response { get; set; }
        [JsonProperty("imdbID")]
        public string ImdbId { get; set; }
        [JsonProperty("imdbRating")]
        public string ImdbRating { get; set; }
        [JsonProperty("imdbVotes")]
        public string ImdbVotes { get; set; }
        public List<OmdbResource> Search { get; set; }
    }
}
