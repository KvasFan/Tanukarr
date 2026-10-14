using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.ImportLists.Progress
{
    public interface IImportListProgressService
    {
        void SyncProgress(int seriesId, IReadOnlyCollection<int> watchedEpisodeIds, Func<bool> canDispatch);
    }

    public interface IImportListProgressClock
    {
        DateTime UtcNow { get; }
    }

    public class ImportListProgressClock : IImportListProgressClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public class ImportListProgressService : IImportListProgressService,
        IHandleAsync<ProviderDeletedEvent<IImportList>>, IHandleAsync<SeriesDeletedEvent>
    {
        private static readonly object QueueLock = new();
        private readonly ISeriesService _series;
        private readonly IEpisodeService _episodes;
        private readonly IImportListFactory _lists;
        private readonly IImportListItemService _items;
        private readonly IImportListProgressStateRepository _states;
        private readonly IEnumerable<IImportListProgressWriter> _writers;
        private readonly IImportListProgressClock _clock;
        private readonly Logger _logger;
        private DateTime _dispatchWindow;
        private int _dispatches;

        public ImportListProgressService(ISeriesService series,
            IEpisodeService episodes,
            IImportListFactory lists,
            IImportListItemService items,
            IImportListProgressStateRepository states,
            IEnumerable<IImportListProgressWriter> writers,
            IImportListProgressClock clock,
            Logger logger)
        {
            _series = series;
            _episodes = episodes;
            _lists = lists;
            _items = items;
            _states = states;
            _writers = writers;
            _clock = clock;
            _logger = logger;
        }

        public void SyncProgress(int seriesId, IReadOnlyCollection<int> watchedEpisodeIds, Func<bool> canDispatch)
        {
            lock (QueueLock)
            {
                var series = _series.GetSeries(seriesId);
                var episodes = _episodes.GetEpisodeBySeries(seriesId);
                var progress = ImportListProgressGuard.ContiguousProgress(episodes, watchedEpisodeIds, series.MetadataSource == MetadataSourceType.Tvdb);
                if (progress == 0)
                {
                    return;
                }

                var watchedIds = watchedEpisodeIds.ToHashSet();
                var sources = episodes.Where(episode => watchedIds.Contains(episode.Id))
                    .Select(episode => episode.WatchedSource).Distinct(StringComparer.Ordinal).ToList();
                if (sources.Count != 1 || string.IsNullOrWhiteSpace(sources[0]))
                {
                    return;
                }

                var lists = _lists.All().Where(list => list.EnableAutomaticAdd).ToList();
                var memberships = _items.GetAllForLists(lists.Select(list => list.Id).ToList());
                foreach (var definition in lists)
                {
                    foreach (var writer in _writers)
                    {
                        var target = memberships.Where(item => item.ImportListId == definition.Id)
                            .Select(item => writer.TryGetTarget(definition, series, item, out var result) ? result : null)
                            .FirstOrDefault(result => result != null);
                        if (target == null)
                        {
                            continue;
                        }

                        target.NativeEpisodes = episodes.Where(episode => episode.SeasonNumber > 0)
                            .OrderBy(episode => episode.SeasonNumber).ThenBy(episode => episode.EpisodeNumber).ToList();

                        Dispatch(definition, seriesId, writer, target, progress, sources[0], canDispatch);
                    }
                }
            }
        }

        private void Dispatch(ImportListDefinition definition, int seriesId, IImportListProgressWriter writer, ImportListProgressTarget target, int progress, string watchedSource, Func<bool> canDispatch)
        {
            var now = _clock.UtcNow;
            var stateFingerprint = ImportListProgressGuard.Fingerprint(target.Fingerprint, watchedSource);
            var state = _states.ForListSeries(definition.Id, seriesId) ?? new ImportListProgressState
            {
                ImportListId = definition.Id,
                SeriesId = seriesId,
                TargetFingerprint = stateFingerprint,
                NextAttemptUtc = now
            };
            if (state.TargetFingerprint != stateFingerprint || (state.AcknowledgedProgress > 0 &&
                state.AcknowledgedIdentityFingerprint != IdentityFingerprint(target, state.AcknowledgedProgress)))
            {
                state.TargetFingerprint = stateFingerprint;
                state.PendingProgress = 0;
                state.AcknowledgedProgress = 0;
                state.AcknowledgedIdentityFingerprint = string.Empty;
                state.FailureCount = 0;
                state.NextAttemptUtc = now;
            }

            state.PendingProgress = Math.Max(state.PendingProgress, progress);
            Save(state);
            if (state.AcknowledgedProgress >= state.PendingProgress || state.NextAttemptUtc > now)
            {
                return;
            }

            // A large existing library is drained over successive polls. Every
            // attempt is persisted before I/O, including across app restarts.
            if (now >= _dispatchWindow.AddMinutes(1))
            {
                _dispatchWindow = now;
                _dispatches = 0;
            }

            if (_dispatches >= 10)
            {
                return;
            }

            // A settings save can complete while an earlier title/list operation
            // is doing HTTP I/O. Recheck both authorities at every new target;
            // retained pending work cannot authorize itself after an opt-out.
            if (canDispatch == null || !canDispatch())
            {
                return;
            }

            var currentDefinition = _lists.Find(definition.Id);
            if (currentDefinition == null || !currentDefinition.EnableAutomaticAdd ||
                currentDefinition.Implementation != definition.Implementation)
            {
                return;
            }

            var currentSeries = _series.GetSeries(seriesId);
            var currentMemberships = _items.GetAllForLists(new List<int> { definition.Id });
            var currentTarget = currentMemberships.Where(item => item.ImportListId == definition.Id)
                .Select(item => writer.TryGetTarget(currentDefinition, currentSeries, item, out var result) ? result : null)
                .FirstOrDefault(result => result != null && result.Fingerprint == target.Fingerprint);
            if (currentTarget == null || !canDispatch())
            {
                return;
            }

            _dispatches++;
            target.AcknowledgedProgress = state.AcknowledgedProgress;
            state.LastAttemptUtc = now;
            state.NextAttemptUtc = now.AddMinutes(5);
            Save(state);
            try
            {
                var confirmed = writer.Advance(currentDefinition, target, state.PendingProgress);
                if (confirmed <= 0)
                {
                    throw new ImportListProgressUnavailableException();
                }

                state.AcknowledgedProgress = Math.Max(state.AcknowledgedProgress, confirmed);
                state.AcknowledgedIdentityFingerprint = IdentityFingerprint(target, state.AcknowledgedProgress);
                state.FailureCount = 0;
                state.NextAttemptUtc = confirmed >= state.PendingProgress ? now : now.AddMinutes(1);
                _logger.Debug("Watched progress confirmed for import list {0}, series {1}: {2}", definition.Id, seriesId, confirmed);
            }
            catch (Exception exception)
            {
                state.FailureCount = Math.Min(state.FailureCount + 1, 16);
                var wait = TimeSpan.FromMinutes(Math.Min(360, 5 * Math.Pow(2, state.FailureCount - 1)));
                if (exception is TooManyRequestsException limited && limited.RetryAfter > wait)
                {
                    wait = limited.RetryAfter < TimeSpan.FromDays(1) ? limited.RetryAfter : TimeSpan.FromDays(1);
                }

                if (exception is ImportListProgressUnavailableException)
                {
                    wait = TimeSpan.FromDays(1);
                }

                state.NextAttemptUtc = now.Add(wait);
                _logger.Warn("Watched progress for import list {0}, series {1} was not confirmed ({2}); retry is queued.", definition.Id, seriesId, exception.GetType().Name);
            }

            Save(state);
        }

        private void Save(ImportListProgressState state)
        {
            if (state.Id == 0)
            {
                _states.Insert(state);
            }
            else
            {
                _states.Update(state);
            }
        }

        private static string IdentityFingerprint(ImportListProgressTarget target, int progress)
        {
            return ImportListProgressGuard.Fingerprint(string.Join(";", target.NativeEpisodes.Take(progress)
                .Select(episode => $"{episode.Id}:{episode.TvdbId}:{episode.SeasonNumber}:{episode.EpisodeNumber}:{episode.ForeignId}")));
        }

        public void HandleAsync(ProviderDeletedEvent<IImportList> message)
        {
            lock (QueueLock)
            {
                _states.DeleteMany(_states.All().Where(state => state.ImportListId == message.ProviderId).ToList());
            }
        }

        public void HandleAsync(SeriesDeletedEvent message)
        {
            lock (QueueLock)
            {
                var ids = message.Series.Select(series => series.Id).ToHashSet();
                _states.DeleteMany(_states.All().Where(state => ids.Contains(state.SeriesId)).ToList());
            }
        }
    }
}
