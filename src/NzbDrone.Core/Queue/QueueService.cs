using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Crypto;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Queue
{
    public interface IQueueService
    {
        List<Queue> GetQueue();
        Queue Find(int id);
        void Remove(int id);
    }

    public class QueueService : IQueueService,
                                IHandle<TrackedDownloadRefreshedEvent>,
                                IHandle<ImportProgressUpdatedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IImportProgressService _importProgressService;
        private static readonly object QueueLock = new();
        private static List<Queue> _queue = new();

        public QueueService(IEventAggregator eventAggregator, IImportProgressService importProgressService)
        {
            _eventAggregator = eventAggregator;
            _importProgressService = importProgressService;
        }

        public List<Queue> GetQueue()
        {
            lock (QueueLock)
            {
                return _queue.ToList();
            }
        }

        public Queue Find(int id)
        {
            lock (QueueLock)
            {
                return _queue.SingleOrDefault(q => q.Id == id);
            }
        }

        public void Remove(int id)
        {
            lock (QueueLock)
            {
                _queue.Remove(_queue.SingleOrDefault(q => q.Id == id));
            }
        }

        private IEnumerable<Queue> MapQueue(TrackedDownload trackedDownload)
        {
            if (trackedDownload.RemoteEpisode?.Episodes != null && trackedDownload.RemoteEpisode.Episodes.Any())
            {
                yield return MapQueueItem(trackedDownload, trackedDownload.RemoteEpisode.Episodes);
            }
            else
            {
                yield return MapQueueItem(trackedDownload, []);
            }
        }

        private Queue MapQueueItem(TrackedDownload trackedDownload, List<Episode> episodes)
        {
            var queue = new Queue
            {
                Series = trackedDownload.RemoteEpisode?.Series,
                SeasonNumber = trackedDownload.RemoteEpisode?.MappedSeasonNumber,
                Episodes = episodes,
                Languages = trackedDownload.RemoteEpisode?.Languages ?? new List<Language> { Language.Unknown },
                Quality = trackedDownload.RemoteEpisode?.ParsedEpisodeInfo.Quality ?? new QualityModel(Quality.Unknown),
                Title = FileExtensions.RemoveFileExtension(trackedDownload.DownloadItem.Title),
                Size = trackedDownload.DownloadItem.TotalSize,
                SizeLeft = trackedDownload.DownloadItem.RemainingSize,
                TimeLeft = trackedDownload.DownloadItem.RemainingTime,
                Status = Enum.TryParse(trackedDownload.DownloadItem.Status.ToString(), out QueueStatus outValue) ? outValue : QueueStatus.Unknown,
                TrackedDownloadStatus = trackedDownload.Status,
                TrackedDownloadState = trackedDownload.State,
                StatusMessages = trackedDownload.StatusMessages.ToList(),
                ErrorMessage = trackedDownload.DownloadItem.Message,
                RemoteEpisode = trackedDownload.RemoteEpisode,
                DownloadId = trackedDownload.DownloadItem.DownloadId,
                Protocol = trackedDownload.Protocol,
                DownloadClient = trackedDownload.DownloadItem.DownloadClientInfo.Name,
                DownloadClientId = trackedDownload.DownloadItem.DownloadClientInfo.Id,
                Indexer = trackedDownload.Indexer,
                OutputPath = trackedDownload.DownloadItem.OutputPath.ToString(),
                Added = trackedDownload.Added,
                DownloadClientHasPostImportCategory = trackedDownload.DownloadItem.DownloadClientInfo.HasPostImportCategory,
                ImportProgress = _importProgressService.GetProgress(trackedDownload.DownloadItem.DownloadClientInfo.Id, trackedDownload.DownloadItem.DownloadId)
            };

            queue.Id = HashConverter.GetHashInt31($"trackedDownload-{trackedDownload.DownloadClient}-{trackedDownload.DownloadItem.DownloadId}");

            if (queue.TimeLeft.HasValue)
            {
                queue.EstimatedCompletionTime = DateTime.UtcNow.Add(queue.TimeLeft.Value);
            }

            return queue;
        }

        public void Handle(TrackedDownloadRefreshedEvent message)
        {
            var trackableDownloads = message.TrackedDownloads.Where(t => t.IsTrackable).ToList();
            _importProgressService.Prune(trackableDownloads.Select(t => new ImportProgressKey(t.DownloadItem.DownloadClientInfo.Id, t.DownloadItem.DownloadId)));

            var queue = trackableDownloads
                .OrderBy(c => c.DownloadItem.RemainingTime)
                .SelectMany(MapQueue)
                .ToList();

            lock (QueueLock)
            {
                _queue = queue;
            }

            _eventAggregator.PublishEvent(new QueueUpdatedEvent());
        }

        public void Handle(ImportProgressUpdatedEvent message)
        {
            var updated = false;

            lock (QueueLock)
            {
                var queueItem = _queue.SingleOrDefault(q =>
                    q.DownloadClientId == message.Key.DownloadClientId &&
                    string.Equals(q.DownloadId, message.Key.DownloadId, StringComparison.OrdinalIgnoreCase));

                if (queueItem != null)
                {
                    var progress = _importProgressService.GetProgress(message.Key.DownloadClientId, message.Key.DownloadId);
                    _queue = _queue.Select(q => q.Id == queueItem.Id ? q.WithImportProgress(progress) : q).ToList();
                    updated = true;
                }
            }

            if (updated)
            {
                _eventAggregator.PublishEvent(new QueueUpdatedEvent());
            }
        }
    }
}
