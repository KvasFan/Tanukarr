using System;
using System.Globalization;
using System.Net.Http;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.MyAnimeList;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists.Progress
{
    public class MyAnimeListProgressWriter : IImportListProgressWriter
    {
        private const string Root = "https://api.myanimelist.net/v2";
        private readonly IMyAnimeListRequestService _requests;

        public MyAnimeListProgressWriter(IMyAnimeListRequestService requests)
        {
            _requests = requests;
        }

        public bool TryGetTarget(ImportListDefinition definition, Series series, ImportListItemInfo item, out ImportListProgressTarget target)
        {
            target = null;
            if (definition.Settings is not MyAnimeListSettings settings || !settings.SyncWatchedProgress ||
                !ImportListProgressGuard.NativeIdentity(series, MetadataSourceType.MyAnimeList, item.MalId))
            {
                return false;
            }

            target = new ImportListProgressTarget
            {
                MediaId = item.MalId,
                Fingerprint = ImportListProgressGuard.Fingerprint("MyAnimeList", settings.BaseUrl, settings.Username, settings.ClientId, settings.AccessToken, series.ForeignId)
            };
            return true;
        }

        public int Advance(ImportListDefinition definition, ImportListProgressTarget target, int progress)
        {
            var settings = (MyAnimeListSettings)definition.Settings;
            ImportListProgressGuard.OfficialRoot(settings.BaseUrl, Root);
            if (string.IsNullOrWhiteSpace(settings.AccessToken) || settings.Expires <= DateTime.UtcNow.AddMinutes(1))
            {
                throw new ImportListProgressUnavailableException();
            }

            var viewer = ImportListProgressGuard.ObjectResponse(_requests.Execute(Request(settings, "users/@me")));
            if (ImportListProgressGuard.Integer(viewer["id"]) <= 0 || string.IsNullOrWhiteSpace((string)viewer["name"]) ||
                (settings.Username != "@me" && !string.Equals(settings.Username, (string)viewer["name"], StringComparison.OrdinalIgnoreCase)))
            {
                throw new ImportListProgressUnavailableException();
            }

            var query = Request(settings, "anime/" + target.MediaId.ToString(CultureInfo.InvariantCulture));
            query.Url = query.Url.AddQueryParam("fields", "num_episodes,my_list_status{num_episodes_watched}");
            var anime = ImportListProgressGuard.ObjectResponse(_requests.Execute(query));
            if (ImportListProgressGuard.Integer(anime["id"]) != target.MediaId || anime["my_list_status"] is not Newtonsoft.Json.Linq.JObject listStatus)
            {
                throw new ImportListProgressUnavailableException();
            }

            var current = ImportListProgressGuard.Integer(listStatus["num_episodes_watched"]);
            if (current >= progress)
            {
                return current;
            }

            var total = ImportListProgressGuard.Integer(anime["num_episodes"]);
            if (total > 0 && progress > total)
            {
                throw new ImportListProgressUnavailableException();
            }

            var update = new HttpRequestBuilder(Root).Resource("anime/" + target.MediaId.ToString(CultureInfo.InvariantCulture) + "/my_list_status")
                .Post().AddFormParameter("num_watched_episodes", progress).Accept(HttpAccept.Json).Build();
            update.Method = HttpMethod.Patch;
            ImportListProgressGuard.PrivateRequest(update, settings.AccessToken);
            var saved = ImportListProgressGuard.ObjectResponse(_requests.Execute(update));
            var confirmed = ImportListProgressGuard.Integer(saved["num_episodes_watched"]);
            return confirmed >= progress ? confirmed : throw new ImportListProgressUnavailableException();
        }

        private static HttpRequest Request(MyAnimeListSettings settings, string resource)
        {
            return ImportListProgressGuard.PrivateRequest(new HttpRequestBuilder(Root).Resource(resource).Accept(HttpAccept.Json).Build(), settings.AccessToken);
        }
    }
}
