using System;
using System.Collections.Generic;
using System.Globalization;
using FluentValidation.Results;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.MDBList
{
    public class MDBListImport : HttpImportListBase<MDBListSettings>
    {
        public MDBListImport(IHttpClient client,
            IImportListStatusService status,
            IConfigService config,
            IParsingService parsing,
            ILocalizationService localization,
            Logger logger)
            : base(client, status, config, parsing, localization, logger)
        {
        }

        public override string Name => "MDBList";
        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);
        public override int PageSize => 250;
        public override IParseImportListResponse GetParser() => new MDBListParser();
        public override IImportListRequestGenerator GetRequestGenerator() => new MDBListRequestGenerator(Settings);
        public override object RequestAction(string action, IDictionary<string, string> query) => new { };

        protected override ImportListFetchResult FetchItems(Func<IImportListRequestGenerator, ImportListPageableRequestChain> selector, bool isRecent = false)
        {
            return FetchComposed(true);
        }

        protected override ValidationFailure TestConnection() => ImportListSetOperations.TestResult(FetchComposed(false));

        private ImportListFetchResult FetchComposed(bool recordStatus)
        {
            using var snapshots = recordStatus ? ImportListSnapshotScope.BeginIfNeeded() : ImportListSnapshotScope.Begin();
            return ImportListSetOperations.Fetch(
                (ImportListDefinition)Definition,
                _importListStatusService,
                _logger,
                () => ImportListSetOperations.Compose(
                    ImportListSetOperations.Sources(Settings.IncludedListUrls, Settings.ListUrl),
                    Settings.ExcludedListUrls,
                    ReadCompleteList),
                recordStatus);
        }

        private List<ImportListItemInfo> ReadCompleteList(string listUrl, bool applyFilters)
        {
            var key = ImportListSnapshotScope.Fingerprint("MdbSelectedList-v1", MDBListSettings.GetListPath(listUrl), Settings.ApiKey);
            var items = ImportListSnapshotScope.Current?.GetOrLoad(key, () => ReadListSnapshot(listUrl)) ?? ReadListSnapshot(listUrl);
            return ImportListSnapshotScope.CopyItems(items);
        }

        private List<ImportListItemInfo> ReadListSnapshot(string listUrl)
        {
            if (MDBListSettings.GetListPath(listUrl) == null)
            {
                throw new ImportListException(null, "Enter an HTTPS list URL on mdblist.com.");
            }

            var generator = (MDBListRequestGenerator)GetRequestGenerator();
            var result = new List<ImportListItemInfo>();
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string cursor = null;
            int? offset = null;
            var rawCount = 0;
            int? expectedCount = null;
            long payloadBytes = 0;
            for (var page = 1; page <= ImportListSetOperations.MaxPages; page++)
            {
                var response = FetchImportListResponse(generator.GetRequest(listUrl, cursor, offset));
                ImportListSnapshotScope.AddPayloadBytes(ref payloadBytes, response.Content);
                var document = JToken.Parse(response.Content);
                var rows = document as JArray ?? document["shows"] as JArray ?? document["items"] as JArray;
                if (rows == null)
                {
                    throw new ImportListException(response, "MDBList did not return a show list.");
                }

                rawCount += rows.Count;
                result.AddRange(GetParser().ParseResponse(response));
                var totalHeader = response.HttpResponse.Headers["X-Total-Items"]?.ToString();
                if (totalHeader != null)
                {
                    if (!int.TryParse(totalHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var total) ||
                        total < 0 || (expectedCount.HasValue && expectedCount != total))
                    {
                        throw new ImportListException(response, "MDBList returned inconsistent item counts.");
                    }

                    expectedCount = total;
                }

                var pagination = document is JObject ? document["pagination"] : null;
                if (pagination != null && pagination.Type is not (JTokenType.Object or JTokenType.Null))
                {
                    throw new ImportListException(response, "MDBList returned invalid pagination metadata.");
                }

                if (pagination?.Type == JTokenType.Null)
                {
                    pagination = null;
                }

                var next = pagination?["next_cursor"] ?? (document is JObject ? document["next_cursor"] : null);
                if (next != null && next.Type is not (JTokenType.String or JTokenType.Null))
                {
                    throw new ImportListException(response, "MDBList returned an invalid pagination cursor.");
                }

                var nextCursor = (string)next;
                var hasMore = ReadHasMore(response, pagination);
                if (pagination is JObject && next == null && !hasMore.HasValue)
                {
                    throw new ImportListException(response, "MDBList omitted pagination completion metadata.");
                }

                if (!string.IsNullOrEmpty(nextCursor))
                {
                    if (hasMore == false || nextCursor.Length > 4096 || !cursors.Add(nextCursor) || rows.Count == 0)
                    {
                        throw new ImportListException(response, "MDBList returned inconsistent or repeated pagination.");
                    }

                    cursor = nextCursor;
                    offset = null;
                    continue;
                }

                if (cursor != null && hasMore == true)
                {
                    throw new ImportListException(response, "MDBList omitted the cursor needed to complete the list.");
                }

                if (hasMore == false || (next != null && string.IsNullOrEmpty(nextCursor)) || rows.Count == 0)
                {
                    if (hasMore == true || (expectedCount.HasValue && rawCount != expectedCount))
                    {
                        throw new ImportListException(response, "MDBList returned an incomplete list.");
                    }

                    return result;
                }

                // Older responses omit cursor metadata. Advance by the raw response
                // count, and fetch until the offset endpoint returns an empty page.
                // Filtering movies must not terminate this legacy pagination.
                cursor = null;
                offset = rawCount;
            }

            throw new ImportListException(null, "MDBList pagination could not be completed. The previous list will be retained.");
        }

        private static bool? ReadHasMore(ImportListResponse response, JToken pagination)
        {
            var value = pagination is JObject ? pagination["has_more"] : null;
            if (value != null && value.Type is not (JTokenType.Boolean or JTokenType.Null))
            {
                throw new ImportListException(response, "MDBList returned invalid pagination metadata.");
            }

            bool? hasMore = value?.Type == JTokenType.Boolean ? (bool)value : null;
            var header = response.HttpResponse.Headers["X-Has-More"]?.ToString();
            if (header != null)
            {
                if (!bool.TryParse(header, out var headerValue) || (hasMore.HasValue && hasMore != headerValue))
                {
                    throw new ImportListException(response, "MDBList returned inconsistent pagination headers.");
                }

                hasMore = headerValue;
            }

            return hasMore;
        }
    }
}
