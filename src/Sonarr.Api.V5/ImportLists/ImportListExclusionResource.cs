using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.ImportLists;

public class ImportListExclusionResource : RestResource
{
    public int TvdbId { get; set; }
    public string? Title { get; set; }
    public MetadataSourceType MetadataSource { get; set; }
    public string? ForeignId { get; set; }
}

public static class ImportListExclusionResourceMapper
{
    public static ImportListExclusionResource ToResource(this ImportListExclusion model)
    {
        return new ImportListExclusionResource
        {
            Id = model.Id,
            TvdbId = model.TvdbId,
            Title = model.Title,
            MetadataSource = model.MetadataSource,
            ForeignId = model.ForeignId,
        };
    }

    public static ImportListExclusion ToModel(this ImportListExclusionResource resource)
    {
        return new ImportListExclusion
        {
            Id = resource.Id,
            TvdbId = resource.TvdbId,
            Title = resource.Title,
            MetadataSource = resource.MetadataSource,
            ForeignId = resource.ForeignId
        };
    }

    public static List<ImportListExclusionResource> ToResource(this IEnumerable<ImportListExclusion> models)
    {
        return models.Select(ToResource).ToList();
    }
}
