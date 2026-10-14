using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Simkl.User;

namespace NzbDrone.Core.ImportLists.Simkl
{
    public interface ISimklV2AuthService
    {
        object RequestAction(string action, IDictionary<string, string> query, SimklUserSettings settings);
        bool RefreshIfNeeded(SimklUserSettings settings);
    }

    public class SimklV2AuthService : ISimklV2AuthService
    {
        internal const string SessionCacheName = "OAuthV2Sessions";
        private const string AuthorizeUrl = "https://simkl.com/oauth2/authorize";
        private const string TokenUrl = "https://api.simkl.com/oauth2/token";
        private static readonly object SessionLock = new();
        private static readonly object RenewalLock = new();
        private readonly ICached<SimklV2OAuthSession> _sessions;
        private readonly ICached<SimklV2Token> _renewed;
        private readonly ISimklV2RequestService _requests;

        public SimklV2AuthService(ISimklV2RequestService requests, ICacheManager cacheManager)
        {
            _requests = requests;
            _sessions = cacheManager.GetCache<SimklV2OAuthSession>(typeof(SimklV2AuthService), SessionCacheName);
            _renewed = cacheManager.GetCache<SimklV2Token>(typeof(SimklV2AuthService), "RenewedGrants");
        }

        public object RequestAction(string action, IDictionary<string, string> query, SimklUserSettings settings)
        {
            try
            {
                return OAuthAction(action, query, settings);
            }
            catch (ValidationException)
            {
                throw;
            }
            catch (Exception)
            {
                throw Failure("Simkl AUTH V2 authentication is unavailable. Check your registered client and callback URL, then authenticate again.");
            }
        }

        public bool RefreshIfNeeded(SimklUserSettings settings)
        {
            try
            {
                var context = JsonConvert.SerializeObject(new
                {
                    client = settings.ClientIdV2?.Trim(),
                    secret = string.IsNullOrEmpty(settings.ClientSecretV2) ? null : settings.ClientSecretV2,
                    settings.AuthUser,
                    settings.BaseUrl,
                    settings.AccessToken,
                    settings.RefreshToken
                });
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
                lock (RenewalLock)
                {
                    var token = _renewed.Find(fingerprint);
                    if (token == null || token.ExpiresUtc <= DateTime.UtcNow)
                    {
                        if (IsToken(settings.AccessToken) && settings.Expires >= DateTime.UtcNow.AddMinutes(5))
                        {
                            return false;
                        }

                        ValidateClient(settings);
                        if (!IsToken(settings.RefreshToken))
                        {
                            throw Failure("This Simkl AUTH V2 token needs renewal. Authenticate again with your registered AUTH V2 client.");
                        }

                        token = ExchangeToken(settings, new Dictionary<string, string>
                        {
                            ["grant_type"] = "refresh_token",
                            ["refresh_token"] = settings.RefreshToken
                        });
                        token.ExpiresUtc = DateTime.UtcNow.AddSeconds(token.ExpiresIn);
                        _renewed.ClearExpired();
                        _renewed.Set(fingerprint, token, TimeSpan.FromMinutes(10));
                    }

                    settings.AccessToken = token.AccessToken;
                    settings.RefreshToken = token.RefreshToken;
                    settings.Expires = token.ExpiresUtc;
                    settings.ScopeV2 = token.Scope;
                }

                return true;
            }
            catch (ValidationException)
            {
                throw;
            }
            catch (Exception)
            {
                throw Failure("Simkl AUTH V2 token renewal failed. The saved credentials were retained; authenticate again or retry when the provider is available.");
            }
        }

