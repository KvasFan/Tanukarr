using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.Grouping
{
    [TestFixture]
    public class AnimeGroupingServiceFixture : CoreTest
    {
        private Mock<IAnimeRelationProvider> _provider;
        private AnimeGroupingService _service;

        [SetUp]
        public void Setup()
        {
            _provider = new Mock<IAnimeRelationProvider>();
            _provider.SetupGet(p => p.Source).Returns(MetadataSourceType.AniList);
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series>());
            Mocker.GetMock<IBuildFileNames>().Setup(b => b.GetAnimeGroupFolder(It.IsAny<Series>(), null))
                .Returns((Series series, NamingConfig config) => AnimeGroupingService.CreateGroupFolder(series.AnimeGroupTitle));
            Mocker.GetMock<IBuildFileNames>().Setup(b => b.GetAnimeGroupMemberFolder(It.IsAny<Series>(), null))
                .Returns((Series series, NamingConfig config) => series.AnimeGroupMemberKind == AnimeGroupMemberKind.Season
                    ? "Season " + series.AnimeGroupSeasonNumber.Value.ToString("00")
                    : series.AnimeGroupKind == AnimeGroupKind.MovieSaga ? "Film " + series.AnimeGroupMovieNumber.Value.ToString("00") : series.Title);
            _service = new AnimeGroupingService(new[] { _provider.Object }, Mocker.GetMock<ISeriesService>().Object, Mocker.GetMock<IBuildFileNames>().Object, TestLogger);
        }

        [Test]
        public void should_keep_fixed_automatic_membership_without_contacting_the_provider()
        {
            var series = Grouped();
            var result = _service.Resolve(series);
            result.Status.Should().Be(AnimeGroupingStatus.Existing);
            result.AnimeGroupId.Should().Be("anilist:1");
            result.AnimeGroupSeasonNumber.Should().Be(3);
            _provider.Verify(p => p.GetAnimeRelations(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_leave_normal_add_fields_unchanged_when_relations_are_unavailable()
        {
            var series = new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "3", TvdbId = -4, Path = "/anime/Part 3" };
            _provider.Setup(p => p.GetAnimeRelations("3")).Throws(new InvalidOperationException("offline"));
            _service.Resolve(series).Status.Should().Be(AnimeGroupingStatus.Unavailable);
            _service.Apply(series);
            series.AnimeGroupId.Should().BeNull();
            series.ForeignId.Should().Be("3");
            series.TvdbId.Should().Be(-4);
            series.Path.Should().Be("/anime/Part 3");
        }

        [TestCase(SeriesTypes.Standard)]
        [TestCase(SeriesTypes.Anime)]
        public void should_resolve_catalogue_relations_independently_of_release_parsing_type(SeriesTypes type)
        {
            _provider.Setup(p => p.GetAnimeRelations("1")).Returns(new AnimeRelationNode
            {
                ForeignId = "1", Title = "Anime", Format = "TV", SequelIds = new List<string> { "2" }
            });
            _provider.Setup(p => p.GetAnimeRelations("2")).Returns(new AnimeRelationNode
            {
                ForeignId = "2", Title = "Anime 2", Format = "TV", PrequelIds = new List<string> { "1" }
            });
            var series = new Series { MetadataSource = MetadataSourceType.AniList, ForeignId = "2", SeriesType = type };
            _service.Apply(series);
            series.AnimeGroupSeasonNumber.Should().Be(2);
            series.SeriesType.Should().Be(type);
        }

        [Test]
        public void should_clear_only_group_metadata_when_disabled()
        {
            var series = Grouped();
            series.AnimeGroupingMode = AnimeGroupingMode.Disabled;
            _service.Apply(series);
            series.AnimeGroupId.Should().BeNull();
            series.AnimeGroupTitle.Should().BeNull();
            series.AnimeGroupSeasonNumber.Should().BeNull();
            series.AnimeGroupFolder.Should().BeNull();
            series.AnimeGroupMemberFolder.Should().BeNull();
            series.AnimeGroupMovieNumber.Should().BeNull();
            series.Path.Should().Be("/anime/Example/Season 03");
            series.ForeignId.Should().Be("3");
            _provider.Verify(p => p.GetAnimeRelations(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_derive_manual_folder_and_reuse_the_existing_canonical_group()
        {
            var previous = Grouped();
            previous.AnimeGroupTitle = "Existing Canonical Title";
            previous.AnimeGroupFolder = "Existing Canonical Folder";
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series> { previous });
            var series = new Series
            {
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = previous.AnimeGroupId,
                AnimeGroupTitle = "Client Title",
                AnimeGroupSeasonNumber = 4,
                AnimeGroupFolder = "../../untrusted"
            };
            _service.Apply(series);
            series.AnimeGroupTitle.Should().Be("Existing Canonical Title");
            series.AnimeGroupFolder.Should().Be("Existing Canonical Folder");
            series.AnimeGroupSeasonNumber.Should().Be(4);
        }

        [Test]
        public void should_preserve_a_stored_manual_folder_during_an_edit()
        {
            var series = Grouped();
            series.AnimeGroupingMode = AnimeGroupingMode.Manual;
            series.AnimeGroupFolder = "Original Folder";
            series.AnimeGroupTitle = "Corrected Display Title";
            _service.Apply(series);
            series.AnimeGroupFolder.Should().Be("Original Folder");
            series.AnimeGroupTitle.Should().Be("Corrected Display Title");
            series.Path.Should().Be("/anime/Example/Season 03");
        }

        [TestCase(AnimeGroupKind.Series, "Movie Title")]
        [TestCase(AnimeGroupKind.MovieSaga, "Film 02")]
        public void should_derive_movie_leaves_through_the_naming_port(AnimeGroupKind kind, string leaf)
        {
            var series = new Series
            {
                Title = "Movie Title", AnimeGroupingMode = AnimeGroupingMode.Manual, AnimeGroupId = "movie-group",
                AnimeGroupTitle = "Group Title", AnimeGroupMemberKind = AnimeGroupMemberKind.Movie,
                AnimeGroupKind = kind, AnimeGroupMovieNumber = 2
            };

            _service.Apply(series);
            series.AnimeGroupFolder.Should().Be("Group Title");
            series.AnimeGroupMemberFolder.Should().Be(leaf);
            series.AnimeGroupSeasonNumber.Should().BeNull();
            Mocker.GetMock<IBuildFileNames>().Verify(
                b => b.GetAnimeGroupMemberFolder(It.Is<Series>(s => s.Title == "Movie Title" && s.AnimeGroupMovieNumber == 2 && s.AnimeGroupMemberKind == AnimeGroupMemberKind.Movie), null),
                Times.Once());
        }

        [Test]
        public void should_promote_a_new_season_joining_a_movie_saga_without_changing_stored_movie_folders()
        {
            var movie = new Series
            {
                Id = 9, AnimeGroupingMode = AnimeGroupingMode.Manual, AnimeGroupId = "saga", AnimeGroupTitle = "Original Saga",
                AnimeGroupMemberKind = AnimeGroupMemberKind.Movie, AnimeGroupKind = AnimeGroupKind.MovieSaga,
                AnimeGroupMovieNumber = 1, AnimeGroupFolder = "Pinned Parent", AnimeGroupMemberFolder = "Film 01", Path = "/anime/Pinned Parent/Film 01"
            };
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series> { movie });
            var season = new Series
            {
                AnimeGroupingMode = AnimeGroupingMode.Manual, AnimeGroupId = "saga", AnimeGroupTitle = "New Title",
                AnimeGroupSeasonNumber = 1, AnimeGroupKind = AnimeGroupKind.Series
            };

            _service.Apply(season);
            season.AnimeGroupKind.Should().Be(AnimeGroupKind.Series);
            season.AnimeGroupFolder.Should().Be("Pinned Parent");
            season.AnimeGroupTitle.Should().Be("Original Saga");
            season.AnimeGroupMemberFolder.Should().Be("Season 01");
            movie.AnimeGroupKind.Should().Be(AnimeGroupKind.MovieSaga);
            movie.AnimeGroupMemberFolder.Should().Be("Film 01");
            movie.Path.Should().Be("/anime/Pinned Parent/Film 01");
        }

        [Test]
        public void should_keep_stored_movie_membership_and_folder_when_templates_or_style_change()
        {
            var movie = new Series
            {
                Id = 7, AnimeGroupingMode = AnimeGroupingMode.Automatic, AnimeGroupId = "anilist:1", AnimeGroupTitle = "Saga",
                AnimeGroupMemberKind = AnimeGroupMemberKind.Movie, AnimeGroupKind = AnimeGroupKind.MovieSaga,
                AnimeGroupMovieNumber = 2, AnimeGroupMovieFolderStyle = AnimeGroupMovieFolderStyle.Title,
                AnimeGroupFolder = "Pinned Parent", AnimeGroupMemberFolder = "Film 02", Path = "/anime/Pinned Parent/Film 02"
            };

            _service.Apply(movie);
            movie.AnimeGroupMovieNumber.Should().Be(2);
            movie.AnimeGroupMemberFolder.Should().Be("Film 02");
            movie.Path.Should().Be("/anime/Pinned Parent/Film 02");
            _provider.Verify(p => p.GetAnimeRelations(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IBuildFileNames>().Verify(b => b.GetAnimeGroupMemberFolder(It.IsAny<Series>(), null), Times.Never());
        }

        [TestCase(AnimeGroupMemberKind.Movie, AnimeGroupKind.Series, null, 0)]
        [TestCase(AnimeGroupMemberKind.Movie, AnimeGroupKind.MovieSaga, null, 1000)]
        [TestCase(AnimeGroupMemberKind.Movie, AnimeGroupKind.Series, 1, 1)]
        [TestCase(AnimeGroupMemberKind.Season, AnimeGroupKind.MovieSaga, 1, null)]
        [TestCase(AnimeGroupMemberKind.Season, AnimeGroupKind.Series, 1, 1)]
        [TestCase((AnimeGroupMemberKind)99, AnimeGroupKind.Series, 1, null)]
        [TestCase(AnimeGroupMemberKind.Movie, (AnimeGroupKind)99, null, 1)]
        public void should_not_resolve_inconsistent_manual_typed_membership(AnimeGroupMemberKind memberKind, AnimeGroupKind groupKind, int? season, int? movie)
        {
            _service.Resolve(new Series
            {
                AnimeGroupingMode = AnimeGroupingMode.Manual, AnimeGroupId = "manual", AnimeGroupTitle = "Title",
                AnimeGroupMemberKind = memberKind, AnimeGroupKind = groupKind,
                AnimeGroupSeasonNumber = season, AnimeGroupMovieNumber = movie
            }).IsResolved.Should().BeFalse();
            _provider.Verify(p => p.GetAnimeRelations(It.IsAny<string>()), Times.Never());
        }

        [TestCase("../")]
        [TestCase("A/B\\C: Title")]
        [TestCase("CON")]
        [TestCase("nul.txt")]
        [TestCase("..")]
        [TestCase("LPT1")]
        public void should_derive_a_safe_single_folder_leaf(string title)
        {
            var folder = AnimeGroupingService.CreateGroupFolder(title);
            folder.Should().NotBeNullOrWhiteSpace();
            folder.Should().NotContain("/").And.NotContain("\\");
            folder.Should().NotBe(".").And.NotBe("..");
            folder.Should().NotBe("CON").And.NotBe("nul.txt").And.NotBe("LPT1");
        }

        [TestCase("猫")]
        [TestCase("😀")]
        public void should_bound_multibyte_folder_names_without_splitting_unicode_characters(string character)
        {
            var title = string.Concat(Enumerable.Repeat(character, 90));
            var folder = AnimeGroupingService.CreateGroupFolder(title);
            Encoding.UTF8.GetByteCount(folder).Should().BeLessThanOrEqualTo(AnimeGroupingService.MaxGroupFolderBytes);
            Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(folder)).Should().Be(folder);
            folder.Should().NotBeNullOrWhiteSpace();
        }

        [TestCase("../../unsafe", "Title", 1)]
        [TestCase("group", "", 1)]
        [TestCase("group", "Title", 0)]
        [TestCase("group", "Title", 1000)]
        public void should_not_resolve_invalid_manual_fields(string id, string title, int number)
        {
            var result = _service.Resolve(new Series
            {
                AnimeGroupingMode = AnimeGroupingMode.Manual, AnimeGroupId = id, AnimeGroupTitle = title, AnimeGroupSeasonNumber = number
            });
            result.IsResolved.Should().BeFalse();
            _provider.Verify(p => p.GetAnimeRelations(It.IsAny<string>()), Times.Never());
        }

        [TestCase("group\n")]
        [TestCase("group\r")]
        [TestCase("group\r\n")]
        [TestCase("group\t")]
        [TestCase("group\u0000")]
        [TestCase("group\u0085")]
        [TestCase("group\u2028")]
        [TestCase("group\u2029")]
        public void should_reject_trailing_ascii_and_unicode_controls_in_group_ids(string id)
        {
            AnimeGroupingService.IsValidGroupId(id).Should().BeFalse();
            _service.Resolve(new Series
            {
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = id,
                AnimeGroupTitle = "Valid title",
                AnimeGroupSeasonNumber = 1
            }).IsResolved.Should().BeFalse();
        }

        private static Series Grouped()
        {
            return new Series
            {
                Id = 5, MetadataSource = MetadataSourceType.AniList, ForeignId = "3", TvdbId = -4,
                Path = "/anime/Example/Season 03", AnimeGroupId = "anilist:1", AnimeGroupTitle = "Example",
                AnimeGroupSeasonNumber = 3, AnimeGroupFolder = "Example"
            };
        }
    }
}
