using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class CompleteSeriesSearchCriteria : SearchCriteriaBase
    {
        public string[] QueryTitles => CleanSceneTitles.Select(t => t.Replace('+', ' ')).ToArray();

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
