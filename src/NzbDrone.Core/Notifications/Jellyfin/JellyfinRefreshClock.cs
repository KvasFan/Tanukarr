using System;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public interface IJellyfinRefreshClock
    {
        DateTime UtcNow { get; }
    }

    public class JellyfinRefreshClock : IJellyfinRefreshClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
