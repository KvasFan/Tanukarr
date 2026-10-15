using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.SeriesStats;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Series;

[V5ApiController("series/lookup")]
public class SeriesLookupController : Controller
{
    private readonly IBuildFileNames _fileNameBuilder;
    private readonly IMapCoversToLocal _coverMapper;
    private readonly IImportListExclusionService _importListExclusionService;
    private readonly IConfigService _configService;
    private readonly IMetadataSearchService _metadataSearch;

    public SeriesLookupController(IBuildFileNames fileNameBuilder, IMapCoversToLocal coverMapper, IImportListExclusionService importListExclusionService, IConfigService configService, IMetadataSearchService metadataSearch)
    {
        _fileNameBuilder = fileNameBuilder;
        _coverMapper = coverMapper;
        _importListExclusionService = importListExclusionService;
        _configService = configService;
        _metadataSearch = metadataSearch;
    }

    /// <summary>
    /// Searches the given metadata source, defaulting to TheTVDB so existing callers are
    /// unaffected. The source chosen here becomes the one that owns any series added from
    /// the results, since a series is owned by exactly one provider.
    /// </summary>
    [HttpGet]
    [Produces("application/json")]
    public Ok<IEnumerable<SeriesResource>> Search([FromQuery] string term, [FromQuery] int? language = null, [FromQuery] MetadataSourceType? metadataSource = MetadataSourceType.Tvdb, [FromQuery] bool allSources = false)
    {
        var languageId = language ?? _configService.PreferredMetadataLanguage;
        var searchLanguage = Language.All.FirstOrDefault(l => l.Id == languageId);

        if (searchLanguage == null || IsoLanguages.Get(searchLanguage) == null)
        {
            throw new BadRequestException($"Invalid language: {languageId}");
        }

        if (!allSources && metadataSource.HasValue && !MetadataSourcePolicy.Enabled(_configService).Contains(metadataSource.Value))
        {
            throw new BadRequestException("The selected metadata source is disabled or unknown.");
        }

        var results = _metadataSearch.Search(term, searchLanguage, allSources ? null : metadataSource);

        return TypedResults.Ok(MapToResource(results));
    }

    private IEnumerable<SeriesResource> MapToResource(IEnumerable<NzbDrone.Core.Tv.Series> series)
    {
        foreach (var currentSeries in series)
        {
            var resource = currentSeries.ToResource();

            _coverMapper.ConvertToLocalUrls(resource.Id, resource.Images, resource.Added);

            var poster = currentSeries.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);

            if (poster != null)
            {
                resource.RemotePoster = poster.RemoteUrl;
            }

            resource.Folder = _fileNameBuilder.GetSeriesFolder(currentSeries);
            resource.Folders = [];
            resource.Statistics = new SeriesStatistics().ToResource(resource.Seasons);
            resource.IsExcluded = _importListExclusionService.FindByTvdbId(currentSeries.TvdbId) is not null;

            foreach (var translation in currentSeries.Translations)
            {
                resource.Folders.Add(new SeriesFolderResource
                {
                    Language = translation.Language,
                    Folder = _fileNameBuilder.GetSeriesFolder(currentSeries, translation.Title)
                });
            }

            yield return resource;
        }
    }
}
