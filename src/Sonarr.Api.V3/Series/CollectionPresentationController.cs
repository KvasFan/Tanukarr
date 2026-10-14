using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Grouping;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V3.Series
{
    [V3ApiController("collection/presentation")]
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
        public ActionResult<SeriesPresentation> Get([FromQuery] string groupId)
        {
            if (!AnimeGroupingService.IsValidGroupId(groupId))
            {
                return BadRequest();
            }

            if (!_seriesService.GetAllSeries().Any(series => AnimeGroupingValidator.HasGroup(series) && series.AnimeGroupId == groupId))
            {
                return NotFound();
            }

            var presentation = _presentationService.GetCollectionPresentation(groupId) ?? new SeriesPresentation();
            _coverMapper.ConvertToLocalUrls(0, presentation.Images);
            return Ok(presentation);
        }
    }
}
