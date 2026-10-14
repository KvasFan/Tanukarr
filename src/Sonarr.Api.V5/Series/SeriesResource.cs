using System.Text.Json.Serialization;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Tv;
using Sonarr.Http.REST;
using Swashbuckle.AspNetCore.Annotations;

namespace Sonarr.Api.V5.Series;

public class SeriesResource : RestResource
{
    public string? Title { get; set; }
    [SwaggerSchema(ReadOnly = true)]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Include)]
    public string? CollectionTitle { get; set; }
    public List<AlternateTitleResource>? AlternateTitles { get; set; }
    public string? SortTitle { get; set; }
    public SeriesStatusType Status { get; set; }
    public bool Ended => Status == SeriesStatusType.Ended;
    public string? ProfileName { get; set; }
    public string? Overview { get; set; }
    public DateTime? NextAiring { get; set; }
    public DateTime? PreviousAiring { get; set; }
    public string? Network { get; set; }
    public string? AirTime { get; set; }
    public List<MediaCover>? Images { get; set; }
    public Language? Language { get; set; }
    public Language? OriginalLanguage { get; set; }
    public string? RemotePoster { get; set; }
    public string? SeasonType { get; set; }
    public List<SeasonResource> Seasons { get; set; } = [];
    public List<SeasonTypeResource> SeasonTypes { get; set; } = [];
    public List<SeriesTranslationResource> Translations { get; set; } = [];
    public int Year { get; set; }
    public string? Path { get; set; }
    public string? PendingPath { get; set; }
    public int QualityProfileId { get; set; }
    public bool SeasonFolder { get; set; }
    public bool Monitored { get; set; }
    public NewItemMonitorTypes MonitorNewItems { get; set; }
    public bool UseSceneNumbering { get; set; }
    public int Runtime { get; set; }
    public int TvdbId { get; set; }
    public int TvRageId { get; set; }
    public int TvMazeId { get; set; }
    public int TmdbId { get; set; }
    public HashSet<int>? MalIds { get; set; }
    public HashSet<int>? AniListIds { get; set; }

    /// <summary>
    /// Which provider owns this series' metadata.
    /// </summary>
    public MetadataSourceType MetadataSource { get; set; }

    /// <summary>
    /// The owning provider's identifier for this series.
    /// </summary>
    public string? ForeignId { get; set; }

    /// <summary>
    /// The episode ordering chosen when the series was added, such as a TMDB episode group
    /// id. Null uses the provider's default. Set on add only.
    /// </summary>
    public string? OrderingId { get; set; }

    public AnimeGroupingMode? AnimeGroupingMode { get; set; }
    public string? AnimeGroupId { get; set; }
    public string? AnimeGroupTitle { get; set; }
    public int? AnimeGroupSeasonNumber { get; set; }

    /// <summary>
    /// Server-derived folder leaf. Request values are ignored.
    /// </summary>
    [SwaggerSchema(ReadOnly = true)]
    public string? AnimeGroupFolder { get; set; }
    public AnimeGroupMemberKind? AnimeGroupMemberKind { get; set; }
    public AnimeGroupKind? AnimeGroupKind { get; set; }
    public AnimeGroupMovieFolderStyle? AnimeGroupMovieFolderStyle { get; set; }
    public int? AnimeGroupMovieNumber { get; set; }

    /// <summary>
    /// Server-derived member folder leaf. Request values are ignored.
    /// </summary>
    [SwaggerSchema(ReadOnly = true)]
    public string? AnimeGroupMemberFolder { get; set; }

    public DateTime? FirstAired { get; set; }
    public DateTime? LastAired { get; set; }
    public SeriesTypes SeriesType { get; set; }
    public string? OriginalTitle { get; set; }
    public string? CleanTitle { get; set; }
    public string? CleanOriginalTitle { get; set; }
    public string? ImdbId { get; set; }
    public string? TitleSlug { get; set; }
    public string? RootFolderPath { get; set; }
    public string? Folder { get; set; }
    public List<SeriesFolderResource> Folders { get; set; } = [];
    public string? Certification { get; set; }
    public List<string>? Genres { get; set; }
    public string? OriginalCountry { get; set; }
    public HashSet<int>? Tags { get; set; }
    public DateTime Added { get; set; }
    public AddSeriesOptions? AddOptions { get; set; }
    public Ratings? Ratings { get; set; }
    public SeriesStatisticsResource? Statistics { get; set; }
    public bool? EpisodesChanged { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [SwaggerIgnore]
    public bool? IsExcluded { get; set; }
}

