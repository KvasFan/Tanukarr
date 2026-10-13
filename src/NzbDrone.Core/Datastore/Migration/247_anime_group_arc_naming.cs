using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(247)]
    public class anime_group_arc_naming : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("NamingConfig")
                .AddColumn("AnimeGroupArcFolderFormat").AsString().NotNullable().WithDefaultValue("{Group Arc Title}");
        }
    }
}
