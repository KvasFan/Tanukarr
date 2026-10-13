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
    public class anime_group_arc_namingFixture : MigrationTest<anime_group_arc_naming>
    {
        [Test]
        public void should_add_arc_defaults_while_preserving_existing_group_and_file_expressions()
        {
            var db = WithDapperMigrationTestDb(m => m.Insert.IntoTable("NamingConfig").Row(new
            {
                RenameEpisodes = true,
                ReplaceIllegalCharacters = true,
                MultiEpisodeStyle = 0,
                StandardEpisodeFormat = "Native - S{season:00}E{episode:00}",
                AnimeEpisodeFormat = "Native - {absolute:000}",
                AnimeGroupFolderFormat = "Collection {Group Title}",
                AnimeGroupSeasonFolderFormat = "Part {Group Season:00}",
                AnimeGroupMovieFolderFormat = "Movie - {Series Title}",
                AnimeGroupMovieSagaFolderFormat = "Feature {Group Movie:00}"
            }));
            var row = db.Query<NamingConfig>("SELECT * FROM \"NamingConfig\"").Single();

            row.AnimeGroupArcFolderFormat.Should().Be("{Group Arc Title}");
            row.AnimeGroupFolderFormat.Should().Be("Collection {Group Title}");
            row.AnimeGroupSeasonFolderFormat.Should().Be("Part {Group Season:00}");
            row.AnimeGroupMovieFolderFormat.Should().Be("Movie - {Series Title}");
            row.AnimeGroupMovieSagaFolderFormat.Should().Be("Feature {Group Movie:00}");
            row.StandardEpisodeFormat.Should().Be("Native - S{season:00}E{episode:00}");
            row.AnimeEpisodeFormat.Should().Be("Native - {absolute:000}");
        }
    }
}
