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
                   series.AnimeGroupTitle.IsNotNullOrWhiteSpace() && MemberNumber(series).HasValue;
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

            var number = MemberNumber(series);
            var property = MemberNumberProperty(series);
            if (number is not > 0 or > AnimeGroupingService.MaxSeasonNumber)
            {
                Fail(property, $"Choose a member number from 1 to {AnimeGroupingService.MaxSeasonNumber}");
            }

            var suppliedNumbers = new[] { series.AnimeGroupSeasonNumber, series.AnimeGroupMovieNumber, series.AnimeGroupSpecialNumber };
            if (suppliedNumbers.Count(value => value.HasValue) != 1)
            {
                Fail(property, "Season, movie and special numbers are separate; set only the number for the selected member type");
            }

            if (series.AnimeGroupMemberKind != AnimeGroupMemberKind.Movie && series.AnimeGroupKind == AnimeGroupKind.MovieSaga)
            {
                Fail("AnimeGroupKind", "A movie saga contains movie members; choose a series group for seasons or specials");
            }

            if (Slots(series).Any(slot => slot > AnimeGroupingService.MaxSeasonNumber))
            {
                Fail(property, "The native seasons exceed the available collection member numbers");
            }

            ValidateSlot(series, existingSeries);
        }

        public static void ValidateSlot(Series series, IEnumerable<Series> existingSeries)
        {
            if (!HasGroup(series))
            {
                return;
            }

            if (Slots(series).Any(slot => slot is < 1 or > AnimeGroupingService.MaxSeasonNumber))
            {
                Fail(MemberNumberProperty(series), "The native seasons exceed the available collection member numbers");
            }

            if ((existingSeries ?? Enumerable.Empty<Series>()).Any(other =>
                    (series.Id == 0 || other.Id != series.Id) &&
                    string.Equals(other.AnimeGroupId, series.AnimeGroupId, StringComparison.Ordinal) &&
                    other.AnimeGroupMemberKind == series.AnimeGroupMemberKind &&
                    Slots(other).Intersect(Slots(series)).Any()))
            {
                Fail(MemberNumberProperty(series), "This group already contains that member number");
            }

            if (series.Path.IsNotNullOrWhiteSpace() && (existingSeries ?? Enumerable.Empty<Series>()).Any(other =>
                    (series.Id == 0 || other.Id != series.Id) && other.Path.IsNotNullOrWhiteSpace() &&
                    (other.Path.PathEquals(series.Path) || other.Path.IsParentPath(series.Path) || series.Path.IsParentPath(other.Path))))
            {
                Fail("Path", "The grouped member path overlaps an existing member; choose a distinct naming format or an explicit path");
            }
        }

        public static int? MemberNumber(Series series)
        {
            return series.AnimeGroupMemberKind switch
            {
                AnimeGroupMemberKind.Movie => series.AnimeGroupMovieNumber,
                AnimeGroupMemberKind.Special => series.AnimeGroupSpecialNumber,
                _ => series.AnimeGroupSeasonNumber
            };
        }

        private static string MemberNumberProperty(Series series)
        {
            return series.AnimeGroupMemberKind switch
            {
                AnimeGroupMemberKind.Movie => "AnimeGroupMovieNumber",
                AnimeGroupMemberKind.Special => "AnimeGroupSpecialNumber",
                _ => "AnimeGroupSeasonNumber"
            };
        }

        private static IEnumerable<int> Slots(Series series)
        {
            var count = series.AnimeGroupMemberKind == AnimeGroupMemberKind.Season
                ? Math.Max(series.Seasons?.Where(season => season.SeasonNumber > 0).Select(season => season.SeasonNumber).Distinct().Count() ?? 0, 1) : 1;
            return Enumerable.Range(MemberNumber(series) ?? 0, count);
        }

        private static void Fail(string property, string message)
        {
            throw new ValidationException(new[] { new ValidationFailure(property, message) });
        }
    }
}
