using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using FluentValidation.Results;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Trakt.List
{
    public class TraktListImport : TraktImportBase<TraktListSettings>
    {
        public TraktListImport(IImportListRepository netImportRepository,
                               IHttpClient httpClient,
                               IImportListStatusService netImportStatusService,
                               IConfigService configService,
                               IParsingService parsingService,
                               ILocalizationService localizationService,
                               Logger logger)
        : base(netImportRepository, httpClient, netImportStatusService, configService, parsingService, localizationService, logger)
        {
        }

        public override string Name => _localizationService.GetLocalizedString("TypeOfList", new Dictionary<string, object> { { "typeOfList", "Trakt" } });

        public override IImportListRequestGenerator GetRequestGenerator()
        {
            return new TraktListRequestGenerator(Settings, ClientId, PageSize, MaxNumResultsPerQuery);
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            return action == "getLists" ? new { options = GetListOptions() } : base.RequestAction(action, query);
        }

        private List<FieldSelectStringOption> GetListOptions()
        {
            var options = new List<FieldSelectStringOption>();
            int? expectedPages = null;
            int? expectedItems = null;
            for (var page = 1; page <= ImportListSetOperations.MaxPages; page++)
            {
                var request = new HttpRequestBuilder(Settings.BaseUrl.Trim()).Accept(HttpAccept.Json)
                    .SetHeader("trakt-api-version", "2").SetHeader("trakt-api-key", ClientId)
                    .SetHeader("Authorization", $"Bearer {Settings.AccessToken}")
                    .Resource("/users/{username}/lists").SetSegment("username", Settings.Username.Trim())
                    .AddQueryParam("page", page).AddQueryParam("limit", PageSize).Build();
                var response = FetchImportListResponse(new ImportListRequest(request));
                var rows = JToken.Parse(response.Content) as JArray ?? throw new ImportListException(response, "Trakt did not return list choices.");
                var unpaged = page == 1 && new[] { "X-Pagination-Page", "X-Pagination-Page-Count", "X-Pagination-Item-Count", "X-Pagination-Limit" }
                    .All(name => response.HttpResponse.Headers[name] == null);
                var pages = unpaged ? 1 : HeaderNumber(response, "X-Pagination-Page-Count");
                var count = unpaged ? rows.Count : HeaderNumber(response, "X-Pagination-Item-Count");
                if (response.HttpResponse.StatusCode != HttpStatusCode.OK || (!unpaged && HeaderNumber(response, "X-Pagination-Page") != page) ||
                    pages < 0 || pages > ImportListSetOperations.MaxPages || count < 0 || (!unpaged && HeaderNumber(response, "X-Pagination-Limit") <= 0) ||
                    (expectedPages.HasValue && expectedPages != pages) || (expectedItems.HasValue && expectedItems != count))
                {
                    throw new ImportListException(response, "Trakt list choices could not be read completely. Existing selections have been retained.");
                }

                expectedPages = pages;
                expectedItems = count;
                foreach (var row in rows)
                {
                    var value = (string)row["ids"]?["slug"] ?? (string)row["ids"]?["trakt"];
                    var name = (string)row["name"];
                    if (!TraktListSettings.IsValidList(value) || name.IsNullOrWhiteSpace())
                    {
                        throw new ImportListException(response, "Trakt returned an invalid list choice.");
                    }

                    options.Add(new FieldSelectStringOption { Value = value, Name = name });
                }

                if (page >= pages)
                {
                    if (options.Count != count)
                    {
                        throw new ImportListException(response, "Trakt returned incomplete list choices.");
                    }

                    return options.DistinctBy(option => option.Value).ToList();
                }
            }

            throw new ImportListException(null, "Trakt list choices exceeded the complete pagination limit.");
        }

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
                    ImportListSetOperations.Sources(Settings.IncludedLists, Settings.Listname.IsNotNullOrWhiteSpace() ? Settings.Listname.ToUrlSlug() : null),
                    Settings.ExcludedLists,
                    ReadCompleteList),
                recordStatus,
                Settings.Limit);
        }

        private List<ImportListItemInfo> ReadCompleteList(string slug, bool applyFilters)
        {
            var request = ((TraktListRequestGenerator)GetRequestGenerator()).GetRequest(slug, 1, applyFilters);
            var key = ImportListSnapshotScope.Fingerprint("TraktSelectedList-v1", request.Url.FullUri, Settings.AccessToken, ClientId);
            var items = ImportListSnapshotScope.Current?.GetOrLoad(key, () => ReadListSnapshot(slug, applyFilters)) ?? ReadListSnapshot(slug, applyFilters);
            return ImportListSnapshotScope.CopyItems(items);
        }

        private List<ImportListItemInfo> ReadListSnapshot(string slug, bool applyFilters)
        {
            if (!TraktListSettings.IsValidList(slug))
            {
                throw new ImportListException(null, "Enter a Trakt list slug or numeric ID.");
            }

            var generator = (TraktListRequestGenerator)GetRequestGenerator();
            var result = new List<ImportListItemInfo>();
            int? expectedPages = null;
            int? expectedItems = null;
            var rawItems = 0;
            long payloadBytes = 0;
            for (var page = 1; page <= ImportListSetOperations.MaxPages; page++)
            {
                var response = FetchImportListResponse(generator.GetRequest(slug, page, applyFilters));
                ImportListSnapshotScope.AddPayloadBytes(ref payloadBytes, response.Content);
                if (response.HttpResponse.StatusCode != HttpStatusCode.OK)
                {
                    throw new ImportListException(response, "Trakt did not return a successful list response.");
                }

                var current = HeaderNumber(response, "X-Pagination-Page");
                var pages = HeaderNumber(response, "X-Pagination-Page-Count");
                var count = HeaderNumber(response, "X-Pagination-Item-Count");
                var size = HeaderNumber(response, "X-Pagination-Limit");
                if (current != page || pages < 0 || pages > ImportListSetOperations.MaxPages || count < 0 || size <= 0 ||
                    (pages == 0 && count > 0) ||
                    (expectedPages.HasValue && expectedPages != pages) || (expectedItems.HasValue && expectedItems != count))
                {
                    throw new ImportListException(response, "Trakt returned incomplete or inconsistent list pagination.");
                }

                expectedPages = pages;
                expectedItems = count;
                var rows = JToken.Parse(response.Content) as JArray ?? throw new ImportListException(response, "Trakt did not return a list of items.");
                rawItems += rows.Count;
                foreach (var row in rows)
                {
                    var type = (string)row["type"];
                    if (type is "movie" or "person")
                    {
                        continue;
                    }

                    var show = row["show"];
                    if (show is not JObject || show["ids"] is not JObject ids || type is not (null or "show" or "season" or "episode"))
                    {
                        throw new ImportListException(response, "Trakt returned an item without an identifiable show.");
                    }

                    result.Add(new ImportListItemInfo
                    {
                        Title = (string)show["title"],
                        Year = (int?)show["year"] ?? 0,
                        TvdbId = PositiveId(ids["tvdb"]),
                        TmdbId = PositiveId(ids["tmdb"]),
                        ImdbId = (string)ids["imdb"]
                    });
                }

                if (page >= pages)
                {
                    if (rawItems != count)
                    {
                        throw new ImportListException(response, "Trakt returned fewer or more list items than its complete pagination declared.");
                    }

                    return result;
                }
            }

            throw new ImportListException(null, "Trakt list pagination could not be completed. The previous list will be retained.");
        }

        private static int HeaderNumber(ImportListResponse response, string name)
        {
            if (!int.TryParse(response.HttpResponse.Headers[name]?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                throw new ImportListException(response, "Trakt omitted complete list pagination headers.");
            }

            return number;
        }

        private static int PositiveId(JToken value)
        {
            return int.TryParse((string)value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : 0;
        }
    }
}
