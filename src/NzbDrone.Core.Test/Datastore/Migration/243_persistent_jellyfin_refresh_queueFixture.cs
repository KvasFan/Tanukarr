using System;
using System.Data.SQLite;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Notifications.Jellyfin;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class PersistentJellyfinRefreshQueueFixture : MigrationTest<persistent_jellyfin_refresh_queue>
    {
        [Test]
        public void should_create_durable_per_connection_watermarks_without_copying_api_credentials()
        {
            var db = WithMigrationTestDb();
            var columns = db.Query("SELECT name FROM pragma_table_info('JellyfinRefreshStates')")
                .Select(column => (string)column["name"]);

            columns.Should().Contain(new[]
            {
                "NotificationId", "TargetFingerprint", "StateToken", "LastRefreshMode", "ImportedGeneration",
                "ImmediateGeneration", "TickGeneration", "LastTickUtc"
            });
            columns.Should().NotContain("ApiKey");
            db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'Notifications'").Should().ContainSingle();
            MigrationVersion.Should().Be(243);
        }

        [Test]
        public void should_round_trip_generations_larger_than_a_32_bit_integer()
        {
            using (var connection = WithDapperMigrationTestDb())
            {
                var state = new JellyfinRefreshState
                {
                    NotificationId = 10,
                    TargetFingerprint = new string('a', 64),
                    StateToken = "d834474f444b4d328119fe8c4b9c71fa",
                    LastRefreshMode = (int)JellyfinRefreshMode.EachImportAndInterval,
                    ImportedGeneration = 9000000000L,
                    ImmediateGeneration = 8000000000L,
                    TickGeneration = 7000000000L,
                    LastTickUtc = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc)
                };

                connection.Execute("INSERT INTO \"JellyfinRefreshStates\" (\"NotificationId\", \"TargetFingerprint\", \"StateToken\", \"LastRefreshMode\", \"ImportedGeneration\", \"ImmediateGeneration\", \"TickGeneration\", \"LastTickUtc\") VALUES (@NotificationId, @TargetFingerprint, @StateToken, @LastRefreshMode, @ImportedGeneration, @ImmediateGeneration, @TickGeneration, @LastTickUtc)", state);
                var stored = connection.QuerySingle<JellyfinRefreshState>("SELECT * FROM \"JellyfinRefreshStates\"");

                stored.ImportedGeneration.Should().Be(state.ImportedGeneration);
                stored.ImmediateGeneration.Should().Be(state.ImmediateGeneration);
                stored.TickGeneration.Should().Be(state.TickGeneration);
                stored.StateToken.Should().Be(state.StateToken);
                stored.LastRefreshMode.Should().Be(state.LastRefreshMode);
                stored.LastTickUtc.Should().Be(state.LastTickUtc);
            }
        }

        [Test]
        public void should_enforce_one_persistent_state_per_notification()
        {
            using (var connection = WithDapperMigrationTestDb())
            {
                const string insert = "INSERT INTO \"JellyfinRefreshStates\" (\"NotificationId\", \"TargetFingerprint\", \"StateToken\", \"LastTickUtc\") VALUES (10, 'fingerprint', 'token', '2026-10-12 12:00:00')";
                connection.Execute(insert);

                Action duplicate = () => connection.Execute(insert);

                duplicate.Should().Throw<SQLiteException>();
                connection.QuerySingle<int>("SELECT COUNT(*) FROM \"JellyfinRefreshStates\"").Should().Be(1);
            }
        }
    }
}
