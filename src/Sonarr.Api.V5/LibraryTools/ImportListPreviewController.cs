using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Cache;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V5.LibraryTools;

[V5ApiController("librarytools/lists")]
public class ImportListPreviewController : Controller
{
    private readonly IImportListFactory _lists;
    private readonly IAddSeriesService _add;
    private readonly ISeriesService _series;
    private readonly IRootFolderService _roots;
    private readonly IQualityProfileService _profiles;
    private readonly ISearchForNewSeries _lookup;
    private readonly IImportListExclusionService _exclusions;
    private readonly ICached<ListPreview> _cache;

    public ImportListPreviewController(IImportListFactory lists,
        IAddSeriesService add,
        ISeriesService series,
        IRootFolderService roots,
        IQualityProfileService profiles,
        ISearchForNewSeries lookup,
        IImportListExclusionService exclusions,
        ICacheManager cache)
    {
        _lists = lists;
        _add = add;
        _series = series;
        _roots = roots;
        _profiles = profiles;
        _lookup = lookup;
        _exclusions = exclusions;
        _cache = cache.GetCache<ListPreview>(GetType());
    }

    [HttpGet("{id:int}")]
    public object Preview(int id)
    {
        _cache.ClearExpired();
        var preview = _cache.Get(
        id.ToString(CultureInfo.InvariantCulture),
        () =>
        {
            var result = _lists.GetInstance(_lists.Get(id)).Fetch();
            return new ListPreview { Token = Guid.NewGuid().ToString("N"), Items = result.Series, Partial = result.AnyFailure };
        },
        TimeSpan.FromMinutes(10));
        var existing = _series.GetAllSeries();
        var exclusions = _exclusions.All();
        var definition = _lists.Get(id);
        return new
        {
            preview.Token, preview.Partial,
            items = preview.Items.Select((item, index) =>
            {
                var source = ImportListIdentity.GetSource(item);
                var candidate = source.HasValue ? ImportListIdentity.CreateSeries(item, _lookup, definition.Language) : null;

                return new
                {
                    key = index, item.Title, item.Year, item.TvdbId, item.TmdbId, item.AniListId, item.MalId, item.ImdbId,
                    metadataSource = source?.ToString(),
                    foreignId = candidate?.ForeignId,
                    alreadyAdded = existing.Any(s => ImportListIdentity.Matches(s, item)),
                    excluded = candidate != null && exclusions.Any(exclusion => ImportListIdentity.IsExcluded(exclusion, candidate)),
                    supported = source.HasValue || !string.IsNullOrWhiteSpace(item.ImdbId)
                };
            }).ToList()
        };
    }

    [HttpDelete("{id:int}/preview")]
    public IActionResult ClearPreview(int id)
    {
        _cache.Remove(id.ToString(CultureInfo.InvariantCulture));
        return NoContent();
    }

    [HttpPost("{id:int}/add")]
    public IActionResult AddSelected(int id, [FromBody] ListAddRequest request)
    {
        _cache.ClearExpired();
        var preview = _cache.Find(id.ToString(CultureInfo.InvariantCulture));

        if (preview?.Partial == true)
        {
            return BadRequest(new { message = "The list preview is incomplete. Refresh it after the provider succeeds before adding shows." });
        }

        var root = _roots.All().FirstOrDefault(r => r.Id == request.RootFolderId);
        if (preview == null || preview.Token != request.Token || root == null || !_profiles.Exists(request.QualityProfileId) ||
            request.Keys == null || request.Keys.Count is < 1 or > 25 || request.Keys.Any(key => key < 0 || key >= preview.Items.Count))
        {
            return BadRequest(new { message = "Refresh the preview, choose a valid folder and profile, and select up to 25 shows at a time." });
        }

        var results = new List<object>();
        var definition = _lists.Get(id);
        var exclusions = _exclusions.All();
        foreach (var key in request.Keys.Distinct())
        {
            var item = preview.Items[key];
            try
            {
                var existing = _series.GetAllSeries().FirstOrDefault(s => ImportListIdentity.Matches(s, item));
                if (existing != null)
                {
                    results.Add(new { key, title = item.Title, seriesId = existing.Id, status = "Already added" });
                    continue;
                }

                var series = ImportListIdentity.CreateSeries(item, _lookup, definition.Language);

                if (exclusions.Any(exclusion => ImportListIdentity.IsExcluded(exclusion, series)))
                {
                    results.Add(new { key, title = item.Title, status = "Excluded from import lists" });
                    continue;
                }

                series.RootFolderPath = root.Path;
                series.QualityProfileId = request.QualityProfileId;
                series.SeasonFolder = true;
                series.Monitored = request.Monitored;
                series.AddOptions = new AddSeriesOptions { Monitor = request.Monitored ? MonitorTypes.All : MonitorTypes.None, SearchForMissingEpisodes = false, SearchForCutoffUnmetEpisodes = false };
                var added = _add.AddSeries(series);
                results.Add(new { key, title = added.Title, seriesId = added.Id, status = "Added" });
            }
            catch (Exception ex) when (ex is FluentValidation.ValidationException or NzbDrone.Core.Exceptions.SeriesNotFoundException or NzbDrone.Core.MetadataSource.SkyHook.SkyHookException or InvalidOperationException or NzbDrone.Common.Http.HttpException or global::System.Net.WebException)
            {
                results.Add(new { key, title = item.Title, status = "Could not add: check the metadata provider and selected folder/profile." });
            }
        }

        return Ok(results);
    }
}

public class ListPreview
{
    public string Token { get; set; } = string.Empty;
    public List<ImportListItemInfo> Items { get; set; } = [];
    public bool Partial { get; set; }
}

public class ListAddRequest
{
    public string Token { get; set; } = string.Empty;
    public List<int> Keys { get; set; } = [];
    public int RootFolderId { get; set; }
    public int QualityProfileId { get; set; }
    public bool Monitored { get; set; }
}
