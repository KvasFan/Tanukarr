using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.MDBList
{
    public class MDBListRequestGenerator : IImportListRequestGenerator
    {
        private readonly MDBListSettings _settings;

        public MDBListRequestGenerator(MDBListSettings settings)
        {
            _settings = settings;
        }

        public ImportListPageableRequestChain GetListItems()
        {
            var chain = new ImportListPageableRequestChain();
            chain.Add(Requests());
            return chain;
        }

        private IEnumerable<ImportListRequest> Requests()
        {
            for (var offset = 0; offset < 1000; offset += 250)
            {
                yield return GetRequest(_settings.IncludedListUrls?.FirstOrDefault() ?? _settings.ListUrl, null, offset);
            }
        }

        public ImportListRequest GetRequest(string listUrl, string cursor, int? offset)
        {
            var builder = new HttpRequestBuilder("https://api.mdblist.com")
                .Resource(MDBListSettings.GetListPath(listUrl))
                .Accept(HttpAccept.Json)
                .AddQueryParam("apikey", _settings.ApiKey)
                .AddQueryParam("limit", 250);
            if (cursor != null)
            {
                builder.AddQueryParam("cursor", cursor);
            }
            else if (offset.HasValue)
            {
                builder.AddQueryParam("offset", offset.Value);
            }

            return new ImportListRequest(builder.Build());
        }
    }
}
