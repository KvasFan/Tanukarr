using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.AniList;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http.CloudFlare;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.AniList.List
{
    internal class AniListImport : AniListImportBase<AniListSettings>
    {
        private readonly IAniListRequestService _aniListRequestService;

        public AniListImport(IImportListRepository netImportRepository,
                    IHttpClient httpClient,
                    IAniListRequestService aniListRequestService,
                    IImportListStatusService importListStatusService,
                    IConfigService configService,
                    IParsingService parsingService,
                    ILocalizationService localizationService,
                    Logger logger)
        : base(netImportRepository, httpClient, importListStatusService, configService, parsingService, localizationService, logger)
        {
            _aniListRequestService = aniListRequestService;
        }

        public override string Name => _localizationService.GetLocalizedString("TypeOfList", new Dictionary<string, object> { { "typeOfList", "AniList" } });

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "getCustomLists")
            {
                return new { options = GetCustomListOptions() };
            }

            return base.RequestAction(action, query);
        }

        private List<FieldSelectStringOption> GetCustomListOptions()
        {
            if (Settings.Username.IsNullOrWhiteSpace())
            {
                throw new ImportListException(null, "Enter the AniList username before loading custom lists.");
            }

            // This reads only names, without fetching entries, refreshing OAuth, saving
            // provider settings or starting an import. The endpoint is fixed.
            var request = new HttpRequest("https://graphql.anilist.co", HttpAccept.Json)
            {
                Method = HttpMethod.Post,
                RateLimit = RateLimit
            };
            request.Headers.ContentType = "application/json";
            request.SetContent(Json.ToJson(new
            {
                query = AniListAPI.CustomListNamesQuery,
                variables = new { username = Settings.Username.Trim() }
            }));
            if (Settings.AccessToken.IsNotNullOrWhiteSpace())
            {
                request.Headers.Add("Authorization", "Bearer " + Settings.AccessToken);
            }

            var response = _aniListRequestService.Execute(request);
            using var document = ParseCustomListNames(response.Content);
            var root = document.RootElement;
            if (response.HasHttpError || root.ValueKind != JsonValueKind.Object ||
                (root.TryGetProperty("errors", out var errors) && errors.ValueKind != JsonValueKind.Null &&
                 (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() > 0)) ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("User", out var user) || user.ValueKind != JsonValueKind.Object ||
                !user.TryGetProperty("mediaListOptions", out var options) || options.ValueKind != JsonValueKind.Object ||
                !options.TryGetProperty("animeList", out var animeList) || animeList.ValueKind != JsonValueKind.Object ||
                !animeList.TryGetProperty("customLists", out var names) ||
                (names.ValueKind != JsonValueKind.Array && names.ValueKind != JsonValueKind.Null))
            {
                throw new ImportListException(null, "AniList did not return custom list names. Check the username and authentication, or enter the names manually.");
            }

            var result = new List<FieldSelectStringOption>();
            if (names.ValueKind == JsonValueKind.Null)
            {
                return result;
            }

            foreach (var item in names.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || item.GetString().IsNullOrWhiteSpace())
                {
                    throw new ImportListException(null, "AniList returned an invalid custom list name. Existing selections have been retained.");
                }

                var name = item.GetString();
                if (!result.Any(option => string.Equals(option.Value, name, StringComparison.Ordinal)))
                {
                    result.Add(new FieldSelectStringOption { Value = name, Name = name });
                }
            }

            return result;
        }

        private static JsonDocument ParseCustomListNames(string content)
        {
            try
            {
                return JsonDocument.Parse(content);
            }
            catch (JsonException)
            {
                throw new ImportListException(null, "AniList did not return valid list options. Existing selections have been retained; try again or enter the names manually.");
            }
        }

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

        private AniListAccountSnapshot GetAccountSnapshot()
        {
            var scope = ImportListSnapshotScope.Current;
            var generator = GetRequestGenerator();
            var key = ImportListSnapshotScope.Fingerprint("AniList",
                "complete-anime-collection-v1",
                Settings.BaseUrl.Trim(),
                Settings.Username.Trim().ToUpperInvariant(),
                Settings.AccessToken);
            var snapshot = scope.GetOrLoad(key,
                () => AniListAccountSnapshot.Load(Settings.Username, chunk => FetchImportListResponse(generator.GetCollectionRequest(chunk))),
                value => value.PayloadBytes);

            // Keep only complete direct public relationship nodes available through
            // the import scope. These do not contain any account or list attributes.
            scope.GetOrLoad(ImportListSnapshotScope.Fingerprint("AniListPublicRelations", key),
                () => AniListPublicRelationSnapshot.From(snapshot.PublicMedia),
                value => value.PayloadBytes);
            return snapshot;
        }

        protected override ImportListResponse FetchImportListResponse(ImportListRequest request)
        {
            _logger.Debug("Downloading Feed " + request.HttpRequest.ToString(false));

            if (request.HttpRequest.RateLimit < RateLimit)
            {
                request.HttpRequest.RateLimit = RateLimit;
            }

            return new ImportListResponse(request, _aniListRequestService.Execute(request.HttpRequest));
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

        protected override ImportListFetchResult FetchItems(Func<IImportListRequestGenerator, ImportListPageableRequestChain> pageableRequestChainSelector, bool isRecent = false)
        {
            var releases = new List<ImportListItemInfo>();
            var url = string.Empty;
            var anyFailure = true;

            try
            {
                using var snapshotScope = ImportListSnapshotScope.BeginIfNeeded();
                var parser = GetParser();
                url = Settings.BaseUrl.Trim();
                var snapshot = GetAccountSnapshot();
                releases.AddRange(parser.ParseEntries(snapshot.GetEntries()).Where(IsValidItem));

                // A later custom-list exclusion removes an earlier occurrence,
                // including media that matched an explicitly included custom list.
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
                if (ex.RetryAfter > TimeSpan.Zero)
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

            if (anyFailure)
            {
                releases.Clear();
            }

            return new ImportListFetchResult(CleanupListItems(releases), anyFailure);
        }

        protected override ValidationFailure TestConnection()
        {
            try
            {
                // An unsaved Test always gets a new snapshot, independently of an
                // import in the caller's execution context or an earlier Test.
                using var snapshotScope = ImportListSnapshotScope.Begin();
                var parser = GetParser();
                var snapshot = GetAccountSnapshot();
                var matches = parser.ParseEntries(snapshot.GetEntries()).Where(IsValidItem).ToList();
                if (matches.Count == 0)
                {
                    return new NzbDroneValidationFailure(string.Empty,
                            "Connected to AniList, but no entries match the selected list statuses, release statuses and custom-list filters. Adjust these filters if you expected entries.")
                        { IsWarning = true };
                }
            }
            catch (TooManyRequestsException ex)
            {
                _logger.Warn("AniList request limit reached while testing the import list");

                var retryMessage = ex.RetryAfter > TimeSpan.Zero
                    ? $" Retry the connection test in at least {Math.Ceiling(ex.RetryAfter.TotalSeconds)} seconds."
                    : " Retry the connection test later.";

                return new ValidationFailure(string.Empty, "AniList request limit reached." + retryMessage);
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
