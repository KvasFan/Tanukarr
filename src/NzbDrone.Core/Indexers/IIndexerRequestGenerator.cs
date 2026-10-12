using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerRequestGenerator
    {
        // Reuse native indexers' unrestricted text query support. Torznab additionally
        // supports a series ID query and provides its own implementation.
        IndexerPageableRequestChain GetSearchRequests(CompleteSeriesSearchCriteria searchCriteria)
        {
            return GetSearchRequests(new SpecialEpisodeSearchCriteria
            {
                Series = searchCriteria.Series,
                SceneTitles = searchCriteria.SceneTitles,
                Episodes = searchCriteria.Episodes,
                EpisodeQueryTitles = searchCriteria.QueryTitles,
                InteractiveSearch = searchCriteria.InteractiveSearch,
                UserInvokedSearch = searchCriteria.UserInvokedSearch,
                MonitoredEpisodesOnly = searchCriteria.MonitoredEpisodesOnly
            });
        }

        IndexerPageableRequestChain GetRecentRequests();
        IndexerPageableRequestChain GetSearchRequests(SingleEpisodeSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(SeasonSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(DailyEpisodeSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(DailySeasonSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(AnimeEpisodeSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(AnimeSeasonSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(SpecialEpisodeSearchCriteria searchCriteria);
    }
}
