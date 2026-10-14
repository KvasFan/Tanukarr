using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class JellyfinWatchedStateMigrationFixture : MigrationTest<jellyfin_watched_state>
    {
        [Test]
        public void should_add_watch_defaults_without_rewriting_episode_identity_or_monitoring()
        {
            var db = WithDapperMigrationTestDb(m => m.Insert.IntoTable("Episodes").Row(new
            {
                Id = 7, SeriesId = 1, EpisodeFileId = 123, SeasonNumber = 1, EpisodeNumber = 2,
                Monitored = true, UnverifiedSceneNumbering = false, Runtime = 24,
                Title = "Existing native imported episode", Images = "[]"
            }));
            var row = db.QuerySingle("SELECT \"Id\", \"EpisodeFileId\", \"SeasonNumber\", \"EpisodeNumber\", \"Monitored\", \"Watched\", \"WatchedAtUtc\", \"WatchedSource\", \"JellyfinUnmonitorOwner\" FROM \"Episodes\"");
            ((long)row.Id).Should().Be(7);
            ((long)row.EpisodeFileId).Should().Be(123);
            ((long)row.SeasonNumber).Should().Be(1);
            ((long)row.EpisodeNumber).Should().Be(2);
            Convert.ToBoolean((object)row.Monitored).Should().BeTrue();
            Convert.ToBoolean((object)row.Watched).Should().BeFalse();
            ((object)row.WatchedAtUtc).Should().BeNull();
            ((object)row.WatchedSource).Should().BeNull();
            ((object)row.JellyfinUnmonitorOwner).Should().BeNull();
        }

        [Test]
        public void should_store_only_bounded_target_fingerprints_and_cadence_not_connection_credentials()
        {
            var db = WithMigrationTestDb();
            var columns = db.Query("SELECT name FROM pragma_table_info('JellyfinWatchStates')")
                .Select(column => (string)column["name"]).ToList();
            columns.Should().BeEquivalentTo(new[] { "Id", "NotificationId", "TargetFingerprint", "LastSyncUtc", "UnmonitorWatchedEpisodes" });
            MigrationVersion.Should().Be(248);
        }
    }
}
