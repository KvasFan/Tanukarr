using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download
{
    public class MovieGrabContext
    {
        public Series Series { get; set; }
        public ParsedEpisodeInfo ParsedEpisodeInfo { get; set; }
        public List<Series> CollectionMovies { get; set; } = new List<Series>();
    }

    public interface IMovieGrabContextService
    {
        MovieGrabContext GetContext(string downloadId);
        bool AllowsMember(LocalEpisode localEpisode, DownloadClientItem downloadItem);
    }

    public class MovieGrabContextService : IMovieGrabContextService
    {
        private readonly IHistoryService _historyService;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;

        public MovieGrabContextService(IHistoryService historyService, ISeriesService seriesService, IEpisodeService episodeService)
        {
            _historyService = historyService;
            _seriesService = seriesService;
            _episodeService = episodeService;
        }

        public MovieGrabContext GetContext(string downloadId)
        {
            if (string.IsNullOrWhiteSpace(downloadId))
            {
                return null;
            }

            var grabs = (_historyService.FindByDownloadId(downloadId) ?? new List<EpisodeHistory>())
                .Where(history => history.EventType == EpisodeHistoryEventType.Grabbed).ToList();
            if (grabs.Count == 0 || grabs.Select(history => history.SeriesId).Distinct().Count() != 1 ||
                grabs.Select(history => history.SourceTitle).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            {
                return null;
            }

            Series series;
            try
            {
                series = _seriesService.GetSeries(grabs[0].SeriesId);
            }
            catch (ModelNotFoundException)
            {
                return null;
            }

            var ids = grabs.Select(history => history.EpisodeId).Distinct().ToList();
            if (series?.MediaType != MediaType.Movie || ids.Count != 1 || ids[0] <= 0 ||
                !HasNativeFeature(series, ids[0]))
            {
                return null;
            }

            var parsed = MovieReleaseParser.Match(grabs[0].SourceTitle, series);
            var members = new List<Series>();
            if (parsed == null && AnimeGroupingValidator.HasGroup(series))
            {
                members = (_seriesService.GetAllSeries() ?? new List<Series>()).Where(member =>
                    member?.MediaType == MediaType.Movie && member.Monitored && AnimeGroupingValidator.HasGroup(member) &&
                    member.AnimeGroupId == series.AnimeGroupId).ToList();
                if (members.Count(member => member.Id == series.Id) != 1 ||
                    members.Any(member => member.Id <= 0) || members.Select(member => member.Id).Distinct().Count() != members.Count)
                {
                    return null;
                }

                parsed = MovieCollectionPackParser.Match(grabs[0].SourceTitle, new MovieSearchCriteria { Series = series, CollectionMovies = members });
            }

            return parsed == null ? null : new MovieGrabContext { Series = series, ParsedEpisodeInfo = parsed, CollectionMovies = members };
        }

        public bool AllowsMember(LocalEpisode localEpisode, DownloadClientItem downloadItem)
        {
            if (localEpisode?.Series?.MediaType != MediaType.Movie || localEpisode.Episodes?.Count != 1 ||
                localEpisode.Episodes[0] is not { Id: > 0, SeasonNumber: 1, EpisodeNumber: 1 } feature ||
                feature.SeriesId != localEpisode.Series.Id)
            {
                return false;
            }

            var context = GetContext(downloadItem?.DownloadId);
            if (context?.ParsedEpisodeInfo.IsMovieCollectionPack != true ||
                !string.Equals(context.ParsedEpisodeInfo.ReleaseTitle, localEpisode.Release?.Title, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var matched = MovieReleaseParser.MatchUnique(localEpisode.Path, context.CollectionMovies);
            return matched?.Id == localEpisode.Series.Id && HasNativeFeature(matched, feature.Id);
        }

        private bool HasNativeFeature(Series series, int episodeId)
        {
            var episodes = _episodeService.GetEpisodeBySeries(series.Id);
            return episodes?.Count == 1 && episodes[0].Id == episodeId && episodes[0].SeriesId == series.Id &&
                episodes[0].SeasonNumber == 1 && episodes[0].EpisodeNumber == 1;
        }
    }
}
