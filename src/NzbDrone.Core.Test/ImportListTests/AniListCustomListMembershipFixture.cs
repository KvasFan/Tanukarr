using System;
using System.Text;
using System.Text.Json;
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
    public class AniListCustomListMembershipFixture : CoreTest
    {
        private AniListSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new AniListSettings { Username = "someone", ExcludedCustomLists = new[] { "Excluded" } };
        }

        private static ImportListResponse Response(string membership, bool graphqlError = false)
        {
            var content = @"{""data"":{""Page"":{""pageInfo"":{""currentPage"":1,""hasNextPage"":false},""mediaList"":
                [{""status"":""CURRENT"",""media"":{""id"":1,""status"":""FINISHED"",""title"":{""userPreferred"":""Generated anime""}},"
                + membership + @"}]}}}";

            if (graphqlError)
            {
                content = content.Replace(@"""data"":", @"""errors"":[{""message"":""Forbidden""}],""data"":");
            }

            var request = new HttpRequest("https://graphql.anilist.co");
            var response = new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(content));
            return new ImportListResponse(new ImportListRequest(request), response);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_normalize_null_map_from_explicit_array_flags_and_veto_watching_members(bool excluded)
        {
            var membership = @"""customLists"":null,""customListEntries"":[{""name"":""Excluded"",""enabled"":"
                + (excluded ? "true" : "false") + "}]";
            var parser = new AniListParser(_settings);

            var result = parser.ParseResponse(Response(membership));

            result.Should().HaveCount(excluded ? 0 : 1);
            parser.ExcludedMediaIds.Should().HaveCount(excluded ? 1 : 0);
        }

        [Test]
        public void should_use_explicit_array_when_map_field_is_missing()
        {
            new AniListParser(_settings).ParseResponse(Response(
                @"""customListEntries"":[{""name"":""Excluded"",""enabled"":false}]"))
                .Should().ContainSingle().Which.AniListId.Should().Be(1);
        }

        [Test]
        public void should_accept_explicit_empty_array_as_known_empty_membership()
        {
            new AniListParser(_settings).ParseResponse(Response(@"""customLists"":null,""customListEntries"":[]"))
                .Should().ContainSingle();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_support_included_custom_lists_and_legacy_selection_through_array_flags(bool legacy)
        {
            _settings.ExcludedCustomLists = Array.Empty<string>();

            if (legacy)
            {
                _settings.CustomList = "Seasonal";
            }
            else
            {
                _settings.IncludedCustomLists = new[] { "Seasonal" };
            }

            new AniListParser(_settings).ParseResponse(Response(
                @"""customLists"":null,""customListEntries"":[{""name"":""Seasonal"",""enabled"":true}]"))
                .Should().ContainSingle();
        }

        [Test]
        public void should_subtract_array_exclusions_when_the_same_member_is_included()
        {
            _settings.IncludedCustomLists = new[] { "Included" };
            var parser = new AniListParser(_settings);

            parser.ParseResponse(Response(@"""customLists"":null,""customListEntries"":[
                {""name"":""Included"",""enabled"":true},{""name"":""Excluded"",""enabled"":true}]"))
                .Should().BeEmpty();
            parser.ExcludedMediaIds.Should().Equal(1);
        }

        [Test]
        public void should_not_infer_explicit_inclusion_when_both_membership_shapes_are_unknown()
        {
            _settings.IncludedCustomLists = new[] { "Included" };

            Assert.Throws<ImportListException>(() => new AniListParser(_settings).ParseResponse(
                Response(@"""customLists"":null,""customListEntries"":null")));
        }

        [Test]
        public void should_keep_known_empty_included_lists_empty_without_falling_back_to_everything()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal" };

            new AniListParser(_settings).ParseResponse(Response(@"""customLists"":null,""customListEntries"":[]"))
                .Should().BeEmpty();
        }

        [Test]
        public void should_preserve_exact_case_distinct_names_and_reject_ambiguous_exclusions()
        {
            const string membership = @"""customLists"":null,""customListEntries"":[
                {""name"":""Excluded"",""enabled"":true},{""name"":""excluded"",""enabled"":false}]";
            _settings.ExcludedCustomLists = new[] { "excluded" };
            new AniListParser(_settings).ParseResponse(Response(membership)).Should().ContainSingle();

            _settings.ExcludedCustomLists = new[] { "EXCLUDED" };
            Assert.Throws<ImportListException>(() => new AniListParser(_settings).ParseResponse(Response(membership)));
        }

        [TestCase(@"""customLists"":null")]
        [TestCase(@"""customLists"":null,""customListEntries"":null")]
        [TestCase(@"""customListEntries"":null")]
        [TestCase(@"""progress"":0")]
        public void should_fail_when_both_membership_shapes_are_unknown(string membership)
        {
            Assert.Throws<ImportListException>(() => new AniListParser(_settings).ParseResponse(Response(membership)));
        }

        [TestCase(@"[null]")]
        [TestCase(@"[{""name"":""Excluded""}]")]
        [TestCase(@"[{""name"":""Excluded"",""enabled"":null}]")]
        [TestCase(@"[{""enabled"":false}]")]
        [TestCase(@"[{""name"":"" "",""enabled"":false}]")]
        [TestCase(@"[{""name"":""Excluded"",""enabled"":true},{""name"":""Excluded"",""enabled"":false}]")]
        public void should_fail_invalid_or_duplicate_array_membership(string array)
        {
            Assert.Throws<ImportListException>(() => new AniListParser(_settings).ParseResponse(Response(
                @"""customLists"":null,""customListEntries"":" + array)));
        }

        [TestCase(@"[{""name"":""Excluded"",""enabled"":""false""}]")]
        [TestCase(@"[{""name"":""Excluded"",""enabled"":0}]")]
        [TestCase(@"{""Excluded"":false}")]
        public void should_reject_wrong_json_token_types_in_typed_array(string invalid)
        {
            Assert.Throws<JsonException>(() => new AniListParser(_settings).ParseResponse(Response(
                @"""customLists"":null,""customListEntries"":" + invalid)));
        }

        [Test]
        public void should_keep_valid_legacy_maps_compatible_without_the_array_alias()
        {
            new AniListParser(_settings).ParseResponse(Response(@"""customLists"":{""Excluded"":false}"))
                .Should().ContainSingle();
        }

        [Test]
        public void should_reject_graphql_errors_even_with_valid_membership_alias()
        {
            Assert.Throws<ImportListException>(() => new AniListParser(_settings).ParseResponse(Response(
                @"""customLists"":null,""customListEntries"":[{""name"":""Excluded"",""enabled"":false}]", true)));
        }
    }
}
