using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(250)]
    public class import_list_metadata_source : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ImportLists")
                .AddColumn("PreferredMetadataSource").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
