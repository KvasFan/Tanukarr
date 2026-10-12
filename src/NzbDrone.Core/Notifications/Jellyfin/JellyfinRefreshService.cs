using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public interface IJellyfinRefreshService
    {
        void OnImport(NotificationDefinition definition);
        void ProcessPending();
    }

    public class JellyfinRefreshService : IJellyfinRefreshService,
        IExecute<ProcessPendingJellyfinRefreshCommand>,
        IHandle<ProviderAddedEvent<INotification>>,
        IHandle<ProviderUpdatedEvent<INotification>>,
        IHandle<ProviderDeletedEvent<INotification>>
    {
        private readonly IJellyfinRefreshStateRepository _stateRepository;
        private readonly INotificationRepository _notificationRepository;
        private readonly IJellyfinProxy _proxy;
        private readonly IJellyfinRefreshClock _clock;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<int, object> _stateLocks = new ConcurrentDictionary<int, object>();
        private readonly ConcurrentDictionary<int, byte> _refreshing = new ConcurrentDictionary<int, byte>();

        public JellyfinRefreshService(IJellyfinRefreshStateRepository stateRepository,
            INotificationRepository notificationRepository,
            IJellyfinProxy proxy,
            IJellyfinRefreshClock clock,
            Logger logger)
        {
            _stateRepository = stateRepository;
            _notificationRepository = notificationRepository;
            _proxy = proxy;
            _clock = clock;
            _logger = logger;
        }

        public void OnImport(NotificationDefinition definition)
        {
            if (definition == null || definition.Id <= 0)
            {
                return;
            }

            var immediate = false;

            lock (StateLock(definition.Id))
            {
                var current = _notificationRepository.Find(definition.Id);

                if (!IsActive(current))
                {
                    DeleteState(definition.Id);
                    return;
                }

                var settings = (JellyfinSettings)current.Settings;
                var state = GetState(current, settings);

                // Watermarks keep an import arriving during an HTTP request pending.
                state.ImportedGeneration = checked(state.ImportedGeneration + 1);
                _stateRepository.Upsert(state);
                immediate = HasImmediateMode(settings);
            }

            if (immediate)
            {
                TryRefresh(definition.Id, false);
            }
        }

        public void ProcessPending()
        {
            var definitions = _notificationRepository.All().ToList();
            var activeIds = definitions.Where(IsActive).Select(definition => definition.Id).ToHashSet();

            foreach (var state in _stateRepository.All().ToList())
            {
                if (!activeIds.Contains(state.NotificationId))
                {
                    lock (StateLock(state.NotificationId))
                    {
                        // Re-read after taking the lock: a concurrent settings save can enable it.
                        if (!IsActive(_notificationRepository.Find(state.NotificationId)))
                        {
                            DeleteState(state.NotificationId);
                        }
                    }
                }
            }

            foreach (var definition in definitions.Where(IsActive))
            {
                TryRefresh(definition.Id, true);
            }
        }

        public void Execute(ProcessPendingJellyfinRefreshCommand message)
        {
            ProcessPending();
        }

        public void Handle(ProviderAddedEvent<INotification> message)
        {
            InitializeDefinition(message.Definition.Id);
        }

        public void Handle(ProviderUpdatedEvent<INotification> message)
        {
            InitializeDefinition(message.Definition.Id);
        }

        public void Handle(ProviderDeletedEvent<INotification> message)
        {
            lock (StateLock(message.ProviderId))
            {
                DeleteState(message.ProviderId);
            }
        }

        private void InitializeDefinition(int notificationId)
        {
            lock (StateLock(notificationId))
            {
                var definition = _notificationRepository.Find(notificationId);

                if (IsActive(definition))
                {
                    GetState(definition, (JellyfinSettings)definition.Settings);
                }
                else
                {
                    DeleteState(notificationId);
                }
            }
        }

        private void TryRefresh(int notificationId, bool processTick)
        {
            // An import never waits for an earlier request to finish before being persisted.
            // The scheduled one-minute task retries any remaining immediate imports.
            if (!_refreshing.TryAdd(notificationId, 0))
            {
                return;
            }

            try
            {
                while (true)
                {
                    JellyfinSettings settings;
                    string fingerprint;
                    string stateToken;
                    int refreshMode;
                    long immediateGeneration;
                    long tickGeneration;
                    bool acknowledgeImmediate;
                    bool acknowledgeTick;

                    lock (StateLock(notificationId))
                    {
                        var definition = _notificationRepository.Find(notificationId);

                        if (!IsActive(definition))
                        {
                            DeleteState(notificationId);
                            return;
                        }

                        settings = (JellyfinSettings)definition.Settings;
                        var state = GetState(definition, settings);
                        var now = _clock.UtcNow;
                        var tickDue = processTick && HasIntervalMode(settings) &&
                            (now - state.LastTickUtc).TotalMinutes >= settings.RefreshIntervalMinutes;

                        acknowledgeImmediate = HasImmediateMode(settings) && state.ImportedGeneration > state.ImmediateGeneration;
                        acknowledgeTick = tickDue && state.ImportedGeneration > state.TickGeneration;

                        if (tickDue && !acknowledgeTick)
                        {
                            // An idle tick advances the cadence without contacting Jellyfin.
                            state.LastTickUtc = now;
                            _stateRepository.Upsert(state);
                        }

                        if (!acknowledgeImmediate && !acknowledgeTick)
                        {
                            return;
                        }

                        fingerprint = state.TargetFingerprint;
                        stateToken = state.StateToken;
                        refreshMode = settings.RefreshMode;

                        // Immediate mode sends one request for each imported media file,
                        // including a concurrent burst or a backlog after a failed request.
                        immediateGeneration = state.ImmediateGeneration + 1;
                        tickGeneration = state.ImportedGeneration;
                    }

                    try
                    {
                        _proxy.RefreshLibrary(settings);
                    }
                    catch (Exception)
                    {
                        // HTTP exception messages/requests can contain credentials. Keep the
                        // pending watermark and report only the local connection identifier.
                        _logger.Warn("Jellyfin library refresh failed for connection {0}; pending imports will be retried", notificationId);
                        return;
                    }

                    lock (StateLock(notificationId))
                    {
                        var definition = _notificationRepository.Find(notificationId);

                        if (!IsActive(definition))
                        {
                            DeleteState(notificationId);
                            return;
                        }

                        var state = GetState(definition, (JellyfinSettings)definition.Settings);

                        // A refresh for an old target must not acknowledge a newly selected library.
                        if (state.TargetFingerprint != fingerprint || state.StateToken != stateToken)
                        {
                            return;
                        }

                        if (acknowledgeImmediate)
                        {
                            state.ImmediateGeneration = Math.Max(state.ImmediateGeneration, immediateGeneration);
                        }

                        if (acknowledgeTick)
                        {
                            state.TickGeneration = Math.Max(state.TickGeneration, tickGeneration);
                            state.LastTickUtc = _clock.UtcNow;
                        }

                        if (state.LastRefreshMode != refreshMode)
                        {
                            // A request already in flight when the mode changes still refreshed
                            // this target. Do not replay its acknowledged imports in the new mode.
                            var acknowledged = Math.Max(state.ImmediateGeneration, state.TickGeneration);
                            state.ImmediateGeneration = acknowledged;
                            state.TickGeneration = acknowledged;
                        }

                        _stateRepository.Upsert(state);
                    }
                }
            }
            finally
            {
                _refreshing.TryRemove(notificationId, out _);
            }
        }

        private JellyfinRefreshState GetState(NotificationDefinition definition, JellyfinSettings settings)
        {
            var state = _stateRepository.ForNotification(definition.Id);
            var fingerprint = TargetFingerprint(settings);

            if (state == null)
            {
                state = new JellyfinRefreshState
                {
                    NotificationId = definition.Id,
                    TargetFingerprint = fingerprint,
                    StateToken = Guid.NewGuid().ToString("N"),
                    LastRefreshMode = settings.RefreshMode,
                    LastTickUtc = _clock.UtcNow
                };

                _stateRepository.Upsert(state);
            }
            else if (state.TargetFingerprint != fingerprint)
            {
                state.TargetFingerprint = fingerprint;
                state.StateToken = Guid.NewGuid().ToString("N");
                state.LastRefreshMode = settings.RefreshMode;
                state.ImportedGeneration = 0;
                state.ImmediateGeneration = 0;
                state.TickGeneration = 0;
                state.LastTickUtc = _clock.UtcNow;
                _stateRepository.Upsert(state);
            }
            else if (state.LastRefreshMode != settings.RefreshMode)
            {
                // A newly enabled mode starts after imports already acknowledged by the
                // previous mode. Failed imports stay pending in ImportedGeneration.
                var acknowledged = Math.Max(state.ImmediateGeneration, state.TickGeneration);
                state.ImmediateGeneration = acknowledged;
                state.TickGeneration = acknowledged;
                state.LastRefreshMode = settings.RefreshMode;
                state.LastTickUtc = _clock.UtcNow;
                _stateRepository.Upsert(state);
            }

            return state;
        }

        private void DeleteState(int notificationId)
        {
            var state = _stateRepository.ForNotification(notificationId);

            if (state != null)
            {
                _stateRepository.Delete(state.Id);
            }
        }

        private object StateLock(int notificationId)
        {
            return _stateLocks.GetOrAdd(notificationId, _ => new object());
        }

        private static bool IsActive(NotificationDefinition definition)
        {
            return definition != null && definition.Id > 0 && definition.Implementation == "Jellyfin" &&
                definition.OnDownload && definition.Settings is JellyfinSettings;
        }

        private static bool HasImmediateMode(JellyfinSettings settings)
        {
            return settings.RefreshMode == (int)JellyfinRefreshMode.EachImport ||
                settings.RefreshMode == (int)JellyfinRefreshMode.EachImportAndInterval;
        }

        private static bool HasIntervalMode(JellyfinSettings settings)
        {
            return settings.RefreshMode == (int)JellyfinRefreshMode.Interval ||
                settings.RefreshMode == (int)JellyfinRefreshMode.EachImportAndInterval;
        }

        private static string TargetFingerprint(JellyfinSettings settings)
        {
            var libraryId = Guid.TryParse(settings.LibraryId, out var parsedId) ? parsedId.ToString("N") : settings.LibraryId;

            // API-key rotation preserves pending imports; target changes discard stale work.
            return string.Join("\n",
                settings.Host?.Trim().ToLowerInvariant(),
                settings.Port.ToString(CultureInfo.InvariantCulture),
                settings.UseSsl.ToString(),
                settings.UrlBase?.TrimEnd('/'),
                libraryId).SHA256Hash();
        }
    }
}
