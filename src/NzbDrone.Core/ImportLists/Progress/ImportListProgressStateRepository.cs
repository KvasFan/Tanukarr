using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ImportLists.Progress
{
    public interface IImportListProgressStateRepository : IBasicRepository<ImportListProgressState>
    {
        ImportListProgressState ForListSeries(int importListId, int seriesId);
    }

    public class ImportListProgressStateRepository : BasicRepository<ImportListProgressState>, IImportListProgressStateRepository
    {
        public ImportListProgressStateRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public ImportListProgressState ForListSeries(int importListId, int seriesId)
        {
            return Query(state => state.ImportListId == importListId && state.SeriesId == seriesId).SingleOrDefault();
        }
    }
}
