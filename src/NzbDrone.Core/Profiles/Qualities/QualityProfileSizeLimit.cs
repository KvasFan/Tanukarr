using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Profiles.Qualities;

public class QualityProfileSizeLimit
{
    public Quality Quality { get; set; }
    public double? MinSize { get; set; }
    public double? MaxSize { get; set; }
    public double? PreferredSize { get; set; }
    public bool UpdateMinSize { get; set; } = true;
    public bool UpdateMaxSize { get; set; } = true;
    public bool UpdatePreferredSize { get; set; } = true;
    public bool MinSizeIsMixed { get; set; }
    public bool MaxSizeIsMixed { get; set; }
    public bool PreferredSizeIsMixed { get; set; }

    public QualityProfileSizeLimit()
    {
    }

    public QualityProfileSizeLimit(QualityDefinition qualityDefinition)
    {
        Quality = qualityDefinition.Quality;
        MinSize = qualityDefinition.MinSize;
        MaxSize = qualityDefinition.MaxSize;
        PreferredSize = qualityDefinition.PreferredSize;
    }

    public static IEnumerable<QualityProfileQualityItem> Leaves(IEnumerable<QualityProfileQualityItem> items)
    {
        return items.SelectMany(item => item.Quality != null ? new[] { item } : Leaves(item.Items));
    }

    public static Dictionary<int, QualityProfileSizeLimit> Summarize(IEnumerable<QualityProfile> profiles)
    {
        return profiles.SelectMany(profile => Leaves(profile.Items))
            .GroupBy(item => item.Quality.Id)
            .ToDictionary(group => group.Key, group =>
            {
                var minimum = group.Select(item => item.MinSize).Distinct().ToList();
                var maximum = group.Select(item => item.MaxSize == 0 ? null : item.MaxSize).Distinct().ToList();
                var preferred = group.Select(item => item.PreferredSize).Distinct().ToList();
                return new QualityProfileSizeLimit
                {
                    Quality = group.First().Quality,
                    MinSize = minimum.Count == 1 ? minimum[0] : null,
                    MaxSize = maximum.Count == 1 ? maximum[0] : null,
                    PreferredSize = preferred.Count == 1 ? preferred[0] : null,
                    MinSizeIsMixed = minimum.Count > 1,
                    MaxSizeIsMixed = maximum.Count > 1,
                    PreferredSizeIsMixed = preferred.Count > 1
                };
            });
    }
}
