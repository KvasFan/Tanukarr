using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class AmericanDadNumberingReplayFixture : TestBase<ParsingService>
    {
        private Series _series;
        private List<Episode> _episodes;

        private sealed class Snapshot
        {
            public List<Episode> Episodes { get; set; }
            public List<SceneMapping> Mappings { get; set; }
        }

        [SetUp]
        public void Setup()
        {
            // Read-only production metadata snapshot; no API keys, paths, download URLs or network calls.
            var snapshot = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Numbering", "american-dad.json")));
            _episodes = snapshot.Episodes;
            _series = new Series { Id = 283, TvdbId = 73141, Title = "American Dad!", CleanTitle = "americandad", UseSceneNumbering = true, SeasonType = SeasonType.Official };
            Mocker.SetConstant<ICacheManager>(new CacheManager());
            Mocker.GetMock<ISceneMappingRepository>().Setup(r => r.All()).Returns(snapshot.Mappings);
            Mocker.SetConstant<ISceneMappingService>(Mocker.Resolve<SceneMappingService>());
            Mocker.SetConstant<IEpisodeNumberingResolver>(Mocker.Resolve<EpisodeNumberingResolver>());
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(283)).Returns(_episodes);
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodesBySeason(283, It.IsAny<int>()))
                .Returns((int id, int season) => _episodes.Where(e => e.SeasonNumber == season).ToList());
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodesBySceneSeason(283, It.IsAny<int>()))
                .Returns((int id, int season) => _episodes.Where(e => e.SceneSeasonNumber == season).ToList());
            Mocker.GetMock<IEpisodeService>().Setup(s => s.FindEpisode(283, It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int id, int season, int number) => _episodes.SingleOrDefault(e => e.SeasonNumber == season && e.EpisodeNumber == number));
            Mocker.GetMock<IEpisodeService>().Setup(s => s.FindEpisodesBySceneNumbering(283, It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int id, int season, int number) => _episodes.Where(e => e.SceneSeasonNumber == season && e.SceneEpisodeNumber == number).ToList());
        }

        [TestCase("American Dad S06 WEBRip EAC3 5 1 1080p x265-SiQ", 6, 18, false)]
        [TestCase("American Dad S06 1080p DSNP WEBRip DD 5 1 x265-EDGE2020", 6, 18, false)]
        [TestCase("American Dad S05 DVDRip XviD-REWARD", 6, 18, false)]
        [TestCase("American Dad (2005) S05 (1080p DSNP Webrip x265 10bit EAC3 5 1 - Goki)[TAoE]", 6, 18, true)]
        [TestCase("American Dad (2005) S06 (1080p DSNP Webrip x265 10bit EAC3 5 1 - Goki)[TAoE]", 7, 19, true)]
        public void replay_actual_search_titles(string title, int season, int count, bool rejected)
        {
            var remote = Subject.Map(Parser.Parser.ParseTitle(title), _series);
            remote.Episodes.Should().HaveCount(count).And.OnlyContain(e => e.SeasonNumber == season);
            Mocker.Resolve<EpisodeNumberingSpecification>().IsSatisfiedBy(remote, new ReleaseDecisionInformation())
                .Accepted.Should().Be(!rejected);
        }

        [TestCase("American Dad (2005) S05E01 1600 Candles (1080p DSNP Webrip x265 10bit EAC3 5.1 - Goki)[TAoE].mkv", 5, "1600 Candles")]
        [TestCase("American Dad (2005) S04E02 Meter Made (1080p DSNP Webrip AV1 10bit EAC3 5.1 - Goki)[TAoE].mkv", 4, "Meter Made")]
        [TestCase("American.Dad.S05E01.1600.Candles.1080p.WEBRip.x265-Goki", 5, "1600 Candles")]
        [TestCase("American.Dad.S04E02.Meter.Made.1080p.WEBRip.x265-Goki", 4, "Meter Made")]
        [TestCase("American.Dad.S05E01.In.Country.Club.1080p.WEBRip.x265-Unknown", 6, "In Country... Club")]
        public void replay_title_identity_in_search_and_import(string title, int season, string episodeTitle)
        {
            var parsed = Parser.Parser.ParseTitle(title);
            var remote = Subject.Map(parsed, _series);
            remote.NumberingRejection.Should().BeNull();
            remote.Episodes.Should().ContainSingle().Which.Title.Should().Be(episodeTitle);
            remote.Episodes[0].SeasonNumber.Should().Be(season);
            Mocker.SetConstant<IParsingService>(Subject);
            var local = new LocalEpisode
            {
                Series = _series,
                Path = "/downloads/" + title + ".mkv",
                FileEpisodeInfo = parsed,
                SceneSource = true,
                OtherVideoFiles = true
            };
            Mocker.Resolve<AggregateEpisodes>().Aggregate(local, null);
            local.NumberingRejection.Should().BeNull();
            local.Episodes.Select(e => e.Id).Should().Equal(remote.Episodes.Select(e => e.Id));
        }

        [Test]
        public void title_corrected_to_another_season_should_not_satisfy_requested_episodes()
        {
            var criteria = new SeasonSearchCriteria
            {
                Series = _series,
                SeasonNumber = 6,
                Episodes = _episodes.Where(e => e.SeasonNumber == 6).ToList()
            };
            var remote = Subject.Map(Parser.Parser.ParseTitle("American.Dad.S05E01.1600.Candles.1080p.WEBRip.x265-Goki"), 73141, 0, null, criteria);
            remote.EpisodeRequested.Should().BeFalse();
            remote.Episodes[0].SeasonNumber.Should().Be(5);
        }

        [Test]
        public void library_rescan_should_keep_corrected_aired_filenames()
        {
            var episodes = Subject.GetEpisodes(Parser.Parser.ParseTitle("American.Dad.S05E01.1600.Candles.1080p.mkv"), _series, false);
            episodes.Should().ContainSingle().Which.Title.Should().Be("1600 Candles");
        }

        [Test]
        public void replay_every_saved_season_six_search_result_without_network_or_downloads()
        {
            var titles = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Numbering", "american-dad-search-titles.json")));
            var approved = new List<string>();
            var blocked = 0;
            var criteria = new SeasonSearchCriteria
            {
                Series = _series,
                SeasonNumber = 6,
                InteractiveSearch = true,
                Episodes = _episodes.Where(e => e.SeasonNumber == 6).ToList()
            };
            foreach (var title in titles)
            {
                var remote = Subject.Map(Parser.Parser.ParseTitle(title), 73141, 0, null, criteria);
                var decision = Mocker.Resolve<EpisodeNumberingSpecification>().IsSatisfiedBy(remote, new ReleaseDecisionInformation(false, criteria));
                if (!decision.Accepted)
                {
                    blocked++;
                }
                else if (remote.EpisodeRequested)
                {
                    remote.Episodes.Should().OnlyContain(e => e.SeasonNumber == 6);
                    approved.Add(title);
                }
            }

            titles.Should().HaveCount(101);
            blocked.Should().BeGreaterThan(0);
            approved.Should().Contain("American Dad S06 WEBRip EAC3 5 1 1080p x265-SiQ");
            approved.Should().NotContain(t => t.Contains("Goki"));
            TestContext.Out.WriteLine($"Numbering-only replay: {titles.Count} results, {blocked} ambiguous rejections, {approved.Count} season-six matches. Other download specifications still apply.");
        }

        [TestCase("American Dad (2005) S05E01 1600 Candles (1080p DSNP Webrip x265 10bit EAC3 5.1 - Goki)[TAoE].mkv", 6)]
        [TestCase("American Dad (2005) S04E02 Meter Made (1080p DSNP Webrip AV1 10bit EAC3 5.1 - Goki)[TAoE].mkv", 5)]
        public void historical_original_filenames_reproduce_wrong_mapping_without_the_resolver(string title, int wrongSeason)
        {
            var original = new ParsingService(
                Mocker.GetMock<IEpisodeService>().Object,
                Mocker.GetMock<ISeriesService>().Object,
                Mocker.Resolve<ISceneMappingService>(),
                NLog.LogManager.GetLogger("NumberingBaseline"),
                Mock.Of<IEpisodeNumberingResolver>());
            original.Map(Parser.Parser.ParseTitle(title), _series).Episodes.Should().ContainSingle()
                .Which.SeasonNumber.Should().Be(wrongSeason);
            Subject.Map(Parser.Parser.ParseTitle(title), _series).Episodes.Should().ContainSingle()
                .Which.SeasonNumber.Should().Be(wrongSeason - 1);
        }
    }
}
