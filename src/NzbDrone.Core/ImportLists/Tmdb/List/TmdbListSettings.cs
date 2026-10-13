using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Tmdb.List;

public class TmdbListSettingsValidator : TmdbSettingsBaseValidator<TmdbListSettings>
{
    public TmdbListSettingsValidator()
    {
        RuleFor(c => c.ListId)
            .GreaterThan(0)
            .When(c => c.ListId.HasValue && !c.AccountListId.HasValue);

        RuleFor(c => c.AccountListId)
            .GreaterThan(0)
            .When(c => c.AccountListId.HasValue && !c.ListId.HasValue);

        RuleForEach(c => c.IncludedListIds).Must(TmdbListSettings.IsValidListId).WithMessage("Enter a positive numeric TMDB list ID.");
        RuleForEach(c => c.ExcludedListIds).Must(TmdbListSettings.IsValidListId).WithMessage("Enter a positive numeric TMDB list ID.");

        RuleFor(c => c).Custom((settings, context) =>
        {
            if (!settings.ListId.HasValue && !settings.AccountListId.HasValue && ImportListSetOperations.Sources(settings.IncludedListIds).Length == 0)
            {
                context.AddFailure(nameof(settings.ListId), "Must provide a list id, or select an account list.");
                context.AddFailure(nameof(settings.AccountListId), "Must select an account list, or provide a list id.");
            }
            else if (settings.ListId.HasValue && settings.AccountListId.HasValue)
            {
                context.AddFailure(nameof(settings.ListId), "Account list has already been selected.");
                context.AddFailure(nameof(settings.AccountListId), "List id has already been provided.");
            }
        });
    }
}

public class TmdbListSettings : TmdbSettingsBase<TmdbListSettings>, IJsonOnDeserialized
{
    private static readonly TmdbListSettingsValidator Validator = new();

    public TmdbListSettings()
        : base(Validator)
    {
    }

    [FieldDefinition(1, Label = "ImportListsTmdbSettingsAccountListId", Hidden = HiddenType.Hidden)]
    public int? AccountListId { get; set; }

    [FieldDefinition(2, Label = "ImportListsTmdbSettingsListId", Hidden = HiddenType.Hidden)]
    public int? ListId { get; set; }

    [FieldDefinition(3, Label = "ImportListsIncludedLists", HelpText = "ImportListsTmdbIncludedListsHelp", Type = FieldType.Tag)]
    public IEnumerable<string> IncludedListIds { get; set; } = Array.Empty<string>();

    [FieldDefinition(4, Label = "ImportListsExcludedLists", HelpText = "ImportListsTmdbExcludedListsHelp", Type = FieldType.Tag)]
    public IEnumerable<string> ExcludedListIds { get; set; } = Array.Empty<string>();

    public override NzbDroneValidationResult Validate()
    {
        NormalizeLegacy();
        return base.Validate();
    }

    public static bool IsValidListId(string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
    }

    void IJsonOnDeserialized.OnDeserialized() => NormalizeLegacy();

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context) => NormalizeLegacy();

    private void NormalizeLegacy()
    {
        if (ListId.HasValue && AccountListId.HasValue)
        {
            return;
        }

        var id = ListId ?? AccountListId;
        if (id > 0)
        {
            IncludedListIds = ImportListSetOperations.Sources(IncludedListIds, id.Value.ToString(CultureInfo.InvariantCulture));
            ListId = null;
            AccountListId = null;
        }
    }
}
