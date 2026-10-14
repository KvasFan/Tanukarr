using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Progress;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public interface IJellyfinWatchedService
    {
        void Sync(bool force = false);
    }

    public class JellyfinWatchedService : IJellyfinWatchedService,
        IExecute<SyncJellyfinWatchedCommand>,
        IHandle<ProviderDeletedEvent<INotification>>
    {
        private readonly INotificationRepository _notificationRepository;
        private readonly IJellyfinWatchStateRepository _stateRepository;
        private readonly IJellyfinWatchProxy _proxy;
        private readonly IEpisodeRepository _episodeRepository;
        private readonly IMediaFileRepository _fileRepository;
        private readonly ISeriesRepository _seriesRepository;
        private readonly IJellyfinRefreshClock _clock;
        private readonly IImportListProgressService _progressService;
        private readonly Logger _logger;
        private readonly object _syncLock = new object();

        public JellyfinWatchedService(INotificationRepository notificationRepository,
            IJellyfinWatchStateRepository stateRepository,
            IJellyfinWatchProxy proxy,
            IEpisodeRepository episodeRepository,
            IMediaFileRepository fileRepository,
            ISeriesRepository seriesRepository,
            IJellyfinRefreshClock clock,
            IImportListProgressService progressService,
            Logger logger)
        {
            _notificationRepository = notificationRepository;
            _stateRepository = stateRepository;
            _proxy = proxy;
            _episodeRepository = episodeRepository;
            _fileRepository = fileRepository;
            _seriesRepository = seriesRepository;
            _clock = clock;
            _progressService = progressService;
            _logger = logger;
        }

        public void Sync(bool force = false)
        {
            lock (_syncLock)
            {
                var definitions = _notificationRepository.All().Where(IsActive).ToList();
                if (definitions.Count > 1)
                {
                    _logger.Warn("Jellyfin watched sync requires exactly one enabled connection; previous watched state is retained");
                    return;
                }

                if (definitions.Count == 0)
                {
                    return;
                }

                var definition = definitions[0];
                var notificationId = definition.Id;
                var settings = (JellyfinSettings)definition.Settings;
                var fingerprint = Fingerprint(notificationId, settings);
                var expectedUnmonitor = settings.UnmonitorWatchedEpisodes;
                var expectedRemonitor = settings.RemonitorUnwatchedEpisodes;
                var expectedSyncProgress = settings.SyncListProgress;
                var expectedTags = new HashSet<int>(definition.Tags ?? new HashSet<int>());
                var state = _stateRepository.ForNotification(notificationId);
                if (!force && state?.TargetFingerprint == fingerprint &&
                    (_clock.UtcNow - state.LastSyncUtc).TotalMinutes < settings.WatchedSyncIntervalMinutes)
                {
                    return;
                }

                try
                {
                    var items = _proxy.GetSnapshot(settings);

                    // A settings save/deletion during the HTTP request cannot apply an
                    // old user's data or authorize a stale monitoring/list-write policy.
                    var current = _notificationRepository.Find(notificationId);
                    if (!IsActive(current) || Fingerprint(current.Id, (JellyfinSettings)current.Settings) != fingerprint ||
                        !SamePolicy((JellyfinSettings)current.Settings, expectedUnmonitor, expectedRemonitor, expectedSyncProgress) ||
                        !SameTags(current.Tags, expectedTags) ||
                        _notificationRepository.All().Count(IsActive) != 1)
                    {
                        return;
                    }

                    var applyInitialUnmonitor = settings.UnmonitorWatchedEpisodes &&
                        (state?.TargetFingerprint != fingerprint || !state.UnmonitorWatchedEpisodes);
                    var matchedSeries = ApplySnapshot(items, settings, fingerprint, current.Tags, applyInitialUnmonitor);
                    state ??= new JellyfinWatchState { NotificationId = notificationId };
                    state.TargetFingerprint = fingerprint;
                    state.LastSyncUtc = _clock.UtcNow;
                    state.UnmonitorWatchedEpisodes = settings.UnmonitorWatchedEpisodes;
                    _stateRepository.Upsert(state);

                    if (expectedSyncProgress)
                    {
                        // Retry even unchanged snapshots; origin writes have their own
                        // durable idempotency/watermarks and never roll back local state.
                        foreach (var seriesId in matchedSeries)
                        {
                            // Capture the original notification scope, not a mutable settings
                            // reference. The progress service invokes this again immediately
                            // before every list/title target, including multiple origin lists.
                            Func<bool> canDispatch = () => IsProgressAuthorized(notificationId, seriesId, fingerprint, expectedUnmonitor, expectedRemonitor, expectedTags);
                            if (!canDispatch())
                            {
                                break;
                            }

                            var watched = _episodeRepository.GetEpisodes(seriesId)
                                .Where(episode => episode.Watched && episode.WatchedSource == fingerprint)
                                .Select(episode => episode.Id).ToList();
                            try
                            {
                                _progressService.SyncProgress(seriesId, watched, canDispatch);
                            }
                            catch (Exception)
                            {
                                _logger.Warn("List progress sync failed for series {0}; local watched state remains saved", seriesId);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Never include HTTP exception text, user names, keys or item paths.
                    // The last successful watermark is unchanged and the next tick retries.
                    _logger.Warn("Jellyfin watched sync did not complete for connection {0}; the last successful poll remains retryable", notificationId);
                }
            }
        }

        public void Execute(SyncJellyfinWatchedCommand message)
        {
            Sync(message.Trigger == CommandTrigger.Manual);
        }

        public void Handle(ProviderDeletedEvent<INotification> message)
        {
            lock (_syncLock)
            {
                var state = _stateRepository.ForNotification(message.ProviderId);
                if (state != null)
                {
                    _stateRepository.Delete(state);
                }

                // Historical episode watch flags remain useful. Ownership belongs to an
                // immutable target fingerprint and cannot re-monitor after deletion.
            }
        }

        private HashSet<int> ApplySnapshot(IList<JellyfinWatchedItem> items, JellyfinSettings settings, string fingerprint, HashSet<int> tags, bool applyInitialUnmonitor)
        {
            if (items == null || items.Any(item => item == null || item.UserData?.Played.HasValue != true ||
                JellyfinWatchPath.Normalize(item.Path) == null))
            {
                throw new InvalidOperationException("Invalid complete watched snapshot");
            }

            var byPath = items.Select(item => new { Path = JellyfinWatchPath.Translate(item.Path, settings), Item = item })
                .Where(value => value.Path != null).GroupBy(value => value.Path, StringComparer.Ordinal).ToList();
            if (byPath.Any(group => group.Select(value => value.Item.UserData.Played.Value).Distinct().Count() > 1))
            {
                throw new InvalidOperationException("Conflicting watched state for the same physical import");
            }

            var series = _seriesRepository.All()
                .Where(value => tags == null || tags.Count == 0 || value.Tags.Overlaps(tags))
                .ToDictionary(value => value.Id);
            var files = _fileRepository.All().Where(file => !string.IsNullOrEmpty(file.RelativePath))
                .Select(file => new
                {
                    File = file,
                    Path = series.TryGetValue(file.SeriesId, out var owner) && !string.IsNullOrEmpty(owner.Path) ?
                        JellyfinWatchPath.Normalize(owner.Path?.TrimEnd('/', '\\') + "/" + file.RelativePath) : null
                })
                .Where(value => value.Path != null)
                .GroupBy(value => value.Path, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single().File, StringComparer.Ordinal);
            var matched = new HashSet<int>();
            var updates = new List<(Episode Episode, JellyfinWatchedItem Item, bool Unmonitor, bool Remonitor)>();
            foreach (var group in byPath)
            {
                if (!files.TryGetValue(group.Key, out var file))
                {
                    continue;
                }

                var item = group.First().Item;
                var owner = series[file.SeriesId];
                foreach (var episode in _episodeRepository.GetEpisodeByFileId(file.Id))
                {
                    if (episode.SeriesId != file.SeriesId)
                    {
                        continue;
                    }

                    matched.Add(episode.SeriesId);
                    var played = item.UserData.Played.Value;
                    var unmonitor = settings.UnmonitorWatchedEpisodes &&
                        (applyInitialUnmonitor || !episode.Watched || episode.WatchedSource != fingerprint);
                    var seasonMonitored = owner.Seasons?.SingleOrDefault(season => season.SeasonNumber == episode.SeasonNumber)?.Monitored == true;
                    var remonitor = settings.RemonitorUnwatchedEpisodes && owner.Monitored && seasonMonitored;
                    if (episode.Watched != played || episode.WatchedSource != fingerprint ||
                        (played && unmonitor && episode.Monitored) ||
                        (!played && episode.JellyfinUnmonitorOwner == fingerprint))
                    {
                        updates.Add((episode, item, unmonitor, remonitor));
                    }
                }
            }

            // All pages, paths, conflicting states and local mappings are checked before
            // writes. Absent/unmatched remote items never mean "unwatched".
            foreach (var update in updates)
            {
                _episodeRepository.SetJellyfinWatched(update.Episode,
                    update.Item.UserData.Played.Value,
                    update.Item.UserData.LastPlayedDate?.ToUniversalTime() ?? _clock.UtcNow,
                    fingerprint,
                    update.Unmonitor,
                    update.Remonitor);
            }

            if (updates.Count > 0)
            {
                _logger.Info("Jellyfin watched sync updated {0} matched episodes", updates.Count);
            }

            return matched;
        }

        private static bool IsActive(NotificationDefinition definition)
        {
            return definition != null && definition.Id > 0 && definition.Implementation == "Jellyfin" &&
                definition.Settings is JellyfinSettings settings && settings.EnableWatchedSync;
        }

        private bool IsProgressAuthorized(int notificationId, int seriesId, string fingerprint, bool unmonitor, bool remonitor, HashSet<int> tags)
        {
            try
            {
                var current = _notificationRepository.Find(notificationId);
                if (!IsActive(current) || _notificationRepository.All().Count(IsActive) != 1)
                {
                    return false;
                }

                var settings = (JellyfinSettings)current.Settings;
                if (Fingerprint(notificationId, settings) != fingerprint ||
                    !SamePolicy(settings, unmonitor, remonitor, true) || !SameTags(current.Tags, tags))
                {
                    return false;
                }

                var series = _seriesRepository.Find(seriesId);
                return series != null && (tags.Count == 0 || (series.Tags?.Overlaps(tags) ?? false));
            }
            catch (Exception)
            {
                // Failure to re-read permission never authorizes an external mutation.
                return false;
            }
        }

        private static bool SamePolicy(JellyfinSettings current, bool unmonitor, bool remonitor, bool syncProgress)
        {
            return current.UnmonitorWatchedEpisodes == unmonitor &&
                current.RemonitorUnwatchedEpisodes == remonitor && current.SyncListProgress == syncProgress;
        }

        private static bool SameTags(HashSet<int> current, HashSet<int> expected)
        {
            return current == null ? expected.Count == 0 : current.SetEquals(expected);
        }

        private static string Fingerprint(int notificationId, JellyfinSettings settings)
        {
            return string.Join("\n",
                notificationId.ToString(CultureInfo.InvariantCulture),
                settings.Host?.ToLowerInvariant(),
                settings.Port.ToString(CultureInfo.InvariantCulture),
                settings.UseSsl.ToString(),
                settings.UrlBase?.TrimEnd('/'),
                settings.LibraryId?.Replace("-", string.Empty).ToLowerInvariant(),
                settings.WatchedUserId?.Replace("-", string.Empty).ToLowerInvariant(),
                JellyfinWatchPath.Normalize(settings.JellyfinPathPrefix),
                JellyfinWatchPath.Normalize(settings.TanukarrPathPrefix)).SHA256Hash();
        }
    }
}
