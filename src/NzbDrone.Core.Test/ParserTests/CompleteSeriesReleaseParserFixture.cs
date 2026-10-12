using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ParserTests
{
    public class CompleteSeriesReleaseParserFixture : CoreTest
    {
        private CompleteSeriesSearchCriteria _criteria;

        [SetUp]
        public void Setup()
        {
            _criteria = new CompleteSeriesSearchCriteria
            {
                Series = new Series { Id = 7, Title = "Anthony Bourdain: No Reservations", Year = 2005 },
                SceneTitles = new List<string> { "Anthony Bourdain: No Reservations", "No Reservations" },
                Episodes = Enumerable.Range(1, 9).Select(s => new Episode { SeasonNumber = s, EpisodeNumber = 1 }).ToList()
            };
        }

        [TestCase("Anthony.Bourdain.No.Reservations.S01-S09.1080p.WEB-DL.x265-GROUP")]
        [TestCase("Anthony Bourdain No Reservations Seasons 1-9 DVDRip")]
        [TestCase("Anthony Bourdain No Reservations Seasons 1 to 9")]
        [TestCase("Anthony Bourdain No Reservations (2005) Complete Series 720p")]
        [TestCase("Anthony Bourdain No Reservations COMPLETE")]
        [TestCase("Anthony Bourdain No Reservations The Complete Collection")]
        [TestCase("No Reservations S01-S09")]
        [TestCase("[Group] No Reservations Complete Series")]
        [TestCase("No Reservations Box Set")]
        [TestCase("No Reservations S01 S02 S03 S04 S05 S06 S07 S08 S09")]
        public void accepts_advertised_complete_series(string title)
        {
            var parsed = CompleteSeriesReleaseParser.Parse(title, _criteria);
            parsed.Should().NotBeNull();
            parsed.SeasonNumbers.Should().Equal(Enumerable.Range(1, 9));
            parsed.FullSeason.Should().BeTrue();
            parsed.ReleaseTitle.Should().Be(title);
        }

        [TestCase("No Reservations S01 COMPLETE")]
        [TestCase("No Reservations S01-S08 COMPLETE SERIES")]
        [TestCase("No Reservations S02-S09")]
        [TestCase("No Reservations S01 S09")]
        [TestCase("No Reservations S09-S01")]
        [TestCase("No Reservations S01E01 1080p")]
        [TestCase("No Reservations S01-S09E01")]
        [TestCase("No Reservations Complete Season")]
        [TestCase("No Reservations Complete Series Part 1")]
        [TestCase("No Reservations (2007) Complete")]
        [TestCase("No Reservations Again S01-S09")]
        [TestCase("Other Show Complete Series")]
        [TestCase("No Reservations 1080p")]
        [TestCase("No Reservations Incomplete")]
        public void excludes_partial_or_unrelated_releases(string title)
        {
            CompleteSeriesReleaseParser.Parse(title, _criteria).Should().BeNull();
        }

        [Test]
        public void accepts_full_single_season_series()
        {
            _criteria.Episodes = _criteria.Episodes.Take(1).ToList();
            CompleteSeriesReleaseParser.Parse("No Reservations S01", _criteria).Should().NotBeNull();
        }

        [TestCase("[Group] No Reservations - 01-26 [1080p]", true)]
        [TestCase("[Group] No Reservations - 01-12 [1080p]", false)]
        [TestCase("[Group] No Reservations COMPLETE 01-26 [1080p]", true)]
        [TestCase("[Group] No Reservations COMPLETE 01-12 [1080p]", false)]
        [TestCase("[Group] No Reservations Complete Series 01-12 [1080p]", false)]
        [TestCase("[Group] No Reservations COMPLETE 01-12+OVA [1080p]", false)]
        [TestCase("[Group] No Reservations COMPLETE 01\u201312 [1080p]", false)]
        [TestCase("[Group] No Reservations COMPLETE 01 to 12 [1080p]", false)]
        public void checks_anime_absolute_batch_coverage(string title, bool complete)
        {
            _criteria.Series.SeriesType = SeriesTypes.Anime;
            _criteria.Episodes = Enumerable.Range(1, 26).Select(e => new Episode
            {
                SeasonNumber = 1, EpisodeNumber = e, AbsoluteEpisodeNumber = e
            }).ToList();
            (CompleteSeriesReleaseParser.Parse(title, _criteria) != null).Should().Be(complete);
        }

        [Test]
        public void preserves_quality_from_original_title()
        {
            var parsed = CompleteSeriesReleaseParser.Parse("No Reservations COMPLETE 1080p WEB-DL x265-GROUP", _criteria);
            parsed.Quality.Quality.Name.Should().Be("WEBDL-1080p");
            parsed.ReleaseGroup.Should().Be("GROUP");
        }
    }
}
