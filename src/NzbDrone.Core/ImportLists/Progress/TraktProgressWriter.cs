using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Trakt;
using NzbDrone.Core.ImportLists.Trakt.List;
using NzbDrone.Core.ImportLists.Trakt.User;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists.Progress
{
    public class TraktProgressWriter : IImportListProgressWriter
    {
        private const string Root = "https://api.trakt.tv";
        private const string ClientId = TraktImportBase<TraktUserSettings>.ClientId;
        private readonly IHttpClient _http;

        public TraktProgressWriter(IHttpClient http)
        {
            _http = http;
        }

        public bool TryGetTarget(ImportListDefinition definition, Series series, ImportListItemInfo item, out ImportListProgressTarget target)
        {
            target = null;
            var enabled = definition.Settings switch
            {
                TraktUserSettings user => user.SyncWatchedProgress,
                TraktListSettings list => list.SyncWatchedProgress,
                _ => false
            };
            if (!enabled || series.MetadataSource != MetadataSourceType.Tvdb || series.TvdbId <= 0 ||
                item.TvdbId != series.TvdbId || series.SeasonType != SeasonType.Official || !string.IsNullOrWhiteSpace(series.OrderingId))
            {
                return false;
            }

            var credentials = Credentials(definition);
            target = new ImportListProgressTarget
            {
                MediaId = series.TvdbId,
                Fingerprint = ImportListProgressGuard.Fingerprint("Trakt", credentials.BaseUrl, credentials.AuthUser, credentials.Username, credentials.Token, series.TvdbId.ToString(CultureInfo.InvariantCulture))
            };
            return true;
        }

        public int Advance(ImportListDefinition definition, ImportListProgressTarget target, int progress)
        {
            var credentials = Credentials(definition);
            ImportListProgressGuard.OfficialRoot(credentials.BaseUrl, Root);
            if (string.IsNullOrWhiteSpace(credentials.Token) || credentials.Expires <= DateTime.UtcNow.AddMinutes(1))
            {
                throw new ImportListProgressUnavailableException();
            }

            var identity = Object(Request(credentials.Token, "users/settings"));
            var slug = (string)identity["user"]?["ids"]?["slug"];
            var username = (string)identity["user"]?["username"];
            if (string.IsNullOrWhiteSpace(slug) || !string.Equals(slug, credentials.AuthUser, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(credentials.Username) && !string.Equals(credentials.Username.Trim(), slug, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(credentials.Username.Trim(), username, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ImportListProgressUnavailableException();
            }

            var show = Lookup(credentials.Token, target.MediaId, "show");
            var showId = ImportListProgressGuard.Integer(show["show"]?["ids"]?["trakt"]);
            if (showId <= 0)
            {
                throw new ImportListProgressUnavailableException();
            }

            // Exact episode IDs avoid applying another catalog's season ordering.
            // Stop after a bounded chunk; the persistent queue resumes next poll.
            var confirmed = target.AcknowledgedProgress;
            var writes = 0;
            foreach (var episode in target.NativeEpisodes.Take(progress).Skip(target.AcknowledgedProgress).Take(20))
            {
                if (episode.TvdbId <= 0)
                {
                    throw new ImportListProgressUnavailableException();
                }

                var result = Lookup(credentials.Token, episode.TvdbId, "episode");
                if (ImportListProgressGuard.Integer(result["show"]?["ids"]?["trakt"]) != showId)
                {
                    throw new ImportListProgressUnavailableException();
                }

                var episodeId = ImportListProgressGuard.Integer(result["episode"]?["ids"]?["trakt"]);
                if (episodeId <= 0)
                {
                    throw new ImportListProgressUnavailableException();
                }

                var historyRequest = Request(credentials.Token, "sync/history/episodes/" + episodeId.ToString(CultureInfo.InvariantCulture));
                historyRequest.Url = historyRequest.Url.AddQueryParam("limit", 1);
                var history = Array(historyRequest);
                if (history.Count == 0)
                {
                    if (writes >= 20)
                    {
                        break;
                    }

                    var update = Request(credentials.Token, "sync/history");
                    update.Method = HttpMethod.Post;
                    update.Headers.ContentType = "application/json";
                    update.SetContent(new JObject
                    {
                        ["episodes"] = new JArray(new JObject
                        {
                            ["ids"] = new JObject { ["trakt"] = episodeId },
                            ["watched_at"] = (episode.WatchedAtUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                        })
                    }.ToString());

                    // Trakt does not deduplicate. Never blindly retry the mutation;
                    // a subsequent queue attempt first reads this exact ID again.
                    var saved = Object(update);
                    if (saved["not_found"] is not JObject missing || missing.Properties().Any(property => property.Value is not JArray array || array.Count > 0) ||
                        ImportListProgressGuard.Integer(saved["added"]?["episodes"]) != 1)
                    {
                        throw new ImportListProgressUnavailableException();
                    }

                    writes++;
                }
                else if (history.Any(row => (string)row["type"] != "episode" || ImportListProgressGuard.Integer(row["episode"]?["ids"]?["trakt"]) != episodeId))
                {
                    throw new ImportListProgressUnavailableException();
                }

                confirmed++;
            }

            return confirmed;
        }

        private JObject Lookup(string token, int id, string type)
        {
            var request = Request(token, "search/tvdb/" + id.ToString(CultureInfo.InvariantCulture));
            request.Url = request.Url.AddQueryParam("type", type);
            var rows = Array(request);
            if (rows.Count != 1 || rows[0] is not JObject result || (string)result["type"] != type ||
                ImportListProgressGuard.Integer(result[type]?["ids"]?["tvdb"]) != id)
            {
                throw new ImportListProgressUnavailableException();
            }

            return result;
        }

        private JObject Object(HttpRequest request) => ImportListProgressGuard.ObjectResponse(_http.Execute(request));

        private JArray Array(HttpRequest request)
        {
            var response = _http.Execute(request);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new TooManyRequestsException(request, response);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ImportListProgressUnavailableException();
            }

            return JArray.Parse(response.Content);
        }

        private static HttpRequest Request(string token, string resource)
        {
            var request = ImportListProgressGuard.PrivateRequest(new HttpRequestBuilder(Root).Resource(resource).Accept(HttpAccept.Json).Build(), token);
            request.Headers.Add("trakt-api-key", ClientId);
            request.Headers.Add("trakt-api-version", "2");
            request.RateLimit = TimeSpan.FromSeconds(1);
            return request;
        }

        private static (string BaseUrl, string AuthUser, string Username, string Token, DateTime Expires) Credentials(ImportListDefinition definition)
        {
            return definition.Settings switch
            {
                TraktUserSettings user => (user.BaseUrl, user.AuthUser, user.Username, user.AccessToken, user.Expires),
                TraktListSettings list => (list.BaseUrl, list.AuthUser, list.Username, list.AccessToken, list.Expires),
                _ => throw new ImportListProgressUnavailableException()
            };
        }
    }
}
