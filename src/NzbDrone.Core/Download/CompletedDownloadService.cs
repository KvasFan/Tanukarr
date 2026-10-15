using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download
{
    public interface ICompletedDownloadService
    {
        void Check(TrackedDownload trackedDownload);
        void Import(TrackedDownload trackedDownload);
        bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults);
    }

    public class CompletedDownloadService : ICompletedDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IHistoryService _historyService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly IDownloadedEpisodesImportService _downloadedEpisodesImportService;
        private readonly IParsingService _parsingService;
        private readonly ISeriesService _seriesService;
        private readonly ITrackedDownloadAlreadyImported _trackedDownloadAlreadyImported;
        private readonly IEpisodeService _episodeService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IRejectedImportService _rejectedImportService;
        private readonly IImportProgressService _importProgressService;
        private readonly IMovieGrabContextService _movieGrabContext;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public CompletedDownloadService(IEventAggregator eventAggregator,
                                        IHistoryService historyService,
                                        IProvideImportItemService provideImportItemService,
                                        IDownloadedEpisodesImportService downloadedEpisodesImportService,
                                        IParsingService parsingService,
                                        ISeriesService seriesService,
                                        ITrackedDownloadAlreadyImported trackedDownloadAlreadyImported,
                                        IEpisodeService episodeService,
                                        IMediaFileService mediaFileService,
                                        IRejectedImportService rejectedImportService,
                                        IImportProgressService importProgressService,
                                        IMovieGrabContextService movieGrabContext,
                                        IDiskProvider diskProvider,
                                        Logger logger)
        {
            _eventAggregator = eventAggregator;
            _historyService = historyService;
            _provideImportItemService = provideImportItemService;
            _downloadedEpisodesImportService = downloadedEpisodesImportService;
            _parsingService = parsingService;
            _seriesService = seriesService;
            _trackedDownloadAlreadyImported = trackedDownloadAlreadyImported;
            _episodeService = episodeService;
            _mediaFileService = mediaFileService;
            _rejectedImportService = rejectedImportService;
            _importProgressService = importProgressService;
            _movieGrabContext = movieGrabContext;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Check(TrackedDownload trackedDownload)
        {
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                return;
            }

            SetImportItem(trackedDownload);

            if (Parser.Parser.ParseTitle(trackedDownload.DownloadItem.Title) is { IsMultiSeason: true } ||
                trackedDownload.RemoteEpisode?.ParsedEpisodeInfo is { IsMultiSeason: true } or { IsCompleteSeries: true })
            {
                trackedDownload.DownloadItem.CanMoveFiles = false;
                trackedDownload.DownloadItem.CanBeRemoved = false;
                trackedDownload.ImportItem.CanMoveFiles = false;
                trackedDownload.ImportItem.CanBeRemoved = false;
            }

            // Only process tracked downloads that are still downloading or have been blocked for importing due to an issue with matching
            if (trackedDownload.State != TrackedDownloadState.Downloading && trackedDownload.State != TrackedDownloadState.ImportBlocked)
            {
                return;
            }

            var grabbedHistories = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId).Where(h => h.EventType == EpisodeHistoryEventType.Grabbed).ToList();
            var historyItem = grabbedHistories.MaxBy(h => h.Date);

            if (historyItem == null && trackedDownload.DownloadItem.Category.IsNullOrWhiteSpace())
            {
                trackedDownload.Warn("Download wasn't grabbed by Sonarr and not in a category, Skipping.");
                return;
            }

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            var series = _parsingService.GetSeries(trackedDownload.DownloadItem.Title);

            if (series == null)
            {
                if (historyItem != null)
                {
                    series = _seriesService.GetSeries(historyItem.SeriesId);
                }

                if (series == null)
                {
                    trackedDownload.Warn("Series title mismatch; automatic import is not possible. Check the download troubleshooting entry on the wiki for common causes.");
                    SetStateToImportBlocked(trackedDownload);

                    return;
                }

                Enum.TryParse(historyItem.Data.GetValueOrDefault(EpisodeHistory.SERIES_MATCH_TYPE, SeriesMatchType.Unknown.ToString()), out SeriesMatchType seriesMatchType);
                Enum.TryParse(historyItem.Data.GetValueOrDefault(EpisodeHistory.RELEASE_SOURCE, ReleaseSourceType.Unknown.ToString()), out ReleaseSourceType releaseSource);

                // Show a warning if the release was matched by ID and the source is not interactive search
                if (seriesMatchType == SeriesMatchType.Id && releaseSource != ReleaseSourceType.InteractiveSearch)
                {
                    trackedDownload.Warn("Found matching series via grab history, but release was matched to series by ID. Automatic import is not possible. See the FAQ for details.");
                    SetStateToImportBlocked(trackedDownload);

                    return;
                }
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;
        }

        public void Import(TrackedDownload trackedDownload)
        {
            SetImportItem(trackedDownload);

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            if (trackedDownload.RemoteEpisode == null)
            {
                trackedDownload.Warn("Unable to parse download, automatic import is not possible.");
                SetStateToImportBlocked(trackedDownload);

                return;
            }

            trackedDownload.State = TrackedDownloadState.Importing;

            ObserveProgress(() => _importProgressService.BeginBatch(
                trackedDownload.DownloadItem.DownloadClientInfo.Id,
                trackedDownload.DownloadItem.DownloadId,
                trackedDownload.RemoteEpisode.Episodes.Select(e => e.Id).Where(id => id > 0).Distinct().OrderBy(id => id)));

            var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;
            var importResults = _downloadedEpisodesImportService.ProcessPath(outputPath,
                ImportMode.Auto,
                trackedDownload.RemoteEpisode.Series,
                trackedDownload.ImportItem);

            if (VerifyImport(trackedDownload, importResults))
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;

            if (importResults.Empty())
            {
                trackedDownload.Warn("No files found are eligible for import in {0}", outputPath);

                return;
            }

            if (importResults.Count == 1)
            {
                var firstResult = importResults.First();

                if (_rejectedImportService.Process(trackedDownload, firstResult))
                {
                    if (trackedDownload.State != TrackedDownloadState.FailedPending)
                    {
                        SetStateToImportBlocked(trackedDownload);
                    }

                    return;
                }

                if (firstResult.ImportDecision.Rejections.FirstOrDefault()?.Reason == ImportRejectionReason.MultiSeason)
                {
                    trackedDownload.Warn(new TrackedDownloadStatusMessage(trackedDownload.DownloadItem.Title, firstResult.Errors));
                    SetStateToImportBlocked(trackedDownload);
                    return;
                }
            }

            var statusMessages = new List<TrackedDownloadStatusMessage>
                                 {
                                    new TrackedDownloadStatusMessage("One or more episodes expected in this release were not imported or missing from the release", new List<string>())
                                 };

            if (importResults.Any(c => c.Result != ImportResultType.Imported))
            {
                statusMessages.AddRange(
                    importResults
                        .Where(v => v.Result != ImportResultType.Imported && v.ImportDecision.LocalEpisode != null)
                        .OrderBy(v => v.ImportDecision.LocalEpisode.Path)
                        .Select(v =>
                            new TrackedDownloadStatusMessage(Path.GetFileName(v.ImportDecision.LocalEpisode.Path),
                                v.Errors)));
            }

            if (statusMessages.Any())
            {
                trackedDownload.Warn(statusMessages.ToArray());
                SetStateToImportBlocked(trackedDownload);
            }
        }

        public bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            if (trackedDownload.RemoteEpisode.Series.MediaType == MediaType.Movie && !VerifyMovieCoverage(trackedDownload, importResults))
            {
                _logger.Debug("Not all physical movie files and native collection features are accounted for in {0}", trackedDownload.DownloadItem.Title);
                return false;
            }

            var importedEpisodes = importResults.Where(c => c.Result == ImportResultType.Imported)
                                                .SelectMany(c => c.ImportDecision.LocalEpisode.Episodes)
                                                .ToList();
            var expectedEpisodeIds = trackedDownload.RemoteEpisode.Episodes.Select(e => e.Id).Where(id => id > 0).ToHashSet();
            var importedEpisodeIds = importedEpisodes.Select(e => e.Id).Where(id => id > 0).ToHashSet();
            var allEpisodesImported = expectedEpisodeIds.Count > 0
                ? expectedEpisodeIds.IsSubsetOf(importedEpisodeIds)
                : importedEpisodes.Count >= Math.Max(1, trackedDownload.RemoteEpisode.Episodes.Count);

            var historyItems = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                .OrderByDescending(h => h.Date)
                .ToList();

            var grabbedHistory = historyItems.Where(h => h.EventType == EpisodeHistoryEventType.Grabbed).ToList();
            var releaseInfo = grabbedHistory.Count > 0 ? new GrabbedReleaseInfo(grabbedHistory) : null;

            if (allEpisodesImported)
            {
                _logger.Debug("All episodes were imported for {0}", trackedDownload.DownloadItem.Title);
                trackedDownload.State = TrackedDownloadState.Imported;

                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload,
                    trackedDownload.RemoteEpisode.Series.Id,
                    importResults.Where(c => c.Result == ImportResultType.Imported).Select(c => c.EpisodeFile).ToList(),
                    releaseInfo));

                return true;
            }

            // Double check if all episodes were imported by checking the history if at least one
            // file was imported. This will allow the decision engine to reject already imported
            // episode files and still mark the download complete when all files are imported.

            // EDGE CASE: This process relies on EpisodeIds being consistent between executions, if a series is updated
            // and an episode is removed, but later comes back with a different ID then Sonarr will treat it as incomplete.
            // Since imports should be relatively fast and these types of data changes are infrequent this should be quite
            // safe, but commenting for future benefit.

            var atLeastOneEpisodeImported = importResults.Any(c => c.Result == ImportResultType.Imported);
            var allEpisodesImportedInHistory = _trackedDownloadAlreadyImported.IsImported(trackedDownload, historyItems);

            if (allEpisodesImportedInHistory)
            {
                // Log different error messages depending on the circumstances, but treat both as fully imported, because that's the reality.
                // The second message shouldn't be logged in most cases, but continued reporting would indicate an ongoing issue.

                if (atLeastOneEpisodeImported)
                {
                    _logger.Debug("All episodes were imported in history for {0}", trackedDownload.DownloadItem.Title);
                }
                else
                {
                    _logger.ForDebugEvent()
                           .Message("No Episodes were just imported, but all episodes were previously imported, possible issue with download history.")
                           .Property("SeriesId", trackedDownload.RemoteEpisode.Series.Id)
                           .Property("DownloadId", trackedDownload.DownloadItem.DownloadId)
                           .Property("Title", trackedDownload.DownloadItem.Title)
                           .Property("Path", trackedDownload.ImportItem.OutputPath.ToString())
                           .WriteSentryWarn("DownloadHistoryIncomplete")
                           .Log();
                }

                var episodes = _episodeService.GetEpisodes(trackedDownload.RemoteEpisode.Episodes.Select(e => e.Id));
                var files = _mediaFileService.GetFiles(episodes.Select(e => e.EpisodeFileId).Where(i => i > 0).Distinct());

                trackedDownload.State = TrackedDownloadState.Imported;
                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteEpisode.Series.Id, files, releaseInfo));

                return true;
            }

            if (VerifyRetainedEpisodes(trackedDownload, importResults, historyItems, importedEpisodeIds, releaseInfo))
            {
                return true;
            }

            _logger.Debug("Not all episodes have been imported for the release '{0}'", trackedDownload.DownloadItem.Title);
            return false;
        }

        private bool VerifyMovieCoverage(TrackedDownload download, List<ImportResult> results)
        {
            var context = _movieGrabContext.GetContext(download.DownloadItem.DownloadId);
            if (download.RemoteEpisode.ParsedEpisodeInfo is { IsMovieCollectionPack: true } &&
                context?.ParsedEpisodeInfo.IsMovieCollectionPack != true)
            {
                return false;
            }

            var members = context?.ParsedEpisodeInfo.IsMovieCollectionPack == true
                ? context.CollectionMovies
                : new List<Series> { download.RemoteEpisode.Series };
            foreach (var result in results.Where(result => result.Result != ImportResultType.Imported))
            {
                var local = result.ImportDecision.LocalEpisode;
                var rejections = result.ImportDecision.Rejections.ToList();
                var member = members.SingleOrDefault(movie => movie.Id == local?.Series?.Id);
                if (result.Result != ImportResultType.Rejected || member == null || local.Episodes?.Count != 1 ||
                    local.Episodes[0] is not { Id: > 0, SeasonNumber: 1, EpisodeNumber: 1 } native || native.SeriesId != member.Id ||
                    rejections.Count == 0 || !rejections.All(rejection => rejection.Reason is ImportRejectionReason.EpisodeAlreadyImported or
                        ImportRejectionReason.NotQualityUpgrade or ImportRejectionReason.NotRevisionUpgrade or ImportRejectionReason.NotCustomFormatUpgrade) ||
                    !HasReadableMovieFeature(member, native.Id))
                {
                    return false;
                }
            }

            return context?.ParsedEpisodeInfo.IsMovieCollectionPack != true || members.All(HasReadableMovieFeature);
        }

        private bool HasReadableMovieFeature(Series movie)
        {
            return HasReadableMovieFeature(movie, null);
        }

        private bool HasReadableMovieFeature(Series movie, int? episodeId)
        {
            var episodes = _episodeService.GetEpisodeBySeries(movie.Id);
            if (episodes?.Count != 1 || episodes[0].Id <= 0 || episodes[0].SeriesId != movie.Id ||
                episodes[0].SeasonNumber != 1 || episodes[0].EpisodeNumber != 1 || episodes[0].EpisodeFileId <= 0 ||
                movie.Path.IsNullOrWhiteSpace() || (episodeId.HasValue && episodes[0].Id != episodeId.Value))
            {
                return false;
            }

            var files = _mediaFileService.GetFiles(new[] { episodes[0].EpisodeFileId });
            if (files?.Count != 1 || files[0].Id != episodes[0].EpisodeFileId || files[0].SeriesId != movie.Id || files[0].RelativePath.IsNullOrWhiteSpace())
            {
                return false;
            }

            try
            {
                var path = Path.GetFullPath(Path.Combine(movie.Path, files[0].RelativePath));
                if (!movie.Path.IsParentPath(path))
                {
                    return false;
                }

                using var stream = _diskProvider.OpenReadStream(path);
                return stream.ReadByte() >= 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Debug(exception, "Unable to verify native movie feature for {0}", movie.Title);
                return false;
            }
        }

        private bool VerifyRetainedEpisodes(TrackedDownload trackedDownload,
                                            List<ImportResult> importResults,
                                            List<EpisodeHistory> historyItems,
                                            HashSet<int> importedEpisodeIds,
                                            GrabbedReleaseInfo releaseInfo)
        {
            var expectedEpisodes = trackedDownload.RemoteEpisode.Episodes;
            if (expectedEpisodes.Empty() || trackedDownload.RemoteEpisode.Series.Path.IsNullOrWhiteSpace())
            {
                return false;
            }

            var historyEpisodeIds = historyItems.GroupBy(h => h.EpisodeId)
                .Where(g => g.First().EventType == EpisodeHistoryEventType.DownloadFolderImported)
                .Select(g => g.Key).ToHashSet();
            var accountedEpisodeIds = importedEpisodeIds.Union(historyEpisodeIds).ToHashSet();
            var retainedEpisodeIds = new HashSet<int>();

            foreach (var result in importResults.Where(r => r.Result != ImportResultType.Imported))
            {
                var localEpisode = result.ImportDecision.LocalEpisode;
                var rejections = result.ImportDecision.Rejections.ToList();
                if (localEpisode?.Episodes == null || localEpisode.Episodes.Empty() ||
                    localEpisode.Series?.Id != trackedDownload.RemoteEpisode.Series.Id ||
                    result.Result != ImportResultType.Rejected || rejections.Empty())
                {
                    return false;
                }

                if (rejections.All(r => r.Reason == ImportRejectionReason.EpisodeAlreadyImported) &&
                    localEpisode.Episodes.All(e => accountedEpisodeIds.Contains(e.Id)))
                {
                    continue;
                }

                // Only explicit preference decisions can retain existing files. A second rejection
                // (mapping, permissions, format loss after rename, etc.) still requires attention.
                if (!rejections.All(r => r.Reason == ImportRejectionReason.NotQualityUpgrade ||
                                         r.Reason == ImportRejectionReason.NotRevisionUpgrade ||
                                         r.Reason == ImportRejectionReason.NotCustomFormatUpgrade))
                {
                    return false;
                }

                retainedEpisodeIds.UnionWith(localEpisode.Episodes.Select(e => e.Id));
            }

            accountedEpisodeIds.UnionWith(retainedEpisodeIds);
            if (retainedEpisodeIds.Empty() || !expectedEpisodes.All(e => accountedEpisodeIds.Contains(e.Id)))
            {
                return false;
            }

            // Re-read associations and open the files as the service user. A stale database record,
            // missing mount or unreadable file must never satisfy the download's expected coverage.
            var episodes = _episodeService.GetEpisodes(expectedEpisodes.Select(e => e.Id));
            var files = _mediaFileService.GetFiles(episodes.Select(e => e.EpisodeFileId).Where(i => i > 0).Distinct());
            var filesById = files.ToDictionary(f => f.Id);
            if (!expectedEpisodes.All(e => episodes.Any(current => current.Id == e.Id &&
                    current.SeriesId == trackedDownload.RemoteEpisode.Series.Id &&
                    filesById.TryGetValue(current.EpisodeFileId, out var file) &&
                    file.SeriesId == trackedDownload.RemoteEpisode.Series.Id &&
                    file.RelativePath.IsNotNullOrWhiteSpace())))
            {
                return false;
            }

            foreach (var file in files)
            {
                try
                {
                    var path = Path.Combine(trackedDownload.RemoteEpisode.Series.Path, file.RelativePath);
                    using var stream = _diskProvider.OpenReadStream(path);
                    if (stream.ReadByte() < 0)
                    {
                        return false;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _logger.Debug(ex, "Unable to verify retained library file {0}", file.Path);
                    return false;
                }
            }

            foreach (var episode in expectedEpisodes.Where(e => retainedEpisodeIds.Contains(e.Id)))
            {
                _logger.Info("Existing version retained for S{0:00}E{1:00} in {2}", episode.SeasonNumber, episode.EpisodeNumber, trackedDownload.DownloadItem.Title);
            }

            _logger.Info("All {0} expected episodes accounted for in {1}; {2} existing versions retained", expectedEpisodes.Count, trackedDownload.DownloadItem.Title, expectedEpisodes.Count(e => retainedEpisodeIds.Contains(e.Id)));
            trackedDownload.State = TrackedDownloadState.Imported;

            // Retained files were not imported from this download. Keep the completion notification's
            // file list limited to actual imports, without fabricating per-episode import history.
            var actuallyImportedFiles = files.Where(f => episodes.Any(e => e.EpisodeFileId == f.Id &&
                (importedEpisodeIds.Contains(e.Id) || historyEpisodeIds.Contains(e.Id)))).ToList();
            _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteEpisode.Series.Id, actuallyImportedFiles, releaseInfo));
            return true;
        }

        private void SetStateToImportBlocked(TrackedDownload trackedDownload)
        {
            trackedDownload.State = TrackedDownloadState.ImportBlocked;

            if (!trackedDownload.HasNotifiedManualInteractionRequired)
            {
                _logger.Warn("Import blocked for '{0}': {1}", trackedDownload.DownloadItem.Title, trackedDownload.StatusMessages.SelectMany(m => m.Messages).ConcatToString("; "));

                var grabbedHistories = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId).Where(h => h.EventType == EpisodeHistoryEventType.Grabbed).ToList();

                trackedDownload.HasNotifiedManualInteractionRequired = true;

                var releaseInfo = grabbedHistories.Count > 0 ? new GrabbedReleaseInfo(grabbedHistories) : null;
                var manualInteractionEvent = new ManualInteractionRequiredEvent(trackedDownload, releaseInfo);

                _eventAggregator.PublishEvent(manualInteractionEvent);
            }
        }

        private void ObserveProgress(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                _logger.Debug(e, "Unable to update import progress");
            }
        }

        private void SetImportItem(TrackedDownload trackedDownload)
        {
            trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
        }

        private bool ValidatePath(TrackedDownload trackedDownload)
        {
            var downloadItemOutputPath = trackedDownload.ImportItem.OutputPath;

            if (downloadItemOutputPath.IsEmpty)
            {
                trackedDownload.Warn("Download doesn't contain intermediate path, Skipping.");
                return false;
            }

            if ((OsInfo.IsWindows && !downloadItemOutputPath.IsWindowsPath) ||
                (OsInfo.IsNotWindows && !downloadItemOutputPath.IsUnixPath))
            {
                trackedDownload.Warn("[{0}] is not a valid local path. You may need a Remote Path Mapping. Check the download troubleshooting entry on the wiki for details.", downloadItemOutputPath);
                return false;
            }

            return true;
        }
    }
}
