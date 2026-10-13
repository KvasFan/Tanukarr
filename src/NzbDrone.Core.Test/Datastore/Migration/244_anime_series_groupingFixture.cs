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
    public class anime_series_groupingFixture : MigrationTest<anime_series_grouping>
    {
        [Test]
        public void should_preserve_existing_paths_and_leave_existing_series_ungrouped()
        {
            var db = WithMigrationTestDb(m => m.Insert.IntoTable("Series").Row(SeriesRow(73255)));
            var row = db.Query<Series244>("SELECT \"Path\", \"AnimeGroupingMode\", \"AnimeGroupId\", \"AnimeGroupTitle\", \"AnimeGroupSeasonNumber\", \"AnimeGroupFolder\" FROM \"Series\"").Single();
            row.AnimeGroupingMode.Should().Be(0);
            row.AnimeGroupId.Should().BeNull();
            row.AnimeGroupTitle.Should().BeNull();
            row.AnimeGroupSeasonNumber.Should().BeNull();
            row.AnimeGroupFolder.Should().BeNull();
            row.Path.Should().Be("/tv/Example 73255");
        }

        [Test]
        public void should_allow_multiple_unrelated_series_with_null_group_membership()
        {
            var db = WithMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Series").Row(SeriesRow(73255));
                m.Insert.IntoTable("Series").Row(SeriesRow(73256));
            });
            db.Query<Series244>("SELECT \"Path\", \"AnimeGroupingMode\", \"AnimeGroupId\", \"AnimeGroupTitle\", \"AnimeGroupSeasonNumber\", \"AnimeGroupFolder\" FROM \"Series\"").Should().HaveCount(2);
        }

        [Test]
        public void should_persist_group_metadata_and_enforce_unique_group_season_slots()
        {
            var db = WithDapperMigrationTestDb(m =>
            {
                m.Insert.IntoTable("Series").Row(SeriesRow(73255));
                m.Insert.IntoTable("Series").Row(SeriesRow(73256));
            });
            db.Execute("UPDATE \"Series\" SET \"AnimeGroupingMode\"=1, \"AnimeGroupId\"='manual-group', \"AnimeGroupTitle\"='Example', \"AnimeGroupSeasonNumber\"=2, \"AnimeGroupFolder\"='Example' WHERE \"TvdbId\"=73255");
            var row = db.Query<Series244>("SELECT * FROM \"Series\" WHERE \"TvdbId\"=73255").Single();
            row.AnimeGroupingMode.Should().Be(1);
            row.AnimeGroupId.Should().Be("manual-group");
            row.AnimeGroupTitle.Should().Be("Example");
            row.AnimeGroupSeasonNumber.Should().Be(2);
            row.AnimeGroupFolder.Should().Be("Example");
            var duplicate = (Action)(() => db.Execute("UPDATE \"Series\" SET \"AnimeGroupId\"='manual-group', \"AnimeGroupSeasonNumber\"=2 WHERE \"TvdbId\"=73256"));
            duplicate.Should().Throw<Exception>().WithMessage("*UNIQUE*");
            db.Execute("UPDATE \"Series\" SET \"AnimeGroupId\"='manual-group', \"AnimeGroupSeasonNumber\"=3 WHERE \"TvdbId\"=73256");
        }

        private static object SeriesRow(int id)
        {
            return new
            {
                TvdbId = id, TvRageId = 0, TvMazeId = 0, Title = "Example " + id, CleanTitle = "example" + id,
                Status = 0, Images = "[]", Path = "/tv/Example " + id, Monitored = true, SeasonFolder = true,
                Runtime = 25, SeriesType = 0, UseSceneNumbering = false, TitleSlug = "example-" + id,
                Seasons = "[]", Genres = "[]", Tags = "[]", Actors = "[]", QualityProfileId = 1,
                OriginalLanguage = 1, Added = "2020-01-01 00:00:00"
            };
        }
    }

    internal class Series244
    {
        public string Path { get; set; }
        public int AnimeGroupingMode { get; set; }
        public string AnimeGroupId { get; set; }
        public string AnimeGroupTitle { get; set; }
        public int? AnimeGroupSeasonNumber { get; set; }
        public string AnimeGroupFolder { get; set; }
    }
}
