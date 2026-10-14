using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public interface IJellyfinWatchStateRepository : IBasicRepository<JellyfinWatchState>
    {
        JellyfinWatchState ForNotification(int notificationId);
    }

    public class JellyfinWatchStateRepository : BasicRepository<JellyfinWatchState>, IJellyfinWatchStateRepository
    {
        public JellyfinWatchStateRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public JellyfinWatchState ForNotification(int notificationId)
        {
            return Query(state => state.NotificationId == notificationId).SingleOrDefault();
        }
    }
}
