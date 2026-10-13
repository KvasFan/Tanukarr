using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using FluentAssertions;
using FluentValidation;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.ImportLists.MyAnimeList;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class MyAnimeListFixture : CoreTest<MyAnimeListImport>
    {
        private readonly List<HttpRequest> _requests = new();
        private MyAnimeListSettings _settings;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _settings = new MyAnimeListSettings { AccessToken = "generated-access", RefreshToken = "generated-refresh", Expires = DateTime.UtcNow.AddDays(2) };
            Subject.Definition = new ImportListDefinition { Id = 10, Name = "MAL fixture", Settings = _settings };
        }

        private static JObject Row(int id, string status = "watching", string release = "finished_airing", params string[] tags)
        {
            return new JObject
            {
                ["node"] = new JObject { ["id"] = id, ["title"] = "Same title", ["status"] = release },
                ["list_status"] = new JObject { ["status"] = status, ["score"] = 8, ["tags"] = new JArray(tags), ["is_rewatching"] = false }
            };
        }

        private static string Page(string next, params JObject[] rows)
        {
            return new JObject { ["data"] = new JArray(rows), ["paging"] = new JObject { ["next"] = next } }.ToString();
        }

        private static ImportListResponse Response(string content)
        {
            var request = new ImportListRequest(new HttpRequest("https://api.myanimelist.net/v2/users/@me/animelist"));
            return new ImportListResponse(request, new HttpResponse(request.HttpRequest, new HttpHeader(), Encoding.UTF8.GetBytes(content)));
        }

        private void GivenResponses(params string[] bodies)
        {
            var pages = new Queue<string>(bodies);
            Mocker.GetMock<IMyAnimeListRequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>()))
                .Returns((HttpRequest request) =>
                {
                    _requests.Add(request);
                    return new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(pages.Dequeue()));
                });
        }

        [TestCase("watching", MyAnimeListStatus.Watching)]
        [TestCase("completed", MyAnimeListStatus.Completed)]
        [TestCase("on_hold", MyAnimeListStatus.OnHold)]
        [TestCase("dropped", MyAnimeListStatus.Dropped)]
        [TestCase("plan_to_watch", MyAnimeListStatus.PlanToWatch)]
        public void should_read_real_snake_case_json_and_keep_account_status_separate_from_release_status(string status, MyAnimeListStatus selected)
        {
            _settings.ListStatuses = new[] { (int)selected };
            var result = new MyAnimeListParser(_settings).ParseResponse(Response(Page(null, Row(21, status))));
            result.Should().ContainSingle().Which.MalId.Should().Be(21);
            Json.Deserialize<MyAnimeListResponse>(Page(null, Row(21, status))).Animes.Single().ListStatus.IsRewatching.Should().BeFalse();
        }

        [Test]
        public void should_categorize_an_active_rewatch_as_watching_according_to_the_official_contract()
        {
            _settings.ListStatuses = new[] { 1 };
            var row = Row(21, "completed");
            row["list_status"]["is_rewatching"] = true;
            new MyAnimeListParser(_settings).ParseResponse(Response(Page(null, row))).Should().ContainSingle();
            Json.Deserialize<MyAnimeListResponse>(Page(null, row)).Animes.Single().ListStatus.IsRewatching.Should().BeTrue();
            _settings.ListStatuses = new[] { 2 };
            new MyAnimeListParser(_settings).ParseResponse(Response(Page(null, row))).Should().BeEmpty();
        }

        [Test]
        public void should_union_multiple_statuses_and_include_tags_then_subtract_every_excluded_tag()
        {
            _settings.ListStatuses = new[] { 1, 5 };
            _settings.IncludedTags = new[] { "Seasonal", "Pinned" };
            _settings.ExcludedTags = new[] { "Never", "Skip" };
            var result = new MyAnimeListParser(_settings).ParseResponse(Response(Page(null,
                Row(21, "watching", "finished_airing", "Seasonal"),
                Row(22, "plan_to_watch", "currently_airing", "Pinned"),
                Row(23, "watching", "finished_airing", "Seasonal", "Never"),
                Row(24, "completed", "finished_airing", "Seasonal"),
                Row(25, "watching"))));
            result.Select(item => item.MalId).Should().Equal(21, 22);
        }

        [Test]
        public void should_keep_literal_tag_names_and_exclude_without_any_include_selection()
        {
            _settings.ExcludedTags = new[] { "Never" };
            var result = new MyAnimeListParser(_settings).ParseResponse(Response(Page(null,
                Row(21, "watching", "finished_airing", "never"),
                Row(22, "watching", "finished_airing", "Never"),
                Row(23, "watching", "finished_airing", " Never "))));
            result.Select(item => item.MalId).Should().Equal(21, 23);
        }

        [TestCase("finished_airing")]
        [TestCase("currently_airing")]
        [TestCase("not_yet_aired")]
        public void should_apply_release_filters_independently_of_watching(string release)
        {
            _settings.ImportFinished = false;
            _settings.ImportAiring = false;
            _settings.ImportUnreleased = false;
            new MyAnimeListParser(_settings).ParseResponse(Response(Page(null, Row(21, "watching", release)))).Should().BeEmpty();
        }

        [Test]
        public void should_apply_the_minimum_account_score_without_changing_tag_or_status_selection()
        {
            _settings.MinimumScore = 8;
            var below = Row(21);
            below["list_status"]["score"] = 7;
            var result = new MyAnimeListParser(_settings).ParseResponse(Response(Page(null, below, Row(22))));
            result.Select(item => item.MalId).Should().Equal(22);
        }

        [TestCase("{\"data\":[],\"paging\":null}")]
        [TestCase("{\"data\":null,\"paging\":{}}")]
        [TestCase("{\"error\":\"invalid_token\"}")]
        [TestCase("null")]
        [TestCase("invalid-json")]
        public void should_reject_incomplete_json_instead_of_replacing_the_previous_list(string content)
        {
            Action act = () => new MyAnimeListParser(_settings).ParseResponse(Response(content));
            act.Should().Throw<ImportListException>();
        }

        [TestCase("tags")]
        [TestCase("score")]
        [TestCase("status")]
        public void should_reject_missing_requested_membership_fields(string field)
        {
            _settings.ExcludedTags = new[] { "Never" };
            var row = Row(21);
            ((JObject)row["list_status"]).Remove(field);
            Action act = () => new MyAnimeListParser(_settings).ParseResponse(Response(Page(null, row)));
            act.Should().Throw<ImportListException>();
        }

        [Test]
        public void should_fetch_all_pages_even_when_the_first_page_matches_no_local_filters()
        {
            _settings.IncludedTags = new[] { "Pinned" };
            GivenResponses(Page("https://api.myanimelist.net/v2/users/@me/animelist?offset=1", Row(21)),
                Page(null, Row(22, "watching", "finished_airing", "Pinned"), Row(23, "watching", "finished_airing", "Pinned")));
            var result = Subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Select(item => item.MalId).Should().Equal(22, 23);
            _requests.Should().HaveCount(2);
            _requests[1].Url.FullUri.Should().Contain("offset=1");
            _requests.Should().OnlyContain(request => request.Headers["Authorization"] == "Bearer generated-access");
        }

        [Test]
        public void should_import_more_than_the_old_thousand_entry_truncation_limit()
        {
            GivenResponses(Page("https://api.myanimelist.net/v2/users/@me/animelist?offset=1000", Enumerable.Range(1, 1000).Select(id => Row(id)).ToArray()),
                Page(null, Row(1001)));
            var result = Subject.Fetch();
            result.AnyFailure.Should().BeFalse();
            result.Series.Should().HaveCount(1001);
            result.Series.Last().MalId.Should().Be(1001);
        }

        [TestCase("https://evil.invalid/v2/users/@me/animelist?offset=1")]
        [TestCase("https://api.myanimelist.net/v2/users/other/animelist?offset=1")]
        [TestCase("http://api.myanimelist.net/v2/users/@me/animelist?offset=1")]
        [TestCase("https://api.myanimelist.net/v2/users/@me/animelist?offset=0")]
        [TestCase("https://api.myanimelist.net/v2/users/@me/animelist?offset=2")]
        [TestCase("https://api.myanimelist.net/v2/users/@me/animelist?offset=1&offset=1")]
        public void should_reject_untrusted_repeated_or_skipped_pagination_without_forwarding_credentials(string next)
        {
            GivenResponses(Page(next, Row(21)));
            var result = Subject.Fetch();
            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            _requests.Should().ContainSingle();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_discard_partial_pages_and_deduplicate_by_native_mal_id_not_title()
        {
            GivenResponses(Page("https://api.myanimelist.net/v2/users/@me/animelist?offset=1", Row(21)), "{\"error\":\"incomplete\"}");
            Subject.Fetch().Series.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
            GivenResponses(Page(null, Row(21), Row(22), Row(21)));
            Subject.Fetch().Series.Select(item => item.MalId).Should().Equal(21, 22);
        }

        [Test]
        public void should_apply_a_later_tag_exclusion_to_an_earlier_duplicate_id()
        {
            _settings.IncludedTags = new[] { "Pinned" };
            _settings.ExcludedTags = new[] { "Never" };
            GivenResponses(Page("https://api.myanimelist.net/v2/users/@me/animelist?offset=1", Row(21, "watching", "finished_airing", "Pinned")),
                Page(null, Row(21, "watching", "finished_airing", "Pinned", "Never")));
            Subject.Fetch().Series.Should().BeEmpty();
        }

        [Test]
        public void should_use_client_id_only_for_a_public_username_without_a_bearer_token()
        {
            _settings.AccessToken = null;
            _settings.Username = "Public-user";
            _settings.ClientId = "generated-client-id";
            _settings.Validate().IsValid.Should().BeTrue();
            var request = ((MyAnimeListRequestGenerator)Subject.GetRequestGenerator()).GetRequest(0).HttpRequest;
            request.Url.FullUri.Should().Contain("users/Public-user/animelist");
            request.Headers["X-MAL-CLIENT-ID"].Should().Be("generated-client-id");
            request.Headers.ContainsKey("Authorization").Should().BeFalse();
        }

        [Test]
        public void should_normalize_legacy_status_once_with_both_actual_settings_serializers()
        {
            const string old = "{\"listStatus\":5,\"accessToken\":\"generated-access\"}";
            foreach (var settings in new[] { Json.Deserialize<MyAnimeListSettings>(old), STJson.Deserialize<MyAnimeListSettings>(old) })
            {
                settings.ListStatuses.Should().Equal(5);
                settings.ListStatus.Should().Be(0);
                settings.ListStatuses = new[] { 0 };
                settings.Validate();
                settings.ListStatuses.Should().Equal(0);
            }
        }

        [Test]
        public void should_exchange_single_use_state_with_pkce_at_the_official_token_endpoint()
        {
            _settings.ClientId = "generated-client-id";
            var start = JObject.FromObject(Subject.RequestAction("startOAuth", new Dictionary<string, string> { ["callbackUrl"] = "http://localhost:6970/oauth.html" }));
            var uri = new Uri(start["oauthUrl"].Value<string>());
            var query = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
            query["code_challenge"].Length.Should().BeInRange(43, 128);
            query["code_challenge_method"].Should().Be("plain");
            query["redirect_uri"].Should().Be("http://localhost:6970/oauth.html");
            GivenResponses("{\"token_type\":\"Bearer\",\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
            var callback = new Dictionary<string, string> { ["state"] = query["state"], ["code"] = "generated+code=with/slashes" };
            var token = JObject.FromObject(Subject.RequestAction("getOAuthToken", callback));
            token["accessToken"].Value<string>().Should().Be("new-access");
            _requests.Single().Url.FullUri.Should().Be(MyAnimeListImport.TokenUrl);
            Encoding.UTF8.GetString(_requests.Single().ContentData).Should().Contain("code_verifier=" + query["code_challenge"]);
            Action replay = () => Subject.RequestAction("getOAuthToken", callback);
            replay.Should().Throw<ValidationException>();
            _requests.Should().ContainSingle();
        }

        [TestCase("missing")]
        [TestCase("wrongClient")]
        [TestCase("expired")]
        public void should_reject_unknown_expired_or_wrong_client_oauth_state_without_sending_a_code(string kind)
        {
            _settings.ClientId = "generated-client-id";
            var start = JObject.FromObject(Subject.RequestAction("startOAuth", new Dictionary<string, string> { ["callbackUrl"] = "http://localhost:6970/oauth.html" }));
            var state = new Uri(start["oauthUrl"].Value<string>()).Query.TrimStart('?').Split('&')
                .Select(part => part.Split('=', 2)).Single(pair => pair[0] == "state")[1];
            if (kind == "missing")
            {
                state = "unrecognized-state";
            }
            else if (kind == "wrongClient")
            {
                _settings.ClientId = "other-client";
            }
            else
            {
                // Expire the real item held by this provider. Clearing the manager's
                // registry does not invalidate caches a service already holds.
                var cache = typeof(MyAnimeListImport).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Subject);
                var entries = (IDictionary)cache.GetType().GetField("_store", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cache);
                var item = entries[state];
                item.GetType().GetProperty("ExpiryTime").SetValue(item, DateTime.UtcNow.AddMinutes(-1));
            }

            Action act = () => Subject.RequestAction("getOAuthToken", new Dictionary<string, string> { ["state"] = state, ["code"] = "generated-code" });
            act.Should().Throw<ValidationException>();
            Mocker.GetMock<IMyAnimeListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [TestCase("ftp://localhost/oauth.html")]
        [TestCase("http://user:password@localhost/oauth.html")]
        [TestCase("http://localhost/oauth.html?redirect=https://evil.invalid")]
        [TestCase("http://localhost/other.html")]
        public void should_reject_invalid_oauth_callback_paths(string callback)
        {
            _settings.ClientId = "generated-client-id";
            Action act = () => Subject.RequestAction("startOAuth", new Dictionary<string, string> { ["callbackUrl"] = callback });
            act.Should().Throw<ValidationException>();
        }

        [Test]
        public void should_keep_an_existing_legacy_token_until_refresh_requires_reauthentication()
        {
            _settings.Expires = DateTime.UtcNow.AddMinutes(-1);
            Subject.Fetch().AnyFailure.Should().BeTrue();
            _settings.AccessToken.Should().Be("generated-access");
            _settings.RefreshToken.Should().Be("generated-refresh");
            Mocker.GetMock<IMyAnimeListRequestService>().Verify(service => service.Execute(It.IsAny<HttpRequest>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_refresh_directly_and_preserve_the_existing_refresh_token_when_not_rotated()
        {
            _settings.ClientId = "generated-client-id";
            _settings.Expires = DateTime.UtcNow.AddMinutes(-1);
            GivenResponses("{\"token_type\":\"Bearer\",\"access_token\":\"new-access\",\"expires_in\":3600}", Page(null, Row(21)));
            Subject.Fetch().AnyFailure.Should().BeFalse();
            _settings.RefreshToken.Should().Be("generated-refresh");
            _requests[0].Url.FullUri.Should().Be(MyAnimeListImport.TokenUrl);
            _requests[1].Headers["Authorization"].Should().Be("Bearer new-access");
            Mocker.GetMock<IImportListRepository>().Verify(repository => repository.UpdateSettings(It.IsAny<ImportListDefinition>()), Times.Once());
        }

        [Test]
        public void should_refresh_an_unsaved_test_without_persisting_its_filters_or_credentials()
        {
            _settings.ClientId = "generated-client-id";
            _settings.Expires = DateTime.UtcNow.AddMinutes(-1);
            _settings.IncludedTags = new[] { "Unsaved tag" };
            GivenResponses("{\"token_type\":\"Bearer\",\"access_token\":\"test-only-access\",\"expires_in\":3600}", Page(null, Row(21)));
            Subject.Test().IsValid.Should().BeTrue();
            Mocker.GetMock<IImportListRepository>().Verify(repository => repository.UpdateSettings(It.IsAny<ImportListDefinition>()), Times.Never());
        }
    }
}
