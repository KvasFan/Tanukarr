using System;
using System.Linq;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.DecisionEngine.Specifications.RssSync
{
    public class CompleteSeasonUpgradeSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly IEpisodeService _episodeService;

        public CompleteSeasonUpgradeSpecification(IEpisodeService episodeService)
        {
            _episodeService = episodeService;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, ReleaseDecisionInformation information)
        {
            // Explicit searches retain their existing upgrade rules. Also allow new
            // episodes before their scheduled air time; other specifications apply.
            if (information.SearchCriteria != null || subject.Episodes.Count == 0 ||
                subject.Episodes.Any(e => e.Monitored && !e.HasFile))
            {
                return DownloadSpecDecision.Accept();
            }

            var now = DateTime.UtcNow;
            foreach (var season in subject.Episodes.Select(e => e.SeasonNumber).Distinct())
            {
                if (_episodeService.GetEpisodesBySeason(subject.Series.Id, season)
                    .Any(e => e.Monitored && !e.HasFile && e.AirDateUtc.HasValue && e.AirDateUtc.Value <= now))
                {
                    return DownloadSpecDecision.Accept();
                }
            }

            return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskNotUpgrade,
                "Automatic feed upgrades are disabled for fully downloaded seasons");
        }
    }
}
