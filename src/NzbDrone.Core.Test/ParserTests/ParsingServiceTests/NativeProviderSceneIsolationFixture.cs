using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    [TestFixture]
    public class NativeProviderSceneIsolationFixture : CoreTest<ParsingService>
    {
        private Series _series;
        private Episode _episode;

        [SetUp]
        public void Setup()
        {
            _series = new Series
            {
                Id = 42,
                MetadataSource = MetadataSourceType.AniList,
                ForeignId = "20934",
                TvdbId = -20934,
                Title = "Native Anime Run",
                AlternateTitles = new List<string> { "Shokugeki no Soma S2" },
                SeriesType = SeriesTypes.Anime,
                SeasonType = SeasonType.Official,
                UseSceneNumbering = false
            };
            _episode = new Episode
            {
                Id = 4201,
                SeriesId = 42,
                SeasonNumber = 1,
                EpisodeNumber = 1,
                AbsoluteEpisodeNumber = 1,
                Title = "Native Episode"
            };
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.FindSceneMapping(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new SceneMapping
                {
                    TvdbId = 7999,
                    SceneOrigin = "scene",
                    SeasonNumber = 2,
                    SceneSeasonNumber = 1
                });
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.GetSceneSeasonNumber(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(2);
            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.FindEpisode(42, 1, 1))
                .Returns(_episode);
            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.FindEpisode(42, 1))
                .Returns(_episode);
            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.GetEpisodeBySeries(42))
                .Returns(new List<Episode> { _episode });
            Mocker.SetConstant<IEpisodeNumberingResolver>(Mocker.Resolve<EpisodeNumberingResolver>());
        }

        [TestCase(MetadataSourceType.AniList, false)]
        [TestCase(MetadataSourceType.AniList, true)]
        [TestCase(MetadataSourceType.MyAnimeList, false)]
        [TestCase(MetadataSourceType.MyAnimeList, true)]
        [TestCase(MetadataSourceType.Tmdb, false)]
        [TestCase(MetadataSourceType.Tmdb, true)]
        public void known_native_file_should_keep_its_library_season_despite_foreign_scene_offset(MetadataSourceType source, bool useMap)
        {
            _series.MetadataSource = source;
            var parsed = Parser.Parser.ParseTitle("Native.Anime.Run.S01E01.1080p.WEB.mkv");

            var episodes = useMap ? Subject.Map(parsed, _series).Episodes : Subject.GetEpisodes(parsed, _series, true);

            episodes.Should().ContainSingle().Which.Should().BeSameAs(_episode);
            _series.ForeignId.Should().Be("20934");
            Mocker.GetMock<IEpisodeService>()
                .Verify(s => s.FindEpisode(42, 2, It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IEpisodeService>()
                .Verify(s => s.FindEpisodesBySceneNumbering(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never());
        }

        [TestCase("Shokugeki no Soma S2 S01E01.1080p.WEB.mkv")]
        [TestCase("Shokugeki no Soma S2 - 01 [Group][1080p].mkv")]
        [TestCase("[Group] Native Anime Run - 01 [1080p].mkv")]
        public void known_native_alias_or_absolute_file_should_keep_the_exact_native_episode(string title)
        {
            var remote = Subject.Map(Parser.Parser.ParseTitle(title), _series);

            remote.Series.Should().BeSameAs(_series);
            remote.SceneMapping.Should().BeNull();
            remote.NumberingRejection.Should().BeNull();
            remote.Episodes.Select(e => e.Id).Should().Equal(4201);
            remote.Episodes[0].SeasonNumber.Should().Be(1);
            Mocker.GetMock<IEpisodeService>()
                .Verify(s => s.FindEpisodesBySceneNumbering(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never());
        }
    }
}
