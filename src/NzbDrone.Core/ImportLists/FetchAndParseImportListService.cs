using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Serializer;
using NzbDrone.Common.TPL;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists
{
    public interface IFetchAndParseImportList
    {
        ImportListFetchResult Fetch();
        ImportListFetchResult FetchSingleList(ImportListDefinition definition);
    }

    public class FetchAndParseImportListService : IFetchAndParseImportList
    {
        private readonly IImportListFactory _importListFactory;
        private readonly IImportListStatusService _importListStatusService;
        private readonly IImportListItemService _importListItemService;
        private readonly ICached<CompletedAniListFetch> _completedAniListFetches;
        private readonly Logger _logger;

        public FetchAndParseImportListService(IImportListFactory importListFactory,
                                             IImportListStatusService importListStatusService,
                                             IImportListItemService importListItemService,
                                             ICacheManager cacheManager,
                                             Logger logger)
        {
            _importListFactory = importListFactory;
            _importListStatusService = importListStatusService;
            _importListItemService = importListItemService;
            _completedAniListFetches = cacheManager.GetCache<CompletedAniListFetch>(typeof(FetchAndParseImportListService), "completedAniListFetches");
            _logger = logger;
        }

        public ImportListFetchResult Fetch()
        {
            var result = new ImportListFetchResult();

            var importLists = _importListFactory.AutomaticAddEnabled();

            if (!importLists.Any())
            {
                _logger.Debug("No enabled import lists, skipping.");
                return result;
            }

            _logger.Debug("Available import lists {0}", importLists.Count);

            var taskList = new List<Task>();
            var taskFactory = new TaskFactory(TaskCreationOptions.LongRunning, TaskContinuationOptions.None);

            foreach (var importList in importLists)
            {
                var importListLocal = importList;
                var status = _importListStatusService.GetListStatus(importListLocal.Definition.Id);
                var importListStatus = status.LastInfoSync;

                if (importListStatus.HasValue)
                {
                    var importListNextSync = importListStatus.Value + importListLocal.MinRefreshInterval;

                    if (DateTime.UtcNow < importListNextSync)
                    {
                        _logger.Trace("Skipping refresh of Import List {0} ({1}) due to minimum refresh interval. Next sync after {2}", importList.Name, importListLocal.Definition.Name, importListNextSync);
                        ResumeCompletedAniListFetch(importListLocal, status, result);
                        continue;
                    }
                }

                var task = taskFactory.StartNew(() =>
                     {
                         try
                         {
                             ForgetCompletedAniListFetch(importListLocal);
                             var fetchResult = importListLocal.Fetch();
                             var importListReports = fetchResult.Series;

                             lock (result)
                             {
                                 _logger.Debug("Found {0} reports from {1} ({2})", importListReports.Count, importList.Name, importListLocal.Definition.Name);

                                 if (!fetchResult.AnyFailure)
                                 {
                                     importListReports.ForEach(s => s.ImportListId = importList.Definition.Id);
                                     result.Series.AddRange(importListReports);
                                     var removed = _importListItemService.SyncSeriesForList(importListReports, importList.Definition.Id);
                                     _importListStatusService.UpdateListSyncStatus(importList.Definition.Id, removed > 0);
                                     RememberCompletedAniListFetch(importListLocal);
                                 }

                                 result.AnyFailure |= fetchResult.AnyFailure;
                             }
                         }
                         catch (Exception e)
                         {
                             lock (result)
                             {
                                 result.AnyFailure = true;
                             }

                             _importListStatusService.RecordFailure(importListLocal.Definition.Id);
                             _logger.Error(e, "Error during Import List Sync of {0} ({1})", importList.Name, importListLocal.Definition.Name);
                         }
                     }).LogExceptions();

                taskList.Add(task);
            }

            Task.WaitAll(taskList.ToArray());

            result.Series = result.Series.DistinctBy(r => new { r.ImportListId, r.TvdbId, r.ImdbId, r.TmdbId, r.AniListId, r.MalId, r.Title }).ToList();

            _logger.Debug("Found {0} total reports from {1} lists", result.Series.Count, importLists.Count);

            return result;
        }

        public ImportListFetchResult FetchSingleList(ImportListDefinition definition)
        {
            var result = new ImportListFetchResult();

            var importList = _importListFactory.GetInstance(definition);

            if (importList == null || !definition.EnableAutomaticAdd)
            {
                _logger.Debug("Import List {0} ({1}) is not enabled, skipping.", importList.Name, importList.Definition.Name);
                return result;
            }

            var taskList = new List<Task>();
            var taskFactory = new TaskFactory(TaskCreationOptions.LongRunning, TaskContinuationOptions.None);

            var importListLocal = importList;

            var task = taskFactory.StartNew(() =>
            {
                try
                {
                    ForgetCompletedAniListFetch(importListLocal);
                    var fetchResult = importListLocal.Fetch();
                    var importListReports = fetchResult.Series;

                    lock (result)
                    {
                        _logger.Debug("Found {0} reports from {1} ({2})", importListReports.Count, importList.Name, importListLocal.Definition.Name);

                        if (!fetchResult.AnyFailure)
                        {
                            importListReports.ForEach(s => s.ImportListId = importList.Definition.Id);
                            result.Series.AddRange(importListReports);
                            var removed = _importListItemService.SyncSeriesForList(importListReports, importList.Definition.Id);
                            _importListStatusService.UpdateListSyncStatus(importList.Definition.Id, removed > 0);
                            RememberCompletedAniListFetch(importListLocal);
                        }

                        result.AnyFailure |= fetchResult.AnyFailure;
                    }

                    result.AnyFailure |= fetchResult.AnyFailure;
                }
                catch (Exception e)
                {
                    lock (result)
                    {
                        result.AnyFailure = true;
                    }

                    _importListStatusService.RecordFailure(importListLocal.Definition.Id);
                    _logger.Error(e, "Error during Import List Sync of {0} ({1})", importList.Name, importListLocal.Definition.Name);
                }
            }).LogExceptions();

            taskList.Add(task);

            Task.WaitAll(taskList.ToArray());

            return result;
        }

        private static bool IsAniListImport(IImportList importList)
        {
            return importList.Definition is ImportListDefinition definition &&
                   definition.Implementation == nameof(AniListImport) &&
                   definition.Settings is AniListSettings;
        }

        private static string DefinitionFingerprint(IImportList importList)
        {
            var definition = (ImportListDefinition)importList.Definition;

            // Include credentials and every behavior-setting in the digest, but never
            // retain or log their serialized values. Runtime status is checked separately.
            var configuration = new
            {
                definition.Id,
                definition.Name,
                definition.Implementation,
                definition.ConfigContract,
                definition.EnableAutomaticAdd,
                definition.SearchForMissingEpisodes,
                definition.ShouldMonitor,
                definition.MonitorNewItems,
                definition.QualityProfileId,
                definition.SeriesType,
                definition.Language,
                definition.SeasonFolder,
                definition.RootFolderPath,
                definition.TagExisting,
                Tags = definition.Tags?.OrderBy(tag => tag).ToArray(),
                definition.Settings,
                importList.ListType,
                importList.MinRefreshInterval
            };

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration.ToJson())));
        }

        private void ForgetCompletedAniListFetch(IImportList importList)
        {
            _completedAniListFetches.Remove(importList.Definition.Id.ToString(CultureInfo.InvariantCulture));
        }

        private void RememberCompletedAniListFetch(IImportList importList)
        {
            if (!IsAniListImport(importList) || !((ImportListDefinition)importList.Definition).EnableAutomaticAdd)
            {
                return;
            }

            // Read the actual persisted status after UpdateListSyncStatus, rather than
            // estimating its timestamp or trusting an older factory definition snapshot.
            var status = _importListStatusService.GetListStatus(importList.Definition.Id);

            if (!status.LastInfoSync.HasValue || status.DisabledTill.HasValue ||
                status.MostRecentFailure >= status.LastInfoSync)
            {
                return;
            }

            _completedAniListFetches.Set(
                importList.Definition.Id.ToString(CultureInfo.InvariantCulture),
                new CompletedAniListFetch
                {
                    Fingerprint = DefinitionFingerprint(importList),
                    LastInfoSync = status.LastInfoSync.Value
                },
                importList.MinRefreshInterval);
        }

        private void ResumeCompletedAniListFetch(IImportList importList, ImportListStatus status, ImportListFetchResult result)
        {
            if (!IsAniListImport(importList))
            {
                ForgetCompletedAniListFetch(importList);
                return;
            }

            var definition = (ImportListDefinition)importList.Definition;
            var key = definition.Id.ToString(CultureInfo.InvariantCulture);
            var completed = _completedAniListFetches.Find(key);

            if (completed == null)
            {
                return;
            }

            if (!definition.EnableAutomaticAdd || status.DisabledTill.HasValue ||
                status.LastInfoSync != completed.LastInfoSync ||
                status.MostRecentFailure >= completed.LastInfoSync ||
                completed.Fingerprint != DefinitionFingerprint(importList))
            {
                _completedAniListFetches.Remove(key);
                return;
            }

            // Only a complete fetch witnessed in this process may resume. Read the
            // authoritative saved membership; never recover from a failed HTTP response.
            // AniList list entries do not carry per-season monitor overrides.
            var items = _importListItemService.GetAllForLists(new List<int> { definition.Id })
                .Where(item => item.ImportListId == definition.Id)
                .Select(item => new ImportListItemInfo
                {
                    ImportListId = definition.Id,
                    ImportList = definition.Name,
                    Title = item.Title,
                    Year = item.Year,
                    TvdbId = item.TvdbId,
                    TmdbId = item.TmdbId,
                    ImdbId = item.ImdbId,
                    AniListId = item.AniListId,
                    MalId = item.MalId,
                    ReleaseDate = item.ReleaseDate
                })
                .ToList();

            lock (result)
            {
                result.Series.AddRange(items);
            }
        }

        private sealed class CompletedAniListFetch
        {
            public string Fingerprint { get; set; }
            public DateTime LastInfoSync { get; set; }
        }
    }
}
