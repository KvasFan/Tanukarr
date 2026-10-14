using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(249)]
    public class import_list_progress_queue : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("ImportListProgressStates")
                .WithColumn("ImportListId").AsInt32().NotNullable()
                .WithColumn("SeriesId").AsInt32().NotNullable()
                .WithColumn("TargetFingerprint").AsString(64).NotNullable()
                .WithColumn("PendingProgress").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("AcknowledgedProgress").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("AcknowledgedIdentityFingerprint").AsString(64).NotNullable().WithDefaultValue(string.Empty)
                .WithColumn("FailureCount").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("NextAttemptUtc").AsDateTime().NotNullable()
                .WithColumn("LastAttemptUtc").AsDateTime().Nullable();

            Create.Index().OnTable("ImportListProgressStates")
                .OnColumn("ImportListId").Ascending().OnColumn("SeriesId").Ascending().WithOptions().Unique();
        }
    }
}
