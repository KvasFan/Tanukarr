using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.MDBList;
using NzbDrone.Core.ImportLists.Tmdb.List;
using NzbDrone.Core.ImportLists.Trakt.List;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ProviderListSettingsFixture
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void should_migrate_saved_tmdb_selections_after_all_json_properties(bool systemTextJson, bool legacyFirst)
        {
            var fields = legacyFirst ? "\"accountListId\":42,\"includedListIds\":[\"1\"]," : "\"includedListIds\":[\"1\"],\"accountListId\":42,";
            var json = "{" + fields + "\"authToken\":\"fixture-token\",\"excludedListIds\":[\"3\"]}";
            var settings = systemTextJson ? STJson.Deserialize<TmdbListSettings>(json) : Json.Deserialize<TmdbListSettings>(json);

            settings.IncludedListIds.Should().Equal("1", "42");
            settings.AccountListId.Should().BeNull();
            settings.ListId.Should().BeNull();
            settings.ExcludedListIds.Should().Equal("3");
            settings.Validate().IsValid.Should().BeTrue();
            settings.IncludedListIds = new[] { "1" };
            var saved = systemTextJson ? STJson.ToJson(settings) : Json.ToJson(settings);
            var reloaded = systemTextJson ? STJson.Deserialize<TmdbListSettings>(saved) : Json.Deserialize<TmdbListSettings>(saved);
            reloaded.IncludedListIds.Should().Equal("1");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void should_migrate_saved_trakt_names_into_slugs_without_a_hidden_filter(bool systemTextJson, bool legacyFirst)
        {
            var fields = legacyFirst ? "\"listname\":\"My List\",\"includedLists\":[\"modern\"]," : "\"includedLists\":[\"modern\"],\"listname\":\"My List\",";
            var json = "{" + fields + "\"username\":\"fixture\",\"excludedLists\":[\"excluded\"]}";
            var settings = systemTextJson ? STJson.Deserialize<TraktListSettings>(json) : Json.Deserialize<TraktListSettings>(json);

            settings.IncludedLists.Should().Equal("modern", "my-list");
            settings.Listname.Should().BeEmpty();
            settings.ExcludedLists.Should().Equal("excluded");
            settings.IncludedLists = new[] { "modern" };
            var saved = systemTextJson ? STJson.ToJson(settings) : Json.ToJson(settings);
            var reloaded = systemTextJson ? STJson.Deserialize<TraktListSettings>(saved) : Json.Deserialize<TraktListSettings>(saved);
            reloaded.IncludedLists.Should().Equal("modern");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void should_migrate_saved_mdblist_urls_without_a_hidden_filter(bool systemTextJson, bool legacyFirst)
        {
            const string url = "https://mdblist.com/lists/fixture/legacy";
            var legacy = "\"listUrl\":\" " + url + " \",";
            const string modern = "\"includedListUrls\":[\"https://mdblist.com/lists/fixture/modern\"],";
            var json = "{" + (legacyFirst ? legacy + modern : modern + legacy) + "\"apiKey\":\"fixture-key\"}";
            var settings = systemTextJson ? STJson.Deserialize<MDBListSettings>(json) : Json.Deserialize<MDBListSettings>(json);

            settings.IncludedListUrls.Should().Equal("https://mdblist.com/lists/fixture/modern", url);
            settings.ListUrl.Should().BeEmpty();
            settings.Validate().IsValid.Should().BeTrue();
            settings.IncludedListUrls = settings.IncludedListUrls.Take(1).ToArray();
            var saved = systemTextJson ? STJson.ToJson(settings) : Json.ToJson(settings);
            var reloaded = systemTextJson ? STJson.Deserialize<MDBListSettings>(saved) : Json.Deserialize<MDBListSettings>(saved);
            reloaded.IncludedListUrls.Should().ContainSingle().Which.Should().Be("https://mdblist.com/lists/fixture/modern");
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("+1")]
        [TestCase("one")]
        [TestCase("1/2")]
        public void should_reject_nonpositive_or_nonliteral_tmdb_ids(string id)
        {
            new TmdbListSettings { AuthToken = "fixture", IncludedListIds = new[] { "1" }, ExcludedListIds = new[] { id } }.Validate().IsValid.Should().BeFalse();
        }

        [TestCase("../other")]
        [TestCase("list?token=secret")]
        [TestCase("https://trakt.tv/lists/1")]
        public void should_reject_trakt_identifiers_that_can_change_the_endpoint(string slug)
        {
            TraktListSettings.IsValidList(slug).Should().BeFalse();
        }

        [TestCase("http://mdblist.com/lists/user/list")]
        [TestCase("https://example.com/lists/user/list")]
        [TestCase("https://mdblist.com.example.com/lists/user/list")]
        [TestCase("https://user@mdblist.com/lists/user/list")]
        [TestCase("https://mdblist.com:444/lists/user/list")]
        [TestCase("https://mdblist.com/lists/user/list?apikey=secret")]
        [TestCase("https://mdblist.com/lists/user/list#fragment")]
        [TestCase("https://mdblist.com/lists/user/%2fother")]
        public void should_reject_mdblist_urls_that_can_leak_credentials_or_change_the_endpoint(string url)
        {
            MDBListSettings.GetListPath(url).Should().BeNull();
            new MDBListSettings { ApiKey = "fixture", IncludedListUrls = new[] { "https://mdblist.com/lists/user/list" }, ExcludedListUrls = new[] { url } }.Validate().IsValid.Should().BeFalse();
        }

        [Test]
        public void should_normalize_legacy_fields_assigned_by_the_provider_api_before_validation()
        {
            var tmdb = new TmdbListSettings { AuthToken = "fixture", ListId = 42 };
            tmdb.Validate().IsValid.Should().BeTrue();
            tmdb.IncludedListIds.Should().Equal("42");
            tmdb.ListId.Should().BeNull();
            var trakt = new TraktListSettings
            {
                Username = "fixture",
                AccessToken = "fixture",
                RefreshToken = "fixture",
                Expires = DateTime.UtcNow.AddDays(30),
                Listname = "My List"
            };
            trakt.Validate().IsValid.Should().BeTrue();
            trakt.IncludedLists.Should().Equal("my-list");
            trakt.Listname.Should().BeEmpty();
            var mdb = new MDBListSettings { ApiKey = "fixture", ListUrl = "https://mdblist.com/lists/fixture/list" };
            mdb.Validate().IsValid.Should().BeTrue();
            mdb.IncludedListUrls.Should().ContainSingle();
            mdb.ListUrl.Should().BeEmpty();
        }

        [Test]
        public void should_require_included_sources_instead_of_inventing_an_all_lists_universe()
        {
            new TmdbListSettings { AuthToken = "fixture" }.Validate().IsValid.Should().BeFalse();
            new MDBListSettings { ApiKey = "fixture" }.Validate().IsValid.Should().BeFalse();
            new TraktListSettings
            {
                Username = "fixture",
                AccessToken = "fixture",
                RefreshToken = "fixture",
                Expires = DateTime.UtcNow.AddDays(30)
            }.Validate().IsValid.Should().BeFalse();
        }
    }
}
