using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public class JellyfinRefreshState : ModelBase
    {
        public int NotificationId { get; set; }
        public string TargetFingerprint { get; set; }
        public string StateToken { get; set; }
        public int LastRefreshMode { get; set; }
        public long ImportedGeneration { get; set; }
        public long ImmediateGeneration { get; set; }
        public long TickGeneration { get; set; }
        public DateTime LastTickUtc { get; set; }
    }
}
