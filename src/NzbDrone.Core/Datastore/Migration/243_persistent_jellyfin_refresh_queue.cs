using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(243)]
    public class persistent_jellyfin_refresh_queue : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("JellyfinRefreshStates")
                .WithColumn("NotificationId").AsInt32().NotNullable()
                .WithColumn("TargetFingerprint").AsString(64).NotNullable()
                .WithColumn("StateToken").AsString(32).NotNullable()
                .WithColumn("LastRefreshMode").AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn("ImportedGeneration").AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn("ImmediateGeneration").AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn("TickGeneration").AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn("LastTickUtc").AsDateTime().NotNullable();

            Create.Index().OnTable("JellyfinRefreshStates")
                .OnColumn("NotificationId").Ascending().WithOptions().Unique();
        }
    }
}
