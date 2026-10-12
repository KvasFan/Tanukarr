using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    public interface IImportListExclusionService
    {
        ImportListExclusion Add(ImportListExclusion importListExclusion);
        List<ImportListExclusion> All();
        PagingSpec<ImportListExclusion> Paged(PagingSpec<ImportListExclusion> pagingSpec);
        void Delete(int id);
        void Delete(List<int> ids);
        ImportListExclusion Get(int id);
        ImportListExclusion FindByTvdbId(int tvdbId);
        ImportListExclusion Update(ImportListExclusion importListExclusion);
    }

    public class ImportListExclusionService : IImportListExclusionService, IHandleAsync<SeriesDeletedEvent>
    {
        private readonly IImportListExclusionRepository _repo;
        private readonly object _syncRoot = new object();

        public ImportListExclusionService(IImportListExclusionRepository repo)
        {
            _repo = repo;
        }

        public ImportListExclusion Add(ImportListExclusion importListExclusion)
        {
            return _repo.Insert(importListExclusion);
        }

        public ImportListExclusion Update(ImportListExclusion importListExclusion)
        {
            // Older clients only send the TVDB placeholder and title when editing an exclusion.
            if (importListExclusion.TvdbId < 0 && importListExclusion.ForeignId.IsNullOrWhiteSpace())
            {
                var existing = _repo.Get(importListExclusion.Id);
                importListExclusion.MetadataSource = existing.MetadataSource;
                importListExclusion.ForeignId = existing.ForeignId;
            }

            return _repo.Update(importListExclusion);
        }

        public void Delete(int id)
        {
            _repo.Delete(id);
        }

        public void Delete(List<int> ids)
        {
            _repo.DeleteMany(ids);
        }

        public ImportListExclusion Get(int id)
        {
            return _repo.Get(id);
        }

        public ImportListExclusion FindByTvdbId(int tvdbId)
        {
            return _repo.FindByTvdbId(tvdbId);
        }

        public List<ImportListExclusion> All()
        {
            return _repo.All().ToList();
        }

        public PagingSpec<ImportListExclusion> Paged(PagingSpec<ImportListExclusion> pagingSpec)
        {
            return _repo.GetPaged(pagingSpec);
        }

        public void HandleAsync(SeriesDeletedEvent message)
        {
            if (!message.AddImportListExclusion)
            {
                return;
            }

            lock (_syncRoot)
            {
                AddExclusions(message);
            }
        }

        private void AddExclusions(SeriesDeletedEvent message)
        {
            var exclusionsToAdd = new List<ImportListExclusion>();
            var existingExclusions = _repo.All().ToList();

            foreach (var series in message.Series)
            {
                if (existingExclusions.Any(exclusion => ImportListIdentity.IsExcluded(exclusion, series)))
                {
                    continue;
                }

                var tvdbId = series.TvdbId;

                // The legacy column is unique. Deleted native series may reuse a synthetic
                // TVDB placeholder, so give the exclusion its own unused placeholder.
                if (series.MetadataSource != MetadataSourceType.Tvdb && existingExclusions.Any(exclusion => exclusion.TvdbId == tvdbId))
                {
                    var minimumId = existingExclusions.Min(exclusion => exclusion.TvdbId);
                    tvdbId = minimumId < 0 ? checked(minimumId - 1) : -1;
                }

                var exclusionToAdd = new ImportListExclusion
                {
                    TvdbId = tvdbId,
                    Title = series.Title,
                    MetadataSource = series.MetadataSource,
                    ForeignId = series.ForeignId
                };
                exclusionsToAdd.Add(exclusionToAdd);
                existingExclusions.Add(exclusionToAdd);
            }

            _repo.InsertMany(exclusionsToAdd);
        }
    }
}
