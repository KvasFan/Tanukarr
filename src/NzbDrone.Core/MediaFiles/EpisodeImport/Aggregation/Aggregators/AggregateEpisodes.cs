using System.Collections.Generic;
using System.IO;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators
{
    public class AggregateEpisodes : IAggregateLocalEpisode
    {
        public int Order => 1;

        private readonly IParsingService _parsingService;
        private readonly IEpisodeNumberingResolver _numberingResolver;
        private readonly IMovieFileIdentityService _movieFileIdentity;

        public AggregateEpisodes(IParsingService parsingService, IEpisodeNumberingResolver numberingResolver, IMovieFileIdentityService movieFileIdentity)
        {
            _parsingService = parsingService;
            _numberingResolver = numberingResolver;
            _movieFileIdentity = movieFileIdentity;
        }

        public LocalEpisode Aggregate(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            localEpisode.VerifiedNumbering = null;
            if (localEpisode.Series.MediaType == MediaType.Movie)
            {
                var persistedEpisodes = _movieFileIdentity.GetPersistedEpisodes(localEpisode);
                if (persistedEpisodes?.Count == 1)
                {
                    localEpisode.Episodes = persistedEpisodes;
                    localEpisode.NumberingRejection = null;
                    return localEpisode;
                }

                var parsedMovie = MovieReleaseParser.Match(localEpisode.Path, localEpisode.Series);
                localEpisode.Episodes = parsedMovie == null ? new List<Episode>() : _parsingService.GetEpisodes(parsedMovie, localEpisode.Series, false);
                localEpisode.NumberingRejection = localEpisode.Episodes.Count == 1 ? null : "Unable to identify exactly one native movie feature from the physical filename.";
                return localEpisode;
            }

            if (localEpisode.SceneSource && !localEpisode.ExistingFile && localEpisode.Series.UseSceneNumbering)
            {
                // Aggregation catches exceptions; retain a rejection if verification fails.
                localEpisode.NumberingRejection = "Unable to verify scene/library numbering";
            }

            localEpisode.Episodes = GetEpisodes(localEpisode);

            // Use the physical file's coordinates and title whenever available. A pack
            // or download name cannot establish that a differently titled file is correct.
            var parsed = localEpisode.FileEpisodeInfo;
            if (parsed == null || parsed.FullSeason)
            {
                parsed = GetBestEpisodeInfo(localEpisode);
            }

            var numbering = _numberingResolver.ResolveLocal(localEpisode, parsed);
            localEpisode.NumberingRejection = numbering?.Rejection;
            if (numbering?.Episodes != null)
            {
                localEpisode.Episodes = numbering.Episodes;
                localEpisode.VerifiedNumbering = numbering.Convention;
            }

            return localEpisode;
        }

        private ParsedEpisodeInfo GetBestEpisodeInfo(LocalEpisode localEpisode)
        {
            var parsedEpisodeInfo = localEpisode.FileEpisodeInfo;
            var downloadClientEpisodeInfo = localEpisode.DownloadClientEpisodeInfo;
            var folderEpisodeInfo = localEpisode.FolderEpisodeInfo;

            if (!localEpisode.OtherVideoFiles && !SceneChecker.IsSceneTitle(Path.GetFileNameWithoutExtension(localEpisode.Path)))
            {
                if (downloadClientEpisodeInfo != null &&
                    !downloadClientEpisodeInfo.FullSeason &&
                    PreferOtherEpisodeInfo(parsedEpisodeInfo, downloadClientEpisodeInfo))
                {
                    parsedEpisodeInfo = localEpisode.DownloadClientEpisodeInfo;
                }
                else if (folderEpisodeInfo != null &&
                         !folderEpisodeInfo.FullSeason &&
                         PreferOtherEpisodeInfo(parsedEpisodeInfo, folderEpisodeInfo))
                {
                    parsedEpisodeInfo = localEpisode.FolderEpisodeInfo;
                }
            }

            if (parsedEpisodeInfo == null)
            {
                parsedEpisodeInfo = GetSpecialEpisodeInfo(localEpisode, parsedEpisodeInfo);
            }

            return parsedEpisodeInfo;
        }

        private ParsedEpisodeInfo GetSpecialEpisodeInfo(LocalEpisode localEpisode, ParsedEpisodeInfo parsedEpisodeInfo)
        {
            var title = Path.GetFileNameWithoutExtension(localEpisode.Path);
            var specialEpisodeInfo = _parsingService.ParseSpecialEpisodeTitle(parsedEpisodeInfo, title, localEpisode.Series);

            return specialEpisodeInfo;
        }

        private List<Episode> GetEpisodes(LocalEpisode localEpisode)
        {
            var bestEpisodeInfoForEpisodes = GetBestEpisodeInfo(localEpisode);
            var isMediaFile = MediaFileExtensions.Extensions.Contains(Path.GetExtension(localEpisode.Path));

            if (bestEpisodeInfoForEpisodes == null)
            {
                return new List<Episode>();
            }

            if (ValidateParsedEpisodeInfo.ValidateForSeriesType(bestEpisodeInfoForEpisodes, localEpisode.Series, isMediaFile))
            {
                var episodes = _parsingService.GetEpisodes(bestEpisodeInfoForEpisodes, localEpisode.Series, localEpisode.SceneSource);

                if (episodes.Empty() && bestEpisodeInfoForEpisodes.IsPossibleSpecialEpisode)
                {
                    var parsedSpecialEpisodeInfo = GetSpecialEpisodeInfo(localEpisode, bestEpisodeInfoForEpisodes);

                    if (parsedSpecialEpisodeInfo != null)
                    {
                        episodes = _parsingService.GetEpisodes(parsedSpecialEpisodeInfo, localEpisode.Series, localEpisode.SceneSource);
                    }
                }

                return episodes;
            }

            return new List<Episode>();
        }

        private bool PreferOtherEpisodeInfo(ParsedEpisodeInfo fileEpisodeInfo, ParsedEpisodeInfo otherEpisodeInfo)
        {
            if (fileEpisodeInfo == null)
            {
                return true;
            }

            // When the files episode info is not absolute prefer it over a parsed episode info that is absolute
            if (!fileEpisodeInfo.IsAbsoluteNumbering && otherEpisodeInfo.IsAbsoluteNumbering)
            {
                return false;
            }

            return true;
        }
    }
}
