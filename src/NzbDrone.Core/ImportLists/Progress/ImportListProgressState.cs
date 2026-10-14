using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ImportLists.Progress
{
    public class ImportListProgressState : ModelBase
    {
        public int ImportListId { get; set; }
        public int SeriesId { get; set; }
        public string TargetFingerprint { get; set; }
        public int PendingProgress { get; set; }
        public int AcknowledgedProgress { get; set; }
        public string AcknowledgedIdentityFingerprint { get; set; } = string.Empty;
        public int FailureCount { get; set; }
        public DateTime NextAttemptUtc { get; set; }
        public DateTime? LastAttemptUtc { get; set; }
    }
}
