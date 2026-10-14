using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.MetadataSource
{
    public class SeriesPresentationCache
    {
        internal const int MaxEntries = 512;
        internal const int MaxEntryBytes = 32 * 1024;
        internal const int MaxTotalBytes = 8 * 1024 * 1024;

        private static readonly TimeSpan SuccessLifetime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan MissingLifetime = TimeSpan.FromMinutes(2);
        private readonly CacheState _state;
        private readonly Func<DateTime> _utcNow;

        public SeriesPresentationCache(ICacheManager cacheManager)
            : this(cacheManager, () => DateTime.UtcNow)
        {
        }

        internal SeriesPresentationCache(ICacheManager cacheManager, Func<DateTime> utcNow)
        {
            _utcNow = utcNow;
            _state = cacheManager.GetCache<CacheState>(GetType(), "publicPresentation")
                .Get("state", () => new CacheState());
        }

        public SeriesPresentation Get(string key, Func<SeriesPresentation> fetch)
        {
            var cached = Find(key);
            if (cached != null)
            {
                return Copy(cached);
            }

            var pending = _state.Pending.GetOrAdd(key, _ => new Lazy<CacheEntry>(() =>
            {
                var existing = Find(key);
                if (existing != null)
                {
                    return existing;
                }

                var value = fetch();
                var json = value == null ? null : Json.ToJson(value);
                var bytes = json == null ? 0 : Encoding.UTF8.GetByteCount(json);
                var entry = new CacheEntry
                {
                    Json = json,
                    Bytes = bytes,
                    ExpiresAt = _utcNow().Add(value == null ? MissingLifetime : SuccessLifetime)
                };

                if (bytes <= MaxEntryBytes)
                {
                    Store(key, entry);
                }

                return entry;
            },
            LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return Copy(pending.Value);
            }
            finally
            {
                ((ICollection<KeyValuePair<string, Lazy<CacheEntry>>>)_state.Pending)
                    .Remove(new KeyValuePair<string, Lazy<CacheEntry>>(key, pending));
            }
        }

        public bool IsProviderCoolingDown(MetadataSourceType source)
        {
            lock (_state.SyncRoot)
            {
                return _state.Cooldowns.TryGetValue(source, out var until) && until > _utcNow();
            }
        }

        public void BackOff(MetadataSourceType source)
        {
            lock (_state.SyncRoot)
            {
                _state.Cooldowns[source] = _utcNow().Add(MissingLifetime);
            }
        }

        private static SeriesPresentation Copy(CacheEntry entry)
        {
            return entry.Json == null ? null : Json.Deserialize<SeriesPresentation>(entry.Json);
        }

        private CacheEntry Find(string key)
        {
            lock (_state.SyncRoot)
            {
                return _state.Entries.TryGetValue(key, out var entry) && entry.ExpiresAt > _utcNow() ? entry : null;
            }
        }

        private void Store(string key, CacheEntry entry)
        {
            lock (_state.SyncRoot)
            {
                foreach (var expired in _state.Entries.Where(item => item.Value.ExpiresAt <= _utcNow())
                    .Select(item => item.Key).ToList())
                {
                    Remove(expired);
                }

                Remove(key);
                while (_state.Entries.Count >= MaxEntries || _state.TotalBytes + entry.Bytes > MaxTotalBytes)
                {
                    Remove(_state.Entries.MinBy(item => item.Value.ExpiresAt).Key);
                }

                _state.Entries[key] = entry;
                _state.TotalBytes += entry.Bytes;
            }
        }

        private void Remove(string key)
        {
            if (_state.Entries.Remove(key, out var removed))
            {
                _state.TotalBytes -= removed.Bytes;
            }
        }

        private sealed class CacheEntry
        {
            public string Json { get; set; }
            public int Bytes { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        private sealed class CacheState
        {
            public object SyncRoot { get; } = new object();
            public Dictionary<string, CacheEntry> Entries { get; } = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
            public ConcurrentDictionary<string, Lazy<CacheEntry>> Pending { get; } = new ConcurrentDictionary<string, Lazy<CacheEntry>>(StringComparer.Ordinal);
            public Dictionary<MetadataSourceType, DateTime> Cooldowns { get; } = new Dictionary<MetadataSourceType, DateTime>();
            public int TotalBytes { get; set; }
        }
    }
}
