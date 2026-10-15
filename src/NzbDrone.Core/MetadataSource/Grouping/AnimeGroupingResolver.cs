using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.Grouping
{
    public static class AnimeGroupingResolver
    {
        public const int MaxLookups = 16;
        public const int MaxRelationCandidates = 8;

        public static AnimeGroupingResult Resolve(MetadataSourceType source, string foreignId, Func<string, AnimeRelationNode> fetch, IReadOnlyList<Series> existingSeries)
        {
            if (source != MetadataSourceType.AniList && source != MetadataSourceType.MyAnimeList)
            {
                return Empty(AnimeGroupingStatus.Unsupported);
            }

            var cache = new Dictionary<string, AnimeRelationNode>(StringComparer.Ordinal);
            AnimeGroupingStatus? failure = null;
            AnimeRelationNode Read(string id)
            {
                if (cache.TryGetValue(id, out var cached))
                {
                    return cached;
                }

                if (cache.Count >= MaxLookups)
                {
                    failure = AnimeGroupingStatus.LimitReached;
                    return null;
                }

                var node = fetch(id);

                if (node?.ForeignId != id)
                {
                    node = null;
                }

                if (node == null)
                {
                    failure = AnimeGroupingStatus.Unavailable;
                }

                cache[id] = node;
                return node;
            }

            List<string> Links(AnimeRelationNode owner, List<string> ids)
            {
                var candidates = ids?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList() ?? new List<string>();

                if (candidates.Count > MaxRelationCandidates)
                {
                    failure = AnimeGroupingStatus.Ambiguous;
                    return null;
                }

                var eligible = new List<string>();
                foreach (var id in candidates)
                {
                    string format = null;
                    owner.RelationFormats?.TryGetValue(id, out format);

                    if (string.IsNullOrWhiteSpace(format))
                    {
                        var node = Read(id);
                        if (node == null)
                        {
                            return null;
                        }

                        format = node.Format;
                    }

                    if (EligibleFormat(format))
                    {
                        eligible.Add(id);
                    }
                }

                return eligible;
            }

            bool IsMovieLink(AnimeRelationNode owner, string id)
            {
                return owner.RelationFormats?.TryGetValue(id, out var format) == true && !string.IsNullOrWhiteSpace(format)
                    ? format == "MOVIE"
                    : Movie(Read(id));
            }

            List<string> Predecessors(AnimeRelationNode owner)
            {
                var ids = Links(owner, owner.PrequelIds);
                if (ids == null || ids.Count < 2)
                {
                    return ids;
                }

                var nodes = ids.Select(Read).ToList();
                if (nodes.Any(node => node == null))
                {
                    return null;
                }

                // A catalogue may list both the main story and its supplementary
                // side story as prequels. Only a reciprocal PARENT/SIDE_STORY pair
                // establishes which one is the main predecessor; titles never do.
                return nodes.Where(node => !nodes.Any(parent => parent.ForeignId != node.ForeignId &&
                        node.ParentIds.Contains(parent.ForeignId) && parent.SideStoryIds.Contains(node.ForeignId)))
                    .Select(node => node.ForeignId).ToList();
            }

            bool Reciprocal(AnimeRelationNode parent, AnimeRelationNode child)
            {
                if (!Compatible(parent) || !Compatible(child))
                {
                    failure = AnimeGroupingStatus.Ambiguous;
                    return false;
                }

                var previous = Predecessors(child);
                var next = Links(parent, parent.SequelIds);
                var childNext = Links(child, child.SequelIds);

                if (previous == null || next == null || childNext == null)
                {
                    return false;
                }

                // A film beside the TV continuation does not invalidate the TV spine.
                // Parallel movie branches do not establish a unique film order.
                var childSeasons = childNext.Count(id => !IsMovieLink(child, id));
                var childMovies = childNext.Count(id => IsMovieLink(child, id));
                if (previous.Count != 1 || previous[0] != parent.ForeignId || !next.Contains(child.ForeignId) ||
                    next.Count(id => IsMovieLink(parent, id) == Movie(child)) != 1 ||
                    (Movie(child) && next.Any(id => !IsMovieLink(parent, id))) ||
                    childSeasons > 1 || (Movie(child) && (childMovies > 1 || (childMovies > 0 && childSeasons > 0))))
                {
                    failure = AnimeGroupingStatus.Ambiguous;
                    return false;
                }

                return true;
            }

            AnimeRelationNode Successor(AnimeRelationNode owner)
            {
                var ids = Links(owner, owner.SequelIds);
                if (ids == null)
                {
                    return null;
                }

                var seasons = ids.Where(id => !IsMovieLink(owner, id)).ToList();
                var movies = ids.Where(id => IsMovieLink(owner, id)).ToList();

                if (seasons.Count > 1 || (Movie(owner) && (movies.Count > 1 || (seasons.Count > 0 && movies.Count > 0))))
                {
                    failure = AnimeGroupingStatus.Ambiguous;
                    return null;
                }

                var id = seasons.FirstOrDefault() ?? (movies.Count == 1 ? movies[0] : null);
                if (id == null)
                {
                    if (movies.Count > 1)
                    {
                        failure = AnimeGroupingStatus.Ambiguous;
                    }

                    return null;
                }

                var next = Read(id);
                return next != null && Reciprocal(owner, next) ? next : null;
            }

            var member = Read(foreignId);
            if (member == null)
            {
                return Empty(failure ?? AnimeGroupingStatus.Unavailable);
            }

            if (!Compatible(member))
            {
                return Empty(AnimeGroupingStatus.Unsupported, member);
            }

            var ancestors = new List<AnimeRelationNode>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = member;
            var linked = false;
            Series anchor = null;

            while (current != null)
            {
                if (!Compatible(current) || !visited.Add(current.ForeignId))
                {
                    return Empty(AnimeGroupingStatus.Ambiguous, member);
                }

                ancestors.Add(current);
                anchor ??= existingSeries.FirstOrDefault(s => s.MetadataSource == source && s.ForeignId == current.ForeignId &&
                    s.AnimeGroupingMode != AnimeGroupingMode.Disabled && AnimeGroupingService.HasValidGroup(s));
                var previousIds = Predecessors(current);

                if (previousIds == null)
                {
                    return Empty(failure ?? AnimeGroupingStatus.Unavailable, member);
                }

                if (previousIds.Count > 1)
                {
                    return Empty(AnimeGroupingStatus.Ambiguous, member);
                }

                var next = Successor(current);
                if (failure != null)
                {
                    return Empty(failure.Value, member);
                }

                linked |= next != null;
                if (previousIds.Count == 0)
                {
                    break;
                }

                var previous = Read(previousIds[0]);
                if (previous == null || !Reciprocal(previous, current))
                {
                    return Empty(failure ?? AnimeGroupingStatus.Unavailable, member);
                }

                linked = true;
                current = previous;
            }

            if (!linked)
            {
                return Empty(AnimeGroupingStatus.Unrelated, member);
            }

            var canonical = ancestors.LastOrDefault(n => !Movie(n));
            if (canonical == null)
            {
                // Distinguish a pure movie saga from a movie prequel to TV using only the
                // unique official forward chain, within the same global lookup budget.
                var future = ancestors.Last();
                var futureVisited = new HashSet<string>(StringComparer.Ordinal);
                while (future != null)
                {
                    if (!futureVisited.Add(future.ForeignId))
                    {
                        return Empty(AnimeGroupingStatus.Ambiguous, member);
                    }

                    if (!Movie(future))
                    {
                        canonical = future;
                        break;
                    }

                    future = Successor(future);
                    if (failure != null)
                    {
                        return Empty(failure.Value, member);
                    }
                }
            }

            var kind = canonical != null || anchor?.AnimeGroupKind == AnimeGroupKind.Series
                ? AnimeGroupKind.Series
                : AnimeGroupKind.MovieSaga;
            canonical ??= ancestors.Last();
            var memberKind = Movie(member) ? AnimeGroupMemberKind.Movie : AnimeGroupMemberKind.Season;
            var number = ancestors.Count(n => Movie(n) == Movie(member));

            if (anchor != null && anchor.AnimeGroupMemberKind == memberKind)
            {
                var between = ancestors.TakeWhile(n => n.ForeignId != anchor.ForeignId).Count(n => Movie(n) == Movie(member));
                number = (memberKind == AnimeGroupMemberKind.Movie ? anchor.AnimeGroupMovieNumber.Value : anchor.AnimeGroupSeasonNumber.Value) + between;
            }

            if (number > AnimeGroupingService.MaxSeasonNumber)
            {
                return Empty(AnimeGroupingStatus.Ambiguous, member);
            }

            var prefix = source == MetadataSourceType.AniList ? "anilist" : "mal";
            return new AnimeGroupingResult
            {
                AnimeGroupingMode = AnimeGroupingMode.Automatic,
                Status = AnimeGroupingStatus.Resolved,
                AnimeGroupId = anchor?.AnimeGroupId ?? prefix + ":" + canonical.ForeignId,
                AnimeGroupTitle = anchor?.AnimeGroupTitle ?? canonical.Title,
                AnimeGroupFolder = anchor?.AnimeGroupFolder ?? AnimeGroupingService.CreateGroupFolder(canonical.Title),
                AnimeGroupKind = kind,
                AnimeGroupMemberKind = memberKind,
                AnimeGroupSeasonNumber = memberKind == AnimeGroupMemberKind.Season ? number : null,
                AnimeGroupMovieNumber = memberKind == AnimeGroupMemberKind.Movie ? number : null,
                MemberTitle = member.Title
            };
        }

        private static bool Compatible(AnimeRelationNode node)
        {
            return node != null && AnimeGroupingService.IsValidGroupTitle(node.Title) &&
                   int.TryParse(node.ForeignId, out var id) && id > 0 && EligibleFormat(node.Format);
        }

        private static bool Movie(AnimeRelationNode node)
        {
            return node?.Format == "MOVIE";
        }

        private static bool EligibleFormat(string format)
        {
            return format == "TV" || format == "TV_SHORT" || format == "ONA" || format == "MOVIE";
        }

        private static AnimeGroupingResult Empty(AnimeGroupingStatus status, AnimeRelationNode member = null)
        {
            return new AnimeGroupingResult
            {
                AnimeGroupingMode = AnimeGroupingMode.Automatic,
                Status = status,
                AnimeGroupMemberKind = Movie(member) ? AnimeGroupMemberKind.Movie : AnimeGroupMemberKind.Season,
                AnimeGroupKind = Movie(member) ? AnimeGroupKind.MovieSaga : AnimeGroupKind.Series,
                MemberTitle = member?.Title
            };
        }
    }
}
