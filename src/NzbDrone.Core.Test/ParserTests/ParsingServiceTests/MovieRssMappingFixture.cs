using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    [TestFixture]
    public class MovieRssMappingFixture : TestBase<ParsingService>
    {
        private Series _movie;
        private Episode _feature;

        [SetUp]
        public void Setup()
        {
            _movie = new Series
            {
                Id = 10, Title = "Generated Feature One", Year = 2019, MediaType = MediaType.Movie,
                MetadataSource = MetadataSourceType.TmdbMovie, ForeignId = "970001", TvdbId = -10
            };
            _feature = new Episode { Id = 20, SeriesId = 10, SeasonNumber = 1, EpisodeNumber = 1 };
            Mocker.GetMock<ISeriesService>().Setup(service => service.FindByTitle(It.IsAny<string>())).Returns(_movie);
            Mocker.GetMock<ISeriesService>().Setup(service => service.GetAllSeries()).Returns(new List<Series> { _movie });
            Mocker.GetMock<IEpisodeService>().Setup(service => service.FindEpisode(10, 1, 1)).Returns(_feature);
            Mocker.GetMock<IEpisodeService>().Setup(service => service.GetEpisodeBySeries(10)).Returns(new List<Episode> { _feature });
        }

        [Test]
        public void should_use_the_verified_movie_title_year_when_the_legacy_rss_parser_reads_a_year_as_an_episode()
        {
            const string title = "Generated.Feature.One.2019.720p.WEB-DL.H264-Fixture";
            var parsed = Parser.Parser.ParseTitle(title) ?? MovieReleaseParser.Parse(title);
            var result = Subject.Map(parsed, 0, 0, null);
            result.Series.Should().BeSameAs(_movie);
            result.Episodes.Should().Equal(_feature);
            result.ParsedEpisodeInfo.IsMovie.Should().BeTrue();
            result.ParsedEpisodeInfo.MovieYear.Should().Be(2019);
        }

        [Test]
        public void should_not_let_legacy_rss_episode_coordinates_bypass_ordinary_movie_identity()
        {
            var parsed = Parser.Parser.ParseTitle("Generated.Feature.One.S01E01.720p.WEB-DL.H264-Fixture");
            var result = Subject.Map(parsed, 0, 0, null);
            result.Episodes.Should().BeEmpty();
        }
    }
}
