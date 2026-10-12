using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Specifications
{
    public class MatchesFolderSpecification : IImportDecisionEngineSpecification
    {
        private readonly Logger _logger;
        private readonly IParsingService _parsingService;
        private readonly IEpisodeNumberingResolver _numberingResolver;

        public MatchesFolderSpecification(IParsingService parsingService, Logger logger, IEpisodeNumberingResolver numberingResolver)
        {
            _logger = logger;
            _parsingService = parsingService;
            _numberingResolver = numberingResolver;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            if (localEpisode.ExistingFile || localEpisode.ManualEpisodeMapping)
            {
                return ImportSpecDecision.Accept();
            }

            var fileInfo = localEpisode.FileEpisodeInfo;
            var folderInfo = localEpisode.FolderEpisodeInfo;

            if (fileInfo != null && fileInfo.IsPossibleSceneSeasonSpecial)
            {
                fileInfo = _parsingService.ParseSpecialEpisodeTitle(fileInfo, fileInfo.ReleaseTitle, localEpisode.Series.TvdbId, 0, null);
            }

            if (folderInfo != null && folderInfo.IsPossibleSceneSeasonSpecial)
            {
                folderInfo = _parsingService.ParseSpecialEpisodeTitle(folderInfo, folderInfo.ReleaseTitle, localEpisode.Series.TvdbId, 0, null);
            }

            if (folderInfo == null)
            {
                _logger.Debug("No folder ParsedEpisodeInfo, skipping check");
                return ImportSpecDecision.Accept();
            }

            if (fileInfo == null)
            {
                _logger.Debug("No file ParsedEpisodeInfo, skipping check");
                return ImportSpecDecision.Accept();
            }

            var folderEpisodes = _parsingService.GetEpisodes(folderInfo, localEpisode.Series, true);
            var fileEpisodes = _parsingService.GetEpisodes(fileInfo, localEpisode.Series, true);
            if (localEpisode.VerifiedNumbering.HasValue && localEpisode.Episodes.Any())
            {
                var numbering = _numberingResolver.Resolve(localEpisode.Series, folderInfo);
                if (numbering?.NeedsEvidence == true)
                {
                    numbering = _numberingResolver.Resolve(localEpisode.Series, folderInfo, null, localEpisode.VerifiedNumbering);
                }

                if (numbering?.Rejection != null)
                {
                    return ImportSpecDecision.Reject(ImportRejectionReason.AmbiguousNumbering, numbering.Rejection);
                }

                folderEpisodes = numbering?.Episodes ?? folderEpisodes;
                fileEpisodes = localEpisode.Episodes;
            }

            if (folderEpisodes.Empty())
            {
                _logger.Debug("No episode numbers in folder ParsedEpisodeInfo, skipping check");
                return ImportSpecDecision.Accept();
            }

            var unexpected = fileEpisodes.Where(e => folderEpisodes.All(o => o.Id != e.Id)).ToList();

            if (unexpected.Any())
            {
                _logger.Debug("Unexpected episode(s) in file: {0}", FormatEpisode(unexpected));

                if (unexpected.Count == 1)
                {
                    return ImportSpecDecision.Reject(ImportRejectionReason.EpisodeUnexpected, "Episode {0} was unexpected considering the {1} folder name", FormatEpisode(unexpected), folderInfo.ReleaseTitle);
                }

                return ImportSpecDecision.Reject(ImportRejectionReason.EpisodeUnexpected, "Episodes {0} were unexpected considering the {1} folder name", FormatEpisode(unexpected), folderInfo.ReleaseTitle);
            }

            return ImportSpecDecision.Accept();
        }

        private string FormatEpisode(List<Episode> episodes)
        {
            return string.Join(", ", episodes.Select(e => $"{e.SeasonNumber}x{e.EpisodeNumber:00}"));
        }
    }
}
