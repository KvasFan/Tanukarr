using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles.EpisodeImport
{
    public enum ImportProgressState
    {
        Waiting,
        Importing,
        Imported,
        Rejected,
        Failed
    }

    public sealed class ImportProgressKey : IEquatable<ImportProgressKey>
    {
        public int DownloadClientId { get; }
        public string DownloadId { get; }

        public ImportProgressKey(int downloadClientId, string downloadId)
        {
            DownloadClientId = downloadClientId;
            DownloadId = downloadId ?? string.Empty;
        }

        public bool Equals(ImportProgressKey other)
        {
            return other != null &&
                   DownloadClientId == other.DownloadClientId &&
                   string.Equals(DownloadId, other.DownloadId, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ImportProgressKey);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DownloadClientId, StringComparer.OrdinalIgnoreCase.GetHashCode(DownloadId));
        }
    }

    public sealed class ImportProgressItem
    {
        public string Id { get; init; }
        public List<int> EpisodeIds { get; init; }
        public int? SeasonNumber { get; init; }
        public List<int> EpisodeNumbers { get; init; }
        public string SourcePath { get; init; }
        public long Size { get; init; }
        public ImportProgressState State { get; init; }
        public DateTime? Started { get; init; }
        public DateTime? Completed { get; init; }
        public List<string> Errors { get; init; }
    }

    public sealed class ImportProgress
    {
        public int TotalEpisodes { get; init; }
        public int ImportedEpisodes { get; init; }
        public int ImportingEpisodes { get; init; }
        public int WaitingEpisodes { get; init; }
        public int FailedEpisodes { get; init; }
        public int RejectedEpisodes { get; init; }
        public List<ImportProgressItem> Items { get; init; }
    }
}
