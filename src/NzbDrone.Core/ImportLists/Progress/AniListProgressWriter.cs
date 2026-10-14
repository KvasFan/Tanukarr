using System;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.AniList;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists.Progress
{
    public class AniListProgressWriter : IImportListProgressWriter
    {
        private const string Root = "https://graphql.anilist.co";
        private const string IdentityQuery = "query($username:String!){Viewer{id name} User(name:$username){id name}}";
        private const string EntryQuery = "query($mediaId:Int!,$userId:Int!){MediaList(mediaId:$mediaId,userId:$userId,type:ANIME){id userId mediaId progress media{episodes type}}}";
        private const string ProgressMutation = "mutation($entryId:Int!,$progress:Int!){SaveMediaListEntry(id:$entryId,progress:$progress){id userId mediaId progress}}";
        private readonly IAniListRequestService _requests;

        public AniListProgressWriter(IAniListRequestService requests)
        {
            _requests = requests;
        }

        public bool TryGetTarget(ImportListDefinition definition, Series series, ImportListItemInfo item, out ImportListProgressTarget target)
        {
            target = null;
            if (definition.Settings is not AniListSettings settings || !settings.SyncWatchedProgress ||
                !ImportListProgressGuard.NativeIdentity(series, MetadataSourceType.AniList, item.AniListId))
            {
                return false;
            }

            target = new ImportListProgressTarget
            {
                MediaId = item.AniListId,
                Fingerprint = ImportListProgressGuard.Fingerprint("AniList", settings.BaseUrl, settings.Username, settings.AccessToken, series.ForeignId)
            };
            return true;
        }

        public int Advance(ImportListDefinition definition, ImportListProgressTarget target, int progress)
        {
            var settings = (AniListSettings)definition.Settings;
            ImportListProgressGuard.OfficialRoot(settings.BaseUrl, Root);
            if (string.IsNullOrWhiteSpace(settings.AccessToken) || settings.Expires <= DateTime.UtcNow.AddMinutes(1))
            {
                throw new ImportListProgressUnavailableException();
            }

            var identity = Graphql(settings, IdentityQuery, new { username = settings.Username });
            if (identity["Viewer"] is not JObject viewer || identity["User"] is not JObject user)
            {
                throw new ImportListProgressUnavailableException();
            }

            var viewerId = ImportListProgressGuard.Integer(viewer["id"]);
            var userId = ImportListProgressGuard.Integer(user["id"]);
            if (viewerId <= 0 || viewerId != userId)
            {
                throw new ImportListProgressUnavailableException();
            }

            if (Graphql(settings, EntryQuery, new { mediaId = target.MediaId, userId })["MediaList"] is not JObject entry || entry["media"] is not JObject media)
            {
                throw new ImportListProgressUnavailableException();
            }

            var entryId = ImportListProgressGuard.Integer(entry?["id"]);
            if (entryId <= 0 || ImportListProgressGuard.Integer(entry?["userId"]) != viewerId ||
                ImportListProgressGuard.Integer(entry?["mediaId"]) != target.MediaId || (string)media["type"] != "ANIME")
            {
                throw new ImportListProgressUnavailableException();
            }

            var current = ImportListProgressGuard.Integer(entry["progress"]);
            if (current >= progress)
            {
                return current;
            }

            if (media["episodes"]?.Type == JTokenType.Integer &&
                ImportListProgressGuard.Integer(media["episodes"]) is var total && total > 0 && progress > total)
            {
                throw new ImportListProgressUnavailableException();
            }

            // Updating the already verified entry ID avoids creating membership.
            // Only progress is sent: list status, custom lists and ratings stay owned
            // by the account holder.
            if (Graphql(settings, ProgressMutation, new { entryId, progress })["SaveMediaListEntry"] is not JObject saved)
            {
                throw new ImportListProgressUnavailableException();
            }

            if (ImportListProgressGuard.Integer(saved?["id"]) != entryId ||
                ImportListProgressGuard.Integer(saved?["userId"]) != viewerId ||
                ImportListProgressGuard.Integer(saved?["mediaId"]) != target.MediaId)
            {
                throw new ImportListProgressUnavailableException();
            }

            var confirmed = ImportListProgressGuard.Integer(saved?["progress"]);
            return confirmed >= progress ? confirmed : throw new ImportListProgressUnavailableException();
        }

        private JObject Graphql(AniListSettings settings, string query, object variables)
        {
            var request = ImportListProgressGuard.PrivateRequest(new HttpRequestBuilder(Root).Post().Accept(HttpAccept.Json).Build(), settings.AccessToken);
            request.Headers.ContentType = "application/json";
            request.SetContent(Json.ToJson(new { query, variables }));
            var response = ImportListProgressGuard.ObjectResponse(_requests.Execute(request));
            if ((response["errors"] is JArray errors && errors.Count > 0) || response["data"] is not JObject data)
            {
                throw new ImportListProgressUnavailableException();
            }

            return data;
        }
    }
}
