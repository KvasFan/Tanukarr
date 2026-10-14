using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class CustomQualityPresetsMigrationFixture : MigrationTest<custom_quality_presets>
    {
        [Test]
        public void should_preserve_quality_identities_and_profile_rules_with_additive_defaults()
        {
            const string items = "[{\"quality\":19,\"allowed\":true,\"items\":[],\"minSize\":3,\"maxSize\":100}]";
            var db = WithDapperMigrationTestDb(migration =>
            {
                migration.Insert.IntoTable("QualityDefinitions").Row(new { Id = 31, Quality = 19, Title = "Generated renamed Blu-ray" });
                migration.Insert.IntoTable("QualityProfiles").Row(new { Id = 7, Name = "Generated existing profile", Cutoff = 19, Items = items });
            });
            var definition = db.QuerySingle("SELECT * FROM QualityDefinitions WHERE Id = 31");
            ((long)definition.Quality).Should().Be(19);
            ((string)definition.Title).Should().Be("Generated renamed Blu-ray");
            ((string)definition.Codec).Should().BeEmpty();
            ((string)definition.TitlePattern).Should().BeEmpty();
            ((long)definition.IsRetired).Should().Be(0);
            var profile = db.QuerySingle("SELECT * FROM QualityProfiles WHERE Id = 7");
            ((string)profile.Items).Should().Be(items);
            ((long)profile.Cutoff).Should().Be(19);
            ((string)profile.CodecMultipliers).Should().Be("{}");
            MigrationVersion.Should().Be(251);
        }
    }
}
