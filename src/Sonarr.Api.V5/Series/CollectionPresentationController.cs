using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V5.Series;

[V5ApiController("collection/presentation")]
public class CollectionPresentationController : Controller
{
    private readonly ISeriesService _seriesService;
    private readonly ISeriesPresentationService _presentationService;
    private readonly IMapCoversToLocal _coverMapper;

    public CollectionPresentationController(ISeriesService seriesService, ISeriesPresentationService presentationService, IMapCoversToLocal coverMapper)
    {
        _seriesService = seriesService;
        _presentationService = presentationService;
        _coverMapper = coverMapper;
    }

    [HttpGet]
    [Produces("application/json")]
    public Results<Ok<SeriesPresentation>, NotFound, BadRequest> Get([FromQuery] string groupId)
    {
        if (!AnimeGroupingService.IsValidGroupId(groupId))
        {
            return TypedResults.BadRequest();
        }

        if (!_seriesService.GetAllSeries().Any(series => AnimeGroupingValidator.HasGroup(series) && series.AnimeGroupId == groupId))
        {
            return TypedResults.NotFound();
        }

        var presentation = _presentationService.GetCollectionPresentation(groupId) ?? new SeriesPresentation();
        _coverMapper.ConvertToLocalUrls(0, presentation.Images);
        return TypedResults.Ok(presentation);
    }
}
