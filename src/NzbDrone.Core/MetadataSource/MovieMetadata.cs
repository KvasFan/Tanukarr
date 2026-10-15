using System;
using System.Collections.Generic;
using System.Globalization;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public static class MovieMetadata
    {
        public static Series Create(MetadataSourceType source, string id, string title)
        {
            return new Series
            {
                MetadataSource = source,
                ForeignId = id,
                MediaType = MediaType.Movie,
                AnimeGroupMemberKind = AnimeGroupMemberKind.Movie,
                Title = title,
                CleanTitle = Parser.Parser.CleanSeriesTitle(title),
                SortTitle = SeriesTitleNormalizer.Normalize(title, 0),
                TitleSlug = source.ToString().ToLowerInvariant() + "-" + id,
                SeriesType = SeriesTypes.Standard,
                SeasonType = SeasonType.Official,
                Seasons = new List<Season> { new Season { SeasonNumber = 1, Title = title, Monitored = true } },
                SeasonTypes = new List<SeasonType>
                {
                    new SeasonType { Name = "Official", Type = SeasonType.Official, SeasonNumbers = new List<int> { 1 }, EpisodeCount = 1 }
                },
                Monitored = true,
                SeasonFolder = false,
                Status = SeriesStatusType.Ended,
                OriginalLanguage = Language.English
            };
        }

        public static Tuple<Series, List<Episode>> WithFeature(Series movie)
        {
            // A feature is one indivisible media item. The existing file/import pipeline
            // stores it as native 1/1; its provider identity never becomes a TVDB ID.
            return Tuple.Create(movie, new List<Episode>
            {
                new Episode
                {
                    ForeignId = movie.ForeignId,
                    SeasonNumber = 1,
                    EpisodeNumber = 1,
                    AbsoluteEpisodeNumber = 1,
                    Title = movie.Title,
                    Overview = movie.Overview,
                    Runtime = movie.Runtime,
                    AirDateUtc = movie.FirstAired,
                    AirDate = movie.FirstAired?.ToString(Episode.AIR_DATE_FORMAT, CultureInfo.InvariantCulture),
                    Ratings = movie.Ratings
                }
            });
        }
    }
}
