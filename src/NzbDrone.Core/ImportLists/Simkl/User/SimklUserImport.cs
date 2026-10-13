using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Simkl.User
{
    public class SimklUserImport : SimklImportBase<SimklUserSettings>
    {
        private readonly ISimklV2AuthService _auth;
        private readonly ISimklCustomListReader _reader;
        private readonly IImportListRepository _repository;

        public SimklUserImport(IImportListRepository netImportRepository,
                               IHttpClient httpClient,
                               IImportListStatusService netImportStatusService,
                               IConfigService configService,
                               IParsingService parsingService,
                               ILocalizationService localizationService,
                               ISimklV2AuthService auth,
                               ISimklCustomListReader reader,
                               Logger logger)
        : base(netImportRepository, httpClient, netImportStatusService, configService, parsingService, localizationService, logger)
        {
            _auth = auth;
            _reader = reader;
            _repository = netImportRepository;
        }

        public override string Name => _localizationService.GetLocalizedString("ImportListsSimklSettingsName");

        private bool UsesV2 => Settings.ClientIdV2.IsNotNullOrWhiteSpace();
        private bool HasCustomFilters => Settings.IncludedCustomListIds?.Any() == true || Settings.ExcludedCustomListIds?.Any() == true;

        public override ImportListFetchResult Fetch()
        {
            if (!UsesV2 && !HasCustomFilters)
            {
                return base.Fetch();
            }

            try
            {
                Settings.Validate().ThrowOnError();
                if (_auth.RefreshIfNeeded(Settings) && Definition.Id > 0)
                {
                    _repository.UpdateSettings((ImportListDefinition)Definition);
                }

                var result = _reader.Read(Settings);
                _reader.Commit(Settings, result);
                _importListStatusService.RecordSuccess(Definition.Id);
                return new ImportListFetchResult(CleanupListItems(result.Items), false);
            }
            catch (TooManyRequestsException ex)
            {
                _importListStatusService.RecordFailure(Definition.Id, ex.RetryAfter > TimeSpan.Zero ? ex.RetryAfter : TimeSpan.FromMinutes(5));
                _logger.Warn("Simkl request limit reached; the previous complete list will be retained.");
            }
            catch (Exception ex)
            {
                _importListStatusService.RecordFailure(Definition.Id);
                _logger.Warn("Simkl custom list import failed ({0}); the previous complete list will be retained. Test the list for details.", ex.GetType().Name);
            }

            return new ImportListFetchResult(Array.Empty<ImportListItemInfo>(), true);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            if (!UsesV2 && !HasCustomFilters)
            {
                base.Test(failures);
                return;
            }

            try
            {
                Settings.Validate().ThrowOnError();
                _auth.RefreshIfNeeded(Settings);
                _reader.Read(Settings);
            }
            catch (Exception ex)
            {
                var message = ex is ImportListException || ex is ValidationException
                    ? ex.Message : "Simkl could not confirm the complete selected lists. Check AUTH V2 authentication, PRO/VIP membership and retry later.";
                failures.Add(new ValidationFailure(string.Empty, message));
            }
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "getCustomLists")
            {
                if (!UsesV2 || Settings.AccessToken.IsNullOrWhiteSpace())
                {
                    throw new ValidationException(new[] { new ValidationFailure("ClientIdV2", "Authenticate with your registered AUTH V2 app and a Simkl PRO/VIP account before loading custom lists.") });
                }

                try
                {
                    return new { options = _reader.GetOptions(Settings) };
                }
                catch (Exception)
                {
                    throw new ValidationException(new[] { new ValidationFailure("IncludedCustomListIds", "Simkl could not return complete custom-list choices. Check AUTH V2/PRO-VIP access, or enter positive list IDs manually; existing selections were retained.") });
                }
            }

            return UsesV2 ? _auth.RequestAction(action, query, Settings) : base.RequestAction(action, query);
        }

        protected override IList<ImportListItemInfo> CleanupListItems(IEnumerable<ImportListItemInfo> releases)
        {
            if (!UsesV2)
            {
                return base.CleanupListItems(releases);
            }

            var result = releases.DistinctBy(item => new { item.TvdbId, item.TmdbId, item.MalId, item.ImdbId }).ToList();
            result.ForEach(item =>
            {
                item.ImportListId = Definition.Id;
                item.ImportList = Definition.Name;
            });
            return result;
        }

        public override IImportListRequestGenerator GetRequestGenerator()
        {
            return new SimklUserRequestGenerator
            {
                Settings = Settings,
                ClientId = ClientId
            };
        }
    }
}
