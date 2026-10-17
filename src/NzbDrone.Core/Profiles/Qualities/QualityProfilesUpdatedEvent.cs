using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Profiles.Qualities;

// Raised once for a batch write that touches several profiles (quality definition
// size limits), so listeners that rebuild caches per update do the work a single time.
public class QualityProfilesUpdatedEvent(IReadOnlyList<int> ids) : IEvent
{
    public IReadOnlyList<int> Ids { get; private set; } = ids;
}
