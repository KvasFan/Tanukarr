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
