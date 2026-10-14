using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class AniListRequestGeneratorFixture : CoreTest
    {
        [Test]
        public void should_query_the_named_users_anime_list_with_watching_statuses()
        {
            var generator = new AniListRequestGenerator
            {
                Settings = new AniListSettings
                {
                    Username = " someone ",
                    ImportCompleted = true,
                    ImportCurrent = false,
                    ImportPlanning = false,
                    ImportFinished = true,
                    ImportReleasing = false
                }
            };

            using var json = JsonDocument.Parse(generator.GetRequest(3).HttpRequest.ContentData);
            var variables = json.RootElement.GetProperty("variables");

            variables.GetProperty("id").GetString().Should().Be("someone");
            variables.GetProperty("page").GetInt32().Should().Be(3);
            variables.GetProperty("statusType").EnumerateArray().Select(status => status.GetString()).Should().Equal("COMPLETED");
            json.RootElement.GetProperty("query").GetString().Should().Contain("type: ANIME").And.Contain("perPage: 50");
            json.RootElement.GetProperty("query").GetString().Should().Contain("customLists")
                .And.Contain("customListEntries: customLists(asArray: true)");
        }

        [Test]
        public void should_fetch_unfiltered_named_account_collections_with_all_group_and_direct_relation_fields()
        {
            var generator = new AniListRequestGenerator
            {
                Settings = new AniListSettings
                {
                    Username = " someone ",
                    AccessToken = "generated-auth-token",
                    IncludedCustomLists = new[] { "Seasonal" },
                    ExcludedCustomLists = new[] { "Excluded" },
                    ImportCurrent = false,
                    ImportCompleted = true
                }
            };
            var request = generator.GetCollectionRequest(2).HttpRequest;
            using var document = JsonDocument.Parse(request.ContentData);
            document.RootElement.GetProperty("variables").GetProperty("id").GetString().Should().Be("someone");
            document.RootElement.GetProperty("variables").GetProperty("chunk").GetInt32().Should().Be(2);
            var query = document.RootElement.GetProperty("query").GetString();
            query.Should().Contain("MediaListCollection").And.Contain("perChunk: 500").And.Contain("type: ANIME")
                .And.Contain("isCustomList").And.Contain("updatedAt").And.Contain("relationType(version: 2)")
                .And.NotContain("status_in").And.NotContain("Seasonal").And.NotContain("Excluded");
            request.Headers["Authorization"].ToString().Should().Be("Bearer generated-auth-token");
            request.AllowAutoRedirect.Should().BeFalse();
            request.SuppressHttpError.Should().BeTrue();
            request.LogResponseContent.Should().BeFalse();
        }
    }
}
