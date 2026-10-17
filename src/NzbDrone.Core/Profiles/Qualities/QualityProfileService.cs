using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.CustomFormats.Events;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Profiles.Qualities
{
    public interface IQualityProfileService
    {
        QualityProfile Add(QualityProfile profile);
        void Update(QualityProfile profile);
        void Delete(int id);
        List<QualityProfile> All();
        QualityProfile Get(int id);
        bool Exists(int id);
        QualityProfile GetDefaultProfile(string name, Quality cutoff = null, params Quality[] allowed);
        void UpdateAllSizeLimits(params QualityProfileSizeLimit[] sizeLimits);
    }

    public class QualityProfileService : IQualityProfileService,
                                         IHandle<ApplicationStartedEvent>,
                                         IHandle<CustomFormatAddedEvent>,
                                         IHandle<CustomFormatDeletedEvent>
    {
        private readonly IQualityProfileRepository _qualityProfileRepository;
        private readonly IImportListFactory _importListFactory;
        private readonly ICustomFormatService _formatService;
        private readonly ISeriesService _seriesService;
        private readonly IQualityProfileRankService _rankService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly IQualityDefinitionService _definitions;

        public QualityProfileService(IQualityProfileRepository qualityProfileRepository,
                                     IImportListFactory importListFactory,
                                     ICustomFormatService formatService,
                                     ISeriesService seriesService,
                                     IQualityProfileRankService rankService,
                                     IEventAggregator eventAggregator,
                                     IQualityDefinitionService definitions,
                                     Logger logger)
        {
            _qualityProfileRepository = qualityProfileRepository;
            _importListFactory = importListFactory;
            _formatService = formatService;
            _seriesService = seriesService;
            _rankService = rankService;
            _eventAggregator = eventAggregator;
            _logger = logger;
            _definitions = definitions;
        }

        public QualityProfile Add(QualityProfile profile)
        {
            lock (QualityDefinitionService.CatalogueLock)
            {
                return AddProfile(profile);
            }
        }

        private QualityProfile AddProfile(QualityProfile profile)
        {
            profile.CodecMultipliers ??= new CodecMultiplierOverrides();
            ValidateProfile(profile);
            var saved = _qualityProfileRepository.Insert(profile);
            _rankService.UpdateRanksForProfile(saved);
            return saved;
        }

        public void Update(QualityProfile profile)
        {
            lock (QualityDefinitionService.CatalogueLock)
            {
                UpdateProfile(profile);
            }
        }

        private void UpdateProfile(QualityProfile profile)
        {
            var existing = _qualityProfileRepository.Get(profile.Id);
            profile.CodecMultipliers ??= existing?.CodecMultipliers ?? new CodecMultiplierOverrides();
            if (existing?.Items != null && profile.Items != null)
            {
                var requestedIds = QualityProfileSizeLimit.Leaves(profile.Items).Select(item => item.Quality.Id).ToHashSet();
                foreach (var item in QualityProfileSizeLimit.Leaves(existing.Items).Where(item => item.Quality.Id >= Quality.CustomIdStart && !requestedIds.Contains(item.Quality.Id)))
                {
                    profile.Items.Add(item);
                }
            }

            ValidateProfile(profile);
            _qualityProfileRepository.Update(profile);
            _rankService.UpdateRanksForProfile(profile);
            _eventAggregator.PublishEvent(new QualityProfileUpdatedEvent(profile.Id));
        }

        public void Delete(int id)
        {
            if (_seriesService.GetAllSeries().Any(c => c.QualityProfileId == id) || _importListFactory.All().Any(c => c.QualityProfileId == id))
            {
                var profile = _qualityProfileRepository.Get(id);
                throw new QualityProfileInUseException(profile.Name);
            }

            _qualityProfileRepository.Delete(id);
            _rankService.DeleteRanksForProfile(id);
        }

        public List<QualityProfile> All()
        {
            return _qualityProfileRepository.All().Select(Hydrate).ToList();
        }

        public QualityProfile Get(int id)
        {
            return Hydrate(_qualityProfileRepository.Get(id));
        }

        public bool Exists(int id)
        {
            return _qualityProfileRepository.Exists(id);
        }

        public void Handle(ApplicationStartedEvent message)
        {
            var profiles = All();

            if (profiles.Any())
            {
                _rankService.SeedAll(profiles);

                return;
            }

            _logger.Info("Setting up default quality profiles");

            AddDefaultProfile("Any",
                Quality.SDTV,
                Quality.SDTV,
                Quality.WEBRip480p,
                Quality.WEBDL480p,
                Quality.DVD,
                Quality.Bluray480p,
                Quality.Bluray576p,
                Quality.HDTV720p,
                Quality.HDTV1080p,
                Quality.WEBRip720p,
                Quality.WEBDL720p,
                Quality.WEBRip1080p,
                Quality.WEBDL1080p,
                Quality.Bluray720p,
                Quality.Bluray1080p);

            AddDefaultProfile("SD",
                Quality.SDTV,
                Quality.SDTV,
                Quality.WEBRip480p,
                Quality.WEBDL480p,
                Quality.DVD,
                Quality.Bluray480p,
                Quality.Bluray576p);

            AddDefaultProfile("HD-720p",
                Quality.HDTV720p,
                Quality.HDTV720p,
                Quality.WEBRip720p,
                Quality.WEBDL720p,
                Quality.Bluray720p);

            AddDefaultProfile("HD-1080p",
                Quality.HDTV1080p,
                Quality.HDTV1080p,
                Quality.WEBRip1080p,
                Quality.WEBDL1080p,
                Quality.Bluray1080p);

            AddDefaultProfile("Ultra-HD",
                Quality.HDTV2160p,
                Quality.HDTV2160p,
                Quality.WEBRip2160p,
                Quality.WEBDL2160p,
                Quality.Bluray2160p);

            AddDefaultProfile("HD - 720p/1080p",
                Quality.HDTV720p,
                Quality.HDTV720p,
                Quality.HDTV1080p,
                Quality.WEBRip720p,
                Quality.WEBDL720p,
                Quality.WEBRip1080p,
                Quality.WEBDL1080p,
                Quality.Bluray720p,
                Quality.Bluray1080p);
        }

        public void Handle(CustomFormatAddedEvent message)
        {
            var all = All();

            foreach (var profile in all)
            {
                profile.FormatItems.Insert(0, new ProfileFormatItem
                {
                    Score = 0,
                    Format = message.CustomFormat
                });

                Update(profile);
            }
        }

        public void Handle(CustomFormatDeletedEvent message)
        {
            var all = All();
            foreach (var profile in all)
            {
                profile.FormatItems = profile.FormatItems.Where(c => c.Format.Id != message.CustomFormat.Id).ToList();

                if (profile.FormatItems.Empty())
                {
                    profile.MinFormatScore = 0;
                    profile.CutoffFormatScore = 0;
                    profile.MinUpgradeFormatScore = 1;
                }

                Update(profile);
            }
        }

        public QualityProfile GetDefaultProfile(string name, Quality cutoff = null, params Quality[] allowed)
        {
            var groupedQualites = Quality.DefaultQualityDefinitions.GroupBy(q => q.Weight);
            var items = new List<QualityProfileQualityItem>();
            var groupId = 1000;
            var profileCutoff = cutoff == null ? Quality.Unknown.Id : cutoff.Id;

            foreach (var group in groupedQualites)
            {
                if (group.Count() == 1)
                {
                    var quality = group.First().Quality;

                    items.Add(new QualityProfileQualityItem
                    {
                        Quality = group.First().Quality,
                        Allowed = allowed.Contains(quality),
                        MinSize = group.First().MinSize,
                        MaxSize = group.First().MaxSize,
                        PreferredSize = group.First().PreferredSize
                    });
                    continue;
                }

                var groupAllowed = group.Any(g => allowed.Contains(g.Quality));

                items.Add(new QualityProfileQualityItem
                {
                    Id = groupId,
                    Name = group.First().GroupName,
                    Items = group.Select(g => new QualityProfileQualityItem
                    {
                        Quality = g.Quality,
                        Allowed = groupAllowed,
                        MinSize = g.MinSize,
                        MaxSize = g.MaxSize,
                        PreferredSize = g.PreferredSize
                    }).ToList(),
                    Allowed = groupAllowed
                });

                if (group.Any(g => g.Quality.Id == profileCutoff))
                {
                    profileCutoff = groupId;
                }

                groupId++;
            }

            var definitions = _definitions.All();
            foreach (var definition in definitions.Where(definition => definition.Quality.Id >= Quality.CustomIdStart))
            {
                items.Add(new QualityProfileQualityItem { Quality = definition.Quality, Allowed = false });
            }

            foreach (var item in QualityProfileSizeLimit.Leaves(items))
            {
                item.Quality = Quality.FindById(item.Quality.Id);
                if (definitions.Any(definition => definition.Quality.Id == item.Quality.Id && definition.IsRetired))
                {
                    item.Allowed = false;
                }
            }

            var formatItems = _formatService.All().Select(format => new ProfileFormatItem
            {
                Score = 0,
                Format = format
            }).ToList();

            var qualityProfile = new QualityProfile
                                 {
                                     Name = name,
                                     Cutoff = profileCutoff,
                                     Items = items,
                                     MinFormatScore = 0,
                                     CutoffFormatScore = 0,
                                     MinUpgradeFormatScore = 1,
                                     FormatItems = formatItems
                                 };

            return qualityProfile;
        }

        public void UpdateAllSizeLimits(params QualityProfileSizeLimit[] sizeLimits)
        {
            lock (QualityDefinitionService.CatalogueLock)
            {
                ApplySizeLimits(sizeLimits);
            }
        }

        private void ApplySizeLimits(QualityProfileSizeLimit[] sizeLimits)
        {
            if (sizeLimits.Length == 0)
            {
                return;
            }

            var all = All();
            var changes = new List<(QualityProfileQualityItem Item, double? Min, double? Preferred, double? Max)>();
            var errors = new List<ValidationFailure>();

            foreach (var qualityProfile in all)
            {
                foreach (var sizeLimit in sizeLimits)
                {
                    var item = QualityProfileSizeLimit.Leaves(qualityProfile.Items)
                        .SingleOrDefault(candidate => candidate.Quality.Id == sizeLimit.Quality.Id);

                    if (item == null)
                    {
                        errors.Add(new ValidationFailure("quality", $"Quality {sizeLimit.Quality.Name} is missing from profile {qualityProfile.Name}."));
                        continue;
                    }

                    var minimum = sizeLimit.UpdateMinSize ? sizeLimit.MinSize : item.MinSize;
                    var maximum = sizeLimit.UpdateMaxSize ? sizeLimit.MaxSize : item.MaxSize;
                    var preferred = sizeLimit.UpdatePreferredSize ? sizeLimit.PreferredSize : item.PreferredSize;
                    var prefix = $"{qualityProfile.Name}: {item.Quality.Name}";

                    ValidateSize(minimum, "minSize", prefix, errors);
                    ValidateSize(maximum, "maxSize", prefix, errors);
                    ValidateSize(preferred, "preferredSize", prefix, errors);

                    if (minimum.HasValue && preferred.HasValue && minimum > preferred)
                    {
                        errors.Add(new ValidationFailure("minSize", $"{prefix}: minimum must not exceed preferred size."));
                    }

                    if (maximum > 0 && (minimum > maximum || preferred > maximum))
                    {
                        errors.Add(new ValidationFailure("maxSize", $"{prefix}: maximum must not be below minimum or preferred size."));
                    }

                    changes.Add((item, minimum, preferred, maximum));
                }
            }

            if (errors.Count > 0)
            {
                throw new ValidationException(errors);
            }

            // Validate every merged profile before changing any object or writing the batch.
            foreach (var change in changes)
            {
                change.Item.MinSize = change.Min;
                change.Item.PreferredSize = change.Preferred;
                change.Item.MaxSize = change.Max;
            }

            _qualityProfileRepository.UpdateMany(all);

            // One event for the batch: pending releases are re-mapped per event, which
            // costs seconds on a large library and would otherwise repeat per profile.
            _eventAggregator.PublishEvent(new QualityProfilesUpdatedEvent(all.Select(profile => profile.Id).ToList()));
        }

        private static void ValidateSize(double? size, string field, string prefix, List<ValidationFailure> errors)
        {
            if (size.HasValue && (!double.IsFinite(size.Value) || size < QualityDefinitionLimits.Min || size > QualityDefinitionLimits.Max))
            {
                errors.Add(new ValidationFailure(field, $"{prefix}: size must be a finite value between {QualityDefinitionLimits.Min} and {QualityDefinitionLimits.Max} MiB per minute, or null."));
            }
        }

        private QualityProfile AddDefaultProfile(string name, Quality cutoff, params Quality[] allowed)
        {
            var profile = GetDefaultProfile(name, cutoff, allowed);

            return Add(profile);
        }

        private void ValidateProfile(QualityProfile profile)
        {
            if (!CodecSizeNormalizer.ValidOverrides(profile.CodecMultipliers))
            {
                throw new ValidationException(new[] { new ValidationFailure("codecMultipliers", "Codec factors must use supported family names and values from 0.01 to 10.") });
            }

            var definitions = _definitions.All();
            var known = definitions.Select(definition => definition.Quality.Id).ToHashSet();
            if (known.Count > 0 && QualityProfileSizeLimit.Leaves(profile.Items ?? new List<QualityProfileQualityItem>()).Any(item => !known.Contains(item.Quality.Id)))
            {
                throw new ValidationException(new[] { new ValidationFailure("items", "This profile contains an unknown quality. Refresh the quality catalogue.") });
            }

            if (profile.Items?.Any(item => item.Allowed) == true && known.Count > 0 &&
                !profile.Items.Where(item => item.Allowed).SelectMany(item => item.GetQualities())
                    .Any(quality => definitions.Any(definition => definition.Quality.Id == quality.Id && !definition.IsRetired)))
            {
                throw new ValidationException(new[] { new ValidationFailure("items", "Select at least one active quality. Deleted qualities are retained only for existing library records.") });
            }

            foreach (var item in QualityProfileSizeLimit.Leaves(profile.Items ?? new List<QualityProfileQualityItem>()).Where(item => item.Allowed))
            {
                if (definitions.Any(definition => definition.Quality.Id == item.Quality.Id && definition.IsRetired))
                {
                    throw new ValidationException(new[] { new ValidationFailure("items", "A deleted quality cannot be enabled. Restore it in Quality settings first.") });
                }
            }
        }

        private static QualityProfile Hydrate(QualityProfile profile)
        {
            if (profile?.Items != null)
            {
                foreach (var item in QualityProfileSizeLimit.Leaves(profile.Items))
                {
                    item.Quality = Quality.FindById(item.Quality.Id);
                }
            }

            return profile;
        }
    }
}
