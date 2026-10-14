using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Notifications.Plex.PlexTv;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Plex
{
    public class PlexImport : HttpImportListBase<PlexListSettings>
    {
        public override string Name => _localizationService.GetLocalizedString("ImportListsPlexSettingsWatchlistName");
        public override ImportListType ListType => ImportListType.Plex;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

        public override int PageSize => 100;
        public override TimeSpan RateLimit => TimeSpan.FromSeconds(5);

        private readonly IPlexTvService _plexTvService;

        public PlexImport(IPlexTvService plexTvService,
                                  IHttpClient httpClient,
                                  IImportListStatusService importListStatusService,
                                  IConfigService configService,
                                  IParsingService parsingService,
                                  ILocalizationService localizationService,
                                  Logger logger)
            : base(httpClient, importListStatusService, configService, parsingService, localizationService, logger)
        {
            _plexTvService = plexTvService;
        }

        public override ImportListFetchResult Fetch()
        {
            Settings.Validate().Filter("AccessToken").ThrowOnError();

            return FetchItems(g => g.GetListItems());
        }

        public override IParseImportListResponse GetParser()
        {
            return new PlexParser();
        }

        public override IImportListRequestGenerator GetRequestGenerator()
        {
            return new PlexListRequestGenerator(_plexTvService, Settings, PageSize);
        }

        protected override ImportListFetchResult FetchItems(Func<IImportListRequestGenerator, ImportListPageableRequestChain> selector, bool isRecent = false)
        {
            return ReadWatchlist(true);
        }

        protected override ValidationFailure TestConnection()
        {
            var result = ReadWatchlist(false);
            if (result.AnyFailure)
            {
                return new ValidationFailure(string.Empty, "Unable to read the complete Plex Watchlist. Check authentication and availability. The previous list will be retained.");
            }

            return result.Series.Count == 0
                ? new NzbDroneValidationFailure(string.Empty, "Connected to Plex, but the complete Watchlist contains no TV series.") { IsWarning = true }
                : null;
        }

        private ImportListFetchResult ReadWatchlist(bool recordStatus)
        {
            using var snapshots = recordStatus ? ImportListSnapshotScope.BeginIfNeeded() : ImportListSnapshotScope.Begin();
            return ImportListSetOperations.Fetch(
                (ImportListDefinition)Definition,
                _importListStatusService,
                _logger,
                () => CleanupListItems(ReadCompleteWatchlist()).ToList(),
                recordStatus);
        }

        private List<ImportListItemInfo> ReadCompleteWatchlist()
        {
            var key = ImportListSnapshotScope.Fingerprint("PlexWatchlist-v1", "https://discover.provider.plex.tv/library/sections/watchlist/all", _configService.PlexClientIdentifier, Settings.AccessToken);
            var items = ImportListSnapshotScope.Current?.GetOrLoad(key, ReadWatchlistSnapshot) ?? ReadWatchlistSnapshot();
            return ImportListSnapshotScope.CopyItems(items);
        }

        private List<ImportListItemInfo> ReadWatchlistSnapshot()
        {
            var generator = new PlexListRequestGenerator(_plexTvService, Settings, PageSize);
            var parser = new PlexParser();
            var result = new List<ImportListItemInfo>();
            var offset = 0;
            int? total = null;
            long payloadBytes = 0;
            for (var index = 0; index < PlexListRequestGenerator.MaxPages; index++)
            {
                var response = FetchImportListResponse(generator.GetRequest(offset));
                ImportListSnapshotScope.AddPayloadBytes(ref payloadBytes, response.Content);
                var page = parser.ParsePage(response, offset);
                if (total.HasValue && page.Total != total)
                {
                    throw new ImportListException(response, "Plex Watchlist changed while being read. The previous list will be retained.");
                }

                total = page.Total;
                result.AddRange(page.Series);
                offset = checked(offset + page.Size);
                if (total == offset || (!total.HasValue && page.Size == 0))
                {
                    return result;
                }

                if (page.Size == 0)
                {
                    throw new ImportListException(response, "Plex Watchlist ended before its declared total. The previous list will be retained.");
                }
            }

            throw new ImportListException(null, "Plex Watchlist pagination limit was reached before completion. The previous list will be retained.");
        }

        protected override ImportListResponse FetchImportListResponse(ImportListRequest request)
        {
            request.HttpRequest.LogHttpError = false;
            request.HttpRequest.LogResponseContent = false;
            request.HttpRequest.AllowAutoRedirect = false;
            request.HttpRequest.SuppressHttpError = true;
            if (request.HttpRequest.RateLimit < RateLimit)
            {
                request.HttpRequest.RateLimit = RateLimit;
            }

            var response = _httpClient.Execute(request.HttpRequest);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new TooManyRequestsException(request.HttpRequest, response);
            }

            return new ImportListResponse(request, response);
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "startOAuth")
            {
                Settings.Validate().Filter("ConsumerKey", "ConsumerSecret").ThrowOnError();

                return _plexTvService.GetPinUrl();
            }
            else if (action == "continueOAuth")
            {
                Settings.Validate().Filter("ConsumerKey", "ConsumerSecret").ThrowOnError();

                if (query["callbackUrl"].IsNullOrWhiteSpace())
                {
                    throw new BadRequestException("QueryParam callbackUrl invalid.");
                }

                if (query["id"].IsNullOrWhiteSpace())
                {
                    throw new BadRequestException("QueryParam id invalid.");
                }

                if (query["code"].IsNullOrWhiteSpace())
                {
                    throw new BadRequestException("QueryParam code invalid.");
                }

                return _plexTvService.GetSignInUrl(query["callbackUrl"], Convert.ToInt32(query["id"]), query["code"]);
            }
            else if (action == "pollOAuth")
            {
                Settings.Validate().Filter("ConsumerKey", "ConsumerSecret").ThrowOnError();

                if (query["pinId"].IsNullOrWhiteSpace())
                {
                    throw new BadRequestException("QueryParam pinId invalid.");
                }

                var authToken = _plexTvService.GetAuthToken(Convert.ToInt32(query["pinId"]));

                return new
                {
                    success = authToken.IsNotNullOrWhiteSpace()
                };
            }
            else if (action == "getOAuthToken")
            {
                Settings.Validate().Filter("ConsumerKey", "ConsumerSecret").ThrowOnError();

                if (query["pinId"].IsNullOrWhiteSpace())
                {
                    throw new BadRequestException("QueryParam pinId invalid.");
                }

                var accessToken = _plexTvService.GetAuthToken(Convert.ToInt32(query["pinId"]));

                return new
                {
                    accessToken
                };
            }

            return new { };
        }
    }
}
