using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    public class MyAnimeListImport : HttpImportListBase<MyAnimeListSettings>
    {
        public const string AuthorizeUrl = "https://myanimelist.net/v1/oauth2/authorize";
        public const string TokenUrl = "https://myanimelist.net/v1/oauth2/token";
        private const int MaxPages = 100;
        private readonly IImportListRepository _importListRepository;
        private readonly IMyAnimeListRequestService _requests;
        private readonly ICached<OAuthSession> _sessions;
        private readonly ICached<RenewedToken> _renewedTokens;
        private readonly object _oauthLock;
        private readonly object _refreshLock;

        public override string Name => "MyAnimeList";
        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

        public MyAnimeListImport(IImportListRepository repository,
            IHttpClient httpClient,
            IImportListStatusService statusService,
            IConfigService configService,
            IParsingService parsingService,
            ILocalizationService localizationService,
            IMyAnimeListRequestService requests,
            ICacheManager cacheManager,
            Logger logger)
            : base(httpClient, statusService, configService, parsingService, localizationService, logger)
        {
            _importListRepository = repository;
            _requests = requests;
            _sessions = cacheManager.GetCache<OAuthSession>(typeof(MyAnimeListImport), "oauth");
            _renewedTokens = cacheManager.GetCache<RenewedToken>(typeof(MyAnimeListImport), "renewedTokens");
            _oauthLock = cacheManager.GetCache<object>(typeof(MyAnimeListImport), "oauthLock").Get("lock", () => new object());
            _refreshLock = cacheManager.GetCache<object>(typeof(MyAnimeListImport), "refreshLock").Get("lock", () => new object());
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            try
            {
                return OAuthAction(action, query);
            }
            catch (ImportListException ex)
            {
                throw new ValidationException(new[] { new ValidationFailure("SignIn", ex.Message) });
            }
            catch (HttpException)
            {
                throw new ValidationException(new[] { new ValidationFailure("SignIn", "MyAnimeList authentication is unavailable or rate limited. Retry later.") });
            }
        }

        private object OAuthAction(string action, IDictionary<string, string> query)
        {
            if (action == "startOAuth")
            {
                if (Settings.ClientId.IsNullOrWhiteSpace())
                {
                    throw new ImportListException(null, "Enter your registered MyAnimeList Client ID before authenticating.");
                }

                if (!query.TryGetValue("callbackUrl", out var callback) || !Uri.TryCreate(callback, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 ||
                    uri.Fragment.Length > 0 || !uri.AbsolutePath.EndsWith("/oauth.html", StringComparison.Ordinal))
                {
                    throw new ImportListException(null, "MyAnimeList requires a valid local OAuth callback URL.");
                }

                var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
                _sessions.ClearExpired();
                _sessions.Set(state, new OAuthSession { ClientId = Settings.ClientId, RedirectUri = callback, Verifier = verifier }, TimeSpan.FromMinutes(10));
                var request = new HttpRequestBuilder(AuthorizeUrl)
                    .AddQueryParam("response_type", "code").AddQueryParam("client_id", Settings.ClientId)
                    .AddQueryParam("redirect_uri", callback).AddQueryParam("state", state)
                    .AddQueryParam("code_challenge", verifier).AddQueryParam("code_challenge_method", "plain").Build();
                return new { oauthUrl = request.Url.FullUri };
            }

            if (action == "getOAuthToken")
            {
                if (!query.TryGetValue("state", out var state) || !query.TryGetValue("code", out var code) || code.IsNullOrWhiteSpace())
                {
                    throw new ImportListException(null, "MyAnimeList authentication was cancelled or returned an invalid state. Authenticate again.");
                }

                OAuthSession session;
                lock (_oauthLock)
                {
                    session = _sessions.Find(state);
                    if (session == null || session.ClientId != Settings.ClientId)
                    {
                        throw new ImportListException(null, "MyAnimeList authentication has expired or belongs to another client. Authenticate again.");
                    }

                    _sessions.Remove(state);
                }

                var token = ExchangeToken(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code", ["code"] = code,
                    ["redirect_uri"] = session.RedirectUri, ["code_verifier"] = session.Verifier
                });
                return new { accessToken = token.AccessToken, refreshToken = token.RefreshToken, expires = DateTime.UtcNow.AddSeconds(token.ExpiresIn) };
            }

            return new { };
        }

        public override ImportListFetchResult Fetch()
        {
            using var snapshots = ImportListSnapshotScope.BeginIfNeeded();
            try
            {
                CheckToken();
                var items = ReadCompleteList();
                _importListStatusService.RecordSuccess(Definition.Id);
                return new ImportListFetchResult(CleanupListItems(items), false);
            }
            catch (TooManyRequestsException ex)
            {
                _importListStatusService.RecordFailure(Definition.Id, ex.RetryAfter > TimeSpan.Zero ? ex.RetryAfter : TimeSpan.FromMinutes(5));
                _logger.Warn("MyAnimeList request limit reached; the previous complete list will be retained.");
            }
            catch (Exception ex)
            {
                _importListStatusService.RecordFailure(Definition.Id);
                _logger.Warn("MyAnimeList import failed ({0}); the previous complete list will be retained. Test the list for details.", ex.GetType().Name);
            }

            return new ImportListFetchResult(Array.Empty<ImportListItemInfo>(), true);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            using var snapshots = ImportListSnapshotScope.Begin();
            try
            {
                CheckToken(false);
                ReadCompleteList();
            }
            catch (Exception ex)
            {
                var message = ex is ImportListException ? ex.Message : "MyAnimeList could not return a complete list. Check authentication, connectivity and retry later.";
                failures.Add(new ValidationFailure(string.Empty, message));
            }
        }

        private List<ImportListItemInfo> ReadCompleteList()
        {
            Settings.Validate().ThrowOnError();
            var generator = (MyAnimeListRequestGenerator)GetRequestGenerator();
            var parser = (MyAnimeListParser)GetParser();
            var key = ImportListSnapshotScope.Fingerprint("MyAnimeListAccount-v1", generator.GetRequest(0).Url.FullUri, Settings.ClientId, Settings.AccessToken);
            var snapshot = ImportListSnapshotScope.Current?.GetOrLoad(key, () => ReadCompleteAccount(generator), value => value.PayloadBytes)
                ?? ReadCompleteAccount(generator);
            return parser.ParseValidatedItems(snapshot.Items).ToList();
        }

        private MyAnimeListAccountSnapshot ReadCompleteAccount(MyAnimeListRequestGenerator generator)
        {
            var snapshot = new MyAnimeListAccountSnapshot();
            var offset = 0;
            for (var page = 1; page <= MaxPages; page++)
            {
                var request = generator.GetRequest(offset);
                var response = FetchImportListResponse(request);
                var payloadBytes = snapshot.PayloadBytes;
                ImportListSnapshotScope.AddPayloadBytes(ref payloadBytes, response.Content);
                snapshot.PayloadBytes = payloadBytes;
                var resource = MyAnimeListParser.ReadPage(response);
                snapshot.Items.AddRange(resource.Animes);
                var next = resource.Paging.Next;
                var count = resource.Animes.Count;
                if (next.IsNullOrWhiteSpace())
                {
                    return snapshot;
                }

                var current = new Uri(request.Url.FullUri);
                if (count == 0 || !Uri.TryCreate(next, UriKind.Absolute, out var cursor) || cursor.Scheme != current.Scheme ||
                    cursor.Host != current.Host || cursor.Port != current.Port || cursor.AbsolutePath != current.AbsolutePath ||
                    cursor.UserInfo.Length > 0 || cursor.Fragment.Length > 0)
                {
                    throw new ImportListException(response, "MyAnimeList returned unsafe or incomplete pagination. The previous list will be retained.");
                }

                var values = cursor.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(pair => pair.Length == 2 && pair[0] == "offset").ToList();
                if (values.Count != 1 || !int.TryParse(values[0][1], NumberStyles.None, CultureInfo.InvariantCulture, out var following) || following != offset + count)
                {
                    throw new ImportListException(response, "MyAnimeList returned a repeated or skipped page. The previous list will be retained.");
                }

                offset = following;
            }

            throw new ImportListException(null, "MyAnimeList exceeded the 100-page safety limit. The previous list will be retained.");
        }

        private sealed class MyAnimeListAccountSnapshot
        {
            public List<MyAnimeListItem> Items { get; } = new();
            public long PayloadBytes { get; set; }
        }

        protected override ImportListResponse FetchImportListResponse(ImportListRequest request)
        {
            var response = _requests.Execute(request.HttpRequest);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ImportListException(null, "MyAnimeList returned HTTP {0}. Check the username, Client ID and authentication; the previous list will be retained.", (int)response.StatusCode);
            }

            return new ImportListResponse(request, response);
        }

        public override IParseImportListResponse GetParser() => new MyAnimeListParser(Settings);
        public override IImportListRequestGenerator GetRequestGenerator() => new MyAnimeListRequestGenerator { Settings = Settings };

        protected override IList<ImportListItemInfo> CleanupListItems(IEnumerable<ImportListItemInfo> releases)
        {
            var result = releases.DistinctBy(item => item.MalId).ToList();
            result.ForEach(item =>
            {
                item.ImportListId = Definition.Id;
                item.ImportList = Definition.Name;
            });
            return result;
        }

        private void CheckToken(bool persist = true)
        {
            if (Settings.AccessToken.IsNullOrWhiteSpace())
            {
                return;
            }

            lock (_refreshLock)
            {
                var originalKey = CredentialFingerprint();
                var renewed = _renewedTokens.Find(originalKey);
                var changed = renewed != null;
                if (renewed != null)
                {
                    ApplyToken(renewed);
                }

                if (Settings.Expires < DateTime.UtcNow.AddMinutes(5))
                {
                    if (Settings.ClientId.IsNullOrWhiteSpace() || Settings.RefreshToken.IsNullOrWhiteSpace())
                    {
                        throw new ImportListException(null, "This MyAnimeList token needs renewal. Enter your registered Client ID and authenticate again; legacy Sonarr-hosted OAuth is unavailable.");
                    }

                    var currentKey = CredentialFingerprint();
                    var token = ExchangeToken(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = Settings.RefreshToken });
                    renewed = new RenewedToken
                    {
                        AccessToken = token.AccessToken,
                        RefreshToken = token.RefreshToken ?? Settings.RefreshToken,
                        Expires = DateTime.UtcNow.AddSeconds(token.ExpiresIn)
                    };
                    _renewedTokens.ClearExpired();
                    _renewedTokens.Set(originalKey, renewed, TimeSpan.FromDays(1));
                    _renewedTokens.Set(currentKey, renewed, TimeSpan.FromDays(1));
                    ApplyToken(renewed);
                    changed = true;
                }

                if (changed && persist && Definition.Id > 0)
                {
                    _importListRepository.UpdateSettings((ImportListDefinition)Definition);
                }
            }
        }

        private string CredentialFingerprint()
        {
            // Reuse renewed credentials after an unsaved Test without retaining the
            // original secret values in keys or saving any of that form's filters.
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.ToJson(new
            {
                Settings.ClientId,
                Settings.ClientSecret,
                Settings.AccessToken,
                Settings.RefreshToken
            }))));
        }

        private void ApplyToken(RenewedToken token)
        {
            Settings.AccessToken = token.AccessToken;
            Settings.RefreshToken = token.RefreshToken;
            Settings.Expires = token.Expires;
        }

        private MyAnimeListAuthToken ExchangeToken(IDictionary<string, string> parameters)
        {
            var builder = new HttpRequestBuilder(TokenUrl).Post().AddFormParameter("client_id", Settings.ClientId).Accept(HttpAccept.Json);
            if (Settings.ClientSecret.IsNotNullOrWhiteSpace())
            {
                builder.AddFormParameter("client_secret", Settings.ClientSecret);
            }

            foreach (var parameter in parameters)
            {
                builder.AddFormParameter(parameter.Key, parameter.Value);
            }

            var response = _requests.Execute(builder.Build());
            MyAnimeListAuthToken token = null;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                try
                {
                    token = Json.Deserialize<MyAnimeListAuthToken>(response.Content);
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    // Never include an OAuth response body or code in diagnostics.
                }
            }

            if (token?.AccessToken.IsNotNullOrWhiteSpace() != true || token.ExpiresIn <= 0 ||
                !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                (parameters["grant_type"] == "authorization_code" && token.RefreshToken.IsNullOrWhiteSpace()))
            {
                throw new ImportListException(null, "MyAnimeList authentication failed. Verify the registered Client ID, optional secret and callback URL, then authenticate again.");
            }

            return token;
        }

        private sealed class RenewedToken
        {
            public string AccessToken { get; set; }
            public string RefreshToken { get; set; }
            public DateTime Expires { get; set; }
        }

        private sealed class OAuthSession
        {
            public string ClientId { get; set; }
            public string RedirectUri { get; set; }
            public string Verifier { get; set; }
        }
    }
}
