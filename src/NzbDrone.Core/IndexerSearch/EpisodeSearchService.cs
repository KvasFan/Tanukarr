using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch
{
    public class EpisodeSearchService : IExecute<EpisodeSearchCommand>,
                                        IExecute<MissingEpisodeSearchCommand>,
                                        IExecute<CutoffUnmetEpisodeSearchCommand>
    {
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly IEpisodeService _episodeService;
        private readonly IEpisodeCutoffService _episodeCutoffService;
        private readonly IQueueService _queueService;
        private readonly Logger _logger;

        public EpisodeSearchService(ISearchForReleases releaseSearchService,
                                    IProcessDownloadDecisions processDownloadDecisions,
                                    IEpisodeService episodeService,
                                    IEpisodeCutoffService episodeCutoffService,
                                    IQueueService queueService,
                                    Logger logger)
        {
            _releaseSearchService = releaseSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _episodeService = episodeService;
            _episodeCutoffService = episodeCutoffService;
            _queueService = queueService;
            _logger = logger;
        }

        private async Task SearchForBulkEpisodes(List<Episode> episodes, bool monitoredOnly, bool userInvokedSearch, bool missingOnly = false)
        {
            _logger.ProgressInfo("Performing search for {0} episodes", episodes.Count);
            var downloadedCount = 0;
            var groups = new List<EpisodeSearchGroup>();
            var grabbedEpisodeIds = new HashSet<int>();

            // Missing searches try a complete-series pack first for every series missing more than one regular
            // season (after adding a series, or "search all missing"), like SeriesSearchService; full multi-season
            // packs then outrank season packs and episodes, and the per-group refresh below drops every episode a
            // grabbed pack covers.
            if (missingOnly && monitoredOnly)
            {
                foreach (var seriesEpisodes in episodes.GroupBy(e => e.SeriesId))
                {
                    if (seriesEpisodes.Where(e => e.SeasonNumber > 0).Select(e => e.SeasonNumber).Distinct().Count() < 2)
                    {
                        continue;
                    }

                    try
                    {
                        var completeDecisions = await _releaseSearchService.CompleteSeriesSearch(seriesEpisodes.Key, userInvokedSearch, false);
                        if (completeDecisions.Any())
                        {
                            var completeResults = await _processDownloadDecisions.ProcessDecisions(completeDecisions);
                            downloadedCount += completeResults.Grabbed.Count;
                            grabbedEpisodeIds.UnionWith(completeResults.Grabbed.SelectMany(d => d.RemoteEpisode.Episodes).Select(e => e.Id));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Complete-series search unavailable for [{0}]; continuing with season searches", seriesEpisodes.Key);
                    }
                }
            }

            foreach (var series in episodes.GroupBy(e => e.SeriesId))
            {
                foreach (var season in series.Select(e => e).GroupBy(e => e.SeasonNumber))
                {
                    groups.Add(new EpisodeSearchGroup
                    {
                        SeriesId = series.Key,
                        SeasonNumber = season.Key,
                        Episodes = season.ToList()
                    });
                }
            }

            // Regular seasons before specials: specials search one episode at a time and can take many minutes.
            foreach (var group in groups.OrderBy(g => g.SeasonNumber == 0).ThenBy(g => g.Episodes.Min(e => e.LastSearchTime ?? DateTime.MinValue)))
            {
                List<DownloadDecision> decisions;

                var seriesId = group.SeriesId;
                var seasonNumber = group.SeasonNumber;

                // A bulk search can run for many minutes; episodes imported, queued or grabbed since it started must
                // not be searched again with the stale list (they would look missing and pass every upgrade check).
                var queued = GetQueuedEpisodeIds().ToHashSet();
                var groupEpisodes = _episodeService.GetEpisodes(group.Episodes.Select(e => e.Id))
                    .Where(e => !queued.Contains(e.Id) && !grabbedEpisodeIds.Contains(e.Id) && (!missingOnly || !e.HasFile))
                    .ToList();

                if (groupEpisodes.Empty())
                {
                    _logger.Debug("Season {0} of [{1}] no longer needs a search", seasonNumber, seriesId);
                    continue;
                }

                if (groupEpisodes.Count > 1)
                {
                    try
                    {
                        decisions = await _releaseSearchService.SeasonSearch(seriesId, seasonNumber, groupEpisodes, monitoredOnly, userInvokedSearch, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Unable to search for episodes in season {0} of [{1}]", seasonNumber, seriesId);
                        continue;
                    }
                }
                else
                {
                    var episode = groupEpisodes.First();

                    try
                    {
                        decisions = await _releaseSearchService.EpisodeSearch(episode, userInvokedSearch, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Unable to search for episode: [{0}]", episode);
                        continue;
                    }
                }

                decisions = decisions.Where(d => !d.RemoteEpisode.Episodes.Any(e => grabbedEpisodeIds.Contains(e.Id))).ToList();
                var processed = await _processDownloadDecisions.ProcessDecisions(decisions);

                downloadedCount += processed.Grabbed.Count;
                grabbedEpisodeIds.UnionWith(processed.Grabbed.SelectMany(d => d.RemoteEpisode.Episodes).Select(e => e.Id));
            }

            _logger.ProgressInfo("Completed search for {0} episodes. {1} reports downloaded.", episodes.Count, downloadedCount);
        }

        private bool IsMonitored(bool episodeMonitored, bool seriesMonitored)
        {
            return episodeMonitored && seriesMonitored;
        }

        public void Execute(EpisodeSearchCommand message)
        {
            foreach (var episodeId in message.EpisodeIds)
            {
                var decisions = _releaseSearchService.EpisodeSearch(episodeId, message.Trigger == CommandTrigger.Manual, false).GetAwaiter().GetResult();
                var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

                _logger.ProgressInfo("Episode search completed. {0} reports downloaded.", processed.Grabbed.Count);
            }
        }

        public void Execute(MissingEpisodeSearchCommand message)
        {
            var monitored = message.Monitored;
            List<Episode> episodes;

            if (message.SeriesId.HasValue)
            {
                episodes = _episodeService.GetEpisodeBySeries(message.SeriesId.Value)
                                          .Where(e => e.Monitored == monitored &&
                                                 !e.HasFile &&
                                                 e.AirDateUtc.HasValue &&
                                                 e.AirDateUtc.Value.Before(DateTime.UtcNow))
                                          .ToList();
            }
            else
            {
                var pagingSpec = new PagingSpec<Episode>
                                 {
                                     Page = 1,
                                     PageSize = 1000000,
                                     SortDirection = SortDirection.Ascending,
                                     SortKey = "Id"
                                 };

                if (monitored)
                {
                    pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Series.Monitored == true);
                }
                else
                {
                    pagingSpec.FilterExpressions.Add(v => v.Monitored == false || v.Series.Monitored == false);
                }

                if (message.SeriesIds?.Any() == true)
                {
                    pagingSpec.FilterExpressions.Add(e => message.SeriesIds.Contains(e.SeriesId));
                }

                if (message.QualityProfileIds?.Any() == true)
                {
                    pagingSpec.FilterExpressions.Add(e => message.QualityProfileIds.Contains(e.Series.QualityProfileId));
                }

                if (message.SeriesType?.Any() == true)
                {
                    pagingSpec.FilterExpressions.Add(e => message.SeriesType.Contains(e.Series.SeriesType));
                }

                episodes = _episodeService.EpisodesWithoutFiles(pagingSpec, true, message.SeriesTags).Records.ToList();
            }

            var queue = GetQueuedEpisodeIds();
            var missing = episodes.Where(e => !queue.Contains(e.Id)).ToList();

            SearchForBulkEpisodes(missing, monitored, message.Trigger == CommandTrigger.Manual, true).GetAwaiter().GetResult();
        }

        public void Execute(CutoffUnmetEpisodeSearchCommand message)
        {
            var monitored = message.Monitored;

            var pagingSpec = new PagingSpec<Episode>
                             {
                                 Page = 1,
                                 PageSize = 100000,
                                 SortDirection = SortDirection.Ascending,
                                 SortKey = "Id"
                             };

            if (message.SeriesId.HasValue)
            {
                var seriesId = message.SeriesId.Value;
                pagingSpec.FilterExpressions.Add(v => v.SeriesId == seriesId);
            }

            if (monitored)
            {
                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Series.Monitored == true);
            }
            else
            {
                pagingSpec.FilterExpressions.Add(v => v.Monitored == false || v.Series.Monitored == false);
            }

            if (message.SeriesIds?.Any() == true)
            {
                pagingSpec.FilterExpressions.Add(e => message.SeriesIds.Contains(e.SeriesId));
            }

            if (message.QualityProfileIds?.Any() == true)
            {
                pagingSpec.FilterExpressions.Add(e => message.QualityProfileIds.Contains(e.Series.QualityProfileId));
            }

            if (message.SeriesType?.Any() == true)
            {
                pagingSpec.FilterExpressions.Add(e => message.SeriesType.Contains(e.Series.SeriesType));
            }

            var episodes = _episodeCutoffService.EpisodesWhereCutoffUnmet(pagingSpec, message.SeriesTags, message.Quality).Records.ToList();
            var queue = GetQueuedEpisodeIds();
            var cutoffUnmet = episodes.Where(e => !queue.Contains(e.Id)).ToList();

            SearchForBulkEpisodes(cutoffUnmet, monitored, message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }

        private List<int> GetQueuedEpisodeIds()
        {
            return _queueService.GetQueue()
                .Where(q => q.Episodes.Any())
                .SelectMany(q => q.Episodes.Select(e => e.Id))
                .ToList();
        }
    }
}
