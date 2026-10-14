using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(251)]
    public class custom_quality_presets : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QualityDefinitions")
                .AddColumn("Source").AsInt32().Nullable()
                .AddColumn("Resolution").AsInt32().Nullable()
                .AddColumn("Codec").AsString().NotNullable().WithDefaultValue(string.Empty)
                .AddColumn("TitlePattern").AsString().NotNullable().WithDefaultValue(string.Empty)
                .AddColumn("IsRetired").AsBoolean().NotNullable().WithDefaultValue(false);
            Alter.Table("QualityProfiles")
                .AddColumn("CodecMultipliers").AsString().NotNullable().WithDefaultValue("{}");
        }
    }
}
