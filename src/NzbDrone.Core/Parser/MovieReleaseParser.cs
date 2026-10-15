using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Parser
{
    public static class MovieReleaseParser
    {
        private static readonly Regex Year = new Regex(@"(?<![0-9])(?<year>(?:18|19|20)[0-9]{2})(?![0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex Imdb = new Regex(@"(?<![A-Za-z0-9])(?<id>tt[0-9]{7,12})(?![0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex Episodic = new Regex(@"(?:\bS[0-9]{1,3}[ ._-]*E[0-9]{1,4}\b|\b[0-9]{1,3}x[0-9]{2,4}\b|\bseasons?[ ._-]*[0-9]+|\b(?:cd|disc)[ ._-]*[0-9]+\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex Technical = new Regex(@"\A[ ._\[\](){}-]*(?:(?:[0-9]{3,4}p|bluray|blu-ray|bdrip|brrip|dvd|web(?:[ ._-]?dl|rip)?|hdtv|remux|uhd|x26[45]|h[ .]?26[45]|hevc|av1|aac|dts|ddp|proper|repack|multi|dual|imdb|tt[0-9]+)(?:\b|[ ._-]))", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        public static ParsedEpisodeInfo Parse(string release)
        {
            if (string.IsNullOrWhiteSpace(release) || release.Length > 2048)
            {
                return null;
            }

            var name = Path.GetFileName(release);
            if (MediaFiles.MediaFileExtensions.Extensions.Contains(Path.GetExtension(name)))
            {
                name = Path.GetFileNameWithoutExtension(name);
            }

            if (Episodic.IsMatch(name))
            {
                return null;
            }

            var ids = Imdb.Matches(name).Select(match => match.Groups["id"].Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count > 1)
            {
                return null;
            }

            foreach (Match match in Year.Matches(name))
            {
                var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
                var title = name.Substring(0, match.Index).Trim(' ', '.', '_', '-', '(', '[', '{');
                var suffix = name.Substring(match.Index + match.Length).Trim(' ', '.', '_', '-', ')', ']', '}');
                if (year < 1878 || year > DateTime.UtcNow.Year + 5 || title.Length == 0 ||
                    (suffix.Length > 0 && !Technical.IsMatch(suffix)))
                {
                    continue;
                }

                return Build(release, title.Replace('.', ' ').Replace('_', ' ').Trim(), year, ids.SingleOrDefault());
            }

            // An explicit IMDb filename is a strong identity, even without title/year.
            return ids.Count == 1 ? Build(release, name, 0, ids[0]) : null;
        }

        public static ParsedEpisodeInfo Match(string release, Series movie)
        {
            if (movie?.MediaType != MediaType.Movie || string.IsNullOrWhiteSpace(release) || release.Length > 2048)
            {
                return null;
            }

            var parsed = Parse(release);
            if (parsed == null && movie.MetadataSource is MetadataSourceType.AniList or MetadataSourceType.MyAnimeList)
            {
                // Anime catalogues have historically represented a verified film as
                // native episode 1. Preserve that release convention only for those
                // owning providers; ordinary movie catalogues still require film IDs.
                var native = Parser.ParsePath(release);
                var nativeIds = Imdb.Matches(Path.GetFileName(release)).Select(match => match.Groups["id"].Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Take(2).ToList();
                if (nativeIds.Count > 1 || (nativeIds.Count == 1 && !string.Equals(nativeIds[0], movie.ImdbId, StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                if (native is { FullSeason: false, IsPartialSeason: false } &&
                    ((native.SeasonNumber == 1 && native.EpisodeNumbers?.SequenceEqual(new[] { 1 }) == true) ||
                     (native.IsAbsoluteNumbering && native.AbsoluteEpisodeNumbers?.SequenceEqual(new[] { 1 }) == true)) &&
                    (native.SeriesTitleInfo?.Year is not > 0 || movie.Year <= 0 || native.SeriesTitleInfo.Year == movie.Year) &&
                    MatchesTitle(movie, native.SeriesTitleInfo?.TitleWithoutYear ?? native.SeriesTitle))
                {
                    return Build(release, movie.Title, movie.Year, nativeIds.SingleOrDefault());
                }
            }

            if (parsed == null || (parsed.MovieYear > 0 && movie.Year > 0 && parsed.MovieYear != movie.Year))
            {
                return null;
            }

            if (parsed.MovieImdbId != null)
            {
                if (!string.Equals(parsed.MovieImdbId, movie.ImdbId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }
            else if (parsed.MovieYear == 0 || movie.Year == 0 ||
                !MatchesTitle(movie, parsed.SeriesTitle))
            {
                return null;
            }

            parsed.SeriesTitle = movie.Title;
            parsed.SeriesTitleInfo = new SeriesTitleInfo { TitleWithoutYear = movie.Title, Year = movie.Year };
            return parsed;
        }

        public static Series MatchUnique(string release, IEnumerable<Series> candidates)
        {
            var matches = (candidates ?? Enumerable.Empty<Series>()).Where(movie => Match(release, movie) != null).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        public static bool HasMappedFeatureCoverage(RemoteEpisode remote, int seriesId, IEnumerable<Series> collectionMembers = null)
        {
            var movie = remote?.Series;
            var parsed = remote?.ParsedEpisodeInfo;
            if (movie?.Id != seriesId || movie.MediaType != MediaType.Movie || parsed is not { IsMovie: true, FullSeason: false, IsPartialSeason: false } ||
                parsed.SeasonNumbers?.SequenceEqual(new[] { 1 }) != true || parsed.EpisodeNumbers?.SequenceEqual(new[] { 1 }) != true ||
                parsed.AbsoluteEpisodeNumbers?.Length != 0 || remote.Episodes?.Count != 1 ||
                remote.Episodes[0] is not { Id: > 0, SeasonNumber: 1, EpisodeNumber: 1 } feature || feature.SeriesId != seriesId ||
                remote.Release == null)
            {
                return false;
            }

            if (!parsed.IsMovieCollectionPack)
            {
                return Match(remote.Release.Title, movie) != null;
            }

            // Recheck the current stored membership: a cached pack must not acquire
            // another collection after a member is moved or its metadata changes.
            var movies = (collectionMembers ?? Enumerable.Empty<Series>()).Where(member =>
                member?.MediaType == MediaType.Movie && member.Monitored && AnimeGroupingValidator.HasGroup(member) &&
                member.AnimeGroupId == movie.AnimeGroupId).ToList();
            var anchors = movies.Where(member => member.Id == seriesId).ToList();
            if (anchors.Count != 1 || movies.Select(member => member.Id).Distinct().Count() != movies.Count)
            {
                return false;
            }

            var verified = MovieCollectionPackParser.Match(remote.Release.Title, new MovieSearchCriteria { Series = anchors[0], CollectionMovies = movies });
            return verified != null && verified.MovieCollectionId == parsed.MovieCollectionId &&
                verified.MovieCollectionRuntime == parsed.MovieCollectionRuntime && verified.MovieYear == parsed.MovieYear;
        }

        private static bool MatchesTitle(Series movie, string title)
        {
            return !string.IsNullOrWhiteSpace(title) &&
                new[] { movie.Title, movie.OriginalTitle }.Concat(movie.AlternateTitles ?? new List<string>())
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                    .Any(candidate => Parser.CleanSeriesTitle(candidate) == Parser.CleanSeriesTitle(title));
        }

        private static ParsedEpisodeInfo Build(string release, string title, int year, string imdb)
        {
            return new ParsedEpisodeInfo
            {
                IsMovie = true,
                MovieYear = year,
                MovieImdbId = imdb,
                ReleaseTitle = release,
                SeriesTitle = title,
                SeriesTitleInfo = new SeriesTitleInfo { TitleWithoutYear = title, Year = year },
                SeasonNumber = 1,
                EpisodeNumbers = new[] { 1 },
                Quality = QualityParser.ParseQuality(release),
                Languages = LanguageParser.ParseLanguages(release),
                ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(release)
            };
        }
    }
}
