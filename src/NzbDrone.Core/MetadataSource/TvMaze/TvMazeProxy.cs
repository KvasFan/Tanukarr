using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.TvMaze.Resource;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.TvMaze
{
    // TVmaze public API: https://www.tvmaze.com/api (CC BY-SA).
    public class TvMazeProxy : IMetadataProvider
    {
        private const string BaseUrl = "https://api.tvmaze.com";
        private const int MaxEpisodes = 20000;
        private static readonly Regex HtmlTags = new Regex("<[^>]*>", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        private readonly IHttpClient _httpClient;
        private readonly ITvMazeRateLimiter _rateLimiter;

        public TvMazeProxy(IHttpClient httpClient, ITvMazeRateLimiter rateLimiter)
        {
            _httpClient = httpClient;
            _rateLimiter = rateLimiter;
        }

        public MetadataSourceType Source => MetadataSourceType.TvMaze;

        public List<Series> SearchForNewSeries(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return new List<Series>();
            }

            if (title.StartsWith("tvmaze:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(title[7..], out var id) || id <= 0)
                {
                    return new List<Series>();
                }

                var show = Get<TvMazeShowResource>("/shows/" + id);
                return ValidShow(show) && show.Id == id ? new List<Series> { MapSeries(show) } : new List<Series>();
            }

            var results = Get<List<TvMazeSearchResource>>("/search/shows?q=" + Uri.EscapeDataString(title.Trim()));
            if (results == null)
            {
                throw new TvMazeException("TVmaze returned an incomplete search response.");
            }

            return results.Where(result => ValidShow(result?.Show)).GroupBy(result => result.Show.Id)
                .Take(20).Select(result => MapSeries(result.First().Show)).ToList();
        }

        public Tuple<Series, List<Episode>> GetSeriesInfo(Series series)
        {
            if (!int.TryParse(series.ForeignId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                throw new SeriesNotFoundException(0, "A positive native TVmaze ID is required.");
            }

            var show = Get<TvMazeShowResource>("/shows/" + id);
            if (!ValidShow(show) || show.Id != id)
            {
                throw new SeriesNotFoundException(0, "The requested TVmaze show was not found.");
            }

            var resources = Get<List<TvMazeEpisodeResource>>($"/shows/{id}/episodes?specials=1");
            if (resources == null || resources.Count > MaxEpisodes || resources.Any(episode => episode == null || episode.Id <= 0 ||
                    !episode.Season.HasValue || episode.Season < 0 || (episode.Number.HasValue && episode.Number <= 0) ||
                    (!episode.Number.HasValue && episode.Type != "significant_special" && episode.Type != "insignificant_special")) ||
                resources.GroupBy(episode => episode.Id).Any(group => group.Count() > 1) ||
                resources.Where(episode => episode.Number.HasValue).GroupBy(episode => (episode.Season, episode.Number)).Any(group => group.Count() > 1) ||
                (resources.Count == 0 && series.Seasons?.Any(season => season.SeasonNumber > 0) == true))
            {
                throw new TvMazeException("TVmaze returned an incomplete or conflicting episode list; existing episodes were retained.");
            }

            var metadata = MapSeries(show);
            var specials = resources.Where(episode => !episode.Number.HasValue).OrderBy(episode => episode.Id)
                .Select((episode, index) => (episode.Id, Number: index + 1)).ToDictionary(episode => episode.Id, episode => episode.Number);
            var episodes = resources.Select(resource => new Episode
            {
                ForeignId = resource.Id.ToString(CultureInfo.InvariantCulture),
                SeasonNumber = resource.Number.HasValue ? resource.Season.Value : 0,
                EpisodeNumber = resource.Number ?? specials[resource.Id],
                Title = resource.Name.IsNotNullOrWhiteSpace() ? resource.Name : "TBA",
                Overview = PlainText(resource.Summary),
                AirDate = resource.Airdate,
                AirDateUtc = ParseDate(resource.Airstamp),
                Runtime = resource.Runtime ?? metadata.Runtime,
                Monitored = true,
                Ratings = new Ratings()
            }).OrderBy(episode => episode.SeasonNumber).ThenBy(episode => episode.EpisodeNumber).ToList();
            metadata.Seasons = episodes.Select(episode => episode.SeasonNumber).Distinct()
                .Select(number => new Season { SeasonNumber = number, Monitored = true }).ToList();
            metadata.SeasonTypes.Add(new SeasonType
            {
                Name = "Official", Type = SeasonType.Official,
                SeasonNumbers = metadata.Seasons.Select(season => season.SeasonNumber).ToList(), EpisodeCount = episodes.Count
            });
            return Tuple.Create(metadata, episodes);
        }

        private static bool ValidShow(TvMazeShowResource show)
        {
            return show?.Id > 0 && show.Name.IsNotNullOrWhiteSpace();
        }

        private static Series MapSeries(TvMazeShowResource show)
        {
            var firstAired = ParseDate(show.Premiered);
            var series = new Series
            {
                MetadataSource = MetadataSourceType.TvMaze,
                ForeignId = show.Id.ToString(CultureInfo.InvariantCulture),
                TvMazeId = show.Id,
                TvdbId = Math.Max(show.Externals?.Tvdb ?? 0, 0),
                TvRageId = Math.Max(show.Externals?.TvRage ?? 0, 0),
                ImdbId = show.Externals?.Imdb.IsNotNullOrWhiteSpace() == true ? Parser.Parser.NormalizeImdbId(show.Externals.Imdb) : null,
                Title = show.Name,
                TitleSlug = "tvmaze-" + show.Id,
                CleanTitle = Parser.Parser.CleanSeriesTitle(show.Name),
                SortTitle = SeriesTitleNormalizer.Normalize(show.Name, 0),
                Overview = PlainText(show.Summary),
                FirstAired = firstAired,
                LastAired = ParseDate(show.Ended),
                Year = firstAired?.Year ?? 0,
                SeasonType = SeasonType.Official,
                SeriesType = SeriesTypes.Standard,
                Status = show.Status == "Ended" ? SeriesStatusType.Ended : show.Status == "In Development" ? SeriesStatusType.Upcoming : SeriesStatusType.Continuing,
                Genres = show.Genres ?? new List<string>(),
                Network = show.Network?.Name ?? show.WebChannel?.Name,
                Runtime = show.AverageRuntime ?? show.Runtime ?? 0,
                OriginalLanguage = Language.All.FirstOrDefault(language => language.Name.Equals(show.Language, StringComparison.OrdinalIgnoreCase)) ?? Language.Unknown,
                Ratings = new Ratings { Value = show.Rating?.Average ?? 0 },
                Monitored = true
            };
            var poster = show.Image?.Original ?? show.Image?.Medium;
            if (Uri.TryCreate(poster, UriKind.Absolute, out var image) && image.Scheme == Uri.UriSchemeHttps && image.Host == "static.tvmaze.com")
            {
                series.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, poster));
            }

            return series;
        }

        private static DateTime? ParseDate(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date) ? date : null;
        }

        private static string PlainText(string value)
        {
            return value == null ? null : WebUtility.HtmlDecode(HtmlTags.Replace(value, string.Empty)).Trim();
        }

        private T Get<T>(string path)
            where T : new()
        {
            var request = new HttpRequestBuilder(BaseUrl + path).Build();
            request.SuppressHttpError = true;
            request.RequestTimeout = TimeSpan.FromSeconds(15);
            request.LogResponseContent = false;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (!_rateLimiter.TryWaitForSlot(TimeSpan.FromSeconds(10)))
                {
                    throw new RequestLimitReachedException("TVmaze is cooling down. Try again later.");
                }

                var response = _httpClient.Get<T>(request);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var limited = new TooManyRequestsException(request, response);
                    _rateLimiter.BackOff(limited.RetryAfter > TimeSpan.Zero ? limited.RetryAfter : TimeSpan.FromSeconds(5));
                    if (attempt == 0)
                    {
                        continue;
                    }
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return default;
                }

                if (response.HasHttpError)
                {
                    throw new HttpException(request, response);
                }

                return response.Resource;
            }

            throw new RequestLimitReachedException("TVmaze request limit reached. Try again later.");
        }
    }
}
