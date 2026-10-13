using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class CompleteSeriesSearchCriteria : SearchCriteriaBase
    {
        // Interactive searches also show complete season packs. Automatic complete-
        // series acquisition keeps the stricter whole-series coverage requirement.
        public bool IncludeSeasonPacks { get; set; }

        public string[] QueryTitles => CleanSceneTitles.Select(t => t.Replace('+', ' '))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();

        public string[] SeasonPackQueryTitles => IncludeSeasonPacks
            ? (Episodes ?? new List<Episode>()).Where(e => e.SeasonNumber > 0)
                .Select(e => e.SeasonNumber).Distinct().OrderBy(s => s)
                .Select(s => $"{QueryTitles.FirstOrDefault()} S{s:00}").ToArray()
            : Array.Empty<string>();

        // A bare title is ambiguous on text-only indexers that return newest matches first
        // (e.g. "House" pages fill with unrelated episodes). Targeted pack queries reach
        // older complete-series releases; their results still pass the normal parser checks.
        public string[] PackQueryTitles
        {
            get
            {
                var lastSeason = Episodes?.Where(e => e.SeasonNumber > 0).Select(e => e.SeasonNumber).DefaultIfEmpty(0).Max() ?? 0;
                var queries = new List<string>();

                foreach (var title in QueryTitles.Take(3))
                {
                    if (lastSeason > 1)
                    {
                        queries.Add($"{title} S01-S{lastSeason:00}");
                    }

                    queries.Add($"{title} complete");
                }

                return queries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        public override string ToString()
        {
            return $"[{Series.Title}] Complete series";
        }
    }
}
