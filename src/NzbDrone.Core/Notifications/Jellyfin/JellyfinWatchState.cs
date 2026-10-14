using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public class JellyfinWatchState : ModelBase
    {
        public int NotificationId { get; set; }
        public string TargetFingerprint { get; set; }
        public DateTime LastSyncUtc { get; set; }
        public bool UnmonitorWatchedEpisodes { get; set; }
    }
}
