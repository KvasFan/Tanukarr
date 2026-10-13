using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.AniList;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public class AniListMetadataCache
    {
        private const int MaxEntries = 512;
        private const int MaxEntryBytes = 512 * 1024;

        private readonly IAniListRequestDelay _delay;
        private readonly CacheState _state;

        public AniListMetadataCache(ICacheManager cacheManager, IAniListRequestDelay delay)
        {
            _delay = delay;
            _state = cacheManager.GetCache<CacheState>(typeof(AniListMetadataCache), "publicMetadata")
                .Get("state", () => new CacheState());
        }

        public T Get<T>(string key, Func<T> fetch, TimeSpan lifetime, Func<T, bool> canCache = null)
            where T : class, new()
        {
            var cached = Find(key);

            if (cached != null)
            {
                return Json.Deserialize<T>(cached.Json);
            }

            // Cached.Get does not coalesce its value factories. Share a single
            // in-flight fetch across proxy instances before consuming quota.
            var pending = _state.Pending.GetOrAdd(key, _ => new Lazy<CacheEntry>(() =>
            {
                var existing = Find(key);

                if (existing != null)
                {
                    return existing;
                }

                var value = fetch();

                if (value == null)
                {
                    return null;
                }

                var entry = new CacheEntry { Json = Json.ToJson(value), ExpiresAt = _delay.UtcNow.Add(lifetime) };

                if ((canCache == null || canCache(value)) && Encoding.UTF8.GetByteCount(entry.Json) <= MaxEntryBytes)
                {
                    Store(key, entry);
                }

                return entry;
            },
            LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                var entry = pending.Value;

                // Metadata objects are mutated while mapping and adding series.
                // Return independent graphs so callers cannot corrupt the cache.
                return entry == null ? null : Json.Deserialize<T>(entry.Json);
            }
            finally
            {
                var pendingEntries = (ICollection<KeyValuePair<string, Lazy<CacheEntry>>>)_state.Pending;
                pendingEntries.Remove(new KeyValuePair<string, Lazy<CacheEntry>>(key, pending));
            }
        }

        public void Set<T>(string key, T value, TimeSpan lifetime)
            where T : class
        {
            if (value == null)
            {
                return;
            }

            var json = Json.ToJson(value);

            if (Encoding.UTF8.GetByteCount(json) <= MaxEntryBytes)
            {
                Store(key, new CacheEntry { Json = json, ExpiresAt = _delay.UtcNow.Add(lifetime) });
            }
        }

        private CacheEntry Find(string key)
        {
            lock (_state.SyncRoot)
            {
                if (!_state.Entries.TryGetValue(key, out var entry))
                {
                    return null;
                }

                if (entry.ExpiresAt <= _delay.UtcNow)
                {
                    _state.Entries.Remove(key);
                    return null;
                }

                return entry;
            }
        }

        private void Store(string key, CacheEntry entry)
        {
            lock (_state.SyncRoot)
            {
                foreach (var expired in _state.Entries.Where(item => item.Value.ExpiresAt <= _delay.UtcNow).Select(item => item.Key).ToList())
                {
                    _state.Entries.Remove(expired);
                }

                if (!_state.Entries.ContainsKey(key) && _state.Entries.Count >= MaxEntries)
                {
                    var oldest = _state.Entries.MinBy(item => item.Value.ExpiresAt).Key;
                    _state.Entries.Remove(oldest);
                }

                _state.Entries[key] = entry;
            }
        }

        private sealed class CacheEntry
        {
            public string Json { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        private sealed class CacheState
        {
            public object SyncRoot { get; } = new object();
            public Dictionary<string, CacheEntry> Entries { get; } = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
            public ConcurrentDictionary<string, Lazy<CacheEntry>> Pending { get; } = new ConcurrentDictionary<string, Lazy<CacheEntry>>(StringComparer.Ordinal);
        }
    }
}
