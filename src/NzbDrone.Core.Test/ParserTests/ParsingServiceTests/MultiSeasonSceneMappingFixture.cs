using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    [TestFixture]
    public class MultiSeasonSceneMappingFixture : TestBase<ParsingService>
    {
        private Series _series;
        private ParsedEpisodeInfo _parsed;
        private List<Episode> _episodes;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 55, Title = "Food Wars!", SeriesType = SeriesTypes.Anime, UseSceneNumbering = true, SeasonType = SeasonType.Official };
            _parsed = Parser.Parser.ParseTitle("Shokugeki no Soma S01-S03 + OVA (Food Wars) [1080p]");
            _episodes = new[] { 24, 13, 24 }.SelectMany((count, index) => Enumerable.Range(1, count)
                .Select(number => new Episode
                {
                    Id = ((index + 1) * 100) + number,
                    SeriesId = _series.Id,
                    SeasonNumber = index + 1,
                    EpisodeNumber = number,
                    SceneSeasonNumber = index + 1,
                    SceneEpisodeNumber = number
                })).ToList();

            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.GetEpisodesBySceneSeason(_series.Id, It.IsAny<int>()))
                .Returns((int id, int season) => _episodes.Where(e => e.SceneSeasonNumber == season).ToList());
            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.GetEpisodesBySeason(_series.Id, It.IsAny<int>()))
                .Returns((int id, int season) => _episodes.Where(e => e.SeasonNumber == season).ToList());
        }

        [Test]
        public void should_resolve_all_61_episodes_when_first_scene_season_matches()
        {
            var result = Subject.Map(_parsed, _series);

            result.Episodes.Select(e => e.Id).Should().BeEquivalentTo(_episodes.Select(e => e.Id));
            result.Episodes.Should().HaveCount(61);
            _parsed.SeasonNumbers.Should().Equal(1, 2, 3);
        }

        [Test]
        public void should_fallback_to_aired_numbering_for_each_missing_scene_season()
        {
            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.GetEpisodesBySceneSeason(_series.Id, 2))
                .Returns(new List<Episode>());

            Subject.Map(_parsed, _series).Episodes.Should().HaveCount(61);
            Mocker.GetMock<IEpisodeService>().Verify(s => s.GetEpisodesBySeason(_series.Id, 2), Times.Once());
            Mocker.GetMock<IEpisodeService>().Verify(s => s.GetEpisodesBySeason(_series.Id, 1), Times.Never());
        }

        [Test]
        public void should_apply_each_seasons_alias_mapping()
        {
            _parsed = Parser.Parser.ParseTitle("Series S01-S02 1080p");
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.FindSceneMapping(_parsed.SeriesTitle, _parsed.ReleaseTitle, 1))
                .Returns(new SceneMapping { SeasonNumber = 2, SceneSeasonNumber = 1 });
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.FindSceneMapping(_parsed.SeriesTitle, _parsed.ReleaseTitle, 2))
                .Returns(new SceneMapping { SeasonNumber = 3, SceneSeasonNumber = 2 });

            Subject.Map(_parsed, _series).Episodes.Select(e => e.Id)
                .Should().BeEquivalentTo(_episodes.Where(e => e.SeasonNumber >= 2).Select(e => e.Id));
        }

        [Test]
        public void should_map_trailing_group_anime_episode_to_its_season_alias()
        {
            var parsed = Parser.Parser.ParseTitle("Shokugeki no Soma S2 - 10 [HorribleSubs][1080p].mkv");
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.GetSceneSeasonNumber("Shokugeki no Soma S2", parsed.ReleaseTitle))
                .Returns(2);
            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.FindEpisodesBySceneNumbering(_series.Id, 2, 10))
                .Returns(_episodes.Where(e => e.SeasonNumber == 2 && e.EpisodeNumber == 10).ToList());

            Subject.Map(parsed, _series).Episodes.Select(e => e.Id).Should().Equal(210);
        }

        [Test]
        public void should_preserve_xem_season_one_alias_override()
        {
            _parsed = Parser.Parser.ParseTitle("Series S01-S02 1080p");
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.FindSceneMapping(_parsed.SeriesTitle, _parsed.ReleaseTitle, 1))
                .Returns(new SceneMapping { Type = "XemService", SceneSeasonNumber = 3 });

            Subject.Map(_parsed, _series).Episodes.Select(e => e.Id)
                .Should().BeEquivalentTo(_episodes.Where(e => e.SeasonNumber >= 2).Select(e => e.Id));
        }

        [Test]
        public void should_preserve_aired_library_scans()
        {
            Subject.GetEpisodes(_parsed, _series, false).Select(e => e.Id)
                .Should().BeEquivalentTo(_episodes.Select(e => e.Id));
            Mocker.GetMock<IEpisodeService>()
                .Verify(s => s.GetEpisodesBySceneSeason(It.IsAny<int>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_honor_tvdb_origin_for_an_individual_season_mapping()
        {
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.FindSceneMapping(_parsed.SeriesTitle, _parsed.ReleaseTitle, 2))
                .Returns(new SceneMapping { SeasonNumber = 2, SceneSeasonNumber = 2, SceneOrigin = "tvdb" });

            Subject.Map(_parsed, _series).Episodes.Should().HaveCount(61);
            Mocker.GetMock<IEpisodeService>().Verify(s => s.GetEpisodesBySceneSeason(_series.Id, 2), Times.Never());
            Mocker.GetMock<IEpisodeService>().Verify(s => s.GetEpisodesBySeason(_series.Id, 2), Times.Once());
        }

        [Test]
        public void should_keep_numbering_ambiguity_rejection()
        {
            Mocker.GetMock<IEpisodeNumberingResolver>()
                .Setup(s => s.Resolve(_series, _parsed, It.IsAny<SceneMapping>(), null))
                .Returns(new EpisodeNumberingResolution { Rejection = "Ambiguous season numbering" });

            Subject.Map(_parsed, _series).NumberingRejection.Should().Be("Ambiguous season numbering");
        }

        [Test]
        public void should_deduplicate_overlapping_scene_seasons()
        {
            Mocker.GetMock<IEpisodeService>()
                .Setup(s => s.GetEpisodesBySceneSeason(_series.Id, It.IsAny<int>()))
                .Returns(_episodes);

            Subject.Map(_parsed, _series).Episodes.Should().HaveCount(61);
        }
    }
}
