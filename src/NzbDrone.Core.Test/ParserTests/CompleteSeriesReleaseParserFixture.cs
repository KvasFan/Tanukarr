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
            parsed.IsCompleteSeries.Should().BeTrue();
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

        [TestCase("No Reservations S01 COMPLETE", new[] { 1 })]
        [TestCase("No Reservations S02-S04 1080p", new[] { 2, 3, 4 })]
        [TestCase("No Reservations S01 S09", new[] { 1, 9 })]
        [TestCase("No Reservations Seasons 3 to 5 COMPLETE SERIES", new[] { 3, 4, 5 })]
        public void interactive_search_accepts_only_the_advertised_known_season_subset(string title, int[] seasons)
        {
            _criteria.IncludeSeasonPacks = true;
            var parsed = CompleteSeriesReleaseParser.Parse(title, _criteria);
            parsed.Should().NotBeNull();
            parsed.FullSeason.Should().BeTrue();
            parsed.IsCompleteSeries.Should().BeFalse();
            parsed.SeasonNumbers.Should().Equal(seasons);
            parsed.ReleaseTitle.Should().Be(title);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void explicit_unknown_seasons_must_not_be_silently_relabelled_as_known_coverage(bool interactive)
        {
            _criteria.IncludeSeasonPacks = interactive;
            CompleteSeriesReleaseParser.Parse("No Reservations S01-S10 COMPLETE", _criteria).Should().BeNull();
            CompleteSeriesReleaseParser.Parse("No Reservations S00-S09 COMPLETE", _criteria).Should().BeNull();
        }

        [TestCase("No Reservations S01E01")]
        [TestCase("No Reservations S01-S03E01")]
        [TestCase("No Reservations S02-S01")]
        [TestCase("No Reservations (2007) S01")]
        [TestCase("No Reservations Again S01")]
        [TestCase("No Reservations COMPLETE Part 1")]
        [TestCase("Other Show S01")]
        public void interactive_season_packs_keep_episode_year_alias_and_range_guards(string title)
        {
            _criteria.IncludeSeasonPacks = true;
            CompleteSeriesReleaseParser.Parse(title, _criteria).Should().BeNull();
        }

        [TestCase("Part2")]
        [TestCase("Part 2")]
        [TestCase("Volume2")]
        [TestCase("Volume 2")]
        [TestCase("Cour2")]
        [TestCase("Cour 2")]
        [TestCase("Episode2")]
        public void should_never_advertise_numeric_partial_markers_as_complete_season_coverage(string marker)
        {
            foreach (var interactive in new[] { false, true })
            {
                _criteria.IncludeSeasonPacks = interactive;
                CompleteSeriesReleaseParser.Parse("No Reservations S01-S09 " + marker + " COMPLETE", _criteria).Should().BeNull();
            }
        }

        [Test]
        public void should_keep_numeric_partial_words_when_they_belong_to_the_exact_native_title()
        {
            _criteria.Series.Title = "Baki-dou Part2";
            _criteria.SceneTitles = new List<string> { "Baki-dou Part2", "Baki-dou" };
            _criteria.Episodes = _criteria.Episodes.Take(1).ToList();
            var parsed = CompleteSeriesReleaseParser.Parse("Baki-dou Part2 S01 COMPLETE", _criteria);
            parsed.Should().NotBeNull();
            parsed.SeriesTitle.Should().Be("Baki-dou Part2");
            parsed.IsCompleteSeries.Should().BeTrue();
        }

        [TestCase("[Erai-raws] Baki-dou - 01 ~ 13 [1080p][BATCH]", true)]
        [TestCase("[Erai-raws] Baki-dou - 14 ~ 25 [1080p][BATCH]", false)]
        [TestCase("[Erai-raws] Baki-dou - 01 ~ 12 [1080p][BATCH]", false)]
        [TestCase("[Erai-raws] Baki-dou - 13 ~ 01 [1080p][BATCH]", false)]
        public void should_check_tilde_batch_coverage_against_the_native_catalogue_entry(string title, bool accepted)
        {
            _criteria.Series.Title = "Baki-dou";
            _criteria.Series.SeriesType = SeriesTypes.Anime;
            _criteria.SceneTitles = new List<string> { "Baki-dou" };
            _criteria.Episodes = Enumerable.Range(1, 13).Select(number => new Episode
            {
                SeasonNumber = 1, EpisodeNumber = number, AbsoluteEpisodeNumber = number
            }).ToList();
            var parsed = CompleteSeriesReleaseParser.Parse(title, _criteria);
            (parsed != null).Should().Be(accepted);
            if (accepted)
            {
                parsed.IsCompleteSeries.Should().BeTrue();
                parsed.SeasonNumbers.Should().Equal(1);
            }
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
