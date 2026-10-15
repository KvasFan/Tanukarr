using System.Collections.Generic;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Config
{
    public class MetadataSourceConfigResource : RestResource
    {
        public List<MetadataSourceType> MetadataSourceOrder { get; set; }
        public List<MetadataSourceType> DisabledMetadataSources { get; set; }
        public bool TmdbApiKeyConfigured { get; set; }
        public bool OmdbApiKeyConfigured { get; set; }
        public string OmdbApiKey { get; set; }

        /// <summary>
        /// User-supplied TMDB API key. TMDB requires one per user and does not permit a
        /// shared key to be redistributed, so there is no default.
        /// </summary>
        public string TmdbApiKey { get; set; }
    }

    public static class MetadataSourceConfigResourceMapper
    {
        public static MetadataSourceConfigResource ToResource(IConfigService model)
        {
            return new MetadataSourceConfigResource
            {
                MetadataSourceOrder = MetadataSourcePolicy.Order(model),
                DisabledMetadataSources = MetadataSourcePolicy.Disabled(model),
                TmdbApiKeyConfigured = model.TmdbApiKey.IsNotNullOrWhiteSpace(),
                OmdbApiKeyConfigured = model.OmdbApiKey.IsNotNullOrWhiteSpace(),
                OmdbApiKey = model.OmdbApiKey.IsNotNullOrWhiteSpace() ? "********" : string.Empty,
                TmdbApiKey = model.TmdbApiKey.IsNotNullOrWhiteSpace() ? "********" : string.Empty
            };
        }
    }
}
