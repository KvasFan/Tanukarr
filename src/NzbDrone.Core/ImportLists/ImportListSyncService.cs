using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists
{
    public class ImportListSyncService : IExecute<ImportListSyncCommand>, IHandleAsync<ProviderDeletedEvent<IImportList>>
    {
        private readonly IImportListFactory _importListFactory;
        private readonly IImportListStatusService _importListStatusService;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly IImportListItemService _importListItemService;
        private readonly IFetchAndParseImportList _listFetcherAndParser;
        private readonly ISearchForNewSeries _seriesSearchService;
        private readonly ISeriesService _seriesService;
        private readonly IAddSeriesService _addSeriesService;
        private readonly IConfigService _configService;
        private readonly ITaskManager _taskManager;
        private readonly Logger _logger;

        public ImportListSyncService(IImportListFactory importListFactory,
            IImportListStatusService importListStatusService,
            IImportListExclusionService importListExclusionService,
            IImportListItemService importListItemService,
            IFetchAndParseImportList listFetcherAndParser,
            ISearchForNewSeries seriesSearchService,
            ISeriesService seriesService,
            IAddSeriesService addSeriesService,
            IConfigService configService,
            ITaskManager taskManager,
            Logger logger)
        {
            _importListFactory = importListFactory;
            _importListStatusService = importListStatusService;
            _importListExclusionService = importListExclusionService;
            _importListItemService = importListItemService;
            _listFetcherAndParser = listFetcherAndParser;
            _seriesSearchService = seriesSearchService;
            _seriesService = seriesService;
            _addSeriesService = addSeriesService;
            _configService = configService;
            _taskManager = taskManager;
            _logger = logger;
        }

        private bool AllListsSuccessfulWithAPendingClean()
        {
            var lists = _importListFactory.AutomaticAddEnabled(false);
            var anyRemoved = false;

            foreach (var list in lists)
            {
                var status = _importListStatusService.GetListStatus(list.Definition.Id);

                if (status.DisabledTill.HasValue)
                {
                    // list failed the last time it was synced.
                    return false;
                }

                if (!status.LastInfoSync.HasValue)
                {
                    // list has never been synced.
                    return false;
                }

                anyRemoved |= status.HasRemovedItemSinceLastClean;
            }

            return anyRemoved;
        }

        private void SyncAll()
        {
            if (_importListFactory.AutomaticAddEnabled().Empty())
            {
                _logger.Debug("No import lists with automatic add enabled");

                return;
            }

            _logger.ProgressInfo("Starting Import List Sync");

            var result = _listFetcherAndParser.Fetch();

            var listItems = result.Series.ToList();

            ProcessListItems(listItems);

            RemoveTagsFromSeriesNotOnLists();

            TryCleanLibrary();
        }

        private void SyncList(ImportListDefinition definition)
        {
            _logger.ProgressInfo("Starting Import List Refresh for List {0}", definition.Name);

            var result = _listFetcherAndParser.FetchSingleList(definition);

            var listItems = result.Series.ToList();

            ProcessListItems(listItems);

            RemoveTagsFromSeriesNotOnLists();

            TryCleanLibrary();
        }

        private void ProcessListItems(List<ImportListItemInfo> items)
        {
            var seriesToAdd = new List<Series>();

            if (items.Count == 0)
            {
                _logger.ProgressInfo("No list items to process");

                return;
            }

            _logger.ProgressInfo("Processing {0} list items", items.Count);

            var reportNumber = 1;

            var listExclusions = _importListExclusionService.All();
            var importLists = _importListFactory.All();

            var existingSeries = _seriesService.GetAllSeries();

            var existingSeriesToUpdate = new Dictionary<int, HashSet<int>>();

            foreach (var item in items)
            {
                _logger.ProgressTrace("Processing list item {0}/{1}", reportNumber, items.Count);

                reportNumber++;

                var importList = importLists.Single(x => x.Id == item.ImportListId);

                if (!importList.EnableAutomaticAdd)
                {
                    continue;
                }

                var existing = existingSeries.FirstOrDefault(s => ImportListIdentity.Matches(s, item));
                Series candidate;

                try
                {
                    candidate = existing ?? ImportListIdentity.CreateSeries(item, _seriesSearchService, importList.Language);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.Debug("[{0}] Rejected: {1}", item.Title, ex.Message);
                    continue;
                }

                existing ??= existingSeries.FirstOrDefault(s => SameSeries(s, candidate));

                if (listExclusions.Any(exclusion => ImportListIdentity.IsExcluded(exclusion, candidate)))
                {
                    _logger.Debug("{0} [{1}] Rejected due to list exclusion", candidate.ForeignId, item.Title);
                    continue;
                }

                if (existing != null)
                {
                    QueueTagsOnPendingSeries(importList, existingSeriesToUpdate, existing.Id);
                    _logger.Debug("{0} [{1}] Rejected, series exists in database", candidate.ForeignId, item.Title);
                    continue;
                }

                var pendingSeries = seriesToAdd.FirstOrDefault(s => ImportListIdentity.Matches(s, item) || SameSeries(s, candidate));

                if (pendingSeries == null)
                {
                    candidate.Monitored = importList.ShouldMonitor != MonitorTypes.None;
                    candidate.MonitorNewItems = importList.MonitorNewItems;
                    candidate.RootFolderPath = importList.RootFolderPath;
                    candidate.QualityProfileId = importList.QualityProfileId;
                    candidate.SeriesType = candidate.MetadataSource is MetadataSourceType.AniList or MetadataSourceType.MyAnimeList
                        ? SeriesTypes.Anime
                        : importList.SeriesType;
                    candidate.SeasonType = SeasonType.Official;
                    candidate.Language = importList.Language;
                    candidate.SeasonFolder = importList.SeasonFolder;
                    candidate.Seasons = item.Seasons;
                    candidate.Tags = new HashSet<int>(importList.Tags);
                    candidate.AddOptions = new AddSeriesOptions
                    {
                        SearchForMissingEpisodes = importList.SearchForMissingEpisodes,
                        Monitor = item.Seasons.Any() ? MonitorTypes.Skip : importList.ShouldMonitor
                    };
                    seriesToAdd.Add(candidate);
                }
                else
                {
                    pendingSeries.Tags.UnionWith(importList.Tags);
                }
            }

            var addedSeries = _addSeriesService.AddSeries(seriesToAdd, true);
            UpdateTagsOnPendingSeries(existingSeriesToUpdate);

            _logger.ProgressInfo("Import List Sync Completed. Items found: {0}, Series added: {1}", items.Count, addedSeries?.Count ?? 0);
        }

        private static bool SameSeries(Series first, Series second)
        {
            return (first.TvdbId > 0 && second.TvdbId > 0 && first.TvdbId == second.TvdbId) ||
                   (first.MetadataSource == second.MetadataSource && first.ForeignId.IsNotNullOrWhiteSpace() && first.ForeignId == second.ForeignId);
        }

        private void QueueTagsOnPendingSeries(ImportListDefinition importList, Dictionary<int, HashSet<int>> existingSeriesToUpdate, int existingSeriesId)
        {
            if (importList.TagExisting == TagExistingType.None || importList.Tags.Count == 0)
            {
                return;
            }

            if (existingSeriesToUpdate.TryGetValue(existingSeriesId, out var tagsToAdd))
            {
                foreach (var importListTag in importList.Tags)
                {
                    tagsToAdd.Add(importListTag);
                }
            }
            else
            {
                existingSeriesToUpdate.Add(existingSeriesId, new HashSet<int>(importList.Tags));
            }
        }

        private void UpdateTagsOnPendingSeries(Dictionary<int, HashSet<int>> existingSeriesToUpdate)
        {
            if (existingSeriesToUpdate.Count == 0)
            {
                return;
            }

            var possibleSeriesToUpdate = _seriesService.GetSeries(existingSeriesToUpdate.Keys);
            var seriesWithUpdatedTags = new List<Series>();

            foreach (var series in possibleSeriesToUpdate)
            {
                var tags = existingSeriesToUpdate[series.Id];
                var currentTagsCount = series.Tags.Count;

                foreach (var tag in tags)
                {
                    series.Tags.Add(tag);
                }

                if (currentTagsCount != series.Tags.Count)
                {
                    _logger.Debug("{0} [{1}] tagged existing series", series.TvdbId, series.Title);
                    seriesWithUpdatedTags.Add(series);
                }
            }

            _seriesService.UpdateTags(seriesWithUpdatedTags);
        }

        private void RemoveTagsFromSeriesNotOnLists()
        {
            var importLists = _importListFactory.All().Where(l => l.EnableAutomaticAdd).ToList();

            var tagsToSync = importLists
                .Where(l => l.TagExisting == TagExistingType.Sync)
                .SelectMany(l => l.Tags)
                .ToHashSet();

            if (tagsToSync.Empty())
            {
                return;
            }

            var listsByTag = tagsToSync.ToDictionary(t => t, t => importLists.Where(l => l.Tags.Contains(t)).ToList());

            tagsToSync.RemoveWhere(tag =>
            {
                var unsyncedList = listsByTag[tag].FirstOrDefault(l => !HasSyncedSuccessfully(l.Id));

                if (unsyncedList != null)
                {
                    _logger.Debug("Not removing tag {0}, import list {1} has not synced successfully", tag, unsyncedList.Name);
                    return true;
                }

                return false;
            });

            if (tagsToSync.Empty())
            {
                return;
            }

            var itemsByList = _importListItemService.GetAllForLists(importLists.Select(l => l.Id).ToList())
                .GroupBy(i => i.ImportListId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var seriesWithUpdatedTags = new List<Series>();

            foreach (var series in _seriesService.GetAllSeries())
            {
                var tagsToRemove = series.Tags
                    .Where(tag => tagsToSync.Contains(tag) &&
                                  !listsByTag[tag].Any(l => itemsByList.TryGetValue(l.Id, out var items) && IsSeriesOnList(series, items)))
                    .ToList();

                if (tagsToRemove.Empty())
                {
                    continue;
                }

                series.Tags.ExceptWith(tagsToRemove);

                _logger.Debug("{0} [{1}] no longer on import lists, removed tags", series.TvdbId, series.Title);
                seriesWithUpdatedTags.Add(series);
            }

            _seriesService.UpdateTags(seriesWithUpdatedTags);
        }

        private bool HasSyncedSuccessfully(int importListId)
        {
            var status = _importListStatusService.GetListStatus(importListId);

            return !status.DisabledTill.HasValue && status.LastInfoSync.HasValue;
        }

        private static bool IsSeriesOnList(Series series, IEnumerable<ImportListItemInfo> listItems)
        {
            return listItems.Any(item => ImportListIdentity.Matches(series, item));
        }

        public void Execute(ImportListSyncCommand message)
        {
            if (message.DefinitionId.HasValue)
            {
                SyncList(_importListFactory.Get(message.DefinitionId.Value));
            }
            else
            {
                SyncAll();
            }
        }

        private void TryCleanLibrary()
        {
            if (_configService.ListSyncLevel == ListSyncLevelType.Disabled)
            {
                return;
            }

            if (AllListsSuccessfulWithAPendingClean())
            {
                CleanLibrary();
            }
        }

        private void CleanLibrary()
        {
            if (_configService.ListSyncLevel == ListSyncLevelType.Disabled)
            {
                return;
            }

            var seriesToUpdate = new List<Series>();
            var seriesInLibrary = _seriesService.GetAllSeries();
            var allListItems = _importListItemService.All();

            foreach (var series in seriesInLibrary)
            {
                if (!IsSeriesOnList(series, allListItems))
                {
                    switch (_configService.ListSyncLevel)
                    {
                        case ListSyncLevelType.LogOnly:
                            _logger.Info("{0} was in your library, but not found in your lists --> You might want to unmonitor or remove it", series);
                            break;
                        case ListSyncLevelType.KeepAndUnmonitor when series.Monitored:
                            _logger.Info("{0} was in your library, but not found in your lists --> Keeping in library but unmonitoring it", series);
                            series.Monitored = false;
                            seriesToUpdate.Add(series);
                            break;
                        case ListSyncLevelType.KeepAndTag when !series.Tags.Contains(_configService.ListSyncTag):
                            _logger.Info("{0} was in your library, but not found in your lists --> Keeping in library but tagging it", series);
                            series.Tags.Add(_configService.ListSyncTag);
                            seriesToUpdate.Add(series);
                            break;
                        default:
                            break;
                    }
                }
            }

            _seriesService.UpdateSeries(seriesToUpdate, false);
            _importListStatusService.MarkListsAsCleaned();
        }

        public void HandleAsync(ProviderDeletedEvent<IImportList> message)
        {
            TryCleanLibrary();
        }
    }
}