public static class SeriesResourceMapper
{
    public static SeriesResource ToResource(this NzbDrone.Core.Tv.Series model, bool includeSeasonImages = false)
    {
        return new SeriesResource
        {
            Id = model.Id,
            Title = model.Title,
            CollectionTitle = model.CollectionTitle,
            SortTitle = model.SortTitle,
            Status = model.Status,
            Overview = model.Overview,
            Network = model.Network,
            AirTime = model.AirTime,
            Images = model.Images.JsonClone(),
            Language = model.Language ?? Language.English,
            SeasonType = model.SeasonType,
            Seasons = model.Seasons.ToResource(includeSeasonImages),
            SeasonTypes = model.SeasonTypes.ToResource(),
            Translations = model.Translations.ToResource(),
            Year = model.Year,
            OriginalCountry = model.OriginalCountry,
            OriginalLanguage = model.OriginalLanguage,
            Path = model.Path,
            PendingPath = model.PendingPath,
            QualityProfileId = model.QualityProfileId,
            SeasonFolder = model.SeasonFolder,
            Monitored = model.Monitored,
            MonitorNewItems = model.MonitorNewItems,
            UseSceneNumbering = model.UseSceneNumbering,
            Runtime = model.Runtime,
            TvdbId = model.TvdbId,
            TvRageId = model.TvRageId,
            TvMazeId = model.TvMazeId,
            TmdbId = model.TmdbId,
            MalIds = model.MalIds,
            AniListIds = model.AniListIds,
            MetadataSource = model.MetadataSource,
            ForeignId = model.ForeignId,
            OrderingId = model.OrderingId,
            AnimeGroupingMode = model.AnimeGroupingMode,
            AnimeGroupId = model.AnimeGroupId,
            AnimeGroupTitle = model.AnimeGroupTitle,
            AnimeGroupSeasonNumber = model.AnimeGroupSeasonNumber,
            AnimeGroupFolder = model.AnimeGroupFolder,
            AnimeGroupMemberKind = model.AnimeGroupMemberKind,
            AnimeGroupKind = model.AnimeGroupKind,
            AnimeGroupMovieFolderStyle = model.AnimeGroupMovieFolderStyle,
            AnimeGroupMovieNumber = model.AnimeGroupMovieNumber,
            AnimeGroupMemberFolder = model.AnimeGroupMemberFolder,
            FirstAired = model.FirstAired,
            LastAired = model.LastAired,
            SeriesType = model.SeriesType,
            OriginalTitle = model.OriginalTitle,
            CleanTitle = model.CleanTitle,
            CleanOriginalTitle = model.CleanOriginalTitle,
            ImdbId = model.ImdbId,
            TitleSlug = model.TitleSlug,
            Certification = model.Certification,
            Genres = model.Genres,
            Tags = model.Tags,
            Added = model.Added,
            AddOptions = model.AddOptions,
            Ratings = model.Ratings
        };
    }

