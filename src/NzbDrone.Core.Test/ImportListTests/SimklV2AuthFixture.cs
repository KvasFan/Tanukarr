using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using FluentValidation;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Simkl;
using NzbDrone.Core.ImportLists.Simkl.User;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class SimklV2AuthFixture : CoreTest<SimklV2AuthService>
    {
        private readonly List<HttpRequest> _requests = new();
        private CacheManager _cache;
        private SimklUserSettings _settings;

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            _cache = new CacheManager();
            Mocker.SetConstant<ICacheManager>(_cache);
            _settings = new SimklUserSettings
            {
                ClientIdV2 = "fixture-client",
                AccessToken = "previous-access",
                RefreshToken = "previous-refresh",
                Expires = DateTime.UtcNow.AddDays(2),
                AuthUser = "42"
            };
        }

        private static Dictionary<string, string> Parameters(string value)
        {
            return value.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2))
                .ToDictionary(pair => Decode(pair[0]), pair => pair.Length > 1 ? Decode(pair[1]) : string.Empty);
        }

        private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

        private Dictionary<string, string> Start(string callback = "http://localhost:6970/oauth.html")
        {
            var response = JObject.FromObject(Subject.RequestAction("startOAuth", new Dictionary<string, string> { ["callbackUrl"] = callback }, _settings));
            var uri = new Uri((string)response["oauthUrl"]);
            uri.Scheme.Should().Be("https");
            uri.Host.Should().Be("simkl.com");
            uri.AbsolutePath.Should().Be("/oauth2/authorize");
            return Parameters(uri.Query);
        }

        private object Grant(string state, string issuer = "https://simkl.com")
        {
            return Subject.RequestAction("getOAuthToken", new Dictionary<string, string> { ["state"] = state, ["code"] = "fixture-code", ["iss"] = issuer }, _settings);
        }

        private static JObject Token()
        {
            return new JObject
            {
                ["access_token"] = "fixture-access",
                ["refresh_token"] = "fixture-refresh",
                ["expires_in"] = 3600,
                ["token_type"] = "Bearer",
                ["scope"] = "media:read"
            };
        }

        private void GivenResponses(params string[] bodies)
        {
            var queue = new Queue<string>(bodies);
            HttpResponse Read(HttpRequest request)
            {
                _requests.Add(request);
                return new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(queue.Dequeue()));
            }

            Mocker.GetMock<ISimklV2RequestService>().Setup(service => service.Execute(It.IsAny<HttpRequest>())).Returns((HttpRequest request) => Read(request));
            Mocker.GetMock<ISimklV2RequestService>().Setup(service => service.ExecuteOnce(It.IsAny<HttpRequest>())).Returns((HttpRequest request) => Read(request));
        }

        [Test]
        public void should_start_with_fixed_consent_host_s256_read_scope_random_state_and_registered_callback()
        {
            var first = Start();
            var second = Start();
            var session = _cache.GetCache<SimklV2OAuthSession>(typeof(SimklV2AuthService), SimklV2AuthService.SessionCacheName).Find(first["state"]);
            var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(session.Verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            first["response_type"].Should().Be("code");
            first["scope"].Should().Be("media:read");
            first["client_id"].Should().Be("fixture-client");
            first["redirect_uri"].Should().Be("http://localhost:6970/oauth.html");
            first["code_challenge_method"].Should().Be("S256");
            first["code_challenge"].Should().Be(challenge);
            first["state"].Should().NotBe(second["state"]).And.HaveLength(43);
            session.Verifier.Should().HaveLength(64);
            session.ExpiresUtc.Should().BeAfter(DateTime.UtcNow.AddMinutes(9));
            _requests.Should().BeEmpty();
            _settings.AccessToken.Should().Be("previous-access");
        }

        [Test]
        public void should_request_write_scope_only_after_explicit_watched_progress_opt_in()
        {
            _settings.SyncWatchedProgress = true;
            var start = Start();
            start["scope"].Should().Be("media:read media:write");
            var token = Token();
            token["scope"] = "media:read media:write";
            GivenResponses(token.ToString(), "{\"account\":{\"id\":123}}");
            var response = JObject.FromObject(Grant(start["state"]));
            response["scopeV2"].Value<string>().Should().Be("media:read media:write");
        }

        [Test]
        public void should_refuse_a_read_only_grant_when_watched_progress_was_requested()
        {
            _settings.SyncWatchedProgress = true;
            var start = Start();
            GivenResponses(Token().ToString());
            Action action = () => Grant(start["state"]);
            action.Should().Throw<ValidationException>();
            _settings.AccessToken.Should().Be("previous-access");
            _settings.ScopeV2.Should().BeNull();
            _requests.Should().ContainSingle();
        }

        [TestCase("http://example.com/oauth.html")]
        [TestCase("https://example.com/callback")]
        [TestCase("https://user@example.com/oauth.html")]
        [TestCase("https://example.com/oauth.html?unexpected=1")]
        [TestCase("https://example.com/oauth.html#fragment")]
        [TestCase("file:///oauth.html")]
        [TestCase(null)]
        public void should_reject_unsafe_or_mismatched_callback_urls_before_transport(string callback)
        {
            Action action = () => Start(callback);

            action.Should().Throw<ValidationException>().Which.Errors.Should().OnlyContain(error => error.PropertyName == "SignIn");
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_exchange_real_snake_case_json_with_form_post_and_confirm_account_without_mutating_settings()
        {
            var start = Start("https://tanukarr.example/prefix/oauth.html");
            GivenResponses(Token().ToString(), "{\"account\":{\"id\":123}} ");

            var response = JObject.FromObject(Grant(start["state"]));

            response["accessToken"].Value<string>().Should().Be("fixture-access");
            response["refreshToken"].Value<string>().Should().Be("fixture-refresh");
            response["authUser"].Value<string>().Should().Be("123");
            response["expires"].Value<DateTime>().Should().BeAfter(DateTime.UtcNow.AddMinutes(59));
            var request = _requests[0];
            request.Url.FullUri.Should().Be("https://api.simkl.com/oauth2/token");
            request.Method.Method.Should().Be("POST");
            request.Headers.ContentType.Should().Contain("application/x-www-form-urlencoded");
            request.ContentSummary.Should().BeNull();
            request.AllowAutoRedirect.Should().BeFalse();
            request.LogHttpError.Should().BeFalse();
            request.LogResponseContent.Should().BeFalse();
            var form = Parameters(Encoding.UTF8.GetString(request.ContentData));
            form["grant_type"].Should().Be("authorization_code");
            form["code"].Should().Be("fixture-code");
            form["redirect_uri"].Should().Be("https://tanukarr.example/prefix/oauth.html");
            form.Should().ContainKey("code_verifier").And.NotContainKey("client_secret");
            _requests[1].Url.Path.Should().Be("/users/settings");
            _requests[1].Headers["Authorization"].ToString().Should().Be("Bearer fixture-access");
            _settings.AccessToken.Should().Be("previous-access");
            _settings.RefreshToken.Should().Be("previous-refresh");
            _settings.AuthUser.Should().Be("42");
            Mocker.GetMock<ISimklV2RequestService>().Verify(service => service.ExecuteOnce(It.IsAny<HttpRequest>()), Times.Once());
            Mocker.GetMock<IImportListRepository>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_send_optional_confidential_client_secret_in_form_only()
        {
            _settings.ClientSecretV2 = "fixture-secret";
            var start = Start();
            GivenResponses(Token().ToString(), "{\"account\":{\"id\":123}}");

            Grant(start["state"]);

            Parameters(Encoding.UTF8.GetString(_requests[0].ContentData))["client_secret"].Should().Be("fixture-secret");
            _requests[0].Url.Query.Should().BeEmpty();
            _requests[0].ContentSummary.Should().BeNull();
        }

        [Test]
        public void should_share_sessions_across_service_instances_and_reject_replay_before_http()
        {
            var start = Start();
            GivenResponses(Token().ToString(), "{\"account\":{\"id\":123}}");
            var second = new SimklV2AuthService(Mocker.GetMock<ISimklV2RequestService>().Object, _cache);
            var query = new Dictionary<string, string> { ["state"] = start["state"], ["code"] = "fixture-code" };

            second.RequestAction("getOAuthToken", query, _settings);
            Action replay = () => Grant(start["state"]);

            replay.Should().Throw<ValidationException>();
            _requests.Count.Should().Be(2);
        }

        [TestCase("unknown-state")]
        [TestCase("wrong-client")]
        [TestCase("wrong-issuer")]
        [TestCase("cache-expired")]
        [TestCase("session-expired")]
        public void should_reject_unknown_foreign_or_expired_authorization_before_http(string failure)
        {
            var start = Start();
            var state = start["state"];
            var sessions = _cache.GetCache<SimklV2OAuthSession>(typeof(SimklV2AuthService), SimklV2AuthService.SessionCacheName);
            if (failure == "unknown-state")
            {
                state = "unknown-state";
            }
            else if (failure == "wrong-client")
            {
                _settings.ClientIdV2 = "another-client";
            }
            else if (failure == "cache-expired")
            {
                sessions.Set(state, sessions.Find(state), TimeSpan.FromSeconds(-1));
            }
            else if (failure == "session-expired")
            {
                sessions.Find(state).ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
            }

            Action action = () => Grant(state, failure == "wrong-issuer" ? "https://foreign.example" : "https://simkl.com");

            action.Should().Throw<ValidationException>();
            _requests.Should().BeEmpty();
            Mocker.GetMock<ISimklV2RequestService>().VerifyNoOtherCalls();
        }

        [TestCase("access_token", "")]
        [TestCase("refresh_token", "")]
        [TestCase("token_type", "Basic")]
        [TestCase("scope", "media:write")]
        [TestCase("expires_in", "0")]
        [TestCase("expires_in", "31536001")]
        public void should_reject_malformed_grants_consume_state_and_keep_prior_credentials(string field, string value)
        {
            var token = Token();
            token[field] = field == "expires_in" ? int.Parse(value) : value;
            var start = Start();
            GivenResponses(token.ToString());
            Action action = () => Grant(start["state"]);

            action.Should().Throw<ValidationException>();
            action.Should().Throw<ValidationException>();
            _requests.Should().ContainSingle();
            _settings.AccessToken.Should().Be("previous-access");
            _settings.RefreshToken.Should().Be("previous-refresh");
        }

        [TestCase("{\"account\":{\"id\":0}}")]
        [TestCase("{\"account\":{\"id\":\"123\"}}")]
        [TestCase("{\"user\":{\"id\":123}}")]
        public void should_reject_unconfirmed_account_identity_without_changing_the_saved_settings(string profile)
        {
            var start = Start();
            GivenResponses(Token().ToString(), profile);
            Action action = () => Grant(start["state"]);

            action.Should().Throw<ValidationException>();
            _settings.AuthUser.Should().Be("42");
            _settings.AccessToken.Should().Be("previous-access");
        }

        [Test]
        public void should_not_disclose_oauth_response_bodies_in_validation_errors()
        {
            var start = Start();
            GivenResponses("{\"error\":\"invalid_grant\",\"message\":\"PRIVATE-OAUTH-BODY fixture-code fixture-secret\"}");
            Action action = () => Grant(start["state"]);

            var failure = action.Should().Throw<ValidationException>().Which;

            failure.Message.Should().NotContain("PRIVATE-OAUTH-BODY").And.NotContain("fixture-code").And.NotContain("fixture-secret");
            failure.Errors.Should().OnlyContain(error => error.PropertyName == "SignIn");
        }

        [Test]
        public void should_refresh_only_in_memory_after_a_fully_validated_readable_grant()
        {
            _settings.Expires = DateTime.UtcNow.AddMinutes(-1);
            GivenResponses(Token().ToString());

            Subject.RefreshIfNeeded(_settings).Should().BeTrue();

            _settings.AccessToken.Should().Be("fixture-access");
            _settings.RefreshToken.Should().Be("fixture-refresh");
            _settings.Expires.Should().BeAfter(DateTime.UtcNow.AddMinutes(59));
            _settings.AuthUser.Should().Be("42");
            var form = Parameters(Encoding.UTF8.GetString(_requests[0].ContentData));
            form["grant_type"].Should().Be("refresh_token");
            form["refresh_token"].Should().Be("previous-refresh");
            Mocker.GetMock<ISimklV2RequestService>().Verify(service => service.ExecuteOnce(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IImportListRepository>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_preserve_every_prior_token_value_when_refresh_validation_fails()
        {
            var expiry = DateTime.UtcNow.AddMinutes(-1);
            _settings.Expires = expiry;
            var token = Token();
            token["scope"] = "media:write";
            GivenResponses(token.ToString());
            Action action = () => Subject.RefreshIfNeeded(_settings);

            action.Should().Throw<ValidationException>();
            _settings.AccessToken.Should().Be("previous-access");
            _settings.RefreshToken.Should().Be("previous-refresh");
            _settings.Expires.Should().Be(expiry);
            _settings.AuthUser.Should().Be("42");
        }

        [Test]
        public void should_not_refresh_an_unexpired_access_token()
        {
            Subject.RefreshIfNeeded(_settings).Should().BeFalse();

            Mocker.GetMock<ISimklV2RequestService>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_handoff_a_test_renewal_to_fresh_saved_settings_without_repeating_the_old_refresh_grant()
        {
            var expiry = DateTime.UtcNow.AddMinutes(-1);
            _settings.Expires = expiry;
            var freshSettings = new SimklUserSettings
            {
                ClientIdV2 = _settings.ClientIdV2,
                AuthUser = _settings.AuthUser,
                AccessToken = _settings.AccessToken,
                RefreshToken = _settings.RefreshToken,
                Expires = new DateTime(expiry.Ticks - (expiry.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc)
            };
            GivenResponses(Token().ToString());
            Subject.RefreshIfNeeded(_settings).Should().BeTrue();
            var freshService = new SimklV2AuthService(Mocker.GetMock<ISimklV2RequestService>().Object, _cache);

            freshService.RefreshIfNeeded(freshSettings).Should().BeTrue();

            _requests.Should().ContainSingle();
            freshSettings.AccessToken.Should().Be("fixture-access");
            freshSettings.RefreshToken.Should().Be("fixture-refresh");
            freshSettings.Expires.Should().Be(_settings.Expires);
            Mocker.GetMock<IImportListRepository>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_not_reuse_a_renewed_grant_after_the_credential_context_changes()
        {
            var expiry = DateTime.UtcNow.AddMinutes(-1);
            _settings.Expires = expiry;
            GivenResponses(Token().ToString(), Token().ToString());
            Subject.RefreshIfNeeded(_settings);
            var changed = new SimklUserSettings
            {
                ClientIdV2 = "another-client",
                AuthUser = "42",
                AccessToken = "previous-access",
                RefreshToken = "previous-refresh",
                Expires = expiry
            };

            Subject.RefreshIfNeeded(changed).Should().BeTrue();

            _requests.Count.Should().Be(2);
            Parameters(Encoding.UTF8.GetString(_requests.Last().ContentData))["client_id"].Should().Be("another-client");
        }
    }
}
