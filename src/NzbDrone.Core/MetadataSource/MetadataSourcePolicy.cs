using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource
{
    public static class MetadataSourcePolicy
    {
        public static List<MetadataSourceType> Order(IConfigService config)
        {
            return Parse(config.MetadataSourceOrder).Concat(Enum.GetValues<MetadataSourceType>()).Distinct().ToList();
        }

        public static List<MetadataSourceType> Disabled(IConfigService config)
        {
            return Parse(config.DisabledMetadataSources).ToList();
        }

        public static List<MetadataSourceType> Enabled(IConfigService config)
        {
            return Order(config).Except(Disabled(config)).ToList();
        }

        public static void Save(IConfigService config, List<MetadataSourceType> order, List<MetadataSourceType> disabled)
        {
            var effectiveOrder = order ?? Order(config);
            var effectiveDisabled = disabled ?? Disabled(config);
            var failures = new List<ValidationFailure>();
            if (effectiveOrder.Count == 0 || effectiveOrder.Distinct().Count() != effectiveOrder.Count || effectiveOrder.Any(source => !Enum.IsDefined(source)))
            {
                failures.Add(new ValidationFailure("MetadataSourceOrder", "Choose each metadata source only once."));
            }

            if (effectiveDisabled.Distinct().Count() != effectiveDisabled.Count || effectiveDisabled.Any(source => !Enum.IsDefined(source)))
            {
                failures.Add(new ValidationFailure("DisabledMetadataSources", "Choose valid metadata sources only once."));
            }

            if (!Enum.GetValues<MetadataSourceType>().Except(effectiveDisabled).Any())
            {
                failures.Add(new ValidationFailure("DisabledMetadataSources", "Enable at least one metadata source."));
            }

            if (failures.Count > 0)
            {
                throw new ValidationException(failures);
            }

            // Older clients can omit a newer source. Append it without changing their order.
            if (order != null)
            {
                config.MetadataSourceOrder = string.Join(",", effectiveOrder.Concat(Enum.GetValues<MetadataSourceType>()).Distinct().Select(source => (int)source));
            }

            if (disabled != null)
            {
                config.DisabledMetadataSources = string.Join(",", effectiveDisabled.Select(source => (int)source));
            }
        }

        private static IEnumerable<MetadataSourceType> Parse(string value)
        {
            return (value ?? string.Empty).Split(',')
                .Where(part => int.TryParse(part, out var id) && Enum.IsDefined((MetadataSourceType)id))
                .Select(part => (MetadataSourceType)int.Parse(part)).Distinct();
        }
    }
}
