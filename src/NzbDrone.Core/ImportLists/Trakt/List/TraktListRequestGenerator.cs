using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Trakt.List
{
    public class TraktListRequestGenerator : TraktRequestGeneratorBase<TraktListSettings>
    {
        public TraktListRequestGenerator(TraktListSettings settings, string clientId, int pageSize, int maxNumResults)
            : base(settings, clientId, pageSize, maxNumResults)
        {
        }

        protected override void SetResource(HttpRequestBuilder requestBuilder)
        {
            requestBuilder
                .Resource("/users/{userName}/lists/{listName}/items/show,season,episode")
                .SetSegment("userName", Settings.Username.Trim())
                .SetSegment("listName", Settings.Listname.IsNotNullOrWhiteSpace() ? Settings.Listname.ToUrlSlug() : Settings.IncludedLists.FirstOrDefault());
        }

        public ImportListRequest GetRequest(string slug, int page, bool applyFilters)
        {
            var builder = new HttpRequestBuilder(Settings.BaseUrl.Trim()).Accept(HttpAccept.Json)
                .SetHeader("trakt-api-version", "2").SetHeader("trakt-api-key", _clientId)
                .SetHeader("Authorization", $"Bearer {Settings.AccessToken}")
                .Resource("/users/{userName}/lists/{listName}/items/show,season,episode")
                .SetSegment("userName", Settings.Username.Trim()).SetSegment("listName", slug);
            if (applyFilters)
            {
                foreach (var parameter in GetFilterParameters())
                {
                    builder.AddQueryParam(parameter.Key, parameter.Value, true);
                }
            }

            builder.AddQueryParam("limit", _pageSize, true).AddQueryParam("page", page, true);
            return new ImportListRequest(builder.Build());
        }

        protected override Dictionary<string, string> GetFilterParameters()
        {
            return TraktQueryHelper.BuildFilterParameters(Settings.Rating, Settings.Genres, Settings.Years, _pageSize, Settings.TraktAdditionalParameters);
        }
    }
}
