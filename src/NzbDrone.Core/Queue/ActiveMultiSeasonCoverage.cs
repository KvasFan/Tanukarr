using System.Linq;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.Queue
{
    public static class ActiveMultiSeasonCoverage
    {
        public static bool IsProtected(Queue item)
        {
            return (item.TrackedDownloadState == TrackedDownloadState.Downloading ||
                    item.TrackedDownloadState == TrackedDownloadState.ImportPending ||
                    item.TrackedDownloadState == TrackedDownloadState.Importing) &&
                   item.RemoteEpisode?.ParsedEpisodeInfo?.FullSeason == true &&
                   item.RemoteEpisode.Episodes != null &&
                   item.RemoteEpisode.Episodes.Select(e => e.SeasonNumber).Distinct().Skip(1).Any();
        }
    }
}
