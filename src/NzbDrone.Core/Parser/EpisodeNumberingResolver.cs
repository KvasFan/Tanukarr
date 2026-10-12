using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Parser
{
    public enum NumberingConvention
    {
        Aired,
        Scene,
        Ambiguous
    }

    public class EpisodeNumberingResolution
    {
        public List<Episode> Episodes { get; set; }
        public NumberingConvention Convention { get; set; }
        public bool FromTitle { get; set; }
        public bool NeedsEvidence { get; set; }
        public string Rejection { get; set; }
    }

    public interface IEpisodeNumberingResolver
    {
        EpisodeNumberingResolution Resolve(Series series, ParsedEpisodeInfo parsed, SceneMapping mapping = null, NumberingConvention? inferred = null);
        EpisodeNumberingResolution ResolveLocal(LocalEpisode localEpisode, ParsedEpisodeInfo parsed);
        Dictionary<int, NumberingConvention> InferBatch(Series series, List<string> paths);
    }

    public class EpisodeNumberingResolver : IEpisodeNumberingResolver
    {
        private static readonly Regex EpisodeToken = new Regex(@"(?i)(?:\bS\d{1,2}[ ._-]*E\d{1,3}(?:(?:[ ._-]*E|-)\d{1,3})*|\b\d{1,2}x\d{1,3})", RegexOptions.Compiled);
        private static readonly Regex Words = new Regex(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
        private static readonly Regex TechnicalSuffix = new Regex(@"^(?:\d{3,4}p|web|webrip|webdl|web dl|hdtv|bluray|blu ray|bdrip|dvdrip|dsnp|amzn|nf|x264|x265|h264|h265|av1|proper|repack|mkv|mp4|avi)(?: |$)", RegexOptions.Compiled);
        private readonly IEpisodeService _episodes;
        private readonly ISceneMappingService _mappings;

        public EpisodeNumberingResolver(IEpisodeService episodes, ISceneMappingService mappings)
        {
            _episodes = episodes;
            _mappings = mappings;
        }

        public EpisodeNumberingResolution Resolve(Series series, ParsedEpisodeInfo parsed, SceneMapping mapping = null, NumberingConvention? inferred = null)
        {
            // Absolute anime, daily releases and specials retain their established paths.
            if (series == null || !series.UseSceneNumbering || series.SeasonType != SeasonType.Official || parsed == null || parsed.IsAbsoluteNumbering ||
                parsed.IsDaily || parsed.Special || !parsed.SeasonNumber.HasValue || parsed.SeasonNumber <= 0 || parsed.EpisodeNumbers.Contains(0))
            {
                return null;
            }

            var all = _episodes.GetEpisodeBySeries(series.Id);
            if (!all.Any(e => HasDifferentNumbering(e)))
            {
                return null;
            }

            mapping ??= GetMapping(parsed);
            if (parsed.FullSeason && parsed.IsMultiSeason)
            {
                return ResolveMultiSeason(series, parsed, mapping, inferred, all);
            }

            var seasons = parsed.SeasonNumbers.ToArray();

            // Preserve explicit alias offsets used by existing scene mappings.
            var offset = mapping?.SeasonNumber >= 0 && mapping.SceneSeasonNumber <= parsed.SeasonNumber
                ? mapping.SeasonNumber.Value - mapping.SceneSeasonNumber.Value
                : 0;
            if (mapping?.SceneOrigin != "tvdb" && mapping?.Type == "XemService" &&
                mapping.SceneSeasonNumber >= 0 && parsed.SeasonNumber == 1 && mapping.SceneSeasonNumber != 1)
            {
                offset = mapping.SceneSeasonNumber.Value - 1;
            }

            seasons = seasons.Select(s => s + offset).ToArray();
            var aired = all.Where(e => seasons.Contains(e.SeasonNumber) &&
                (parsed.FullSeason || parsed.EpisodeNumbers.Contains(e.EpisodeNumber))).ToList();
            var scene = all.Where(e => e.SceneSeasonNumber.HasValue && seasons.Contains(e.SceneSeasonNumber.Value) &&
                (parsed.FullSeason || (e.SceneEpisodeNumber.HasValue && parsed.EpisodeNumbers.Contains(e.SceneEpisodeNumber.Value)))).ToList();

            // No differing mapping at these coordinates: keep normal parsing and fallback.
            if (!aired.Concat(scene).Any(HasDifferentNumbering))
            {
                return null;
            }

            // A pack can contain the same episode set in both orders while its files
            // still require different mappings. Only identical individual mappings bypass.
            if (!parsed.FullSeason && aired.Select(e => e.Id).ToHashSet().SetEquals(scene.Select(e => e.Id)))
            {
                return null;
            }

            if (inferred == NumberingConvention.Ambiguous)
            {
                return Reject("Conflicting episode titles identify different numbering conventions within this season pack");
            }

            if (!parsed.FullSeason && parsed.EpisodeNumbers.Length == 1)
            {
                var titles = FindTitleMatches(parsed.ReleaseTitle, all);
                if (titles.Count == 1)
                {
                    var title = titles[0];
                    var inAired = aired.Any(e => e.Id == title.Id);
                    var inScene = scene.Any(e => e.Id == title.Id);
                    if (inAired != inScene)
                    {
                        var convention = inAired ? NumberingConvention.Aired : NumberingConvention.Scene;
                        if (inferred.HasValue && inferred != convention)
                        {
                            return Reject("Episode title conflicts with the numbering verified for this season pack");
                        }

                        return new EpisodeNumberingResolution
                        {
                            Episodes = new List<Episode> { title },
                            Convention = convention,
                            FromTitle = true
                        };
                    }

                    if (!inAired && !inScene)
                    {
                        return Reject("Episode title conflicts with both scene and library episode numbers");
                    }
                }
                else if (titles.Count > 1)
                {
                    return Reject("Episode title is not unique; numbering requires manual verification");
                }
            }

            // Unknown/mixed origins are not evidence, including unknown:tvdb.
            var conventionFromMapping = mapping?.SceneOrigin == "tvdb" ? NumberingConvention.Aired
                : mapping?.SceneOrigin == "scene" ? NumberingConvention.Scene
                : (NumberingConvention?)null;
            var selected = inferred ?? conventionFromMapping;
            if (selected.HasValue)
            {
                var episodes = selected == NumberingConvention.Aired ? aired : scene;
                var complete = parsed.FullSeason
                    ? seasons.All(s => episodes.Any(e => selected == NumberingConvention.Aired ? e.SeasonNumber == s : e.SceneSeasonNumber == s))
                    : parsed.EpisodeNumbers.All(n => episodes.Any(e => selected == NumberingConvention.Aired ? e.EpisodeNumber == n : e.SceneEpisodeNumber == n));
                if (episodes.Count > 0 && complete)
                {
                    return new EpisodeNumberingResolution { Episodes = episodes, Convention = selected.Value };
                }

                return Reject("Verified numbering convention does not identify every requested episode");
            }

            return new EpisodeNumberingResolution
            {
                Convention = NumberingConvention.Ambiguous,
                NeedsEvidence = true,
                Rejection = "Ambiguous scene/library numbering: verify episode titles or select episodes manually"
            };
        }

        public EpisodeNumberingResolution ResolveLocal(LocalEpisode localEpisode, ParsedEpisodeInfo parsed)
        {
            if (localEpisode.ExistingFile || !localEpisode.SceneSource || !localEpisode.Series.UseSceneNumbering || parsed?.SeasonNumber == null)
            {
                return null;
            }

            var mapping = GetMapping(parsed);
            if (mapping?.SceneOrigin != "tvdb" && mapping?.SceneOrigin != "scene")
            {
                // A verified release-group convention may be inherited by files which
                // omit the group, but only when the raw season belongs to that pack.
                foreach (var context in new[] { localEpisode.DownloadClientEpisodeInfo, localEpisode.FolderEpisodeInfo })
                {
                    if (context == null || !context.SeasonNumbers.Contains(parsed.SeasonNumber.Value))
                    {
                        continue;
                    }

                    var contextMapping = _mappings.FindSceneMapping(context.SeriesTitle, context.ReleaseTitle, parsed.SeasonNumber.Value);
                    if (contextMapping?.SceneOrigin == "tvdb" || contextMapping?.SceneOrigin == "scene")
                    {
                        mapping = contextMapping;
                        break;
                    }
                }
            }

            NumberingConvention? inferred = null;
            if (localEpisode.BatchNumbering != null && localEpisode.BatchNumbering.TryGetValue(parsed.SeasonNumber.Value, out var batch))
            {
                inferred = batch;
            }

            return Resolve(localEpisode.Series, parsed, mapping, inferred);
        }

        private EpisodeNumberingResolution ResolveMultiSeason(Series series, ParsedEpisodeInfo parsed, SceneMapping mapping, NumberingConvention? inferred, List<Episode> all)
        {
            var results = new List<EpisodeNumberingResolution>();
            var resolvedEpisodes = new List<Episode>();
            var missingSeason = false;
            foreach (var season in parsed.SeasonNumbers)
            {
                var seasonInfo = new ParsedEpisodeInfo
                {
                    SeriesTitle = parsed.SeriesTitle,
                    ReleaseTitle = parsed.ReleaseTitle,
                    SeasonNumber = season,
                    FullSeason = true
                };
                var seasonMapping = season == parsed.SeasonNumber ? mapping : GetMapping(seasonInfo);

                // A verified release-group origin can cover the pack, but alias offsets
                // belong to their individual season mappings.
                seasonMapping ??= new SceneMapping { SceneOrigin = mapping?.SceneOrigin };
                var resolution = Resolve(series, seasonInfo, seasonMapping, inferred);
                if (resolution?.Rejection != null)
                {
                    return resolution;
                }

                if (resolution != null)
                {
                    results.Add(resolution);
                    resolvedEpisodes.AddRange(resolution.Episodes);
                    continue;
                }

                var mappedSeason = season;
                if (seasonMapping.SeasonNumber >= 0 && seasonMapping.SceneSeasonNumber <= season)
                {
                    mappedSeason += seasonMapping.SeasonNumber.Value - seasonMapping.SceneSeasonNumber.Value;
                }

                if (seasonMapping.SceneOrigin != "tvdb" && seasonMapping.Type == "XemService" &&
                    seasonMapping.SceneSeasonNumber >= 0 && season == 1 && seasonMapping.SceneSeasonNumber != 1)
                {
                    mappedSeason = seasonMapping.SceneSeasonNumber.Value;
                }

                var episodes = seasonMapping.SceneOrigin == "tvdb"
                    ? all.Where(e => e.SeasonNumber == mappedSeason).ToList()
                    : all.Where(e => e.SceneSeasonNumber == mappedSeason).ToList();
                if (episodes.Count == 0)
                {
                    episodes = all.Where(e => e.SeasonNumber == mappedSeason).ToList();
                }

                missingSeason |= episodes.Count == 0;
                resolvedEpisodes.AddRange(episodes);
            }

            if (results.Count == 0)
            {
                return null;
            }

            if (missingSeason)
            {
                return Reject("Verified numbering convention does not identify every requested season");
            }

            return new EpisodeNumberingResolution
            {
                Episodes = resolvedEpisodes.DistinctBy(e => e.Id).ToList(),
                Convention = results.Select(r => r.Convention).Distinct().Count() == 1 ? results[0].Convention : NumberingConvention.Ambiguous
            };
        }

        public Dictionary<int, NumberingConvention> InferBatch(Series series, List<string> paths)
        {
            var result = new Dictionary<int, NumberingConvention>();
            if (!series.UseSceneNumbering)
            {
                return result;
            }

            var votes = new Dictionary<int, List<EpisodeNumberingResolution>>();
            foreach (var path in paths)
            {
                var parsed = Parser.ParseTitle(Path.GetFileName(path));
                var resolution = Resolve(series, parsed);
                if (resolution?.FromTitle != true)
                {
                    continue;
                }

                if (!votes.TryGetValue(parsed.SeasonNumber.Value, out var seasonVotes))
                {
                    seasonVotes = new List<EpisodeNumberingResolution>();
                    votes[parsed.SeasonNumber.Value] = seasonVotes;
                }

                seasonVotes.Add(resolution);
            }

            foreach (var season in votes)
            {
                if (season.Value.Select(v => v.Convention).Distinct().Count() > 1)
                {
                    result[season.Key] = NumberingConvention.Ambiguous;
                }
                else if (season.Value.SelectMany(v => v.Episodes).Select(e => e.Id).Distinct().Count() >= 2)
                {
                    result[season.Key] = season.Value[0].Convention;
                }
            }

            return result;
        }

        private SceneMapping GetMapping(ParsedEpisodeInfo parsed)
        {
            return _mappings.FindSceneMapping(parsed.SeriesTitle, parsed.ReleaseTitle, parsed.SeasonNumber ?? -1);
        }

        private static bool HasDifferentNumbering(Episode episode)
        {
            return (episode.SceneSeasonNumber.HasValue && episode.SceneSeasonNumber != episode.SeasonNumber) ||
                   (episode.SceneEpisodeNumber.HasValue && episode.SceneEpisodeNumber != episode.EpisodeNumber);
        }

        private static List<Episode> FindTitleMatches(string releaseTitle, List<Episode> episodes)
        {
            var token = EpisodeToken.Match(releaseTitle ?? string.Empty);
            if (!token.Success)
            {
                return new List<Episode>();
            }

            var suffix = Normalize(releaseTitle.Substring(token.Index + token.Length));
            return episodes.Where(e =>
            {
                var title = Normalize(e.Title);

                // Short/generic labels are weak evidence, especially "Pilot" and "Part 1".
                if (title.Length < 6 || title == "episode" || title == "unknown" || title.StartsWith("episode ") || title.StartsWith("part "))
                {
                    return false;
                }

                if (suffix == title)
                {
                    return true;
                }

                return suffix.StartsWith(title + " ", StringComparison.Ordinal) &&
                       TechnicalSuffix.IsMatch(suffix.AsSpan(title.Length + 1));
            }).ToList();
        }

        private static string Normalize(string text)
        {
            return Words.Replace((text ?? string.Empty).ToLowerInvariant(), " ").Trim();
        }

        private static EpisodeNumberingResolution Reject(string message)
        {
            return new EpisodeNumberingResolution { Convention = NumberingConvention.Ambiguous, Rejection = message };
        }
    }
}
