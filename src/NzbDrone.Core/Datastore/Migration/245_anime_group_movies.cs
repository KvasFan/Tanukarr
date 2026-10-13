using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(245)]
    public class anime_group_movies : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Series")
                .AddColumn("AnimeGroupMemberKind").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("AnimeGroupKind").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("AnimeGroupMovieFolderStyle").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("AnimeGroupMovieNumber").AsInt32().Nullable()
                .AddColumn("AnimeGroupMemberFolder").AsString(180).Nullable();

            Delete.Index("IX_Series_AnimeGroupSlot").OnTable("Series");
            Create.Index("IX_Series_AnimeGroupSeasonSlot").OnTable("Series")
                .OnColumn("AnimeGroupId").Ascending()
                .OnColumn("AnimeGroupMemberKind").Ascending()
                .OnColumn("AnimeGroupSeasonNumber").Ascending().WithOptions().Unique();
            Create.Index("IX_Series_AnimeGroupMovieSlot").OnTable("Series")
                .OnColumn("AnimeGroupId").Ascending()
                .OnColumn("AnimeGroupMemberKind").Ascending()
                .OnColumn("AnimeGroupMovieNumber").Ascending().WithOptions().Unique();
        }
    }
}
