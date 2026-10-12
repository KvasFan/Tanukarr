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
        }
    }
}
