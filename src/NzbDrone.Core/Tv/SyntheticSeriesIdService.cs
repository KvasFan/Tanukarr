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
    /// Issues placeholder TVDB ids for series that have no entry on TheTVDB.
    /// <para>
    /// TheTVDB's editors sometimes decline to list content separately, folding a recut or a
    /// spin-off into its parent series, while TMDB and AniList list it in its own right.
    /// Such a series has no TVDB id, but one is still needed because the column is uniquely
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

                _logger.Debug("Allocated synthetic TVDB id {0} for a series with no TheTVDB entry", allocated);

                return allocated;
            }
        }
    }
}
