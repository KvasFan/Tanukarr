using System;
using System.Globalization;
using System.IO;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Organizer
{
    public interface IAnimeGroupPathBuilder
    {
        string BuildPath(Series series);
    }

    public class AnimeGroupPathBuilder : IAnimeGroupPathBuilder
    {
        private readonly ISeriesRepository _seriesRepository;

        public AnimeGroupPathBuilder(ISeriesRepository seriesRepository)
        {
            _seriesRepository = seriesRepository;
        }

        public string BuildPath(Series series)
        {
            var root = Path.GetFullPath(series.RootFolderPath);
            var folder = series.AnimeGroupFolder;

            if (folder.IsNullOrWhiteSpace() || folder is "." or ".." ||
                folder.IndexOfAny(new[] { '/', '\\' }) >= 0 || Path.IsPathRooted(folder))
            {
                throw new ValidationException(new[] { new ValidationFailure("AnimeGroupFolder", "The anime group folder must be a single safe directory name") });
            }

            var memberFolder = GetMemberDirectoryName(series);
            if (!IsSafeFolder(memberFolder))
            {
                throw new ValidationException(new[] { new ValidationFailure("AnimeGroupMemberFolder", "The group member folder must be a single safe directory name") });
            }

            var destination = Path.GetFullPath(Path.Combine(root, folder, memberFolder));

            if (!root.IsParentPath(destination))
            {
                throw new ValidationException(new[] { new ValidationFailure("Path", "The anime group must remain inside the selected root folder") });
            }

            var paths = _seriesRepository.AllSeriesPaths();
            if (paths != null)
            {
                foreach (var other in paths)
                {
                    if (other.Key != series.Id && other.Value.IsNotNullOrWhiteSpace() &&
                        (other.Value.PathEquals(destination) || other.Value.IsParentPath(destination) || destination.IsParentPath(other.Value)))
                    {
                        throw new ValidationException(new[] { new ValidationFailure("Path", "The grouped member path overlaps an existing series. Move that series to its own member directory explicitly before adding this member") });
                    }
                }
            }

            var groupPath = Path.GetDirectoryName(destination);
            var otherGroups = _seriesRepository.All();
            if (otherGroups != null && otherGroups.Any(other =>
                    other.Id != series.Id && other.Path.IsNotNullOrWhiteSpace() &&
                    !string.Equals(other.AnimeGroupId, series.AnimeGroupId, StringComparison.Ordinal) &&
                    Path.GetDirectoryName(other.Path)?.PathEquals(groupPath) == true))
            {
                throw new ValidationException(new[] { new ValidationFailure("AnimeGroupTitle", "A different anime group already uses this folder. Choose a distinct group title or the existing group") });
            }

            return destination;
        }

        public static string GetSeasonDirectoryName(int seasonNumber)
        {
            return "Season " + seasonNumber.ToString("00", CultureInfo.InvariantCulture);
        }

        public static bool IsGroupedSeasonPath(Series series)
        {
            return IsGroupedMemberPath(series);
        }

        public static bool IsGroupedMemberPath(Series series)
        {
            if (!AnimeGroupingValidator.HasGroup(series) || series.Path.IsNullOrWhiteSpace() || series.AnimeGroupFolder.IsNullOrWhiteSpace())
            {
                return false;
            }

            var path = series.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetFileName(path), GetMemberDirectoryName(series), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), series.AnimeGroupFolder, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetMemberDirectoryName(Series series)
        {
            // Migration 244 members retain their original season folders. New members
            // receive the configured, sanitized folder from the grouping service.
            return series.AnimeGroupMemberFolder.IsNotNullOrWhiteSpace()
                ? series.AnimeGroupMemberFolder
                : series.AnimeGroupMemberKind == AnimeGroupMemberKind.Season && series.AnimeGroupSeasonNumber.HasValue
                    ? GetSeasonDirectoryName(series.AnimeGroupSeasonNumber.Value)
                    : null;
        }

        private static bool IsSafeFolder(string folder)
        {
            return folder.IsNotNullOrWhiteSpace() && folder is not "." and not ".." &&
                   folder.IndexOfAny(new[] { '/', '\\' }) < 0 && !Path.IsPathRooted(folder) &&
                   !folder.Any(char.IsControl);
        }
    }
}
