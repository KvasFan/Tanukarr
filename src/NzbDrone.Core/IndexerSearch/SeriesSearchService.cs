using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch
{
    public class SeriesSearchService : IExecute<SeriesSearchCommand>
    {
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly Logger _logger;
        private readonly IQueueService _queueService;

        public SeriesSearchService(ISeriesService seriesService,
                                   IEpisodeService episodeService,
                                   ISearchForReleases releaseSearchService,
                                   IProcessDownloadDecisions processDownloadDecisions,
                                   Logger logger,
                                   IQueueService queueService)
        {
            _seriesService = seriesService;
            _episodeService = episodeService;
            _releaseSearchService = releaseSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _logger = logger;
            _queueService = queueService;
        }

        public void Execute(SeriesSearchCommand message)
        {
            var series = _seriesService.GetSeries(message.SeriesId);
            var downloadedCount = 0;
            var userInvokedSearch = message.Trigger == CommandTrigger.Manual;
            var profile = series.QualityProfile.Value;
            var grabbedEpisodeIds = new HashSet<int>();

            HashSet<int> CoveredEpisodeIds()
            {
                var covered = new HashSet<int>(grabbedEpisodeIds);
                covered.UnionWith(_queueService.GetQueue()
                    .Where(q => q.RemoteEpisode?.Series?.Id == series.Id && ActiveMultiSeasonCoverage.IsProtected(q))
                    .SelectMany(q => q.RemoteEpisode.Episodes)
                    .Select(e => e.Id));
                return covered;
            }

            if (series.Seasons.None(s => s.Monitored))
            {
                _logger.Debug("No seasons of {0} are monitored, searching for all monitored episodes", series.Title);

                var episodes = _episodeService.GetEpisodeBySeries(series.Id)
                    .Where(e => e.Monitored &&
                                !e.HasFile &&
                                e.AirDateUtc.HasValue &&
                                e.AirDateUtc.Value.Before(DateTime.UtcNow))
                    .ToList();

                foreach (var episode in episodes)
                {
                    if (CoveredEpisodeIds().Contains(episode.Id))
                    {
                        continue;
                    }

                    var decisions = _releaseSearchService.EpisodeSearch(episode, userInvokedSearch, false).GetAwaiter().GetResult();

                    // Keep successful grabs across season/episode batches, even before the queue refreshes.
                    decisions = decisions.Where(d => !d.RemoteEpisode.Episodes.Any(e => grabbedEpisodeIds.Contains(e.Id))).ToList();
                    var processDecisions = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();
                    downloadedCount += processDecisions.Grabbed.Count;
                    grabbedEpisodeIds.UnionWith(processDecisions.Grabbed.SelectMany(d => d.RemoteEpisode.Episodes).Select(e => e.Id));
                }
            }
            else
            {
                var monitoredSeasons = series.Seasons.Where(s => s.Monitored && s.SeasonNumber > 0)
                    .Select(s => s.SeasonNumber).ToHashSet();
                var initialCoverage = CoveredEpisodeIds();
                var incompleteSeasons = _episodeService.GetEpisodeBySeries(series.Id)
                    .Where(e => monitoredSeasons.Contains(e.SeasonNumber) && e.Monitored &&
                                !e.HasFile && e.AirDateUtc.HasValue && e.AirDateUtc.Value.Before(DateTime.UtcNow) &&
                                !initialCoverage.Contains(e.Id))
                    .Select(e => e.SeasonNumber).Distinct().Count();

                if (incompleteSeasons > 1)
                {
                    try
                    {
                        var completeDecisions = _releaseSearchService.CompleteSeriesSearch(series.Id, userInvokedSearch, false).GetAwaiter().GetResult();
                        if (completeDecisions.Any())
                        {
                            var completeResults = _processDownloadDecisions.ProcessDecisions(completeDecisions).GetAwaiter().GetResult();
                            downloadedCount += completeResults.Grabbed.Count;
                            grabbedEpisodeIds.UnionWith(completeResults.Grabbed.SelectMany(d => d.RemoteEpisode.Episodes).Select(e => e.Id));
                        }
                    }
                    catch (SearchFailedException ex)
                    {
                        _logger.Warn(ex, "Complete-series search unavailable for {0}; continuing with season searches", series.Title);
                    }
                }

                foreach (var season in series.Seasons.OrderBy(s => s.SeasonNumber))
                {
                    if (!season.Monitored)
                    {
                        _logger.Debug("Season {0} of {1} is not monitored, skipping search", season.SeasonNumber, series.Title);
                        continue;
                    }

                    var seasonEpisodes = _episodeService.GetEpisodeBySeries(series.Id)
                        .Where(e => e.SeasonNumber == season.SeasonNumber && e.Monitored &&
                                    (profile.UpgradeAllowed || !e.HasFile))
                        .ToList();

                    // Search Monitored fills gaps; upgrades remain allowed only within an incomplete season.
                    var missingEpisodes = seasonEpisodes
                        .Where(e => !e.HasFile && e.AirDateUtc.HasValue && e.AirDateUtc.Value.Before(DateTime.UtcNow))
                        .ToList();
                    if (!missingEpisodes.Any())
                    {
                        _logger.Debug("Season {0} of {1} has no missing aired monitored episodes, skipping search", season.SeasonNumber, series.Title);
                        continue;
                    }

                    var covered = CoveredEpisodeIds();
                    if (missingEpisodes.All(e => covered.Contains(e.Id)))
                    {
                        _logger.Debug("Season {0} of {1} is already covered by downloads, skipping search", season.SeasonNumber, series.Title);
                        continue;
                    }

                    if (series.SeriesType == SeriesTypes.Anime)
                    {
                        var airedEpisodes = seasonEpisodes
                            .Where(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value.Before(DateTime.UtcNow))
                            .ToList();
                        if (!airedEpisodes.Any())
                        {
                            continue;
                        }

                        var packDecisions = _releaseSearchService.AnimeSeasonPackSearch(series, airedEpisodes, true, userInvokedSearch, false).GetAwaiter().GetResult()
                            .Where(d => d.RemoteEpisode.ParsedEpisodeInfo?.FullSeason == true &&
                                        !d.RemoteEpisode.Episodes.Any(e => grabbedEpisodeIds.Contains(e.Id)))
                            .ToList();
                        var packs = _processDownloadDecisions.ProcessDecisions(packDecisions).GetAwaiter().GetResult();
                        downloadedCount += packs.Grabbed.Count;
                        grabbedEpisodeIds.UnionWith(packs.Grabbed.SelectMany(d => d.RemoteEpisode.Episodes).Select(e => e.Id));

                        foreach (var episode in airedEpisodes)
                        {
                            if (CoveredEpisodeIds().Contains(episode.Id))
                            {
                                continue;
                            }

                            var episodeDecisions = _releaseSearchService.EpisodeSearch(episode, userInvokedSearch, false).GetAwaiter().GetResult()
                                .Where(d => !d.RemoteEpisode.Episodes.Any(e => grabbedEpisodeIds.Contains(e.Id)))
                                .ToList();
                            var results = _processDownloadDecisions.ProcessDecisions(episodeDecisions).GetAwaiter().GetResult();
                            downloadedCount += results.Grabbed.Count;
                            grabbedEpisodeIds.UnionWith(results.Grabbed.SelectMany(d => d.RemoteEpisode.Episodes).Select(e => e.Id));
                        }

                        continue;
                    }

                    var decisions = _releaseSearchService.SeasonSearch(message.SeriesId, season.SeasonNumber, !profile.UpgradeAllowed, true, userInvokedSearch, false).GetAwaiter().GetResult();

                    // Keep successful grabs across season/episode batches, even before the queue refreshes.
                    decisions = decisions.Where(d => !d.RemoteEpisode.Episodes.Any(e => grabbedEpisodeIds.Contains(e.Id))).ToList();
                    var processDecisions = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();
                    downloadedCount += processDecisions.Grabbed.Count;
                    grabbedEpisodeIds.UnionWith(processDecisions.Grabbed.SelectMany(d => d.RemoteEpisode.Episodes).Select(e => e.Id));
                }
            }

            _logger.ProgressInfo("Series search completed. {0} reports downloaded.", downloadedCount);
        }
    }
}
