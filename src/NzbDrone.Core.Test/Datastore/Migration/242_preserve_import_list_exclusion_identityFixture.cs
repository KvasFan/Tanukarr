using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class PreserveImportListExclusionIdentityFixture : MigrationTest<preserve_import_list_exclusion_identity>
    {
        [Test]
        public void should_keep_legacy_exclusions_as_tvdb_exclusions()
        {
            var db = WithMigrationTestDb(m => m.Insert.IntoTable("ImportListExclusions").Row(new { Id = 1, TvdbId = 123, Title = "Existing exclusion" }));
            var legacy = db.Query<ExclusionIdentityMigrationRow>("SELECT \"TvdbId\", \"MetadataSource\", \"ForeignId\" FROM \"ImportListExclusions\"").Single();

            legacy.TvdbId.Should().Be(123);
            legacy.MetadataSource.Should().Be((int)MetadataSourceType.Tvdb);
            legacy.ForeignId.Should().BeNull();
        }
    }

    public class ExclusionIdentityMigrationRow
    {
        public int TvdbId { get; set; }
        public int MetadataSource { get; set; }
        public string ForeignId { get; set; }
    }
}
