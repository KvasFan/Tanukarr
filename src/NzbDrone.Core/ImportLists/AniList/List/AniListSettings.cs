using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.AniList.List
{
    public class AniListSettingsValidator : AniListSettingsBaseValidator<AniListSettings>
    {
        public AniListSettingsValidator()
        {
            RuleFor(c => c.Username).NotEmpty();

            RuleFor(c => c.ImportCurrent).NotEmpty()
                                         .WithMessage("At least one status type must be selected")
                                         .When(c => !(c.ImportPlanning || c.ImportCompleted || c.ImportDropped || c.ImportPaused || c.ImportRepeating));

            RuleFor(c => c.ImportFinished).NotEmpty()
                                          .WithMessage("At least one media release status must be selected")
                                          .When(c => !(c.ImportReleasing || c.ImportUnreleased || c.ImportCancelled || c.ImportHiatus));

            RuleForEach(c => c.IncludedCustomLists).Must(name => name.IsNotNullOrWhiteSpace())
                                                 .WithMessage("Custom list names must not be empty or whitespace.");

            RuleForEach(c => c.ExcludedCustomLists).Must(name => name.IsNotNullOrWhiteSpace())
                                                 .WithMessage("Custom list names must not be empty or whitespace.");
        }
    }

    public class AniListSettings : AniListSettingsBase<AniListSettings>, IJsonOnDeserialized
    {
        public const string SectionImport = "Import List Status";
        public const string SectionMedia = "Import Media Release Status";

        private static readonly AniListSettingsValidator Validator = new();

        public AniListSettings()
        {
            CustomList = string.Empty;
            ImportCurrent = true;
            ImportPlanning = true;
            ImportReleasing = true;
            ImportFinished = true;
            IncludedCustomLists = Array.Empty<string>();
            ExcludedCustomLists = Array.Empty<string>();
        }

        [FieldDefinition(1, Label = "Username", HelpText = "ImportListsAniListSettingsUsernameHelpText")]
        public string Username { get; set; }

        // Retain the old API field so older clients can submit it; new forms use
        // IncludedCustomLists, which receives this selection when settings load.
        [FieldDefinition(2, Label = "ImportListsAniListSettingsCustomList", Hidden = HiddenType.Hidden)]
        public string CustomList { get; set; }

        [FieldDefinition(3, Label = "ImportListsAniListSettingsImportWatching", Type = FieldType.Checkbox, Section = SectionImport, HelpText = "ImportListsAniListSettingsImportWatchingHelpText")]
        public bool ImportCurrent { get; set; }

        [FieldDefinition(4, Label = "ImportListsAniListSettingsImportPlanning", Type = FieldType.Checkbox, Section = SectionImport, HelpText = "ImportListsAniListSettingsImportPlanningHelpText")]
        public bool ImportPlanning { get; set; }

        [FieldDefinition(5, Label = "ImportListsAniListSettingsImportCompleted", Type = FieldType.Checkbox, Section = SectionImport, HelpText = "ImportListsAniListSettingsImportCompletedHelpText")]
        public bool ImportCompleted { get; set; }

        [FieldDefinition(6, Label = "ImportListsAniListSettingsImportDropped", Type = FieldType.Checkbox, Section = SectionImport, HelpText = "ImportListsAniListSettingsImportDroppedHelpText")]
        public bool ImportDropped { get; set; }

        [FieldDefinition(7, Label = "ImportListsAniListSettingsImportPaused", Type = FieldType.Checkbox, Section = SectionImport, HelpText = "ImportListsAniListSettingsImportPausedHelpText")]
        public bool ImportPaused { get; set; }

        [FieldDefinition(8, Label = "ImportListsAniListSettingsImportRepeating", Type = FieldType.Checkbox, Section = SectionImport, HelpText = "ImportListsAniListSettingsImportRepeatingHelpText")]
        public bool ImportRepeating { get; set; }

        [FieldDefinition(9, Label = "ImportListsAniListSettingsImportFinished", Type = FieldType.Checkbox, Section = SectionMedia, HelpText = "ImportListsAniListSettingsImportFinishedHelpText")]
        public bool ImportFinished { get; set; }

        [FieldDefinition(10, Label = "ImportListsAniListSettingsImportReleasing", Type = FieldType.Checkbox, Section = SectionMedia, HelpText = "ImportListsAniListSettingsImportReleasingHelpText")]
        public bool ImportReleasing { get; set; }

        [FieldDefinition(11, Label = "ImportListsAniListSettingsImportNotYetReleased", Type = FieldType.Checkbox, Section = SectionMedia, HelpText = "ImportListsAniListSettingsImportNotYetReleasedHelpText")]
        public bool ImportUnreleased { get; set; }

        [FieldDefinition(12, Label = "ImportListsAniListSettingsImportCancelled", Type = FieldType.Checkbox, Section = SectionMedia, HelpText = "ImportListsAniListSettingsImportCancelledHelpText")]
        public bool ImportCancelled { get; set; }

        [FieldDefinition(13, Label = "ImportListsAniListSettingsImportHiatus", Type = FieldType.Checkbox, Section = SectionMedia, HelpText = "ImportListsAniListSettingsImportHiatusHelpText")]
        public bool ImportHiatus { get; set; }

        [FieldDefinition(14, Label = "ImportListsAniListSettingsIncludedCustomLists", Type = FieldType.Tag, HelpText = "ImportListsAniListSettingsIncludedCustomListsHelpText")]
        public IEnumerable<string> IncludedCustomLists { get; set; }

        [FieldDefinition(15, Label = "ImportListsAniListSettingsExcludedCustomLists", Type = FieldType.Tag, HelpText = "ImportListsAniListSettingsExcludedCustomListsHelpText")]
        public IEnumerable<string> ExcludedCustomLists { get; set; }

        [FieldDefinition(16, Label = "ImportListsSyncWatchedProgress", Type = FieldType.Checkbox, HelpText = "ImportListsSyncWatchedProgressHelpText")]
        public bool SyncWatchedProgress { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            NormalizeLegacyCustomList();
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        void IJsonOnDeserialized.OnDeserialized()
        {
            NormalizeLegacyCustomList();
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            NormalizeLegacyCustomList();
        }

        private void NormalizeLegacyCustomList()
        {
            if (CustomList.IsNotNullOrWhiteSpace())
            {
                // The legacy textbox trimmed its single name. Preserve literal
                // modern names, including case and whitespace, when merging it.
                var included = (IncludedCustomLists ?? Array.Empty<string>()).ToList();
                var legacy = CustomList.Trim();
                if (!included.Contains(legacy, StringComparer.Ordinal))
                {
                    included.Add(legacy);
                }

                IncludedCustomLists = included.ToArray();
            }

            CustomList = string.Empty;
        }
    }
}
