using FluentValidation;
using NzbDrone.Core.Qualities;

namespace Sonarr.Api.V3.Profiles.Quality;

public class QualityProfileResourceValidator : AbstractValidator<QualityProfileResource>
{
    public QualityProfileResourceValidator()
    {
        RuleForEach(c => c.Items)
            .SetValidator(new QualityProfileQualityItemResourceValidator());
    }
}

public class QualityProfileQualityItemResourceValidator : AbstractValidator<QualityProfileQualityItemResource>
{
    public QualityProfileQualityItemResourceValidator()
    {
        RuleForEach(c => c.Items).SetValidator(this);

        RuleFor(c => c.MinSize).Must(value => !value.HasValue || double.IsFinite(value.Value));
        RuleFor(c => c.PreferredSize).Must(value => !value.HasValue || double.IsFinite(value.Value));
        RuleFor(c => c.MaxSize).Must(value => !value.HasValue || double.IsFinite(value.Value));

        RuleFor(c => c.MinSize)
            .GreaterThanOrEqualTo(QualityDefinitionLimits.Min)
            .WithErrorCode("GreaterThanOrEqualTo")
            .LessThanOrEqualTo(c => c.PreferredSize ?? (c.MaxSize > 0 ? c.MaxSize : QualityDefinitionLimits.Max))
            .WithErrorCode("LessThanOrEqualTo")
            .When(c => c.MinSize is not null);

        RuleFor(c => c.PreferredSize)
            .GreaterThanOrEqualTo(c => c.MinSize ?? QualityDefinitionLimits.Min)
            .WithErrorCode("GreaterThanOrEqualTo")
            .LessThanOrEqualTo(c => c.MaxSize > 0 ? c.MaxSize : QualityDefinitionLimits.Max)
            .WithErrorCode("LessThanOrEqualTo")
            .When(c => c.PreferredSize is not null);

        RuleFor(c => c.MaxSize)
            .GreaterThanOrEqualTo(c => c.MaxSize == 0 ? 0 : global::System.Math.Max(c.MinSize ?? 0, c.PreferredSize ?? 0))
            .WithErrorCode("GreaterThanOrEqualTo")
            .LessThanOrEqualTo(QualityDefinitionLimits.Max)
            .WithErrorCode("LessThanOrEqualTo")
            .When(c => c.MaxSize is not null);
    }
}
