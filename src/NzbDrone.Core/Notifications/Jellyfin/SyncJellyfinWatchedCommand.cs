using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public class SyncJellyfinWatchedCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
