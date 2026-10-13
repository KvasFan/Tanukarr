using System;
using System.Collections.Generic;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    public class MyAnimeListRequestGenerator : IImportListRequestGenerator
    {
        public MyAnimeListSettings Settings { get; set; }

        public static readonly IReadOnlyDictionary<MyAnimeListStatus, string> StatusMapping = new Dictionary<MyAnimeListStatus, string>
        {
            { MyAnimeListStatus.Watching, "watching" },
            { MyAnimeListStatus.Completed, "completed" },
            { MyAnimeListStatus.OnHold, "on_hold" },
            { MyAnimeListStatus.Dropped, "dropped" },
            { MyAnimeListStatus.PlanToWatch, "plan_to_watch" },
        };

        public virtual ImportListPageableRequestChain GetListItems()
        {
            var pageableReq = new ImportListPageableRequestChain();

            pageableReq.Add(GetSeriesRequest());

            return pageableReq;
        }

        private IEnumerable<ImportListRequest> GetSeriesRequest()
        {
            yield return GetRequest(0);
        }

        public ImportListRequest GetRequest(int offset)
        {
            Settings.NormalizeLegacyStatus();
            var requestBuilder = new HttpRequestBuilder(Settings.BaseUrl.Trim());

            requestBuilder.Resource("users/{username}/animelist");
            requestBuilder.SetSegment("username", Settings.Username == "@me" ? "@me" : Uri.EscapeDataString(Settings.Username));
            requestBuilder.AddQueryParam("fields", "status,list_status{status,score,tags,is_rewatching}");
            requestBuilder.AddQueryParam("limit", "1000");
            requestBuilder.AddQueryParam("offset", offset);
            requestBuilder.Accept(HttpAccept.Json);

            var httpReq = new ImportListRequest(requestBuilder.Build());

            if (Settings.AccessToken.IsNotNullOrWhiteSpace())
            {
                httpReq.HttpRequest.Headers.Add("Authorization", $"Bearer {Settings.AccessToken}");
            }
            else
            {
                httpReq.HttpRequest.Headers.Add("X-MAL-CLIENT-ID", Settings.ClientId);
            }

            return httpReq;
        }
    }
}
