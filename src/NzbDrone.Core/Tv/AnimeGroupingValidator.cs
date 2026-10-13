using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource.Grouping;

namespace NzbDrone.Core.Tv
{
    public static class AnimeGroupingValidator
    {
        public static bool HasGroup(Series series)
        {
            return series.AnimeGroupingMode != AnimeGroupingMode.Disabled &&
                   series.AnimeGroupId.IsNotNullOrWhiteSpace() &&
                   series.AnimeGroupTitle.IsNotNullOrWhiteSpace() &&
                   (series.AnimeGroupMemberKind == AnimeGroupMemberKind.Movie
                       ? series.AnimeGroupMovieNumber.HasValue
                       : series.AnimeGroupSeasonNumber.HasValue);
        }

        public static void Validate(Series series, IEnumerable<Series> existingSeries)
        {
            if (!Enum.IsDefined(series.AnimeGroupingMode))
            {
                Fail("AnimeGroupingMode", "Choose Automatic, Manual or Disabled anime grouping");
            }

            if (series.AnimeGroupingMode == AnimeGroupingMode.Disabled)
            {
                return;
            }

            if (series.AnimeGroupingMode == AnimeGroupingMode.Automatic && series.AnimeGroupId.IsNullOrWhiteSpace())
            {
                return;
            }

            if (!AnimeGroupingService.IsValidGroupId(series.AnimeGroupId))
            {
                Fail("AnimeGroupId", "Choose a valid anime group");
            }

            if (!AnimeGroupingService.IsValidGroupTitle(series.AnimeGroupTitle))
            {
                Fail("AnimeGroupTitle", "Enter a valid anime group title");
            }

            if (!Enum.IsDefined(series.AnimeGroupMemberKind) || !Enum.IsDefined(series.AnimeGroupKind) ||
                !Enum.IsDefined(series.AnimeGroupMovieFolderStyle))
            {
                Fail("AnimeGroupMemberKind", "Choose a valid member type, group type and movie folder style");
            }

            var isMovie = series.AnimeGroupMemberKind == AnimeGroupMemberKind.Movie;
            var number = isMovie ? series.AnimeGroupMovieNumber : series.AnimeGroupSeasonNumber;
            var property = isMovie ? "AnimeGroupMovieNumber" : "AnimeGroupSeasonNumber";
            if (number is not > 0 or > AnimeGroupingService.MaxSeasonNumber)
            {
                Fail(property, $"Choose a member number from 1 to {AnimeGroupingService.MaxSeasonNumber}");
            }

            if ((isMovie && series.AnimeGroupSeasonNumber.HasValue) || (!isMovie && series.AnimeGroupMovieNumber.HasValue))
            {
                Fail(property, "Movie and season numbers are separate; set only the number for the selected member type");
            }

            if (!isMovie && series.AnimeGroupKind == AnimeGroupKind.MovieSaga)
            {
                Fail("AnimeGroupKind", "A movie saga contains movie members; choose a series group for seasons");
            }

            if (series.Seasons.Where(s => s.SeasonNumber > 0).Select(s => s.SeasonNumber).Distinct().Skip(1).Any())
            {
                Fail(property, "Group a single catalogue entry; this entry already contains multiple native seasons");
            }

            ValidateSlot(series, existingSeries);
        }

        public static void ValidateSlot(Series series, IEnumerable<Series> existingSeries)
        {
            if (!HasGroup(series))
            {
                return;
            }

            if ((existingSeries ?? Enumerable.Empty<Series>()).Any(other =>
                    (series.Id == 0 || other.Id != series.Id) &&
                    string.Equals(other.AnimeGroupId, series.AnimeGroupId, StringComparison.Ordinal) &&
                    other.AnimeGroupMemberKind == series.AnimeGroupMemberKind &&
                    (series.AnimeGroupMemberKind == AnimeGroupMemberKind.Movie
                        ? other.AnimeGroupMovieNumber == series.AnimeGroupMovieNumber
                        : other.AnimeGroupSeasonNumber == series.AnimeGroupSeasonNumber)))
            {
                Fail(series.AnimeGroupMemberKind == AnimeGroupMemberKind.Movie ? "AnimeGroupMovieNumber" : "AnimeGroupSeasonNumber",
                     "This group already contains that member number");
            }

            if (series.Path.IsNotNullOrWhiteSpace() && (existingSeries ?? Enumerable.Empty<Series>()).Any(other =>
                    (series.Id == 0 || other.Id != series.Id) && other.Path.IsNotNullOrWhiteSpace() &&
                    (other.Path.PathEquals(series.Path) || other.Path.IsParentPath(series.Path) || series.Path.IsParentPath(other.Path))))
            {
                Fail("Path", "The grouped member path overlaps an existing member; choose a distinct naming format or an explicit path");
            }
        }

        private static void Fail(string property, string message)
        {
            throw new ValidationException(new[] { new ValidationFailure(property, message) });
        }
    }
}
