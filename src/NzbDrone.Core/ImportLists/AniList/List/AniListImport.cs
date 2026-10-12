using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.CloudFlare;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.AniList.List
{
    internal class AniListImport : AniListImportBase<AniListSettings>
    {
        public AniListImport(IImportListRepository netImportRepository,
                    IHttpClient httpClient,
                    IImportListStatusService importListStatusService,
                    IConfigService configService,
                    IParsingService parsingService,
                    ILocalizationService localizationService,
                    Logger logger)
        : base(netImportRepository, httpClient, importListStatusService, configService, parsingService, localizationService, logger)
        {
        }

        public override string Name => _localizationService.GetLocalizedString("TypeOfList", new Dictionary<string, object> { { "typeOfList", "AniList" } });

        public override AniListRequestGenerator GetRequestGenerator()
        {
            return new AniListRequestGenerator()
            {
                Settings = Settings,
                ClientId = ClientId
            };
        }

        public override AniListParser GetParser()
        {
            return new AniListParser(Settings);
        }

        protected override bool IsValidItem(ImportListItemInfo listItem)
        {
            return listItem.AniListId > 0;
        }

        protected override IList<ImportListItemInfo> CleanupListItems(IEnumerable<ImportListItemInfo> releases)
        {
            var result = releases.DistinctBy(r => r.AniListId).ToList();

            result.ForEach(item =>
            {
                item.ImportListId = Definition.Id;
                item.ImportList = Definition.Name;
            });

            return result;
        }

        private static void ValidatePagination(ImportListResponse response, PageInfo pageInfo, int requestedPage)
        {
            if (pageInfo.CurrentPage != requestedPage)
            {
                throw new ImportListException(response, "AniList returned page {0} when page {1} was requested.", pageInfo.CurrentPage, requestedPage);
            }

            if (pageInfo.HasNextPage == true && requestedPage >= AniListAPI.MaxPages)
            {
                throw new ImportListException(response, "AniList's 5000-entry pagination limit was reached. Narrow the selected list statuses before syncing.");
            }
        }

        protected override ImportListFetchResult FetchItems(Func<IImportListRequestGenerator, ImportListPageableRequestChain> pageableRequestChainSelector, bool isRecent = false)
        {
            var releases = new List<ImportListItemInfo>();
            var url = string.Empty;
            var anyFailure = true;

            try
            {
                var generator = GetRequestGenerator();
                var parser = GetParser();
                var pageIndex = 1;
                var hasNextPage = false;
                ImportListRequest currentRequest = null;

                // Anilist caps the result list to 50 items at maximum per query, so the data must be pulled in batches.
                // The number of pages are not known upfront, so the fetch logic must be changed to look at the returned page data.
                do
                {
                    // Build the query for the current page
                    currentRequest = generator.GetRequest(pageIndex);
                    url = currentRequest.Url.FullUri;

                    // Fetch and parse the response
                    var response = FetchImportListResponse(currentRequest);
                    var page = parser.ParseResponse(response, out var pageInfo).ToList();
                    ValidatePagination(response, pageInfo, pageIndex);
                    releases.AddRange(page.Where(IsValidItem));

                    hasNextPage = pageInfo.HasNextPage == true;
                    pageIndex++;
                }
                while (hasNextPage);

                // A later page can veto an earlier occurrence of the same media ID.
                releases.RemoveAll(item => parser.ExcludedMediaIds.Contains(item.AniListId));

                _importListStatusService.RecordSuccess(Definition.Id);
                anyFailure = false;
            }
            catch (WebException webException)
            {
                if (webException.Status == WebExceptionStatus.NameResolutionFailure ||
                    webException.Status == WebExceptionStatus.ConnectFailure)
                {
                    _importListStatusService.RecordConnectionFailure(Definition.Id);
                }
                else
                {
                    _importListStatusService.RecordFailure(Definition.Id);
                }

                if (webException.Message.Contains("502") || webException.Message.Contains("503") ||
                    webException.Message.Contains("timed out"))
                {
                    _logger.Warn("{0} server is currently unavailable. {1} {2}", this, url, webException.Message);
                }
                else
                {
                    _logger.Warn("{0} {1} {2}", this, url, webException.Message);
                }
            }
            catch (TooManyRequestsException ex)
            {
                if (ex.RetryAfter != TimeSpan.Zero)
                {
                    _importListStatusService.RecordFailure(Definition.Id, ex.RetryAfter);
                }
                else
                {
                    _importListStatusService.RecordFailure(Definition.Id, TimeSpan.FromHours(1));
                }

                _logger.Warn("API Request Limit reached for {0}", this);
            }
            catch (HttpException ex)
            {
                _importListStatusService.RecordFailure(Definition.Id);
                _logger.Warn("{0} {1}", this, ex.Message);
            }
            catch (RequestLimitReachedException)
            {
                _importListStatusService.RecordFailure(Definition.Id, TimeSpan.FromHours(1));
                _logger.Warn("API Request Limit reached for {0}", this);
            }
            catch (CloudFlareCaptchaException ex)
            {
                _importListStatusService.RecordFailure(Definition.Id);
                ex.WithData("FeedUrl", url);

                if (ex.IsExpired)
                {
                    _logger.Error(ex, "Expired CAPTCHA token for {0}, please refresh in import list settings.", this);
                }
                else
                {
                    _logger.Error(ex, "CAPTCHA token required for {0}, check import list settings.", this);
                }
            }
            catch (ImportListException ex)
            {
                _importListStatusService.RecordFailure(Definition.Id);
                _logger.Warn(ex, "{0}", url);
            }
            catch (Exception ex)
            {
                _importListStatusService.RecordFailure(Definition.Id);
                ex.WithData("FeedUrl", url);
                _logger.Error(ex, "An error occurred while processing feed. {0}", url);
            }

            return new ImportListFetchResult(CleanupListItems(releases), anyFailure);
        }

        protected override ValidationFailure TestConnection()
        {
            try
            {
                var parser = GetParser();
                var generator = GetRequestGenerator();
                var pageIndex = 1;
                var continueTesting = true;
                var matchingIds = new HashSet<int>();
                var checkAllPages = Settings.ExcludedCustomLists?.Any() == true ||
                                    Settings.ExcludeCurrent || Settings.ExcludePlanning ||
                                    Settings.ExcludeCompleted || Settings.ExcludeDropped ||
                                    Settings.ExcludePaused || Settings.ExcludeRepeating;

                // Anilist caps the result list to 50 items at maximum per query, so the data must be pulled in batches.
                // The number of pages are not known upfront, so the fetch logic must be changed to look at the returned page data.
                do
                {
                    var currentRequest = generator.GetRequest(pageIndex);
                    var response = FetchImportListResponse(currentRequest);
                    var page = parser.ParseResponse(response, out var pageInfo).ToList();
                    ValidatePagination(response, pageInfo, pageIndex);

                    matchingIds.UnionWith(page.Where(IsValidItem).Select(item => item.AniListId));

                    // Exclusions may veto an earlier result, so test every page when enabled.
                    continueTesting = pageInfo.HasNextPage == true && (checkAllPages || page.Count == 0);
                    pageIndex++;
                }
                while (continueTesting);

                matchingIds.ExceptWith(parser.ExcludedMediaIds);

                if (matchingIds.Count == 0)
                {
                    return new NzbDroneValidationFailure(string.Empty,
                            "No results were returned from your import list, please check your settings and the log for details.")
                        { IsWarning = true };
                }
            }
            catch (RequestLimitReachedException)
            {
                _logger.Warn("Request limit reached");
                return new ValidationFailure(string.Empty, "AniList request limit reached. Retry the connection test later.");
            }
            catch (UnsupportedFeedException ex)
            {
                _logger.Warn(ex, "Import list feed is not supported");

                return new ValidationFailure(string.Empty, "Import list feed is not supported: " + ex.Message);
            }
            catch (ImportListException ex)
            {
                _logger.Warn(ex, "Unable to connect to import list");

                return new ValidationFailure(string.Empty, $"Unable to connect to import list: {ex.Message}. Check the log surrounding this error for details.");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to connect to import list");

                return new ValidationFailure(string.Empty, $"Unable to connect to import list: {ex.Message}. Check the log surrounding this error for details.");
            }

            return null;
        }
    }
}
