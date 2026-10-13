using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.Grouping
{
    [TestFixture]
    public class AnimeGroupingResolverFixture : CoreTest
    {
        private Dictionary<string, AnimeRelationNode> _nodes;
        private List<string> _requests;

        [SetUp]
        public void Setup()
        {
            _nodes = new Dictionary<string, AnimeRelationNode>();
            _requests = new List<string>();
        }

        private void Chain(int count)
        {
            for (var number = 1; number <= count; number++)
            {
                _nodes[number.ToString()] = new AnimeRelationNode
                {
                    ForeignId = number.ToString(),
                    Title = number == 1 ? "Example Anime" : "Example Part " + number,
                    Format = "TV",
                    PrequelIds = number > 1 ? new List<string> { (number - 1).ToString() } : new List<string>(),
                    SequelIds = number < count ? new List<string> { (number + 1).ToString() } : new List<string>()
                };
            }
        }

        private AnimeGroupingResult Resolve(string id, MetadataSourceType source = MetadataSourceType.AniList, params Series[] existing)
        {
            return AnimeGroupingResolver.Resolve(
                source,
                id,
                requested =>
                {
                    _requests.Add(requested);
                    return _nodes.GetValueOrDefault(requested);
                },
                existing);
        }

        [TestCase(MetadataSourceType.AniList, "anilist:1")]
        [TestCase(MetadataSourceType.MyAnimeList, "mal:1")]
        public void should_resolve_a_reciprocal_chain_to_its_catalogue_root(MetadataSourceType source, string groupId)
        {
            Chain(3);
            var result = Resolve("3", source);

            result.Status.Should().Be(AnimeGroupingStatus.Resolved);
            result.AnimeGroupId.Should().Be(groupId);
            result.AnimeGroupTitle.Should().Be("Example Anime");
            result.AnimeGroupSeasonNumber.Should().Be(3);
            result.AnimeGroupFolder.Should().Be("Example Anime");
            _requests.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void should_assign_season_one_to_a_root_with_an_eligible_sequel()
        {
            Chain(2);
            Resolve("1").AnimeGroupSeasonNumber.Should().Be(1);
        }

        [Test]
        public void should_leave_an_unrelated_entry_ungrouped()
        {
            Chain(1);
            var result = Resolve("1");
            result.Status.Should().Be(AnimeGroupingStatus.Unrelated);
            result.AnimeGroupId.Should().BeNull();
        }

        [TestCase("TV")]
        [TestCase("TV_SHORT")]
        [TestCase("ONA")]
        public void should_allow_only_episodic_formats(string format)
        {
            Chain(2);
            _nodes["2"].Format = format;
            Resolve("2").IsResolved.Should().BeTrue();
        }

        [TestCase("SPECIAL")]
        [TestCase("OVA")]
        [TestCase("MUSIC")]
        [TestCase(null)]
        public void should_not_group_non_episodic_entries(string format)
        {
            Chain(2);
            _nodes["2"].Format = format;
            Resolve("2").Status.Should().Be(AnimeGroupingStatus.Unsupported);
        }

        [TestCase("1", AnimeGroupMemberKind.Season, 1, null)]
        [TestCase("2", AnimeGroupMemberKind.Movie, null, 1)]
        [TestCase("3", AnimeGroupMemberKind.Season, 2, null)]
        [TestCase("4", AnimeGroupMemberKind.Movie, null, 2)]
        public void should_keep_season_and_movie_ordinals_separate_in_a_mixed_chain(string id, AnimeGroupMemberKind kind, int? season, int? movie)
        {
            Chain(4);
            _nodes["2"].Format = "MOVIE";
            _nodes["4"].Format = "MOVIE";
            var result = Resolve(id);

            result.Status.Should().Be(AnimeGroupingStatus.Resolved);
            result.AnimeGroupId.Should().Be("anilist:1");
            result.AnimeGroupKind.Should().Be(AnimeGroupKind.Series);
            result.AnimeGroupMemberKind.Should().Be(kind);
            result.AnimeGroupSeasonNumber.Should().Be(season);
            result.AnimeGroupMovieNumber.Should().Be(movie);
            _requests.Should().OnlyHaveUniqueItems();
        }

        [TestCase("1", 1)]
        [TestCase("3", 3)]
        public void should_use_the_first_movie_as_the_canonical_root_of_a_movie_saga(string id, int number)
        {
            Chain(3);
            foreach (var node in _nodes.Values)
            {
                node.Format = "MOVIE";
            }

            var result = Resolve(id);
            result.Status.Should().Be(AnimeGroupingStatus.Resolved);
            result.AnimeGroupId.Should().Be("anilist:1");
            result.AnimeGroupTitle.Should().Be("Example Anime");
            result.AnimeGroupKind.Should().Be(AnimeGroupKind.MovieSaga);
            result.AnimeGroupMovieNumber.Should().Be(number);
            result.AnimeGroupSeasonNumber.Should().BeNull();
        }

        [TestCase("1", AnimeGroupMemberKind.Movie, null, 1)]
        [TestCase("2", AnimeGroupMemberKind.Movie, null, 2)]
        [TestCase("3", AnimeGroupMemberKind.Season, 1, null)]
        public void should_choose_the_first_tv_title_consistently_when_movies_precede_it(string id, AnimeGroupMemberKind kind, int? season, int? movie)
        {
            Chain(3);
            _nodes["1"].Format = "MOVIE";
            _nodes["2"].Format = "MOVIE";
            var result = Resolve(id);
            result.Status.Should().Be(AnimeGroupingStatus.Resolved);
            result.AnimeGroupId.Should().Be("anilist:3");
            result.AnimeGroupTitle.Should().Be("Example Part 3");
            result.AnimeGroupKind.Should().Be(AnimeGroupKind.Series);
            result.AnimeGroupMemberKind.Should().Be(kind);
            result.AnimeGroupSeasonNumber.Should().Be(season);
            result.AnimeGroupMovieNumber.Should().Be(movie);
        }

        [TestCase("2", AnimeGroupingStatus.Resolved)]
        [TestCase("3", AnimeGroupingStatus.Resolved)]
        [TestCase("4", AnimeGroupingStatus.Ambiguous)]
        public void should_keep_a_tv_continuation_grouped_but_decline_a_parallel_movie_branch(string id, AnimeGroupingStatus status)
        {
            Chain(3);
            _nodes["2"].SequelIds.Add("4");
            _nodes["4"] = new AnimeRelationNode
            {
                ForeignId = "4", Title = "Parallel Movie", Format = "MOVIE", PrequelIds = new List<string> { "2" }
            };
            var result = Resolve(id);
            result.Status.Should().Be(status);
            if (result.IsResolved)
            {
                result.AnimeGroupId.Should().Be("anilist:1");
                result.AnimeGroupSeasonNumber.Should().Be(int.Parse(id));
            }
        }

        [Test]
        public void should_preserve_a_manually_corrected_movie_ordinal_without_affecting_tv_ordinals()
        {
            Chain(4);
            _nodes["2"].Format = "MOVIE";
            _nodes["4"].Format = "MOVIE";
            var anchor = new Series
            {
                MetadataSource = MetadataSourceType.AniList, ForeignId = "2", AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = "manual-mixed", AnimeGroupTitle = "Mixed Group", AnimeGroupFolder = "Pinned",
                AnimeGroupMemberKind = AnimeGroupMemberKind.Movie, AnimeGroupKind = AnimeGroupKind.Series, AnimeGroupMovieNumber = 7
            };
            Resolve("4", MetadataSourceType.AniList, anchor).AnimeGroupMovieNumber.Should().Be(8);
            var season = Resolve("3", MetadataSourceType.AniList, anchor);
            season.AnimeGroupId.Should().Be("manual-mixed");
            season.AnimeGroupSeasonNumber.Should().Be(2);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_reject_eligible_branches(bool prequelBranch)
        {
            Chain(3);
            _nodes["4"] = new AnimeRelationNode { ForeignId = "4", Title = "Another Edition", Format = "TV" };
            (prequelBranch ? _nodes["2"].PrequelIds : _nodes["2"].SequelIds).Add("4");
            Resolve("2").Status.Should().Be(AnimeGroupingStatus.Ambiguous);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_exclude_a_special_before_counting_sequel_branches(bool publishedFormat)
        {
            Chain(3);
            _nodes["2"].SequelIds.Add("4");
            _nodes["4"] = new AnimeRelationNode { ForeignId = "4", Title = "Special", Format = "SPECIAL" };
            if (publishedFormat)
            {
                _nodes["2"].RelationFormats["4"] = "SPECIAL";
            }

            Resolve("2").AnimeGroupSeasonNumber.Should().Be(2);
            if (publishedFormat)
            {
                _requests.Should().NotContain("4");
            }
        }

        [Test]
        public void should_require_a_reciprocal_relationship()
        {
            Chain(2);
            _nodes["1"].SequelIds.Clear();
            Resolve("2").Status.Should().Be(AnimeGroupingStatus.Ambiguous);
        }

        [Test]
        public void should_reject_a_cycle()
        {
            Chain(2);
            _nodes["1"].PrequelIds.Add("2");
            _nodes["2"].SequelIds.Add("1");
            Resolve("1").Status.Should().Be(AnimeGroupingStatus.Ambiguous);
            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_bound_all_hydrated_nodes_including_neighbors()
        {
            Chain(40);
            Resolve("40").Status.Should().Be(AnimeGroupingStatus.LimitReached);
            _requests.Should().HaveCount(AnimeGroupingResolver.MaxLookups);
            _requests.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void should_reject_an_excessive_relation_list_before_hydrating_it()
        {
            Chain(1);
            _nodes["1"].SequelIds = Enumerable.Range(2, 30).Select(n => n.ToString()).ToList();
            Resolve("1").Status.Should().Be(AnimeGroupingStatus.Ambiguous);
            _requests.Should().ContainSingle();
        }

        [Test]
        public void should_preserve_a_manually_corrected_prequel_as_the_canonical_anchor()
        {
            Chain(3);
            var result = Resolve("3", MetadataSourceType.AniList, new Series
            {
                MetadataSource = MetadataSourceType.AniList,
                ForeignId = "2",
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = "corrected-franchise",
                AnimeGroupTitle = "Corrected Franchise",
                AnimeGroupSeasonNumber = 7,
                AnimeGroupFolder = "Existing Folder"
            });

            result.AnimeGroupId.Should().Be("corrected-franchise");
            result.AnimeGroupSeasonNumber.Should().Be(8);
            result.AnimeGroupFolder.Should().Be("Existing Folder");
        }

        [Test]
        public void should_never_anchor_to_the_same_numeric_id_in_another_catalogue()
        {
            Chain(2);
            var result = Resolve("2", MetadataSourceType.AniList, new Series
            {
                MetadataSource = MetadataSourceType.MyAnimeList,
                ForeignId = "1",
                AnimeGroupId = "wrong-catalogue",
                AnimeGroupTitle = "Wrong",
                AnimeGroupSeasonNumber = 5
            });
            result.AnimeGroupId.Should().Be("anilist:1");
            result.AnimeGroupSeasonNumber.Should().Be(2);
        }

        [Test]
        public void should_not_accept_a_response_for_a_different_catalogue_id()
        {
            Chain(2);
            _nodes["2"].ForeignId = "99";
            Resolve("2").Status.Should().Be(AnimeGroupingStatus.Unavailable);
        }

        [Test]
        public void should_decline_an_overlong_catalogue_title_without_returning_invalid_group_metadata()
        {
            Chain(2);
            _nodes["1"].Title = new string('a', AnimeGroupingService.MaxGroupTitleLength + 1);
            var result = Resolve("2");
            result.IsResolved.Should().BeFalse();
            result.AnimeGroupId.Should().BeNull();
        }
    }
}
