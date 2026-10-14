using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class SeriesPresentationServiceFixture : CoreTest<SeriesPresentationService>
    {
        private Mock<ISeriesPresentationProvider> _aniList;
        private Mock<ISeriesPresentationProvider> _mal;
        private Mock<ISeriesPresentationProvider> _tmdb;
        private Mock<ISeriesPresentationProvider> _tvdb;

        [SetUp]
        public void Setup()
        {
            _aniList = Provider(MetadataSourceType.AniList);
            _mal = Provider(MetadataSourceType.MyAnimeList);
            _tmdb = Provider(MetadataSourceType.Tmdb);
            _tvdb = Provider(MetadataSourceType.Tvdb);
            Mocker.SetConstant<IEnumerable<ISeriesPresentationProvider>>(new[] { _aniList.Object, _mal.Object, _tmdb.Object, _tvdb.Object });
        }

        private static Mock<ISeriesPresentationProvider> Provider(MetadataSourceType source)
        {
            var provider = new Mock<ISeriesPresentationProvider>();
            provider.SetupGet(candidate => candidate.Source).Returns(source);
            return provider;
        }

        private static Series Anime()
        {
            return new Series
            {
                MetadataSource = MetadataSourceType.AniList,
                ForeignId = "100",
                AniListIds = new HashSet<int> { 100 },
                MalIds = new HashSet<int> { 200 },
                TvdbId = -37,
                Title = "Owner title",
                OrderingId = "owner-ordering",
                Path = "/media/Collection/Season 03",
                AnimeGroupSeasonNumber = 3,
                Seasons = new List<Season> { new Season { SeasonNumber = 1 } }
            };
        }

        private static SeriesPresentation Presentation(string host, string overview = "Alternative overview")
        {
            return new SeriesPresentation
            {
                Overview = overview,
                Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Poster, $"https://{host}/poster.jpg"),
                    new MediaCover.MediaCover(MediaCoverTypes.Fanart, $"https://{host}/backdrop.jpg")
                }
            };
        }

        [Test]
        public void should_fill_only_presentation_without_changing_the_metadata_owner_native_ids_ordering_paths_or_seasons()
        {
            var series = Anime();
            _mal.Setup(provider => provider.GetPresentation(It.IsAny<Series>())).Returns(Presentation("cdn.myanimelist.net"));
            Subject.Enrich(series);

            series.Images.Select(image => image.RemoteUrl).Should().Equal("https://cdn.myanimelist.net/poster.jpg", "https://cdn.myanimelist.net/backdrop.jpg");
            series.Overview.Should().Be("Alternative overview");
            series.MetadataSource.Should().Be(MetadataSourceType.AniList);
            series.ForeignId.Should().Be("100");
            series.AniListIds.Should().Equal(100);
            series.MalIds.Should().Equal(200);
            series.TvdbId.Should().Be(-37);
            series.Title.Should().Be("Owner title");
            series.OrderingId.Should().Be("owner-ordering");
            series.Path.Should().Be("/media/Collection/Season 03");
            series.AnimeGroupSeasonNumber.Should().Be(3);
            series.Seasons.Single().SeasonNumber.Should().Be(1);
        }

        [Test]
        public void should_not_replace_valid_owner_poster_or_description_when_adding_a_missing_backdrop()
        {
            var series = Anime();
            series.MetadataSource = MetadataSourceType.MyAnimeList;
            series.ForeignId = "200";
            series.Overview = "Owner overview";
            series.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, "https://owner.invalid/poster.jpg"));
            _aniList.Setup(provider => provider.GetPresentation(It.IsAny<Series>())).Returns(Presentation("s4.anilist.co"));
            Subject.Enrich(series);

            series.Overview.Should().Be("Owner overview");
            series.Images.Single(image => image.CoverType == MediaCoverTypes.Poster).RemoteUrl.Should().Be("https://owner.invalid/poster.jpg");
            series.Images.Single(image => image.CoverType == MediaCoverTypes.Fanart).RemoteUrl.Should().Be("https://s4.anilist.co/backdrop.jpg");
        }

        [Test]
        public void should_preserve_previous_presentation_for_the_same_owner_when_a_refresh_returns_empty_fields()
        {
            var previous = Anime();
            previous.Overview = "Saved overview";
            previous.Images = Presentation("owner.invalid").Images;
            var current = Anime();
            Subject.Enrich(current, previous);

            current.Overview.Should().Be("Saved overview");
            current.Images.Should().HaveCount(2);
            current.Images.First().Should().NotBeSameAs(previous.Images.First());
            _mal.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
        }

        [Test]
        public void should_not_attach_presentation_from_an_old_provider_identity_after_a_replacement()
        {
            var previous = Anime();
            previous.Overview = "Wrong old overview";
            previous.Images = Presentation("owner.invalid").Images;
            var current = Anime();
            current.ForeignId = "101";
            Subject.Enrich(current, previous);
            current.Overview.Should().BeNull();
            current.Images.Should().BeEmpty();
        }

        [Test]
        public void should_use_known_anime_ids_for_tvdb_owned_metadata_without_treating_a_synthetic_tvdb_id_as_real()
        {
            var series = Anime();
            series.MetadataSource = MetadataSourceType.Tvdb;
            series.ForeignId = null;
            _aniList.Setup(provider => provider.GetPresentation(It.Is<Series>(identity => identity.AniListIds.Contains(100))))
                .Returns(Presentation("s4.anilist.co"));
            Subject.Enrich(series);
            series.Images.Should().HaveCount(2);
            _tvdb.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
        }

        [Test]
        public void should_not_guess_identity_by_title_or_select_arbitrary_ids_from_an_ambiguous_season_set()
        {
            var series = new Series
            {
                MetadataSource = MetadataSourceType.Tvdb,
                TvdbId = -1,
                Title = "91 Days",
                AniListIds = new HashSet<int> { 100, 101 },
                MalIds = new HashSet<int> { 200, 201 }
            };
            Subject.Enrich(series);
            _aniList.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
            _mal.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
            _tmdb.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
            _tvdb.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
        }

        [Test]
        public void should_use_at_most_two_alternative_providers_and_share_a_failure_cooldown_across_series()
        {
            var series = Anime();
            series.MetadataSource = MetadataSourceType.Tvdb;
            series.TvdbId = 10;
            series.TmdbId = 20;
            _tmdb.Setup(provider => provider.GetPresentation(It.IsAny<Series>())).Throws(new InvalidOperationException("Private key must not be logged"));
            _aniList.Setup(provider => provider.GetPresentation(It.IsAny<Series>())).Returns((SeriesPresentation)null);
            Subject.Enrich(series);
            _tmdb.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Once());
            _aniList.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Once());
            _mal.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());

            var second = Anime();
            second.MetadataSource = MetadataSourceType.Tvdb;
            second.TmdbId = 21;
            second.AniListIds = new HashSet<int> { 101 };
            Subject.Enrich(second);
            _tmdb.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Once());
            series.Images.Should().BeEmpty();
        }

        [TestCase("http://cdn.myanimelist.net/poster.jpg")]
        [TestCase("https://localhost/poster.jpg")]
        [TestCase("https://127.0.0.1/poster.jpg")]
        [TestCase("https://cdn.myanimelist.net.evil.invalid/poster.jpg")]
        [TestCase("https://user:password@cdn.myanimelist.net/poster.jpg")]
        [TestCase("https://cdn.myanimelist.net:9443/poster.jpg")]
        [TestCase("file:///tmp/poster.jpg")]
        public void should_reject_untrusted_alternative_image_urls(string url)
        {
            var series = Anime();
            _mal.Setup(provider => provider.GetPresentation(It.IsAny<Series>())).Returns(new SeriesPresentation
            {
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, url) }
            });
            Subject.Enrich(series);
            series.Images.Should().BeEmpty();
        }

        [Test]
        public void should_cache_public_results_without_exposing_mutable_graphs_or_owner_settings_to_providers()
        {
            var series = Anime();
            _mal.Setup(provider => provider.GetPresentation(It.Is<Series>(identity => identity.Path == null && identity.Title == null && identity.Id == 0)))
                .Returns(Presentation("cdn.myanimelist.net"));
            Subject.Enrich(series);
            series.Images.Clear();
            series.Overview = null;
            var cache = new SeriesPresentationCache(Mocker.Resolve<ICacheManager>());
            var another = new SeriesPresentationService(new[] { _mal.Object }, cache, TestLogger);
            another.Enrich(series);

            _mal.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Once());
            series.Images.Should().HaveCount(2);
            series.Overview.Should().Be("Alternative overview");
        }

        [Test]
        public void should_not_spend_jikan_quota_when_only_a_backdrop_is_missing()
        {
            var series = Anime();
            series.Overview = "Complete owner overview";
            series.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, "https://s4.anilist.co/poster.jpg"));
            Subject.Enrich(series);
            _mal.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
        }

        [Test]
        public void should_use_cached_public_presentation_even_when_another_lookup_has_put_the_provider_in_cooldown()
        {
            var series = Anime();
            _mal.Setup(provider => provider.GetPresentation(It.IsAny<Series>())).Returns(Presentation("cdn.myanimelist.net"));
            Subject.Enrich(series);
            var cache = new SeriesPresentationCache(Mocker.Resolve<ICacheManager>());
            cache.BackOff(MetadataSourceType.MyAnimeList);
            series.Images.Clear();
            series.Overview = null;
            Subject.Enrich(series);
            series.Images.Should().HaveCount(2);
            _mal.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Once());
        }

        [Test]
        public void should_reuse_previously_verified_cross_ids_for_lookup_without_assigning_them_as_current_owner_ids()
        {
            var previous = Anime();
            var current = Anime();
            current.MalIds.Clear();
            _mal.Setup(provider => provider.GetPresentation(It.Is<Series>(identity => identity.MalIds.Contains(200))))
                .Returns(Presentation("cdn.myanimelist.net"));
            Subject.Enrich(current, previous);
            current.Images.Should().HaveCount(2);
            current.MalIds.Should().BeEmpty();
            current.AniListIds.Should().Equal(100);
            current.ForeignId.Should().Be("100");
        }

        [TestCase("anilist:100", MetadataSourceType.AniList, "s4.anilist.co")]
        [TestCase("mal:200", MetadataSourceType.MyAnimeList, "cdn.myanimelist.net")]
        public void should_fetch_the_verified_canonical_collection_root_without_requiring_a_local_first_season(string groupId, MetadataSourceType source, string host)
        {
            var provider = source == MetadataSourceType.AniList ? _aniList : _mal;
            provider.Setup(candidate => candidate.GetPresentation(It.Is<Series>(identity =>
                identity.ForeignId == groupId.Split(':', StringSplitOptions.None)[1] && identity.MetadataSource == source && identity.Id == 0)))
                .Returns(Presentation(host, "Canonical collection overview"));
            var first = Subject.GetCollectionPresentation(groupId);
            first.Overview.Should().Be("Canonical collection overview");
            first.Images.Single(image => image.CoverType == MediaCoverTypes.Poster).RemoteUrl.Should().Be($"https://{host}/poster.jpg");
            first.Images.Clear();
            Subject.GetCollectionPresentation(groupId).Images.Should().HaveCount(2);
            provider.Verify(candidate => candidate.GetPresentation(It.IsAny<Series>()), Times.Once());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("manual:custom")]
        [TestCase("tvdb:100")]
        [TestCase("anilist:0")]
        [TestCase("anilist:-100")]
        [TestCase("anilist:+100")]
        [TestCase("anilist:100 ")]
        [TestCase("anilist:0100")]
        [TestCase("anilist:100:200")]
        [TestCase("https://graphql.anilist.co/100")]
        [TestCase("Anilist:100")]
        public void should_not_make_a_collection_root_request_for_manual_unknown_or_noncanonical_group_ids(string groupId)
        {
            Subject.GetCollectionPresentation(groupId).Should().BeNull();
            _aniList.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
            _mal.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
            _tmdb.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
            _tvdb.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Never());
        }

        [Test]
        public void should_keep_the_collection_page_usable_when_the_canonical_provider_is_unavailable()
        {
            _aniList.Setup(provider => provider.GetPresentation(It.IsAny<Series>())).Throws(new InvalidOperationException("Unavailable catalogue"));
            Subject.GetCollectionPresentation("anilist:100").Should().BeNull();
            Subject.GetCollectionPresentation("anilist:101").Should().BeNull();
            _aniList.Verify(provider => provider.GetPresentation(It.IsAny<Series>()), Times.Once());
        }
    }
}