        private object OAuthAction(string action, IDictionary<string, string> query, SimklUserSettings settings)
        {
            if (action == "startOAuth")
            {
                ValidateClient(settings);
                if (query == null || !query.TryGetValue("callbackUrl", out var callback) ||
                    !Uri.TryCreate(callback, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "https" && (uri.Scheme != "http" || !uri.IsLoopback)) ||
                    uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
                    !uri.AbsolutePath.EndsWith("/oauth.html", StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(callback) > 1024)
                {
                    throw Failure("Simkl AUTH V2 requires an HTTPS OAuth callback, or loopback HTTP, ending in /oauth.html. Register this exact URL in your Simkl app.");
                }

                var state = Base64Url(RandomNumberGenerator.GetBytes(32));
                var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
                var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
                _sessions.ClearExpired();
                _sessions.Set(
                    state,
                    new SimklV2OAuthSession
                    {
                        ClientId = settings.ClientIdV2.Trim(),
                        RedirectUri = callback,
                        Verifier = verifier,
                        ExpiresUtc = DateTime.UtcNow.AddMinutes(10)
                    },
                    TimeSpan.FromMinutes(10));
                var request = new HttpRequestBuilder(AuthorizeUrl)
                    .AddQueryParam("response_type", "code").AddQueryParam("client_id", settings.ClientIdV2.Trim())
                    .AddQueryParam("redirect_uri", callback).AddQueryParam("state", state)
                    .AddQueryParam("scope", settings.SyncWatchedProgress ? "media:read media:write" : "media:read")
                    .AddQueryParam("code_challenge", challenge).AddQueryParam("code_challenge_method", "S256").Build();
                return new { oauthUrl = request.Url.FullUri };
            }

            if (action == "getOAuthToken")
            {
                ValidateClient(settings);
                if (query == null || !query.TryGetValue("state", out var state) || !IsToken(state) ||
                    !query.TryGetValue("code", out var code) || !IsToken(code) ||
                    (query.TryGetValue("iss", out var issuer) && issuer != "https://simkl.com"))
                {
                    throw Failure("Simkl AUTH V2 returned an invalid code, state or issuer. Authenticate again.");
                }

                SimklV2OAuthSession session;
                lock (SessionLock)
                {
                    session = _sessions.Find(state);
                    if (session == null || session.ExpiresUtc <= DateTime.UtcNow || session.ClientId != settings.ClientIdV2.Trim())
                    {
                        throw Failure("Simkl AUTH V2 authentication expired or belongs to another client. Authenticate again.");
                    }

                    // A V2 code is consumed by its first exchange, including failures.
                    _sessions.Remove(state);
                }

                var token = ExchangeToken(settings, new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = session.RedirectUri,
                    ["code_verifier"] = session.Verifier
                });
                var profileSettings = new SimklUserSettings
                {
                    BaseUrl = settings.BaseUrl,
                    ClientIdV2 = settings.ClientIdV2,
                    AccessToken = token.AccessToken
                };
                var profile = _requests.Execute(SimklV2RequestBuilder.Build("users/settings", profileSettings));
                var accountId = profile.StatusCode == HttpStatusCode.OK ? JObject.Parse(profile.Content)["account"]?["id"] : null;
                if (accountId?.Type != JTokenType.Integer || !int.TryParse((string)accountId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                {
                    throw Failure("Simkl AUTH V2 did not confirm an authenticated account. Authenticate again.");
                }

                return new
                {
                    accessToken = token.AccessToken,
                    refreshToken = token.RefreshToken,
                    scopeV2 = token.Scope,
                    expires = DateTime.UtcNow.AddSeconds(token.ExpiresIn),
                    authUser = id.ToString(CultureInfo.InvariantCulture)
                };
            }

            return new { };
        }

        private SimklV2Token ExchangeToken(SimklUserSettings settings, IDictionary<string, string> parameters)
        {
            var builder = new HttpRequestBuilder(TokenUrl).Post().Accept(HttpAccept.Json).AddFormParameter("client_id", settings.ClientIdV2.Trim());
            if (!string.IsNullOrWhiteSpace(settings.ClientSecretV2))
            {
                builder.AddFormParameter("client_secret", settings.ClientSecretV2);
            }

            foreach (var parameter in parameters)
            {
                if (Encoding.UTF8.GetByteCount(parameter.Value) > 1024)
                {
                    throw Failure("Simkl AUTH V2 returned an invalid authentication parameter. Authenticate again.");
                }

                builder.AddFormParameter(parameter.Key, parameter.Value);
            }

            var request = builder.Build();
            request.ContentSummary = null;
            request.LogHttpError = false;
            request.LogResponseContent = false;
            request.SuppressHttpError = true;
            request.AllowAutoRedirect = false;
            var response = parameters["grant_type"] == "authorization_code" ? _requests.ExecuteOnce(request) : _requests.Execute(request);
            var token = response.StatusCode == HttpStatusCode.OK ? JsonConvert.DeserializeObject<SimklV2Token>(response.Content) : null;
            if (token == null || !IsToken(token.AccessToken) || !IsToken(token.RefreshToken) ||
                !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                token.ExpiresIn <= 0 || token.ExpiresIn > 31536000 ||
                token.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("media:read", StringComparer.Ordinal) != true ||
                (settings.SyncWatchedProgress && token.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("media:write", StringComparer.Ordinal) != true))
            {
                throw Failure("Simkl AUTH V2 authentication did not return a valid readable grant. Verify your registered client, secret and callback URL, then authenticate again.");
            }

            return token;
        }

        private static void ValidateClient(SimklUserSettings settings)
        {
            if (!IsToken(settings?.ClientIdV2) || Encoding.UTF8.GetByteCount(settings.ClientIdV2) > 1024 ||
                (!string.IsNullOrEmpty(settings.ClientSecretV2) && (!IsToken(settings.ClientSecretV2) || Encoding.UTF8.GetByteCount(settings.ClientSecretV2) > 1024)))
            {
                throw Failure("Enter your registered Simkl AUTH V2 Client ID and optional client secret before authenticating.");
            }
        }

        private static bool IsToken(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 1024 && !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character));
        }

        private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static ValidationException Failure(string message) => new(new[] { new ValidationFailure("SignIn", message) });

        private sealed class SimklV2Token
        {
            public DateTime ExpiresUtc { get; set; }

            [JsonProperty("access_token")]
            public string AccessToken { get; set; }

            [JsonProperty("refresh_token")]
            public string RefreshToken { get; set; }

            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonProperty("token_type")]
            public string TokenType { get; set; }

            [JsonProperty("scope")]
            public string Scope { get; set; }
        }
    }

    internal sealed class SimklV2OAuthSession
    {
        public string ClientId { get; set; }
        public string RedirectUri { get; set; }
        public string Verifier { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }
}
