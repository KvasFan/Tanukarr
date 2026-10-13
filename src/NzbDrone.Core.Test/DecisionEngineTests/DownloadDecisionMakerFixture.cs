using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class DownloadDecisionMakerFixture : CoreTest<DownloadDecisionMaker>
    {
        private List<ReleaseInfo> _reports;
        private RemoteEpisode _remoteEpisode;

        private Mock<IDownloadDecisionEngineSpecification> _pass1;
        private Mock<IDownloadDecisionEngineSpecification> _pass2;
        private Mock<IDownloadDecisionEngineSpecification> _pass3;

        private Mock<IDownloadDecisionEngineSpecification> _fail1;
        private Mock<IDownloadDecisionEngineSpecification> _fail2;
        private Mock<IDownloadDecisionEngineSpecification> _fail3;

        private Mock<IDownloadDecisionEngineSpecification> _failDelayed1;

        [SetUp]
        public void Setup()
        {
            _pass1 = new Mock<IDownloadDecisionEngineSpecification>();
            _pass2 = new Mock<IDownloadDecisionEngineSpecification>();
            _pass3 = new Mock<IDownloadDecisionEngineSpecification>();

            _fail1 = new Mock<IDownloadDecisionEngineSpecification>();
            _fail2 = new Mock<IDownloadDecisionEngineSpecification>();
            _fail3 = new Mock<IDownloadDecisionEngineSpecification>();

            _failDelayed1 = new Mock<IDownloadDecisionEngineSpecification>();

            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>())).Returns(DownloadSpecDecision.Accept);
            _pass2.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>())).Returns(DownloadSpecDecision.Accept);
            _pass3.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>())).Returns(DownloadSpecDecision.Accept);

            _fail1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>())).Returns(DownloadSpecDecision.Reject(DownloadRejectionReason.Unknown, "fail1"));
            _fail2.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>())).Returns(DownloadSpecDecision.Reject(DownloadRejectionReason.Unknown, "fail2"));
            _fail3.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>())).Returns(DownloadSpecDecision.Reject(DownloadRejectionReason.Unknown, "fail3"));

            _failDelayed1.Setup(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>())).Returns(DownloadSpecDecision.Reject(DownloadRejectionReason.MinimumAgeDelay, "failDelayed1"));
            _failDelayed1.SetupGet(c => c.Priority).Returns(SpecificationPriority.Disk);

            _reports = new List<ReleaseInfo> { new ReleaseInfo { Title = "The.Office.S03E115.DVDRip.XviD-OSiTV" } };
            _remoteEpisode = new RemoteEpisode
            {
                Series = new Series(),
                Episodes = new List<Episode> { new Episode() }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(c => c.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(_remoteEpisode);
        }

        private void GivenSpecifications(params Mock<IDownloadDecisionEngineSpecification>[] mocks)
        {
            Mocker.SetConstant<IEnumerable<IDownloadDecisionEngineSpecification>>(mocks.Select(c => c.Object));
        }

        [TestCase("Berserk 1997 S01 1080p BluRay Dual-Audio Opus 2 0 x265-Kitsune")]
        [TestCase("Kenpuu Denki Berserk COMPLETE 1080p")]
        public void complete_series_should_preserve_validated_identity_and_all_episodes(string title)
        {
            GivenSpecifications(_pass1);
            var criteria = BerserkCriteria();
            GivenRealCompleteSeriesMapping(criteria);
            _reports[0].Title = title;
            var decision = Subject.GetSearchDecision(_reports, criteria).Single();

            decision.Approved.Should().BeTrue();
            decision.RemoteEpisode.Series.Should().BeSameAs(criteria.Series);
            decision.RemoteEpisode.Episodes.Should().Equal(criteria.Episodes);
            decision.RemoteEpisode.EpisodeRequested.Should().BeTrue();
            _pass1.Verify(s => s.IsSatisfiedBy(decision.RemoteEpisode, It.IsAny<ReleaseDecisionInformation>()), Times.Once());
            decision.RemoteEpisode.SeriesMatchType.Should().NotBe(SeriesMatchType.Unknown);
        }

        [TestCase(false, "Berserk 1997 COMPLETE 1080p")]
        [TestCase(true, "Berserk 1997 COMPLETE 1080p")]
        [TestCase(false, "Kenpuu Denki Berserk COMPLETE 1080p")]
        [TestCase(true, "Kenpuu Denki Berserk COMPLETE 1080p")]
        public void native_complete_series_should_use_its_verified_titles_without_foreign_scene_identity(bool foreignSeriesPresent, string title)
        {
            var criteria = NativeBerserkCriteria();
            GivenRealCompleteSeriesMapping(criteria);
            criteria.Series.AlternateTitles = new List<string> { "Kenpuu Denki Berserk" };
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.FindSceneMapping(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new SceneMapping { TvdbId = 307111, SceneOrigin = "mixed", SeasonNumber = 99, SceneSeasonNumber = 1 });
            if (!foreignSeriesPresent)
            {
                Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTvdbId(307111)).Returns((Series)null);
            }

            Mocker.SetConstant<IEnumerable<IDownloadDecisionEngineSpecification>>(new IDownloadDecisionEngineSpecification[]
            {
                _pass1.Object,
                Mocker.Resolve<NzbDrone.Core.DecisionEngine.Specifications.Search.SeriesSpecification>(),
                Mocker.Resolve<SceneMappingSpecification>()
            });
            _reports[0].Title = title;

            var decision = Subject.GetSearchDecision(_reports, criteria).Single();

            decision.Approved.Should().BeTrue();
            decision.RemoteEpisode.Series.Should().BeSameAs(criteria.Series);
            decision.RemoteEpisode.Episodes.Should().Equal(criteria.Episodes);
            decision.RemoteEpisode.SceneMapping.Should().BeNull();
            decision.RemoteEpisode.MappedSeasonNumber.Should().Be(1);
        }

        [TestCase("Other Show COMPLETE 1080p")]
        [TestCase("Berserk 2016 COMPLETE 1080p")]
        public void native_complete_series_should_not_force_wrong_titles_or_years(string title)
        {
            _reports[0].Title = title;

            Subject.GetSearchDecision(_reports, NativeBerserkCriteria()).Should().BeEmpty();
        }

        [TestCase(307111, 0, null)]
        [TestCase(0, 99, null)]
        [TestCase(0, 0, "tt5847454")]
        public void native_complete_series_should_not_override_conflicting_release_identity(int tvdbId, int tvRageId, string imdbId)
        {
            var criteria = NativeBerserkCriteria();
            GivenRealCompleteSeriesMapping(criteria);
            Mocker.SetConstant<IEnumerable<IDownloadDecisionEngineSpecification>>(new[]
            {
                Mocker.Resolve<NzbDrone.Core.DecisionEngine.Specifications.Search.SeriesSpecification>()
            });
            _reports[0] = new ReleaseInfo { Title = "Berserk 1997 COMPLETE 1080p", TvdbId = tvdbId, TvRageId = tvRageId, ImdbId = imdbId };

            Subject.GetSearchDecision(_reports, criteria).Single().Approved.Should().BeFalse();
        }

        private static CompleteSeriesSearchCriteria NativeBerserkCriteria()
        {
            var criteria = BerserkCriteria();
            criteria.Series.MetadataSource = MetadataSourceType.AniList;
            criteria.Series.TvdbId = -1234;
            criteria.Series.TvRageId = 0;
            criteria.Series.ImdbId = null;
            return criteria;
        }

        [TestCase(MetadataSourceType.AniList, "Berserk 1997 S02 1080p", new[] { 2 })]
        [TestCase(MetadataSourceType.MyAnimeList, "Kenpuu Denki Berserk S02-S03 1080p", new[] { 2, 3 })]
        [TestCase(MetadataSourceType.Tmdb, "Berserk 1997 S01 S03 1080p", new[] { 1, 3 })]
        public void interactive_native_season_subset_should_keep_owner_identity_and_only_advertised_episodes(MetadataSourceType source, string title, int[] seasons)
        {
            var criteria = NativeBerserkCriteria();
            criteria.IncludeSeasonPacks = true;
            criteria.Series.MetadataSource = source;
            criteria.Series.AlternateTitles = new List<string> { "Kenpuu Denki Berserk" };
            criteria.Episodes = Enumerable.Range(1, 3).SelectMany(season => Enumerable.Range(1, 2).Select(episode => new Episode
            {
                Id = (season * 10) + episode, SeriesId = criteria.Series.Id, SeasonNumber = season, EpisodeNumber = episode
            })).ToList();
            GivenRealCompleteSeriesMapping(criteria);
            GivenSpecifications(_pass1);
            _reports[0].Title = title;

            var decision = Subject.GetSearchDecision(_reports, criteria).Single();
            decision.Approved.Should().BeTrue();
            decision.RemoteEpisode.Series.Should().BeSameAs(criteria.Series);
            decision.RemoteEpisode.SceneMapping.Should().BeNull();
            decision.RemoteEpisode.ParsedEpisodeInfo.IsCompleteSeries.Should().BeFalse();
            decision.RemoteEpisode.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(seasons);
            decision.RemoteEpisode.Episodes.Should().Equal(criteria.Episodes.Where(episode => seasons.Contains(episode.SeasonNumber)));
        }

        [TestCase(307111, 0, null)]
        [TestCase(0, 99, null)]
        [TestCase(0, 0, "tt5847454")]
        public void interactive_native_season_subset_must_not_override_conflicting_release_identity(int tvdbId, int tvRageId, string imdbId)
        {
            var criteria = NativeBerserkCriteria();
            criteria.IncludeSeasonPacks = true;
            criteria.Episodes.Add(new Episode { Id = 300, SeriesId = criteria.Series.Id, SeasonNumber = 2, EpisodeNumber = 1 });
            GivenRealCompleteSeriesMapping(criteria);
            GivenSpecifications(_pass1);
            _reports[0] = new ReleaseInfo { Title = "Berserk 1997 S01 1080p", TvdbId = tvdbId, TvRageId = tvRageId, ImdbId = imdbId };

            var decision = Subject.GetSearchDecision(_reports, criteria).Single();
            decision.Approved.Should().BeFalse();
            decision.Rejections.Single().Reason.Should().Be(DownloadRejectionReason.WrongSeries);
        }

        [Test]
        public void complete_series_should_preserve_multi_season_coverage()
        {
            GivenSpecifications(_pass1);
            var criteria = BerserkCriteria();
            criteria.Series.Title = "Berserk (2016)";
            criteria.Series.Year = 2016;
            criteria.Series.TvdbId = 307111;
            criteria.Episodes = Enumerable.Range(1, 24).Select(n => new Episode { Id = n, SeasonNumber = n <= 12 ? 1 : 2, EpisodeNumber = ((n - 1) % 12) + 1 }).ToList();
            GivenRealCompleteSeriesMapping(criteria);
            _reports[0].Title = "Berserk 2016 S01-S02 1080p";
            var decision = Subject.GetSearchDecision(_reports, criteria).Single();
            decision.Approved.Should().BeTrue();
            decision.RemoteEpisode.Episodes.Should().Equal(criteria.Episodes);
            decision.RemoteEpisode.ParsedEpisodeInfo.SeasonNumbers.Should().Equal(1, 2);
        }

        [TestCase(73752, 0, "tt0118276", true)]
        [TestCase(307111, 0, null, false)]
        [TestCase(73752, 99, null, false)]
        [TestCase(73752, 0, "tt5847454", false)]
        public void complete_series_should_reject_conflicting_release_identifiers(int tvdbId, int tvRageId, string imdbId, bool approved)
        {
            GivenSpecifications(_pass1);
            _reports[0] = new ReleaseInfo { Title = "Berserk 1997 S01 1080p", TvdbId = tvdbId, TvRageId = tvRageId, ImdbId = imdbId };
            var criteria = BerserkCriteria();
            GivenRealCompleteSeriesMapping(criteria);
            var decision = Subject.GetSearchDecision(_reports, criteria).Single();
            decision.Approved.Should().Be(approved);
            if (!approved)
            {
                decision.Rejections.Single().Reason.Should().Be(DownloadRejectionReason.WrongSeries);
                _pass1.Verify(s => s.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
            }
        }

        [TestCase("Berserk 2016 S01 1080p")]
        [TestCase("Berserk The Golden Age Arc S01 1080p")]
        [TestCase("Other Show COMPLETE")]
        public void complete_series_should_exclude_wrong_year_or_title(string title)
        {
            _reports[0].Title = title;
            Subject.GetSearchDecision(_reports, BerserkCriteria()).Should().BeEmpty();
        }

        [Test]
        public void complete_series_should_keep_normal_rejection_checks()
        {
            GivenSpecifications(_fail1);
            _reports[0].Title = "Berserk 1997 S01 1080p";
            var criteria = BerserkCriteria();
            GivenRealCompleteSeriesMapping(criteria);
            Subject.GetSearchDecision(_reports, criteria).Single().Rejections.Single().Message.Should().Be("fail1");
        }

        [Test]
        public void complete_series_should_not_force_ambiguous_yearless_title_to_requested_series()
        {
            var criteria = BerserkCriteria();
            GivenRealCompleteSeriesMapping(criteria);
            Mocker.SetConstant<IEnumerable<IDownloadDecisionEngineSpecification>>(new[]
            {
                Mocker.Resolve<NzbDrone.Core.DecisionEngine.Specifications.Search.SeriesSpecification>()
            });
            _reports[0].Title = "Berserk S01 1080p";
            var decision = Subject.GetSearchDecision(_reports, criteria).Single();
            decision.Approved.Should().BeFalse();
            decision.RemoteEpisode.Series.TvdbId.Should().Be(307111);
            decision.Rejections.Single().Reason.Should().Be(DownloadRejectionReason.WrongSeries);
        }

        [Test]
        public void complete_series_should_preserve_ambiguous_numbering_warning_for_alias()
        {
            var criteria = BerserkCriteria();
            GivenRealCompleteSeriesMapping(criteria);
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.FindSceneMapping("Kenpuu Denki Berserk", It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new SceneMapping { TvdbId = 73752, SceneOrigin = "mixed" });
            Mocker.SetConstant<IEnumerable<IDownloadDecisionEngineSpecification>>(new[]
            {
                Mocker.Resolve<NzbDrone.Core.DecisionEngine.Specifications.SceneMappingSpecification>()
            });
            _reports[0].Title = "Kenpuu Denki Berserk COMPLETE";
            var decision = Subject.GetSearchDecision(_reports, criteria).Single();
            decision.Approved.Should().BeFalse();
            decision.Rejections.Single().Reason.Should().Be(DownloadRejectionReason.AmbiguousNumbering);
        }

        private void GivenRealCompleteSeriesMapping(CompleteSeriesSearchCriteria criteria)
        {
            var namesake = new Series { Id = 747, TvdbId = 307111, Title = "Berserk (2016)", Year = 2016, SeriesType = SeriesTypes.Anime, SeasonType = SeasonType.Official };
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.FindSceneMapping("Berserk", It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new SceneMapping { TvdbId = 307111 });
            Mocker.GetMock<ISceneMappingService>()
                .Setup(s => s.FindSceneMapping("Kenpuu Denki Berserk", It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new SceneMapping { TvdbId = 73752 });
            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTvdbId(307111)).Returns(namesake);
            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTitle(It.IsAny<string>(), criteria.Series.Year)).Returns(criteria.Series);
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodesBySeason(747, 1))
                .Returns(Enumerable.Range(1, 12).Select(n => new Episode { Id = 200 + n, SeriesId = 747, SeasonNumber = 1, EpisodeNumber = n }).ToList());
            Mocker.SetConstant<IParsingService>(Mocker.Resolve<ParsingService>());
        }

        private static CompleteSeriesSearchCriteria BerserkCriteria()
        {
            return new CompleteSeriesSearchCriteria
            {
                Series = new Series { Id = 746, TvdbId = 73752, TvRageId = 42, ImdbId = "tt0118276", Title = "Berserk", Year = 1997, SeriesType = SeriesTypes.Anime, SeasonType = SeasonType.Official },
                SceneTitles = new List<string> { "Berserk", "Kenpuu Denki Berserk" },
                Episodes = Enumerable.Range(1, 25).Select(n => new Episode { Id = 100 + n, SeriesId = 746, SeasonNumber = 1, EpisodeNumber = n, AbsoluteEpisodeNumber = n }).ToList()
            };
        }

        [Test]
        public void should_call_all_specifications()
        {
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetRssDecision(_reports).ToList();

            _fail1.Verify(c => c.IsSatisfiedBy(_remoteEpisode, It.IsAny<ReleaseDecisionInformation>()), Times.Once());
            _fail2.Verify(c => c.IsSatisfiedBy(_remoteEpisode, It.IsAny<ReleaseDecisionInformation>()), Times.Once());
            _fail3.Verify(c => c.IsSatisfiedBy(_remoteEpisode, It.IsAny<ReleaseDecisionInformation>()), Times.Once());
            _pass1.Verify(c => c.IsSatisfiedBy(_remoteEpisode, It.IsAny<ReleaseDecisionInformation>()), Times.Once());
            _pass2.Verify(c => c.IsSatisfiedBy(_remoteEpisode, It.IsAny<ReleaseDecisionInformation>()), Times.Once());
            _pass3.Verify(c => c.IsSatisfiedBy(_remoteEpisode, It.IsAny<ReleaseDecisionInformation>()), Times.Once());
        }

        [Test]
        public void should_call_delayed_specifications_if_non_delayed_passed()
        {
            GivenSpecifications(_pass1, _failDelayed1);

            Subject.GetRssDecision(_reports).ToList();
            _failDelayed1.Verify(c => c.IsSatisfiedBy(_remoteEpisode, It.IsAny<ReleaseDecisionInformation>()), Times.Once());
        }

        [Test]
        public void should_not_call_delayed_specifications_if_non_delayed_failed()
        {
            GivenSpecifications(_fail1, _failDelayed1);

            Subject.GetRssDecision(_reports).ToList();

            _failDelayed1.Verify(c => c.IsSatisfiedBy(_remoteEpisode, It.IsAny<ReleaseDecisionInformation>()), Times.Never());
        }

        [Test]
        public void should_return_rejected_if_single_specs_fail()
        {
            GivenSpecifications(_fail1);

            var result = Subject.GetRssDecision(_reports);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_of_specs_fail()
        {
            GivenSpecifications(_pass1, _fail1, _pass2, _pass3);

            var result = Subject.GetRssDecision(_reports);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_pass_if_all_specs_pass()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetRssDecision(_reports);

            result.Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_have_same_number_of_rejections_as_specs_that_failed()
        {
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            var result = Subject.GetRssDecision(_reports);
            result.Single().Rejections.Should().HaveCount(3);
        }

        [Test]
        public void should_not_attempt_to_map_episode_if_not_parsable()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            // Stripping the metadata leaves brackets rather than a title, so neither parser
            // recovers anything to map.
            _reports[0].Title = "[Not][parsable][1080p][x265(10bit)]";

            Subject.GetRssDecision(_reports).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<SearchCriteriaBase>()), Times.Never());

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
        }

        [Test]
        public void should_map_a_title_the_regexes_cannot_parse()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            _reports[0].Title = "Not parsable";

            Subject.GetRssDecision(_reports).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<SearchCriteriaBase>()), Times.Once());
        }

        [Test]
        public void should_not_attempt_to_map_episode_if_series_title_is_blank()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            _reports[0].Title = "1937 - Snow White and the Seven Dwarves";

            var results = Subject.GetRssDecision(_reports).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<SearchCriteriaBase>()), Times.Never());

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());

            results.Should().BeEmpty();
        }

        [Test]
        public void should_return_rejected_result_for_unparsable_search()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);
            _reports[0].Title = "1937 - Snow White and the Seven Dwarves";

            Subject.GetSearchDecision(_reports, new SingleEpisodeSearchCriteria()).ToList();

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<SearchCriteriaBase>()), Times.Never());

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
        }

        [Test]
        public void should_not_attempt_to_make_decision_if_series_is_unknown()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteEpisode.Series = null;

            Subject.GetRssDecision(_reports);

            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<RemoteEpisode>(), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
        }

        [Test]
        public void broken_report_shouldnt_blowup_the_process()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IParsingService>().Setup(c => c.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<SearchCriteriaBase>()))
                     .Throws<TestException>();

            _reports = new List<ReleaseInfo>
                {
                    new ReleaseInfo { Title = "The.Office.S03E115.DVDRip.XviD-OSiTV" },
                    new ReleaseInfo { Title = "The.Office.S03E115.DVDRip.XviD-OSiTV" },
                    new ReleaseInfo { Title = "The.Office.S03E115.DVDRip.XviD-OSiTV" }
                };

            Subject.GetRssDecision(_reports);

            Mocker.GetMock<IParsingService>().Verify(c => c.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<SearchCriteriaBase>()), Times.Exactly(_reports.Count));

            ExceptionVerification.ExpectedErrors(3);
        }

        [Test]
        public void should_return_unknown_series_rejection_if_series_is_unknown()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteEpisode.Series = null;

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);
        }

        [Test]
        public void should_only_include_reports_for_requested_episodes()
        {
            var series = Builder<Series>.CreateNew().Build();

            var episodes = Builder<Episode>.CreateListOfSize(2)
                .All()
                .With(v => v.SeriesId, series.Id)
                .With(v => v.Series, series)
                .With(v => v.SeasonNumber, 1)
                .With(v => v.SceneSeasonNumber, 2)
                .BuildList();

            var criteria = new SeasonSearchCriteria { Episodes = episodes.Take(1).ToList(), SeasonNumber = 1 };

            var reports = episodes.Select(v =>
                new ReleaseInfo()
                {
                    Title = string.Format("{0}.S{1:00}E{2:00}.720p.WEB-DL-DRONE", series.Title, v.SceneSeasonNumber, v.SceneEpisodeNumber)
                }).ToList();

            Mocker.GetMock<IParsingService>()
                .Setup(v => v.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<SearchCriteriaBase>()))
                .Returns<ParsedEpisodeInfo, int, int, string, SearchCriteriaBase>((p, _, _, _, _) =>
                    new RemoteEpisode
                    {
                        DownloadAllowed = true,
                        ParsedEpisodeInfo = p,
                        Series = series,
                        Episodes = episodes.Where(v => v.SceneEpisodeNumber == p.EpisodeNumbers.First()).ToList()
                    });

            Mocker.SetConstant<IEnumerable<IDownloadDecisionEngineSpecification>>(new List<IDownloadDecisionEngineSpecification>
            {
                Mocker.Resolve<NzbDrone.Core.DecisionEngine.Specifications.Search.EpisodeRequestedSpecification>()
            });

            var decisions = Subject.GetSearchDecision(reports, criteria);

            var approvedDecisions = decisions.Where(v => v.Approved).ToList();

            approvedDecisions.Count.Should().Be(1);
        }

        [Test]
        public void should_not_allow_download_if_series_is_unknown()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteEpisode.Series = null;

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);

            result.First().RemoteEpisode.DownloadAllowed.Should().BeFalse();
        }

        [Test]
        public void should_not_allow_download_if_no_episodes_found()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            _remoteEpisode.Episodes = new List<Episode>();

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);

            result.First().RemoteEpisode.DownloadAllowed.Should().BeFalse();
        }

        [Test]
        public void should_return_a_decision_when_exception_is_caught()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IParsingService>().Setup(c => c.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<SearchCriteriaBase>()))
                     .Throws<TestException>();

            _reports = new List<ReleaseInfo>
                {
                    new ReleaseInfo { Title = "The.Office.S03E115.DVDRip.XviD-OSiTV" },
                };

            Subject.GetRssDecision(_reports).Should().HaveCount(1);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_return_unknown_series_rejection_if_series_title_is_an_alias_for_another_series()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            Mocker.GetMock<ISceneMappingService>()
                  .Setup(s => s.FindTvdbId(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                  .Returns(12345);

            _remoteEpisode.Series = null;

            var result = Subject.GetRssDecision(_reports);

            result.Should().HaveCount(1);
            result.First().Rejections.First().Message.Should().Contain("12345");
        }
    }
}
