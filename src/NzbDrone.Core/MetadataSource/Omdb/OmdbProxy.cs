using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.Omdb
{
    public class OmdbProxy : IMetadataProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly IConfigService _config;

        public OmdbProxy(IHttpClient httpClient, IConfigService config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        public MetadataSourceType Source => MetadataSourceType.Omdb;

        public Tuple<Series, List<Episode>> GetSeriesInfo(Series series)
        {
            var id = series.ForeignId?.ToLowerInvariant();
            if (!ValidId(id))
            {
                throw new MovieMetadataException("Choose a valid native IMDb title ID for OMDb.");
            }

            var resource = Get("i", id);
            if (resource.Response != "True" || resource.ImdbId != id || resource.Type != "movie")
            {
                throw new MovieMetadataException("OMDb did not return the requested movie; existing metadata is retained.");
            }

            return MovieMetadata.WithFeature(Map(resource));
        }

        public List<Series> SearchForNewSeries(string title)
        {
            if (title.IsNullOrWhiteSpace() || title.Length > 2048 || _config.OmdbApiKey.IsNullOrWhiteSpace())
            {
                return new List<Series>();
            }

            title = title.Trim();
            var match = Regex.Match(title, @"\A(?:imdb:|omdb:)?(?<id>tt[0-9]{7,12})\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success)
            {
                return new List<Series> { GetSeriesInfo(new Series { ForeignId = match.Groups["id"].Value.ToLowerInvariant() }).Item1 };
            }

            var result = Get("s", title);
            if (result.Response != "True")
            {
                return new List<Series>();
            }

            return (result.Search ?? new List<OmdbResource>()).Where(resource => resource.Type == "movie" && ValidId(resource.ImdbId) && resource.Title.IsNotNullOrWhiteSpace())
                .GroupBy(resource => resource.ImdbId, StringComparer.Ordinal).Select(group => Map(group.First())).ToList();
        }

        private Series Map(OmdbResource resource)
        {
            if (!ValidId(resource.ImdbId) || resource.Title.IsNullOrWhiteSpace())
            {
                throw new MovieMetadataException("OMDb movie response has no native identity or title.");
            }

            var movie = MovieMetadata.Create(Source, resource.ImdbId, resource.Title);
            movie.ImdbId = resource.ImdbId;
            movie.Overview = Known(resource.Plot);
            movie.Certification = Known(resource.Rated);
            movie.Genres = (Known(resource.Genre) ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            movie.Year = int.TryParse(resource.Year, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year >= 1878 ? year : 0;
            var duration = Regex.Match(resource.Runtime?.Length <= 32 ? resource.Runtime : string.Empty, @"\A(?<minutes>[0-9]{1,4}) min\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            movie.Runtime = duration.Success ? int.Parse(duration.Groups["minutes"].Value, CultureInfo.InvariantCulture) : 0;
            if (DateTime.TryParseExact(resource.Released, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var released))
            {
                movie.FirstAired = released;
                movie.LastAired = released;
            }

            movie.Ratings = new Ratings
            {
                Value = decimal.TryParse(resource.ImdbRating, NumberStyles.Number, CultureInfo.InvariantCulture, out var rating) ? Math.Clamp(rating, 0, 10) : 0,
                Votes = int.TryParse(resource.ImdbVotes, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var votes) ? Math.Max(votes, 0) : 0
            };
            if (Uri.TryCreate(resource.Poster, UriKind.Absolute, out var poster) && poster.Scheme == Uri.UriSchemeHttps && poster.Port == 443 &&
                poster.UserInfo.Length == 0 && poster.Host is "m.media-amazon.com" or "ia.media-imdb.com")
            {
                movie.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, poster.AbsoluteUri));
            }

            return movie;
        }

        private static string Known(string value) => value == "N/A" ? null : value;

        private static bool ValidId(string id) => id?.Length <= 14 && Regex.IsMatch(id, @"\Att[0-9]{7,12}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private OmdbResource Get(string name, string value)
        {
            if (_config.OmdbApiKey.IsNullOrWhiteSpace())
            {
                throw new MovieMetadataException("Configure your own OMDb API key before adding or refreshing an OMDb movie.");
            }

            var request = new HttpRequestBuilder("https://www.omdbapi.com/")
                .AddQueryParam("apikey", _config.OmdbApiKey).AddQueryParam(name, value)
                .AddQueryParam("type", "movie").AddQueryParam("plot", "full").Build();
            request.RequestTimeout = TimeSpan.FromSeconds(15);
            request.RateLimit = TimeSpan.FromMilliseconds(1000);
            request.RateLimitKey = "www.omdbapi.com";
            request.AllowAutoRedirect = false;
            request.SuppressHttpError = true;
            request.LogHttpError = false;
            request.LogResponseContent = false;
            var response = _httpClient.Get<OmdbResource>(request);
            if (response.HasHttpError || response.HasHttpRedirect || response.ResponseData?.Length is not > 0 or > 4194304)
            {
                throw new MovieMetadataException("OMDb lookup failed; check the configured key, quota and provider availability.");
            }

            try
            {
                return response.Resource ?? throw new MovieMetadataException("OMDb returned incomplete movie metadata.");
            }
            catch (JsonException)
            {
                throw new MovieMetadataException("OMDb returned malformed movie metadata; existing metadata is retained.");
            }
        }
    }
}
