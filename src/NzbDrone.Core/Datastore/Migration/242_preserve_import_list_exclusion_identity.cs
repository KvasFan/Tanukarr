using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(242)]
    public class preserve_import_list_exclusion_identity : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("ImportListExclusions").AddColumn("MetadataSource").AsInt32().NotNullable().WithDefaultValue(0);
            Alter.Table("ImportListExclusions").AddColumn("ForeignId").AsString().Nullable();
        }
    }
}
