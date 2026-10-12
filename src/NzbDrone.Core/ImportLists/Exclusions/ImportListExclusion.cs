using NzbDrone.Core.Datastore;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    public class ImportListExclusion : ModelBase
    {
        public int TvdbId { get; set; }
        public string Title { get; set; }
        public MetadataSourceType MetadataSource { get; set; }
        public string ForeignId { get; set; }
    }
}
