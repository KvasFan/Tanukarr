using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(246)]
    public class anime_group_naming : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("NamingConfig")
                .AddColumn("AnimeGroupFolderFormat").AsString().NotNullable().WithDefaultValue("{Group Title}")
                .AddColumn("AnimeGroupSeasonFolderFormat").AsString().NotNullable().WithDefaultValue("Season {Group Season:00}")
                .AddColumn("AnimeGroupMovieFolderFormat").AsString().NotNullable().WithDefaultValue("{Series Title}")
                .AddColumn("AnimeGroupMovieSagaFolderFormat").AsString().NotNullable().WithDefaultValue("Film {Group Movie:00}");
        }
    }
}
