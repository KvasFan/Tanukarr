using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Trakt.List
{
    public class TraktListSettingsValidator : TraktSettingsBaseValidator<TraktListSettings>
    {
        public TraktListSettingsValidator()
        {
            RuleFor(c => c.Username).NotEmpty();
            RuleFor(c => c.IncludedLists).Must(values => ImportListSetOperations.Sources(values).Length > 0).WithMessage("Select at least one included Trakt list.");
            RuleForEach(c => c.IncludedLists).Must(TraktListSettings.IsValidList).WithMessage("Enter a Trakt list slug or numeric ID.");
            RuleForEach(c => c.ExcludedLists).Must(TraktListSettings.IsValidList).WithMessage("Enter a Trakt list slug or numeric ID.");

            RuleFor(c => c.Years)
                .Must(BeValidYearRange)
                .When(c => c.Years.IsNotNullOrWhiteSpace())
                .WithMessage("Not a valid year or range of years");
        }
    }

    public class TraktListSettings : TraktSettingsBase<TraktListSettings>, IJsonOnDeserialized
    {
        private static readonly TraktListSettingsValidator Validator = new();

        [FieldDefinition(1, Label = "Username", HelpText = "ImportListsTraktSettingsUsernameHelpText")]
        public string Username { get; set; }

        [FieldDefinition(2, Label = "ImportListsTraktSettingsListName", Hidden = HiddenType.Hidden)]
        public string Listname { get; set; } = string.Empty;

        [FieldDefinition(4, Label = "ImportListsIncludedLists", HelpText = "ImportListsTraktIncludedListsHelp", Type = FieldType.Tag)]
        public IEnumerable<string> IncludedLists { get; set; } = Array.Empty<string>();

        [FieldDefinition(5, Label = "ImportListsExcludedLists", HelpText = "ImportListsTraktExcludedListsHelp", Type = FieldType.Tag)]
        public IEnumerable<string> ExcludedLists { get; set; } = Array.Empty<string>();

        [FieldDefinition(3, Label = "ImportListsTraktSettingsYears", HelpText = "ImportListsTraktSettingsYearsSeriesHelpText")]
        public string Years { get; set; }

        [FieldDefinition(6, Label = "ImportListsSyncWatchedProgress", Type = FieldType.Checkbox, HelpText = "ImportListsSyncWatchedProgressHelpText")]
        public bool SyncWatchedProgress { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            NormalizeLegacy();
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        public static bool IsValidList(string value)
        {
            return value != null && Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]*\z", RegexOptions.None, TimeSpan.FromSeconds(1));
        }

        void IJsonOnDeserialized.OnDeserialized() => NormalizeLegacy();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) => NormalizeLegacy();

        private void NormalizeLegacy()
        {
            if (Listname.IsNotNullOrWhiteSpace())
            {
                IncludedLists = ImportListSetOperations.Sources(IncludedLists, Listname.ToUrlSlug());
            }

            Listname = string.Empty;
        }
    }
}
