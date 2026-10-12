using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public class JellyfinSettingsValidator : AbstractValidator<JellyfinSettings>
    {
        public JellyfinSettingsValidator()
        {
            RuleFor(c => c.Host).ValidHost();
            RuleFor(c => c.Port).InclusiveBetween(1, 65535);
            RuleFor(c => c.UrlBase).Must(BeUrlPath)
                .WithMessage("Must be a URL path without a query, fragment, credentials, or protocol (for example, /jellyfin).");
            RuleFor(c => c.ApiKey).NotEmpty()
                .Must(BeHeaderToken)
                .WithMessage("The API key must not contain whitespace, control characters, quotes, commas, or backslashes.");
            RuleFor(c => c.LibraryId).Must(c => Guid.TryParse(c, out var id) && id != Guid.Empty)
                .WithMessage("Select a Jellyfin library.");
            RuleFor(c => c.RefreshMode).Must(c => Enum.IsDefined(typeof(JellyfinRefreshMode), c))
                .WithMessage("Select a supported refresh mode.");
            RuleFor(c => c.RefreshIntervalMinutes).GreaterThanOrEqualTo(1);
        }

        private static bool BeUrlPath(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            foreach (var character in value)
            {
                if (char.IsControl(character) || char.IsWhiteSpace(character) || character is '?' or '#' or ':' or '\\')
                {
                    return false;
                }
            }

            return !value.StartsWith("//", StringComparison.Ordinal);
        }

        private static bool BeHeaderToken(string value)
        {
            if (value == null)
            {
                return true;
            }

            foreach (var character in value)
            {
                if (char.IsControl(character) || char.IsWhiteSpace(character) || character is '"' or ',' or '\\')
                {
                    return false;
                }
            }

            return true;
        }
    }

    public class JellyfinSettings : NotificationSettingsBase<JellyfinSettings>
    {
        private static readonly JellyfinSettingsValidator Validator = new();

        public JellyfinSettings()
        {
            Port = 8096;
            RefreshMode = (int)JellyfinRefreshMode.Interval;
            RefreshIntervalMinutes = 60;
        }

        [FieldDefinition(0, Label = "Host")]
        public string Host { get; set; }

        [FieldDefinition(1, Label = "Port", Type = FieldType.Number)]
        public int Port { get; set; }

        [FieldDefinition(2, Label = "UseSsl", Type = FieldType.Checkbox, HelpText = "NotificationsSettingsUseSslHelpText")]
        [FieldToken(TokenField.HelpText, "UseSsl", "serviceName", "Jellyfin")]
        public bool UseSsl { get; set; }

        [FieldDefinition(3, Label = "UrlBase", Advanced = true, HelpText = "ConnectionSettingsUrlBaseHelpText")]
        [FieldToken(TokenField.HelpText, "UrlBase", "connectionName", "Jellyfin")]
        [FieldToken(TokenField.HelpText, "UrlBase", "url", "http://[host]:[port]/jellyfin")]
        public string UrlBase { get; set; }

        [FieldDefinition(4, Label = "ApiKey", Privacy = PrivacyLevel.ApiKey, HelpText = "NotificationsJellyfinSettingsApiKeyHelpText")]
        public string ApiKey { get; set; }

        [FieldDefinition(5, Label = "NotificationsJellyfinSettingsLibrary", Type = FieldType.Select, SelectOptionsProviderAction = "libraries", HelpText = "NotificationsJellyfinSettingsLibraryHelpText")]
        public string LibraryId { get; set; }

        [FieldDefinition(6, Label = "NotificationsJellyfinSettingsRefreshMode", Type = FieldType.Select, SelectOptions = typeof(JellyfinRefreshMode), HelpText = "NotificationsJellyfinSettingsRefreshModeHelpText")]
        public int RefreshMode { get; set; }

        [FieldDefinition(7, Label = "NotificationsJellyfinSettingsRefreshInterval", Type = FieldType.Number, Unit = "minutes", HelpText = "NotificationsJellyfinSettingsRefreshIntervalHelpText")]
        public int RefreshIntervalMinutes { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
