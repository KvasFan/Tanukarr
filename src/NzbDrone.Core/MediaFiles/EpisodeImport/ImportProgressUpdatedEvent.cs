using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.EpisodeImport
{
    public sealed class ImportProgressUpdatedEvent : IEvent
    {
        public ImportProgressKey Key { get; }

        public ImportProgressUpdatedEvent(ImportProgressKey key)
        {
            Key = key;
        }
    }
}
