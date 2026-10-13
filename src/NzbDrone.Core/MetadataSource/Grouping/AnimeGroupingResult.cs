using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.Grouping
{
    public enum AnimeGroupingStatus
    {
        Resolved,
        Existing,
        Manual,
        Disabled,
        Unrelated,
        Unsupported,
        Ambiguous,
        Unavailable,
        LimitReached
    }

    public class AnimeGroupingResult
    {
        public AnimeGroupingMode AnimeGroupingMode { get; set; }
        public AnimeGroupingStatus Status { get; set; }
        public string AnimeGroupId { get; set; }
        public string AnimeGroupTitle { get; set; }
        public int? AnimeGroupSeasonNumber { get; set; }
        public string AnimeGroupFolder { get; set; }
        public AnimeGroupMemberKind AnimeGroupMemberKind { get; set; }
        public AnimeGroupKind AnimeGroupKind { get; set; }
        public AnimeGroupMovieFolderStyle AnimeGroupMovieFolderStyle { get; set; }
        public int? AnimeGroupMovieNumber { get; set; }
        public string AnimeGroupMemberFolder { get; set; }
        internal string MemberTitle { get; set; }
        public bool IsResolved => Status == AnimeGroupingStatus.Resolved ||
                                  Status == AnimeGroupingStatus.Existing ||
                                  Status == AnimeGroupingStatus.Manual;
    }
}
