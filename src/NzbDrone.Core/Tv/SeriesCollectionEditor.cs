using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Core.MetadataSource.Grouping;

namespace NzbDrone.Core.Tv
{
    public static class SeriesCollectionEditor
    {
        public static void Apply(IReadOnlyCollection<Series> selected,
                                 IEnumerable<Series> existing,
                                 string groupTitle,
                                 AnimeGroupKind? kind,
                                 AnimeGroupMovieFolderStyle? movieFolderStyle,
                                 string collectionTitle)
        {
            var hasGroupChanges = groupTitle != null || kind.HasValue || movieFolderStyle.HasValue;
            if (!hasGroupChanges && collectionTitle == null)
            {
                return;
            }

            if (selected.Count == 0 || selected.Any(s => s.Id <= 0) || selected.Select(s => s.Id).Distinct().Count() != selected.Count)
            {
                Fail("SeriesIds", "Select the saved collection members");
            }

            var normalizedTitle = groupTitle?.Trim();
            var normalizedCollectionTitle = collectionTitle?.Trim();
            if (groupTitle != null && (!AnimeGroupingService.IsValidGroupTitle(normalizedTitle) || normalizedTitle is "." or ".."))
            {
                Fail("AnimeGroupTitle", "Enter a valid collection name");
            }

            if (collectionTitle != null && normalizedCollectionTitle.Length > 0 &&
                (!AnimeGroupingService.IsValidGroupTitle(normalizedCollectionTitle) || normalizedCollectionTitle is "." or ".."))
            {
                Fail("CollectionTitle", "Enter a valid collection name");
            }

            if (kind.HasValue && !Enum.IsDefined(kind.Value))
            {
                Fail("AnimeGroupKind", "Choose a collection type");
            }

            if (movieFolderStyle.HasValue && !Enum.IsDefined(movieFolderStyle.Value))
            {
                Fail("AnimeGroupMovieFolderStyle", "Choose a movie folder format");
            }

            if (collectionTitle != null && (hasGroupChanges || selected.Count != 1 || AnimeGroupingValidator.HasGroup(selected.First())))
            {
                Fail("CollectionTitle", "Edit a linked collection using its collection name");
            }

            if (hasGroupChanges)
            {
                var groupId = selected.First().AnimeGroupId;
                if (!selected.All(s => AnimeGroupingValidator.HasGroup(s) && string.Equals(s.AnimeGroupId, groupId, StringComparison.Ordinal)))
                {
                    Fail("SeriesIds", "Select members of one linked collection");
                }

                var members = existing.Where(s => AnimeGroupingValidator.HasGroup(s) &&
                                                  string.Equals(s.AnimeGroupId, groupId, StringComparison.Ordinal))
                                      .Select(s => s.Id).ToHashSet();
                if (!members.SetEquals(selected.Select(s => s.Id)))
                {
                    Fail("SeriesIds", "Select every member before changing collection settings");
                }

                if (kind == AnimeGroupKind.MovieSaga && selected.Any(s => s.AnimeGroupMemberKind != AnimeGroupMemberKind.Movie))
                {
                    Fail("AnimeGroupKind", "A movie saga contains only movies");
                }
            }

            foreach (var series in selected)
            {
                if (collectionTitle != null)
                {
                    series.CollectionTitle = normalizedCollectionTitle.Length == 0 ? null : normalizedCollectionTitle;
                }

                if (!hasGroupChanges)
                {
                    continue;
                }

                series.AnimeGroupingMode = AnimeGroupingMode.Manual;
                if (groupTitle != null)
                {
                    series.AnimeGroupTitle = normalizedTitle;
                }

                if (kind.HasValue)
                {
                    series.AnimeGroupKind = kind.Value;
                }

                if (movieFolderStyle.HasValue)
                {
                    series.AnimeGroupMovieFolderStyle = movieFolderStyle.Value;
                }
            }
        }

        private static void Fail(string property, string message)
        {
            throw new ValidationException(new[] { new ValidationFailure(property, message) });
        }
    }
}
