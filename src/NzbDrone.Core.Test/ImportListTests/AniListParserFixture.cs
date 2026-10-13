using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class AniListParserFixture : CoreTest
    {
        private static ImportListResponse Response(string content)
        {
            var request = new HttpRequest("https://graphql.anilist.co");
            var response = new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(content));

            return new ImportListResponse(new ImportListRequest(request), response);
        }

        /// <summary>
        /// Two finished entries. The first is on "Seasonal", the second is not on any list.
        /// AniList reports every list the user has defined on every entry, so the flag
        /// rather than the key's presence decides membership.
        /// </summary>
        private const string Json = @"{
            ""data"": { ""Page"": {
                ""pageInfo"": { ""currentPage"": 1, ""lastPage"": 1, ""hasNextPage"": false },
                ""mediaList"": [
                    { ""status"": ""COMPLETED"", ""progress"": 26,
                      ""customLists"": { ""Seasonal"": true, ""Rewatch"": false },
                      ""media"": { ""id"": 1, ""status"": ""FINISHED"", ""format"": ""TV"", ""episodes"": 26,
                                   ""title"": { ""userPreferred"": ""Cowboy Bebop"", ""romaji"": ""Cowboy Bebop"" } } },
                    { ""status"": ""COMPLETED"", ""progress"": 24,
                      ""customLists"": { ""Seasonal"": false, ""Rewatch"": false },
                      ""media"": { ""id"": 9253, ""status"": ""FINISHED"", ""format"": ""TV"", ""episodes"": 24,
                                   ""title"": { ""userPreferred"": ""Steins;Gate"", ""romaji"": ""Steins;Gate"" } } }
                ]
            } }
        }";

        private static AniListParser ParserWithCustomList(string customList)
        {
            return new AniListParser(new AniListSettings
            {
                Username = "someone",
                CustomList = customList,
                ImportCompleted = true,
                ImportFinished = true
            });
        }

        [Test]
        public void should_return_everything_when_no_custom_list_is_set()
        {
            var result = ParserWithCustomList(null).ParseResponse(Response(Json));

            result.Should().HaveCount(2);
        }

        [Test]
        public void should_return_everything_when_custom_list_is_blank()
        {
            var result = ParserWithCustomList("   ").ParseResponse(Response(Json));

            result.Should().HaveCount(2);
        }

        [Test]
        public void should_return_only_entries_on_the_named_custom_list()
        {
            var result = ParserWithCustomList("Seasonal").ParseResponse(Response(Json));

            result.Should().ContainSingle();
            result.Single().AniListId.Should().Be(1);
        }

        [Test]
        public void should_match_the_custom_list_name_case_insensitively()
        {
            // The name is typed by hand into settings, so its case cannot be relied upon.
            ParserWithCustomList("seasonal").ParseResponse(Response(Json)).Should().ContainSingle();
        }

        [Test]
        public void should_ignore_surrounding_whitespace_in_the_custom_list_name()
        {
            ParserWithCustomList("  Seasonal  ").ParseResponse(Response(Json)).Should().ContainSingle();
        }

        [Test]
        public void should_return_nothing_when_the_custom_list_matches_no_entries()
        {
            // A list that exists but holds nothing, and a name that does not exist at all,
            // must both yield an empty list rather than falling back to everything.
            ParserWithCustomList("Rewatch").ParseResponse(Response(Json)).Should().BeEmpty();
            ParserWithCustomList("Nonexistent").ParseResponse(Response(Json)).Should().BeEmpty();
        }

        [Test]
        public void should_exclude_entries_with_no_custom_lists_at_all()
        {
            var json = Json.Replace(@"""customLists"": { ""Seasonal"": true, ""Rewatch"": false }", @"""customLists"": {}");

            ParserWithCustomList("Seasonal").ParseResponse(Response(json)).Should().BeEmpty();
        }

        [Test]
        public void should_retain_the_previous_list_when_required_inclusion_membership_is_missing()
        {
            var json = Json.Replace(@"""customLists"": { ""Seasonal"": true, ""Rewatch"": false },", string.Empty);

            Assert.Throws<ImportListException>(() => ParserWithCustomList("Seasonal").ParseResponse(Response(json)));
        }

        [Test]
        public void should_use_the_romaji_title_when_the_preferred_title_is_missing()
        {
            var json = Json.Replace(@"""userPreferred"": ""Cowboy Bebop""", @"""userPreferred"": null");

            ParserWithCustomList("Seasonal").ParseResponse(Response(json)).Single().Title.Should().Be("Cowboy Bebop");
        }

        [Test]
        public void should_keep_anilist_and_mal_ids_in_separate_namespaces()
        {
            var json = Json.Replace(@"""id"": 1,", @"""id"": 1, ""idMal"": 42,");
            var item = ParserWithCustomList("Seasonal").ParseResponse(Response(json)).Single();

            item.AniListId.Should().Be(1);
            item.MalId.Should().Be(42);
            item.TvdbId.Should().Be(0);
        }

        [Test]
        public void should_not_combine_custom_lists_that_differ_only_by_case()
        {
            var json = Json.Replace(@"""Seasonal"": true,", @"""Seasonal"": true, ""seasonal"": false,");

            ParserWithCustomList("Seasonal").ParseResponse(Response(json)).Should().ContainSingle();
            ParserWithCustomList("seasonal").ParseResponse(Response(json)).Should().BeEmpty();
            ParserWithCustomList("SEASONAL").ParseResponse(Response(json)).Should().BeEmpty();
        }

        [Test]
        public void should_distinguish_completed_watching_status_from_finished_release_status()
        {
            var parser = new AniListParser(new AniListSettings
            {
                CustomList = "Seasonal",
                ImportCompleted = true,
                ImportFinished = false,
                ImportReleasing = true
            });

            parser.ParseResponse(Response(Json)).Should().BeEmpty();
            parser.ParseResponse(Response(Json.Replace(@"""status"": ""FINISHED""", @"""status"": ""RELEASING"""))).Should().ContainSingle();
        }

        [TestCase(@"{""data"": null, ""errors"": [{""message"": ""Private user""}]}")]
        [TestCase(@"{""data"": {""Page"": null}}")]
        [TestCase(@"{""data"": {""Page"": {""mediaList"": []}}}")]
        [TestCase(@"{""data"": {""Page"": {""pageInfo"": {""currentPage"": 1}, ""mediaList"": []}}}")]
        public void should_fail_instead_of_treating_an_incomplete_response_as_an_empty_list(string json)
        {
            Assert.Throws<ImportListException>(() => ParserWithCustomList(null).ParseResponse(Response(json)));
        }

        [Test]
        public void should_reject_partial_data_with_graphql_errors()
        {
            var json = Json.Replace(@"""data"":", @"""errors"": [{""message"": ""Forbidden""}], ""data"":");

            Assert.Throws<ImportListException>(() => ParserWithCustomList(null).ParseResponse(Response(json)));
        }

        [Test]
        public void should_accept_an_empty_complete_list()
        {
            const string json = @"{""data"": {""Page"": {""pageInfo"": {""currentPage"": 1, ""hasNextPage"": false}, ""mediaList"": []}}}";

            ParserWithCustomList(null).ParseResponse(Response(json), out var pageInfo).Should().BeEmpty();
            pageInfo.HasNextPage.Should().BeFalse();
        }
    }
}
