using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists
{
    internal static class ImportListSetOperations
    {
        public const int MaxPages = 1000;

        public static string[] Sources(IEnumerable<string> modern, params string[] legacy)
        {
            return (modern ?? Array.Empty<string>()).Concat(legacy.Where(value => !string.IsNullOrWhiteSpace(value)))
                .Select(value => value?.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        }

        public static List<ImportListItemInfo> Compose(
            IEnumerable<string> included,
            IEnumerable<string> excluded,
            Func<string, bool, List<ImportListItemInfo>> read)
        {
            var includedSources = Sources(included);
            if (includedSources.Length == 0)
            {
                throw new ImportListException(null, "Select at least one included list.");
            }

            var candidates = includedSources.SelectMany(source => read(source, true)).ToList();
            var candidateEntries = candidates.Select(item => new { Item = item, Keys = Keys(item) }).ToList();
            var excludedEntries = Sources(excluded).SelectMany(source => read(source, false)).Select(Keys).ToList();
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var keys in candidateEntries.Select(entry => entry.Keys).Concat(excludedEntries))
            {
                foreach (var key in keys)
                {
                    aliases.TryAdd(key, key);
                }

                foreach (var key in keys.Skip(1))
                {
                    aliases[FindRoot(aliases, key)] = FindRoot(aliases, keys[0]);
                }
            }

            var excludedKeys = excludedEntries.SelectMany(keys => keys).Select(key => FindRoot(aliases, key)).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<ImportListItemInfo>();
            foreach (var candidate in candidateEntries)
            {
                var identity = FindRoot(aliases, candidate.Keys[0]);
                if (excludedKeys.Contains(identity) || !seen.Add(identity))
                {
                    continue;
                }

                result.Add(candidate.Item);
            }

            return result;
        }

        private static string FindRoot(Dictionary<string, string> aliases, string key)
        {
            var root = key;
            while (aliases[root] != root)
            {
                root = aliases[root];
            }

            while (aliases[key] != key)
            {
                var next = aliases[key];
                aliases[key] = root;
                key = next;
            }

            return root;
        }

        public static ImportListFetchResult Fetch(
            ImportListDefinition definition,
            IImportListStatusService status,
            Logger logger,
            Func<List<ImportListItemInfo>> read,
            bool recordStatus,
            int? limit = null)
        {
            try
            {
                var result = read();
                if (limit.HasValue)
                {
                    result = result.Take(limit.Value).ToList();
                }

                result.ForEach(item =>
                {
                    item.ImportListId = definition.Id;
                    item.ImportList = definition.Name;
                });
                if (recordStatus)
                {
                    status.RecordSuccess(definition.Id);
                }

                return new ImportListFetchResult(result, false);
            }
            catch (Exception exception)
            {
                if (recordStatus)
                {
                    if (exception is TooManyRequestsException limited)
                    {
                        status.RecordFailure(definition.Id, limited.RetryAfter > TimeSpan.Zero ? limited.RetryAfter : TimeSpan.FromHours(1));
                    }
                    else if (exception is WebException web && web.Status is WebExceptionStatus.NameResolutionFailure or WebExceptionStatus.ConnectFailure)
                    {
                        status.RecordConnectionFailure(definition.Id);
                    }
                    else
                    {
                        status.RecordFailure(definition.Id);
                    }
                }

                // Provider errors can contain query-string API keys and private JSON.
                // Report the complete-fetch failure without reproducing those values.
                logger.Warn("A selected import list could not be read completely. Check list access and provider availability; the previous list will be retained.");
                return new ImportListFetchResult(Array.Empty<ImportListItemInfo>(), true);
            }
        }

        public static ValidationFailure TestResult(ImportListFetchResult result)
        {
            if (result.AnyFailure)
            {
                return new ValidationFailure(string.Empty, "Unable to read every included and excluded list completely. Check credentials, list access and provider availability. The previous list will be retained.");
            }

            return result.Series.Count == 0
                ? new NzbDroneValidationFailure(string.Empty, "Connected, but no series match the included lists minus the excluded lists and current filters.") { IsWarning = true }
                : null;
        }

        private static List<string> Keys(ImportListItemInfo item)
        {
            var keys = new List<string>();
            if (item?.TvdbId > 0)
            {
                keys.Add("tv:tvdb:" + item.TvdbId);
            }

            if (item?.TmdbId > 0)
            {
                keys.Add("tv:tmdb:" + item.TmdbId);
            }

            if (item?.ImdbId != null && Regex.IsMatch(item.ImdbId, @"\Att[0-9]+\z", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                keys.Add("tv:imdb:" + item.ImdbId);
            }

            if (keys.Count == 0)
            {
                throw new ImportListException(null, "A list entry has no supported positive series identity. The previous list will be retained.");
            }

            return keys;
        }
    }
}
