using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public class Jellyfin : NotificationBase<JellyfinSettings>
    {
        private readonly IJellyfinProxy _proxy;
        private readonly IJellyfinRefreshService _refreshService;

        public Jellyfin(IJellyfinProxy proxy, IJellyfinRefreshService refreshService)
        {
            _proxy = proxy;
            _refreshService = refreshService;
        }

        public override string Name => "Jellyfin";
        public override string Link => "https://jellyfin.org/docs/general/server/";

        public override void OnDownload(DownloadMessage message)
        {
            _refreshService.OnImport((NotificationDefinition)Definition);
        }

        public override ValidationResult Test()
        {
            var validation = Settings.Validate();

            if (!validation.IsValid)
            {
                return validation;
            }

            try
            {
                _proxy.TestLibrary(Settings);
                return new ValidationResult();
            }
            catch (JellyfinException exception)
            {
                return new ValidationResult(new[] { new ValidationFailure(exception.PropertyName, exception.Message) });
            }
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "libraries")
            {
                Settings.Validate().Filter("Host", "Port", "UseSsl", "UrlBase", "ApiKey").ThrowOnError();

                var options = _proxy.GetLibraries(Settings)
                    .OrderBy(c => c.Name, StringComparer.InvariantCultureIgnoreCase)
                    .Select(c => new FieldSelectStringOption
                    {
                        Value = c.ItemId,
                        Name = c.Name,
                        Hint = c.CollectionType
                    });

                return new { options };
            }

            return new { };
        }
    }
}
