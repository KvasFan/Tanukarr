using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.Tmdb.Resource
{
    public class TmdbMovieResource
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Overview { get; set; }
        public string Status { get; set; }
        public int Runtime { get; set; }
        public List<TmdbGenreResource> Genres { get; set; }
        [JsonProperty("original_title")]
        public string OriginalTitle { get; set; }
        [JsonProperty("original_language")]
        public string OriginalLanguage { get; set; }
        [JsonProperty("release_date")]
        public string ReleaseDate { get; set; }
        [JsonProperty("imdb_id")]
        public string ImdbId { get; set; }
        [JsonProperty("poster_path")]
        public string PosterPath { get; set; }
        [JsonProperty("backdrop_path")]
        public string BackdropPath { get; set; }
        [JsonProperty("vote_count")]
        public int VoteCount { get; set; }
        [JsonProperty("vote_average")]
        public decimal VoteAverage { get; set; }
        [JsonProperty("belongs_to_collection")]
        public TmdbMovieCollectionResource Collection { get; set; }
    }

    public class TmdbMovieSearchResource
    {
        public List<TmdbMovieResource> Results { get; set; }
        [JsonProperty("movie_results")]
        public List<TmdbMovieResource> MovieResults { get; set; }
    }

    public class TmdbMovieCollectionResource
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<TmdbMovieResource> Parts { get; set; }
    }
}
