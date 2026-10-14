using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Simkl;
using NzbDrone.Core.ImportLists.Simkl.User;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists.Progress
{
    public class SimklProgressWriter : IImportListProgressWriter
    {
        private const string Root = "https://api.simkl.com";
        private readonly ISimklV2RequestService _requests;

        public SimklProgressWriter(ISimklV2RequestService requests)
        {
            _requests = requests;
        }

        public bool TryGetTarget(ImportListDefinition definition, Series series, ImportListItemInfo item, out ImportListProgressTarget target)
        {
            target = null;
            if (definition.Settings is not SimklUserSettings settings || !settings.SyncWatchedProgress ||
                !string.IsNullOrWhiteSpace(series.OrderingId))
            {
                return false;
            }

            var id = series.MetadataSource switch
            {
                MetadataSourceType.AniList when ImportListProgressGuard.NativeIdentity(series, MetadataSourceType.AniList, item.AniListId) => item.AniListId,
                MetadataSourceType.MyAnimeList when ImportListProgressGuard.NativeIdentity(series, MetadataSourceType.MyAnimeList, item.MalId) => item.MalId,
                MetadataSourceType.Tvdb when series.TvdbId > 0 && item.TvdbId == series.TvdbId && series.SeasonType == SeasonType.Official => item.TvdbId,
                _ => 0
            };
            if (id <= 0)
            {
                return false;
            }

            target = new SimklProgressTarget
            {
                MediaId = id,
                ProviderIdName = series.MetadataSource switch
                {
                    MetadataSourceType.AniList => "anilist",
                    MetadataSourceType.MyAnimeList => "mal",
                    _ => "tvdb"
                },
                IsMovie = series.AnimeGroupMemberKind == AnimeGroupMemberKind.Movie,
                IsTvdb = series.MetadataSource == MetadataSourceType.Tvdb,
                Fingerprint = ImportListProgressGuard.Fingerprint("Simkl", settings.BaseUrl, settings.AuthUser, settings.ClientIdV2, settings.AccessToken, settings.ScopeV2, series.MetadataSource.ToString(), id.ToString(CultureInfo.InvariantCulture))
            };
            return true;
        }

        public int Advance(ImportListDefinition definition, ImportListProgressTarget target, int progress)
        {
            var settings = (SimklUserSettings)definition.Settings;
            var simkl = (SimklProgressTarget)target;
            ImportListProgressGuard.OfficialRoot(settings.BaseUrl, Root);
            if (string.IsNullOrWhiteSpace(settings.ClientIdV2) || string.IsNullOrWhiteSpace(settings.AccessToken) ||
                settings.Expires <= DateTime.UtcNow.AddMinutes(1) || settings.ScopeV2?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("media:write", StringComparer.Ordinal) != true)
            {
                throw new ImportListProgressUnavailableException();
            }

            var profile = ImportListProgressGuard.ObjectResponse(_requests.Execute(Request(settings, "users/settings")));
            var accountId = ImportListProgressGuard.Integer(profile["account"]?["id"]);
            if (accountId <= 0 || settings.AuthUser != accountId.ToString(CultureInfo.InvariantCulture))
            {
                throw new ImportListProgressUnavailableException();
            }

            var ids = new JObject { [simkl.ProviderIdName] = target.MediaId.ToString(CultureInfo.InvariantCulture) };
            var lookup = Request(settings, "sync/watched");
            lookup.Method = HttpMethod.Post;
            lookup.Headers.ContentType = "application/json";
            lookup.SetContent(new JArray(new JObject { ["ids"] = ids, ["type"] = simkl.IsMovie ? "movie" : "show" }).ToString());
            var response = _requests.Execute(lookup);
            if (response.StatusCode != HttpStatusCode.OK || JArray.Parse(response.Content) is not { Count: 1 } results ||
                results[0] is not JObject existing || string.IsNullOrWhiteSpace((string)existing["list"]) ||
                ImportListProgressGuard.Integer(existing["simkl"]) <= 0)
            {
                throw new ImportListProgressUnavailableException();
            }

            // Pin the history write to the canonical record verified above, instead
            // of resolving an ambiguous external identifier a second time.
            var item = new JObject { ["ids"] = new JObject { ["simkl"] = ImportListProgressGuard.Integer(existing["simkl"]) } };
            if (simkl.IsMovie)
            {
                if (progress != 1 || target.NativeEpisodes.Count != 1)
                {
                    throw new ImportListProgressUnavailableException();
                }
            }
            else
            {
                // Explicit episode arrays prohibit implicit whole-season expansion.
                // TVDB anime uses the provider's documented numbering switch.
                var chunk = target.NativeEpisodes.Take(progress).Skip(target.AcknowledgedProgress).Take(100).ToList();
                item["seasons"] = JArray.FromObject(chunk.GroupBy(episode => episode.SeasonNumber)
                    .Select(season => new { number = season.Key, episodes = season.Select(episode => new { number = episode.EpisodeNumber }) }));
                progress = target.AcknowledgedProgress + chunk.Count;
                if (simkl.IsTvdb)
                {
                    item["use_tvdb_anime_seasons"] = true;
                }
            }

            var update = Request(settings, "sync/history");
            update.Url = update.Url.AddQueryParam("skip_auto_watching", "yes");
            update.Method = HttpMethod.Post;
            update.Headers.ContentType = "application/json";
            update.SetContent(new JObject { [simkl.IsMovie ? "movies" : "shows"] = new JArray(item) }.ToString());

            // Simkl documents duplicate histories as no-ops by default. Never set
            // allow_rewatch; a failed reply is retried without adding new plays.
            var savedResponse = _requests.ExecuteOnce(update);
            if (savedResponse.StatusCode != HttpStatusCode.Created && savedResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new ImportListProgressUnavailableException();
            }

            var saved = JObject.Parse(savedResponse.Content);
            if (saved["not_found"] is not JObject missing || missing.Properties().Any(property => property.Value is not JArray array || array.Count > 0) ||
                saved["added"] is not JObject added || added[simkl.IsMovie ? "movies" : "episodes"]?.Type != JTokenType.Integer)
            {
                throw new ImportListProgressUnavailableException();
            }

            return progress;
        }

        private static HttpRequest Request(SimklUserSettings settings, string resource)
        {
            return ImportListProgressGuard.PrivateRequest(SimklV2RequestBuilder.Build(resource, settings), settings.AccessToken);
        }

        private class SimklProgressTarget : ImportListProgressTarget
        {
            public string ProviderIdName { get; set; }
            public bool IsTvdb { get; set; }
            public bool IsMovie { get; set; }
        }
    }
}
