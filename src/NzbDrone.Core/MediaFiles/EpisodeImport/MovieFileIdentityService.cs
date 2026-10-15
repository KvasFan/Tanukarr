using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.EpisodeImport
{
    public interface IMovieFileIdentityService
    {
        List<Episode> GetPersistedEpisodes(LocalEpisode localEpisode);
    }

    public class MovieFileIdentityService : IMovieFileIdentityService
    {
        private readonly IMediaFileService _files;
        private readonly IEpisodeService _episodes;

        public MovieFileIdentityService(IMediaFileService files, IEpisodeService episodes)
        {
            _files = files;
            _episodes = episodes;
        }

        public List<Episode> GetPersistedEpisodes(LocalEpisode localEpisode)
        {
            var series = localEpisode.Series;
            if (series?.MediaType != MediaType.Movie || series.Id <= 0 ||
                series.Path.IsNullOrWhiteSpace() || !series.Path.IsParentPath(localEpisode.Path))
            {
                return null;
            }

            var relativePath = series.Path.GetRelativePath(localEpisode.Path);
            var files = (_files.GetFilesWithRelativePath(series.Id, relativePath) ?? new List<EpisodeFile>())
                .Where(file => file.Id > 0 && file.SeriesId == series.Id && file.RelativePath.PathEquals(relativePath)).ToList();
            if (files.Count != 1)
            {
                return null;
            }

            var episodes = _episodes.GetEpisodesByFileId(files[0].Id);
            return episodes?.Count == 1 && episodes[0].SeriesId == series.Id && episodes[0].EpisodeFileId == files[0].Id
                ? episodes : null;
        }
    }
}
