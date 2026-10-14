using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.Qualities
{
    [V3ApiController]
    public class QualityDefinitionController :
        RestControllerWithSignalR<QualityDefinitionResource, QualityDefinition>,
        IHandle<CommandExecutedEvent>
    {
        private readonly IQualityDefinitionService _qualityDefinitionService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IQualityDefinitionUpdateService _updateService;

        public QualityDefinitionController(
            IQualityDefinitionService qualityDefinitionService,
            IQualityProfileService qualityProfileService,
            IQualityDefinitionUpdateService updateService,
            IBroadcastSignalRMessage signalRBroadcaster)
            : base(signalRBroadcaster)
        {
            _qualityDefinitionService = qualityDefinitionService;
            _qualityProfileService = qualityProfileService;
            _updateService = updateService;
        }

        [RestPutById]
        public ActionResult<QualityDefinitionResource> Update([FromBody] QualityDefinitionResource resource)
        {
            var model = resource.ToModel();
            _updateService.UpdateMany(new List<QualityDefinitionSizeUpdate> { ToUpdate(resource) });

            return Accepted(model.Id);
        }

        protected override QualityDefinitionResource GetResourceById(int id)
        {
            var model = _qualityDefinitionService.GetById(id);
            var limits = QualityProfileSizeLimit.Summarize(_qualityProfileService.All());
            return model.ToResource(limits.GetValueOrDefault(model.Quality.Id));
        }

        [HttpGet]
        public List<QualityDefinitionResource> GetAll()
        {
            return GetResources();
        }

        [HttpPut("update")]
        public object UpdateMany([FromBody] List<QualityDefinitionResource> resource)
        {
            _updateService.UpdateMany(resource.Select(ToUpdate).ToList());

            return Accepted(GetResources());
        }

        [HttpGet("limits")]
        public ActionResult<QualityDefinitionLimitsResource> GetLimits()
        {
            return Ok(new QualityDefinitionLimitsResource(
                QualityDefinitionLimits.Min,
                QualityDefinitionLimits.Max));
        }

        [HttpGet("codecs")]
        [ProducesResponseType(typeof(Dictionary<string, decimal>), 200)]
        public JsonResult GetCodecs()
        {
            return new JsonResult(CodecSizeNormalizer.MultiplierCatalog, new JsonSerializerOptions { DictionaryKeyPolicy = null });
        }

        [HttpGet("codecdefaults")]
        public JsonResult GetCodecDefaults()
        {
            return new JsonResult(CodecSizeNormalizer.DefaultMultipliers, new JsonSerializerOptions { DictionaryKeyPolicy = null });
        }

        [RestPostById]
        public ActionResult<QualityDefinitionResource> Create([FromBody] QualityDefinitionResource resource)
        {
            var model = _qualityDefinitionService.Add(resource.ToModel(), resource.CopyFromId);
            return Created(model.Id);
        }

        [RestDeleteById]
        public IActionResult DeleteDefinition(int id)
        {
            _qualityDefinitionService.Retire(id);
            return NoContent();
        }

        [HttpPost("{id:int}/restore")]
        public QualityDefinitionResource Restore(int id)
        {
            _qualityDefinitionService.Restore(id);
            return GetResourceById(id);
        }

        private List<QualityDefinitionResource> GetResources()
        {
            var limits = QualityProfileSizeLimit.Summarize(_qualityProfileService.All());
            return _qualityDefinitionService.All()
                .Select(model => model.ToResource(limits.GetValueOrDefault(model.Quality.Id)))
                .ToList();
        }

        private static QualityDefinitionSizeUpdate ToUpdate(QualityDefinitionResource resource)
        {
            return new QualityDefinitionSizeUpdate
            {
                Definition = resource?.ToModel(),
                UpdateMinSize = resource?.MinSizeSpecified == true && !(resource.MinSizeIsMixed && resource.MinSize == null),
                UpdateMaxSize = resource?.MaxSizeSpecified == true && !(resource.MaxSizeIsMixed && resource.MaxSize == null),
                UpdatePreferredSize = resource?.PreferredSizeSpecified == true && !(resource.PreferredSizeIsMixed && resource.PreferredSize == null)
            };
        }

        [NonAction]
        public void Handle(CommandExecutedEvent message)
        {
            if (message.Command.Name == "ResetQualityDefinitions")
            {
                BroadcastResourceChange(ModelAction.Sync);
            }
        }
    }
}
