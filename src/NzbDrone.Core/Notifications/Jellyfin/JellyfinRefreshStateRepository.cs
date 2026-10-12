using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public interface IJellyfinRefreshStateRepository : IBasicRepository<JellyfinRefreshState>
    {
        JellyfinRefreshState ForNotification(int notificationId);
    }

    public class JellyfinRefreshStateRepository : BasicRepository<JellyfinRefreshState>, IJellyfinRefreshStateRepository
    {
        public JellyfinRefreshStateRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public JellyfinRefreshState ForNotification(int notificationId)
        {
            return Query(state => state.NotificationId == notificationId).SingleOrDefault();
        }
    }
}
