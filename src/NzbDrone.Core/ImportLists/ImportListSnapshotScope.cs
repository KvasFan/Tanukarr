using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists
{
    public sealed class ImportListSnapshotScope : IDisposable
    {
        public const int MaxSnapshots = 64;
        public const long MaxSnapshotBytes = 8 * 1024 * 1024;
        public const long MaxRetainedBytes = 32 * 1024 * 1024;

        private static readonly AsyncLocal<ImportListSnapshotScope> Ambient = new();
        private readonly ImportListSnapshotScope _previous;
        private readonly object _gate = new();
        private readonly Dictionary<string, SnapshotEntry> _entries = new(StringComparer.Ordinal);
        private bool _disposed;
        private long _retainedBytes;

        private ImportListSnapshotScope(ImportListSnapshotScope previous)
        {
            _previous = previous;
        }

        public static ImportListSnapshotScope Current => Ambient.Value is { _disposed: false } scope ? scope : null;

        public int RetainedSnapshotCount
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Values.Count(entry => entry.Retained);
                }
            }
        }

        public long RetainedBytes
        {
            get
            {
                lock (_gate)
                {
                    return _retainedBytes;
                }
            }
        }

        public static ImportListSnapshotScope Begin()
        {
            var scope = new ImportListSnapshotScope(Current);
            Ambient.Value = scope;
            return scope;
        }

        public static IDisposable BeginIfNeeded() => Current == null ? Begin() : NoopDisposable.Instance;

        public T GetOrLoad<T>(string hashedKey, Func<T> loader, Func<T, long> measureSize = null)
        {
            var key = TypedKey<T>(hashedKey);
            SnapshotEntry entry;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_entries.TryGetValue(key, out entry) && _entries.Count < MaxSnapshots)
                {
                    entry = new SnapshotEntry();
                    entry.Value = new Lazy<object>(() =>
                    {
                        var value = loader();
                        var bytes = measureSize?.Invoke(value) ?? Encoding.UTF8.GetByteCount(Json.ToJson(value));
                        lock (_gate)
                        {
                            if (_disposed || bytes < 0 || bytes > MaxSnapshotBytes || bytes > MaxRetainedBytes - _retainedBytes)
                            {
                                _entries.Remove(key);
                            }
                            else
                            {
                                entry.Retained = true;
                                _retainedBytes += bytes;
                            }
                        }

                        return value;
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication);
                    _entries.Add(key, entry);
                }
            }

            // Retention limits never turn a complete result into a partial import.
            // Overflow reads complete normally without retaining their payload.
            return entry == null ? loader() : (T)entry.Value.Value;
        }

        public bool TryGet<T>(string hashedKey, out T value)
        {
            var key = TypedKey<T>(hashedKey);
            lock (_gate)
            {
                if (!_disposed && _entries.TryGetValue(key, out var entry) && entry.Retained && entry.Value.IsValueCreated)
                {
                    value = (T)entry.Value.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        public IReadOnlyList<T> GetCompletedValues<T>()
        {
            var prefix = typeof(T).FullName + ":";
            lock (_gate)
            {
                return _disposed ? Array.Empty<T>() : _entries
                    .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal) && pair.Value.Retained && pair.Value.Value.IsValueCreated)
                    .Select(pair => (T)pair.Value.Value.Value).ToArray();
            }
        }

        public static string Fingerprint(params string[] parts)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var part in parts)
            {
                var bytes = Encoding.UTF8.GetBytes(part ?? string.Empty);
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        public static void AddPayloadBytes(ref long totalBytes, string content)
        {
            var bytes = Encoding.UTF8.GetByteCount(content ?? string.Empty);
            if (bytes > MaxSnapshotBytes - totalBytes)
            {
                throw new ImportListException(null, "The complete list exceeds the 8 MiB response safety limit. The previous list will be retained.");
            }

            totalBytes += bytes;
        }

        public static List<ImportListItemInfo> CopyItems(IEnumerable<ImportListItemInfo> items)
        {
            return items.Select(item => new ImportListItemInfo
            {
                Id = item.Id,
                ImportListId = item.ImportListId,
                ImportList = item.ImportList,
                Title = item.Title,
                Year = item.Year,
                TvdbId = item.TvdbId,
                TmdbId = item.TmdbId,
                ImdbId = item.ImdbId,
                MalId = item.MalId,
                AniListId = item.AniListId,
                ReleaseDate = item.ReleaseDate,
                Seasons = item.Seasons?.Select(season => new Season
                {
                    SeasonNumber = season.SeasonNumber,
                    Monitored = season.Monitored,
                    Title = season.Title,
                    Images = season.Images?.Select(cover => new MediaCover.MediaCover
                    {
                        CoverType = cover.CoverType,
                        Url = cover.Url,
                        RemoteUrl = cover.RemoteUrl
                    }).ToList()
                }).ToList()
            }).ToList();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _entries.Clear();
                _retainedBytes = 0;
            }

            if (ReferenceEquals(Ambient.Value, this))
            {
                Ambient.Value = _previous;
            }
        }

        private static string TypedKey<T>(string hashedKey)
        {
            if (hashedKey?.Length != 64 || hashedKey.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new ArgumentException("Snapshot keys must be SHA-256 fingerprints.", nameof(hashedKey));
            }

            return typeof(T).FullName + ":" + hashedKey.ToUpperInvariant();
        }

        private sealed class SnapshotEntry
        {
            public Lazy<object> Value { get; set; }
            public bool Retained { get; set; }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static NoopDisposable Instance { get; } = new();
            public void Dispose()
            {
            }
        }
    }
}
