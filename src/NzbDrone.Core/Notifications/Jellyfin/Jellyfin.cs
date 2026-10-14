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
        private readonly IJellyfinWatchProxy _watchProxy;
        private readonly INotificationRepository _notificationRepository;

        public Jellyfin(IJellyfinProxy proxy, IJellyfinRefreshService refreshService, IJellyfinWatchProxy watchProxy, INotificationRepository notificationRepository)
        {
            _proxy = proxy;
            _refreshService = refreshService;
            _watchProxy = watchProxy;
            _notificationRepository = notificationRepository;
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
                if (Settings.EnableWatchedSync)
                {
                    if (_notificationRepository.All().Any(definition => definition.Id != Definition.Id &&
                        definition.Implementation == "Jellyfin" && definition.Settings is JellyfinSettings other && other.EnableWatchedSync))
                    {
                        return new ValidationResult(new[] { new ValidationFailure("EnableWatchedSync", "Only one Jellyfin connection may synchronize watched status. Disable watched sync on the other connection first.") });
                    }

                    _watchProxy.GetSnapshot(Settings);
                }

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

            if (action == "users")
            {
                var options = _watchProxy.GetUsers(Settings)
                    .OrderBy(user => user.Name, StringComparer.InvariantCultureIgnoreCase)
                    .Select(user => new FieldSelectStringOption { Value = user.Id, Name = user.Name });
                return new { options };
            }

            return new { };
        }
    }
}
