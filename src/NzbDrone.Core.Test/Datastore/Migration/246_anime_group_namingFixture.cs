using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class anime_group_namingFixture : MigrationTest<anime_group_naming>
    {
        [Test]
        public void should_add_group_defaults_without_replacing_existing_filename_expressions()
        {
            var db = WithDapperMigrationTestDb(m => m.Insert.IntoTable("NamingConfig").Row(new
            {
                RenameEpisodes = true,
                ReplaceIllegalCharacters = true,
                MultiEpisodeStyle = 0,
                StandardEpisodeFormat = "Custom - S{season:00}E{episode:00}",
                DailyEpisodeFormat = "Custom - {Air-Date}",
                AnimeEpisodeFormat = "Custom - {absolute:000}",
                SeriesFolderFormat = "{Series Title}",
                SeasonFolderFormat = "Season {season:00}",
                SpecialsFolderFormat = "Extras"
            }));
            var row = db.Query<NamingConfig>("SELECT * FROM \"NamingConfig\"").Single();

            row.AnimeGroupFolderFormat.Should().Be("{Group Title}");
            row.AnimeGroupSeasonFolderFormat.Should().Be("Season {Group Season:00}");
            row.AnimeGroupMovieFolderFormat.Should().Be("{Series Title}");
            row.AnimeGroupMovieSagaFolderFormat.Should().Be("Film {Group Movie:00}");
            row.StandardEpisodeFormat.Should().Be("Custom - S{season:00}E{episode:00}");
            row.AnimeEpisodeFormat.Should().Be("Custom - {absolute:000}");
            row.RenameEpisodes.Should().BeTrue();
        }

        [Test]
        public void should_persist_independent_custom_folder_expressions()
        {
            var db = WithDapperMigrationTestDb();
            db.Execute("INSERT INTO \"NamingConfig\" (\"RenameEpisodes\", \"MultiEpisodeStyle\", \"ReplaceIllegalCharacters\", \"AnimeGroupFolderFormat\", \"AnimeGroupSeasonFolderFormat\", \"AnimeGroupMovieFolderFormat\", \"AnimeGroupMovieSagaFolderFormat\") VALUES (@Rename, 0, 1, @Root, @Season, @Movie, @Saga)",
                new { Rename = 1, Root = "Anime - {Group Title}", Season = "Part {Group Season:00}", Movie = "Movie - {Series Title}", Saga = "Feature {Group Movie:00}" });
            var row = db.Query<NamingConfig>("SELECT * FROM \"NamingConfig\"").Single();

            row.AnimeGroupFolderFormat.Should().Be("Anime - {Group Title}");
            row.AnimeGroupSeasonFolderFormat.Should().Be("Part {Group Season:00}");
            row.AnimeGroupMovieFolderFormat.Should().Be("Movie - {Series Title}");
            row.AnimeGroupMovieSagaFolderFormat.Should().Be("Feature {Group Movie:00}");
        }
    }
}
