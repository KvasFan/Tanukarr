using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.SeriesStats;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.SeriesTests
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class AnimeGroupingUpdateFixture : TestBase
    {
        private readonly bool _useV3;
        private NzbDrone.Core.Tv.Series _series;
        private List<NzbDrone.Core.Tv.Series> _existing;

        public AnimeGroupingUpdateFixture(bool useV3)
        {
            _useV3 = useV3;
        }

        [SetUp]
        public void Setup()
        {
            _series = new NzbDrone.Core.Tv.Series
            {
                Id = 42,
                Title = "Anime second entry",
                TitleSlug = "anilist-200",
                MetadataSource = MetadataSourceType.AniList,
                ForeignId = "200",
                TvdbId = -42,
                Path = Path.Combine(Path.GetTempPath(), "Tanukarr-group-update", "Existing folder"),
                QualityProfileId = 1,
                SeasonType = SeasonType.Official,
                SeriesType = SeriesTypes.Standard,
                Language = Language.English,
                Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = true } },
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = "manual:original",
                AnimeGroupTitle = "Original anime",
                AnimeGroupSeasonNumber = 2,
                AnimeGroupFolder = "Original anime"
            };
            _existing = new List<NzbDrone.Core.Tv.Series> { _series };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(42)).Returns(() => _series);
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(() => _existing);
            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.UpdateSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                  .Returns<NzbDrone.Core.Tv.Series, string, string, bool>((model, source, destination, move) => model);
            Mocker.GetMock<ISeriesStatisticsService>().Setup(s => s.SeriesStatistics(42, 1)).Returns(new SeriesStatistics());
            Mocker.GetMock<ISeriesTranslationService>().Setup(s => s.GetTranslations(42)).Returns(new List<SeriesTranslation>());
            Mocker.GetMock<IBuildFileNames>().Setup(f => f.GetAnimeGroupFolder(It.IsAny<NzbDrone.Core.Tv.Series>(), null))
                .Returns<NzbDrone.Core.Tv.Series, NamingConfig>((s, config) => AnimeGroupingService.CreateGroupFolder(s.AnimeGroupTitle));
            Mocker.GetMock<IBuildFileNames>().Setup(f => f.GetAnimeGroupMemberFolder(It.IsAny<NzbDrone.Core.Tv.Series>(), null))
                .Returns<NzbDrone.Core.Tv.Series, NamingConfig>((s, config) => s.AnimeGroupMemberKind == AnimeGroupMemberKind.Movie ? s.Title : "Season " + s.AnimeGroupSeasonNumber.GetValueOrDefault().ToString("00"));
            Mocker.SetConstant<IAnimeGroupingService>(new AnimeGroupingService(Array.Empty<IAnimeRelationProvider>(), Mocker.GetMock<ISeriesService>().Object, Mocker.GetMock<IBuildFileNames>().Object, LogManager.GetCurrentClassLogger()));
        }

        [Test]
        public void should_preserve_manual_membership_for_legacy_put_without_group_fields()
        {
            var resource = Resource();
            resource.AnimeGroupingMode = null;
            resource.AnimeGroupId = null;
            resource.AnimeGroupTitle = null;
            resource.AnimeGroupSeasonNumber = null;

            Update(resource);

            _series.AnimeGroupingMode.Should().Be(AnimeGroupingMode.Manual);
            _series.AnimeGroupId.Should().Be("manual:original");
            _series.AnimeGroupSeasonNumber.Should().Be(2);
            AssertIdentityAndPath();
        }

        [Test]
        public void should_change_manual_membership_without_moving_or_renumbering_the_native_entry()
        {
            var resource = Resource();
            resource.AnimeGroupId = "manual:corrected";
            resource.AnimeGroupTitle = "Corrected anime";
            resource.AnimeGroupSeasonNumber = 3;
            resource.AnimeGroupFolder = "../client-controlled-folder";

            Update(resource);

            _series.AnimeGroupId.Should().Be("manual:corrected");
            _series.AnimeGroupFolder.Should().Be("Corrected anime");
            _series.AnimeGroupSeasonNumber.Should().Be(3);
            AssertIdentityAndPath();
        }

        [Test]
        public void should_ignore_client_manufactured_automatic_membership()
        {
            _series.AnimeGroupingMode = AnimeGroupingMode.Automatic;
            var resource = Resource();
            resource.AnimeGroupId = "manual:injected";
            resource.AnimeGroupTitle = "Injected group";
            resource.AnimeGroupSeasonNumber = 99;
            resource.AnimeGroupFolder = "Injected folder";

            Update(resource);

            _series.AnimeGroupId.Should().Be("manual:original");
            _series.AnimeGroupTitle.Should().Be("Original anime");
            _series.AnimeGroupSeasonNumber.Should().Be(2);
            _series.AnimeGroupFolder.Should().Be("Original anime");
            AssertIdentityAndPath();
        }

        [Test]
        public void should_reset_manual_membership_when_returning_to_automatic_with_no_available_relations()
        {
            var resource = Resource();
            resource.AnimeGroupingMode = AnimeGroupingMode.Automatic;

            Update(resource);

            _series.AnimeGroupingMode.Should().Be(AnimeGroupingMode.Automatic);
            _series.AnimeGroupId.Should().BeNull();
            _series.AnimeGroupFolder.Should().BeNull();
            AssertIdentityAndPath();
        }

        [Test]
        public void should_disable_membership_without_changing_existing_storage()
        {
            var resource = Resource();
            resource.AnimeGroupingMode = AnimeGroupingMode.Disabled;

            Update(resource);

            _series.AnimeGroupId.Should().BeNull();
            _series.AnimeGroupTitle.Should().BeNull();
            _series.AnimeGroupSeasonNumber.Should().BeNull();
            _series.AnimeGroupFolder.Should().BeNull();
            AssertIdentityAndPath();
        }

        [Test]
        public void should_reject_an_occupied_season_before_saving()
        {
            _existing.Add(new NzbDrone.Core.Tv.Series
            {
                Id = 99,
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = "manual:occupied",
                AnimeGroupTitle = "Another anime",
                AnimeGroupFolder = "Another anime",
                AnimeGroupSeasonNumber = 3
            });
            var resource = Resource();
            resource.AnimeGroupId = "manual:occupied";
            resource.AnimeGroupTitle = "Another anime";
            resource.AnimeGroupSeasonNumber = 3;

            Action update = () => Update(resource);
            update.Should().Throw<ValidationException>();
            Mocker.GetMock<ISeriesService>().Verify(s => s.UpdateSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_reject_an_undefined_grouping_mode_before_saving()
        {
            var resource = Resource();
            resource.AnimeGroupingMode = (AnimeGroupingMode)999;

            Action update = () => Update(resource);
            update.Should().Throw<ValidationException>();
            Mocker.GetMock<ISeriesService>().Verify(s => s.UpdateSeries(It.IsAny<NzbDrone.Core.Tv.Series>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        private Sonarr.Api.V5.Series.SeriesResource Resource()
        {
            return Sonarr.Api.V5.Series.SeriesResourceMapper.ToResource(_series);
        }

        [Test]
        public void should_preserve_typed_movie_membership_for_a_legacy_put()
        {
            _series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            _series.AnimeGroupKind = AnimeGroupKind.MovieSaga;
            _series.AnimeGroupSeasonNumber = null;
            _series.AnimeGroupMovieNumber = 2;
            _series.AnimeGroupMovieFolderStyle = AnimeGroupMovieFolderStyle.Numbered;
            _series.AnimeGroupMemberFolder = "Film 02";
            var resource = Resource();
            resource.AnimeGroupingMode = null;
            resource.AnimeGroupMemberKind = null;
            resource.AnimeGroupKind = null;
            resource.AnimeGroupMovieNumber = null;
            resource.AnimeGroupMovieFolderStyle = null;
            resource.AnimeGroupMemberFolder = "../../forged-member";

            Update(resource);

            _series.AnimeGroupMemberKind.Should().Be(AnimeGroupMemberKind.Movie);
            _series.AnimeGroupKind.Should().Be(AnimeGroupKind.MovieSaga);
            _series.AnimeGroupMovieNumber.Should().Be(2);
            _series.AnimeGroupMovieFolderStyle.Should().Be(AnimeGroupMovieFolderStyle.Numbered);
            _series.AnimeGroupMemberFolder.Should().Be("Film 02");
            _series.AnimeGroupSeasonNumber.Should().BeNull();
            AssertIdentityAndPath();
        }

        [Test]
        public void should_allow_manual_movie_correction_without_moving_or_renumbering_native_content()
        {
            var resource = Resource();
            resource.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            resource.AnimeGroupMovieNumber = 1;
            resource.AnimeGroupSeasonNumber = null;
            resource.AnimeGroupMemberFolder = "../../forged-member";

            Update(resource);

            _series.AnimeGroupMemberKind.Should().Be(AnimeGroupMemberKind.Movie);
            _series.AnimeGroupMovieNumber.Should().Be(1);
            _series.AnimeGroupSeasonNumber.Should().BeNull();
            _series.AnimeGroupMemberFolder.Should().Be(_series.Title);
            AssertIdentityAndPath();
        }

        [Test]
        public void should_discard_client_typed_movie_facts_for_an_existing_automatic_season()
        {
            _series.AnimeGroupingMode = AnimeGroupingMode.Automatic;
            _series.AnimeGroupMemberFolder = "Season 02";
            var resource = Resource();
            resource.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            resource.AnimeGroupKind = AnimeGroupKind.MovieSaga;
            resource.AnimeGroupMovieNumber = 99;
            resource.AnimeGroupSeasonNumber = null;
            resource.AnimeGroupMemberFolder = "../../forged-member";

            Update(resource);

            _series.AnimeGroupMemberKind.Should().Be(AnimeGroupMemberKind.Season);
            _series.AnimeGroupKind.Should().Be(AnimeGroupKind.Series);
            _series.AnimeGroupSeasonNumber.Should().Be(2);
            _series.AnimeGroupMovieNumber.Should().BeNull();
            _series.AnimeGroupMemberFolder.Should().Be("Season 02");
            AssertIdentityAndPath();
        }

        private void Update(Sonarr.Api.V5.Series.SeriesResource resource)
        {
            if (_useV3)
            {
                var controller = Mocker.Resolve<Sonarr.Api.V3.Series.SeriesController>();
                controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
                var legacy = new Sonarr.Api.V3.Series.SeriesResource();
                foreach (var property in resource.GetType().GetProperties())
                {
                    var target = legacy.GetType().GetProperty(property.Name);
                    if (target?.CanWrite == true && target.PropertyType.IsAssignableFrom(property.PropertyType))
                    {
                        target.SetValue(legacy, property.GetValue(resource));
                    }
                }

                legacy.Seasons = Sonarr.Api.V3.Series.SeriesResourceMapper.ToResource(_series).Seasons;
                controller.UpdateSeries(legacy);
            }
            else
            {
                var controller = Mocker.Resolve<Sonarr.Api.V5.Series.SeriesController>();
                controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
                controller.Url = new Mock<IUrlHelper>().Object;
                controller.UpdateSeries(resource);
            }
        }

        private void AssertIdentityAndPath()
        {
            _series.Id.Should().Be(42);
            _series.MetadataSource.Should().Be(MetadataSourceType.AniList);
            _series.ForeignId.Should().Be("200");
            _series.TvdbId.Should().Be(-42);
            _series.Seasons.Should().ContainSingle().Which.SeasonNumber.Should().Be(1);
            var expected = Path.Combine(Path.GetTempPath(), "Tanukarr-group-update", "Existing folder");
            _series.Path.Should().Be(expected);
            Mocker.GetMock<ISeriesService>().Verify(s => s.UpdateSeries(_series, expected, expected, false), Times.Once());
        }
    }
}
