using System;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists
{
    public static class ImportListIdentity
    {
        public static bool Matches(Series series, ImportListItemInfo item)
        {
            if (item.AniListId > 0 && series.MetadataSource == MetadataSourceType.AniList && series.ForeignId.IsNotNullOrWhiteSpace())
            {
                return series.ForeignId == item.AniListId.ToString(CultureInfo.InvariantCulture);
            }

            return (item.TvdbId > 0 && series.TvdbId > 0 && item.TvdbId == series.TvdbId) ||
                   (item.TmdbId > 0 && series.TmdbId > 0 && item.TmdbId == series.TmdbId) ||
                   (item.AniListId > 0 && series.AniListIds?.Contains(item.AniListId) == true) ||
                   (item.MalId > 0 && series.MalIds?.Contains(item.MalId) == true) ||
                   (item.ImdbId.IsNotNullOrWhiteSpace() && series.ImdbId.IsNotNullOrWhiteSpace() && item.ImdbId == series.ImdbId) ||
                   (item.TmdbId > 0 && series.MetadataSource == MetadataSourceType.Tmdb && series.ForeignId == item.TmdbId.ToString(CultureInfo.InvariantCulture)) ||
                   (item.MalId > 0 && series.MetadataSource == MetadataSourceType.MyAnimeList && series.ForeignId == item.MalId.ToString(CultureInfo.InvariantCulture));
        }

        public static bool IsExcluded(ImportListExclusion exclusion, Series series)
        {
            if (exclusion.MetadataSource != MetadataSourceType.Tvdb && exclusion.ForeignId.IsNotNullOrWhiteSpace())
            {
                return exclusion.MetadataSource == series.MetadataSource && exclusion.ForeignId == series.ForeignId;
            }

            return exclusion.TvdbId > 0 && series.TvdbId > 0 && exclusion.TvdbId == series.TvdbId;
        }

        public static bool IsExcluded(ImportListExclusion exclusion, ImportListItemInfo item)
        {
            if (exclusion.MetadataSource != MetadataSourceType.Tvdb && exclusion.ForeignId.IsNotNullOrWhiteSpace())
            {
                return int.TryParse(exclusion.ForeignId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
                    id > 0 && GetId(item, exclusion.MetadataSource) == id;
            }

            return exclusion.TvdbId > 0 && item.TvdbId > 0 && exclusion.TvdbId == item.TvdbId;
        }

        public static MetadataSourceType? GetPreferredSource(ImportListMetadataSource preference)
        {
            return preference switch
            {
                ImportListMetadataSource.Automatic => null,
                ImportListMetadataSource.Tvdb => MetadataSourceType.Tvdb,
                ImportListMetadataSource.Tmdb => MetadataSourceType.Tmdb,
                ImportListMetadataSource.AniList => MetadataSourceType.AniList,
                ImportListMetadataSource.MyAnimeList => MetadataSourceType.MyAnimeList,
                _ => throw new InvalidOperationException("Unsupported preferred metadata source.")
            };
        }

        public static MetadataSourceType? GetSource(ImportListItemInfo item, ImportListMetadataSource preference = ImportListMetadataSource.Automatic)
        {
            var preferred = GetPreferredSource(preference);
            if (preferred.HasValue && GetId(item, preferred.Value) > 0)
            {
                return preferred;
            }

            if (item.TvdbId > 0)
            {
                return MetadataSourceType.Tvdb;
            }

            if (item.AniListId > 0)
            {
                return MetadataSourceType.AniList;
            }

            if (item.TmdbId > 0)
            {
                return MetadataSourceType.Tmdb;
            }

            if (item.MalId > 0)
            {
                return MetadataSourceType.MyAnimeList;
            }

            return null;
        }

        public static Series CreateSeries(ImportListItemInfo item, ISearchForNewSeries lookup, Language language, ImportListMetadataSource preference = ImportListMetadataSource.Automatic)
        {
            var source = GetSource(item, preference);

            if (!source.HasValue)
            {
                if (item.ImdbId.IsNotNullOrWhiteSpace())
                {
                    return lookup.SearchForNewSeriesByImdbId(item.ImdbId, language).FirstOrDefault() ??
                           throw new InvalidOperationException("No metadata match for this IMDb ID.");
                }

                throw new InvalidOperationException("This list item does not supply a supported show ID.");
            }

            var id = GetId(item, source.Value);

            var series = new Series
            {
                MetadataSource = source.Value,
                ForeignId = id.ToString(CultureInfo.InvariantCulture),
                TvdbId = item.TvdbId > 0 ? item.TvdbId : 0,
                TmdbId = item.TmdbId > 0 ? item.TmdbId : 0,
                ImdbId = item.ImdbId,
                Title = item.Title,
                Year = item.Year,
                SeriesType = source.Value is MetadataSourceType.AniList or MetadataSourceType.MyAnimeList ? SeriesTypes.Anime : SeriesTypes.Standard,
                Language = language,
                SeasonType = SeasonType.Official
            };

            if (item.AniListId > 0)
            {
                series.AniListIds.Add(item.AniListId);
            }

            if (item.MalId > 0)
            {
                series.MalIds.Add(item.MalId);
            }

            return series;
        }

        private static int GetId(ImportListItemInfo item, MetadataSourceType source)
        {
            return source switch
            {
                MetadataSourceType.Tvdb => item.TvdbId,
                MetadataSourceType.AniList => item.AniListId,
                MetadataSourceType.Tmdb => item.TmdbId,
                MetadataSourceType.MyAnimeList => item.MalId,
                _ => 0
            };
        }
    }
}
