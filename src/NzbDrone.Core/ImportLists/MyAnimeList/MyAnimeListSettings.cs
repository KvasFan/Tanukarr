using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    public class MalSettingsValidator : AbstractValidator<MyAnimeListSettings>
    {
        public MalSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.BaseUrl).Must(url => Uri.TryCreate(url, UriKind.Absolute, out var root) &&
                root.Scheme == Uri.UriSchemeHttps && root.UserInfo.Length == 0 && root.Query.Length == 0 && root.Fragment.Length == 0)
                .WithMessage("MyAnimeList API requests require an HTTPS root URL without user information, a query or fragment.");
            RuleFor(c => c.AccessToken).NotEmpty().OverridePropertyName("SignIn")
                .WithMessage("Authenticate with MyAnimeList, or enter a Client ID and a public username.")
                .When(c => c.ClientId.IsNullOrWhiteSpace() || c.Username == "@me");
            RuleFor(c => c.Username).NotEmpty().Matches("^(@me|[A-Za-z0-9_-]+)$");

            RuleFor(c => c.MinimumScore)
                .InclusiveBetween(0, 10)
                .WithMessage("Must be between 0 and 10");

            RuleFor(c => c.ListStatuses).Custom((statuses, context) =>
            {
                if (statuses == null || !statuses.Any() || statuses.Any(status => !Enum.IsDefined(typeof(MyAnimeListStatus), status)))
                {
                    context.AddFailure("Select at least one valid MyAnimeList account status.");
                }
            });
            RuleFor(c => c.IncludedTags).Must(tags => tags == null || tags.All(tag => tag.IsNotNullOrWhiteSpace()));
            RuleFor(c => c.ExcludedTags).Must(tags => tags == null || tags.All(tag => tag.IsNotNullOrWhiteSpace()));
        }
    }

    public class MyAnimeListSettings : ImportListSettingsBase<MyAnimeListSettings>, IJsonOnDeserialized
    {
        private static readonly MalSettingsValidator Validator = new();
        private IEnumerable<int> _listStatuses;

        public override string BaseUrl { get; set; }  = "https://api.myanimelist.net/v2";

        [FieldDefinition(0, Label = "ImportListsMyAnimeListSettingsListStatus", Type = FieldType.Select, SelectOptions = typeof(MyAnimeListStatus), Hidden = HiddenType.Hidden)]
        public int ListStatus { get; set; }

        [FieldDefinition(1, Label = "ImportListsMyAnimeListSettingsUsername", HelpText = "ImportListsMyAnimeListSettingsUsernameHelpText")]
        public string Username { get; set; } = "@me";

        [FieldDefinition(2, Label = "ImportListsMyAnimeListSettingsClientId", HelpText = "ImportListsMyAnimeListSettingsClientIdHelpText", HelpLink = "https://myanimelist.net/apiconfig")]
        public string ClientId { get; set; }

        [FieldDefinition(3, Label = "ImportListsMyAnimeListSettingsClientSecret", HelpText = "ImportListsMyAnimeListSettingsClientSecretHelpText", Type = FieldType.Password, Privacy = PrivacyLevel.ApiKey, Advanced = true)]
        public string ClientSecret { get; set; }

        [FieldDefinition(4, Label = "ImportListsMyAnimeListSettingsListStatuses", Type = FieldType.TagSelect, SelectOptions = typeof(MyAnimeListStatus), HelpText = "ImportListsMyAnimeListSettingsListStatusesHelpText")]
        public IEnumerable<int> ListStatuses
        {
            get => _listStatuses ?? new[] { (int)MyAnimeListStatus.All };
            set => _listStatuses = value;
        }

        [FieldDefinition(5, Label = "ImportListsMyAnimeListSettingsIncludedTags", Type = FieldType.Tag, HelpText = "ImportListsMyAnimeListSettingsIncludedTagsHelpText")]
        public IEnumerable<string> IncludedTags { get; set; } = Array.Empty<string>();

        [FieldDefinition(6, Label = "ImportListsMyAnimeListSettingsExcludedTags", Type = FieldType.Tag, HelpText = "ImportListsMyAnimeListSettingsExcludedTagsHelpText")]
        public IEnumerable<string> ExcludedTags { get; set; } = Array.Empty<string>();

        [FieldDefinition(7, Label = "ImportListsMyAnimeListSettingsImportFinished", Type = FieldType.Checkbox)]
        public bool ImportFinished { get; set; } = true;

        [FieldDefinition(8, Label = "ImportListsMyAnimeListSettingsImportAiring", Type = FieldType.Checkbox)]
        public bool ImportAiring { get; set; } = true;

        [FieldDefinition(9, Label = "ImportListsMyAnimeListSettingsImportUnreleased", Type = FieldType.Checkbox)]
        public bool ImportUnreleased { get; set; } = true;

        [FieldDefinition(10, Label = "ImportListsMyAnimeListSettingsScore", HelpText = "ImportListsMyAnimeListSettingsScoreHelpText")]
        public int MinimumScore { get; set; } = 0;

        [FieldDefinition(0, Label = "ImportListsSettingsAccessToken", Type = FieldType.Textbox, Privacy = PrivacyLevel.ApiKey, Hidden = HiddenType.Hidden)]
        public string AccessToken { get; set; }

        [FieldDefinition(0, Label = "ImportListsSettingsRefreshToken", Type = FieldType.Textbox, Privacy = PrivacyLevel.ApiKey, Hidden = HiddenType.Hidden)]
        public string RefreshToken { get; set; }

        [FieldDefinition(0, Label = "ImportListsSettingsExpires", Type = FieldType.Textbox, Hidden = HiddenType.Hidden)]
        public DateTime Expires { get; set; }

        [FieldDefinition(99, Label = "ImportListsMyAnimeListSettingsAuthenticateWithMyAnimeList", Type = FieldType.OAuth)]
        public string SignIn { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            NormalizeLegacyStatus();
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        [OnDeserialized]
        internal void OnDeserialized(StreamingContext context)
        {
            NormalizeLegacyStatus();
        }

        void IJsonOnDeserialized.OnDeserialized()
        {
            NormalizeLegacyStatus();
        }

        public void NormalizeLegacyStatus()
        {
            _listStatuses ??= new[] { ListStatus };
            ListStatus = 0;
        }
    }
}
