using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Qualities
{
    public partial class QualityDefinitionService
    {
        internal static readonly object CatalogueLock = new object();

        public QualityDefinition Add(QualityDefinition definition, int? copyFromId = null)
        {
            lock (CatalogueLock)
            {
                definition.Id = 0;
                ValidateCriteria(definition);
                var all = All();
                var original = copyFromId.HasValue ? all.SingleOrDefault(existing => existing.Id == copyFromId.Value) : null;
                if (copyFromId.HasValue && original == null)
                {
                    throw new ValidationException(new[] { new ValidationFailure("copyFromId", "The quality to copy no longer exists.") });
                }

                if (all.Any(existing => string.Equals(existing.Title, definition.Title, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ValidationException(new[] { new ValidationFailure("title", "A quality with this name already exists.") });
                }

                var nextId = Math.Max(Quality.CustomIdStart, all.Select(existing => existing.Quality.Id).DefaultIfEmpty(0).Max() + 1);
                definition.Id = 0;
                definition.IsRetired = false;
                definition.Quality = new Quality { Id = nextId, Name = definition.Title, Source = definition.Source.Value, Resolution = definition.Resolution.Value };
                var saved = _repo.Insert(definition);
                Configure(saved);
                _cache.Clear();

                var profiles = _profiles.All().ToList();
                foreach (var profile in profiles)
                {
                    var source = original == null ? null : QualityProfileSizeLimit.Leaves(profile.Items).FirstOrDefault(item => item.Quality.Id == original.Quality.Id);
                    profile.Items.Add(new QualityProfileQualityItem
                    {
                        Quality = saved.Quality,
                        Allowed = false,
                        MinSize = source?.MinSize,
                        MaxSize = source?.MaxSize,
                        PreferredSize = source?.PreferredSize
                    });
                }

                _profiles.UpdateMany(profiles);
                profiles.ForEach(_ranks.UpdateRanksForProfile);
                return saved;
            }
        }

        public void Retire(int id)
        {
            lock (CatalogueLock)
            {
                var definition = GetById(id);
                if (definition.Quality.Id == 0)
                {
                    throw new ValidationException(new[] { new ValidationFailure("quality", "The Unknown fallback cannot be deleted.") });
                }

                var profiles = _profiles.All().ToList();
                var used = profiles.Where(profile => profile.Items.Where(item => item.Allowed).SelectMany(item => item.GetQualities())
                    .Any(quality => quality.Id == definition.Quality.Id) || profile.Cutoff == definition.Quality.Id ||
                    profile.Items.Any(item => item.Id == profile.Cutoff && item.GetQualities().Any(quality => quality.Id == definition.Quality.Id))).ToList();
                if (used.Count > 0)
                {
                    throw new ValidationException(new[] { new ValidationFailure("quality", "Disable this quality and change its upgrade cutoff in these profiles before deleting it: " + string.Join(", ", used.Select(profile => profile.Name))) });
                }

                definition.IsRetired = true;
                Update(definition);
            }
        }

        public QualityDefinition Restore(int id)
        {
            lock (CatalogueLock)
            {
                var definition = GetById(id);
                definition.IsRetired = false;
                Update(definition);
                return definition;
            }
        }

        public static void ValidateCriteria(QualityDefinition definition)
        {
            var errors = new List<ValidationFailure>();
            if (string.IsNullOrWhiteSpace(definition.Title) || definition.Title.Length > 100)
            {
                errors.Add(new ValidationFailure("title", "Enter a quality name of 1 to 100 characters."));
            }

            if (definition.Source.HasValue && !Enum.IsDefined(definition.Source.Value))
            {
                errors.Add(new ValidationFailure("source", "Select a supported source."));
            }

            if (definition.Resolution.HasValue && !new[] { 0, 480, 576, 720, 1080, 2160 }.Contains(definition.Resolution.Value))
            {
                errors.Add(new ValidationFailure("resolution", "Select a supported video resolution."));
            }

            if (!string.IsNullOrEmpty(definition.Codec) && !CodecSizeNormalizer.DefaultMultipliers.ContainsKey(definition.Codec))
            {
                errors.Add(new ValidationFailure("codec", "Select a supported codec family."));
            }

            if (!string.IsNullOrEmpty(definition.TitlePattern))
            {
                if (definition.TitlePattern.Length > 256)
                {
                    errors.Add(new ValidationFailure("titlePattern", "The release title expression must not exceed 256 characters."));
                }
                else
                {
                    try
                    {
                        _ = new Regex(definition.TitlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
                    }
                    catch (ArgumentException)
                    {
                        errors.Add(new ValidationFailure("titlePattern", "Enter a valid release title expression."));
                    }
                }
            }

            if (definition.Id == 0 && (!definition.Source.HasValue || !definition.Resolution.HasValue))
            {
                errors.Add(new ValidationFailure("quality", "Source and resolution are required for a new quality."));
            }

            if (errors.Count > 0)
            {
                throw new ValidationException(errors);
            }
        }

        private static void Configure(QualityDefinition definition)
        {
            definition.Quality = new Quality
            {
                Id = definition.Quality.Id,
                Name = definition.Title,
                Source = definition.Source ?? definition.Quality.Source,
                Resolution = definition.Resolution ?? definition.Quality.Resolution,
                IsRetired = definition.IsRetired
            };
            Quality.Register(definition.Quality);
        }
    }
}
