using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities.Commands;

namespace NzbDrone.Core.Qualities
{
    public interface IQualityDefinitionService
    {
        void Update(QualityDefinition qualityDefinition);
        void UpdateMany(List<QualityDefinition> qualityDefinitions);
        List<QualityDefinition> All();
        QualityDefinition GetById(int id);
        QualityDefinition Get(Quality quality);
        QualityDefinition Add(QualityDefinition definition, int? copyFromId = null);
        void Retire(int id);
        QualityDefinition Restore(int id);
    }

    public partial class QualityDefinitionService : IQualityDefinitionService, IExecute<ResetQualityDefinitionsCommand>, IHandle<ApplicationStartedEvent>
    {
        private readonly IQualityDefinitionRepository _repo;
        private readonly ICached<Dictionary<Quality, QualityDefinition>> _cache;
        private readonly Logger _logger;
        private readonly IQualityProfileRepository _profiles;
        private readonly IQualityProfileRankService _ranks;

        public QualityDefinitionService(IQualityDefinitionRepository repo, ICacheManager cacheManager, IQualityProfileRepository profiles, IQualityProfileRankService ranks, Logger logger)
        {
            _repo = repo;
            _cache = cacheManager.GetCache<Dictionary<Quality, QualityDefinition>>(this.GetType());
            _logger = logger;
            _profiles = profiles;
            _ranks = ranks;
        }

        private Dictionary<Quality, QualityDefinition> GetAll()
        {
            return _cache.Get("all", () => _repo.All().Select(WithWeight).ToDictionary(v => v.Quality), TimeSpan.FromSeconds(5.0));
        }

        public void Update(QualityDefinition qualityDefinition)
        {
            _repo.Update(qualityDefinition);
            Configure(qualityDefinition);

            _cache.Clear();
        }

        public void UpdateMany(List<QualityDefinition> qualityDefinitions)
        {
            _repo.UpdateMany(qualityDefinitions);
            qualityDefinitions.ForEach(Configure);
            _cache.Clear();
        }

        public List<QualityDefinition> All()
        {
            return GetAll().Values.OrderBy(d => d.Weight).ToList();
        }

        public QualityDefinition GetById(int id)
        {
            return GetAll().Values.Single(v => v.Id == id);
        }

        public QualityDefinition Get(Quality quality)
        {
            return GetAll()[quality];
        }

        private void InsertMissingDefinitions()
        {
            var insertList = new List<QualityDefinition>();
            var updateList = new List<QualityDefinition>();

            var allDefinitions = Quality.DefaultQualityDefinitions.OrderBy(d => d.Weight).ToList();
            var existingDefinitions = _repo.All().ToList();

            foreach (var definition in allDefinitions)
            {
                var existing = existingDefinitions.SingleOrDefault(d => d.Quality == definition.Quality);

                if (existing == null)
                {
                    insertList.Add(definition);
                }
                else
                {
                    updateList.Add(existing);
                    existingDefinitions.Remove(existing);
                }
            }

            _repo.InsertMany(insertList);
            _repo.UpdateMany(updateList);
            _repo.DeleteMany(existingDefinitions.Where(definition => definition.Quality.Id < Quality.CustomIdStart).ToList());

            _cache.Clear();
        }

        private static QualityDefinition WithWeight(QualityDefinition definition)
        {
            var standard = Quality.DefaultQualityDefinitions.SingleOrDefault(d => d.Quality == definition.Quality);
            definition.Weight = standard?.Weight ?? 19;
            Configure(definition);

            return definition;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _logger.Debug("Setting up default quality config");

            InsertMissingDefinitions();
            All();
        }

        public void Execute(ResetQualityDefinitionsCommand message)
        {
            lock (CatalogueLock)
            {
                ResetDefinitions(message);
            }
        }

        private void ResetDefinitions(ResetQualityDefinitionsCommand message)
        {
            var updateList = new List<QualityDefinition>();

            var allDefinitions = Quality.DefaultQualityDefinitions.OrderBy(d => d.Weight).ToList();
            var existingDefinitions = _repo.All().ToList();

            foreach (var definition in allDefinitions)
            {
                var existing = existingDefinitions.SingleOrDefault(d => d.Quality == definition.Quality);

                existing.Title = message.ResetTitles ? definition.Title : existing.Title;

                updateList.Add(existing);
            }

            _repo.UpdateMany(updateList);

            _cache.Clear();
        }
    }
}
