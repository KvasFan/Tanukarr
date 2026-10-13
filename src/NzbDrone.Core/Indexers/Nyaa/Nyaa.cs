using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Nyaa
{
    public class Nyaa : HttpIndexerBase<NyaaSettings>
    {
        public override string Name => "Nyaa";

        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;

        public Nyaa(IHttpClient httpClient, IIndexerStatusService indexerStatusService, IConfigService configService, IParsingService parsingService, Logger logger, ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, configService, parsingService, logger, localizationService)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new NyaaRequestGenerator() { Settings = Settings };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new TorrentRssParser() { UseGuidInfoUrl = true, SizeElementName = "size", InfoHashElementName = "infoHash", PeersElementName = "leechers", CalculatePeersAsSum = true, SeedsElementName = "seeders" };
        }

        public override async Task<IList<ReleaseInfo>> Fetch(CompleteSeriesSearchCriteria criteria)
        {
            // Start with bounded title queries. Only query missing pack coverage:
            // many anime feeds already contain all season batches in one response.
            var reports = await FetchReleases(g => ((NyaaRequestGenerator)g).GetTitleRequests(criteria.QueryTitles));
            var packs = reports.Select(r => CompleteSeriesReleaseParser.Parse(r.Title, criteria)).Where(p => p != null).ToList();
            var supplemental = new List<string>();
            if (!packs.Any(p => p.IsCompleteSeries))
            {
                supplemental.AddRange(criteria.PackQueryTitles);
            }

            if (criteria.IncludeSeasonPacks)
            {
                var singleSeasons = packs.Where(p => p.FullSeason && p.SeasonNumbers.Count == 1)
                    .Select(p => p.SeasonNumbers[0]).ToHashSet();
                var title = criteria.QueryTitles.FirstOrDefault();
                supplemental.AddRange(criteria.Episodes.Where(e => e.SeasonNumber > 0).Select(e => e.SeasonNumber).Distinct()
                    .Where(s => !singleSeasons.Contains(s)).Select(s => $"{title} S{s:00}"));
            }

            supplemental = supplemental.Except(criteria.QueryTitles, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (supplemental.Count > 0)
            {
                reports = reports.Concat(await FetchReleases(g => ((NyaaRequestGenerator)g).GetTitleRequests(supplemental))).ToList();
            }

            return reports.GroupBy(r => r.Guid).Select(g => g.First()).ToList();
        }
    }
}
