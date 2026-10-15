using System;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Parser
{
    public static class MovieCollectionPackParser
    {
        public static ParsedEpisodeInfo Match(string title, MovieSearchCriteria criteria)
        {
            var movies = criteria?.CollectionMovies;
            if (criteria?.Series == null || !AnimeGroupingService.IsValidGroupTitle(criteria.Series.AnimeGroupTitle) ||
                string.IsNullOrWhiteSpace(title) || title.Length > 2048 || movies?.Count is not >= 2 or > 999 ||
                movies.Any(movie => movie == null || movie.MediaType != MediaType.Movie || movie.Year <= 0 || movie.Runtime is <= 0 or > 10080 ||
                    !AnimeGroupingValidator.HasGroup(movie) || movie.AnimeGroupId != criteria.Series.AnimeGroupId) ||
                Regex.IsMatch(title, @"\b(?:S[0-9]+(?:[ ._-]*(?:E[0-9]+|-[ ._-]*S[0-9]+))?|seasons?[ ._-]*[0-9]+|specials?[ ._-]*[0-9]+)\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
            {
                return null;
            }

            var groupTitle = criteria.Series.AnimeGroupTitle;
            var name = string.Join(@"[ ._-]+", groupTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
            var firstYear = movies.Min(movie => movie.Year);
            var lastYear = movies.Max(movie => movie.Year);
            var pattern = @"\A" + name + @"[ ._()\[\]-]+(?:complete[ ._-]+)?" + firstYear + @"[ ._-]+" + lastYear + @"(?=[ ._()\[\]-]|$)";
            if (!Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                return null;
            }

            return new ParsedEpisodeInfo
            {
                IsMovie = true,
                IsMovieCollectionPack = true,
                MovieCollectionId = criteria.Series.AnimeGroupId,
                MovieCollectionRuntime = movies.Sum(movie => movie.Runtime),
                MovieYear = criteria.Series.Year,
                ReleaseTitle = title,
                SeriesTitle = criteria.Series.Title,
                SeriesTitleInfo = new SeriesTitleInfo { TitleWithoutYear = criteria.Series.Title, Year = criteria.Series.Year },
                SeasonNumber = 1,
                EpisodeNumbers = new[] { 1 },
                Quality = QualityParser.ParseQuality(title),
                Languages = LanguageParser.ParseLanguages(title),
                ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(title)
            };
        }
    }
}
