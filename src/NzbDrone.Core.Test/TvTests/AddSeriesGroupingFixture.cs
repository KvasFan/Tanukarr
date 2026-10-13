using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class AddSeriesGroupingFixture : CoreTest<AddSeriesService>
    {
        [SetUp]
        public void Setup()
        {
            var provider = new Mock<IMetadataProvider>();
            provider.Setup(p => p.GetSeriesInfo(It.IsAny<Series>()))
                    .Returns<Series>(request => Tuple.Create(
                        new Series
                        {
                            MetadataSource = MetadataSourceType.AniList,
                            ForeignId = request.ForeignId,
                            Title = "Second catalogue entry",
                            TitleSlug = "anilist-" + request.ForeignId,
                            Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = true } }
                        },
                        new List<Episode>()));
            Mocker.GetMock<IMetadataProviderFactory>().Setup(f => f.GetProvider(It.IsAny<Series>())).Returns(provider.Object);
            Mocker.GetMock<IAllocateSyntheticSeriesIds>().Setup(s => s.AllocateTvdbId()).Returns(-1);
            Mocker.GetMock<IAddSeriesValidator>().Setup(v => v.Validate(It.IsAny<Series>())).Returns(new ValidationResult());
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series>());
            Mocker.GetMock<ISeriesService>().Setup(s => s.AllSeriesTvdbIds()).Returns(new Dictionary<int, int>());
            Mocker.GetMock<IAnimeGroupPathBuilder>().Setup(p => p.BuildPath(It.IsAny<Series>()))
                  .Returns<Series>(s => Path.Combine(s.RootFolderPath, "Canonical anime", "Season 02"));
            Mocker.GetMock<IBuildFileNames>().Setup(f => f.GetSeriesFolder(It.IsAny<Series>(), null))
                  .Returns<Series, NamingConfig>((s, config) => s.Title);
        }

        [Test]
        public void should_generate_a_group_leaf_and_keep_owner_and_native_season()
        {
            GivenResolvedGroup();
            var request = Request();

            var result = Subject.AddSeries(request);

            result.Path.Should().Be(Path.Combine(request.RootFolderPath, "Canonical anime", "Season 02"));
            result.SeasonFolder.Should().BeFalse();
            result.MetadataSource.Should().Be(MetadataSourceType.AniList);
            result.ForeignId.Should().Be("200");
            result.TvdbId.Should().Be(-1);
            result.AnimeGroupSeasonNumber.Should().Be(2);
            result.Seasons.Should().ContainSingle().Which.SeasonNumber.Should().Be(1);
        }

        [Test]
        public void should_discard_client_automatic_facts_before_server_discovery()
        {
            var request = Request();
            request.AnimeGroupId = "manual:injected";
            request.AnimeGroupTitle = "Injected title";
            request.AnimeGroupSeasonNumber = 999;
            request.AnimeGroupFolder = "../outside";
            request.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            request.AnimeGroupKind = AnimeGroupKind.MovieSaga;
            request.AnimeGroupMovieNumber = 998;
            request.AnimeGroupMemberFolder = "../../outside";
            Mocker.GetMock<IAnimeGroupingService>().Setup(g => g.Apply(It.IsAny<Series>()))
                  .Callback<Series>(series =>
                  {
                      series.AnimeGroupId.Should().BeNull();
                      series.AnimeGroupTitle.Should().BeNull();
                      series.AnimeGroupSeasonNumber.Should().BeNull();
                      series.AnimeGroupFolder.Should().BeNull();
                      series.AnimeGroupMemberKind.Should().Be(AnimeGroupMemberKind.Season);
                      series.AnimeGroupKind.Should().Be(AnimeGroupKind.Series);
                      series.AnimeGroupMovieNumber.Should().BeNull();
                      series.AnimeGroupMemberFolder.Should().BeNull();
                  });

            var result = Subject.AddSeries(request);

            result.AnimeGroupId.Should().BeNull();
            result.Path.Should().Be(Path.Combine(request.RootFolderPath, result.Title));
            Mocker.GetMock<IAnimeGroupPathBuilder>().Verify(p => p.BuildPath(It.IsAny<Series>()), Times.Never());
        }

        [Test]
        public void should_preserve_an_explicit_existing_path_and_season_folder_preference()
        {
            GivenResolvedGroup();
            var request = Request();
            request.Path = Path.Combine(request.RootFolderPath, "Existing second entry");

            var result = Subject.AddSeries(request);

            result.Path.Should().Be(request.Path);
            result.SeasonFolder.Should().BeTrue();
            result.AnimeGroupSeasonNumber.Should().Be(2);
            Mocker.GetMock<IAnimeGroupPathBuilder>().Verify(p => p.BuildPath(It.IsAny<Series>()), Times.Never());
        }

        [Test]
        public void should_keep_normal_add_available_when_grouping_cannot_resolve()
        {
            var request = Request();

            var result = Subject.AddSeries(request);

            result.Path.Should().Be(Path.Combine(request.RootFolderPath, result.Title));
            result.SeasonFolder.Should().BeTrue();
            result.AnimeGroupId.Should().BeNull();
        }

        [Test]
        public void should_reject_a_duplicate_group_slot_inside_one_import_list_batch()
        {
            GivenResolvedGroup();
            var second = Request();
            second.ForeignId = "201";

            Action add = () => Subject.AddSeries(new List<Series> { Request(), second });

            add.Should().Throw<ValidationException>()
               .Which.Errors.Should().Contain(error => error.PropertyName == "AnimeGroupSeasonNumber");
            Mocker.GetMock<ISeriesService>().Verify(s => s.AddSeries(It.IsAny<List<Series>>()), Times.Never());
        }

        [Test]
        public void should_add_a_movie_to_its_own_group_leaf_without_changing_native_season_identity()
        {
            Mocker.GetMock<IAnimeGroupingService>().Setup(g => g.Apply(It.IsAny<Series>()))
                .Callback<Series>(s =>
                {
                    s.AnimeGroupId = "anilist:100";
                    s.AnimeGroupTitle = "Canonical anime";
                    s.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
                    s.AnimeGroupMovieNumber = 1;
                    s.AnimeGroupSeasonNumber = null;
                    s.AnimeGroupFolder = "Canonical anime";
                    s.AnimeGroupMemberFolder = "Feature film";
                });
            Mocker.GetMock<IAnimeGroupPathBuilder>().Setup(p => p.BuildPath(It.IsAny<Series>()))
                .Returns<Series>(s => Path.Combine(s.RootFolderPath, s.AnimeGroupFolder, s.AnimeGroupMemberFolder));
            var request = Request();

            var result = Subject.AddSeries(request);

            result.Path.Should().Be(Path.Combine(request.RootFolderPath, "Canonical anime", "Feature film"));
            result.SeasonFolder.Should().BeFalse();
            result.AnimeGroupMovieNumber.Should().Be(1);
            result.Seasons.Should().ContainSingle().Which.SeasonNumber.Should().Be(1);
            result.ForeignId.Should().Be("200");
        }

        private void GivenResolvedGroup()
        {
            Mocker.GetMock<IAnimeGroupingService>().Setup(g => g.Apply(It.IsAny<Series>()))
                  .Callback<Series>(series =>
                  {
                      series.AnimeGroupId = "anilist:100";
                      series.AnimeGroupTitle = "Canonical anime";
                      series.AnimeGroupSeasonNumber = 2;
                      series.AnimeGroupFolder = "Canonical anime";
                  });
        }

        private static Series Request()
        {
            return new Series
            {
                MetadataSource = MetadataSourceType.AniList,
                ForeignId = "200",
                RootFolderPath = Path.Combine(Path.GetTempPath(), "Tanukarr-add-grouping"),
                AnimeGroupingMode = AnimeGroupingMode.Automatic,
                SeriesType = SeriesTypes.Standard,
                SeasonType = SeasonType.Official,
                SeasonFolder = true,
                Language = Language.English
            };
        }
    }
}
