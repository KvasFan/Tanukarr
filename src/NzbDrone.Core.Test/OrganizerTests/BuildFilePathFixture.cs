using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]

    public class BuildFilePathFixture : CoreTest<FileNameBuilder>
    {
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _namingConfig = NamingConfig.Default;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig()).Returns(_namingConfig);
        }

        [Test]
        [TestCase("30 Rock - S01E05 - Episode Title", 1, true, "Season {season:00}", @"C:\Test\30 Rock\Season 01\30 Rock - S01E05 - Episode Title.mkv")]
        [TestCase("30 Rock - S01E05 - Episode Title", 1, true, "Season {season}", @"C:\Test\30 Rock\Season 1\30 Rock - S01E05 - Episode Title.mkv")]
        [TestCase("30 Rock - S01E05 - Episode Title", 1, false, "Season {season:00}", @"C:\Test\30 Rock\30 Rock - S01E05 - Episode Title.mkv")]
        [TestCase("30 Rock - S01E05 - Episode Title", 1, false, "Season {season}", @"C:\Test\30 Rock\30 Rock - S01E05 - Episode Title.mkv")]
        [TestCase("30 Rock - S01E05 - Episode Title", 1, true, "ReallyUglySeasonFolder {season}", @"C:\Test\30 Rock\ReallyUglySeasonFolder 1\30 Rock - S01E05 - Episode Title.mkv")]
        [TestCase("30 Rock - S00E05 - Episode Title", 0, true, "Season {season}", @"C:\Test\30 Rock\MySpecials\30 Rock - S00E05 - Episode Title.mkv")]
        public void CalculateFilePath_SeasonFolder_SingleNumber(string filename, int seasonNumber, bool useSeasonFolder, string seasonFolderFormat, string expectedPath)
        {
            var fakeEpisodes = Builder<Episode>.CreateListOfSize(1)
                .All()
                .With(s => s.Title = "Episode Title")
                .With(s => s.SeasonNumber = seasonNumber)
                .With(s => s.EpisodeNumber = 5)
                .Build().ToList();
            var fakeSeries = Builder<Series>.CreateNew()
                .With(s => s.Title = "30 Rock")
                .With(s => s.Path = @"C:\Test\30 Rock".AsOsAgnostic())
                .With(s => s.SeasonFolder = useSeasonFolder)
                .With(s => s.SeriesType = SeriesTypes.Standard)
                .Build();
            var fakeEpisodeFile = Builder<EpisodeFile>.CreateNew()
                .With(s => s.SceneName = filename)
                .Build();

            _namingConfig.SeasonFolderFormat = seasonFolderFormat;
            _namingConfig.SpecialsFolderFormat = "MySpecials";

            Subject.BuildFilePath(fakeEpisodes, fakeSeries, fakeEpisodeFile, ".mkv").Should().Be(expectedPath.AsOsAgnostic());
        }

        [Test]
        public void should_clean_season_folder_when_it_contains_illegal_characters_in_series_title()
        {
            var filename = @"S01E05 - Episode Title";
            var seasonNumber = 1;
            var expectedPath = @"C:\Test\NCIS - Los Angeles\NCIS - Los Angeles Season 1\S01E05 - Episode Title.mkv";

            var fakeEpisodes = Builder<Episode>.CreateListOfSize(1)
                .All()
                .With(s => s.Title = "Episode Title")
                .With(s => s.SeasonNumber = seasonNumber)
                .With(s => s.EpisodeNumber = 5)
                .Build().ToList();
            var fakeSeries = Builder<Series>.CreateNew()
                .With(s => s.Title = "NCIS: Los Angeles")
                .With(s => s.Path = @"C:\Test\NCIS - Los Angeles".AsOsAgnostic())
                .With(s => s.SeasonFolder = true)
                .With(s => s.SeriesType = SeriesTypes.Standard)
                .Build();
            var fakeEpisodeFile = Builder<EpisodeFile>.CreateNew()
                .With(s => s.SceneName = filename)
                .Build();

            _namingConfig.SeasonFolderFormat = "{Series Title} Season {season:0}";

            Subject.BuildFilePath(fakeEpisodes, fakeSeries, fakeEpisodeFile, ".mkv").Should().Be(expectedPath.AsOsAgnostic());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_keep_native_S01_numbering_in_the_group_Season_02_leaf_without_nesting(bool seasonFolder)
        {
            var path = Path.Combine(Path.GetTempPath(), "Canonical Anime", "Season 02");
            var series = new Series
            {
                Title = "Native Sequel",
                Path = path,
                SeriesType = SeriesTypes.Anime,
                SeasonFolder = seasonFolder,
                AnimeGroupingMode = AnimeGroupingMode.Automatic,
                AnimeGroupId = "anilist:100",
                AnimeGroupTitle = "Canonical Anime",
                AnimeGroupFolder = "Canonical Anime",
                AnimeGroupSeasonNumber = 2
            };
            var episodes = new List<Episode>
            {
                new Episode { Id = 101, SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 1, Title = "Native Episode" }
            };
            var file = new EpisodeFile { Quality = new QualityModel(Quality.HDTV720p) };
            _namingConfig.RenameEpisodes = true;
            _namingConfig.AnimeEpisodeFormat = "{Series Title} - S{season:00}E{episode:00}";
            Mocker.GetMock<IQualityDefinitionService>()
                .Setup(s => s.Get(Moq.It.IsAny<Quality>()))
                .Returns(Quality.DefaultQualityDefinitions.First(q => q.Quality == Quality.HDTV720p));

            Subject.BuildFilePath(episodes, series, file, ".mkv")
                .Should().Be(Path.Combine(path, "Native Sequel - S01E01.mkv"));
            Subject.BuildSeasonPath(series, 1).Should().Be(path);
            episodes[0].Id.Should().Be(101);
            episodes[0].SeasonNumber.Should().Be(1);
            episodes[0].EpisodeNumber.Should().Be(1);
            episodes[0].AbsoluteEpisodeNumber.Should().Be(1);
        }

        [TestCase("Feature film")]
        [TestCase("Film 02")]
        public void should_keep_native_movie_files_in_the_stored_movie_leaf(string leaf)
        {
            var path = Path.Combine(Path.GetTempPath(), "Canonical Anime", leaf);
            var series = new Series
            {
                Title = "Native Film",
                Path = path,
                SeriesType = SeriesTypes.Anime,
                SeasonFolder = true,
                AnimeGroupingMode = AnimeGroupingMode.Automatic,
                AnimeGroupId = "anilist:100",
                AnimeGroupTitle = "Canonical Anime",
                AnimeGroupFolder = "Canonical Anime",
                AnimeGroupMemberKind = AnimeGroupMemberKind.Movie,
                AnimeGroupMovieNumber = 2,
                AnimeGroupMemberFolder = leaf
            };
            var episodes = new List<Episode>
            {
                new Episode { Id = 201, SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 1, Title = "Native Film" }
            };
            var file = new EpisodeFile { Quality = new QualityModel(Quality.HDTV720p) };
            _namingConfig.RenameEpisodes = true;
            _namingConfig.AnimeEpisodeFormat = "{Group Title} - Film {Group Movie:00} - S{season:00}E{episode:00}";
            Mocker.GetMock<IQualityDefinitionService>().Setup(s => s.Get(Moq.It.IsAny<Quality>()))
                .Returns(Quality.DefaultQualityDefinitions.First(q => q.Quality == Quality.HDTV720p));

            Subject.BuildFilePath(episodes, series, file, ".mkv")
                .Should().Be(Path.Combine(path, "Canonical Anime - Film 02 - S01E01.mkv"));
            episodes[0].Id.Should().Be(201);
            episodes[0].SeasonNumber.Should().Be(1);
        }

        [Test]
        public void should_keep_an_explicit_ungrouped_path_using_its_native_season_folder()
        {
            var path = Path.Combine(Path.GetTempPath(), "Existing folder");
            var series = new Series
            {
                Path = path,
                SeasonFolder = true,
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = "manual:100",
                AnimeGroupTitle = "Canonical Anime",
                AnimeGroupFolder = "Canonical Anime",
                AnimeGroupSeasonNumber = 2
            };
            _namingConfig.SeasonFolderFormat = "Season {season:00}";

            Subject.BuildSeasonPath(series, 1).Should().Be(Path.Combine(path, "Season 01"));
            series.Path.Should().Be(path);
        }
    }
}
