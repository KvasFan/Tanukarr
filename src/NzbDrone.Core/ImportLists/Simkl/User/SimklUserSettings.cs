using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Simkl.User
{
    public class SimklUserSettingsValidator : SimklSettingsBaseValidator<SimklUserSettings>
    {
        public SimklUserSettingsValidator()
        {
            RuleFor(c => c.ListType).NotNull();
            RuleFor(c => c.ListType).Must(value => Enum.IsDefined(typeof(SimklUserListType), value));
            RuleFor(c => c.ShowType).Must(value => Enum.IsDefined(typeof(SimklUserShowType), value));
            RuleFor(c => c.IncludedCustomListIds).Must(ValidIds).WithMessage("Use positive numeric Simkl custom-list IDs.");
            RuleFor(c => c.ExcludedCustomListIds).Must(ValidIds).WithMessage("Use positive numeric Simkl custom-list IDs.");
            RuleFor(c => c.ClientIdV2).NotEmpty().WithMessage("Custom lists require a registered AUTH V2 Client ID and a PRO or VIP account.")
                .When(c => c.IncludedCustomListIds?.Any() == true || c.ExcludedCustomListIds?.Any() == true);
            RuleFor(c => c.BaseUrl).Must(url => Uri.TryCreate(url, UriKind.Absolute, out var root) &&
                root.Scheme == Uri.UriSchemeHttps && root.UserInfo.Length == 0 && root.Query.Length == 0 && root.Fragment.Length == 0)
                .When(c => c.ClientIdV2.IsNotNullOrWhiteSpace());
        }

        private static bool ValidIds(IEnumerable<string> ids) => ids == null || ids.All(id => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0);
    }

    public class SimklUserSettings : SimklSettingsBase<SimklUserSettings>
    {
        private static readonly SimklUserSettingsValidator Validator = new();

        public SimklUserSettings()
        {
            ShowType = (int)SimklUserShowType.Shows;
            ListType = (int)SimklUserListType.Watching;
        }

        [FieldDefinition(1, Label = "ImportListsSimklSettingsShowType", Type = FieldType.Select, SelectOptions = typeof(SimklUserShowType), HelpText = "ImportListsSimklSettingsShowTypeHelpText")]
        public int ShowType { get; set; }

        [FieldDefinition(2, Label = "ImportListsSimklSettingsListType", Type = FieldType.Select, SelectOptions = typeof(SimklUserListType), HelpText = "ImportListsSimklSettingsListTypeHelpText")]
        public int ListType { get; set; }

        [FieldDefinition(3, Label = "ImportListsSimklSettingsClientIdV2", HelpText = "ImportListsSimklSettingsClientIdV2HelpText", HelpLink = "https://api.simkl.org/guides/custom-lists")]
        public string ClientIdV2 { get; set; }

        [FieldDefinition(4, Label = "ImportListsSimklSettingsClientSecretV2", HelpText = "ImportListsSimklSettingsClientSecretV2HelpText", Type = FieldType.Password, Privacy = PrivacyLevel.ApiKey, Advanced = true)]
        public string ClientSecretV2 { get; set; }

        [FieldDefinition(5, Label = "ImportListsSimklSettingsIncludedCustomLists", Type = FieldType.Tag, SelectOptionsProviderAction = "getCustomLists", HelpText = "ImportListsSimklSettingsIncludedCustomListsHelpText")]
        public IEnumerable<string> IncludedCustomListIds { get; set; } = Array.Empty<string>();

        [FieldDefinition(6, Label = "ImportListsSimklSettingsExcludedCustomLists", Type = FieldType.Tag, SelectOptionsProviderAction = "getCustomLists", HelpText = "ImportListsSimklSettingsExcludedCustomListsHelpText")]
        public IEnumerable<string> ExcludedCustomListIds { get; set; } = Array.Empty<string>();

        public override NzbDroneValidationResult Validate()
        {
            ClientIdV2 = ClientIdV2?.Trim();
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
