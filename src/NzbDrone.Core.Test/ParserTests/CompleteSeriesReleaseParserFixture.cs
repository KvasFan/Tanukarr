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

        private void GivenSingleSeasonSeries()
        {
            _criteria.Series.Seasons = new List<Season> { new Season { SeasonNumber = 0 }, new Season { SeasonNumber = 1 } };
            _criteria.Episodes = Enumerable.Range(1, 23).Select(e => new Episode { SeasonNumber = 1, EpisodeNumber = e }).ToList();
        }

        [TestCase("No Reservations (BD 1080p HEVC x265 10-bit FLAC)")]
        [TestCase("No Reservations BD 1080p Hi10p FLAC5 1 x264x [Dual-Audio]")]
        [TestCase("No.Reservations.1080p.BluRay.x265-GROUP")]
        [TestCase("[Group] No Reservations [BD 1080p]")]
        public void accepts_bare_title_pack_for_single_season_series(string title)
        {
            GivenSingleSeasonSeries();

            var parsed = CompleteSeriesReleaseParser.Parse(title, _criteria);
            parsed.Should().NotBeNull();
            parsed.SeasonNumbers.Should().Equal(1);
            parsed.FullSeason.Should().BeTrue();
        }

        [TestCase("No Reservations The Movie 1080p")]
        [TestCase("No Reservations Again 1080p BluRay")]
        [TestCase("No Reservations S01E01 1080p")]
        [TestCase("No Reservations Episode 3 1080p")]
        [TestCase("No Reservations S02 1080p")]
        [TestCase("No Reservations (2007) 1080p")]
        [TestCase("No Reservations")]
        public void rejects_non_pack_titles_for_single_season_series(string title)
        {
            GivenSingleSeasonSeries();

            CompleteSeriesReleaseParser.Parse(title, _criteria).Should().BeNull();
        }

        [Test]
        public void rejects_bare_title_pack_for_multi_season_series()
        {
            _criteria.Series.Seasons = Enumerable.Range(1, 9).Select(s => new Season { SeasonNumber = s }).ToList();

            CompleteSeriesReleaseParser.Parse("No Reservations BD 1080p x265", _criteria).Should().BeNull();
        }

        [Test]
        public void rejects_bare_title_pack_when_seasons_unknown()
        {
            _criteria.Episodes = _criteria.Episodes.Take(1).ToList();

            CompleteSeriesReleaseParser.Parse("No Reservations BD 1080p x265", _criteria).Should().BeNull();
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

        private void GivenCollectionSecondSeason(bool interactive = true)
        {
            var series = new Series
            {
                Id = 564,
                Title = "Assassination Classroom Second Season",
                Year = 2016,
                SeriesType = SeriesTypes.Anime,
                Seasons = new List<Season> { new Season { SeasonNumber = 1 } },
                AnimeGroupingMode = AnimeGroupingMode.Automatic,
                AnimeGroupMemberKind = AnimeGroupMemberKind.Season,
                AnimeGroupTitle = "Assassination Classroom",
                AnimeGroupSeasonNumber = 2
            };

            _criteria = new CompleteSeriesSearchCriteria
            {
                Series = series,
                SceneTitles = new List<string> { series.Title, "Ansatsu Kyoushitsu 2nd Season" },
                Episodes = Enumerable.Range(1, 25).Select(e => new Episode { SeasonNumber = 1, EpisodeNumber = e, AbsoluteEpisodeNumber = e }).ToList(),
                IncludeSeasonPacks = interactive
            };
            _criteria.UseCollectionNumbering();
        }

        [Test]
        public void maps_collection_season_numbers_to_the_member()
        {
            GivenCollectionSecondSeason();

            _criteria.CollectionTitle.Should().Be("Assassination Classroom");
            _criteria.CollectionSeasons.Should().Equal(new Dictionary<int, int> { { 2, 1 } });
            _criteria.CollectionQueryTitles.Should().Equal("Assassination Classroom S02");
        }

        [TestCase("Assassination Classroom S02 MULTi (VOSTFR-FRENCH) 1080p x265 10bit [Apoca]")]
        [TestCase("[hchcsen] Assassination Classroom (2015) - Season 2 (S02) (BD 1080p HEVC x265 Opus 2.0) [Dual-Audio] | Ansatsu Kyoushitsu")]
        public void accepts_collection_title_with_the_member_collection_season(string title)
        {
            GivenCollectionSecondSeason();

            var parsed = CompleteSeriesReleaseParser.Parse(title, _criteria);
            parsed.Should().NotBeNull();
            parsed.SeasonNumbers.Should().Equal(1);
            parsed.FullSeason.Should().BeTrue();
            parsed.IsCompleteSeries.Should().BeTrue();
            parsed.SeriesTitle.Should().Be("Assassination Classroom Second Season");
            parsed.ReleaseTitle.Should().Be(title);
        }

        [Test]
        public void accepts_bare_dual_audio_batch_for_single_season_member()
        {
            GivenCollectionSecondSeason();

            var parsed = CompleteSeriesReleaseParser.Parse("[Exiled-Destiny] Assassination Classroom Second Season [Dual Audio]", _criteria);
            parsed.Should().NotBeNull();
            parsed.SeasonNumbers.Should().Equal(1);
        }

        [TestCase("Assassination Classroom S01 MULTi (VOSTFR-FRENCH) 1080p x265 10bit [Apoca]")]
        [TestCase("[Sokudo] Assassination Classroom S01+S02+OVA [1080p BD AV1][dual audio]")]
        [TestCase("[Exiled-Destiny] Assassination Classroom v2 [Dual Audio]")]
        [TestCase("Assassination Classroom Complete Series 1080p")]
        [TestCase("[Group] Assassination Classroom - 23-47 [1080p]")]
        [TestCase("[hchcsen] Assassination Classroom (2015) - S00E02 - Episode 0 (BD 1080p HEVC x265 Opus 2.0)")]
        [TestCase("Assassination Classroom (2020) S02 1080p")]
        public void rejects_collection_titles_for_other_members_or_whole_collection(string title)
        {
            GivenCollectionSecondSeason();

            CompleteSeriesReleaseParser.Parse(title, _criteria).Should().BeNull();
        }

        [Test]
        public void ignores_collection_numbering_when_grouping_is_disabled()
        {
            GivenCollectionSecondSeason();
            _criteria.Series.AnimeGroupingMode = AnimeGroupingMode.Disabled;
            _criteria.CollectionTitle = null;
            _criteria.CollectionSeasons = new Dictionary<int, int>();
            _criteria.UseCollectionNumbering();

            _criteria.CollectionQueryTitles.Should().BeEmpty();
            CompleteSeriesReleaseParser.Parse("Assassination Classroom S02 1080p", _criteria).Should().BeNull();
        }

        [Test]
        public void first_member_titled_like_the_collection_rejects_the_second_collection_season()
        {
            GivenCollectionSecondSeason();
            _criteria.Series.Title = "Assassination Classroom";
            _criteria.Series.Year = 2015;
            _criteria.Series.AnimeGroupSeasonNumber = 1;
            _criteria.SceneTitles = new List<string> { "Assassination Classroom" };
            _criteria.UseCollectionNumbering();

            CompleteSeriesReleaseParser.Parse("Assassination Classroom S02 MULTi 1080p x265", _criteria).Should().BeNull();
            CompleteSeriesReleaseParser.Parse("[Exiled-Destiny] Assassination Classroom Second Season [Dual Audio]", _criteria).Should().BeNull();
            CompleteSeriesReleaseParser.Parse("Assassination Classroom S01 MULTi 1080p x265", _criteria).SeasonNumbers.Should().Equal(1);
        }
    }
}
