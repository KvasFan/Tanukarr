using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.EpisodeImport
{
    public interface IImportProgressService
    {
        void BeginBatch(int downloadClientId, string downloadId, IEnumerable<int> expectedEpisodeIds);
        void RegisterDecisions(DownloadClientItem downloadItem, IEnumerable<ImportDecision> decisions);
        void MarkImporting(DownloadClientItem downloadItem, LocalEpisode localEpisode);
        void MarkImported(DownloadClientItem downloadItem, LocalEpisode localEpisode);
        void MarkFailed(DownloadClientItem downloadItem, LocalEpisode localEpisode, params string[] errors);
        ImportProgress GetProgress(int downloadClientId, string downloadId);
        void Prune(IEnumerable<ImportProgressKey> activeKeys);
    }

    public sealed class ImportProgressService : IImportProgressService
    {
        private sealed class Batch
        {
            public object SyncRoot { get; } = new();
            public HashSet<int> ExpectedEpisodeIds { get; } = new();
            public Dictionary<string, Item> Items { get; } = new(PathEqualityComparer.Instance);
            public int NextOrder { get; set; }
        }

        private sealed class Item
        {
            public string Id { get; init; }
            public int Order { get; init; }
            public List<int> EpisodeIds { get; set; }
            public int? SeasonNumber { get; set; }
            public List<int> EpisodeNumbers { get; set; }
            public string SourcePath { get; init; }
            public long Size { get; set; }
            public ImportProgressState State { get; set; }
            public DateTime? Started { get; set; }
            public DateTime? Completed { get; set; }
            public List<string> Errors { get; set; }
        }

        private readonly IEventAggregator _eventAggregator;
        private readonly object _batchesLock = new();
        private readonly Dictionary<ImportProgressKey, Batch> _batches = new();

        public ImportProgressService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        public void BeginBatch(int downloadClientId, string downloadId, IEnumerable<int> expectedEpisodeIds)
        {
            if (downloadClientId <= 0 || string.IsNullOrWhiteSpace(downloadId))
            {
                return;
            }

            var key = new ImportProgressKey(downloadClientId, downloadId);
            var batch = GetOrCreateBatch(key);
            var expected = (expectedEpisodeIds ?? Enumerable.Empty<int>()).Where(id => id > 0).ToHashSet();

            lock (batch.SyncRoot)
            {
                if (batch.ExpectedEpisodeIds.Count > 0 && !batch.ExpectedEpisodeIds.SetEquals(expected))
                {
                    batch.ExpectedEpisodeIds.Clear();
                    batch.Items.Clear();
                    batch.NextOrder = 0;
                }

                batch.ExpectedEpisodeIds.UnionWith(expected);
            }

            Publish(key);
        }

        public void RegisterDecisions(DownloadClientItem downloadItem, IEnumerable<ImportDecision> decisions)
        {
            if (!TryGetKey(downloadItem, out var key))
            {
                return;
            }

            var decisionList = decisions.ToList();
            var batch = GetOrCreateBatch(key);

            lock (batch.SyncRoot)
            {
                if (batch.ExpectedEpisodeIds.Count == 0)
                {
                    batch.ExpectedEpisodeIds.UnionWith(decisionList.SelectMany(d => GetEpisodeIds(d.LocalEpisode)));
                }

                foreach (var decision in decisionList.Where(d => d.LocalEpisode?.Path != null))
                {
                    var localEpisode = decision.LocalEpisode;
                    var episodes = localEpisode.Episodes ?? new List<Episode>();
                    var episodeIds = GetEpisodeIds(localEpisode);
                    var seasons = episodes.Select(e => e.SeasonNumber).Distinct().OrderBy(s => s).ToList();

                    if (!batch.Items.TryGetValue(localEpisode.Path, out var item))
                    {
                        item = new Item
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Order = batch.NextOrder++,
                            SourcePath = localEpisode.Path
                        };
                        batch.Items.Add(localEpisode.Path, item);
                    }

                    if (item.State == ImportProgressState.Imported)
                    {
                        continue;
                    }

                    item.EpisodeIds = episodeIds;
                    item.SeasonNumber = seasons.Count == 1 ? seasons[0] : null;
                    item.EpisodeNumbers = episodes.Select(e => e.EpisodeNumber).Distinct().OrderBy(e => e).ToList();
                    item.Size = localEpisode.Size;
                    item.State = decision.Approved ? ImportProgressState.Waiting : ImportProgressState.Rejected;
                    item.Started = null;
                    item.Completed = decision.Approved ? null : DateTime.UtcNow;
                    item.Errors = decision.Rejections.Select(r => r.Message).ToList();
                }
            }

            Publish(key);
        }

        public void MarkImporting(DownloadClientItem downloadItem, LocalEpisode localEpisode)
        {
            Update(downloadItem, localEpisode, item =>
            {
                item.State = ImportProgressState.Importing;
                item.Started = DateTime.UtcNow;
                item.Completed = null;
                item.Errors = new List<string>();
            });
        }

        public void MarkImported(DownloadClientItem downloadItem, LocalEpisode localEpisode)
        {
            Update(downloadItem, localEpisode, item =>
            {
                item.State = ImportProgressState.Imported;
                item.Started ??= DateTime.UtcNow;
                item.Completed = DateTime.UtcNow;
                item.Errors = new List<string>();
            });
        }

        public void MarkFailed(DownloadClientItem downloadItem, LocalEpisode localEpisode, params string[] errors)
        {
            Update(downloadItem, localEpisode, item =>
            {
                item.State = ImportProgressState.Failed;
                item.Started ??= DateTime.UtcNow;
                item.Completed = DateTime.UtcNow;
                item.Errors = errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            });
        }

        public ImportProgress GetProgress(int downloadClientId, string downloadId)
        {
            var key = new ImportProgressKey(downloadClientId, downloadId);
            Batch batch;

            lock (_batchesLock)
            {
                if (!_batches.TryGetValue(key, out batch))
                {
                    return null;
                }
            }

            lock (batch.SyncRoot)
            {
                return CreateSnapshot(batch);
            }
        }

        public void Prune(IEnumerable<ImportProgressKey> activeKeys)
        {
            var active = activeKeys.ToHashSet();

            lock (_batchesLock)
            {
                foreach (var key in _batches.Keys.Where(key => !active.Contains(key)).ToList())
                {
                    _batches.Remove(key);
                }
            }
        }

        private Batch GetOrCreateBatch(ImportProgressKey key)
        {
            lock (_batchesLock)
            {
                if (!_batches.TryGetValue(key, out var batch))
                {
                    batch = new Batch();
                    _batches.Add(key, batch);
                }

                return batch;
            }
        }

        private void Update(DownloadClientItem downloadItem, LocalEpisode localEpisode, Action<Item> update)
        {
            if (!TryGetKey(downloadItem, out var key) || localEpisode?.Path == null)
            {
                return;
            }

            Batch batch;
            lock (_batchesLock)
            {
                if (!_batches.TryGetValue(key, out batch))
                {
                    return;
                }
            }

            lock (batch.SyncRoot)
            {
                if (!batch.Items.TryGetValue(localEpisode.Path, out var item))
                {
                    return;
                }

                update(item);
            }

            Publish(key);
        }

        private static ImportProgress CreateSnapshot(Batch batch)
        {
            var items = batch.Items.Values.OrderBy(i => i.Order).ToList();
            var expectedIds = batch.ExpectedEpisodeIds.Count > 0
                ? batch.ExpectedEpisodeIds
                : items.SelectMany(i => i.EpisodeIds).ToHashSet();

            var imported = CountEpisodes(expectedIds, items, ImportProgressState.Imported);
            var importing = CountEpisodes(expectedIds.Except(imported).ToHashSet(), items, ImportProgressState.Importing);
            var waiting = CountEpisodes(expectedIds.Except(imported).Except(importing).ToHashSet(), items, ImportProgressState.Waiting);
            var remaining = expectedIds.Except(imported).Except(importing).Except(waiting).ToHashSet();
            var failed = CountEpisodes(remaining, items, ImportProgressState.Failed);
            var rejected = CountEpisodes(remaining.Except(failed).ToHashSet(), items, ImportProgressState.Rejected);
            var unrepresented = expectedIds.Except(imported).Except(importing).Except(waiting).Except(failed).Except(rejected).Count();

            return new ImportProgress
            {
                TotalEpisodes = expectedIds.Count,
                ImportedEpisodes = imported.Count,
                ImportingEpisodes = importing.Count,
                WaitingEpisodes = waiting.Count + unrepresented,
                FailedEpisodes = failed.Count,
                RejectedEpisodes = rejected.Count,
                Items = items.Select(i => new ImportProgressItem
                {
                    Id = i.Id,
                    EpisodeIds = i.EpisodeIds.ToList(),
                    SeasonNumber = i.SeasonNumber,
                    EpisodeNumbers = i.EpisodeNumbers.ToList(),
                    SourcePath = i.SourcePath,
                    Size = i.Size,
                    State = i.State,
                    Started = i.Started,
                    Completed = i.Completed,
                    Errors = i.Errors.ToList()
                }).ToList()
            };
        }

        private static HashSet<int> CountEpisodes(HashSet<int> candidates, List<Item> items, ImportProgressState state)
        {
            return items.Where(i => i.State == state)
                .SelectMany(i => i.EpisodeIds)
                .Where(candidates.Contains)
                .ToHashSet();
        }

        private static List<int> GetEpisodeIds(LocalEpisode localEpisode)
        {
            return (localEpisode?.Episodes ?? new List<Episode>())
                .Select(e => e.Id)
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
        }

        private static bool TryGetKey(DownloadClientItem downloadItem, out ImportProgressKey key)
        {
            key = null;
            if (downloadItem?.DownloadClientInfo == null ||
                downloadItem.DownloadClientInfo.Id <= 0 ||
                string.IsNullOrWhiteSpace(downloadItem.DownloadId))
            {
                return false;
            }

            key = new ImportProgressKey(downloadItem.DownloadClientInfo.Id, downloadItem.DownloadId);
            return true;
        }

        private void Publish(ImportProgressKey key)
        {
            _eventAggregator.PublishEvent(new ImportProgressUpdatedEvent(key));
        }
    }
}
