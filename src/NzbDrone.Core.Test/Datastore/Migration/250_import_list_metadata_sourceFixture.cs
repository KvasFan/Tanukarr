using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class ImportListMetadataSourceMigrationFixture : MigrationTest<import_list_metadata_source>
    {
        [Test]
        public void should_default_existing_lists_to_automatic_without_rewriting_settings_or_root_folders()
        {
            const string settings = "{\"username\":\"generated-account\",\"includedCustomLists\":[\"Generated List\"]}";
            var db = WithDapperMigrationTestDb(migration => migration.Insert.IntoTable("ImportLists").Row(new
            {
                Id = 7, Name = "Generated existing list", Implementation = "AniListImport",
                ConfigContract = "AniListSettings", Settings = settings, EnableAutomaticAdd = true,
                RootFolderPath = "/generated/existing-library", QualityProfileId = 1, ShouldMonitor = 0,
                SeriesType = 1, SeasonFolder = true, SearchForMissingEpisodes = false,
                MonitorNewItems = 0, Language = 1, Tags = "[]", TagExisting = 0
            }));

            var row = db.QuerySingle("SELECT Id,Settings,RootFolderPath,PreferredMetadataSource FROM ImportLists WHERE Id = 7");
            ((long)row.Id).Should().Be(7);
            ((string)row.Settings).Should().Be(settings);
            ((string)row.RootFolderPath).Should().Be("/generated/existing-library");
            ((long)row.PreferredMetadataSource).Should().Be(0);
            MigrationVersion.Should().Be(250);
        }
    }
}
