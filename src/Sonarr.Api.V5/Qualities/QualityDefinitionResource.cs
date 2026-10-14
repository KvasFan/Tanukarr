using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Qualities;

public class QualityDefinitionResource : RestResource
{
    private double? _minSize;
    private double? _maxSize;
    private double? _preferredSize;

    public Quality? Quality { get; set; }
    public string? Title { get; set; }
    public int Weight { get; set; }
    public QualitySource? Source { get; set; }
    public int? Resolution { get; set; }
    public string? Codec { get; set; }
    public string? TitlePattern { get; set; }
    public bool IsRetired { get; set; }
    public bool IsCustom { get; set; }
    public int? CopyFromId { get; set; }
    public double? MinSize
    {
        get => _minSize;
        set
        {
            _minSize = value;
            MinSizeSpecified = true;
        }
    }

    public double? MaxSize
    {
        get => _maxSize;
        set
        {
            _maxSize = value;
            MaxSizeSpecified = true;
        }
    }

    public double? PreferredSize
    {
        get => _preferredSize;
        set
        {
            _preferredSize = value;
            PreferredSizeSpecified = true;
        }
    }

    [Newtonsoft.Json.JsonIgnore]
    [global::System.Text.Json.Serialization.JsonIgnore]
    public bool MinSizeSpecified { get; private set; }
    [Newtonsoft.Json.JsonIgnore]
    [global::System.Text.Json.Serialization.JsonIgnore]
    public bool MaxSizeSpecified { get; private set; }
    [Newtonsoft.Json.JsonIgnore]
    [global::System.Text.Json.Serialization.JsonIgnore]
    public bool PreferredSizeSpecified { get; private set; }
    public bool MinSizeIsMixed { get; set; }
    public bool MaxSizeIsMixed { get; set; }
    public bool PreferredSizeIsMixed { get; set; }
}

public static class QualityDefinitionResourceMapper
{
    public static QualityDefinitionResource ToResource(this QualityDefinition model, QualityProfileSizeLimit? limits = null)
    {
        return new QualityDefinitionResource
        {
            Id = model.Id,
            Quality = model.Quality,
            Title = model.Title,
            Weight = model.Weight,
            Source = model.Source ?? model.Quality.Source,
            Resolution = model.Resolution ?? model.Quality.Resolution,
            Codec = model.Codec,
            TitlePattern = model.TitlePattern,
            IsRetired = model.IsRetired,
            IsCustom = model.Quality.Id >= Quality.CustomIdStart,
            MinSize = limits?.MinSize,
            MaxSize = limits?.MaxSize,
            PreferredSize = limits?.PreferredSize,
            MinSizeIsMixed = limits?.MinSizeIsMixed ?? false,
            MaxSizeIsMixed = limits?.MaxSizeIsMixed ?? false,
            PreferredSizeIsMixed = limits?.PreferredSizeIsMixed ?? false,
        };
    }

    public static QualityDefinition ToModel(this QualityDefinitionResource resource)
    {
        return new QualityDefinition
        {
            Id = resource.Id,
            Quality = resource.Quality,
            Title = resource.Title,
            Weight = resource.Weight,
            Source = resource.Source,
            Resolution = resource.Resolution,
            Codec = resource.Codec,
            TitlePattern = resource.TitlePattern,
            MinSize = resource.MinSize,
            MaxSize = resource.MaxSize,
            PreferredSize = resource.PreferredSize,
        };
    }

    public static List<QualityDefinitionResource> ToResource(this IEnumerable<QualityDefinition> models)
    {
        return models.Select(model => model.ToResource()).ToList();
    }

    public static List<QualityDefinition> ToModel(this IEnumerable<QualityDefinitionResource> resources)
    {
        return resources.Select(ToModel).ToList();
    }
}
