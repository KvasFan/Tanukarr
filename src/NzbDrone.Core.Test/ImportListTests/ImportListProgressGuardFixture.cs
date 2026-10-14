using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Progress;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListProgressGuardFixture : CoreTest
    {
        private static List<Episode> Episodes(params int[] numbers) => numbers.Select(number => new Episode
        {
            Id = number + 100,
            SeasonNumber = 1,
            EpisodeNumber = number,
            AbsoluteEpisodeNumber = number + 900,
            SceneEpisodeNumber = number + 700
        }).ToList();

        [TestCase(new[] { 1, 2, 3 }, new[] { 101, 102 }, 2)]
        [TestCase(new[] { 1, 2, 3 }, new[] { 101, 103 }, 1)]
        [TestCase(new[] { 1, 3 }, new[] { 101, 103 }, 1)]
        [TestCase(new[] { 2, 3 }, new[] { 102, 103 }, 0)]
        [TestCase(new[] { 1, 1, 2 }, new[] { 101, 102 }, 0)]
        [TestCase(new[] { 3, 1, 2 }, new[] { 101, 102, 103 }, 3)]
        public void should_only_count_the_watched_contiguous_native_prefix(int[] numbers, int[] watched, int expected)
        {
            ImportListProgressGuard.ContiguousProgress(Episodes(numbers), watched).Should().Be(expected);
        }

        [Test]
        public void should_ignore_specials_and_unrelated_episode_ids()
        {
            var episodes = Episodes(1, 2, 3);
            episodes.Add(new Episode { Id = 999, SeasonNumber = 0, EpisodeNumber = 1 });
            ImportListProgressGuard.ContiguousProgress(episodes, new[] { 101, 102, 999, 800 }).Should().Be(2);
        }

        [Test]
        public void should_reject_scalar_progress_from_multiple_catalog_seasons()
        {
            var episodes = Episodes(1, 2);
            episodes.Add(new Episode { Id = 300, SeasonNumber = 2, EpisodeNumber = 1 });
            ImportListProgressGuard.ContiguousProgress(episodes, new[] { 101, 102, 300 }).Should().Be(0);
            ImportListProgressGuard.ContiguousProgress(episodes, new[] { 101, 102, 300 }, true).Should().Be(3);
        }

        [Test]
        public void should_stop_when_a_native_season_or_episode_is_missing()
        {
            var episodes = Episodes(1, 2);
            episodes.Add(new Episode { Id = 300, SeasonNumber = 3, EpisodeNumber = 1 });
            ImportListProgressGuard.ContiguousProgress(episodes, new[] { 101, 102, 300 }, true).Should().Be(2);
            episodes.Last().SeasonNumber = 2;
            episodes.Last().EpisodeNumber = 2;
            ImportListProgressGuard.ContiguousProgress(episodes, new[] { 101, 102, 300 }, true).Should().Be(2);
        }

        [Test]
        public void should_require_the_owning_catalog_identity_and_default_order()
        {
            var series = new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "12", TvdbId = -12, AnimeGroupSeasonNumber = 3 };
            ImportListProgressGuard.NativeIdentity(series, MetadataSourceType.AniList, 12).Should().BeTrue();
            ImportListProgressGuard.NativeIdentity(series, MetadataSourceType.MyAnimeList, 12).Should().BeFalse();
            ImportListProgressGuard.NativeIdentity(series, MetadataSourceType.AniList, 0).Should().BeFalse();
            series.OrderingId = "alternate";
            ImportListProgressGuard.NativeIdentity(series, MetadataSourceType.AniList, 12).Should().BeFalse();
        }

        [TestCase("http://graphql.anilist.co")]
        [TestCase("https://graphql.anilist.co:8443")]
        [TestCase("https://graphql.anilist.co/unexpected")]
        [TestCase("https://graphql.anilist.co?secret=fixture")]
        [TestCase("https://graphql.anilist.co#fragment")]
        [TestCase("https://user@graphql.anilist.co")]
        [TestCase("https://graphql.anilist.co.example")]
        public void should_refuse_credential_forwarding_to_modified_provider_roots(string configured)
        {
            Action action = () => ImportListProgressGuard.OfficialRoot(configured, "https://graphql.anilist.co");
            action.Should().Throw<ImportListProgressUnavailableException>();
        }

        [Test]
        public void should_redact_private_request_details_and_replace_an_existing_auth_header()
        {
            var request = new HttpRequest("https://graphql.anilist.co") { ContentSummary = "private fixture" };
            request.Headers.Add("Authorization", "old fixture");
            ImportListProgressGuard.PrivateRequest(request, "new-fixture-token");
            request.Headers.GetValues("Authorization").Should().Equal("Bearer new-fixture-token");
            request.ContentSummary.Should().BeNull();
            request.AllowAutoRedirect.Should().BeFalse();
            request.LogResponseContent.Should().BeFalse();
            request.LogHttpError.Should().BeFalse();
            request.ToString().Should().NotContain("fixture-token");
        }

        [Test]
        public void should_store_only_hashes_without_component_boundary_collisions()
        {
            var first = ImportListProgressGuard.Fingerprint("secret-token", "a:b", "c");
            first.Should().HaveLength(64).And.NotContain("secret-token");
            first.Should().NotBe(ImportListProgressGuard.Fingerprint("secret-token", "a", "b:c"));
        }
    }
}
