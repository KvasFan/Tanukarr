using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Series;

[V5ApiController("series/grouping")]
public class AnimeGroupingController : Controller
{
    private readonly IAnimeGroupingService _groupingService;

    public AnimeGroupingController(IAnimeGroupingService groupingService)
    {
        _groupingService = groupingService;
    }

    [HttpGet]
    public Ok<AnimeGroupingResult> Preview([FromQuery] MetadataSourceType metadataSource, [FromQuery] string foreignId)
    {
        if (!Enum.IsDefined(metadataSource) || !int.TryParse(foreignId, out var id) || id <= 0)
        {
            throw new BadRequestException("A valid metadata source and positive catalogue ID are required");
        }

        return TypedResults.Ok(_groupingService.Resolve(new NzbDrone.Core.Tv.Series
        {
            MetadataSource = metadataSource,
            ForeignId = id.ToString()
        }));
    }
}
