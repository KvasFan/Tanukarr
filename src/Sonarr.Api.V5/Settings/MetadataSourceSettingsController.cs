using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using Sonarr.Http;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Settings;

[V5ApiController("settings/metadata")]
[Route("api/v5/settings/metadatasource")]
public class MetadataSourceSettingsController : SettingsController<MetadataSourceSettingsResource>
{
    private readonly IConfigService _configService;

    public MetadataSourceSettingsController(IConfigFileProvider configFileProvider, IConfigService configService)
        : base(configFileProvider, configService)
    {
        _configService = configService;

        SharedValidator.RuleFor(c => c.PreferredMetadataLanguage).Custom((value, context) =>
        {
            if (value.HasValue && !Language.All.Any(o => o.Id == value.Value))
            {
                context.AddFailure("Invalid Metadata Language value");
            }
        });

        SharedValidator.RuleFor(c => c.PreferredMetadataLanguage)
                       .GreaterThanOrEqualTo(1)
                       .WithMessage("The Metadata Language value cannot be less than 1");
    }

    protected override MetadataSourceSettingsResource ToResource(IConfigFileProvider configFile, IConfigService model)
    {
        return MetadataSourceSettingsResourceMapper.ToResource(model);
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public override Results<Accepted<MetadataSourceSettingsResource>, NotFound> SaveSettings([FromBody] MetadataSourceSettingsResource resource)
    {
        MetadataSourcePolicy.Save(_configService, resource.MetadataSourceOrder, resource.DisabledMetadataSources);
        if (resource.OmdbApiKey != null && resource.OmdbApiKey != MetadataSourceSettingsResource.Mask)
        {
            _configService.OmdbApiKey = resource.OmdbApiKey.Trim();
        }

        if (resource.PreferredMetadataLanguage.HasValue)
        {
            _configService.PreferredMetadataLanguage = resource.PreferredMetadataLanguage.Value;
        }

        if (resource.TmdbApiKey != null && resource.TmdbApiKey != MetadataSourceSettingsResource.Mask)
        {
            _configService.TmdbApiKey = resource.TmdbApiKey.IsNullOrWhiteSpace()
                ? string.Empty
                : resource.TmdbApiKey.Trim();
        }

        return TypedAccepted(resource.Id);
    }
}
