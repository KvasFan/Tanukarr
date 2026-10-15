using System.Collections.Generic;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class MovieSearchCriteria : SpecialEpisodeSearchCriteria
    {
        public List<Series> CollectionMovies { get; set; } = new List<Series>();

        public override string ToString() => $"[{Series.Title} ({Series.Year})] Movie";
    }
}
