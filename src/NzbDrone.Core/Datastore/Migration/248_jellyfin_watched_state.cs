using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(248)]
    public class jellyfin_watched_state : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Episodes").AddColumn("Watched").AsBoolean().NotNullable().WithDefaultValue(false)
                .AddColumn("WatchedAtUtc").AsDateTime().Nullable()
                .AddColumn("WatchedSource").AsString(64).Nullable()
                .AddColumn("JellyfinUnmonitorOwner").AsString(64).Nullable();

            Create.TableForModel("JellyfinWatchStates")
                .WithColumn("NotificationId").AsInt32().NotNullable()
                .WithColumn("TargetFingerprint").AsString(64).NotNullable()
                .WithColumn("LastSyncUtc").AsDateTime().NotNullable();
            Alter.Table("JellyfinWatchStates").AddColumn("UnmonitorWatchedEpisodes").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.Index().OnTable("JellyfinWatchStates")
                .OnColumn("NotificationId").Ascending().WithOptions().Unique();
        }
    }
}
