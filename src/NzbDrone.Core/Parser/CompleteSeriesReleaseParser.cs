using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Parser
{
    public static class CompleteSeriesReleaseParser
    {
        private static readonly Regex SeasonRange = new Regex(@"\b(?:s|seasons?|series)\s*(?<first>\d{1,3})\s*(?:-|–|—|to)\s*(?:s|seasons?|series)?\s*(?<last>\d{1,3})(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Season = new Regex(@"\b(?:s|seasons?|series)\s*(?<season>\d{1,3})(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Complete = new Regex(@"^(?:the\s+)?(?:complete(?:\s+(?:series|collection))?|(?:entire|full)\s+series|all\s+seasons|box\s*set)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BareQuality = new Regex(@"^(?:bd|bdrip|blu-?ray|brrip|bdremux|remux|web(?:-?dl|-?rip)?|hdtv|dvd(?:rip)?|\d{3,4}p|x26[45]|h\s?26[45]|hevc|avc|hi10p?|10-?bit|dual[\s-]?audio|multi[\s-]?audio)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Episode = new Regex(@"\bs\d+\s*e\d+|\b\d+x\d+\b|\b(?:episodes?|parts?|volumes?|cours?)(?=\b|\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AbsoluteRange = new Regex(@"^\s*(?<first>\d{1,4})\s*(?:-|~|\u2013|\u2014|to)\s*(?<last>\d{1,4})(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ParsedEpisodeInfo Parse(string title, CompleteSeriesSearchCriteria criteria)
        {
            var expectedSeasons = criteria.Episodes.Select(e => e.SeasonNumber).Where(s => s > 0).Distinct().OrderBy(s => s).ToArray();
            if (expectedSeasons.Length == 0)
            {
                return null;
            }

            // An exact known title/alias prefix is required. Do not turn an unrelated
            // "Complete" hit into this series just because it came from its search.
            var cleaned = Regex.Replace(title, @"^\[[^\]]+\]\s*", string.Empty);
            cleaned = Regex.Replace(cleaned, @"['`’]", string.Empty).Replace("&", "and").RemoveDiacritics();
            string suffix = null;
            string matchedTitle = null;
            foreach (var alias in criteria.SceneTitles.OrderByDescending(t => t.Length))
            {
                var words = SearchCriteriaBase.GetCleanSceneTitle(alias).Split('+');
                var pattern = @"^(?:the[\W_]+)?" + string.Join(@"[\W_]+", words.Select(Regex.Escape)) + @"(?=$|[\W_])(?<suffix>.*)$";
                var match = Regex.Match(cleaned, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    suffix = match.Groups["suffix"].Value;
                    matchedTitle = cleaned.Substring(0, match.Groups["suffix"].Index).Trim();
                    break;
                }
            }

            // A collection member is also released under the collection title with the
            // collection's season number ("Collection S02" for its second entry).
            var viaCollection = false;
            if (suffix == null && criteria.CollectionTitle.IsNotNullOrWhiteSpace() && criteria.CollectionSeasons.Count > 0)
            {
                var words = SearchCriteriaBase.GetCleanSceneTitle(criteria.CollectionTitle).Split('+');
                var pattern = @"^(?:the[\W_]+)?" + string.Join(@"[\W_]+", words.Select(Regex.Escape)) + @"(?=$|[\W_])(?<suffix>.*)$";
                var match = Regex.Match(cleaned, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    suffix = match.Groups["suffix"].Value;
                    viaCollection = true;
                }
            }

            if (suffix == null)
            {
                return null;
            }

            var description = Regex.Replace(suffix, @"[._\[\]()]+", " ").Trim(' ', '-');
            var year = Regex.Match(description, @"^(?<year>(?:19|20)\d{2})(?:\s+|$)");
            if (year.Success)
            {
                var releaseYear = int.Parse(year.Groups["year"].Value);

                // A collection title carries the year its first entry aired.
                if (viaCollection ? criteria.Series.Year > 0 && releaseYear > criteria.Series.Year : criteria.Series.Year != releaseYear)
                {
                    return null;
                }

                if (!viaCollection)
                {
                    matchedTitle += $" ({releaseYear})";
                }

                // "Title (2015) - Season 2" separates the year from the season with a dash.
                description = description.Substring(year.Length).Trim(' ', '-');
            }

            if (Episode.IsMatch(description))
            {
                return null;
            }

            // Some trackers name a single-season show's only pack with nothing but the
            // title and quality tokens. Accept that only when the library knows of exactly
            // one regular season, so it cannot stand in for a multi-season run.
            var bareSingleSeasonPack = !viaCollection && BareQuality.IsMatch(description) &&
                !Regex.IsMatch(description, @"\b(?:seasons?|series|complete)\b|^\d+\s*-\s*\d+", RegexOptions.IgnoreCase) &&
                !Season.IsMatch(description) &&
                expectedSeasons.Length == 1 &&
                criteria.Series.Seasons != null &&
                criteria.Series.Seasons.Where(s => s.SeasonNumber > 0).Select(s => s.SeasonNumber).SequenceEqual(expectedSeasons);

            // A longer, different series title must not match a shorter alias prefix.
            if (!bareSingleSeasonPack && !Complete.IsMatch(description) && !Season.IsMatch(description.Split(' ')[0]) &&
                !Regex.IsMatch(description, @"^(?:seasons?|series)\s+\d+", RegexOptions.IgnoreCase) &&
                !(criteria.Series.SeriesType == SeriesTypes.Anime && AbsoluteRange.IsMatch(description)))
            {
                return null;
            }

            var completeLabel = Complete.Match(description);
            var absoluteDescription = completeLabel.Success ? description.Substring(completeLabel.Length) : description;
            var absoluteRange = criteria.Series.SeriesType == SeriesTypes.Anime ? AbsoluteRange.Match(absoluteDescription) : null;
            if (absoluteRange?.Success == true)
            {
                // Absolute numbers under the collection title count the whole collection.
                if (viaCollection)
                {
                    return null;
                }

                var first = int.Parse(absoluteRange.Groups["first"].Value);
                var last = int.Parse(absoluteRange.Groups["last"].Value);
                if (first <= 0 || last < first || criteria.Episodes.Any(e => !e.AbsoluteEpisodeNumber.HasValue ||
                    e.AbsoluteEpisodeNumber.Value < first || e.AbsoluteEpisodeNumber.Value > last))
                {
                    return null;
                }
            }

            var ranges = SeasonRange.Matches(description).Cast<Match>().ToList();
            var seasons = Season.Matches(description).Cast<Match>().Select(m => int.Parse(m.Groups["season"].Value)).ToHashSet();
            foreach (var range in ranges)
            {
                var first = int.Parse(range.Groups["first"].Value);
                var last = int.Parse(range.Groups["last"].Value);
                if (first > last)
                {
                    return null;
                }

                seasons.UnionWith(Enumerable.Range(first, last - first + 1));
            }

            if (viaCollection)
            {
                // Only an explicit collection season identifies this member; a bare or
                // COMPLETE collection title covers other members too.
                if (seasons.Count == 0 || seasons.Any(season => !criteria.CollectionSeasons.ContainsKey(season)))
                {
                    return null;
                }

                seasons = seasons.Select(season => criteria.CollectionSeasons[season]).ToHashSet();
                matchedTitle = criteria.Series.Title;
            }

            // Explicit season coverage takes precedence over a misleading COMPLETE label.
            var coveredSeasons = expectedSeasons;
            if (seasons.Count > 0)
            {
                if (seasons.Any(season => !expectedSeasons.Contains(season)) ||
                    (!criteria.IncludeSeasonPacks && !expectedSeasons.All(seasons.Contains)))
                {
                    return null;
                }

                coveredSeasons = seasons.OrderBy(season => season).ToArray();
            }
            else if (absoluteRange?.Success == true)
            {
                // Absolute coverage was verified above, including ranges following COMPLETE.
            }
            else if (!bareSingleSeasonPack && (!Complete.IsMatch(description) || Regex.IsMatch(description, @"\bseason\b", RegexOptions.IgnoreCase)))
            {
                return null;
            }

            // Preserve ordinary quality/language/group parsing while supplying the
            // advertised coverage only within this complete-series search context.
            // Keep the actual alias and explicit year: replacing them with the library
            // title can resolve a namesake through a different series' scene mapping.
            var parsed = Parser.ParseTitle($"{matchedTitle} S{coveredSeasons[0]:00}");
            if (parsed == null)
            {
                return null;
            }

            parsed.ReleaseTitle = title;
            parsed.ReleaseTokens = suffix;
            parsed.Quality = QualityParser.ParseQuality(title);
            parsed.Languages = LanguageParser.ParseLanguages(suffix);
            parsed.ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(title);
            parsed.SeasonNumbers = coveredSeasons.ToList();
            parsed.FullSeason = true;
            parsed.IsCompleteSeries = coveredSeasons.SequenceEqual(expectedSeasons);
            return parsed;
        }

        public static bool HasMappedSeasonCoverage(RemoteEpisode remoteEpisode, int seriesId)
        {
            var parsed = remoteEpisode.ParsedEpisodeInfo;
            return remoteEpisode.Series?.Id == seriesId && parsed is { FullSeason: true, IsPartialSeason: false } &&
                   parsed.EpisodeNumbers.Length == 0 && parsed.AbsoluteEpisodeNumbers.Length == 0 &&
                   parsed.SeasonNumbers.Count > 0 && parsed.SeasonNumbers.All(season => season > 0) &&
                   remoteEpisode.Episodes?.Count > 0 && remoteEpisode.Episodes.All(episode =>
                       episode.Id > 0 && episode.SeriesId == seriesId && parsed.SeasonNumbers.Contains(episode.SeasonNumber)) &&
                   parsed.SeasonNumbers.All(season => remoteEpisode.Episodes.Any(episode => episode.SeasonNumber == season));
        }
    }
}