    public static NzbDrone.Core.Tv.Series ToModel(this SeriesResource resource)
    {
        return new NzbDrone.Core.Tv.Series
        {
            Id = resource.Id,
            Title = resource.Title,
            SortTitle = resource.SortTitle,
            Status = resource.Status,
            Overview = resource.Overview,
            Network = resource.Network,
            AirTime = resource.AirTime,
            Images = resource.Images,
            Language = resource.Language,
            SeasonType = resource.SeasonType,
            Seasons = resource.Seasons?.ToModel() ?? [],
            Year = resource.Year,
            OriginalLanguage = resource.OriginalLanguage,
            Path = resource.Path,
            QualityProfileId = resource.QualityProfileId,
            SeasonFolder = resource.SeasonFolder,
            Monitored = resource.Monitored,
            MonitorNewItems = resource.MonitorNewItems,
            UseSceneNumbering = resource.UseSceneNumbering,
            Runtime = resource.Runtime,
            TvdbId = resource.TvdbId,
            TvRageId = resource.TvRageId,
            TvMazeId = resource.TvMazeId,
            TmdbId = resource.TmdbId,
            MalIds = resource.MalIds,
            AniListIds = resource.AniListIds,
            MetadataSource = resource.MetadataSource,
            ForeignId = resource.ForeignId,
            OrderingId = resource.OrderingId,
            AnimeGroupingMode = resource.AnimeGroupingMode ?? AnimeGroupingMode.Automatic,
            AnimeGroupId = resource.AnimeGroupId,
            AnimeGroupTitle = resource.AnimeGroupTitle,
            AnimeGroupSeasonNumber = resource.AnimeGroupSeasonNumber,
            AnimeGroupMemberKind = resource.AnimeGroupMemberKind ?? AnimeGroupMemberKind.Season,
            AnimeGroupKind = resource.AnimeGroupKind ?? AnimeGroupKind.Series,
            AnimeGroupMovieFolderStyle = resource.AnimeGroupMovieFolderStyle ?? AnimeGroupMovieFolderStyle.Automatic,
            AnimeGroupMovieNumber = resource.AnimeGroupMovieNumber,
            FirstAired = resource.FirstAired,
            SeriesType = resource.SeriesType,
            OriginalTitle = resource.OriginalTitle,
            CleanTitle = resource.CleanTitle,
            CleanOriginalTitle = resource.CleanOriginalTitle,
            ImdbId = resource.ImdbId,
            TitleSlug = resource.TitleSlug,
            RootFolderPath = resource.RootFolderPath,
            Certification = resource.Certification,
            Genres = resource.Genres,
            Tags = resource.Tags,
            Added = resource.Added,
            AddOptions = resource.AddOptions,
            Ratings = resource.Ratings
        };
    }

    public static NzbDrone.Core.Tv.Series ToModel(this SeriesResource resource, NzbDrone.Core.Tv.Series series)
    {
        var updatedSeries = resource.ToModel();

        if (resource.AnimeGroupingMode == null)
        {
            updatedSeries.AnimeGroupingMode = series.AnimeGroupingMode;
            updatedSeries.AnimeGroupId = series.AnimeGroupId;
            updatedSeries.AnimeGroupTitle = series.AnimeGroupTitle;
            updatedSeries.AnimeGroupSeasonNumber = series.AnimeGroupSeasonNumber;
            updatedSeries.AnimeGroupMemberKind = series.AnimeGroupMemberKind;
            updatedSeries.AnimeGroupKind = series.AnimeGroupKind;
            updatedSeries.AnimeGroupMovieFolderStyle = series.AnimeGroupMovieFolderStyle;
            updatedSeries.AnimeGroupMovieNumber = series.AnimeGroupMovieNumber;
        }
        else
        {
            updatedSeries.AnimeGroupMemberKind = resource.AnimeGroupMemberKind ?? series.AnimeGroupMemberKind;
            updatedSeries.AnimeGroupKind = resource.AnimeGroupKind ?? series.AnimeGroupKind;
            updatedSeries.AnimeGroupMovieFolderStyle = resource.AnimeGroupMovieFolderStyle ?? series.AnimeGroupMovieFolderStyle;
            if (resource.AnimeGroupMemberKind == null && resource.AnimeGroupMovieNumber == null)
            {
                updatedSeries.AnimeGroupMovieNumber = series.AnimeGroupMovieNumber;
            }
        }

        series.ApplyChanges(updatedSeries);

        return series;
    }

    public static List<SeriesResource> ToResource(this IEnumerable<NzbDrone.Core.Tv.Series> series,
        bool includeSeasonImages = false)
    {
        return series.Select(s => ToResource(s, includeSeasonImages)).ToList();
    }

    public static List<NzbDrone.Core.Tv.Series> ToModel(this IEnumerable<SeriesResource> resources)
    {
        return resources.Select(ToModel).ToList();
    }
}
