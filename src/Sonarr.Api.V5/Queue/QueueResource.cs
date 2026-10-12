using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using Sonarr.Api.V5.CustomFormats;
using Sonarr.Api.V5.Episodes;
using Sonarr.Api.V5.Series;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Queue
{
    public class QueueResource : RestResource
    {
        public int? SeriesId { get; set; }
        public IEnumerable<int> EpisodeIds { get; set; } = [];
        public List<int> SeasonNumbers { get; set; } = [];
        public Dictionary<int, List<int>> EpisodeIdsBySeason { get; set; } = [];
        public List<int> EpisodeIdsWithFiles { get; set; } = [];
        public SeriesResource? Series { get; set; }
        public List<EpisodeResource>? Episodes { get; set; }
        public List<Language> Languages { get; set; } = [];
        public QualityModel Quality { get; set; } = new(NzbDrone.Core.Qualities.Quality.Unknown);
        public List<CustomFormatResource> CustomFormats { get; set; } = [];
        public int CustomFormatScore { get; set; }
        public decimal Size { get; set; }
        public string? Title { get; set; }
        public decimal SizeLeft { get; set; }
        public TimeSpan? TimeLeft { get; set; }
        public DateTime? EstimatedCompletionTime { get; set; }
        public DateTime? Added { get; set; }
        public QueueStatus Status { get; set; }
        public TrackedDownloadStatus? TrackedDownloadStatus { get; set; }
        public TrackedDownloadState? TrackedDownloadState { get; set; }
        public List<TrackedDownloadStatusMessage>? StatusMessages { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DownloadId { get; set; }
        public DownloadProtocol Protocol { get; set; }
        public string? DownloadClient { get; set; }
        public bool DownloadClientHasPostImportCategory { get; set; }
        public string? Indexer { get; set; }
        public string? OutputPath { get; set; }
        public int EpisodesWithFilesCount { get; set; }
        public bool IsFullSeason { get; set; }
        public ImportProgressResource? ImportProgress { get; set; }
    }

    public class ImportProgressResource
    {
        public int TotalEpisodes { get; set; }
        public int ImportedEpisodes { get; set; }
        public int ImportingEpisodes { get; set; }
        public int WaitingEpisodes { get; set; }
        public int FailedEpisodes { get; set; }
        public int RejectedEpisodes { get; set; }
        public List<ImportProgressItemResource> Items { get; set; } = [];
    }

    public class ImportProgressItemResource
    {
        public string? Id { get; set; }
        public List<int> EpisodeIds { get; set; } = [];
        public int? SeasonNumber { get; set; }
        public List<int> EpisodeNumbers { get; set; } = [];
        public string? SourcePath { get; set; }
        public long Size { get; set; }
        public ImportProgressState State { get; set; }
        public DateTime? Started { get; set; }
        public DateTime? Completed { get; set; }
        public List<string> Errors { get; set; } = [];
    }

    public static class QueueResourceMapper
    {
        public static QueueResource ToResource(this NzbDrone.Core.Queue.Queue model, bool includeSeries, bool includeEpisodes)
        {
            var episodes = model.Episodes ?? [];
            var episodeIdsBySeason = episodes.GroupBy(e => e.SeasonNumber)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Id).Distinct().ToList());
            var episodeIdsWithFiles = episodes.Where(e => e.HasFile).Select(e => e.Id).Distinct().ToList();
            var customFormats = model.RemoteEpisode?.CustomFormats;
            var customFormatScore = model.Series?.QualityProfile?.Value?.CalculateCustomFormatScore(customFormats) ?? 0;

            return new QueueResource
            {
                Id = model.Id,
                SeriesId = model.Series?.Id,
                EpisodeIds = episodes.Select(e => e.Id).Distinct().ToList(),
                SeasonNumbers = episodeIdsBySeason.Count > 0
                    ? episodeIdsBySeason.Keys.OrderBy(s => s).ToList()
                    : model.SeasonNumber.HasValue ? [model.SeasonNumber.Value] : [],
                EpisodeIdsBySeason = episodeIdsBySeason,
                EpisodeIdsWithFiles = episodeIdsWithFiles,
                Series = includeSeries && model.Series != null ? model.Series.ToResource() : null,
                Episodes = includeEpisodes ? model.Episodes?.ToResource() : null,
                Languages = model.Languages,
                Quality = model.Quality,
                CustomFormats = customFormats?.ToResource(false) ?? [],
                CustomFormatScore = customFormatScore,
                Size = model.Size,
                Title = model.Title,
                SizeLeft = model.SizeLeft,
                TimeLeft = model.TimeLeft,
                EstimatedCompletionTime = model.EstimatedCompletionTime,
                Added = model.Added,
                Status = model.Status,
                TrackedDownloadStatus = model.TrackedDownloadStatus,
                TrackedDownloadState = model.TrackedDownloadState,
                StatusMessages = model.StatusMessages,
                ErrorMessage = model.ErrorMessage,
                DownloadId = model.DownloadId,
                Protocol = model.Protocol,
                DownloadClient = model.DownloadClient,
                DownloadClientHasPostImportCategory = model.DownloadClientHasPostImportCategory,
                Indexer = model.Indexer,
                OutputPath = model.OutputPath,
                EpisodesWithFilesCount = episodeIdsWithFiles.Count,
                IsFullSeason = model.RemoteEpisode?.ParsedEpisodeInfo?.FullSeason ?? false,
                ImportProgress = model.ImportProgress?.ToResource()
            };
        }

        private static ImportProgressResource ToResource(this ImportProgress model)
        {
            return new ImportProgressResource
            {
                TotalEpisodes = model.TotalEpisodes,
                ImportedEpisodes = model.ImportedEpisodes,
                ImportingEpisodes = model.ImportingEpisodes,
                WaitingEpisodes = model.WaitingEpisodes,
                FailedEpisodes = model.FailedEpisodes,
                RejectedEpisodes = model.RejectedEpisodes,
                Items = model.Items.Select(item => new ImportProgressItemResource
                {
                    Id = item.Id,
                    EpisodeIds = item.EpisodeIds,
                    SeasonNumber = item.SeasonNumber,
                    EpisodeNumbers = item.EpisodeNumbers,
                    SourcePath = item.SourcePath,
                    Size = item.Size,
                    State = item.State,
                    Started = item.Started,
                    Completed = item.Completed,
                    Errors = item.Errors
                }).ToList()
            };
        }

        public static List<QueueResource> ToResource(this IEnumerable<NzbDrone.Core.Queue.Queue> models, bool includeSeries, bool includeEpisode)
        {
            return models.Select((m) => ToResource(m, includeSeries, includeEpisode)).ToList();
        }
    }
}
