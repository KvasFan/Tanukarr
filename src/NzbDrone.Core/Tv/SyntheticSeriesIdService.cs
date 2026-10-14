using System;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Tv
{
    public interface IAllocateSyntheticSeriesIds
    {
        int AllocateTvdbId();
    }

    /// <summary>
    /// Issues internal placeholder TVDB ids when no verified positive TVDB id is supplied.
    /// <para>
    /// A selected metadata provider can identify a series by its own native catalogue id
    /// without supplying a verified TheTVDB mapping. An internal value is still needed
    /// because the column is uniquely
    /// indexed and <see cref="ISeriesRepository.FindByTvdbId"/> expects at most one match,
    /// so zero cannot be used more than once.
    /// </para>
    /// <para>
    /// Negative values solve both problems. Each is distinct, so the unique index and the
    /// lookup are satisfied, and the guards throughout the codebase already test for a
    /// positive id before using it, so every TVDB-specific behaviour, including id-based
    /// indexer search and XEM scene mappings, skips itself without further change.
    /// </para>
    /// </summary>
    public class SyntheticSeriesIdService : IAllocateSyntheticSeriesIds
    {
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;
        private readonly object _lock = new object();
        private int _lastAllocatedTvdbId;

        public SyntheticSeriesIdService(ISeriesService seriesService, Logger logger)
        {
            _seriesService = seriesService;
            _logger = logger;
        }

        public int AllocateTvdbId()
        {
            // Reserve ids before insertion: bulk additions prepare several series before
            // writing any of them, and concurrent additions share this service instance.
            lock (_lock)
            {
                var existing = _seriesService.AllSeriesTvdbIds().Values;
                var lowest = Math.Min(_lastAllocatedTvdbId, existing.Any() ? existing.Min() : 0);
                var allocated = checked(lowest - 1);
                _lastAllocatedTvdbId = allocated;

                _logger.Debug("Allocated internal TVDB placeholder {0}; the selected metadata provider did not supply a verified positive TheTVDB ID", allocated);

                return allocated;
            }
        }
    }
}
