using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class AnimeGroupingValidatorFixture : CoreTest
    {
        private Series _series;

        [SetUp]
        public void Setup()
        {
            _series = Member(10, "manual:example", 2);
        }

        [TestCase(MetadataSourceType.AniList)]
        [TestCase(MetadataSourceType.MyAnimeList)]
        [TestCase(MetadataSourceType.Tvdb)]
        [TestCase(MetadataSourceType.Tmdb)]
        public void should_accept_a_manual_single_season_without_replacing_its_provider_identity(MetadataSourceType source)
        {
            _series.MetadataSource = source;
            _series.ForeignId = "1234";
            _series.Path = "existing explicit path";

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().NotThrow();
            _series.MetadataSource.Should().Be(source);
            _series.ForeignId.Should().Be("1234");
            _series.Path.Should().Be("existing explicit path");
            _series.Seasons.Should().ContainSingle(s => s.SeasonNumber == 1);
        }

        [TestCase(1)]
        [TestCase(999)]
        public void should_accept_the_group_season_boundaries(int seasonNumber)
        {
            _series.AnimeGroupSeasonNumber = seasonNumber;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().NotThrow();
        }

        [TestCase(null)]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(1000)]
        public void should_reject_an_invalid_group_season(int? seasonNumber)
        {
            _series.AnimeGroupSeasonNumber = seasonNumber;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().Throw<ValidationException>()
                .Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupSeasonNumber");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("../example")]
        [TestCase("manual:example/child")]
        [TestCase("manual:example child")]
        public void should_reject_an_invalid_manual_group_id(string groupId)
        {
            _series.AnimeGroupId = groupId;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().Throw<ValidationException>()
                .Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupId");
        }

        [Test]
        public void should_reject_an_overlong_group_id()
        {
            _series.AnimeGroupId = new string('a', 97);

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().Throw<ValidationException>();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("Anime\nTitle")]
        public void should_reject_an_invalid_group_title(string title)
        {
            _series.AnimeGroupTitle = title;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().Throw<ValidationException>()
                .Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupTitle");
        }

        [Test]
        public void should_reject_an_overlong_group_title()
        {
            _series.AnimeGroupTitle = new string('a', 181);

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().Throw<ValidationException>();
        }

        [Test]
        public void should_reject_an_unknown_grouping_mode()
        {
            _series.AnimeGroupingMode = (AnimeGroupingMode)99;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().Throw<ValidationException>()
                .Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupingMode");
        }

        [Test]
        public void should_allow_native_multi_season_entries_without_renumbering_them()
        {
            _series.Seasons.Add(new Season { SeasonNumber = 2 });

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().NotThrow();
            _series.Seasons.Should().Contain(s => s.SeasonNumber == 1).And.Contain(s => s.SeasonNumber == 2);
        }

        [Test]
        public void should_allow_specials_alongside_one_native_season()
        {
            _series.Seasons.Add(new Season { SeasonNumber = 0 });

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().NotThrow();
        }

        [Test]
        public void should_reject_a_slot_owned_by_another_persisted_member()
        {
            var other = Member(20, _series.AnimeGroupId, 2);

            Action validate = () => AnimeGroupingValidator.Validate(_series, new[] { other });

            validate.Should().Throw<ValidationException>();
        }

        [Test]
        public void should_reject_a_duplicate_slot_between_two_unpersisted_batch_members()
        {
            _series.Id = 0;
            var other = Member(0, _series.AnimeGroupId, 2);

            Action validate = () => AnimeGroupingValidator.ValidateSlot(_series, new[] { other });

            validate.Should().Throw<ValidationException>();
        }

        [Test]
        public void should_allow_the_current_member_to_keep_its_own_slot()
        {
            var stored = Member(_series.Id, _series.AnimeGroupId, 2);

            Action validate = () => AnimeGroupingValidator.Validate(_series, new[] { stored });

            validate.Should().NotThrow();
        }

        [TestCase("manual:example", 1)]
        [TestCase("manual:other", 2)]
        public void should_allow_a_different_season_or_group(string otherGroupId, int otherSeason)
        {
            var other = Member(20, otherGroupId, otherSeason);

            Action validate = () => AnimeGroupingValidator.Validate(_series, new[] { other });

            validate.Should().NotThrow();
        }

        [Test]
        public void should_allow_automatic_discovery_without_group_fields()
        {
            _series.AnimeGroupingMode = AnimeGroupingMode.Automatic;
            _series.AnimeGroupId = null;
            _series.AnimeGroupTitle = null;
            _series.AnimeGroupSeasonNumber = null;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().NotThrow();
            AnimeGroupingValidator.HasGroup(_series).Should().BeFalse();
        }

        [Test]
        public void should_allow_disabled_grouping_without_changing_the_existing_member()
        {
            _series.AnimeGroupingMode = AnimeGroupingMode.Disabled;
            _series.Path = "existing explicit path";
            var other = Member(20, _series.AnimeGroupId, 2);

            Action validate = () => AnimeGroupingValidator.Validate(_series, new[] { other });

            validate.Should().NotThrow();
            AnimeGroupingValidator.HasGroup(_series).Should().BeFalse();
            _series.Path.Should().Be("existing explicit path");
            _series.AnimeGroupId.Should().Be("manual:example");
            _series.AnimeGroupSeasonNumber.Should().Be(2);
        }

        [Test]
        public void should_allow_a_movie_and_season_to_have_independent_number_one()
        {
            var season = Member(20, _series.AnimeGroupId, 1);
            _series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            _series.AnimeGroupSeasonNumber = null;
            _series.AnimeGroupMovieNumber = 1;

            Action validate = () => AnimeGroupingValidator.Validate(_series, new[] { season });

            validate.Should().NotThrow();
        }

        [TestCase(null)]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(1000)]
        public void should_reject_invalid_movie_positions(int? movieNumber)
        {
            _series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            _series.AnimeGroupSeasonNumber = null;
            _series.AnimeGroupMovieNumber = movieNumber;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().Throw<ValidationException>().Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupMovieNumber");
        }

        [TestCase(1)]
        [TestCase(999)]
        public void should_accept_movie_positions_at_the_boundaries(int movieNumber)
        {
            _series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            _series.AnimeGroupKind = AnimeGroupKind.MovieSaga;
            _series.AnimeGroupSeasonNumber = null;
            _series.AnimeGroupMovieNumber = movieNumber;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().NotThrow();
        }

        [Test]
        public void should_reject_two_movies_with_the_same_position_in_one_group()
        {
            _series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            _series.AnimeGroupSeasonNumber = null;
            _series.AnimeGroupMovieNumber = 1;
            var other = Member(20, _series.AnimeGroupId, 1);
            other.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            other.AnimeGroupSeasonNumber = null;
            other.AnimeGroupMovieNumber = 1;

            Action validate = () => AnimeGroupingValidator.Validate(_series, new[] { other });

            validate.Should().Throw<ValidationException>().Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupMovieNumber");
        }

        [TestCase(AnimeGroupMemberKind.Season)]
        [TestCase(AnimeGroupMemberKind.Movie)]
        public void should_reject_setting_both_season_and_movie_numbers(AnimeGroupMemberKind kind)
        {
            _series.AnimeGroupMemberKind = kind;
            _series.AnimeGroupMovieNumber = 1;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().Throw<ValidationException>();
        }

        [Test]
        public void should_require_a_series_group_for_a_season_member()
        {
            _series.AnimeGroupKind = AnimeGroupKind.MovieSaga;

            Action validate = () => AnimeGroupingValidator.Validate(_series, Array.Empty<Series>());

            validate.Should().Throw<ValidationException>().Which.Errors.Should().Contain(e => e.PropertyName == "AnimeGroupKind");
        }

        [Test]
        public void should_allow_manual_promotion_of_a_movie_saga_without_mutating_stored_movies()
        {
            var movie = Member(20, _series.AnimeGroupId, 1);
            movie.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            movie.AnimeGroupKind = AnimeGroupKind.MovieSaga;
            movie.AnimeGroupSeasonNumber = null;
            movie.AnimeGroupMovieNumber = 1;
            movie.Path = Path.Combine(Path.GetTempPath(), "Saga", "Film 01");

            Action validate = () => AnimeGroupingValidator.Validate(_series, new[] { movie });

            validate.Should().NotThrow();
            movie.AnimeGroupKind.Should().Be(AnimeGroupKind.MovieSaga);
            movie.Path.Should().EndWith("Film 01");
        }

        [Test]
        public void should_reject_batch_member_paths_that_collide_despite_distinct_movie_numbers()
        {
            _series.Id = 0;
            _series.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            _series.AnimeGroupSeasonNumber = null;
            _series.AnimeGroupMovieNumber = 1;
            _series.Path = Path.Combine(Path.GetTempPath(), "Group", "Movie title");
            var other = Member(0, _series.AnimeGroupId, 1);
            other.AnimeGroupMemberKind = AnimeGroupMemberKind.Movie;
            other.AnimeGroupSeasonNumber = null;
            other.AnimeGroupMovieNumber = 2;
            other.Path = _series.Path;

            Action validate = () => AnimeGroupingValidator.ValidateSlot(_series, new[] { other });

            validate.Should().Throw<ValidationException>().Which.Errors.Should().Contain(e => e.PropertyName == "Path");
        }

        private static Series Member(int id, string groupId, int groupSeason)
        {
            return new Series
            {
                Id = id,
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = groupId,
                AnimeGroupTitle = "Canonical Anime",
                AnimeGroupSeasonNumber = groupSeason,
                Seasons = new List<Season> { new Season { SeasonNumber = 1 } }
            };
        }
    }
}
