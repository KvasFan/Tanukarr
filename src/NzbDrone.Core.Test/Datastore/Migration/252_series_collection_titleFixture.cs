using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class SeriesCollectionTitleMigrationFixture : MigrationTest<series_collection_title>
    {
        [Test]
        public void should_add_a_nullable_display_name_without_rewriting_native_identity_paths_or_grouping()
        {
            var db = WithDapperMigrationTestDb(migration => migration.Insert.IntoTable("Series").Row(new
            {
                Id = 42, TvdbId = -42, TvRageId = 0, TvMazeId = 0, Title = "Native entry", CleanTitle = "nativeentry",
                Status = 0, Images = "[]", Path = "/generated/Original/Season 02", Monitored = true, SeasonFolder = true,
                Runtime = 25, SeriesType = 0, UseSceneNumbering = false, TitleSlug = "native-entry", Seasons = "[]",
                Genres = "[]", Tags = "[]", Actors = "[]", QualityProfileId = 1, OriginalLanguage = 1,
                Added = "2020-01-01 00:00:00", MetadataSource = 2, ForeignId = "200", AnimeGroupingMode = 1,
                AnimeGroupId = "anilist:100", AnimeGroupTitle = "Original", AnimeGroupSeasonNumber = 2, AnimeGroupFolder = "Original"
            }));
            var row = db.QuerySingle("SELECT * FROM Series WHERE Id = 42");
            ((string)row.CollectionTitle).Should().BeNull();
            ((string)row.Title).Should().Be("Native entry");
            ((long)row.TvdbId).Should().Be(-42);
            ((long)row.MetadataSource).Should().Be(2);
            ((string)row.ForeignId).Should().Be("200");
            ((string)row.Path).Should().Be("/generated/Original/Season 02");
            ((string)row.AnimeGroupId).Should().Be("anilist:100");
            ((long)row.AnimeGroupSeasonNumber).Should().Be(2);
            db.Execute("UPDATE Series SET CollectionTitle = @title WHERE Id = 42", new { title = "Display collection" });
            db.QuerySingle<string>("SELECT CollectionTitle FROM Series WHERE Id = 42").Should().Be("Display collection");
            MigrationVersion.Should().Be(252);
        }
    }
}
