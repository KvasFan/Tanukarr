using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class anime_group_moviesFixture : MigrationTest<anime_group_movies>
    {
        [Test]
        public void should_preserve_existing_season_grouping_and_paths_when_adding_typed_movie_fields()
        {
            var db = WithMigrationTestDb(m => m.Insert.IntoTable("Series").Row(SeriesRow(73255, true)));
            var row = db.Query<Series245>("SELECT \"Path\", \"AnimeGroupId\", \"AnimeGroupSeasonNumber\", \"AnimeGroupMemberKind\", \"AnimeGroupKind\", \"AnimeGroupMovieFolderStyle\", \"AnimeGroupMovieNumber\", \"AnimeGroupMemberFolder\" FROM \"Series\"").Single();
            row.Path.Should().Be("/anime/Example/Season 01");
            row.AnimeGroupId.Should().Be("group");
            row.AnimeGroupSeasonNumber.Should().Be(1);
            row.AnimeGroupMemberKind.Should().Be(0);
            row.AnimeGroupKind.Should().Be(0);
            row.AnimeGroupMovieFolderStyle.Should().Be(0);
            row.AnimeGroupMovieNumber.Should().BeNull();
            row.AnimeGroupMemberFolder.Should().BeNull();
        }

        [Test]
        public void should_allow_season_one_and_movie_one_in_the_same_group_but_reject_duplicate_movie_slots()
        {
            var db = WithDapperMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Series").Row(SeriesRow(73255, true));
                m.Insert.IntoTable("Series").Row(SeriesRow(73256, false));
                m.Insert.IntoTable("Series").Row(SeriesRow(73257, false));
            });
            db.Execute("UPDATE \"Series\" SET \"AnimeGroupId\"='group', \"AnimeGroupMemberKind\"=1, \"AnimeGroupMovieNumber\"=1 WHERE \"TvdbId\"=73256");
            var duplicate = (Action)(() => db.Execute("UPDATE \"Series\" SET \"AnimeGroupId\"='group', \"AnimeGroupMemberKind\"=1, \"AnimeGroupMovieNumber\"=1 WHERE \"TvdbId\"=73257"));
            duplicate.Should().Throw<Exception>().WithMessage("*UNIQUE*");
            db.Execute("UPDATE \"Series\" SET \"AnimeGroupId\"='group', \"AnimeGroupMemberKind\"=1, \"AnimeGroupMovieNumber\"=2 WHERE \"TvdbId\"=73257");
            db.Query<int>("SELECT COUNT(*) FROM \"Series\" WHERE \"AnimeGroupId\"='group'").Single().Should().Be(3);
        }

        [Test]
        public void should_keep_a_unique_season_slot_and_allow_unrelated_null_memberships()
        {
            var db = WithDapperMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Series").Row(SeriesRow(73255, true));
                m.Insert.IntoTable("Series").Row(SeriesRow(73256, false));
                m.Insert.IntoTable("Series").Row(SeriesRow(73257, false));
            });
            var duplicate = (Action)(() => db.Execute("UPDATE \"Series\" SET \"AnimeGroupId\"='group', \"AnimeGroupSeasonNumber\"=1 WHERE \"TvdbId\"=73256"));
            duplicate.Should().Throw<Exception>().WithMessage("*UNIQUE*");
            db.Query<int>("SELECT COUNT(*) FROM \"Series\" WHERE \"AnimeGroupId\" IS NULL").Single().Should().Be(2);
        }

        private static object SeriesRow(int id, bool grouped)
        {
            return new
            {
                TvdbId = id, TvRageId = 0, TvMazeId = 0, Title = "Example " + id, CleanTitle = "example" + id,
                Status = 0, Images = "[]", Path = grouped ? "/anime/Example/Season 01" : "/tv/Example " + id,
                Monitored = true, SeasonFolder = true, Runtime = 25, SeriesType = 0, UseSceneNumbering = false,
                TitleSlug = "example-" + id, Seasons = "[]", Genres = "[]", Tags = "[]", Actors = "[]",
                QualityProfileId = 1, OriginalLanguage = 1, Added = "2020-01-01 00:00:00",
                AnimeGroupingMode = grouped ? 1 : 0, AnimeGroupId = grouped ? "group" : null,
                AnimeGroupTitle = grouped ? "Example" : null, AnimeGroupSeasonNumber = grouped ? (int?)1 : null,
                AnimeGroupFolder = grouped ? "Example" : null
            };
        }
    }

    internal class Series245
    {
        public string Path { get; set; }
        public string AnimeGroupId { get; set; }
        public int? AnimeGroupSeasonNumber { get; set; }
        public int AnimeGroupMemberKind { get; set; }
        public int AnimeGroupKind { get; set; }
        public int AnimeGroupMovieFolderStyle { get; set; }
        public int? AnimeGroupMovieNumber { get; set; }
        public string AnimeGroupMemberFolder { get; set; }
    }
}
