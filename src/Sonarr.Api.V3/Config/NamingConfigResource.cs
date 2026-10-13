using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Config
{
    public class NamingConfigResource : RestResource
    {
        public bool RenameEpisodes { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public int ColonReplacementFormat { get; set; }
        public string CustomColonReplacementFormat { get; set; }
        public int MultiEpisodeStyle { get; set; }
        public string StandardEpisodeFormat { get; set; }
        public string DailyEpisodeFormat { get; set; }
        public string AnimeEpisodeFormat { get; set; }
        public string SeriesFolderFormat { get; set; }
        public string SeasonFolderFormat { get; set; }
        public string SpecialsFolderFormat { get; set; }
        public string AnimeGroupFolderFormat { get; set; }
        public string AnimeGroupSeasonFolderFormat { get; set; }
        public string AnimeGroupArcFolderFormat { get; set; }
        public string AnimeGroupMovieFolderFormat { get; set; }
        public string AnimeGroupMovieSagaFolderFormat { get; set; }
    }
}
