using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataSearchService
    {
        List<Series> Search(string term, Language language, MetadataSourceType? source);
    }

    public class MetadataSearchService : IMetadataSearchService
    {
        private readonly IConfigService _config;
        private readonly IMetadataProviderFactory _providers;
        private readonly ISearchForNewSeries _tvdb;
        private readonly Logger _logger;

        public MetadataSearchService(IConfigService config, IMetadataProviderFactory providers, ISearchForNewSeries tvdb, Logger logger)
        {
            _config = config;
            _providers = providers;
            _tvdb = tvdb;
            _logger = logger;
        }

        public List<Series> Search(string term, Language language, MetadataSourceType? source)
        {
            var enabled = MetadataSourcePolicy.Enabled(_config);
            if (source.HasValue && !enabled.Contains(source.Value))
            {
                throw new ArgumentException("The selected metadata source is disabled or unknown.", nameof(source));
            }

            var results = new List<Series>();
            Exception lastFailure = null;
            var succeeded = false;
            foreach (var current in source.HasValue ? new List<MetadataSourceType> { source.Value } : enabled)
            {
                try
                {
                    results.AddRange(current == MetadataSourceType.Tvdb
                        ? _tvdb.SearchForNewSeries(term, language)
                        : _providers.GetProvider(current).SearchForNewSeries(term));
                    succeeded = true;
                }
                catch (Exception ex) when (!source.HasValue && (ex is NzbDroneException || ex is HttpException || ex is System.Net.WebException))
                {
                    // A failed optional catalogue must not hide successful native results.
                    _logger.Warn("Metadata search source {0} is unavailable; continuing with enabled sources.", current);
                    lastFailure = ex;
                }
            }

            if (!succeeded && lastFailure != null)
            {
                throw lastFailure;
            }

            return results;
        }
    }
}
