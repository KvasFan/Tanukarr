using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using Sonarr.Http;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.Config
{
    [V3ApiController("config/metadatasource")]
    public class MetadataSourceConfigController : ConfigController<MetadataSourceConfigResource>
    {
        public MetadataSourceConfigController(IConfigService configService)
            : base(configService)
        {
        }

        [RestPutById]
        [Consumes("application/json")]
        public override ActionResult<MetadataSourceConfigResource> SaveConfig([FromBody] MetadataSourceConfigResource resource)
        {
            MetadataSourcePolicy.Save(_configService, resource.MetadataSourceOrder, resource.DisabledMetadataSources);
            if (resource.TmdbApiKey != null && resource.TmdbApiKey != "********")
            {
                _configService.TmdbApiKey = resource.TmdbApiKey.Trim();
            }

            return Accepted(resource.Id);
        }

        protected override MetadataSourceConfigResource ToResource(IConfigService model)
        {
            return MetadataSourceConfigResourceMapper.ToResource(model);
        }
    }
}
