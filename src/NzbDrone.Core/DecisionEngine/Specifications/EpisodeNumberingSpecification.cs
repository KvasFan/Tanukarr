using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class EpisodeNumberingSpecification : IDownloadDecisionEngineSpecification
    {
        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, ReleaseDecisionInformation information)
        {
            // Unresolved identity must not enter the delayed-download queue.
            return string.IsNullOrEmpty(remoteEpisode.NumberingRejection)
                ? DownloadSpecDecision.Accept()
                : DownloadSpecDecision.Reject(DownloadRejectionReason.AmbiguousNumbering, remoteEpisode.NumberingRejection);
        }
    }
}
