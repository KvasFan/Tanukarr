using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(244)]
    public class anime_series_grouping : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Series")
                .AddColumn("AnimeGroupingMode").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("AnimeGroupId").AsString(96).Nullable()
                .AddColumn("AnimeGroupTitle").AsString(180).Nullable()
                .AddColumn("AnimeGroupSeasonNumber").AsInt32().Nullable()
                .AddColumn("AnimeGroupFolder").AsString(180).Nullable();

            // NULL membership leaves existing and unrelated series independent. This is
            // also the final guard against simultaneous additions claiming the same slot.
            Create.Index("IX_Series_AnimeGroupSlot").OnTable("Series")
                .OnColumn("AnimeGroupId").Ascending()
                .OnColumn("AnimeGroupSeasonNumber").Ascending().WithOptions().Unique();
        }
    }
}
