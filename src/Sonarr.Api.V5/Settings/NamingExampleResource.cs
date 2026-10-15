using NzbDrone.Core.Organizer;

namespace Sonarr.Api.V5.Settings;

public class NamingExampleResource
{
    public string? MovieExample { get; set; }
    public string? SingleEpisodeExample { get; set; }
    public string? MultiEpisodeExample { get; set; }
    public string? DailyEpisodeExample { get; set; }
    public string? AnimeEpisodeExample { get; set; }
    public string? AnimeMultiEpisodeExample { get; set; }
    public string? SeriesFolderExample { get; set; }
    public string? SeasonFolderExample { get; set; }
    public string? SpecialsFolderExample { get; set; }
    public string? AnimeGroupFolderExample { get; set; }
    public string? AnimeGroupSeasonFolderExample { get; set; }
    public string? AnimeGroupArcFolderExample { get; set; }
    public string? AnimeGroupMovieFolderExample { get; set; }
    public string? AnimeGroupMovieSagaFolderExample { get; set; }
    public string? AnimeGroupMovieEpisodeExample { get; set; }
}

public static class NamingConfigResourceMapper
{
    public static NamingSettingsResource ToResource(this NamingConfig model)
    {
        return new NamingSettingsResource
        {
            Id = model.Id,

            RenameEpisodes = model.RenameEpisodes,
            ReplaceIllegalCharacters = model.ReplaceIllegalCharacters,
            ColonReplacementFormat = (int)model.ColonReplacementFormat,
            CustomColonReplacementFormat = model.CustomColonReplacementFormat,
            MultiEpisodeStyle = (int)model.MultiEpisodeStyle,
            StandardEpisodeFormat = model.StandardEpisodeFormat,
            MovieFormat = model.MovieFormat,
            DailyEpisodeFormat = model.DailyEpisodeFormat,
            AnimeEpisodeFormat = model.AnimeEpisodeFormat,
            SeriesFolderFormat = model.SeriesFolderFormat,
            SeasonFolderFormat = model.SeasonFolderFormat,
            SpecialsFolderFormat = model.SpecialsFolderFormat,
            AnimeGroupFolderFormat = model.AnimeGroupFolderFormat,
            AnimeGroupSeasonFolderFormat = model.AnimeGroupSeasonFolderFormat,
            AnimeGroupArcFolderFormat = model.AnimeGroupArcFolderFormat,
            AnimeGroupMovieFolderFormat = model.AnimeGroupMovieFolderFormat,
            AnimeGroupMovieSagaFolderFormat = model.AnimeGroupMovieSagaFolderFormat
        };
    }

    public static NamingConfig ToModel(this NamingSettingsResource resource, NamingConfig? existing = null)
    {
        return new NamingConfig
        {
            Id = resource.Id,

            RenameEpisodes = resource.RenameEpisodes,
            ReplaceIllegalCharacters = resource.ReplaceIllegalCharacters,
            MultiEpisodeStyle = (MultiEpisodeStyle)resource.MultiEpisodeStyle,
            ColonReplacementFormat = (ColonReplacementFormat)resource.ColonReplacementFormat,
            CustomColonReplacementFormat = resource.CustomColonReplacementFormat ?? "",
            StandardEpisodeFormat = resource.StandardEpisodeFormat,
            MovieFormat = resource.MovieFormat ?? existing?.MovieFormat ?? NamingConfig.Default.MovieFormat,
            DailyEpisodeFormat = resource.DailyEpisodeFormat,
            AnimeEpisodeFormat = resource.AnimeEpisodeFormat,
            SeriesFolderFormat = resource.SeriesFolderFormat,
            SeasonFolderFormat = resource.SeasonFolderFormat,
            SpecialsFolderFormat = resource.SpecialsFolderFormat,
            AnimeGroupFolderFormat = resource.AnimeGroupFolderFormat ?? existing?.AnimeGroupFolderFormat ?? NamingConfig.Default.AnimeGroupFolderFormat,
            AnimeGroupSeasonFolderFormat = resource.AnimeGroupSeasonFolderFormat ?? existing?.AnimeGroupSeasonFolderFormat ?? NamingConfig.Default.AnimeGroupSeasonFolderFormat,
            AnimeGroupArcFolderFormat = resource.AnimeGroupArcFolderFormat ?? existing?.AnimeGroupArcFolderFormat ?? NamingConfig.Default.AnimeGroupArcFolderFormat,
            AnimeGroupMovieFolderFormat = resource.AnimeGroupMovieFolderFormat ?? existing?.AnimeGroupMovieFolderFormat ?? NamingConfig.Default.AnimeGroupMovieFolderFormat,
            AnimeGroupMovieSagaFolderFormat = resource.AnimeGroupMovieSagaFolderFormat ?? existing?.AnimeGroupMovieSagaFolderFormat ?? NamingConfig.Default.AnimeGroupMovieSagaFolderFormat
        };
    }
}
