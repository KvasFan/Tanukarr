using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch
{
    public class CompleteSeriesSearchService : IExecute<CompleteSeriesSearchCommand>
    {
        private readonly ISeriesService _seriesService;
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly Logger _logger;

        public CompleteSeriesSearchService(ISeriesService seriesService,
                                           ISearchForReleases releaseSearchService,
                                           IProcessDownloadDecisions processDownloadDecisions,
                                           Logger logger)
        {
            _seriesService = seriesService;
            _releaseSearchService = releaseSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _logger = logger;
        }

        public void Execute(CompleteSeriesSearchCommand message)
        {
            var series = _seriesService.GetSeries(message.SeriesId);
            if (!series.Monitored)
            {
                _logger.ProgressInfo("Complete-series search skipped: {0} is not monitored", series.Title);
                return;
            }

            var decisions = _releaseSearchService.CompleteSeriesSearch(series.Id, message.Trigger == CommandTrigger.Manual, false).GetAwaiter().GetResult();

            foreach (var decision in decisions)
            {
                _logger.Info("Complete-series candidate: {0}: {1}", decision.RemoteEpisode.Release.Title, decision.Approved ? "Approved" : string.Join("; ", decision.Rejections.Select(r => r.Message)));
            }

            if (message.DryRun)
            {
                _logger.ProgressInfo("Complete-series dry run finished for {0}: {1} approved, {2} rejected or delayed; no downloads submitted", series.Title, decisions.Count(d => d.Approved), decisions.Count(d => !d.Approved));
                return;
            }

            // Reuse normal ranking, queue checks, delays, and download-client handling.
            // Complete packs overlap, so ProcessDecisions grabs at most one of them.
            // Do not fall back to season searches when no complete pack qualifies.
            var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();
            _logger.ProgressInfo("Complete-series search finished for {0}: {1} grabbed, {2} pending, {3} rejected", series.Title, processed.Grabbed.Count, processed.Pending.Count, processed.Rejected.Count);
        }
    }
}
