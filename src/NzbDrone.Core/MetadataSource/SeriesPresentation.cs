using System.Collections.Generic;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISeriesPresentationProvider
    {
        MetadataSourceType Source { get; }
        SeriesPresentation GetPresentation(Series identity);
    }

    public class SeriesPresentation
    {
        public string Overview { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; } = new List<MediaCover.MediaCover>();
    }
}
