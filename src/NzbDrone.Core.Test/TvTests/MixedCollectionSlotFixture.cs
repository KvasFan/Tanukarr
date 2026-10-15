using System;
using FluentAssertions;
using FluentValidation;
using NUnit.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class MixedCollectionSlotFixture
    {
        private static Series Member(int id, AnimeGroupMemberKind kind, int number)
        {
            return new Series
            {
                Id = id,
                AnimeGroupingMode = AnimeGroupingMode.Manual,
                AnimeGroupId = "manual:mixed-fixture",
                AnimeGroupTitle = "Generated mixed collection",
                AnimeGroupMemberKind = kind,
                AnimeGroupSeasonNumber = kind == AnimeGroupMemberKind.Season ? number : null,
                AnimeGroupMovieNumber = kind == AnimeGroupMemberKind.Movie ? number : null,
                AnimeGroupSpecialNumber = kind == AnimeGroupMemberKind.Special ? number : null
            };
        }

        [Test]
        public void should_keep_season_movie_and_special_slots_independent()
        {
            var season = Member(1, AnimeGroupMemberKind.Season, 1);
            season.Seasons.Add(new Season { SeasonNumber = 2 });
            season.Seasons.Add(new Season { SeasonNumber = 4 });
            var movie = Member(2, AnimeGroupMemberKind.Movie, 1);
            var firstSpecial = Member(3, AnimeGroupMemberKind.Special, 1);
            var secondSpecial = Member(4, AnimeGroupMemberKind.Special, 2);
            var all = new[] { season, movie, firstSpecial, secondSpecial };
            foreach (var member in all)
            {
                Action validate = () => AnimeGroupingValidator.Validate(member, all);
                validate.Should().NotThrow();
            }

            season.Seasons.Should().Contain(item => item.SeasonNumber == 2).And.Contain(item => item.SeasonNumber == 4);
        }

        [Test]
        public void should_reserve_every_logical_slot_of_a_native_multi_season_member()
        {
            var series = Member(1, AnimeGroupMemberKind.Season, 3);
            series.Seasons.Add(new Season { SeasonNumber = 2 });
            series.Seasons.Add(new Season { SeasonNumber = 4 });
            var conflicting = Member(2, AnimeGroupMemberKind.Season, 4);
            Action validate = () => AnimeGroupingValidator.Validate(conflicting, new[] { series });
            validate.Should().Throw<ValidationException>();
        }

        [Test]
        public void should_reject_duplicate_special_slot_and_cross_kind_numbers()
        {
            var first = Member(1, AnimeGroupMemberKind.Special, 1);
            var second = Member(2, AnimeGroupMemberKind.Special, 1);
            Action validate = () => AnimeGroupingValidator.Validate(second, new[] { first });
            validate.Should().Throw<ValidationException>();
            second.AnimeGroupMovieNumber = 2;
            validate.Should().Throw<ValidationException>();
        }
    }
}
