using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class CompleteSeasonUpgradeSpecificationFixture : CoreTest<CompleteSeasonUpgradeSpecification>
    {
        private RemoteEpisode _release;
        private List<Episode> _season;

        [SetUp]
        public void Setup()
        {
            _season = new List<Episode>
            {
                new Episode { SeasonNumber = 1, Monitored = true, EpisodeFileId = 10, AirDateUtc = DateTime.UtcNow.AddDays(-1) }
            };
            _release = new RemoteEpisode { Series = new Series { Id = 1 }, Episodes = new List<Episode>(_season) };
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodesBySeason(1, 1)).Returns(_season);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void rejects_complete_season_feed_upgrades(bool pushed)
        {
            Subject.IsSatisfiedBy(_release, new ReleaseDecisionInformation(pushed, null)).Accepted.Should().BeFalse();
        }

        [Test]
        public void explicit_search_keeps_upgrade_rules()
        {
            Subject.IsSatisfiedBy(_release, new ReleaseDecisionInformation(false, new SeasonSearchCriteria())).Accepted.Should().BeTrue();
        }

        [Test]
        public void allows_new_episode_even_before_air_time()
        {
            _release.Episodes[0].EpisodeFileId = 0;
            _release.Episodes[0].AirDateUtc = DateTime.UtcNow.AddHours(1);
            Subject.IsSatisfiedBy(_release, new ReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void incomplete_season_keeps_upgrade_rules()
        {
            _season.Add(new Episode { Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-1) });
            Subject.IsSatisfiedBy(_release, new ReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [TestCase(false, -1)]
        [TestCase(true, 1)]
        public void unmonitored_or_future_gap_does_not_allow_upgrade(bool monitored, int days)
        {
            _season.Add(new Episode { Monitored = monitored, AirDateUtc = DateTime.UtcNow.AddDays(days) });
            Subject.IsSatisfiedBy(_release, new ReleaseDecisionInformation()).Accepted.Should().BeFalse();
        }

        [Test]
        public void multi_season_pack_filling_gap_remains_eligible()
        {
            _release.Episodes.Add(new Episode { SeasonNumber = 2, Monitored = true });
            Subject.IsSatisfiedBy(_release, new ReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void all_complete_multi_season_pack_is_rejected()
        {
            var second = new List<Episode> { new Episode { SeasonNumber = 2, Monitored = true, EpisodeFileId = 20 } };
            _release.Episodes.AddRange(second);
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodesBySeason(1, 2)).Returns(second);
            Subject.IsSatisfiedBy(_release, new ReleaseDecisionInformation()).Accepted.Should().BeFalse();
        }
    }
}
