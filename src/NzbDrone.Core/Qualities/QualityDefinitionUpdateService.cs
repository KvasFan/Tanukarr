using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Qualities;

public class QualityDefinitionSizeUpdate
{
    public QualityDefinition Definition { get; set; }
    public bool UpdateMinSize { get; set; }
    public bool UpdateMaxSize { get; set; }
    public bool UpdatePreferredSize { get; set; }
}

public interface IQualityDefinitionUpdateService
{
    void UpdateMany(List<QualityDefinitionSizeUpdate> updates);
}

public class QualityDefinitionUpdateService : IQualityDefinitionUpdateService
{
    private readonly IQualityDefinitionService _definitions;
    private readonly IQualityProfileService _profiles;

    public QualityDefinitionUpdateService(IQualityDefinitionService definitions, IQualityProfileService profiles)
    {
        _definitions = definitions;
        _profiles = profiles;
    }

    public void UpdateMany(List<QualityDefinitionSizeUpdate> updates)
    {
        lock (QualityDefinitionService.CatalogueLock)
        {
            ApplyUpdates(updates);
        }
    }

    private void ApplyUpdates(List<QualityDefinitionSizeUpdate> updates)
    {
        if (updates == null)
        {
            throw new ValidationException(new[] { new ValidationFailure("", "A quality definition update array is required.") });
        }

        var existing = _definitions.All().ToDictionary(definition => definition.Id);
        var models = new List<QualityDefinition>();
        var limits = new List<QualityProfileSizeLimit>();
        var errors = new List<ValidationFailure>();
        var ids = new HashSet<int>();

        foreach (var update in updates)
        {
            if (update?.Definition == null)
            {
                errors.Add(new ValidationFailure("", "Quality definition updates must not contain null entries."));
                continue;
            }

            var requested = update.Definition;

            if (!ids.Add(requested.Id) || !existing.TryGetValue(requested.Id, out var current))
            {
                errors.Add(new ValidationFailure("id", "Quality definition IDs must exist and must not be repeated."));
                continue;
            }

            if (requested.Quality != null && requested.Quality.Id != current.Quality.Id)
            {
                errors.Add(new ValidationFailure("quality", "A quality definition's quality cannot be changed."));
            }

            if (requested.Title != null && string.IsNullOrWhiteSpace(requested.Title))
            {
                errors.Add(new ValidationFailure("title", "Quality titles must not be blank."));
            }

            var merged = new QualityDefinition(current.Quality)
            {
                Id = current.Id,
                Title = requested.Title ?? current.Title,
                Weight = current.Weight,
                Source = requested.Source ?? current.Source,
                Resolution = requested.Resolution ?? current.Resolution,
                Codec = requested.Codec ?? current.Codec,
                TitlePattern = requested.TitlePattern ?? current.TitlePattern,
                IsRetired = current.IsRetired
            };
            QualityDefinitionService.ValidateCriteria(merged);
            models.Add(merged);

            if (update.UpdateMinSize || update.UpdateMaxSize || update.UpdatePreferredSize)
            {
                limits.Add(new QualityProfileSizeLimit(requested)
                {
                    Quality = current.Quality,
                    UpdateMinSize = update.UpdateMinSize,
                    UpdateMaxSize = update.UpdateMaxSize,
                    UpdatePreferredSize = update.UpdatePreferredSize
                });
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var titles = existing.Values.ToDictionary(definition => definition.Id, definition => definition.Title);
        models.ForEach(model => titles[model.Id] = model.Title);
        if (titles.Values.GroupBy(title => title, System.StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ValidationException(new[] { new ValidationFailure("title", "Quality names must be unique, including deleted qualities.") });
        }

        if (models.Any(model => existing.Values.Any(other => other.Id != model.Id && string.Equals(other.Title, model.Title, System.StringComparison.OrdinalIgnoreCase))))
        {
            throw new ValidationException(new[] { new ValidationFailure("title", "A quality name is still assigned to another definition. Rename it separately first.") });
        }

        // The profile service validates the complete batch before any profile write.
        // Size fields are persisted in profiles, never in the retired definition columns.
        if (limits.Count > 0)
        {
            _profiles.UpdateAllSizeLimits(limits.ToArray());
        }

        if (models.Count > 0)
        {
            _definitions.UpdateMany(models);
        }
    }
}
