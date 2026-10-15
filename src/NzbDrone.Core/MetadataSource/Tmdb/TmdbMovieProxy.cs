using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.MetadataSource.Tmdb.Resource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.Tmdb
{
    public interface IMovieCollectionProvider
    {
        MetadataSourceType Source { get; }
        AnimeGroupingResult GetMovieCollection(Series movie);
    }

    public class TmdbMovieProxy : IMetadataProvider, IMovieCollectionProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly IConfigService _config;

        public TmdbMovieProxy(IHttpClient httpClient, IConfigService config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        public MetadataSourceType Source => MetadataSourceType.TmdbMovie;

        public Tuple<Series, List<Episode>> GetSeriesInfo(Series series)
        {
            if (!int.TryParse(series.ForeignId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
                id.ToString(CultureInfo.InvariantCulture) != series.ForeignId)
            {
                throw new MovieMetadataException("Choose a positive native TMDB movie ID.");
            }

            var resource = Get<TmdbMovieResource>("/movie/" + id);
            if (resource?.Id != id)
            {
                throw new MovieMetadataException("TMDB returned a different or missing movie identity; existing metadata is retained.");
            }

            return MovieMetadata.WithFeature(Map(resource));
        }

        public List<Series> SearchForNewSeries(string title)
        {
            if (title.IsNullOrWhiteSpace() || title.Length > 2048 || _config.TmdbApiKey.IsNullOrWhiteSpace())
            {
                return new List<Series>();
            }

            title = title.Trim();
            var imdb = Regex.Match(title, @"\A(?:imdb:)?(?<id>tt[0-9]{7,12})\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (imdb.Success)
            {
                var id = imdb.Groups["id"].Value.ToLowerInvariant();
                return (Get<TmdbMovieSearchResource>("/find/" + id, "external_source", "imdb_id")?.MovieResults ?? new List<TmdbMovieResource>())
                    .Where(movie => movie.Id > 0 && movie.Title.IsNotNullOrWhiteSpace()).Select(Map).ToList();
            }

            var native = Regex.Match(title, @"\A(?:tmdbmovie|tmdb):(?<id>[1-9][0-9]*)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (native.Success)
            {
                return new List<Series> { GetSeriesInfo(new Series { ForeignId = native.Groups["id"].Value }).Item1 };
            }

            var response = Get<TmdbMovieSearchResource>("/search/movie", "query", title);
            var results = (response?.Results ?? new List<TmdbMovieResource>())
                .Where(movie => movie.Id > 0 && movie.Title.IsNotNullOrWhiteSpace()).Select(Map).ToList();
            results.Sort(new SearchSeriesComparer(title));
            return results;
        }

        public AnimeGroupingResult GetMovieCollection(Series movie)
        {
            if (!int.TryParse(movie?.ForeignId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
                id.ToString(CultureInfo.InvariantCulture) != movie.ForeignId)
            {
                throw new MovieMetadataException("Choose a positive native TMDB movie ID.");
            }

            var resource = Get<TmdbMovieResource>("/movie/" + id);
            if (resource?.Id.ToString(CultureInfo.InvariantCulture) != movie.ForeignId || resource.Collection?.Id is not > 0)
            {
                return new AnimeGroupingResult { Status = AnimeGroupingStatus.Unrelated };
            }

            var collection = Get<TmdbMovieCollectionResource>("/collection/" + resource.Collection.Id);
            if (collection?.Id != resource.Collection.Id || collection.Name.IsNullOrWhiteSpace() || collection.Parts == null ||
                collection.Parts.Count > 999 || collection.Parts.Any(part => part.Id <= 0 || part.Title.IsNullOrWhiteSpace() || !Date(part.ReleaseDate).HasValue) ||
                collection.Parts.Select(part => part.Id).Distinct().Count() != collection.Parts.Count)
            {
                throw new MovieMetadataException("TMDB movie collection is incomplete; choose manual membership until its catalogue is complete.");
            }

            var ordered = collection.Parts.OrderBy(part => Date(part.ReleaseDate)).ThenBy(part => part.Id).ToList();
            var slot = ordered.FindIndex(part => part.Id == resource.Id);
            if (slot < 0)
            {
                throw new MovieMetadataException("TMDB collection does not contain this movie.");
            }

            return new AnimeGroupingResult
            {
                Status = AnimeGroupingStatus.Resolved,
                AnimeGroupingMode = AnimeGroupingMode.Automatic,
                AnimeGroupId = "tmdb-movie:" + collection.Id,
                AnimeGroupTitle = collection.Name,
                AnimeGroupMemberKind = AnimeGroupMemberKind.Movie,
                AnimeGroupKind = AnimeGroupKind.MovieSaga,
                AnimeGroupMovieNumber = slot + 1,
                MemberTitle = movie.Title
            };
        }

        private Series Map(TmdbMovieResource resource)
        {
            if (resource.Id <= 0 || resource.Title.IsNullOrWhiteSpace() || resource.Runtime is < 0 or > 10080)
            {
                throw new MovieMetadataException("TMDB movie response has no native ID or title.");
            }

            var movie = MovieMetadata.Create(Source, resource.Id.ToString(CultureInfo.InvariantCulture), resource.Title);
            movie.TmdbId = resource.Id;
            movie.ImdbId = resource.ImdbId?.Length <= 14 && Regex.IsMatch(resource.ImdbId, @"\Att[0-9]{7,12}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ? resource.ImdbId : null;
            movie.Overview = resource.Overview;
            movie.Runtime = Math.Max(resource.Runtime, 0);
            movie.FirstAired = Date(resource.ReleaseDate);
            movie.LastAired = movie.FirstAired;
            movie.Year = movie.FirstAired?.Year ?? 0;
            movie.OriginalTitle = resource.OriginalTitle;
            movie.AlternateTitles = resource.OriginalTitle.IsNotNullOrWhiteSpace() && resource.OriginalTitle != resource.Title
                ? new List<string> { resource.OriginalTitle } : new List<string>();
            movie.OriginalLanguage = IsoLanguages.Find(resource.OriginalLanguage?.ToLowerInvariant() ?? string.Empty)?.Language ?? Language.English;
            movie.Genres = resource.Genres?.Select(genre => genre.Name).ToList() ?? new List<string>();
            movie.Ratings = new Ratings { Votes = resource.VoteCount, Value = resource.VoteAverage };
            movie.Status = resource.Status == "Released" || movie.FirstAired < DateTime.UtcNow ? SeriesStatusType.Ended : SeriesStatusType.Upcoming;
            AddImage(movie, resource.PosterPath, MediaCoverTypes.Poster);
            AddImage(movie, resource.BackdropPath, MediaCoverTypes.Fanart);
            return movie;
        }

        private static void AddImage(Series movie, string path, MediaCoverTypes type)
        {
            if (path?.StartsWith('/') == true && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains("..", StringComparison.Ordinal))
            {
                movie.Images.Add(new MediaCover.MediaCover(type, "https://image.tmdb.org/t/p/original" + path));
            }
        }

        private static DateTime? Date(string value)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
        }

        private T Get<T>(string path, string key = null, string value = null)
            where T : new()
        {
            if (_config.TmdbApiKey.IsNullOrWhiteSpace())
            {
                throw new TmdbApiKeyMissingException();
            }

            var builder = new HttpRequestBuilder("https://api.themoviedb.org/3" + path).AddQueryParam("api_key", _config.TmdbApiKey);
            if (key != null)
            {
                builder.AddQueryParam(key, value);
            }

            var request = builder.Build();
            request.AllowAutoRedirect = false;
            request.SuppressHttpError = true;
            request.LogHttpError = false;
            request.LogResponseContent = false;
            request.RequestTimeout = TimeSpan.FromSeconds(15);
            request.RateLimit = TimeSpan.FromMilliseconds(250);
            request.RateLimitKey = "api.themoviedb.org";
            var response = _httpClient.Get<T>(request);
            if (response.HasHttpError || response.HasHttpRedirect || response.ResponseData?.Length is not > 0 or > 4194304)
            {
                throw new MovieMetadataException("TMDB movie lookup failed; check the configured key and provider availability.");
            }

            try
            {
                return response.Resource ?? throw new MovieMetadataException("TMDB returned incomplete movie metadata.");
            }
            catch (JsonException)
            {
                // Deserialization errors can contain raw response data and credentials.
                throw new MovieMetadataException("TMDB returned malformed movie metadata; existing metadata is retained.");
            }
        }
    }
}
