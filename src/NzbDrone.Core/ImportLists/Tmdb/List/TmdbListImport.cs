using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentValidation.Results;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Tmdb.List;

public class TmdbListImport : TmdbImportBase<TmdbListSettings>
{
    public TmdbListImport(IHttpClient httpClient,
                          IImportListStatusService importListStatusService,
                          IConfigService configService,
                          IParsingService parsingService,
                          ILocalizationService localizationService,
                          Logger logger)
        : base(httpClient, importListStatusService, configService, parsingService, localizationService, logger)
    {
    }

    public override int PageSize => 20;
    public override string Name => "TMDb List";

    public override object RequestAction(string action, IDictionary<string, string> query)
    {
        if (action == "getAccountLists")
        {
            return new { options = GetAccountListOptions() };
        }

        return base.RequestAction(action, query);
    }

    public override IParseImportListResponse GetParser()
    {
        return new TmdbListParser();
    }

    public override IImportListRequestGenerator GetRequestGenerator()
    {
        return new TmdbListRequestGenerator(Settings, 10);
    }

    protected override ImportListFetchResult FetchItems(Func<IImportListRequestGenerator, ImportListPageableRequestChain> selector, bool isRecent = false)
    {
        return FetchComposed(true);
    }

    protected override ValidationFailure TestConnection() => ImportListSetOperations.TestResult(FetchComposed(false));

    private ImportListFetchResult FetchComposed(bool recordStatus)
    {
        return ImportListSetOperations.Fetch(
            (ImportListDefinition)Definition,
            _importListStatusService,
            _logger,
            () => ImportListSetOperations.Compose(
                ImportListSetOperations.Sources(Settings.IncludedListIds, (Settings.ListId ?? Settings.AccountListId)?.ToString(CultureInfo.InvariantCulture)),
                Settings.ExcludedListIds,
                ReadCompleteList),
            recordStatus);
    }

    private List<ImportListItemInfo> ReadCompleteList(string listId, bool applyFilters)
    {
        if (!TmdbListSettings.IsValidListId(listId))
        {
            throw new ImportListException(null, "Enter a positive numeric TMDB list ID.");
        }

        var result = new List<ImportListItemInfo>();
        int? totalPages = null;
        int? expectedItems = null;
        var rawItems = 0;
        for (var page = 1; page <= ImportListSetOperations.MaxPages; page++)
        {
            var request = new HttpRequestBuilder(Settings.BaseUrl).Accept(HttpAccept.Json)
                .SetHeader("Authorization", $"Bearer {Settings.AuthToken}")
                .Resource($"4/list/{listId}").AddQueryParam("page", page).Build();
            var response = FetchImportListResponse(new ImportListRequest(request));
            var body = JObject.Parse(response.Content);
            if (body["results"] is not JArray items || body["page"]?.Type != JTokenType.Integer || (int)body["page"] != page ||
                body["total_pages"]?.Type != JTokenType.Integer || (int)body["total_pages"] < 0 ||
                (int)body["total_pages"] > ImportListSetOperations.MaxPages)
            {
                throw new ImportListException(response, "TMDB did not return complete list pagination.");
            }

            var pages = (int)body["total_pages"];
            if ((totalPages.HasValue && totalPages != pages) || (pages == 0 && items.Count > 0) ||
                items.Any(item => item is not JObject ||
                    ((string)item["media_type"] == "tv" && (int?)item["id"] is not > 0) ||
                    ((int?)item["id"] is > 0 && (string)item["media_type"] is not ("tv" or "movie" or "person"))))
            {
                throw new ImportListException(response, "TMDB returned inconsistent list metadata or an unknown media namespace.");
            }

            if (body["total_results"] != null)
            {
                if (body["total_results"].Type != JTokenType.Integer || (int)body["total_results"] < 0 ||
                    (expectedItems.HasValue && expectedItems != (int)body["total_results"]))
                {
                    throw new ImportListException(response, "TMDB returned inconsistent item counts.");
                }

                expectedItems = (int)body["total_results"];
            }

            if (page < pages && items.Count == 0)
            {
                throw new ImportListException(response, "TMDB omitted a nonterminal list page.");
            }

            totalPages = pages;
            rawItems += items.Count;
            result.AddRange(GetParser().ParseResponse(response));
            if (page >= pages)
            {
                if (expectedItems.HasValue && expectedItems != rawItems)
                {
                    throw new ImportListException(response, "TMDB returned an incomplete list.");
                }

                return result;
            }
        }

        throw new ImportListException(null, "TMDB list pagination could not be completed. The previous list will be retained.");
    }

    private List<FieldSelectStringOption> GetAccountListOptions()
    {
        var options = new List<FieldSelectStringOption>();

        if (Settings.AuthToken.IsNullOrWhiteSpace() || Settings.AccountId.IsNullOrWhiteSpace())
        {
            return options;
        }

        var builder = new HttpRequestBuilder(Settings.BaseUrl)
            .Accept(HttpAccept.Json)
            .Resource($"4/account/{Uri.EscapeDataString(Settings.AccountId.Trim())}/lists")
            .SetHeader("Authorization", $"Bearer {Settings.AuthToken}");

        int? expectedPages = null;
        int? expectedItems = null;
        for (var i = 1; i <= ImportListSetOperations.MaxPages; i++)
        {
            builder.AddQueryParam("page", i, true);
            var request = new ImportListRequest(builder.Build());

            var response = FetchImportListResponse(request);
            var body = JObject.Parse(response.Content);
            if (response.HttpResponse.HasHttpError || body["results"] is not JArray ||
                body["page"]?.Type != JTokenType.Integer || (int)body["page"] != i ||
                body["total_pages"]?.Type != JTokenType.Integer || (int)body["total_pages"] < 0 ||
                (int)body["total_pages"] > ImportListSetOperations.MaxPages ||
                (expectedPages.HasValue && expectedPages != (int)body["total_pages"]))
            {
                throw new ImportListException(response, "TMDB account list choices could not be read completely. Existing selections have been retained.");
            }

            expectedPages = (int)body["total_pages"];
            var resource = Json.Deserialize<TmdbPagedResource<TmdbAccountListResource>>(response.Content);
            if ((i < expectedPages && resource.Results.Count == 0) || (expectedPages == 0 && resource.Results.Count > 0) ||
                resource.Results.Any(item => item == null || item.Id <= 0 || item.Name.IsNullOrWhiteSpace()))
            {
                throw new ImportListException(response, "TMDB returned an invalid account list choice.");
            }

            if (body["total_results"] != null)
            {
                if (body["total_results"].Type != JTokenType.Integer || (int)body["total_results"] < 0 ||
                    (expectedItems.HasValue && expectedItems != (int)body["total_results"]))
                {
                    throw new ImportListException(response, "TMDB returned inconsistent account list counts.");
                }

                expectedItems = (int)body["total_results"];
            }

            options.AddRange(resource.Results
                .Select(r => new FieldSelectStringOption
                {
                    Name = r.Name,
                    Value = r.Id.ToString(CultureInfo.InvariantCulture),
                    Hint = $"(Id: {r.Id}, Items: {r.NumberOfItems})"
                }));

            if (i >= expectedPages)
            {
                if (expectedItems.HasValue && expectedItems != options.Count)
                {
                    throw new ImportListException(response, "TMDB returned incomplete account list choices.");
                }

                return options.DistinctBy(option => option.Value).ToList();
            }
        }

        throw new ImportListException(null, "TMDB account list choices exceeded the complete pagination limit.");
    }
}
